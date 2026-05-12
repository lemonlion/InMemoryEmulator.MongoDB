using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 11 tests covering:
/// 1. $bit with positional operators
/// 2. FindOneAndDelete missing FaultInjector/OperationLog
/// 3. $set with numeric array index paths (e.g., "arr.0.field")
/// 4. $bucketAuto with output accumulators
/// </summary>
[Collection("Integration")]
public class Round11BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round11BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $bit with positional operators

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Bit_AllPositional_BitwiseOrOnAllElements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/bit/
        //   "The $bit operator performs a bitwise update of a field."
        var col = _fixture.GetCollection<BsonDocument>("bit_all_pos");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "flags", 5 } },  // 0101
                    new BsonDocument { { "name", "b" }, { "flags", 3 } },  // 0011
                }
            }
        });

        var update = Builders<BsonDocument>.Update.BitwiseOr("items.$[].flags", 8); // 1000
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.Equal(13, items[0]["flags"].AsInt32); // 0101 | 1000 = 1101 = 13
        Assert.Equal(11, items[1]["flags"].AsInt32); // 0011 | 1000 = 1011 = 11
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Bit_FilteredPositional_BitwiseAndOnMatching()
    {
        var col = _fixture.GetCollection<BsonDocument>("bit_filtered_pos");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "perms", 7 } },   // 0111
                    new BsonDocument { { "name", "b" }, { "perms", 15 } },  // 1111
                    new BsonDocument { { "name", "c" }, { "perms", 3 } },   // 0011
                }
            }
        });

        var update = Builders<BsonDocument>.Update.BitwiseAnd("items.$[i].perms", 5); // 0101
        var options = new UpdateOptions
        {
            ArrayFilters = new[]
            {
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("i.perms", new BsonDocument("$gte", 7)))
            }
        };

        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update, options);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.Equal(5, items[0]["perms"].AsInt32);  // 0111 & 0101 = 0101 = 5
        Assert.Equal(5, items[1]["perms"].AsInt32);  // 1111 & 0101 = 0101 = 5
        Assert.Equal(3, items[2]["perms"].AsInt32);  // unchanged (perms < 7)
    }

    #endregion

    #region $set with numeric array index in path

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Set_NumericArrayIndex_UpdatesCorrectElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/set/#set-elements-in-arrays
        //   "To specify a <field> in an embedded document or in an array, use dot notation."
        var col = _fixture.GetCollection<BsonDocument>("set_num_idx");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "qty", 5 } },
                    new BsonDocument { { "name", "b" }, { "qty", 10 } },
                }
            }
        });

        var update = Builders<BsonDocument>.Update.Set("items.1.qty", 99);
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.Equal(5, items[0]["qty"].AsInt32);
        Assert.Equal(99, items[1]["qty"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Set_NumericArrayIndex_DirectElement()
    {
        // Set a direct array element by index
        var col = _fixture.GetCollection<BsonDocument>("set_num_idx_direct");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "grades", new BsonArray { 80, 85, 90 } }
        });

        var update = Builders<BsonDocument>.Update.Set("grades.1", 100);
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(new BsonArray { 80, 100, 90 }, result["grades"].AsBsonArray);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Inc_NumericArrayIndex_IncrementsCorrectElement()
    {
        // $inc with numeric array index path
        var col = _fixture.GetCollection<BsonDocument>("inc_num_idx");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "qty", 5 } },
                    new BsonDocument { { "name", "b" }, { "qty", 10 } },
                }
            }
        });

        var update = Builders<BsonDocument>.Update.Inc("items.0.qty", 3);
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.Equal(8, items[0]["qty"].AsInt32);
        Assert.Equal(10, items[1]["qty"].AsInt32);
    }

    #endregion

    #region FindOneAndDelete FaultInjector / OperationLog

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public async Task FindOneAndDelete_RecordsOperationLog()
    {
        // Verify that FindOneAndDelete records to OperationLog like other write operations
        var col = _fixture.GetCollection<BsonDocument>("fad_oplog");
        var inMemCol = (InMemoryMongoCollection<BsonDocument>)col;

        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", 10 } });

        // Clear existing log entries from insert
        inMemCol.OperationLog.Clear();

        await col.FindOneAndDeleteAsync(Builders<BsonDocument>.Filter.Eq("_id", 1));

        var logs = inMemCol.OperationLog.GetAll();
        Assert.Contains(logs, l => l.Type == "FindOneAndDelete");
    }

    #endregion

    #region $bucketAuto with output accumulators

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task BucketAuto_WithOutputAccumulators()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/bucketAuto/
        //   "output: A document that specifies the fields to include in the output documents."
        var col = _fixture.GetCollection<BsonDocument>("bucket_auto_output");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "score", 10 } },
            new BsonDocument { { "_id", 2 }, { "score", 20 } },
            new BsonDocument { { "_id", 3 }, { "score", 30 } },
            new BsonDocument { { "_id", 4 }, { "score", 40 } },
        });

        var pipeline = new BsonDocument[]
        {
            new BsonDocument("$bucketAuto", new BsonDocument
            {
                { "groupBy", "$score" },
                { "buckets", 2 },
                { "output", new BsonDocument
                    {
                        { "total", new BsonDocument("$sum", "$score") },
                        { "avg", new BsonDocument("$avg", "$score") },
                    }
                }
            })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        Assert.Equal(2, results.Count);

        // First bucket: scores 10, 20 → total=30, avg=15
        Assert.Equal(30, results[0]["total"].ToInt32());
        Assert.Equal(15.0, results[0]["avg"].ToDouble(), 0.01);

        // Second bucket: scores 30, 40 → total=70, avg=35
        Assert.Equal(70, results[1]["total"].ToInt32());
        Assert.Equal(35.0, results[1]["avg"].ToDouble(), 0.01);
    }

    #endregion
}
