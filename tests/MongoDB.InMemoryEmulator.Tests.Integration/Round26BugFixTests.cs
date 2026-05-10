using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round26BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round26BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Bug 1: Upsert double-apply

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task UpdateOne_Upsert_Inc_AppliedOnce()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.updateOne/
        //   "If upsert: true and no documents match the filter, a new document is created from
        //    the equality conditions in the filter and the update modifications."
        //   $inc should be applied exactly once.
        var col = _fixture.GetCollection<BsonDocument>("upsert_inc_once");

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("name", "Alice"),
            Builders<BsonDocument>.Update.Inc("score", 10),
            new UpdateOptions { IsUpsert = true });

        var doc = await col.Find(Builders<BsonDocument>.Filter.Eq("name", "Alice"))
            .FirstOrDefaultAsync();

        Assert.NotNull(doc);
        Assert.Equal(10, doc["score"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task UpdateOne_Upsert_Push_AppliedOnce()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/push/
        //   $push appends a value to an array. Should be applied once during upsert.
        var col = _fixture.GetCollection<BsonDocument>("upsert_push_once");

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("name", "Bob"),
            Builders<BsonDocument>.Update.Push("tags", "new"),
            new UpdateOptions { IsUpsert = true });

        var doc = await col.Find(Builders<BsonDocument>.Filter.Eq("name", "Bob"))
            .FirstOrDefaultAsync();

        Assert.NotNull(doc);
        var tags = doc["tags"].AsBsonArray;
        Assert.Single(tags);
        Assert.Equal("new", tags[0].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task UpdateMany_Upsert_Inc_AppliedOnce()
    {
        // Same bug for UpdateMany path
        var col = _fixture.GetCollection<BsonDocument>("upsert_many_inc");

        await col.UpdateManyAsync(
            Builders<BsonDocument>.Filter.Eq("name", "Charlie"),
            Builders<BsonDocument>.Update.Inc("counter", 5),
            new UpdateOptions { IsUpsert = true });

        var doc = await col.Find(Builders<BsonDocument>.Filter.Eq("name", "Charlie"))
            .FirstOrDefaultAsync();

        Assert.NotNull(doc);
        Assert.Equal(5, doc["counter"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task FindOneAndUpdate_Upsert_Inc_AppliedOnce()
    {
        // Same bug for FindOneAndUpdate path
        var col = _fixture.GetCollection<BsonDocument>("upsert_findupdate_inc");

        var result = await col.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("name", "Diana"),
            Builders<BsonDocument>.Update.Inc("points", 7),
            new FindOneAndUpdateOptions<BsonDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            });

        Assert.NotNull(result);
        Assert.Equal(7, result["points"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task UpdateOne_Upsert_AddToSet_AppliedOnce()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/addToSet/
        //   $addToSet adds a value to array if not present. Applied twice, value appears once anyway (idempotent)
        //   but array itself should only contain the one value.
        var col = _fixture.GetCollection<BsonDocument>("upsert_addtoset");

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("name", "Eve"),
            Builders<BsonDocument>.Update.AddToSet("roles", "admin"),
            new UpdateOptions { IsUpsert = true });

        var doc = await col.Find(Builders<BsonDocument>.Filter.Eq("name", "Eve"))
            .FirstOrDefaultAsync();

        Assert.NotNull(doc);
        var roles = doc["roles"].AsBsonArray;
        Assert.Single(roles);
    }

    #endregion

    #region Bug 2: $project drops null-valued fields

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Project_IncludesNullValuedField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/project/
        //   "Passes along the documents with the requested fields."
        //   A field with value null should be included in inclusion projection.
        var col = _fixture.GetCollection<BsonDocument>("project_null_field");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "name", BsonNull.Value },
            { "age", 25 }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument { { "name", 1 } })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Single(results);
        Assert.True(results[0].Contains("name"), "Null-valued field 'name' should be present in projection");
        Assert.Equal(BsonNull.Value, results[0]["name"]);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Project_ExcludesMissingField()
    {
        // A field that is truly missing (not in the document) should NOT appear.
        var col = _fixture.GetCollection<BsonDocument>("project_missing_field");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "age", 25 }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument { { "name", 1 } })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Single(results);
        Assert.False(results[0].Contains("name"), "Missing field should not appear in projection");
    }

    #endregion

    #region Bug 3: $lookup array-to-array matching

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Lookup_ArrayToArray_SharedElements_Match()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/lookup/
        //   When both localField and foreignField are arrays, documents match
        //   if the arrays share any common element.
        var orders = _fixture.GetCollection<BsonDocument>("lookup_arr_orders");
        var products = _fixture.GetCollection<BsonDocument>("lookup_arr_products");

        await orders.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "productTags", new BsonArray { "electronics", "sale" } }
        });

        await products.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "A" }, { "tags", new BsonArray { "electronics", "premium" } } },
            new BsonDocument { { "_id", "B" }, { "tags", new BsonArray { "clothing", "sale" } } },
            new BsonDocument { { "_id", "C" }, { "tags", new BsonArray { "food", "organic" } } }
        });

        var pipeline = new BsonDocument[]
        {
            new("$lookup", new BsonDocument
            {
                { "from", "lookup_arr_products" },
                { "localField", "productTags" },
                { "foreignField", "tags" },
                { "as", "matchedProducts" }
            })
        };

        var results = await orders.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Single(results);
        var matched = results[0]["matchedProducts"].AsBsonArray;
        // Should match "A" (shares "electronics") and "B" (shares "sale"), but NOT "C"
        Assert.Equal(2, matched.Count);
        var matchedIds = matched.Select(m => m.AsBsonDocument["_id"].AsString).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "A", "B" }, matchedIds);
    }

    #endregion

    #region Bug 4: ReplaceOne modifiedCount when identical

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ReplaceOne_IdenticalDocument_ModifiedCountIsZero()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.replaceOne/
        //   "modifiedCount: 0 if the replacement document is the same as the document it replaced."
        var col = _fixture.GetCollection<BsonDocument>("replace_identical");
        var doc = new BsonDocument { { "_id", 1 }, { "name", "Alice" }, { "age", 30 } };
        await col.InsertOneAsync(doc);

        var replacement = new BsonDocument { { "_id", 1 }, { "name", "Alice" }, { "age", 30 } };
        var result = await col.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1), replacement);

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(0, result.ModifiedCount);
    }

    #endregion
}
