using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round28BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round28BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Bug 1: BulkWrite ordered mode throws wrong exception type

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task BulkWrite_OrderedMode_ThrowsMongoBulkWriteException_WhenDuplicateKey()
    {
        // Ref: https://www.mongodb.com/docs/drivers/csharp/current/fundamentals/crud/write-operations/bulk-write/
        //   "If an error occurs during an ordered bulk operation, the driver throws a
        //    MongoBulkWriteException and does not execute the remaining operations."
        var col = _fixture.GetCollection<BsonDocument>("bw_ordered_exc");

        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", "existing" } });

        var requests = new WriteModel<BsonDocument>[]
        {
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 2 }, { "x", "new" } }),
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 1 }, { "x", "dup" } }), // duplicate
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 3 }, { "x", "never" } }),
        };

        var ex = await Assert.ThrowsAsync<MongoBulkWriteException<BsonDocument>>(
            () => col.BulkWriteAsync(requests, new BulkWriteOptions { IsOrdered = true }));

        // Ordered mode: first insert succeeds, second fails, third is not processed
        Assert.Equal(1, ex.Result.InsertedCount);
        Assert.Single(ex.WriteErrors);
        Assert.Equal(1, ex.WriteErrors[0].Index);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task BulkWrite_OrderedMode_StopsAtFirstError_AndReportsPartialResult()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.bulkWrite/
        //   "With ordered operations, after an error, the remaining operations are not attempted."
        var col = _fixture.GetCollection<BsonDocument>("bw_ordered_partial");

        var requests = new WriteModel<BsonDocument>[]
        {
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 1 } }),
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 2 } }),
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 2 } }), // duplicate of previous
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 3 } }),
        };

        var ex = await Assert.ThrowsAsync<MongoBulkWriteException<BsonDocument>>(
            () => col.BulkWriteAsync(requests, new BulkWriteOptions { IsOrdered = true }));

        Assert.Equal(2, ex.Result.InsertedCount);
        Assert.Single(ex.WriteErrors);
        Assert.Equal(2, ex.WriteErrors[0].Index);
    }

    #endregion

    #region Bug 2: $merge doesn't preserve existing _id

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Merge_WhenMatchedMerge_PreservesExistingId_WithCustomOnField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/merge/
        //   "The _id field in the output collection is immutable."
        var sourceCol = _fixture.GetCollection<BsonDocument>("merge_src");
        var targetCol = _fixture.GetCollection<BsonDocument>("merge_target_merge");

        // Seed target collection
        await targetCol.InsertOneAsync(new BsonDocument
        {
            { "_id", "existing-id" },
            { "name", "Alice" },
            { "score", 80 }
        });

        // Seed source with different _id but same name
        await sourceCol.InsertOneAsync(new BsonDocument
        {
            { "_id", "pipeline-id" },
            { "name", "Alice" },
            { "score", 95 }
        });

        // $merge with on: "name", whenMatched: "merge"
        var pipeline = new BsonDocument[]
        {
            new("$merge", new BsonDocument
            {
                { "into", "merge_target_merge" },
                { "on", "name" },
                { "whenMatched", "merge" },
                { "whenNotMatched", "insert" }
            })
        };

        await sourceCol.AggregateAsync<BsonDocument>(pipeline);

        var result = await targetCol.Find(new BsonDocument("name", "Alice")).FirstOrDefaultAsync();
        Assert.NotNull(result);
        Assert.Equal("existing-id", result["_id"].AsString); // _id must be preserved
        Assert.Equal(95, result["score"].ToInt32()); // score updated from pipeline
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Merge_WhenMatchedReplace_PreservesExistingId_WithCustomOnField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/merge/
        //   "The _id field in the output collection is immutable."
        var sourceCol = _fixture.GetCollection<BsonDocument>("merge_src_repl");
        var targetCol = _fixture.GetCollection<BsonDocument>("merge_target_replace");

        await targetCol.InsertOneAsync(new BsonDocument
        {
            { "_id", "existing-id" },
            { "name", "Bob" },
            { "score", 70 }
        });

        await sourceCol.InsertOneAsync(new BsonDocument
        {
            { "_id", "pipeline-id" },
            { "name", "Bob" },
            { "score", 99 },
            { "extra", "new-field" }
        });

        var pipeline = new BsonDocument[]
        {
            new("$merge", new BsonDocument
            {
                { "into", "merge_target_replace" },
                { "on", "name" },
                { "whenMatched", "replace" },
                { "whenNotMatched", "insert" }
            })
        };

        await sourceCol.AggregateAsync<BsonDocument>(pipeline);

        var result = await targetCol.Find(new BsonDocument("name", "Bob")).FirstOrDefaultAsync();
        Assert.NotNull(result);
        Assert.Equal("existing-id", result["_id"].AsString); // _id must be preserved
        Assert.Equal(99, result["score"].ToInt32());
    }

    #endregion

    #region Bug 3: $fill ignores partitionBy

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Fill_Locf_RespectsPartitionBy_DoesNotCarryAcrossPartitions()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/fill/
        //   "partitionBy: Specifies an expression to group the documents."
        //   locf should fill within each partition independently
        var col = _fixture.GetCollection<BsonDocument>("fill_partition");

        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "group", "A" }, { "val", 10 }, { "seq", 1 } },
            new BsonDocument { { "_id", 2 }, { "group", "A" }, { "seq", 2 } }, // val missing
            new BsonDocument { { "_id", 3 }, { "group", "B" }, { "seq", 3 } }, // val missing — should NOT get val=10 from group A
            new BsonDocument { { "_id", 4 }, { "group", "B" }, { "val", 20 }, { "seq", 4 } },
        });

        var pipeline = new BsonDocument[]
        {
            new("$fill", new BsonDocument
            {
                { "partitionBy", "$group" },
                { "sortBy", new BsonDocument("seq", 1) },
                { "output", new BsonDocument("val", new BsonDocument("method", "locf")) }
            }),
            new("$sort", new BsonDocument("_id", 1))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Equal(4, results.Count);
        Assert.Equal(10, results[0]["val"].ToInt32());    // group A, seq 1: original
        Assert.Equal(10, results[1]["val"].ToInt32());    // group A, seq 2: locf from A's previous
        Assert.False(results[2].Contains("val") && results[2]["val"] != BsonNull.Value,
            "group B, seq 3: no prior value in partition B, val should remain missing/null");
        Assert.Equal(20, results[3]["val"].ToInt32());    // group B, seq 4: original
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Fill_Locf_WithPartitionByFields_RespectsPartitions()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/fill/
        //   "partitionByFields: Specifies an array of fields as the compound key to group the documents."
        var col = _fixture.GetCollection<BsonDocument>("fill_partition_fields");

        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "region", "east" }, { "val", 100 }, { "seq", 1 } },
            new BsonDocument { { "_id", 2 }, { "region", "east" }, { "seq", 2 } }, // val missing
            new BsonDocument { { "_id", 3 }, { "region", "west" }, { "seq", 3 } }, // val missing
            new BsonDocument { { "_id", 4 }, { "region", "west" }, { "val", 200 }, { "seq", 4 } },
        });

        var pipeline = new BsonDocument[]
        {
            new("$fill", new BsonDocument
            {
                { "partitionByFields", new BsonArray { "region" } },
                { "sortBy", new BsonDocument("seq", 1) },
                { "output", new BsonDocument("val", new BsonDocument("method", "locf")) }
            }),
            new("$sort", new BsonDocument("_id", 1))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Equal(4, results.Count);
        Assert.Equal(100, results[0]["val"].ToInt32());   // east, seq 1
        Assert.Equal(100, results[1]["val"].ToInt32());   // east, seq 2: locf
        Assert.False(results[2].Contains("val") && results[2]["val"] != BsonNull.Value,
            "west, seq 3: should not carry from east partition");
        Assert.Equal(200, results[3]["val"].ToInt32());   // west, seq 4
    }

    #endregion
}
