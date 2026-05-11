using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

public class Round51BugFixTests
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public Round51BugFixTests()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round51");
        _collection = db.GetCollection<BsonDocument>("items");
    }

    #region Bug 1: $arrayElemAt out-of-bounds returns null instead of missing

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/arrayElemAt/
    //   "If the idx expression resolves to a value greater than or equal to the array length
    //    or less than the negated array length, $arrayElemAt returns no result."
    //   "No result" = missing, NOT null. In $project, the field should be omitted entirely.
    [Fact]
    public async Task Aggregate_ArrayElemAt_OutOfBounds_ReturnsFieldMissing()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1)
            .Add("arr", new BsonArray { 10, 20, 30 }));

        // Use $addFields which avoids driver-injected projection rendering
        var pipeline = new[]
        {
            BsonDocument.Parse(@"{ $addFields: { result: { $arrayElemAt: ['$arr', 10] } } }"),
            BsonDocument.Parse(@"{ $project: { result: 1, _id: 1 } }")
        };
        var results = await (await _collection.AggregateAsync<BsonDocument>(pipeline)).ToListAsync();
        var doc = results.Single();

        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/arrayElemAt/
        //   "If the idx expression resolves to a value greater than or equal to the array length
        //    or less than the negated array length, $arrayElemAt returns no result."
        //   In $addFields, a "no result" expression means the field is NOT added.
        //   So after $addFields, "result" should not exist, and $project with result:1
        //   on a non-existent field should omit it.
        Assert.False(doc.Contains("result"),
            $"Expected 'result' field to be missing for out-of-bounds $arrayElemAt, but got: {doc}");
    }

    [Fact]
    public async Task Aggregate_ArrayElemAt_NegativeOutOfBounds_ReturnsFieldMissing()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1)
            .Add("arr", new BsonArray { 10, 20, 30 }));

        var pipeline = _collection.Aggregate()
            .Project(BsonDocument.Parse(@"{ result: { $arrayElemAt: ['$arr', -10] } }"));
        var results = await pipeline.ToListAsync();
        var doc = results.Single();

        Assert.False(doc.Contains("result"),
            $"Expected 'result' field to be missing for negative out-of-bounds $arrayElemAt, but got: {doc}");
    }

    [Fact]
    public async Task Aggregate_ArrayElemAt_ValidIndex_ReturnsValue()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1)
            .Add("arr", new BsonArray { 10, 20, 30 }));

        var pipeline = new[]
        {
            BsonDocument.Parse(@"{ $project: { result: { $arrayElemAt: ['$arr', 1] } } }")
        };
        var results = await (await _collection.AggregateAsync<BsonDocument>(pipeline)).ToListAsync();
        var doc = results.Single();

        Assert.True(doc.Contains("result"));
        Assert.Equal(20, doc["result"].AsInt32);
    }

    #endregion

    #region Bug 2: $gte null and $lte null don't match documents with missing fields

    // Ref: https://www.mongodb.com/docs/manual/tutorial/query-for-null-fields/
    //   For comparison operators, missing fields are treated as null.
    //   null >= null is true, so $gte: null should match missing fields.
    [Fact]
    public async Task Filter_GteNull_MatchesMissingField()
    {
        await _collection.InsertManyAsync(new[]
        {
            new BsonDocument("_id", 1).Add("x", 5),
            new BsonDocument("_id", 2).Add("x", BsonNull.Value),
            new BsonDocument("_id", 3) // x is missing
        });

        var filter = Builders<BsonDocument>.Filter.Gte("x", BsonNull.Value);
        var results = await _collection.Find(filter).SortBy(d => d["_id"]).ToListAsync();

        // $gte: null should match: {x: 5} (5 >= null), {x: null} (null >= null), {} (missing treated as null)
        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0]["_id"].AsInt32);
        Assert.Equal(2, results[1]["_id"].AsInt32);
        Assert.Equal(3, results[2]["_id"].AsInt32);
    }

    [Fact]
    public async Task Filter_LteNull_MatchesMissingField()
    {
        await _collection.InsertManyAsync(new[]
        {
            new BsonDocument("_id", 1).Add("x", 5),
            new BsonDocument("_id", 2).Add("x", BsonNull.Value),
            new BsonDocument("_id", 3) // x is missing
        });

        var filter = Builders<BsonDocument>.Filter.Lte("x", BsonNull.Value);
        var results = await _collection.Find(filter).SortBy(d => d["_id"]).ToListAsync();

        // $lte: null should match: {x: null} (null <= null), {} (missing treated as null)
        // {x: 5} should NOT match (5 is not <= null in BSON order)
        Assert.Equal(2, results.Count);
        Assert.Equal(2, results[0]["_id"].AsInt32);
        Assert.Equal(3, results[1]["_id"].AsInt32);
    }

    [Fact]
    public async Task Filter_GtNull_DoesNotMatchMissingField()
    {
        await _collection.InsertManyAsync(new[]
        {
            new BsonDocument("_id", 1).Add("x", 5),
            new BsonDocument("_id", 2).Add("x", BsonNull.Value),
            new BsonDocument("_id", 3) // x is missing
        });

        var filter = Builders<BsonDocument>.Filter.Gt("x", BsonNull.Value);
        var results = await _collection.Find(filter).SortBy(d => d["_id"]).ToListAsync();

        // $gt: null should match: {x: 5} (5 > null in BSON order)
        // Should NOT match {x: null} or {} (null > null = false)
        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    [Fact]
    public async Task Filter_LtNull_MatchesNothing()
    {
        await _collection.InsertManyAsync(new[]
        {
            new BsonDocument("_id", 1).Add("x", 5),
            new BsonDocument("_id", 2).Add("x", BsonNull.Value),
            new BsonDocument("_id", 3) // x is missing
        });

        var filter = Builders<BsonDocument>.Filter.Lt("x", BsonNull.Value);
        var results = await _collection.Find(filter).ToListAsync();

        // $lt: null should match nothing (null < null = false, 5 < null = false)
        Assert.Empty(results);
    }

    #endregion

    #region Bug 3: InsertMany unordered hardcodes error code 11000 for all errors

    // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.insertMany/
    //   "Unordered inserts collect all errors and report them together."
    //   Each BulkWriteError should have the correct error code from the original error.
    [Fact]
    public async Task InsertMany_Unordered_SchemaValidationError_HasCorrectErrorCode()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round51_schema");

        // Create collection with schema validation via RunCommand
        db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "create", "validated" },
            { "validator", new BsonDocument("requiredField", new BsonDocument("$exists", true)) },
            { "validationAction", "error" }
        });

        var col = db.GetCollection<BsonDocument>("validated");

        // Insert one valid document, one invalid document, one valid document
        var docs = new[]
        {
            new BsonDocument("_id", 1).Add("requiredField", "ok"),
            new BsonDocument("_id", 2), // missing requiredField — validation error
            new BsonDocument("_id", 3).Add("requiredField", "also ok")
        };

        var exception = await Assert.ThrowsAsync<MongoBulkWriteException<BsonDocument>>(
            () => col.InsertManyAsync(docs, new InsertManyOptions { IsOrdered = false }));

        // The error for doc 2 should have error code 121 (DocumentValidationFailure), not 11000
        Assert.Single(exception.WriteErrors);
        Assert.Equal(121, exception.WriteErrors[0].Code);
    }

    [Fact]
    public async Task InsertMany_Unordered_SchemaValidationError_ContinuesProcessing()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round51_schema2");

        // Create collection with schema validation via RunCommand
        db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "create", "validated2" },
            { "validator", new BsonDocument("requiredField", new BsonDocument("$exists", true)) },
            { "validationAction", "error" }
        });

        var col = db.GetCollection<BsonDocument>("validated2");

        var docs = new[]
        {
            new BsonDocument("_id", 1).Add("requiredField", "ok"),
            new BsonDocument("_id", 2), // missing requiredField — validation error
            new BsonDocument("_id", 3).Add("requiredField", "also ok")
        };

        var exception = await Assert.ThrowsAsync<MongoBulkWriteException<BsonDocument>>(
            () => col.InsertManyAsync(docs, new InsertManyOptions { IsOrdered = false }));

        // Unordered: doc 1 and doc 3 should have been inserted despite doc 2 failing
        Assert.Equal(2, exception.Result.InsertedCount);
        var allDocs = await col.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.Equal(2, allDocs.Count);
    }

    #endregion
}
