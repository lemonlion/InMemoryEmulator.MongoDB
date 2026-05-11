using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round21BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round21BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $set through scalar path should error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Set_ThroughScalarPath_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/set/
        //   "Cannot create field 'c' in element {b: 5}"
        //   MongoDB throws PathNotViable (code 28) when a dotted path
        //   traverses a scalar rather than a document.
        var col = _fixture.GetCollection<BsonDocument>("r21_set_scalar_path");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonDocument { { "b", 5 } } }
        });

        // Attempting to $set "a.b.c" when a.b is 5 (scalar)
        await Assert.ThrowsAsync<MongoWriteException>(() =>
            col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                Builders<BsonDocument>.Update.Set("a.b.c", 10)));

        // Verify the original document was NOT corrupted
        var doc = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(5, doc["a"]["b"].AsInt32);
    }

    #endregion

    #region $objectToArray on non-object throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ObjectToArray_OnNonObject_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/objectToArray/
        //   "$objectToArray requires a document input"
        var col = _fixture.GetCollection<BsonDocument>("r21_obj2arr_nonobj");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", "hello" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$objectToArray", "$val")))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $filter on non-array input throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Filter_OnNonArrayInput_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/filter/
        //   "$filter's input must be an array"
        var col = _fixture.GetCollection<BsonDocument>("r21_filter_nonarray");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", "notarray" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$filter", new BsonDocument
                {
                    { "input", "$val" },
                    { "cond", true }
                })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $map on non-array input throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Map_OnNonArrayInput_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/map/
        //   "$map's input must be an array"
        var col = _fixture.GetCollection<BsonDocument>("r21_map_nonarray");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", 42 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$map", new BsonDocument
                {
                    { "input", "$val" },
                    { "in", "$$this" }
                })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion
}
