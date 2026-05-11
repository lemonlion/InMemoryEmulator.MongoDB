using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

public class Round53BugFixTests
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public Round53BugFixTests()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round53");
        _collection = db.GetCollection<BsonDocument>("items");
    }

    #region Bug 1: Unique index cross-type numeric comparison

    // Ref: https://www.mongodb.com/docs/manual/core/index-unique/
    //   "A unique index ensures that the indexed fields do not store duplicate values."
    //   MongoDB treats 5 (int) and 5.0 (double) as equal for uniqueness.
    [Fact]
    public async Task UniqueIndex_CrossTypeNumeric_ThrowsDuplicate()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round53_idx");
        var col = db.GetCollection<BsonDocument>("indexed");

        await col.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("x"),
                new CreateIndexOptions { Unique = true }));

        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", 5 } }); // Int32

        var ex = await Assert.ThrowsAsync<MongoWriteException>(
            () => col.InsertOneAsync(new BsonDocument { { "_id", 2 }, { "x", 5.0 } })); // Double
        Assert.Equal(11000, ex.WriteError.Code);
    }

    [Fact]
    public async Task UniqueIndex_CrossTypeNumeric_Int64_ThrowsDuplicate()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round53_idx2");
        var col = db.GetCollection<BsonDocument>("indexed");

        await col.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("x"),
                new CreateIndexOptions { Unique = true }));

        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", (long)5 } }); // Int64

        var ex = await Assert.ThrowsAsync<MongoWriteException>(
            () => col.InsertOneAsync(new BsonDocument { { "_id", 2 }, { "x", 5 } })); // Int32
        Assert.Equal(11000, ex.WriteError.Code);
    }

    #endregion

    #region Bug 2: $all uses type-sensitive comparison

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/all/
    //   "$all selects documents where the value of a field is an array that contains
    //    all the specified elements."
    //   MongoDB compares numerics cross-type (5 == 5.0 == 5L).
    [Fact]
    public async Task Filter_All_CrossTypeNumeric_Matches()
    {
        await _collection.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "tags", new BsonArray { 5, 10, 15 } } }); // Int32

        // Query with doubles
        var filter = Builders<BsonDocument>.Filter.All("tags", new BsonValue[] { new BsonDouble(5.0), new BsonDouble(10.0) });
        var results = await _collection.Find(filter).ToListAsync();
        Assert.Single(results);
    }

    [Fact]
    public async Task Filter_All_CrossTypeNumeric_Int64_Matches()
    {
        await _collection.InsertOneAsync(new BsonDocument { { "_id", 2 }, { "nums", new BsonArray { new BsonInt64(100) } } });

        var filter = Builders<BsonDocument>.Filter.All("nums", new BsonValue[] { new BsonInt32(100) });
        var results = await _collection.Find(filter).ToListAsync();
        Assert.Single(results);
    }

    #endregion

    #region Bug 3: $elemMatch with scalar operators (verify it works)

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/elemMatch/
    //   "The $elemMatch operator matches documents that contain an array field with at least
    //    one element that matches all the specified query criteria."
    [Fact]
    public async Task Filter_ElemMatch_ScalarArray_WithOperators()
    {
        await _collection.InsertOneAsync(new BsonDocument { { "_id", 10 }, { "scores", new BsonArray { 1, 5, 9, 15 } } });

        var filter = Builders<BsonDocument>.Filter.ElemMatch<BsonValue>("scores",
            new BsonDocument { { "$gte", 3 }, { "$lt", 10 } });
        var results = await _collection.Find(filter).ToListAsync();
        Assert.Single(results); // Element 5 and 9 both match
    }

    #endregion
}