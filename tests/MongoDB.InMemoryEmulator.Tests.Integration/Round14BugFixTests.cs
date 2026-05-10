using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round14BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round14BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $pop on non-array field

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Pop_OnNonArrayField_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/pop/
        //   "If <field> is not an array, $pop fails."
        var col = _fixture.GetCollection<BsonDocument>("pop_non_array");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "value", "hello" } });

        var update = new BsonDocument("$pop", new BsonDocument("value", 1));

        await Assert.ThrowsAnyAsync<MongoCommandException>(async () =>
            await col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                new BsonDocumentUpdateDefinition<BsonDocument>(update)));
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Pop_OnMissingField_IsNoOp()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/pop/
        //   "If the field does not exist, $pop does nothing."
        var col = _fixture.GetCollection<BsonDocument>("pop_missing_field");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "other", "data" } });

        var update = new BsonDocument("$pop", new BsonDocument("missing", 1));

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocumentUpdateDefinition<BsonDocument>(update));

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.False(result.Contains("missing"));
    }

    #endregion

    #region $pull on non-array field

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Pull_OnNonArrayField_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/pull/
        //   "If the specified <field> is not an array, the operation will fail."
        var col = _fixture.GetCollection<BsonDocument>("pull_non_array");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "value", 42 } });

        var update = new BsonDocument("$pull", new BsonDocument("value", 42));

        await Assert.ThrowsAnyAsync<MongoCommandException>(async () =>
            await col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                new BsonDocumentUpdateDefinition<BsonDocument>(update)));
    }

    #endregion

    #region $pullAll on non-array field

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task PullAll_OnNonArrayField_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/pullAll/
        //   "If a <field> is not an array, the operation will fail."
        var col = _fixture.GetCollection<BsonDocument>("pullall_non_array");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "value", "text" } });

        var update = new BsonDocument("$pullAll", new BsonDocument("value", new BsonArray { "text" }));

        await Assert.ThrowsAnyAsync<MongoCommandException>(async () =>
            await col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                new BsonDocumentUpdateDefinition<BsonDocument>(update)));
    }

    #endregion

    #region $inc on non-numeric field

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Inc_OnNonNumericField_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/inc/
        //   "Cannot apply $inc to a value of non-numeric type."
        var col = _fixture.GetCollection<BsonDocument>("inc_non_numeric");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "name", "test" } });

        var update = Builders<BsonDocument>.Update.Inc("name", 1);

        await Assert.ThrowsAnyAsync<MongoCommandException>(async () =>
            await col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1), update));
    }

    #endregion

    #region $mul on non-numeric field

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Mul_OnNonNumericField_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/mul/
        //   "Cannot apply $mul to a value of non-numeric type."
        var col = _fixture.GetCollection<BsonDocument>("mul_non_numeric");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "name", "test" } });

        var update = new BsonDocument("$mul", new BsonDocument("name", 2));

        await Assert.ThrowsAnyAsync<MongoCommandException>(async () =>
            await col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                new BsonDocumentUpdateDefinition<BsonDocument>(update)));
    }

    #endregion

    #region $rename to same field name

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Rename_ToSameName_IsNoOp()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/rename/
        //   "The $rename operator logically performs an $unset of both the old name and the new name,
        //    and then performs a $set on the target field with the value from the source field."
        //   When old and new are the same, this is effectively a no-op but should not error.
        var col = _fixture.GetCollection<BsonDocument>("rename_same_name");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", 10 } });

        var update = new BsonDocument("$rename", new BsonDocument("a", "a"));

        // MongoDB allows this — it's a no-op
        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocumentUpdateDefinition<BsonDocument>(update));

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(10, result["a"].AsInt32);
    }

    #endregion
}
