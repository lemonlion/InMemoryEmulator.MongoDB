using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 35: Duplicate index key detection, $slice/$elemMatch with dot-notation projections
/// </summary>
public class Round35BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>("items");
    }

    #region Duplicate index key detection

    [Fact]
    public void CreateIndex_SameKeySameName_NoOp()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/createIndexes/#behaviors
        //   "If an index with the same specification already exists, no-op"
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "name", "test" } });

        var name1 = col.Indexes.CreateOne(
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("name")));
        var name2 = col.Indexes.CreateOne(
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("name")));

        Assert.Equal(name1, name2);
    }

    [Fact]
    public void CreateIndex_SameKeyDifferentName_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/createIndexes/#behaviors
        //   "If you create an index with one set of index key specifications and try to create
        //    another index with the same key specifications but a different name → error"
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "name", "test" } });

        col.Indexes.CreateOne(
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("name")));

        var ex = Assert.Throws<MongoCommandException>(() =>
            col.Indexes.CreateOne(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("name"),
                    new CreateIndexOptions { Name = "different_name" })));

        Assert.Contains("already exists", ex.Message);
    }

    #endregion

    #region $slice projection with dot-notation

    [Fact]
    public void Slice_TopLevelArray_ReturnsFirstN()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/slice/
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 10, 20, 30, 40, 50 } }
        });

        // Use raw BsonDocument projection to avoid driver rendering differences
        var projection = new BsonDocument("scores", new BsonDocument("$slice", 3));
        var result = col.Find(Builders<BsonDocument>.Filter.Empty)
            .Project<BsonDocument>(projection)
            .First();

        var arr = result["scores"].AsBsonArray;
        Assert.Equal(3, arr.Count);
        Assert.Equal(10, arr[0].AsInt32);
        Assert.Equal(20, arr[1].AsInt32);
        Assert.Equal(30, arr[2].AsInt32);
    }

    [Fact]
    public void Slice_DotNotation_ReturnsFirstN()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/slice/
        //   "You can use dot notation to project on embedded arrays."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "nested", new BsonDocument("scores", new BsonArray { 1, 2, 3, 4, 5 }) }
        });

        // Use raw BsonDocument projection to avoid driver rendering differences
        var projection = new BsonDocument("nested.scores", new BsonDocument("$slice", 2));
        var result = col.Find(Builders<BsonDocument>.Filter.Empty)
            .Project<BsonDocument>(projection)
            .First();

        var arr = result["nested"]["scores"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal(1, arr[0].AsInt32);
        Assert.Equal(2, arr[1].AsInt32);
    }

    #endregion

    #region $elemMatch projection with dot-notation

    [Fact]
    public void ElemMatch_TopLevel_ReturnsFirstMatch()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/elemMatch/
        //   "Projects the first element in an array that matches the specified $elemMatch condition."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "results", new BsonArray
                {
                    new BsonDocument { { "score", 5 } },
                    new BsonDocument { { "score", 15 } },
                    new BsonDocument { { "score", 25 } }
                }
            }
        });

        var projection = Builders<BsonDocument>.Projection.ElemMatch("results",
            Builders<BsonDocument>.Filter.Gte("score", 10));

        var result = col.Find(Builders<BsonDocument>.Filter.Empty)
            .Project(projection)
            .First();

        var arr = result["results"].AsBsonArray;
        Assert.Single(arr);
        Assert.Equal(15, arr[0]["score"].AsInt32);
    }

    [Fact]
    public void ElemMatch_DotNotation_ReturnsFirstMatch()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/elemMatch/
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "data", new BsonDocument("items", new BsonArray
                {
                    new BsonDocument { { "val", 3 } },
                    new BsonDocument { { "val", 7 } },
                    new BsonDocument { { "val", 12 } }
                })
            }
        });

        var projection = Builders<BsonDocument>.Projection.ElemMatch("data.items",
            Builders<BsonDocument>.Filter.Gte("val", 5));

        var result = col.Find(Builders<BsonDocument>.Filter.Empty)
            .Project(projection)
            .First();

        var items = result["data"]["items"].AsBsonArray;
        Assert.Single(items);
        Assert.Equal(7, items[0]["val"].AsInt32);
    }

    #endregion

    #region $push with $sort on scalar array

    [Fact]
    public void Push_SortScalarArray_Ascending()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/push/
        //   "If the array elements are not documents, you can sort on the array elements directly
        //    by specifying 1 for ascending or -1 for descending in the $sort modifier."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 50, 10, 30 } }
        });

        var update = new BsonDocument("$push", new BsonDocument("scores",
            new BsonDocument
            {
                { "$each", new BsonArray { 20, 40 } },
                { "$sort", 1 }
            }));
        col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        var arr = result["scores"].AsBsonArray;
        Assert.Equal(5, arr.Count);
        Assert.Equal(10, arr[0].AsInt32);
        Assert.Equal(20, arr[1].AsInt32);
        Assert.Equal(30, arr[2].AsInt32);
        Assert.Equal(40, arr[3].AsInt32);
        Assert.Equal(50, arr[4].AsInt32);
    }

    [Fact]
    public void Push_SortDocArrayByField_Ascending()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/push/
        //   "If the array elements are documents, you can sort by a field in the documents."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "b" }, { "val", 2 } },
                    new BsonDocument { { "name", "a" }, { "val", 1 } }
                }
            }
        });

        var update = new BsonDocument("$push", new BsonDocument("items",
            new BsonDocument
            {
                { "$each", new BsonArray { new BsonDocument { { "name", "c" }, { "val", 3 } } } },
                { "$sort", new BsonDocument("val", 1) }
            }));
        col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        var arr = result["items"].AsBsonArray;
        Assert.Equal(3, arr.Count);
        Assert.Equal(1, arr[0]["val"].AsInt32);
        Assert.Equal(2, arr[1]["val"].AsInt32);
        Assert.Equal(3, arr[2]["val"].AsInt32);
    }

    #endregion

    #region $jsonSchema with allOf/anyOf/oneOf/not

    [Fact]
    public void JsonSchema_AllOf_MustSatisfyAll()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/jsonSchema/
        //   "allOf: Array of JSON Schema objects. Must match ALL schemas."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "x", 10 }, { "y", "hello" } });
        col.InsertOne(new BsonDocument { { "_id", 2 }, { "x", "oops" }, { "y", "world" } });

        var schema = new BsonDocument("allOf", new BsonArray
        {
            new BsonDocument("properties", new BsonDocument("x", new BsonDocument("bsonType", "int"))),
            new BsonDocument("properties", new BsonDocument("y", new BsonDocument("bsonType", "string")))
        });

        var results = col.Find(new BsonDocument("$jsonSchema", schema)).ToList();
        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    [Fact]
    public void JsonSchema_AnyOf_MustSatisfyAtLeastOne()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/jsonSchema/
        //   "anyOf: Must match at least ONE of the schemas."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "x", 10 } });
        col.InsertOne(new BsonDocument { { "_id", 2 }, { "x", "hello" } });
        col.InsertOne(new BsonDocument { { "_id", 3 }, { "x", true } });

        var schema = new BsonDocument("anyOf", new BsonArray
        {
            new BsonDocument("properties", new BsonDocument("x", new BsonDocument("bsonType", "int"))),
            new BsonDocument("properties", new BsonDocument("x", new BsonDocument("bsonType", "string")))
        });

        var results = col.Find(new BsonDocument("$jsonSchema", schema)).ToList();
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r["_id"].AsInt32 == 1);
        Assert.Contains(results, r => r["_id"].AsInt32 == 2);
    }

    [Fact]
    public void JsonSchema_Not_MustNotSatisfy()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/jsonSchema/
        //   "not: Must NOT match the schema."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "x", 10 } });
        col.InsertOne(new BsonDocument { { "_id", 2 }, { "x", "hello" } });

        var schema = new BsonDocument("not",
            new BsonDocument("properties", new BsonDocument("x", new BsonDocument("bsonType", "int"))));

        var results = col.Find(new BsonDocument("$jsonSchema", schema)).ToList();
        Assert.Single(results);
        Assert.Equal(2, results[0]["_id"].AsInt32);
    }

    #endregion

    #region $slice negative count

    [Fact]
    public void Slice_NegativeCount_ReturnsLastN()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/slice/
        //   "A negative number n returns the last n elements."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 10, 20, 30, 40, 50 } }
        });

        var projection = new BsonDocument("scores", new BsonDocument("$slice", -2));
        var result = col.Find(Builders<BsonDocument>.Filter.Empty)
            .Project<BsonDocument>(projection)
            .First();

        var arr = result["scores"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal(40, arr[0].AsInt32);
        Assert.Equal(50, arr[1].AsInt32);
    }

    [Fact]
    public void Slice_SkipAndLimit_ReturnsSubset()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/slice/
        //   "{ $slice: [skip, limit] } — skips the first 'skip' elements, returns 'limit' elements."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 10, 20, 30, 40, 50 } }
        });

        var projection = new BsonDocument("scores", new BsonDocument("$slice", new BsonArray { 1, 2 }));
        var result = col.Find(Builders<BsonDocument>.Filter.Empty)
            .Project<BsonDocument>(projection)
            .First();

        var arr = result["scores"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal(20, arr[0].AsInt32);
        Assert.Equal(30, arr[1].AsInt32);
    }

    #endregion
}
