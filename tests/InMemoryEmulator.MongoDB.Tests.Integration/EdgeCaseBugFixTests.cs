using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Tests for additional edge cases and bug fixes.
/// </summary>
[Collection("Integration")]
public class EdgeCaseBugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public EdgeCaseBugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $in with Regex

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task In_WithRegex_MatchesDocumentsByPattern()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/in/
        //   "The $in operator can select documents using regular expressions."
        var col = _fixture.GetCollection<BsonDocument>("in_regex_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "name", "hello" } },
            new BsonDocument { { "_id", 2 }, { "name", "world" } },
            new BsonDocument { { "_id", 3 }, { "name", "height" } }
        });

        // $in with a regex: matches either exact "world" or starts with "he"
        var filter = new BsonDocument("name", new BsonDocument("$in",
            new BsonArray { new BsonRegularExpression("^he"), "world" }));
        var results = await col.Find(filter).ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r["_id"] == 1); // "hello" starts with "he"
        Assert.Contains(results, r => r["_id"] == 2); // "world" equals "world"
        Assert.Contains(results, r => r["_id"] == 3); // "height" starts with "he"
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task In_WithRegex_NoMatchWhenPatternDoesntMatch()
    {
        var col = _fixture.GetCollection<BsonDocument>("in_regex_2");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "name", "apple" } },
            new BsonDocument { { "_id", 2 }, { "name", "banana" } }
        });

        var filter = new BsonDocument("name", new BsonDocument("$in",
            new BsonArray { new BsonRegularExpression("^xyz") }));
        var results = await col.Find(filter).ToListAsync();

        Assert.Empty(results);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task In_WithRegex_OnArrayField_MatchesIfAnyElementMatches()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/in/
        var col = _fixture.GetCollection<BsonDocument>("in_regex_arr_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "tags", new BsonArray { "alpha", "beta" } } },
            new BsonDocument { { "_id", 2 }, { "tags", new BsonArray { "gamma", "delta" } } }
        });

        var filter = new BsonDocument("tags", new BsonDocument("$in",
            new BsonArray { new BsonRegularExpression("^al") }));
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    #endregion

    #region RenameCollection Atomicity

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task RenameCollection_ToExistingName_ThrowsAndPreservesSource()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/renameCollection/
        //   "Returns an error if target namespace already exists."
        var db = _fixture.Database;
        var sourceCol = db.GetCollection<BsonDocument>("rename_source");
        var targetCol = db.GetCollection<BsonDocument>("rename_target");

        await sourceCol.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "value", "source" } });
        await targetCol.InsertOneAsync(new BsonDocument { { "_id", 2 }, { "value", "target" } });

        // Renaming to existing collection should fail
        var ex = Assert.Throws<MongoCommandException>(
            () => db.RenameCollection("rename_source", "rename_target"));

        // Source collection should still exist with its data intact.
        // Get a FRESH reference to verify the store is still in the database's dictionary.
        var freshSourceCol = db.GetCollection<BsonDocument>("rename_source");
        var sourceData = await freshSourceCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.Single(sourceData);
        Assert.Equal(1, sourceData[0]["_id"].AsInt32);
    }

    #endregion

    #region $substr Negative Start

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Substr_NegativeStart_ThrowsError()
    {
        // Ref: Observed real MongoDB 7.0:
        //   "$substrBytes: starting index must be non-negative (got: -3)"
        var col = _fixture.GetCollection<BsonDocument>("substr_neg_1");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "text", "hello" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$substr", new BsonArray { "$text", -3, 2 })))
        };

        await Assert.ThrowsAnyAsync<MongoCommandException>(async () =>
            await col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Substr_StartBeyondLength_ReturnsEmptyString()
    {
        var col = _fixture.GetCollection<BsonDocument>("substr_beyond_1");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "text", "hi" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$substr", new BsonArray { "$text", 10, 2 })))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Single(results);
        Assert.Equal("", results[0]["result"].AsString);
    }

    #endregion

    #region $nin with Regex

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Nin_WithRegex_ExcludesMatchingDocuments()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/nin/
        //   "$nin uses regex like $in does."
        var col = _fixture.GetCollection<BsonDocument>("nin_regex_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "name", "hello" } },
            new BsonDocument { { "_id", 2 }, { "name", "world" } },
            new BsonDocument { { "_id", 3 }, { "name", "height" } }
        });

        var filter = new BsonDocument("name", new BsonDocument("$nin",
            new BsonArray { new BsonRegularExpression("^he") }));
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(2, results[0]["_id"].AsInt32); // "world" doesn't match ^he
    }

    #endregion

    #region Comparison operators with null operands

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Gt_WithNullOperand_ReturnsNoResults()
    {
        // Ref: Observed real MongoDB 7.0:
        //   {$gt: null} returns NO documents — nothing is "greater than" null in BSON comparison.
        var col = _fixture.GetCollection<BsonDocument>("cmp_null_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", 10 } },
            new BsonDocument { { "_id", 2 }, { "val", BsonNull.Value } },
            new BsonDocument { { "_id", 3 } } // val is missing
        });

        // $gt: null should match NO documents in real MongoDB
        var filter = Builders<BsonDocument>.Filter.Gt("val", BsonNull.Value);
        var results = await col.Find(filter).ToListAsync();

        Assert.Empty(results);
    }

    #endregion
}
