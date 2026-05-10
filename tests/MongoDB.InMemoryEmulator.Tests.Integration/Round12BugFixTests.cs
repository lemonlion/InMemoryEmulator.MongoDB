using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 12 tests covering:
/// 1. $unset with numeric array indices in paths
/// 2. Missing FaultInjector/OperationLog in several write operations
/// </summary>
[Collection("Integration")]
public class Round12BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round12BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $unset with numeric array indices

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Unset_NumericArrayIndex_RemovesFieldFromArrayElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/unset/
        //   "The specified value ... does not impact the operation."
        var col = _fixture.GetCollection<BsonDocument>("unset_arr_idx");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "qty", 5 }, { "temp", true } },
                    new BsonDocument { { "name", "b" }, { "qty", 10 }, { "temp", true } },
                }
            }
        });

        var update = Builders<BsonDocument>.Update.Unset("items.0.temp");
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.False(items[0].AsBsonDocument.Contains("temp"));
        Assert.True(items[1].AsBsonDocument.Contains("temp"));
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Unset_NumericArrayIndex_DirectElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/unset/
        //   "If the field does not exist, then $unset does nothing."
        //   When unsetting an array element by index, MongoDB sets it to null rather than removing it.
        var col = _fixture.GetCollection<BsonDocument>("unset_arr_direct");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "grades", new BsonArray { 80, 85, 90 } }
        });

        var update = Builders<BsonDocument>.Update.Unset("grades.1");
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var grades = result["grades"].AsBsonArray;
        // MongoDB sets the element to null when unsetting by index
        Assert.Equal(3, grades.Count);
        Assert.Equal(BsonNull.Value, grades[1]);
    }

    #endregion

    #region OperationLog coverage for write operations

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public async Task FindOneAndReplace_RecordsOperationLog()
    {
        var col = _fixture.GetCollection<BsonDocument>("opl_far");
        var inMemCol = (InMemoryMongoCollection<BsonDocument>)col;
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", 10 } });
        inMemCol.OperationLog.Clear();

        await col.FindOneAndReplaceAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument { { "_id", 1 }, { "x", 20 } });

        var logs = inMemCol.OperationLog.GetAll();
        Assert.Contains(logs, l => l.Type == "FindOneAndReplace");
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public async Task FindOneAndUpdate_RecordsOperationLog()
    {
        var col = _fixture.GetCollection<BsonDocument>("opl_fau");
        var inMemCol = (InMemoryMongoCollection<BsonDocument>)col;
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", 10 } });
        inMemCol.OperationLog.Clear();

        await col.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Set("x", 20));

        var logs = inMemCol.OperationLog.GetAll();
        Assert.Contains(logs, l => l.Type == "FindOneAndUpdate");
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public async Task DeleteMany_RecordsOperationLog()
    {
        var col = _fixture.GetCollection<BsonDocument>("opl_dm");
        var inMemCol = (InMemoryMongoCollection<BsonDocument>)col;
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 10 } },
            new BsonDocument { { "_id", 2 }, { "x", 20 } },
        });
        inMemCol.OperationLog.Clear();

        await col.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty);

        var logs = inMemCol.OperationLog.GetAll();
        Assert.Contains(logs, l => l.Type == "DeleteMany");
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public async Task UpdateMany_RecordsOperationLog()
    {
        var col = _fixture.GetCollection<BsonDocument>("opl_um");
        var inMemCol = (InMemoryMongoCollection<BsonDocument>)col;
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", 10 } });
        inMemCol.OperationLog.Clear();

        await col.UpdateManyAsync(
            Builders<BsonDocument>.Filter.Empty,
            Builders<BsonDocument>.Update.Set("x", 20));

        var logs = inMemCol.OperationLog.GetAll();
        Assert.Contains(logs, l => l.Type == "UpdateMany");
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public async Task ReplaceOne_RecordsOperationLog()
    {
        var col = _fixture.GetCollection<BsonDocument>("opl_ro");
        var inMemCol = (InMemoryMongoCollection<BsonDocument>)col;
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", 10 } });
        inMemCol.OperationLog.Clear();

        await col.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument { { "_id", 1 }, { "x", 20 } });

        var logs = inMemCol.OperationLog.GetAll();
        Assert.Contains(logs, l => l.Type == "ReplaceOne");
    }

    #endregion
}
