using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round22BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round22BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region CreateIndex same name different keys

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task CreateIndex_SameNameDifferentKeys_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.createIndex/
        //   "If you create an index with one set of options and then try to create the same index
        //    but with different options, MongoDB will return an error."
        var col = _fixture.GetCollection<BsonDocument>("r22_index_conflict");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 } });

        // Create an index
        col.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("field1"),
            new CreateIndexOptions { Name = "myindex" }));

        // Attempt to create a different index with the same name
        Assert.Throws<MongoCommandException>(() =>
            col.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Descending("field2"),
                new CreateIndexOptions { Name = "myindex" })));
    }

    #endregion

    #region $reduce on non-array input

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Reduce_OnNonArrayInput_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/reduce/
        //   "$reduce's input must be an array"
        var col = _fixture.GetCollection<BsonDocument>("r22_reduce_nonarray");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", "notarray" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$reduce", new BsonDocument
                {
                    { "input", "$val" },
                    { "initialValue", 0 },
                    { "in", new BsonDocument("$add", new BsonArray { "$$value", 1 }) }
                })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $reverseArray on non-array input

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ReverseArray_OnNonArray_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/reverseArray/
        //   "$reverseArray's argument must resolve to an array"
        var col = _fixture.GetCollection<BsonDocument>("r22_reverse_nonarray");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", 42 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$reverseArray", "$val")))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $slice on non-array input

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Slice_OnNonArray_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/slice/
        //   "The first argument must be an array"
        var col = _fixture.GetCollection<BsonDocument>("r22_slice_nonarray");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", "hello" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$slice", new BsonArray { "$val", 2 })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion
}
