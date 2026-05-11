using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round18BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round18BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $toUpper/$toLower null returns empty string

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ToUpper_OnNull_ReturnsEmptyStringExactly()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/toUpper/
        //   "If the argument resolves to null, $toUpper returns an empty string ''."
        var col = _fixture.GetCollection<BsonDocument>("r18_toupper_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$toUpper", "$missing")))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Equal("", results[0]["result"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ToLower_OnNull_ReturnsEmptyStringExactly()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/toLower/
        //   "If the argument resolves to null, $toLower returns an empty string ''."
        var col = _fixture.GetCollection<BsonDocument>("r18_tolower_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$toLower", "$missing")))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Equal("", results[0]["result"].AsString);
    }

    #endregion

    #region $toUpper on non-string returns empty string

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ToUpper_OnInteger_ReturnsEmptyString()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/toUpper/
        //   Real MongoDB does not throw for non-string types; it returns "" (same as null).
        var col = _fixture.GetCollection<BsonDocument>("r18_toupper_nonstr");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", 123 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$toUpper", "$val")))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Single(results);
        Assert.Equal("", results[0]["result"].AsString);
    }

    #endregion

    #region $size (aggregation) on non-array throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Size_Aggregation_OnNonArray_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/size/
        //   "The argument for $size must resolve to an array."
        var col = _fixture.GetCollection<BsonDocument>("r18_size_nonarray");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", "hello" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("count",
                new BsonDocument("$size", "$val")))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $strLenBytes on null throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task StrLenBytes_OnNull_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/strLenBytes/
        //   "$strLenBytes requires a string argument, found: null"
        var col = _fixture.GetCollection<BsonDocument>("r18_strlen_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("len",
                new BsonDocument("$strLenBytes", "$missing")))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $strLenBytes on non-string throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task StrLenBytes_OnInteger_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/strLenBytes/
        //   "$strLenBytes requires a string argument, found: int"
        var col = _fixture.GetCollection<BsonDocument>("r18_strlen_int");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", 42 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("len",
                new BsonDocument("$strLenBytes", "$val")))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $regexMatch on non-string input throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task RegexMatch_OnNonStringInput_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/regexMatch/
        //   "$regexMatch needs 'input' to be of type string"
        var col = _fixture.GetCollection<BsonDocument>("r18_regex_nonstr");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", 789 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("match",
                new BsonDocument("$regexMatch", new BsonDocument
                {
                    { "input", "$val" },
                    { "regex", "^7" }
                })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion
}
