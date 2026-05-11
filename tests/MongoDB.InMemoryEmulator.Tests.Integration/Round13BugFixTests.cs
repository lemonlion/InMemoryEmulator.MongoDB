using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 13 tests covering:
/// 1. $not with missing fields (returns wrong result for $lt/$gt/$exists on absent fields)
/// 2. CreateUpsertDocumentFromFilter missing $and support and array/doc equality
/// 3. $graphLookup duplicate result documents
/// 4. $push modifier validation without $each
/// </summary>
[Collection("Integration")]
public class Round13BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round13BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $not with missing fields

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Not_LtOnMissingField_ShouldMatch()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/not/
        //   "$not: Performs a logical NOT operation on the specified <operator-expression>
        //    and selects the documents that do NOT match."
        // A document without the field should match { x: { $not: { $lt: 5 } } }
        // because $lt on a missing field returns false, and $not inverts it to true.
        var col = _fixture.GetCollection<BsonDocument>("not_missing");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 3 } },    // x < 5 is true, $not → false
            new BsonDocument { { "_id", 2 }, { "x", 10 } },   // x < 5 is false, $not → true
            new BsonDocument { { "_id", 3 } },                  // x missing → $lt false → $not → true
        });

        var filter = Builders<BsonDocument>.Filter.Not(
            Builders<BsonDocument>.Filter.Lt("x", 5));
        var results = await col.Find(filter).ToListAsync();
        var ids = results.Select(d => d["_id"].AsInt32).OrderBy(x => x).ToList();

        Assert.Equal(new[] { 2, 3 }, ids);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Not_ExistsTrue_MatchesMissingField()
    {
        // { x: { $not: { $exists: true } } } is equivalent to { x: { $exists: false } }
        var col = _fixture.GetCollection<BsonDocument>("not_exists");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 10 } },
            new BsonDocument { { "_id", 2 } },
        });

        var filter = Builders<BsonDocument>.Filter.Not(
            Builders<BsonDocument>.Filter.Exists("x"));
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(2, results[0]["_id"].AsInt32);
    }

    #endregion

    #region Upsert with $and filter

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Upsert_AndFilter_ExtractsAllEqualityConditions()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.updateOne/
        //   "If a filter matches no documents, upsert creates a new document from the filter
        //    equality conditions plus the update operations."
        var col = _fixture.GetCollection<BsonDocument>("upsert_and");

        // Use $and explicitly in filter
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("type", "widget"),
            Builders<BsonDocument>.Filter.Eq("color", "blue"));

        var update = Builders<BsonDocument>.Update.Set("qty", 5);
        await col.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });

        var result = await col.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync();
        Assert.Equal("widget", result["type"].AsString);
        Assert.Equal("blue", result["color"].AsString);
        Assert.Equal(5, result["qty"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Upsert_DocumentEqualityInFilter_IncludedInUpsertDoc()
    {
        // Document/array equality in filter should be included in the upserted document
        var col = _fixture.GetCollection<BsonDocument>("upsert_doc_eq");

        var filter = Builders<BsonDocument>.Filter.Eq("nested",
            new BsonDocument { { "a", 1 }, { "b", 2 } });

        var update = Builders<BsonDocument>.Update.Set("status", "new");
        await col.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });

        var result = await col.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync();
        Assert.Equal(new BsonDocument { { "a", 1 }, { "b", 2 } }, result["nested"].AsBsonDocument);
        Assert.Equal("new", result["status"].AsString);
    }

    #endregion

    #region $graphLookup deduplication

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task GraphLookup_DeduplicatesResults()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/graphLookup/
        //   "For each matching document, $graphLookup takes the value of connectFromField
        //    and checks every document in the from collection for a matching connectToField."
        // A document that matches via multiple frontier values should appear only once.
        var col = _fixture.GetCollection<BsonDocument>("gl_dedup");
        var foreignCol = _fixture.GetCollection<BsonDocument>("gl_dedup_foreign");

        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "start", new BsonArray { "A", "B" } }
        });

        // This foreign doc matches both "A" and "B" via connectToField
        await foreignCol.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "f1" }, { "code", "A" }, { "next", "C" } },
            new BsonDocument { { "_id", "f2" }, { "code", "B" }, { "next", "C" } },
            new BsonDocument { { "_id", "f3" }, { "code", "C" }, { "next", BsonNull.Value } },
        });

        var pipeline = new BsonDocument[]
        {
            new BsonDocument("$graphLookup", new BsonDocument
            {
                { "from", "gl_dedup_foreign" },
                { "startWith", "$start" },
                { "connectFromField", "next" },
                { "connectToField", "code" },
                { "as", "connections" }
            })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Single(results);

        var connections = results[0]["connections"].AsBsonArray;
        // f1 matches A, f2 matches B, f3 matches C (from both f1 and f2's "next")
        // f3 should appear only once
        var ids = connections.Select(c => c["_id"].AsString).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "f1", "f2", "f3" }, ids);
    }

    #endregion

    #region $push modifier validation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Push_SortWithoutEach_PushesLiteralDocument()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/sort/
        //   Real MongoDB 7.0: $push with $sort but without $each pushes the modifier
        //   document as a literal value (e.g., {"$sort": 1}) — no error thrown.
        var col = _fixture.GetCollection<BsonDocument>("push_sort_no_each");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "arr", new BsonArray { 3, 1, 2 } }
        });

        var update = new BsonDocument("$push", new BsonDocument("arr",
            new BsonDocument("$sort", 1)));

        // Real MongoDB pushes {"$sort": 1} as a literal document value
        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocumentUpdateDefinition<BsonDocument>(update));

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var arr = result["arr"].AsBsonArray;
        Assert.Equal(4, arr.Count);
        Assert.Equal(new BsonDocument("$sort", 1), arr[3].AsBsonDocument);
    }

    #endregion
}
