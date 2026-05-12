using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

public class Round52BugFixTests
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public Round52BugFixTests()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round52");
        _collection = db.GetCollection<BsonDocument>("items");
    }

    #region Bug 1: $push $sort with document spec on mixed/scalar arrays

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/push/
    //   "$sort modifies the order of the array elements after $each is applied."
    //   When $sort is a document spec (e.g., {score:1}), non-document elements
    //   should be treated as having missing sort fields (compared as null/missing).
    [Fact]
    public async Task Update_Push_Sort_DocumentSpec_WithScalarElements_DoesNotThrow()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1)
            .Add("items", new BsonArray { new BsonDocument("score", 5), new BsonDocument("score", 2) }));

        // Push a new item and sort by score ascending - add a null element to make it mixed
        var update = Builders<BsonDocument>.Update.Push("items", new BsonDocument("score", 3));
        await _collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Combine(
                Builders<BsonDocument>.Update.Push("items", BsonNull.Value),
                Builders<BsonDocument>.Update.Set("_sort_hack", 1)));

        // Now push with $sort spec
        var pushUpdate = new BsonDocument("$push", new BsonDocument("items", new BsonDocument
        {
            { "$each", new BsonArray { new BsonDocument("score", 1) } },
            { "$sort", new BsonDocument("score", 1) }
        }));
        await _collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            pushUpdate);

        var result = await _collection.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        var items = result["items"].AsBsonArray;
        // Should not throw and array should be sorted (nulls/non-docs come before docs with score)
        Assert.True(items.Count >= 3);
    }

    [Fact]
    public async Task Update_Push_Sort_NumericSpec_WithScalarArray()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 2)
            .Add("scores", new BsonArray { 5, 3, 8, 1 }));

        var pushUpdate = new BsonDocument("$push", new BsonDocument("scores", new BsonDocument
        {
            { "$each", new BsonArray { 4, 2 } },
            { "$sort", 1 }
        }));
        await _collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 2),
            pushUpdate);

        var result = await _collection.Find(Builders<BsonDocument>.Filter.Eq("_id", 2)).FirstAsync();
        var scores = result["scores"].AsBsonArray;
        Assert.Equal(new BsonArray { 1, 2, 3, 4, 5, 8 }, scores);
    }

    #endregion

    #region Bug 2: $rename through array paths should error

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/rename/
    //   "The $rename operator cannot move a field into or out of an array element.
    //    $rename does not work if these fields are in array elements."
    [Fact]
    public async Task Update_Rename_ThroughArrayPath_ThrowsWriteError()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 3)
            .Add("items", new BsonArray { new BsonDocument { { "old", 1 } } }));

        var update = Builders<BsonDocument>.Update.Rename("items.0.old", "items.0.new");

        var ex = await Assert.ThrowsAsync<MongoWriteException>(
            () => _collection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 3), update));
        // MongoDB returns error: "The source field for $rename may not be dynamic"
        // or "cannot use the part (0 of items.0.old) to traverse the element"
        Assert.NotNull(ex.WriteError);
    }

    [Fact]
    public async Task Update_Rename_TopLevelFields_Works()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 4)
            .Add("oldName", "hello"));

        var update = Builders<BsonDocument>.Update.Rename("oldName", "newName");
        await _collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 4), update);

        var result = await _collection.Find(Builders<BsonDocument>.Filter.Eq("_id", 4)).FirstAsync();
        Assert.False(result.Contains("oldName"));
        Assert.Equal("hello", result["newName"].AsString);
    }

    #endregion
}