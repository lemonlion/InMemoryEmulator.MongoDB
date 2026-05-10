using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 4 bug fix tests covering:
/// 1. $regex + $options in operator form
/// 2. $round IEEE 754 round-to-even
/// 3. Upsert change stream events
/// 4. BulkWrite InsertOneModel index validation and change events
/// 5. ListCollectionNames includes views
/// 6. RunCommand distinct array unwinding
/// </summary>
[Collection("Integration")]
public class Round4BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round4BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PipelineDefinition<ChangeStreamDocument<BsonDocument>, BsonDocument> RawPipeline()
    {
        return new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .As<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>, BsonDocument>();
    }

    #region $regex + $options

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Filter_RegexWithOptions_CaseInsensitive()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/regex/
        //   "$options: Optional. Modifies the $regex behavior."
        var col = _fixture.GetCollection<BsonDocument>("regex_options");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "name", "Alice" } },
            new BsonDocument { { "name", "bob" } },
            new BsonDocument { { "name", "CHARLIE" } },
        });

        // Use raw BsonDocument filter: { name: { $regex: "alice", $options: "i" } }
        var filter = new BsonDocument("name", new BsonDocument
        {
            { "$regex", "alice" },
            { "$options", "i" }
        });

        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal("Alice", results[0]["name"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Filter_RegexWithOptions_Multiline()
    {
        var col = _fixture.GetCollection<BsonDocument>("regex_multiline");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "text", "hello\nworld" } },
            new BsonDocument { { "text", "hello world" } },
        });

        var filter = new BsonDocument("text", new BsonDocument
        {
            { "$regex", "^world" },
            { "$options", "m" }
        });

        var results = await col.Find(filter).ToListAsync();
        Assert.Single(results);
        Assert.Contains("\n", results[0]["text"].AsString);
    }

    #endregion

    #region $round IEEE 754

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Round_UsesRoundToEven()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/round/
        //   "Rounds using the IEEE 754 round-to-even rule."
        //   2.5 rounds to 2 (not 3), 3.5 rounds to 4.
        var col = _fixture.GetCollection<BsonDocument>("round_even");
        await col.InsertOneAsync(new BsonDocument { { "_id", "r1" }, { "val", 2.5 } });
        await col.InsertOneAsync(new BsonDocument { { "_id", "r2" }, { "val", 3.5 } });
        await col.InsertOneAsync(new BsonDocument { { "_id", "r3" }, { "val", 4.5 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument
            {
                { "rounded", new BsonDocument("$round", new BsonArray { "$val", 0 }) }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        // IEEE 754 round-to-even: 2.5 → 2, 3.5 → 4, 4.5 → 4
        Assert.Equal(2.0, results[0]["rounded"].ToDouble());
        Assert.Equal(4.0, results[1]["rounded"].ToDouble());
        Assert.Equal(4.0, results[2]["rounded"].ToDouble());
    }

    #endregion

    #region Upsert change stream events

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public async Task Watch_receives_insert_event_from_UpdateOne_upsert()
    {
        // Ref: https://www.mongodb.com/docs/manual/changeStreams/
        //   "Change streams notify about insert, update, replace, delete events."
        var col = _fixture.GetCollection<BsonDocument>("cs_upsert_update");

        using var cursor = col.Watch(RawPipeline());

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "upsert1"),
            Builders<BsonDocument>.Update.Set("name", "Upserted"),
            new UpdateOptions { IsUpsert = true });

        var hasEvents = cursor.MoveNext();
        Assert.True(hasEvents);
        var evt = cursor.Current.First();
        Assert.Equal("insert", evt["operationType"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public async Task Watch_receives_insert_event_from_ReplaceOne_upsert()
    {
        var col = _fixture.GetCollection<BsonDocument>("cs_upsert_replace");

        using var cursor = col.Watch(RawPipeline());

        await col.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "upsert2"),
            new BsonDocument { { "_id", "upsert2" }, { "name", "Replaced" } },
            new ReplaceOptions { IsUpsert = true });

        var hasEvents = cursor.MoveNext();
        Assert.True(hasEvents);
        var evt = cursor.Current.First();
        Assert.Equal("insert", evt["operationType"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public async Task Watch_receives_insert_event_from_FindOneAndUpdate_upsert()
    {
        var col = _fixture.GetCollection<BsonDocument>("cs_upsert_findupd");

        using var cursor = col.Watch(RawPipeline());

        await col.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "upsert3"),
            Builders<BsonDocument>.Update.Set("name", "Found"),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true });

        var hasEvents = cursor.MoveNext();
        Assert.True(hasEvents);
        var evt = cursor.Current.First();
        Assert.Equal("insert", evt["operationType"].AsString);
    }

    #endregion

    #region BulkWrite InsertOneModel validation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task BulkWrite_InsertOneModel_EnforcesUniqueIndex()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.bulkWrite/
        //   "BulkWrite inserts should validate unique indexes."
        var col = _fixture.GetCollection<BsonDocument>("bulk_idx");
        await col.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("email"),
            new CreateIndexOptions { Unique = true }));

        await col.InsertOneAsync(new BsonDocument { { "email", "test@test.com" } });

        // Ref: https://www.mongodb.com/docs/drivers/csharp/current/fundamentals/crud/write-operations/bulk-write/
        //   "BulkWrite always throws MongoBulkWriteException on failure."
        await Assert.ThrowsAnyAsync<MongoBulkWriteException<BsonDocument>>(() =>
            col.BulkWriteAsync(new[]
            {
                new InsertOneModel<BsonDocument>(new BsonDocument { { "email", "test@test.com" } })
            }));
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public async Task BulkWrite_InsertOneModel_PublishesChangeEvent()
    {
        var col = _fixture.GetCollection<BsonDocument>("bulk_cs");

        using var cursor = col.Watch(RawPipeline());

        await col.BulkWriteAsync(new[]
        {
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", "bk1" }, { "name", "BulkInsert" } })
        });

        var hasEvents = cursor.MoveNext();
        Assert.True(hasEvents);
        var evt = cursor.Current.First();
        Assert.Equal("insert", evt["operationType"].AsString);
    }

    #endregion

    #region ListCollectionNames includes views

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ListCollectionNames_IncludesViews()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/listCollections/
        //   "Returns both collections and views."
        var db = _fixture.Database;

        // Create a collection
        var col = db.GetCollection<BsonDocument>("real_collection");
        await col.InsertOneAsync(new BsonDocument { { "x", 1 } });

        // Create a view via RunCommand
        db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "create", "my_view" },
            { "viewOn", "real_collection" },
            { "pipeline", new BsonArray() },
        });

        var names = (await db.ListCollectionNamesAsync()).ToList();

        Assert.Contains("real_collection", names);
        Assert.Contains("my_view", names);
    }

    #endregion

    #region RunCommand distinct with arrays

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task RunCommand_Distinct_UnwindsArrayValues()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/distinct/
        //   "If the value of the specified field is an array, distinct considers each element separately."
        var db = _fixture.Database;
        var col = db.GetCollection<BsonDocument>("rc_distinct_arr");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "tags", new BsonArray { "a", "b" } } },
            new BsonDocument { { "tags", new BsonArray { "b", "c" } } },
        });

        var result = db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "distinct", "rc_distinct_arr" },
            { "key", "tags" },
        });

        var values = result["values"].AsBsonArray.Select(v => v.AsString).ToList();
        Assert.Equal(3, values.Count);
        Assert.Contains("a", values);
        Assert.Contains("b", values);
        Assert.Contains("c", values);
    }

    #endregion
}
