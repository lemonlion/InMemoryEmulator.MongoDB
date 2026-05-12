using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

[Collection("Integration")]
public class Round27BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round27BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task InsertMany_Ordered_DuplicateKey_ThrowsBulkWriteException()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.insertMany/
        //   "InsertMany errors are always wrapped in MongoBulkWriteException."
        var col = _fixture.GetCollection<BsonDocument>("r27_insertmany_ordered");
        var docs = new[]
        {
            new BsonDocument("_id", 1),
            new BsonDocument("_id", 1), // duplicate
            new BsonDocument("_id", 2),
        };

        var ex = await Assert.ThrowsAsync<MongoBulkWriteException<BsonDocument>>(
            () => col.InsertManyAsync(docs));

        // Should report 1 write error at index 1
        Assert.Single(ex.WriteErrors);
        Assert.Equal(1, ex.WriteErrors[0].Index);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task AddFields_WithNestedDocumentExpression_EvaluatesFields()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/addFields/
        //   "Adds new fields to documents."
        // This verifies that $addFields with nested document values evaluates field expressions
        var col = _fixture.GetCollection<BsonDocument>("r27_addfields_nested");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", 10 }, { "y", 20 } });

        var pipeline = new[]
        {
            new BsonDocument("$addFields", new BsonDocument("coords",
                new BsonDocument { { "lat", "$x" }, { "lng", "$y" } }))
        };

        var result = await col.Aggregate<BsonDocument>(pipeline).FirstAsync();
        var coords = result["coords"].AsBsonDocument;
        Assert.Equal(10, coords["lat"].AsInt32);
        Assert.Equal(20, coords["lng"].AsInt32);
    }
}
