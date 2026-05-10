using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Tests for array field matching with comparison and other operators.
/// In MongoDB, when a field contains an array, operators like $gt, $lt, etc.
/// match if at least one element satisfies the condition (implicit $or over elements).
/// </summary>
/// <remarks>
/// Ref: https://www.mongodb.com/docs/manual/tutorial/query-arrays/
///   "When the field holds an array, operators match the document if at least
///    one array element meets the condition."
/// </remarks>
[Collection("Integration")]
public class ArrayFieldMatchingTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public ArrayFieldMatchingTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Comparison Operators with Array Fields

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Gt_OnArrayField_MatchesIfAnyElementSatisfies()
    {
        // Ref: https://www.mongodb.com/docs/manual/tutorial/query-arrays/
        //   "the operation queries for all documents where the value of the field
        //    is an array that contains at least one element greater than the specified value."
        var col = _fixture.GetCollection<BsonDocument>("arr_gt_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "scores", new BsonArray { 1, 10, 3 } } },
            new BsonDocument { { "_id", 2 }, { "scores", new BsonArray { 1, 2, 3 } } },
            new BsonDocument { { "_id", 3 }, { "scores", 15 } } // scalar field
        });

        var filter = Builders<BsonDocument>.Filter.Gt("scores", 5);
        var results = await col.Find(filter).ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r["_id"] == 1); // array contains 10 > 5
        Assert.Contains(results, r => r["_id"] == 3); // scalar 15 > 5
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Gt_OnArrayField_NoMatchWhenNoElementSatisfies()
    {
        var col = _fixture.GetCollection<BsonDocument>("arr_gt_2");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "scores", new BsonArray { 1, 2, 3 } } },
            new BsonDocument { { "_id", 2 }, { "scores", new BsonArray { 4, 5 } } }
        });

        var filter = Builders<BsonDocument>.Filter.Gt("scores", 50);
        var results = await col.Find(filter).ToListAsync();

        Assert.Empty(results);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Gte_OnArrayField_MatchesIfAnyElementSatisfies()
    {
        var col = _fixture.GetCollection<BsonDocument>("arr_gte_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "scores", new BsonArray { 1, 5, 3 } } },
            new BsonDocument { { "_id", 2 }, { "scores", new BsonArray { 1, 2, 3 } } }
        });

        var filter = Builders<BsonDocument>.Filter.Gte("scores", 5);
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Lt_OnArrayField_MatchesIfAnyElementSatisfies()
    {
        var col = _fixture.GetCollection<BsonDocument>("arr_lt_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "scores", new BsonArray { 10, 20, 30 } } },
            new BsonDocument { { "_id", 2 }, { "scores", new BsonArray { 50, 60, 70 } } }
        });

        var filter = Builders<BsonDocument>.Filter.Lt("scores", 15);
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32); // contains 10 < 15
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Lte_OnArrayField_MatchesIfAnyElementSatisfies()
    {
        var col = _fixture.GetCollection<BsonDocument>("arr_lte_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "scores", new BsonArray { 10, 20, 30 } } },
            new BsonDocument { { "_id", 2 }, { "scores", new BsonArray { 50, 60, 70 } } }
        });

        var filter = Builders<BsonDocument>.Filter.Lte("scores", 10);
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32); // contains 10 <= 10
    }

    #endregion

    #region Regex with Array Fields

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Regex_OnArrayField_MatchesIfAnyElementMatches()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/regex/
        //   "If the field contains an array, $regex selects only the documents
        //    where at least one element matches the expression."
        var col = _fixture.GetCollection<BsonDocument>("arr_regex_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "tags", new BsonArray { "hello", "world" } } },
            new BsonDocument { { "_id", 2 }, { "tags", new BsonArray { "foo", "bar" } } },
            new BsonDocument { { "_id", 3 }, { "tags", "hello_there" } } // scalar
        });

        var filter = Builders<BsonDocument>.Filter.Regex("tags", new BsonRegularExpression("^hello"));
        var results = await col.Find(filter).ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r["_id"] == 1);
        Assert.Contains(results, r => r["_id"] == 3);
    }

    #endregion

    #region $mod with Array Fields

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Mod_OnArrayField_MatchesIfAnyElementSatisfies()
    {
        var col = _fixture.GetCollection<BsonDocument>("arr_mod_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "values", new BsonArray { 1, 6, 7 } } },
            new BsonDocument { { "_id", 2 }, { "values", new BsonArray { 2, 4, 8 } } }
        });

        var filter = Builders<BsonDocument>.Filter.Mod("values", 3, 0);
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32); // contains 6 which is 6 % 3 == 0
    }

    #endregion

    #region Dot-Notation Through Arrays

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task DotNotation_ThroughArray_MatchesElementFields()
    {
        // Ref: https://www.mongodb.com/docs/manual/tutorial/query-array-of-documents/
        //   "Use dot notation to query for a field nested in an array of documents."
        var col = _fixture.GetCollection<BsonDocument>("arr_dot_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "items", new BsonArray {
                new BsonDocument { { "name", "apple" }, { "qty", 5 } },
                new BsonDocument { { "name", "banana" }, { "qty", 10 } }
            }}},
            new BsonDocument { { "_id", 2 }, { "items", new BsonArray {
                new BsonDocument { { "name", "cherry" }, { "qty", 20 } }
            }}}
        });

        var filter = Builders<BsonDocument>.Filter.Eq("items.name", "apple");
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task DotNotation_ThroughArray_GtOnNestedField()
    {
        // Ref: https://www.mongodb.com/docs/manual/tutorial/query-array-of-documents/
        var col = _fixture.GetCollection<BsonDocument>("arr_dot_2");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "items", new BsonArray {
                new BsonDocument { { "name", "apple" }, { "qty", 5 } },
                new BsonDocument { { "name", "banana" }, { "qty", 10 } }
            }}},
            new BsonDocument { { "_id", 2 }, { "items", new BsonArray {
                new BsonDocument { { "name", "cherry" }, { "qty", 3 } }
            }}}
        });

        var filter = Builders<BsonDocument>.Filter.Gt("items.qty", 7);
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32); // banana has qty 10 > 7
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task DotNotation_ThroughArray_NumericIndex()
    {
        // Ref: https://www.mongodb.com/docs/manual/tutorial/query-array-of-documents/
        //   "Using dot notation, you can specify query conditions for field in a document
        //    at a particular index or position of the array."
        var col = _fixture.GetCollection<BsonDocument>("arr_dot_3");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "items", new BsonArray {
            new BsonDocument { { "name", "apple" }, { "qty", 5 } },
            new BsonDocument { { "name", "banana" }, { "qty", 10 } }
        }}});

        var filter = Builders<BsonDocument>.Filter.Eq("items.0.name", "apple");
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    #endregion

    #region Sort with Array Fields

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Sort_Ascending_OnArrayField_UseMinElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/cursor.sort/#sort-asc-desc
        //   "For an ascending sort, comparison of a multi-value field such as an array
        //    to a single-value field in another document, the sort picks the least value
        //    of the multi-value field for comparison."
        var col = _fixture.GetCollection<BsonDocument>("arr_sort_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "scores", new BsonArray { 50, 10, 30 } } }, // min=10
            new BsonDocument { { "_id", 2 }, { "scores", new BsonArray { 20, 40 } } },      // min=20
            new BsonDocument { { "_id", 3 }, { "scores", 15 } }                              // scalar=15
        });

        var results = await col.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("scores"))
            .ToListAsync();

        // Ascending by min element: doc1(10) < doc3(15) < doc2(20)
        Assert.Equal(1, results[0]["_id"].AsInt32);
        Assert.Equal(3, results[1]["_id"].AsInt32);
        Assert.Equal(2, results[2]["_id"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Sort_Descending_OnArrayField_UseMaxElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/cursor.sort/#sort-asc-desc
        //   "For a descending sort, comparison of a multi-value field such as an array
        //    to a single-value field in another document, the sort picks the greatest value."
        var col = _fixture.GetCollection<BsonDocument>("arr_sort_2");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "scores", new BsonArray { 50, 10, 30 } } }, // max=50
            new BsonDocument { { "_id", 2 }, { "scores", new BsonArray { 20, 40 } } },      // max=40
            new BsonDocument { { "_id", 3 }, { "scores", 45 } }                              // scalar=45
        });

        var results = await col.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("scores"))
            .ToListAsync();

        // Descending by max element: doc1(50) > doc3(45) > doc2(40)
        Assert.Equal(1, results[0]["_id"].AsInt32);
        Assert.Equal(3, results[1]["_id"].AsInt32);
        Assert.Equal(2, results[2]["_id"].AsInt32);
    }

    #endregion

    #region $type Missing Aliases

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Type_Timestamp_MatchesTimestampFields()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/type/
        //   "Available Types: ... 'timestamp' (17)"
        var col = _fixture.GetCollection<BsonDocument>("type_ts_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "ts", new BsonTimestamp(1234567890, 1) } },
            new BsonDocument { { "_id", 2 }, { "ts", new BsonDateTime(DateTime.UtcNow) } }
        });

        var filter = new BsonDocument("ts", new BsonDocument("$type", "timestamp"));
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Type_MinKey_MatchesMinKeyFields()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/type/
        //   "Available Types: ... 'minKey' (-1)"
        var col = _fixture.GetCollection<BsonDocument>("type_mk_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", BsonMinKey.Value } },
            new BsonDocument { { "_id", 2 }, { "val", 42 } }
        });

        var filter = new BsonDocument("val", new BsonDocument("$type", "minKey"));
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Type_MaxKey_MatchesMaxKeyFields()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/type/
        var col = _fixture.GetCollection<BsonDocument>("type_maxk_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", BsonMaxKey.Value } },
            new BsonDocument { { "_id", 2 }, { "val", 42 } }
        });

        var filter = new BsonDocument("val", new BsonDocument("$type", "maxKey"));
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Type_JavaScript_MatchesJavaScriptFields()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/type/
        //   "Available Types: ... 'javascript' (13)"
        var col = _fixture.GetCollection<BsonDocument>("type_js_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "code", new BsonJavaScript("function() { return 1; }") } },
            new BsonDocument { { "_id", 2 }, { "code", "not javascript" } }
        });

        var filter = new BsonDocument("code", new BsonDocument("$type", "javascript"));
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Type_ArrayAlias_OnArrayWithComparison()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/type/
        //   "$type with array matches if ANY element has the specified type"
        var col = _fixture.GetCollection<BsonDocument>("type_arr_elem_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", new BsonArray { 1, "hello", true } } },
            new BsonDocument { { "_id", 2 }, { "val", new BsonArray { 1, 2, 3 } } }
        });

        var filter = new BsonDocument("val", new BsonDocument("$type", "string"));
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32); // has a string element
    }

    #endregion
}
