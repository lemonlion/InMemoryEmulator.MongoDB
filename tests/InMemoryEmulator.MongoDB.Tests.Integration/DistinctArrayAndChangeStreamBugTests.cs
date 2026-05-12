using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Tests for bugs found in Round 2:
/// 1. Distinct should unwrap array fields into individual elements
/// 2. Change streams should receive events from UpdateMany, DeleteMany, FindOneAnd* operations
/// 3. UpdateMany should record changes as Update (not Replace) in the change log
/// </summary>
[Collection("Integration")]
public class DistinctArrayAndChangeStreamBugTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public DistinctArrayAndChangeStreamBugTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Helper: create an identity pipeline that outputs BsonDocument for easier assertion.
    private static PipelineDefinition<ChangeStreamDocument<BsonDocument>, BsonDocument> RawPipeline()
    {
        return new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .As<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>, BsonDocument>();
    }

    #region Distinct array unwinding

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Distinct_ArrayField_UnwrapsEachElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/distinct/
        //   "If the value of the specified field is an array, distinct considers each element
        //    of the array as a separate value."
        var col = _fixture.GetCollection<BsonDocument>("distinct_array_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "tags", new BsonArray { "A", "B" } } },
            new BsonDocument { { "tags", new BsonArray { "B", "C" } } },
            new BsonDocument { { "tags", new BsonArray { "C", "D" } } }
        });

        var cursor = await col.DistinctAsync<string>("tags", Builders<BsonDocument>.Filter.Empty);
        var values = await cursor.ToListAsync();

        // Should return A, B, C, D — not the arrays themselves
        Assert.Equal(4, values.Count);
        Assert.Contains("A", values);
        Assert.Contains("B", values);
        Assert.Contains("C", values);
        Assert.Contains("D", values);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Distinct_ArrayField_DeduplicatesAcrossDocuments()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/distinct/
        //   "If the value of the specified field is an array, distinct considers each element
        //    of the array as a separate value."
        var col = _fixture.GetCollection<BsonDocument>("distinct_array_2");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "scores", new BsonArray { 1, 2, 3 } } },
            new BsonDocument { { "scores", new BsonArray { 2, 3, 4 } } },
            new BsonDocument { { "scores", new BsonArray { 4, 5 } } }
        });

        var cursor = await col.DistinctAsync<int>("scores", Builders<BsonDocument>.Filter.Empty);
        var values = await cursor.ToListAsync();

        Assert.Equal(5, values.Count);
        Assert.Contains(1, values);
        Assert.Contains(2, values);
        Assert.Contains(3, values);
        Assert.Contains(4, values);
        Assert.Contains(5, values);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Distinct_MixedArrayAndScalar_UnwrapsArraysOnly()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/distinct/
        //   "If the value of the specified field is an array, distinct considers each element
        //    of the array as a separate value."
        var col = _fixture.GetCollection<BsonDocument>("distinct_array_3");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "val", new BsonArray { "X", "Y" } } },
            new BsonDocument { { "val", "X" } },
            new BsonDocument { { "val", "Z" } },
        });

        var cursor = await col.DistinctAsync<string>("val", Builders<BsonDocument>.Filter.Empty);
        var values = await cursor.ToListAsync();

        // Array [X, Y] unwraps to X, Y; scalar X deduplicates; scalar Z; => X, Y, Z
        Assert.Equal(3, values.Count);
        Assert.Contains("X", values);
        Assert.Contains("Y", values);
        Assert.Contains("Z", values);
    }

    #endregion

    #region Change stream events from UpdateMany / DeleteMany / FindOneAnd*

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Watch_receives_events_from_UpdateMany()
    {
        // Ref: https://www.mongodb.com/docs/manual/changeStreams/
        //   "Change streams allow applications to access real-time data changes."
        var col = _fixture.GetCollection<BsonDocument>("cs_update_many");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "um1" }, { "status", "pending" } },
            new BsonDocument { { "_id", "um2" }, { "status", "pending" } },
        });

        using var cursor = await col.WatchAsync(RawPipeline());

        await col.UpdateManyAsync(
            Builders<BsonDocument>.Filter.Eq("status", "pending"),
            Builders<BsonDocument>.Update.Set("status", "done"));

        var events = await ChangeStreamHelper.WaitForEventsAsync(cursor, 2);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("update", e["operationType"].AsString));
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Watch_receives_events_from_DeleteMany()
    {
        // Ref: https://www.mongodb.com/docs/manual/changeStreams/
        //   "Change streams allow applications to access real-time data changes."
        var col = _fixture.GetCollection<BsonDocument>("cs_delete_many");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "dm1" }, { "temp", true } },
            new BsonDocument { { "_id", "dm2" }, { "temp", true } },
        });

        using var cursor = await col.WatchAsync(RawPipeline());

        await col.DeleteManyAsync(Builders<BsonDocument>.Filter.Eq("temp", true));

        var events = await ChangeStreamHelper.WaitForEventsAsync(cursor, 2);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("delete", e["operationType"].AsString));
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Watch_receives_event_from_FindOneAndDelete()
    {
        var col = _fixture.GetCollection<BsonDocument>("cs_find_del");
        await col.InsertOneAsync(new BsonDocument { { "_id", "fd1" }, { "name", "ToDelete" } });

        using var cursor = await col.WatchAsync(RawPipeline());

        await col.FindOneAndDeleteAsync(Builders<BsonDocument>.Filter.Eq("_id", "fd1"));

        var events = await ChangeStreamHelper.WaitForEventsAsync(cursor, 1);
        Assert.Single(events);
        Assert.Equal("delete", events[0]["operationType"].AsString);
        Assert.Equal("fd1", events[0]["documentKey"]["_id"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Watch_receives_event_from_FindOneAndReplace()
    {
        var col = _fixture.GetCollection<BsonDocument>("cs_find_rep");
        await col.InsertOneAsync(new BsonDocument { { "_id", "fr1" }, { "name", "Old" } });

        using var cursor = await col.WatchAsync(RawPipeline());

        await col.FindOneAndReplaceAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "fr1"),
            new BsonDocument { { "_id", "fr1" }, { "name", "New" } });

        var events = await ChangeStreamHelper.WaitForEventsAsync(cursor, 1);
        Assert.Single(events);
        Assert.Equal("replace", events[0]["operationType"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Watch_receives_event_from_FindOneAndUpdate()
    {
        var col = _fixture.GetCollection<BsonDocument>("cs_find_upd");
        await col.InsertOneAsync(new BsonDocument { { "_id", "fu1" }, { "count", 0 } });

        using var cursor = await col.WatchAsync(RawPipeline());

        await col.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "fu1"),
            Builders<BsonDocument>.Update.Inc("count", 1));

        var events = await ChangeStreamHelper.WaitForEventsAsync(cursor, 1);
        Assert.Single(events);
        Assert.Equal("update", events[0]["operationType"].AsString);
    }

    #endregion

    #region UpdateMany should use Update (not Replace) change type

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task UpdateMany_records_Update_change_type_not_Replace()
    {
        // Ref: https://www.mongodb.com/docs/manual/changeStreams/
        //   "For update operations, the change stream event includes an updateDescription
        //    field that contains the delta of the changes."
        var col = _fixture.GetCollection<BsonDocument>("cs_upd_many_type");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "t1" }, { "status", "a" } },
            new BsonDocument { { "_id", "t2" }, { "status", "a" } },
        });

        using var cursor = await col.WatchAsync(RawPipeline());

        await col.UpdateManyAsync(
            Builders<BsonDocument>.Filter.Eq("status", "a"),
            Builders<BsonDocument>.Update.Set("status", "b"));

        var events = await ChangeStreamHelper.WaitForEventsAsync(cursor, 2);
        // All events should be "update", NOT "replace"
        Assert.All(events, e => Assert.Equal("update", e["operationType"].AsString));
    }

    #endregion
}
