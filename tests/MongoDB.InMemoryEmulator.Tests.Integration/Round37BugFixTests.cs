using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 37: RenameCollection DropTarget, ReplaceOne upsert _id from filter
/// </summary>
public class Round37BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection(string name = "items")
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>(name);
    }

    private static IMongoDatabase CreateDatabase()
    {
        var client = new InMemoryMongoClient();
        return client.GetDatabase("testdb");
    }

    #region RenameCollection with DropTarget

    [Fact]
    public void RenameCollection_DropTarget_True_DropsTargetFirst()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/renameCollection/
        //   "dropTarget: If true, mongod will drop the target of renameCollection prior to
        //    renaming the collection."
        var db = CreateDatabase();
        var source = db.GetCollection<BsonDocument>("source");
        var target = db.GetCollection<BsonDocument>("target");

        source.InsertOne(new BsonDocument { { "_id", 1 }, { "from", "source" } });
        target.InsertOne(new BsonDocument { { "_id", 2 }, { "from", "target" } });

        db.RenameCollection("source", "target", new RenameCollectionOptions { DropTarget = true });

        var resultCol = db.GetCollection<BsonDocument>("target");
        var docs = resultCol.Find(FilterDefinition<BsonDocument>.Empty).ToList();
        Assert.Single(docs);
        Assert.Equal("source", docs[0]["from"].AsString);

        // Source should no longer exist
        var names = db.ListCollectionNames().ToList();
        Assert.DoesNotContain("source", names);
        Assert.Contains("target", names);
    }

    [Fact]
    public void RenameCollection_DropTarget_False_ThrowsIfTargetExists()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/renameCollection/
        //   "Returns an error if target namespace already exists."
        var db = CreateDatabase();
        var source = db.GetCollection<BsonDocument>("source");
        var target = db.GetCollection<BsonDocument>("target");

        source.InsertOne(new BsonDocument { { "_id", 1 }, { "x", 1 } });
        target.InsertOne(new BsonDocument { { "_id", 2 }, { "y", 2 } });

        Assert.Throws<MongoCommandException>(() =>
            db.RenameCollection("source", "target", new RenameCollectionOptions { DropTarget = false }));
    }

    #endregion

    #region ReplaceOne upsert extracts _id from filter

    [Fact]
    public void ReplaceOne_Upsert_UsesIdFromFilter()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.replaceOne/
        //   "If upsert: true and no documents match the filter, then replaceOne creates a new
        //    document based on the replacement document."
        //   The _id from the filter is used for the new document.
        var col = CreateCollection();

        var filter = Builders<BsonDocument>.Filter.Eq("_id", "my-custom-id");
        var replacement = new BsonDocument { { "name", "test" }, { "value", 42 } };

        var result = col.ReplaceOne(filter, replacement, new ReplaceOptions { IsUpsert = true });

        Assert.Equal(0, result.MatchedCount);
        Assert.Equal((BsonValue)"my-custom-id", result.UpsertedId);

        var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", "my-custom-id")).FirstOrDefault();
        Assert.NotNull(doc);
        Assert.Equal("test", doc["name"].AsString);
        Assert.Equal(42, doc["value"].AsInt32);
    }

    [Fact]
    public void ReplaceOne_Upsert_ReplacementWithExplicitId_UsesReplacementId()
    {
        // When the replacement already contains _id, use that value
        var col = CreateCollection();

        var filter = Builders<BsonDocument>.Filter.Eq("status", "active");
        var replacement = new BsonDocument { { "_id", "explicit-id" }, { "name", "test" } };

        var result = col.ReplaceOne(filter, replacement, new ReplaceOptions { IsUpsert = true });

        Assert.Equal((BsonValue)"explicit-id", result.UpsertedId);
        var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", "explicit-id")).FirstOrDefault();
        Assert.NotNull(doc);
        Assert.Equal("test", doc["name"].AsString);
    }

    [Fact]
    public void ReplaceOne_Upsert_NoIdInFilterOrReplacement_GeneratesObjectId()
    {
        // When neither filter nor replacement provides _id, auto-generate
        var col = CreateCollection();

        var filter = Builders<BsonDocument>.Filter.Eq("status", "active");
        var replacement = new BsonDocument { { "name", "test" } };

        var result = col.ReplaceOne(filter, replacement, new ReplaceOptions { IsUpsert = true });

        Assert.NotNull(result.UpsertedId);
        Assert.IsType<BsonObjectId>(result.UpsertedId);
    }

    [Fact]
    public void FindOneAndReplace_Upsert_UsesIdFromFilter()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.findOneAndReplace/
        //   Same upsert semantics as replaceOne
        var col = CreateCollection();

        var filter = Builders<BsonDocument>.Filter.Eq("_id", "filter-id");
        var replacement = new BsonDocument { { "name", "replaced" }, { "score", 100 } };

        var result = col.FindOneAndReplace(filter, replacement,
            new FindOneAndReplaceOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After });

        Assert.NotNull(result);
        Assert.Equal("filter-id", result["_id"].AsString);
        Assert.Equal("replaced", result["name"].AsString);
        Assert.Equal(100, result["score"].AsInt32);
    }

    [Fact]
    public void ReplaceOne_Upsert_FilterWithAndContainingId_UsesId()
    {
        // When filter uses $and with _id equality, should still extract _id
        var col = CreateCollection();

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", "and-id"),
            Builders<BsonDocument>.Filter.Eq("type", "foo"));
        var replacement = new BsonDocument { { "name", "test" } };

        var result = col.ReplaceOne(filter, replacement, new ReplaceOptions { IsUpsert = true });

        Assert.Equal((BsonValue)"and-id", result.UpsertedId);
        var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", "and-id")).FirstOrDefault();
        Assert.NotNull(doc);
    }

    #endregion

    #region $mod filter truncates decimal divisor/remainder

    [Fact]
    public void Mod_FloatingPointDivisor_TruncatesTowardsZero()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/mod/
        //   "Floating Point Arguments: The $mod expression rounds decimal input towards zero."
        //   "$mod: [4.5, 0]" → treated as "$mod: [4, 0]"
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "qty", 0 } },
            new BsonDocument { { "_id", 2 }, { "qty", 5 } },
            new BsonDocument { { "_id", 3 }, { "qty", 12 } }
        });

        var filter = new BsonDocument("qty", new BsonDocument("$mod", new BsonArray { 4.5, 0 }));
        var results = col.Find(filter).ToList();

        // 4.5 truncated to 4: 0%4==0 ✓, 5%4==1 ✗, 12%4==0 ✓
        Assert.Equal(2, results.Count);
        Assert.Contains(results, d => d["_id"] == 1);
        Assert.Contains(results, d => d["_id"] == 3);
    }

    [Fact]
    public void Mod_FloatingPointRemainder_TruncatesTowardsZero()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/mod/
        //   "Floating Point Arguments: The $mod expression rounds decimal input towards zero."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "qty", 0 } },
            new BsonDocument { { "_id", 2 }, { "qty", 5 } },
            new BsonDocument { { "_id", 3 }, { "qty", 12 } }
        });

        // $mod: [4, 1.9] → treated as $mod: [4, 1]
        var filter = new BsonDocument("qty", new BsonDocument("$mod", new BsonArray { 4, 1.9 }));
        var results = col.Find(filter).ToList();

        // 0%4==0 ✗, 5%4==1 ✓, 12%4==0 ✗
        Assert.Single(results);
        Assert.Equal(2, results[0]["_id"].AsInt32);
    }

    [Fact]
    public void Mod_NegativeDividend_ReturnsNegativeRemainder()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/mod/
        //   "When the dividend is negative, the remainder is also negative."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "qty", -5 } },
            new BsonDocument { { "_id", 2 }, { "qty", 5 } },
            new BsonDocument { { "_id", 3 }, { "qty", -8 } }
        });

        // -5 % 3 = -2 (C# truncated division), 5 % 3 = 2, -8 % 3 = -2
        var filter = Builders<BsonDocument>.Filter.Mod("qty", 3, -2);
        var results = col.Find(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, d => d["_id"] == 1);
        Assert.Contains(results, d => d["_id"] == 3);
    }

    #endregion
}
