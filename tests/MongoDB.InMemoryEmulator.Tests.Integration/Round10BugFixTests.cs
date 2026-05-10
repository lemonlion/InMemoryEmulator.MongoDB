using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 10 tests covering:
/// 1. Array operators ($push/$pull/$addToSet/$pop) with positional operators
/// 2. BulkWrite forwarding arrayFilters from UpdateOneModel/UpdateManyModel
/// 3. $rename with positional operators (should throw appropriate error)
/// </summary>
[Collection("Integration")]
public class Round10BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round10BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $push with positional operators

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Push_PositionalDollar_AddsToMatchedSubdocArray()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/push/
        //   "$push appends a specified value to an array."
        // Combined with positional $, it should push into the matched element's sub-array.
        var col = _fixture.GetCollection<BsonDocument>("push_positional");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "apple" }, { "tags", new BsonArray { "fruit" } } },
                    new BsonDocument { { "name", "carrot" }, { "tags", new BsonArray { "vegetable" } } },
                }
            }
        });

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Filter.Eq("items.name", "carrot"));

        var update = Builders<BsonDocument>.Update.Push("items.$.tags", "orange");
        await col.UpdateOneAsync(filter, update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.Equal(new BsonArray { "fruit" }, items[0]["tags"].AsBsonArray);
        Assert.Equal(new BsonArray { "vegetable", "orange" }, items[1]["tags"].AsBsonArray);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Push_AllPositional_AddsToAllSubArrays()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional-all/
        var col = _fixture.GetCollection<BsonDocument>("push_all_pos");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "tags", new BsonArray { "x" } } },
                    new BsonDocument { { "name", "b" }, { "tags", new BsonArray { "y" } } },
                }
            }
        });

        var update = Builders<BsonDocument>.Update.Push("items.$[].tags", "new");
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.Equal(new BsonArray { "x", "new" }, items[0]["tags"].AsBsonArray);
        Assert.Equal(new BsonArray { "y", "new" }, items[1]["tags"].AsBsonArray);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Push_FilteredPositional_AddsToMatchingSubArrays()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional-filtered/
        var col = _fixture.GetCollection<BsonDocument>("push_filtered_pos");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "active", true }, { "tags", new BsonArray { "x" } } },
                    new BsonDocument { { "name", "b" }, { "active", false }, { "tags", new BsonArray { "y" } } },
                    new BsonDocument { { "name", "c" }, { "active", true }, { "tags", new BsonArray { "z" } } },
                }
            }
        });

        var update = Builders<BsonDocument>.Update.Push("items.$[elem].tags", "added");
        var options = new UpdateOptions
        {
            ArrayFilters = new[]
            {
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("elem.active", true))
            }
        };

        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update, options);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.Equal(new BsonArray { "x", "added" }, items[0]["tags"].AsBsonArray);
        Assert.Equal(new BsonArray { "y" }, items[1]["tags"].AsBsonArray);
        Assert.Equal(new BsonArray { "z", "added" }, items[2]["tags"].AsBsonArray);
    }

    #endregion

    #region $pull / $addToSet with positional operators

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Pull_AllPositional_RemovesFromAllSubArrays()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/pull/
        var col = _fixture.GetCollection<BsonDocument>("pull_all_pos");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "vals", new BsonArray { 1, 2, 3 } } },
                    new BsonDocument { { "name", "b" }, { "vals", new BsonArray { 2, 3, 4 } } },
                }
            }
        });

        var update = Builders<BsonDocument>.Update.Pull("items.$[].vals", 2);
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        Assert.Equal(new BsonArray { 1, 3 }, items[0]["vals"].AsBsonArray);
        Assert.Equal(new BsonArray { 3, 4 }, items[1]["vals"].AsBsonArray);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task AddToSet_AllPositional_AddsUniqueToAllSubArrays()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/addToSet/
        var col = _fixture.GetCollection<BsonDocument>("addtoset_all_pos");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "vals", new BsonArray { 1, 2 } } },
                    new BsonDocument { { "name", "b" }, { "vals", new BsonArray { 2, 3 } } },
                }
            }
        });

        var update = Builders<BsonDocument>.Update.AddToSet("items.$[].vals", 2);
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        // 2 already exists in both, so no change
        Assert.Equal(new BsonArray { 1, 2 }, items[0]["vals"].AsBsonArray);
        Assert.Equal(new BsonArray { 2, 3 }, items[1]["vals"].AsBsonArray);
    }

    #endregion

    #region BulkWrite with arrayFilters

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task BulkWrite_UpdateOneModel_PassesArrayFilters()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.bulkWrite/
        //   "arrayFilters: An array of filter documents that determine which array elements to modify."
        var col = _fixture.GetCollection<BsonDocument>("bulk_af");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 10, 60, 30, 80 } }
        });

        var model = new UpdateOneModel<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Set("scores.$[s]", 0))
        {
            ArrayFilters = new[]
            {
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("s", new BsonDocument("$gte", 50)))
            }
        };

        await col.BulkWriteAsync(new[] { model });

        var result = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(new BsonArray { 10, 0, 30, 0 }, result["scores"].AsBsonArray);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task BulkWrite_UpdateManyModel_PassesArrayFilters()
    {
        var col = _fixture.GetCollection<BsonDocument>("bulk_af_many");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "vals", new BsonArray { 5, 50, 500 } } },
            new BsonDocument { { "_id", 2 }, { "vals", new BsonArray { 10, 100, 1000 } } },
        });

        var model = new UpdateManyModel<BsonDocument>(
            Builders<BsonDocument>.Filter.Empty,
            Builders<BsonDocument>.Update.Set("vals.$[v]", 99))
        {
            ArrayFilters = new[]
            {
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("v", new BsonDocument("$gte", 100)))
            }
        };

        await col.BulkWriteAsync(new[] { model });

        var doc1 = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var doc2 = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 2)).FirstAsync();
        Assert.Equal(new BsonArray { 5, 50, 99 }, doc1["vals"].AsBsonArray);
        Assert.Equal(new BsonArray { 10, 99, 99 }, doc2["vals"].AsBsonArray);
    }

    #endregion
}
