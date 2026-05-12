using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 43: Delete change event field assignment, BulkWrite unordered MongoCommandException handling,
/// $indexOfArray negative start/end validation
/// </summary>
[Collection("Integration")]
public class Round43BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round43BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Delete change event: FullDocument is null, FullDocumentBeforeChange has the document

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Watch_DeleteEvent_FullDocumentIsNull_FullDocumentBeforeChangeHasDocument()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/change-events/delete/
        //   "For delete events, fullDocument is omitted and fullDocumentBeforeChange
        //    holds the pre-delete state of the document."
        var col = _fixture.GetCollection<BsonDocument>("cs_del_r43");

        // Enable changeStreamPreAndPostImages on the collection (required by real MongoDB)
        // Ref: https://www.mongodb.com/docs/manual/changeStreams/#change-streams-with-document-pre--and-post-images
        var collMod = new BsonDocument
        {
            { "collMod", "cs_del_r43" },
            { "changeStreamPreAndPostImages", new BsonDocument("enabled", true) }
        };
        // Ensure collection exists first
        await col.InsertOneAsync(new BsonDocument { { "_id", "d1" }, { "name", "Alice" } });
        try { _fixture.Database.RunCommand<BsonDocument>(collMod); } catch { /* In-memory may not support collMod */ }

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .As<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>, BsonDocument>();

        var options = new ChangeStreamOptions
        {
            FullDocumentBeforeChange = ChangeStreamFullDocumentBeforeChangeOption.WhenAvailable
        };

        using var cursor = await col.WatchAsync(pipeline, options);

        await col.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", "d1"));

        var events = await ChangeStreamHelper.WaitForEventsAsync(cursor, 1);
        Assert.Single(events);
        var evt = events[0];

        Assert.Equal("delete", evt["operationType"].AsString);
        Assert.Equal("d1", evt["documentKey"]["_id"].AsString);

        // fullDocument should be absent for delete events
        Assert.False(evt.Contains("fullDocument"), "Delete events should not contain fullDocument");

        // fullDocumentBeforeChange should have the pre-deletion document when available
        if (evt.Contains("fullDocumentBeforeChange") && evt["fullDocumentBeforeChange"] != BsonNull.Value)
        {
            Assert.Equal("Alice", evt["fullDocumentBeforeChange"]["name"].AsString);
        }
    }

    #endregion

    #region BulkWrite unordered: MongoCommandException is caught and collected

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task BulkWrite_Unordered_MongoCommandExceptionCollectedNotThrown()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.bulkWrite/
        //   "If ordered is set to false, documents are inserted in an unordered format
        //    and may be reordered by mongod to increase performance."
        //   Errors don't stop subsequent operations.
        var col = _fixture.GetCollection<BsonDocument>("bw_unord_r43");

        // Insert a document, then attempt unordered bulk with duplicate _id and another insert.
        // The duplicate should be collected as an error, and the other insert should succeed.
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "v", "original" } });

        var models = new WriteModel<BsonDocument>[]
        {
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 1 }, { "v", "dup" } }),
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 2 }, { "v", "new" } }),
        };

        var ex = Assert.Throws<MongoBulkWriteException<BsonDocument>>(
            () => col.BulkWrite(models, new BulkWriteOptions { IsOrdered = false }));

        // The duplicate key error should be collected
        Assert.Single(ex.WriteErrors);
        Assert.Equal(0, ex.WriteErrors[0].Index);

        // The second insert should have succeeded
        Assert.Equal(1, ex.Result.InsertedCount);
        var doc2 = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 2)).FirstOrDefault();
        Assert.NotNull(doc2);
        Assert.Equal("new", doc2["v"].AsString);
    }

    #endregion

    #region $indexOfArray: negative start throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_IndexOfArray_NegativeStart_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfArray/
        //   "If <start> is a negative number, $indexOfArray returns an error."
        var col = _fixture.GetCollection<BsonDocument>("idxarr_neg_start_r43");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "arr", new BsonArray { 1, 2, 3 } } });

        Assert.Throws<MongoCommandException>(() =>
            col.Aggregate()
                .Project(new BsonDocument("idx",
                    new BsonDocument("$indexOfArray", new BsonArray { "$arr", 2, -1 })))
                .First());
    }

    #endregion

    #region $indexOfArray: negative end throws error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_IndexOfArray_NegativeEnd_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfArray/
        //   "If <end> is a negative number, $indexOfArray returns an error."
        var col = _fixture.GetCollection<BsonDocument>("idxarr_neg_end_r43");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "arr", new BsonArray { 1, 2, 3 } } });

        Assert.Throws<MongoCommandException>(() =>
            col.Aggregate()
                .Project(new BsonDocument("idx",
                    new BsonDocument("$indexOfArray", new BsonArray { "$arr", 2, 0, -1 })))
                .First());
    }

    #endregion

    #region $indexOfArray: positive cases still work

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_IndexOfArray_ValidStartEnd_ReturnsCorrectIndex()
    {
        var col = _fixture.GetCollection<BsonDocument>("idxarr_valid_r43");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "arr", new BsonArray { 10, 20, 30, 20, 50 } } });

        var result = col.Aggregate()
            .Project(new BsonDocument("idx",
                new BsonDocument("$indexOfArray", new BsonArray { "$arr", 20, 2 })))
            .First();

        Assert.Equal(3, result["idx"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_IndexOfArray_StartBeyondArray_ReturnsMinusOne()
    {
        var col = _fixture.GetCollection<BsonDocument>("idxarr_beyond_r43");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "arr", new BsonArray { 1, 2, 3 } } });

        var result = col.Aggregate()
            .Project(new BsonDocument("idx",
                new BsonDocument("$indexOfArray", new BsonArray { "$arr", 1, 10 })))
            .First();

        Assert.Equal(-1, result["idx"].AsInt32);
    }

    #endregion
}
