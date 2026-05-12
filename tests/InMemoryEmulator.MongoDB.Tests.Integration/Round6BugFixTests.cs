using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 6 bug fix tests covering:
/// 1. $unwind treats scalar values as single-element arrays (not drop)
/// 2. $unwind preserveNullAndEmptyArrays with nested dot-notation paths
/// 3. $group $sum preserves integer types (Int32/Int64) when all inputs are integers
/// 4. Aggregation $project inclusion with dot-notation creates nested structure
/// 5. Aggregation $project exclusion with dot-notation removes nested fields
/// </summary>
[Collection("Integration")]
public class Round6BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round6BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Bug 1: $unwind scalar values

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Unwind_ScalarValue_TreatsAsSingleElementArray()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/unwind/
        //   "If the operand does not resolve to an array but is not missing, null, or an empty array,
        //    $unwind treats the operand as a single element array."
        var col = _fixture.GetCollection<BsonDocument>("unwind_scalar");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "item", "A" }, { "tags", new BsonArray { "x", "y" } } },
            new BsonDocument { { "_id", 2 }, { "item", "B" }, { "tags", "single" } },
        });

        var pipeline = new[] { new BsonDocument("$unwind", "$tags") };
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        // Doc 1 produces 2 results (array unwound), doc 2 produces 1 result (scalar treated as array)
        Assert.Equal(3, results.Count);
        var scalarDoc = results.First(r => r["_id"] == 2);
        Assert.Equal("single", scalarDoc["tags"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Unwind_ScalarSubdocument_TreatsAsSingleElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/unwind/
        var col = _fixture.GetCollection<BsonDocument>("unwind_scalar_doc");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "data", new BsonDocument("x", 1) }
        });

        var pipeline = new[] { new BsonDocument("$unwind", "$data") };
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0]["data"]["x"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Unwind_ScalarInteger_TreatsAsSingleElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/unwind/
        var col = _fixture.GetCollection<BsonDocument>("unwind_scalar_int");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", 42 } });

        var pipeline = new[] { new BsonDocument("$unwind", "$val") };
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Single(results);
        Assert.Equal(42, results[0]["val"].AsInt32);
    }

    #endregion

    #region Bug 2: $unwind preserveNullAndEmptyArrays with dot-notation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Unwind_PreserveEmpty_NestedDotPath_RemovesField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/unwind/
        //   "preserveNullAndEmptyArrays: If true, output documents even if path is null/missing/empty."
        var col = _fixture.GetCollection<BsonDocument>("unwind_nested_dot");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "a", new BsonDocument("items", new BsonArray { 10, 20 }) } },
            new BsonDocument { { "_id", 2 }, { "a", new BsonDocument("items", new BsonArray()) } },
        });

        var pipeline = new[]
        {
            new BsonDocument("$unwind", new BsonDocument
            {
                { "path", "$a.items" },
                { "preserveNullAndEmptyArrays", true }
            })
        };
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        // Doc 1: 2 docs (unwound array)
        // Doc 2: 1 doc with a.items removed (empty array + preserve)
        Assert.Equal(3, results.Count);
        var emptyDoc = results.First(r => r["_id"] == 2);
        Assert.True(emptyDoc.Contains("a"));
        Assert.False(emptyDoc["a"].AsBsonDocument.Contains("items"));
    }

    #endregion

    #region Bug 3: $group $sum type preservation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Group_Sum_PreservesInt32Type()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sum/
        //   "Returns an integer when all values are integers."
        var col = _fixture.GetCollection<BsonDocument>("group_sum_int");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "category", "A" }, { "qty", 10 } },
            new BsonDocument { { "_id", 2 }, { "category", "A" }, { "qty", 20 } },
            new BsonDocument { { "_id", 3 }, { "category", "A" }, { "qty", 30 } },
        });

        var pipeline = new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$category" },
                { "total", new BsonDocument("$sum", "$qty") }
            })
        };
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Single(results);
        // Sum of Int32 values should be Int32 or Int64, NOT Double
        var totalType = results[0]["total"].BsonType;
        Assert.True(totalType == BsonType.Int32 || totalType == BsonType.Int64,
            $"Expected Int32 or Int64 but got {totalType}");
        Assert.Equal(60, results[0]["total"].ToInt32());
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Group_Sum_ReturnsDoubleWhenMixed()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sum/
        //   "Returns a double when any value is a double."
        var col = _fixture.GetCollection<BsonDocument>("group_sum_mixed");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "category", "A" }, { "qty", 10 } },
            new BsonDocument { { "_id", 2 }, { "category", "A" }, { "qty", 20.5 } },
        });

        var pipeline = new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$category" },
                { "total", new BsonDocument("$sum", "$qty") }
            })
        };
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Single(results);
        Assert.Equal(BsonType.Double, results[0]["total"].BsonType);
        Assert.Equal(30.5, results[0]["total"].AsDouble);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Group_Sum_Int64_PreservesLongType()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sum/
        //   "Returns a long when any value is a long."
        var col = _fixture.GetCollection<BsonDocument>("group_sum_long");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "category", "A" }, { "qty", (long)100 } },
            new BsonDocument { { "_id", 2 }, { "category", "A" }, { "qty", 200 } },
        });

        var pipeline = new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$category" },
                { "total", new BsonDocument("$sum", "$qty") }
            })
        };
        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Single(results);
        Assert.Equal(BsonType.Int64, results[0]["total"].BsonType);
        Assert.Equal(300L, results[0]["total"].AsInt64);
    }

    #endregion

    #region Bug 4: Aggregation $project inclusion with dot-notation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Project_InclusionDotNotation_CreatesNestedStructure()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/project/
        //   "You can use dot notation to include fields in embedded documents."
        var col = _fixture.GetCollection<BsonDocument>("proj_dot_incl");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonDocument { { "b", 10 }, { "c", 20 }, { "d", 30 } } },
            { "x", 99 }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument { { "a.b", 1 } })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Single(results);
        // Should produce { _id: 1, a: { b: 10 } }  NOT { _id: 1, "a.b": 10 }
        Assert.True(results[0].Contains("a"), "Should have 'a' field");
        Assert.True(results[0]["a"].IsBsonDocument, "'a' should be a BsonDocument");
        Assert.Equal(10, results[0]["a"]["b"].AsInt32);
        Assert.False(results[0]["a"].AsBsonDocument.Contains("c"), "'c' should be excluded");
        Assert.False(results[0].Contains("x"), "'x' should be excluded in inclusion mode");
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Project_InclusionDotNotation_DeepNesting()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/project/
        var col = _fixture.GetCollection<BsonDocument>("proj_dot_deep");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonDocument("b", new BsonDocument { { "c", 100 }, { "d", 200 } }) }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument { { "a.b.c", 1 } })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Single(results);
        Assert.True(results[0]["a"].IsBsonDocument);
        Assert.True(results[0]["a"]["b"].IsBsonDocument);
        Assert.Equal(100, results[0]["a"]["b"]["c"].AsInt32);
        Assert.False(results[0]["a"]["b"].AsBsonDocument.Contains("d"));
    }

    #endregion

    #region Bug 5: Aggregation $project exclusion with dot-notation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Project_ExclusionDotNotation_RemovesNestedField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/project/
        //   "You can use dot notation to exclude fields in embedded documents."
        var col = _fixture.GetCollection<BsonDocument>("proj_dot_excl");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonDocument { { "b", 10 }, { "c", 20 } } },
            { "x", 99 }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument { { "a.b", 0 } })
        };

        var results = await col.Aggregate<BsonDocument>(pipeline).ToListAsync();

        Assert.Single(results);
        // Should produce { _id: 1, a: { c: 20 }, x: 99 }
        Assert.True(results[0].Contains("a"));
        Assert.False(results[0]["a"].AsBsonDocument.Contains("b"), "'a.b' should be excluded");
        Assert.Equal(20, results[0]["a"]["c"].AsInt32);
        Assert.Equal(99, results[0]["x"].AsInt32);
    }

    #endregion
}
