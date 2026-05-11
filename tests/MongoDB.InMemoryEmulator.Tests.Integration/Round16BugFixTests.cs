using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round16BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round16BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Distinct null handling

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Distinct_ExplicitNullValues_IncludesNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/distinct/
        //   "If the value of the specified field is null, distinct returns null as one of the distinct values."
        var col = _fixture.GetCollection<BsonDocument>("distinct_null");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "status", "active" } },
            new BsonDocument { { "_id", 2 }, { "status", BsonNull.Value } },
            new BsonDocument { { "_id", 3 }, { "status", "inactive" } },
            new BsonDocument { { "_id", 4 }, { "status", "active" } },
        });

        var values = await col.DistinctAsync<BsonValue>("status", Builders<BsonDocument>.Filter.Empty);
        var results = await values.ToListAsync();

        // Should include null, "active", "inactive" (3 distinct values)
        Assert.Equal(3, results.Count);
        Assert.Contains(results, v => v == BsonNull.Value || v.IsBsonNull);
        Assert.Contains(results, v => v.IsString && v.AsString == "active");
        Assert.Contains(results, v => v.IsString && v.AsString == "inactive");
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Distinct_MissingField_DoesNotIncludeNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/distinct/
        //   Real MongoDB does NOT include null for documents where the field is missing.
        //   Only explicitly null-valued fields contribute a null value.
        var col = _fixture.GetCollection<BsonDocument>("distinct_missing");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 10 } },
            new BsonDocument { { "_id", 2 }, { "y", 20 } }, // missing field "x"
            new BsonDocument { { "_id", 3 }, { "x", 30 } },
        });

        var values = await col.DistinctAsync<BsonValue>("x", Builders<BsonDocument>.Filter.Empty);
        var results = await values.ToListAsync();

        // Should include 10, 30 only — missing field does NOT contribute null
        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, v => v == BsonNull.Value || v.IsBsonNull);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Distinct_NullAndMissing_ReturnsOnlyOneNull()
    {
        // Both explicit null and missing field contribute a single null
        var col = _fixture.GetCollection<BsonDocument>("distinct_null_missing");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "v", 1 } },
            new BsonDocument { { "_id", 2 }, { "v", BsonNull.Value } }, // explicit null
            new BsonDocument { { "_id", 3 } }, // missing field
            new BsonDocument { { "_id", 4 }, { "v", 2 } },
        });

        var values = await col.DistinctAsync<BsonValue>("v", Builders<BsonDocument>.Filter.Empty);
        var results = await values.ToListAsync();

        // Should have: 1, null, 2 (null appears once for both explicit null and missing)
        Assert.Equal(3, results.Count);
        Assert.Single(results, v => v == BsonNull.Value || v.IsBsonNull);
    }

    #endregion

    #region Distinct with array element null

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Distinct_ArrayWithNullElements_IncludesNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/distinct/
        //   "If the value of the specified field is an array, distinct considers each element
        //    of the array as a separate value."
        var col = _fixture.GetCollection<BsonDocument>("distinct_arr_null");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "tags", new BsonArray { "a", BsonNull.Value, "b" } } },
            new BsonDocument { { "_id", 2 }, { "tags", new BsonArray { "b", "c" } } },
        });

        var values = await col.DistinctAsync<BsonValue>("tags", Builders<BsonDocument>.Filter.Empty);
        var results = await values.ToListAsync();

        // Should include: "a", null, "b", "c" (4 distinct values)
        Assert.Equal(4, results.Count);
        Assert.Contains(results, v => v == BsonNull.Value || v.IsBsonNull);
    }

    #endregion

    #region $group $push accumulator includes null/missing values

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Group_PushAccumulator_IncludesMissingAsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/push/
        //   "$push returns an array of all values for each group including duplicates."
        //   Missing fields are included as null.
        var col = _fixture.GetCollection<BsonDocument>("group_push_null");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "cat", "A" }, { "val", 10 } },
            new BsonDocument { { "_id", 2 }, { "cat", "A" } }, // missing "val"
            new BsonDocument { { "_id", 3 }, { "cat", "A" }, { "val", 30 } },
        });

        var pipeline = new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$cat" },
                { "values", new BsonDocument("$push", "$val") }
            })
        };

        var results = await col.AggregateAsync<BsonDocument>(pipeline);
        var result = await results.ToListAsync();

        Assert.Single(result);
        var values = result[0]["values"].AsBsonArray;
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/push/
        //   Real MongoDB $push does NOT include a value for documents where the field is missing.
        Assert.Equal(2, values.Count);
        Assert.DoesNotContain(values, v => v == BsonNull.Value || v.IsBsonNull);
    }

    #endregion

    #region ListDatabaseNames excludes empty databases

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ListDatabaseNames_ExcludesEmptyDatabases()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/listDatabases/
        //   "By default, MongoDB does not include empty databases in the output."
        var client = _fixture.Client;
        var dbName = $"temp_empty_db_test_{Guid.NewGuid():N}";

        // Create a database with data then drop the collection
        var db = client.GetDatabase(dbName);
        var col = db.GetCollection<BsonDocument>("temp_col");
        await col.InsertOneAsync(new BsonDocument("x", 1));
        await db.DropCollectionAsync("temp_col");

        // The empty database should not appear in list
        var dbNames = await client.ListDatabaseNamesAsync();
        var names = await dbNames.ToListAsync();
        Assert.DoesNotContain(dbName, names);
    }

    #endregion
}
