using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 3 bug fix tests covering:
/// 1. CountDocuments with Skip/Limit options
/// 2. $push with negative $position
/// 3. $unset aggregation stage with dot-notation
/// 4. Exclusion projection with dot-notation
/// 5. $strcasecmp normalization to -1/0/1
/// 6. $add with date arithmetic
/// 7. $subtract with date arithmetic
/// 8. $pull with non-operator document conditions on subdocuments
/// 9. FindOneAndUpdate uses _store.Update for correct change type
/// </summary>
[Collection("Integration")]
public class Round3BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round3BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region CountDocuments with Skip/Limit

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task CountDocuments_WithLimit_ReturnsAtMostLimitCount()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/count/
        //   "limit: Optional. The maximum number of matching documents to return."
        var col = _fixture.GetCollection<BsonDocument>("count_limit");
        await col.InsertManyAsync(Enumerable.Range(1, 10)
            .Select(i => new BsonDocument { { "x", i } }));

        var count = await col.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Empty,
            new CountOptions { Limit = 5 });

        Assert.Equal(5, count);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task CountDocuments_WithSkip_SkipsDocuments()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/count/
        //   "skip: Optional. The number of matching documents to skip before returning results."
        var col = _fixture.GetCollection<BsonDocument>("count_skip");
        await col.InsertManyAsync(Enumerable.Range(1, 10)
            .Select(i => new BsonDocument { { "x", i } }));

        var count = await col.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Empty,
            new CountOptions { Skip = 3 });

        Assert.Equal(7, count);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task CountDocuments_WithSkipAndLimit_CombinesBoth()
    {
        var col = _fixture.GetCollection<BsonDocument>("count_skip_limit");
        await col.InsertManyAsync(Enumerable.Range(1, 10)
            .Select(i => new BsonDocument { { "x", i } }));

        var count = await col.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Empty,
            new CountOptions { Skip = 3, Limit = 4 });

        Assert.Equal(4, count);
    }

    #endregion

    #region $push with negative $position

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Push_NegativePosition_InsertsFromEnd()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/position/
        //   "A negative value ... calculates the position relative to the end of the array."
        //   "$position: -1 inserts before the last element."
        var col = _fixture.GetCollection<BsonDocument>("push_neg_pos");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", "p1" },
            { "arr", new BsonArray { 1, 2, 3 } }
        });

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "p1"),
            Builders<BsonDocument>.Update.PushEach("arr", new[] { 99 }, position: -1));

        var doc = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", "p1")).FirstAsync();
        var arr = doc["arr"].AsBsonArray;
        // -1 inserts before last element: [1, 2, 99, 3]
        Assert.Equal(4, arr.Count);
        Assert.Equal(1, arr[0].AsInt32);
        Assert.Equal(2, arr[1].AsInt32);
        Assert.Equal(99, arr[2].AsInt32);
        Assert.Equal(3, arr[3].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Push_NegativePosition_MinusTwo_InsertsTwoFromEnd()
    {
        var col = _fixture.GetCollection<BsonDocument>("push_neg_pos2");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", "p2" },
            { "arr", new BsonArray { 10, 20, 30, 40 } }
        });

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "p2"),
            Builders<BsonDocument>.Update.PushEach("arr", new[] { 99 }, position: -2));

        var doc = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", "p2")).FirstAsync();
        var arr = doc["arr"].AsBsonArray;
        // -2 inserts at position (4 + (-2)) = 2: [10, 20, 99, 30, 40]
        Assert.Equal(5, arr.Count);
        Assert.Equal(10, arr[0].AsInt32);
        Assert.Equal(20, arr[1].AsInt32);
        Assert.Equal(99, arr[2].AsInt32);
        Assert.Equal(30, arr[3].AsInt32);
        Assert.Equal(40, arr[4].AsInt32);
    }

    #endregion

    #region $unset aggregation with dot-notation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Unset_DotNotation_RemovesNestedField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/unset/
        //   "You can use dot notation to unset nested fields."
        var col = _fixture.GetCollection<BsonDocument>("agg_unset_dot");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", "u1" },
            { "address", new BsonDocument { { "city", "London" }, { "zip", "SW1" } } },
            { "name", "Alice" }
        });

        var pipeline = new[] { new BsonDocument("$unset", "address.city") };
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Single(results);
        var addr = results[0]["address"].AsBsonDocument;
        Assert.False(addr.Contains("city"));
        Assert.Equal("SW1", addr["zip"].AsString);
        Assert.Equal("Alice", results[0]["name"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Unset_DotNotation_Array_RemovesMultipleNestedFields()
    {
        var col = _fixture.GetCollection<BsonDocument>("agg_unset_dot2");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", "u2" },
            { "address", new BsonDocument { { "city", "Paris" }, { "zip", "75001" }, { "country", "FR" } } },
            { "name", "Bob" }
        });

        var pipeline = new[] { new BsonDocument("$unset", new BsonArray { "address.city", "address.zip" }) };
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        var addr = results[0]["address"].AsBsonDocument;
        Assert.False(addr.Contains("city"));
        Assert.False(addr.Contains("zip"));
        Assert.Equal("FR", addr["country"].AsString);
    }

    #endregion

    #region Exclusion projection with dot-notation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Find_ExclusionProjection_DotNotation_RemovesNestedField()
    {
        // Ref: https://www.mongodb.com/docs/manual/tutorial/project-fields-from-query-results/
        //   "Use dot notation to suppress fields in embedded documents."
        var col = _fixture.GetCollection<BsonDocument>("proj_excl_dot");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", "e1" },
            { "address", new BsonDocument { { "city", "Berlin" }, { "zip", "10115" } } },
            { "name", "Charlie" }
        });

        var result = await col.Find(Builders<BsonDocument>.Filter.Empty)
            .Project(Builders<BsonDocument>.Projection.Exclude("address.city"))
            .FirstAsync();

        Assert.Equal("Charlie", result["name"].AsString);
        var addr = result["address"].AsBsonDocument;
        Assert.False(addr.Contains("city"));
        Assert.Equal("10115", addr["zip"].AsString);
    }

    #endregion

    #region $strcasecmp normalization

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Strcasecmp_ReturnsNormalized_Minus1_0_1()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/strcasecmp/
        //   "Returns 1, 0, or -1."
        var col = _fixture.GetCollection<BsonDocument>("strcasecmp");
        await col.InsertOneAsync(new BsonDocument { { "_id", "s1" }, { "a", "apple" }, { "b", "banana" } });
        await col.InsertOneAsync(new BsonDocument { { "_id", "s2" }, { "a", "cherry" }, { "b", "cherry" } });
        await col.InsertOneAsync(new BsonDocument { { "_id", "s3" }, { "a", "date" }, { "b", "cherry" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument
            {
                { "cmp", new BsonDocument("$strcasecmp", new BsonArray { "$a", "$b" }) }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        // apple < banana => -1  (exactly -1, not any negative)
        Assert.Equal(-1, results[0]["cmp"].AsInt32);
        // cherry == cherry => 0
        Assert.Equal(0, results[1]["cmp"].AsInt32);
        // date > cherry => 1  (exactly 1, not any positive)
        Assert.Equal(1, results[2]["cmp"].AsInt32);
    }

    #endregion

    #region $add with date arithmetic

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Add_DatePlusMilliseconds_ReturnsDate()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/add/
        //   "If one of the arguments is a date, $add treats the other arguments as milliseconds
        //    to add to the date."
        var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var col = _fixture.GetCollection<BsonDocument>("add_date");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", "d1" },
            { "date", baseDate },
            { "ms", 60000 } // 60 seconds = 1 minute
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument
            {
                { "result", new BsonDocument("$add", new BsonArray { "$date", "$ms" }) }
            })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        var resultDate = results[0]["result"].ToUniversalTime();
        Assert.Equal(baseDate.AddMilliseconds(60000), resultDate);
    }

    #endregion

    #region $subtract with date arithmetic

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Subtract_DateMinusMilliseconds_ReturnsDate()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/subtract/
        //   "If the two values are a date and a number... subtracts the number, in milliseconds, from the date."
        var baseDate = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var col = _fixture.GetCollection<BsonDocument>("sub_date_num");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", "sd1" },
            { "date", baseDate },
            { "ms", 3600000 } // 1 hour in ms
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument
            {
                { "result", new BsonDocument("$subtract", new BsonArray { "$date", "$ms" }) }
            })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        var resultDate = results[0]["result"].ToUniversalTime();
        Assert.Equal(baseDate.AddMilliseconds(-3600000), resultDate);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Subtract_DateMinusDate_ReturnsMilliseconds()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/subtract/
        //   "If the two values are dates, return the difference in milliseconds."
        var date1 = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var date2 = new DateTime(2024, 6, 15, 11, 0, 0, DateTimeKind.Utc);
        var col = _fixture.GetCollection<BsonDocument>("sub_date_date");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", "sd2" },
            { "date1", date1 },
            { "date2", date2 }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument
            {
                { "diff", new BsonDocument("$subtract", new BsonArray { "$date1", "$date2" }) }
            })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();
        // 1 hour = 3,600,000 milliseconds
        Assert.Equal(3600000, results[0]["diff"].ToDouble());
    }

    #endregion

    #region $pull with non-operator document conditions

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Pull_WithFieldCondition_MatchesSubdocumentsByField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/pull/
        //   "To specify a <condition> you can use the query filters."
        //   e.g. $pull: { items: { size: "small" } } removes subdocs where size == "small"
        var col = _fixture.GetCollection<BsonDocument>("pull_subdoc");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", "pr1" },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "A" }, { "size", "small" } },
                    new BsonDocument { { "name", "B" }, { "size", "large" } },
                    new BsonDocument { { "name", "C" }, { "size", "small" } },
                }
            }
        });

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "pr1"),
            Builders<BsonDocument>.Update.PullFilter<BsonDocument>("items",
                Builders<BsonDocument>.Filter.Eq("size", "small")));

        var doc = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", "pr1")).FirstAsync();
        var items = doc["items"].AsBsonArray;
        Assert.Single(items);
        Assert.Equal("B", items[0]["name"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Pull_RawDocCondition_MatchesSubdocumentsByField()
    {
        // Same test but using raw BsonDocument update (not PullFilter)
        // This triggers the non-operator document condition path in ShouldPull
        var col = _fixture.GetCollection<BsonDocument>("pull_subdoc_raw");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", "pr2" },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "X" }, { "status", "done" } },
                    new BsonDocument { { "name", "Y" }, { "status", "pending" } },
                    new BsonDocument { { "name", "Z" }, { "status", "done" } },
                }
            }
        });

        // Use raw update document: { $pull: { items: { status: "done" } } }
        var update = new BsonDocument("$pull", new BsonDocument("items", new BsonDocument("status", "done")));
        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "pr2"),
            new BsonDocumentUpdateDefinition<BsonDocument>(update));

        var doc = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", "pr2")).FirstAsync();
        var items = doc["items"].AsBsonArray;
        Assert.Single(items);
        Assert.Equal("Y", items[0]["name"].AsString);
    }

    #endregion
}
