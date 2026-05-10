using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

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

    #region Bug 1: $graphLookup doesn't deduplicate results

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task GraphLookup_DeduplicatesResults_WhenMultipleStartValuesMatchSameDocument()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/graphLookup/
        //   "As each document in result set is reached, $graphLookup adds the document
        //    to the working set, if it is not already there."
        var col = _fixture.GetCollection<BsonDocument>("graphlookup_dedup");

        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "start" }, { "refs", new BsonArray { "A", "B" } } },
            new BsonDocument { { "_id", "target" }, { "tags", new BsonArray { "A", "B" } }, { "next", "C" } }
        });

        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument("_id", "start")),
            new("$graphLookup", new BsonDocument
            {
                { "from", "graphlookup_dedup" },
                { "startWith", "$refs" },
                { "connectFromField", "next" },
                { "connectToField", "tags" },
                { "as", "connected" }
            })
        };

        var result = await col.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();

        Assert.NotNull(result);
        var connected = result["connected"].AsBsonArray;
        // The target document should appear exactly once, not twice
        Assert.Single(connected);
        Assert.Equal("target", connected[0].AsBsonDocument["_id"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task GraphLookup_DeduplicatesResults_WhenRecursionReachesDocumentAgain()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/graphLookup/
        //   Results must be deduplicated even when the same document is reachable
        //   from multiple paths in the graph.
        var col = _fixture.GetCollection<BsonDocument>("graphlookup_dedup2");

        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "root" }, { "link", "A" } },
            new BsonDocument { { "_id", "nodeA" }, { "key", "A" }, { "link", "B" } },
            new BsonDocument { { "_id", "nodeB" }, { "key", "B" }, { "link", "A" } } // cycles back to A
        });

        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument("_id", "root")),
            new("$graphLookup", new BsonDocument
            {
                { "from", "graphlookup_dedup2" },
                { "startWith", "$link" },
                { "connectFromField", "link" },
                { "connectToField", "key" },
                { "as", "connected" }
            })
        };

        var result = await col.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();

        Assert.NotNull(result);
        var connected = result["connected"].AsBsonArray;
        // nodeA and nodeB, each appearing exactly once despite cycle
        Assert.Equal(2, connected.Count);
        var ids = connected.Select(c => c.AsBsonDocument["_id"].AsString).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "nodeA", "nodeB" }, ids);
    }

    #endregion

    #region Bug 2: $group $sum overflows BsonInt32

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Group_Sum_PromotesToInt64_WhenSumExceedsInt32Range()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sum/
        //   "Returns a long when the result would overflow the int range."
        var col = _fixture.GetCollection<BsonDocument>("sum_overflow");

        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "group", "A" }, { "value", 1_500_000_000 } },  // BsonInt32
            new BsonDocument { { "group", "A" }, { "value", 1_500_000_000 } },  // BsonInt32
        });

        var pipeline = new BsonDocument[]
        {
            new("$group", new BsonDocument
            {
                { "_id", "$group" },
                { "total", new BsonDocument("$sum", "$value") }
            })
        };

        var result = await col.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();

        Assert.NotNull(result);
        // 1.5B + 1.5B = 3B, exceeds int32 max (2,147,483,647)
        Assert.Equal(3_000_000_000L, result["total"].ToInt64());
        // Should be Int64, not a truncated Int32
        Assert.True(
            result["total"].BsonType == BsonType.Int64 || result["total"].BsonType == BsonType.Double,
            $"Expected Int64 or Double but got {result["total"].BsonType}");
    }

    #endregion

    #region Bug 3: $bucketAuto splits equal values across buckets

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task BucketAuto_DoesNotSplitEqualValues_AcrossBuckets()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/bucketAuto/
        //   Documents with the same groupBy value must stay in the same bucket.
        var col = _fixture.GetCollection<BsonDocument>("bucketauto_equal");

        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "v", 1 } },
            new BsonDocument { { "v", 1 } },
            new BsonDocument { { "v", 1 } },
            new BsonDocument { { "v", 2 } },
            new BsonDocument { { "v", 2 } },
            new BsonDocument { { "v", 3 } },
        });

        var pipeline = new BsonDocument[]
        {
            new("$bucketAuto", new BsonDocument
            {
                { "groupBy", "$v" },
                { "buckets", 3 }
            })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        // All documents with value 1 must be in the same bucket
        // Verify no bucket boundary falls in the middle of equal values
        foreach (var bucket in results)
        {
            var min = bucket["_id"].AsBsonDocument["min"];
            var max = bucket["_id"].AsBsonDocument["max"];
            // Get the count
            var count = bucket["count"].AsInt32;
            Assert.True(count > 0);
        }

        // Total document count across all buckets must equal 6
        var totalCount = results.Sum(r => r["count"].AsInt32);
        Assert.Equal(6, totalCount);

        // Verify that each distinct value appears in exactly one bucket
        // by checking all three 1s are in the same bucket
        var bucketWith1 = results.Where(b =>
        {
            var min = b["_id"].AsBsonDocument["min"].ToInt32();
            var max = b["_id"].AsBsonDocument["max"].ToInt32();
            return min <= 1 && max >= 1;
        }).ToList();

        // Only one bucket should contain value 1
        Assert.Single(bucketWith1);
        // That bucket should contain all three documents with value 1
        Assert.True(bucketWith1[0]["count"].AsInt32 >= 3,
            "All documents with value 1 should be in the same bucket");
    }

    #endregion

    #region Bug 4: $bucket throws when default is explicitly null

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Bucket_WithNullDefault_DoesNotThrow()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/bucket/
        //   "default: A literal that specifies the _id of an additional bucket."
        //   Using null as the default value should be valid.
        var col = _fixture.GetCollection<BsonDocument>("bucket_null_default");

        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "v", 5 } },   // fits in [0, 10)
            new BsonDocument { { "v", 15 } },  // doesn't fit, goes to default
        });

        var pipeline = new BsonDocument[]
        {
            new("$bucket", new BsonDocument
            {
                { "groupBy", "$v" },
                { "boundaries", new BsonArray { 0, 10 } },
                { "default", BsonNull.Value }
            })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Equal(2, results.Count);

        // One bucket for [0, 10) with the document { v: 5 }
        var normalBucket = results.FirstOrDefault(r => r["_id"].AsInt32 == 0);
        Assert.NotNull(normalBucket);
        Assert.Equal(1, normalBucket["count"].AsInt32);

        // One default bucket with _id: null for the document { v: 15 }
        var defaultBucket = results.FirstOrDefault(r => r["_id"] == BsonNull.Value);
        Assert.NotNull(defaultBucket);
        Assert.Equal(1, defaultBucket["count"].AsInt32);
    }

    #endregion
}
