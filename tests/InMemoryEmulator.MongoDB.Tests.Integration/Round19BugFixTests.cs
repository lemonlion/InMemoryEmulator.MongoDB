using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

[Collection("Integration")]
public class Round19BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round19BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $getField with null input

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task GetField_WithNullInput_OmitsFieldFromOutput()
    {
        // Ref: Observed real MongoDB 7.0:
        //   When $getField input resolves to null/missing, the field is omitted from output entirely.
        var col = _fixture.GetCollection<BsonDocument>("r19_getfield_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "name", "test" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$getField", new BsonDocument
                {
                    { "field", "name" },
                    { "input", "$missing" }
                })))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        // Real MongoDB omits the field entirely — it doesn't appear in the output
        Assert.False(results[0].Contains("result"));
    }

    #endregion

    #region $setField with null input

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task SetField_WithNullInput_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setField/
        //   "If the input argument resolves to null or missing, $setField returns null."
        var col = _fixture.GetCollection<BsonDocument>("r19_setfield_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "name", "test" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$setField", new BsonDocument
                {
                    { "field", "newField" },
                    { "input", "$missing" },
                    { "value", "val" }
                })))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.True(results[0]["result"] == BsonNull.Value || results[0]["result"].IsBsonNull);
    }

    #endregion

    #region $concat with non-string argument

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Concat_WithNonStringArg_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/concat/
        //   "$concat only supports strings, not int"
        var col = _fixture.GetCollection<BsonDocument>("r19_concat_nonstr");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "num", 42 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$concat", new BsonArray { "hello ", "$num" })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $trim with non-string input

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Trim_WithNonStringInput_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/trim/
        //   "$trim requires its input to be a string"
        var col = _fixture.GetCollection<BsonDocument>("r19_trim_nonstr");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "num", 42 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$trim", new BsonDocument("input", "$num"))))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $arrayElemAt with non-array first argument

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ArrayElemAt_WithNonArray_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/arrayElemAt/
        //   "$arrayElemAt's first argument must be an array"
        var col = _fixture.GetCollection<BsonDocument>("r19_arrayelemat_nonarray");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", "notarray" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$arrayElemAt", new BsonArray { "$val", 0 })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $in (aggregation) with non-array second argument

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task In_Aggregation_WithNonArraySecondArg_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/in/
        //   "$in requires an array as a second argument"
        var col = _fixture.GetCollection<BsonDocument>("r19_in_nonarray");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", "hello" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$in", new BsonArray { "x", "$val" })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion

    #region $concatArrays with non-array argument

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ConcatArrays_WithNonArrayArg_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/concatArrays/
        //   "$concatArrays only supports arrays, not string"
        var col = _fixture.GetCollection<BsonDocument>("r19_concatarrays_nonarray");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", "notarray" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$concatArrays", new BsonArray { new BsonArray { 1 }, "$val" })))
        };

        await Assert.ThrowsAsync<MongoCommandException>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());
    }

    #endregion
}
