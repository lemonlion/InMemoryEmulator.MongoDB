using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round15BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round15BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Projection mode mixing validation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Projection_MixedInclusionExclusion_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.find/#std-label-find-projection
        //   "You cannot combine inclusion and exclusion statements, with the exception of the _id field."
        var col = _fixture.GetCollection<BsonDocument>("proj_mixed_mode");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", 10 }, { "b", 20 }, { "c", 30 } });

        var projection = new BsonDocument { { "a", 1 }, { "b", 0 } };

        // MongoDB throws a MongoCommandException for mixed projection
        await Assert.ThrowsAnyAsync<MongoCommandException>(async () =>
            await col.Find(Builders<BsonDocument>.Filter.Empty)
                .Project(projection)
                .ToListAsync());
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Projection_ExclusionWithIdExcluded_IsValid()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.find/#std-label-find-projection
        //   "_id exclusion is the exception — you can exclude _id while including other fields."
        var col = _fixture.GetCollection<BsonDocument>("proj_id_excluded");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", 10 }, { "b", 20 } });

        var projection = new BsonDocument { { "_id", 0 }, { "a", 1 } };

        var results = await col.Find(Builders<BsonDocument>.Filter.Empty)
            .Project(projection)
            .ToListAsync();

        Assert.Single(results);
        Assert.False(results[0].Contains("_id"));
        Assert.Equal(10, results[0]["a"].AsInt32);
        Assert.False(results[0].Contains("b"));
    }

    #endregion

    #region $bit on non-numeric field

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Bit_OnNonNumericField_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/bit/
        //   "$bit requires the field to hold a numeric value."
        var col = _fixture.GetCollection<BsonDocument>("bit_non_numeric");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "flags", "not-a-number" } });

        var update = new BsonDocument("$bit", new BsonDocument("flags",
            new BsonDocument("or", 4)));

        await Assert.ThrowsAnyAsync<MongoWriteException>(async () =>
            await col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                new BsonDocumentUpdateDefinition<BsonDocument>(update)));
    }

    #endregion

    #region $inc on missing field creates the field

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Inc_OnMissingField_CreatesFieldWithIncrementValue()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/inc/
        //   "If the field does not exist, $inc creates the field and sets the field to the specified value."
        var col = _fixture.GetCollection<BsonDocument>("inc_missing_field");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 } });

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Inc("counter", 5));

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(5, result["counter"].AsInt32);
    }

    #endregion

    #region $mul on missing field creates field with zero

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Mul_OnMissingField_CreatesFieldWithZero()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/mul/
        //   "If the field does not exist, $mul creates the field and sets the value to zero
        //    of the same numeric type as the multiplier."
        var col = _fixture.GetCollection<BsonDocument>("mul_missing_field");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 } });

        var update = new BsonDocument("$mul", new BsonDocument("price", 2.5));
        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocumentUpdateDefinition<BsonDocument>(update));

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(0.0, result["price"].AsDouble);
    }

    #endregion

    #region $addToSet with non-array field

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task AddToSet_OnNonArrayField_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/addToSet/
        //   "If the field is not an array, the operation will fail."
        var col = _fixture.GetCollection<BsonDocument>("addtoset_non_array");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "value", "text" } });

        var update = new BsonDocument("$addToSet", new BsonDocument("value", "new"));

        await Assert.ThrowsAnyAsync<MongoWriteException>(async () =>
            await col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                new BsonDocumentUpdateDefinition<BsonDocument>(update)));
    }

    #endregion

    #region $push on non-array field

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Push_OnNonArrayField_ThrowsError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/push/
        //   "If the field is not an array, the operation will fail."
        var col = _fixture.GetCollection<BsonDocument>("push_non_array");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "value", 42 } });

        var update = new BsonDocument("$push", new BsonDocument("value", 99));

        await Assert.ThrowsAnyAsync<MongoWriteException>(async () =>
            await col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                new BsonDocumentUpdateDefinition<BsonDocument>(update)));
    }

    #endregion
}
