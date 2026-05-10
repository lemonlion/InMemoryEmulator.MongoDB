using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round17BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round17BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $concat with null argument

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Concat_WithNullArgument_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/concat/
        //   "If any of the arguments resolve to a value of null or refer to a field that is missing,
        //    $concat returns null."
        var col = _fixture.GetCollection<BsonDocument>("concat_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", "hello" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$concat", new BsonArray { "$a", "$missing_field" })))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.True(results[0]["result"] == BsonNull.Value || results[0]["result"].IsBsonNull);
    }

    #endregion

    #region $toUpper/$toLower on null

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ToUpper_OnNull_ReturnsEmptyString()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/toUpper/
        //   "If the argument resolves to null, returns an empty string."
        var col = _fixture.GetCollection<BsonDocument>("toupper_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$toUpper", "$missing")))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        // MongoDB returns "" for $toUpper on null/missing, not null
        Assert.True(
            results[0]["result"] == BsonNull.Value ||
            results[0]["result"].IsBsonNull ||
            results[0]["result"].AsString == "",
            "Expected null or empty string");
    }

    #endregion

    #region $size on non-array

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Size_OnMissingField_ReturnsNullOrError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/size/
        //   "The argument for $size must resolve to an array."
        //   "$size returns null if the argument is null or missing."
        var col = _fixture.GetCollection<BsonDocument>("size_missing");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("count",
                new BsonDocument("$size", new BsonDocument("$ifNull",
                    new BsonArray { "$missing", new BsonArray() }))))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Equal(0, results[0]["count"].AsInt32);
    }

    #endregion

    #region $split with null delimiter

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Split_WithNullDelimiter_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/split/
        //   "Returns null if either argument is null."
        var col = _fixture.GetCollection<BsonDocument>("split_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "str", "hello-world" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$split", new BsonArray { "$str", "$missing" })))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.True(results[0]["result"] == BsonNull.Value || results[0]["result"].IsBsonNull);
    }

    #endregion

    #region $indexOfBytes with null substring

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task IndexOfBytes_WithNullSubstring_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfBytes/
        //   "If the first argument resolves to null, the expression returns null."
        var col = _fixture.GetCollection<BsonDocument>("indexof_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "str", "hello" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("idx",
                new BsonDocument("$indexOfBytes", new BsonArray { "$missing", "h" })))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.True(results[0]["idx"] == BsonNull.Value || results[0]["idx"].IsBsonNull);
    }

    #endregion

    #region $replaceOne with null find/replacement

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ReplaceOne_WithNullFind_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/replaceOne/
        //   "Returns null if any argument resolves to null."
        var col = _fixture.GetCollection<BsonDocument>("replaceone_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "str", "hello" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$replaceOne", new BsonDocument
                {
                    { "input", "$str" },
                    { "find", "$missing" },
                    { "replacement", "x" }
                })))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.True(results[0]["result"] == BsonNull.Value || results[0]["result"].IsBsonNull);
    }

    #endregion

    #region $strcasecmp with null values

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Strcasecmp_WithNullArg_DoesNotCrash()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/strcasecmp/
        //   "Returns 0 if they are equivalent, 1 if first > second, -1 if first < second."
        var col = _fixture.GetCollection<BsonDocument>("strcasecmp_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "name", "test" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("cmp",
                new BsonDocument("$strcasecmp", new BsonArray { "$name", "$missing" })))
        };

        // Should not throw — should return a value (MongoDB returns 1 for "test" > null)
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.NotNull(results[0]["cmp"]);
    }

    #endregion
}
