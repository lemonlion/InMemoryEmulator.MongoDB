using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round23BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round23BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $sortArray on non-array input

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task SortArray_OnNonArrayInput_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sortArray/
        //   "input must resolve to an array"
        var col = _fixture.GetCollection<BsonDocument>("r23_sortarray_nonarray");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", "notarray" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("sorted",
                new BsonDocument("$sortArray", new BsonDocument
                {
                    { "input", "$val" },
                    { "sortBy", 1 }
                })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $toObjectId on non-string

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ToObjectId_OnNonString_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/toObjectId/
        //   "$toObjectId requires a string argument"
        var col = _fixture.GetCollection<BsonDocument>("r23_toobjectid_nonstr");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "num", 123 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("oid",
                new BsonDocument("$toObjectId", "$num")))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $split on non-string input

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Split_OnNonStringInput_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/split/
        //   "Both arguments must be strings"
        var col = _fixture.GetCollection<BsonDocument>("r23_split_nonstr");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "num", 123 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("parts",
                new BsonDocument("$split", new BsonArray { "$num", "," })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $dateFromString on non-string

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task DateFromString_OnNonString_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateFromString/
        //   "dateString must be a string"
        var col = _fixture.GetCollection<BsonDocument>("r23_datefromstr_nonstr");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "num", 12345 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("date",
                new BsonDocument("$dateFromString", new BsonDocument("dateString", "$num"))))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $trim chars on null

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Trim_WithNullChars_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/trim/
        //   "If chars resolves to null, $trim returns null."
        var col = _fixture.GetCollection<BsonDocument>("r23_trim_nullchars");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "str", "  hello  " } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$trim", new BsonDocument
                {
                    { "input", "$str" },
                    { "chars", "$missing" }
                })))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.True(results[0]["result"] == BsonNull.Value || results[0]["result"].IsBsonNull);
    }

    #endregion
}
