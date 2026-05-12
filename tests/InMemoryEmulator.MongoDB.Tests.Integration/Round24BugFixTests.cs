using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

[Collection("Integration")]
public class Round24BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round24BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $range step=0 should error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Range_StepZero_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/range/
        //   "A non-zero step value."
        var col = _fixture.GetCollection<BsonDocument>("r24_range_step0");
        await col.InsertOneAsync(new BsonDocument("_id", 1));

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("r",
                new BsonDocument("$range", new BsonArray { 0, 10, 0 })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.AggregateAsync<BsonDocument>(pipeline));
    }

    #endregion

    #region $round null returns null

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Round_NullInput_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/round/
        //   "If the argument resolves to a value of null or refers to a missing field,
        //    $round returns null."
        var col = _fixture.GetCollection<BsonDocument>("r24_round_null");
        await col.InsertOneAsync(new BsonDocument("_id", 1));

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("rounded",
                new BsonDocument("$round", new BsonArray { BsonNull.Value, 1 })))
        };

        var result = await col.Aggregate<BsonDocument>(pipeline).FirstAsync();
        Assert.Equal(BsonNull.Value, result["rounded"]);
    }

    #endregion

    #region $trunc null returns null

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Trunc_NullInput_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/trunc/
        //   "If the argument resolves to a value of null or refers to a missing field,
        //    $trunc returns null."
        var col = _fixture.GetCollection<BsonDocument>("r24_trunc_null");
        await col.InsertOneAsync(new BsonDocument("_id", 1));

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("truncated",
                new BsonDocument("$trunc", new BsonArray { BsonNull.Value, 1 })))
        };

        var result = await col.Aggregate<BsonDocument>(pipeline).FirstAsync();
        Assert.Equal(BsonNull.Value, result["truncated"]);
    }

    #endregion

    #region $indexOfBytes start >= string length returns -1

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task IndexOfBytes_StartBeyondStringLength_ReturnsMinusOne()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfBytes/
        //   "{ $indexOfBytes: [ "vanilla", "ll", 12 ] } → -1"
        var col = _fixture.GetCollection<BsonDocument>("r24_indexofbytes_oob");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "s", "hello" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("idx",
                new BsonDocument("$indexOfBytes", new BsonArray { "$s", "l", 100 })))
        };

        var result = await col.Aggregate<BsonDocument>(pipeline).FirstAsync();
        Assert.Equal(-1, result["idx"].AsInt32);
    }

    #endregion

    #region $indexOfBytes null substring should error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task IndexOfBytes_NullSubstring_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfBytes/
        //   "The $indexOfBytes expression has the following operator expression syntax:
        //    { $indexOfBytes: [ <string expression>, <substring expression>, ... ] }
        //    <substring expression> can be any valid expression as long as it resolves to a string."
        //   Null substring is an error per MongoDB behavior.
        var col = _fixture.GetCollection<BsonDocument>("r24_indexofbytes_nullsub");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "s", "hello" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("idx",
                new BsonDocument("$indexOfBytes", new BsonArray { "$s", BsonNull.Value })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.AggregateAsync<BsonDocument>(pipeline));
    }

    #endregion

    #region $zip null input returns null

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Zip_NullInput_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/zip/
        //   "If any of the inputs arrays resolves to a value of null or refers to a
        //    missing field, $zip returns null."
        var col = _fixture.GetCollection<BsonDocument>("r24_zip_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", new BsonArray { 1, 2 } } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("zipped",
                new BsonDocument("$zip", new BsonDocument("inputs",
                    new BsonArray { "$a", "$missing" }))))
        };

        var result = await col.Aggregate<BsonDocument>(pipeline).FirstAsync();
        Assert.Equal(BsonNull.Value, result["zipped"]);
    }

    #endregion

    #region $mergeObjects non-document input throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task MergeObjects_NonDocumentInput_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/mergeObjects/
        //   "$mergeObjects requires object inputs, but received: <type>"
        var col = _fixture.GetCollection<BsonDocument>("r24_mergeobjects_nonobj");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", new BsonDocument("x", 1) } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("merged",
                new BsonDocument("$mergeObjects", new BsonArray { "$a", "hello" })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.AggregateAsync<BsonDocument>(pipeline));
    }

    #endregion
}
