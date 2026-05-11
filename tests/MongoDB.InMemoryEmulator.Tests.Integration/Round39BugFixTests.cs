using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 39: $meta sort crash, math operator type preservation ($abs, $ceil, $floor, $round, $trunc)
/// </summary>
public class Round39BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection(string name = "items")
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>(name);
    }

    #region $meta sort should not crash

    [Fact]
    public void Sort_MetaTextScore_DoesNotCrash()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sort/
        //   "{ $meta: 'textScore' }" is a valid sort expression.
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "title", "hello world" } },
            new BsonDocument { { "_id", 2 }, { "title", "goodbye" } },
        });

        // Sort with $meta: "textScore" should not throw InvalidCastException
        var sort = new BsonDocument("score", new BsonDocument("$meta", "textScore"));
        var results = col.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(new BsonDocumentSortDefinition<BsonDocument>(sort))
            .ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Sort_MetaTextScore_WithOtherFields_SortsByOtherFields()
    {
        // $meta sort should be skipped, but other sort fields should still work
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 3 } },
            new BsonDocument { { "_id", 2 }, { "x", 1 } },
            new BsonDocument { { "_id", 3 }, { "x", 2 } },
        });

        var sort = new BsonDocument
        {
            { "score", new BsonDocument("$meta", "textScore") },
            { "x", 1 }
        };
        var results = col.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(new BsonDocumentSortDefinition<BsonDocument>(sort))
            .ToList();

        Assert.Equal(2, results[0]["_id"].AsInt32);
        Assert.Equal(3, results[1]["_id"].AsInt32);
        Assert.Equal(1, results[2]["_id"].AsInt32);
    }

    #endregion

    #region $abs preserves type

    [Fact]
    public void Aggregate_Abs_PreservesInt32()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/abs/
        //   "$abs returns a value with the same type as the input."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", -5 } }); // Int32

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$abs", "$val")))
            .First();

        Assert.Equal(BsonType.Int32, result["result"].BsonType);
        Assert.Equal(5, result["result"].AsInt32);
    }

    [Fact]
    public void Aggregate_Abs_PreservesInt64()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", (long)-123456789012 } }); // Int64

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$abs", "$val")))
            .First();

        Assert.Equal(BsonType.Int64, result["result"].BsonType);
        Assert.Equal(123456789012L, result["result"].AsInt64);
    }

    [Fact]
    public void Aggregate_Abs_PreservesDecimal128()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", new BsonDecimal128(-3.14m) } });

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$abs", "$val")))
            .First();

        Assert.Equal(BsonType.Decimal128, result["result"].BsonType);
    }

    #endregion

    #region $ceil preserves type

    [Fact]
    public void Aggregate_Ceil_PreservesInt32()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/ceil/
        //   "Returns a value with the same type as the input."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", 7 } }); // Int32

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$ceil", "$val")))
            .First();

        Assert.Equal(BsonType.Int32, result["result"].BsonType);
        Assert.Equal(7, result["result"].AsInt32);
    }

    [Fact]
    public void Aggregate_Ceil_PreservesDouble()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", 2.3 } }); // Double

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$ceil", "$val")))
            .First();

        Assert.Equal(BsonType.Double, result["result"].BsonType);
        Assert.Equal(3.0, result["result"].AsDouble);
    }

    #endregion

    #region $floor preserves type

    [Fact]
    public void Aggregate_Floor_PreservesInt64()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/floor/
        //   "Returns a value with the same type as the input."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", (long)42 } }); // Int64

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$floor", "$val")))
            .First();

        Assert.Equal(BsonType.Int64, result["result"].BsonType);
        Assert.Equal(42L, result["result"].AsInt64);
    }

    [Fact]
    public void Aggregate_Floor_PreservesDouble()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", 7.9 } });

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$floor", "$val")))
            .First();

        Assert.Equal(BsonType.Double, result["result"].BsonType);
        Assert.Equal(7.0, result["result"].AsDouble);
    }

    #endregion

    #region $trunc preserves type

    [Fact]
    public void Aggregate_Trunc_PreservesInt32()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/trunc/
        //   "$trunc returns a value with the same type as the input value."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", 42 } });

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$trunc", new BsonArray { "$val", 0 })))
            .First();

        Assert.Equal(BsonType.Int32, result["result"].BsonType);
        Assert.Equal(42, result["result"].AsInt32);
    }

    [Fact]
    public void Aggregate_Trunc_PreservesDouble()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", 7.89 } });

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$trunc", new BsonArray { "$val", 1 })))
            .First();

        Assert.Equal(BsonType.Double, result["result"].BsonType);
        Assert.Equal(7.8, result["result"].AsDouble);
    }

    [Fact]
    public void Aggregate_Trunc_PreservesDecimal128()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", new BsonDecimal128(9.876m) } });

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$trunc", new BsonArray { "$val", 2 })))
            .First();

        Assert.Equal(BsonType.Decimal128, result["result"].BsonType);
        Assert.Equal(9.87m, result["result"].AsDecimal);
    }

    #endregion

    #region $round preserves type

    [Fact]
    public void Aggregate_Round_PreservesInt32()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/round/
        //   "$round returns a value with the same type as the input value."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", 1234 } });

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$round", new BsonArray { "$val", -2 })))
            .First();

        Assert.Equal(BsonType.Int32, result["result"].BsonType);
        Assert.Equal(1200, result["result"].AsInt32);
    }

    [Fact]
    public void Aggregate_Round_PreservesDouble()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", 2.555 } });

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$round", new BsonArray { "$val", 2 })))
            .First();

        Assert.Equal(BsonType.Double, result["result"].BsonType);
        // IEEE 754 round-to-even: 2.555 rounds to 2.56
        Assert.Equal(2.56, result["result"].AsDouble);
    }

    [Fact]
    public void Aggregate_Round_PreservesInt64()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", (long)9876 } });

        var result = col.Aggregate()
            .Project(new BsonDocument("result", new BsonDocument("$round", new BsonArray { "$val", -1 })))
            .First();

        Assert.Equal(BsonType.Int64, result["result"].BsonType);
        Assert.Equal(9880L, result["result"].AsInt64);
    }

    #endregion
}
