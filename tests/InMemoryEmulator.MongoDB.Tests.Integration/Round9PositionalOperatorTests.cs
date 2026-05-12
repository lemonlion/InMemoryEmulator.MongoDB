using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 9 tests covering positional update operators:
/// 1. $ (positional) — updates matched array element
/// 2. $[] (all positional) — updates all array elements
/// 3. $[identifier] (filtered positional with arrayFilters)
/// </summary>
[Collection("Integration")]
public class Round9PositionalOperatorTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round9PositionalOperatorTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $ (positional) operator

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Positional_Set_UpdatesMatchedArrayElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional/
        //   "The positional $ operator identifies an element in an array to update
        //    without explicitly specifying the position of the element in the array."
        var col = _fixture.GetCollection<BsonDocument>("pos_set");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "grades", new BsonArray { 80, 85, 90 } }
        });

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Filter.Eq("grades", 85));

        var update = Builders<BsonDocument>.Update.Set("grades.$", 95);
        await col.UpdateOneAsync(filter, update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var grades = result["grades"].AsBsonArray;
        Assert.Equal(new BsonArray { 80, 95, 90 }, grades);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Positional_Set_UpdatesMatchedSubdocumentElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional/
        var col = _fixture.GetCollection<BsonDocument>("pos_subdoc");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "apple" }, { "qty", 5 } },
                    new BsonDocument { { "name", "banana" }, { "qty", 10 } },
                    new BsonDocument { { "name", "cherry" }, { "qty", 15 } },
                }
            }
        });

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Filter.Eq("items.name", "banana"));

        var update = Builders<BsonDocument>.Update.Set("items.$.qty", 20);
        await col.UpdateOneAsync(filter, update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.Equal(5, items[0]["qty"].AsInt32);
        Assert.Equal(20, items[1]["qty"].AsInt32);
        Assert.Equal(15, items[2]["qty"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Positional_Inc_IncrementsMatchedElement()
    {
        var col = _fixture.GetCollection<BsonDocument>("pos_inc");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 10, 20, 30 } }
        });

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Filter.Eq("scores", 20));

        var update = Builders<BsonDocument>.Update.Inc("scores.$", 5);
        await col.UpdateOneAsync(filter, update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(new BsonArray { 10, 25, 30 }, result["scores"].AsBsonArray);
    }

    #endregion

    #region $[] (all positional) operator

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task AllPositional_Inc_AllElements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional-all/
        //   "The all positional operator $[] indicates that the update operator should modify
        //    all elements in the specified array field."
        var col = _fixture.GetCollection<BsonDocument>("pos_all_inc");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "grades", new BsonArray { 80, 85, 90 } }
        });

        var update = Builders<BsonDocument>.Update.Inc("grades.$[]", 10);
        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(new BsonArray { 90, 95, 100 }, result["grades"].AsBsonArray);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task AllPositional_Set_NestedField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional-all/
        var col = _fixture.GetCollection<BsonDocument>("pos_all_nested");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "active", false } },
                    new BsonDocument { { "name", "b" }, { "active", false } },
                }
            }
        });

        var update = Builders<BsonDocument>.Update.Set("items.$[].active", true);
        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.True(items[0]["active"].AsBoolean);
        Assert.True(items[1]["active"].AsBoolean);
    }

    #endregion

    #region $[<identifier>] (filtered positional) operator

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task FilteredPositional_UpdateMatchingElements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional-filtered/
        //   "The filtered positional operator $[<identifier>] identifies the array elements
        //    that match the arrayFilters conditions for an update operation."
        var col = _fixture.GetCollection<BsonDocument>("pos_filtered");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "grades", new BsonArray { 80, 45, 90, 30 } }
        });

        var update = Builders<BsonDocument>.Update.Set("grades.$[elem]", 50);
        var options = new UpdateOptions
        {
            ArrayFilters = new[]
            {
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("elem", new BsonDocument("$lt", 50)))
            }
        };

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1), update, options);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(new BsonArray { 80, 50, 90, 50 }, result["grades"].AsBsonArray);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task FilteredPositional_NestedSubdocuments()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional-filtered/
        var col = _fixture.GetCollection<BsonDocument>("pos_filtered_nested");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "status", "pending" } },
                    new BsonDocument { { "name", "b" }, { "status", "done" } },
                    new BsonDocument { { "name", "c" }, { "status", "pending" } },
                }
            }
        });

        var update = Builders<BsonDocument>.Update.Set("items.$[i].status", "completed");
        var options = new UpdateOptions
        {
            ArrayFilters = new[]
            {
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("i.status", "pending"))
            }
        };

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1), update, options);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.Equal("completed", items[0]["status"].AsString);
        Assert.Equal("done", items[1]["status"].AsString);
        Assert.Equal("completed", items[2]["status"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task FilteredPositional_Inc_MatchingElements()
    {
        var col = _fixture.GetCollection<BsonDocument>("pos_filtered_inc");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 10, 60, 30, 70 } }
        });

        var update = Builders<BsonDocument>.Update.Inc("scores.$[s]", 100);
        var options = new UpdateOptions
        {
            ArrayFilters = new[]
            {
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("s", new BsonDocument("$gte", 50)))
            }
        };

        await col.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1), update, options);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(new BsonArray { 10, 160, 30, 170 }, result["scores"].AsBsonArray);
    }

    #endregion

    #region UpdateMany with positional operators

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task UpdateMany_AllPositional_AcrossMultipleDocs()
    {
        var col = _fixture.GetCollection<BsonDocument>("pos_many");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "vals", new BsonArray { 1, 2, 3 } } },
            new BsonDocument { { "_id", 2 }, { "vals", new BsonArray { 4, 5, 6 } } },
        });

        var update = Builders<BsonDocument>.Update.Mul("vals.$[]", 10);
        await col.UpdateManyAsync(
            Builders<BsonDocument>.Filter.Empty, update);

        var doc1 = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var doc2 = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 2)).FirstAsync();
        Assert.Equal(new BsonArray { 10, 20, 30 }, doc1["vals"].AsBsonArray);
        Assert.Equal(new BsonArray { 40, 50, 60 }, doc2["vals"].AsBsonArray);
    }

    #endregion
}
