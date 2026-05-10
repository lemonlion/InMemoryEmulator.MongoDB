using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round30BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round30BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Bug 1: $type with array of type specifiers containing "array"

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/type/
    //   "$type can also accept an array of types to check."
    //   { field: { $type: ["array"] } } should match documents where the field is an array.

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Type_ArrayOfTypesContainingArray_MatchesArrayField()
    {
        var col = _fixture.GetCollection<BsonDocument>("type_arr_spec_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", new BsonArray { 1, 2, 3 } } },
            new BsonDocument { { "_id", 2 }, { "val", "hello" } },
            new BsonDocument { { "_id", 3 }, { "val", 42 } }
        });

        // { val: { $type: ["array"] } } — should match doc 1 (val is an array)
        var filter = new BsonDocument("val", new BsonDocument("$type", new BsonArray { "array" }));
        var results = await col.Find(filter).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Type_ArrayOfTypesContainingArrayAndString_MatchesBoth()
    {
        var col = _fixture.GetCollection<BsonDocument>("type_arr_spec_2");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", new BsonArray { 1, 2, 3 } } },
            new BsonDocument { { "_id", 2 }, { "val", "hello" } },
            new BsonDocument { { "_id", 3 }, { "val", 42 } }
        });

        // { val: { $type: ["string", "array"] } } — should match docs 1 and 2
        var filter = new BsonDocument("val", new BsonDocument("$type", new BsonArray { "string", "array" }));
        var results = await col.Find(filter).SortBy(d => d["_id"]).ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0]["_id"].AsInt32);
        Assert.Equal(2, results[1]["_id"].AsInt32);
    }

    #endregion

    #region Bug 2: Distinct with dot-notation through arrays of subdocuments

    // Ref: https://www.mongodb.com/docs/manual/reference/command/distinct/
    //   "If the value of the specified field is an array, distinct considers each element
    //    of the array as a separate value."
    //   Dot-notation traverses arrays: distinct("items.name") on { items: [{name:"a"},{name:"b"}] }
    //   should return ["a", "b"].

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Distinct_DotNotationThroughArrayOfSubdocuments_ReturnsNestedValues()
    {
        var col = _fixture.GetCollection<BsonDocument>("distinct_dot_arr_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "items", new BsonArray
            {
                new BsonDocument("name", "apple"),
                new BsonDocument("name", "banana")
            }}},
            new BsonDocument { { "_id", 2 }, { "items", new BsonArray
            {
                new BsonDocument("name", "banana"),
                new BsonDocument("name", "cherry")
            }}}
        });

        var cursor = await col.DistinctAsync<string>("items.name", Builders<BsonDocument>.Filter.Empty);
        var values = cursor.ToList();

        Assert.Equal(3, values.Count);
        Assert.Contains("apple", values);
        Assert.Contains("banana", values);
        Assert.Contains("cherry", values);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Distinct_DotNotationThroughArrayOfSubdocuments_WithFilter()
    {
        var col = _fixture.GetCollection<BsonDocument>("distinct_dot_arr_2");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "category", "fruit" }, { "items", new BsonArray
            {
                new BsonDocument("name", "apple"),
                new BsonDocument("name", "banana")
            }}},
            new BsonDocument { { "_id", 2 }, { "category", "veggie" }, { "items", new BsonArray
            {
                new BsonDocument("name", "carrot"),
                new BsonDocument("name", "broccoli")
            }}}
        });

        var filter = Builders<BsonDocument>.Filter.Eq("category", "fruit");
        var cursor = await col.DistinctAsync<string>("items.name", filter);
        var values = cursor.ToList();

        Assert.Equal(2, values.Count);
        Assert.Contains("apple", values);
        Assert.Contains("banana", values);
    }

    #endregion

    #region Bug 3: Sort with dot-notation through arrays of subdocuments

    // Ref: https://www.mongodb.com/docs/manual/reference/method/cursor.sort/#sort-asc-desc
    //   "For an ascending sort, comparison of a multi-value field such as an array
    //    to a single value field in another document, the sort picks the least value
    //    of the multi-value field for comparison."

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Sort_DotNotationThroughArrayOfSubdocuments_AscendingByMinValue()
    {
        var col = _fixture.GetCollection<BsonDocument>("sort_dot_arr_1");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "items", new BsonArray
            {
                new BsonDocument("score", 10),
                new BsonDocument("score", 3)
            }}},
            new BsonDocument { { "_id", 2 }, { "items", new BsonArray
            {
                new BsonDocument("score", 1),
                new BsonDocument("score", 7)
            }}},
            new BsonDocument { { "_id", 3 }, { "items", new BsonArray
            {
                new BsonDocument("score", 5),
                new BsonDocument("score", 2)
            }}}
        });

        var sort = Builders<BsonDocument>.Sort.Ascending("items.score");
        var results = await col.Find(FilterDefinition<BsonDocument>.Empty).Sort(sort).ToListAsync();

        // Ascending sort by min value in each array:
        // doc 2 (min=1), doc 3 (min=2), doc 1 (min=3)
        Assert.Equal(2, results[0]["_id"].AsInt32);
        Assert.Equal(3, results[1]["_id"].AsInt32);
        Assert.Equal(1, results[2]["_id"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Sort_DotNotationThroughArrayOfSubdocuments_DescendingByMaxValue()
    {
        var col = _fixture.GetCollection<BsonDocument>("sort_dot_arr_2");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "items", new BsonArray
            {
                new BsonDocument("score", 5),
                new BsonDocument("score", 2)
            }}},
            new BsonDocument { { "_id", 2 }, { "items", new BsonArray
            {
                new BsonDocument("score", 10),
                new BsonDocument("score", 3)
            }}},
            new BsonDocument { { "_id", 3 }, { "items", new BsonArray
            {
                new BsonDocument("score", 1),
                new BsonDocument("score", 7)
            }}}
        });

        var sort = Builders<BsonDocument>.Sort.Descending("items.score");
        var results = await col.Find(FilterDefinition<BsonDocument>.Empty).Sort(sort).ToListAsync();

        // Descending sort by max value in each array:
        // doc 2 (max=10), doc 3 (max=7), doc 1 (max=5)
        Assert.Equal(2, results[0]["_id"].AsInt32);
        Assert.Equal(3, results[1]["_id"].AsInt32);
        Assert.Equal(1, results[2]["_id"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task AggregationSort_DotNotationThroughArrayOfSubdocuments()
    {
        var col = _fixture.GetCollection<BsonDocument>("sort_dot_arr_3");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "items", new BsonArray
            {
                new BsonDocument("score", 10),
                new BsonDocument("score", 3)
            }}},
            new BsonDocument { { "_id", 2 }, { "items", new BsonArray
            {
                new BsonDocument("score", 1),
                new BsonDocument("score", 7)
            }}}
        });

        var pipeline = new[] { new BsonDocument("$sort", new BsonDocument("items.score", 1)) };
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        // Ascending sort by min value: doc 2 (min=1), doc 1 (min=3)
        Assert.Equal(2, results[0]["_id"].AsInt32);
        Assert.Equal(1, results[1]["_id"].AsInt32);
    }

    #endregion
}
