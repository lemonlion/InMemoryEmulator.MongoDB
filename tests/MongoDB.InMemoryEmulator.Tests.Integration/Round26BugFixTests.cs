using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round26BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round26BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Group_DocumentId_EvaluatesFieldExpressions()
    {
        var col = _fixture.GetCollection<BsonDocument>("r26_group_docid");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "a", 1 }, { "b", "x" } },
            new BsonDocument { { "_id", 2 }, { "a", 1 }, { "b", "y" } },
            new BsonDocument { { "_id", 3 }, { "a", 2 }, { "b", "x" } },
        });

        var pipeline = new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "fieldA", "$a" }, { "fieldB", "$b" } } },
                { "count", new BsonDocument("$sum", 1) }
            })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(1, r["count"].ToInt32()));
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Count_EmptyInput_ReturnsNoDocuments()
    {
        var col = _fixture.GetCollection<BsonDocument>("r26_count_empty");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "status", "active" } });

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("status", "archived")),
            new BsonDocument("$count", "total")
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Empty(results);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Push_OnNullField_ThrowsError()
    {
        var col = _fixture.GetCollection<BsonDocument>("r26_push_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "arr", BsonNull.Value } });

        await Assert.ThrowsAsync<MongoWriteException>(() =>
            col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                Builders<BsonDocument>.Update.Push("arr", 5)));
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task AddToSet_OnNullField_ThrowsError()
    {
        var col = _fixture.GetCollection<BsonDocument>("r26_addtoset_null");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "arr", BsonNull.Value } });

        await Assert.ThrowsAsync<MongoWriteException>(() =>
            col.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                Builders<BsonDocument>.Update.AddToSet("arr", 5)));
    }
}
