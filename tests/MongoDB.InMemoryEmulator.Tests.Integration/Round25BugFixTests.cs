using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round25BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round25BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $inc on null-valued field throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Inc_OnNullField_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/inc/
        //   "Cannot apply $inc to a value of non-numeric type null."
        var col = _fixture.GetCollection<BsonDocument>("r25_inc_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", BsonNull.Value } });

        var ex = await Assert.ThrowsAsync<MongoCommandException>(() =>
            col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                Builders<BsonDocument>.Update.Inc("x", 5)));

        Assert.Contains("non-numeric type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region $mul on null-valued field throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Mul_OnNullField_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/mul/
        //   "Cannot apply $mul to a value of non-numeric type null."
        var col = _fixture.GetCollection<BsonDocument>("r25_mul_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", BsonNull.Value } });

        var ex = await Assert.ThrowsAsync<MongoCommandException>(() =>
            col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                Builders<BsonDocument>.Update.Mul("x", 3)));

        Assert.Contains("non-numeric type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region $rename on null-valued field should rename it

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Rename_NullValuedField_RenamesSuccessfully()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/rename/
        //   "$rename does nothing if the field does not exist."
        //   But a field set to null DOES exist and should be renamed.
        var col = _fixture.GetCollection<BsonDocument>("r25_rename_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "old", BsonNull.Value } });

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Rename("old", "new"));

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.False(result.Contains("old"));
        Assert.True(result.Contains("new"));
        Assert.Equal(BsonNull.Value, result["new"]);
    }

    #endregion

    #region $min on null-valued field is no-op (null < numbers in BSON)

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Min_OnNullField_WithNumber_IsNoOp()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/bson-type-comparison-order/
        //   "MinKey (internal type) < Null < Numbers < ..."
        //   So $min with a number on a null field should be a no-op since null < 5.
        var col = _fixture.GetCollection<BsonDocument>("r25_min_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", BsonNull.Value } });

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Min("x", 5));

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(BsonNull.Value, result["x"]);
    }

    #endregion

    #region $all matches scalar field value

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task All_MatchesScalarFieldValue()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/all/
        //   "{ tags: { $all: ['ssl'] } } is equivalent to { $and: [ { tags: 'ssl' } ] }"
        //   Since { tags: 'ssl' } matches a scalar value, $all should too.
        var col = _fixture.GetCollection<BsonDocument>("r25_all_scalar");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "tag", "ssl" } });
        await col.InsertOneAsync(new BsonDocument { { "_id", 2 }, { "tag", "other" } });

        var filter = Builders<BsonDocument>.Filter.All("tag", new[] { "ssl" });
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    #endregion
}
