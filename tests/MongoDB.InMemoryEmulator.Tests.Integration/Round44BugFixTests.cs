using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 44: $avg Decimal128 return type, $indexOfBytes/$indexOfCP negative start/end validation
/// </summary>
public class Round44BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection(string name = "items")
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>(name);
    }

    #region $avg returns Decimal128 when any input is Decimal128

    [Fact]
    public void Group_Avg_Decimal128Input_ReturnsDecimal128()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/avg/
        //   "The default return type is a double. If at least one operand is a decimal,
        //    then the return type is a decimal."
        var col = CreateCollection("avg_dec_r44");
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "g", "a" }, { "v", new BsonDecimal128(10.5m) } },
            new BsonDocument { { "_id", 2 }, { "g", "a" }, { "v", new BsonDecimal128(20.5m) } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument
            {
                { "_id", "$g" },
                { "avgVal", new BsonDocument("$avg", "$v") }
            })
            .First();

        Assert.Equal(BsonType.Decimal128, result["avgVal"].BsonType);
        Assert.Equal(15.5m, result["avgVal"].AsDecimal);
    }

    [Fact]
    public void Group_Avg_MixedDecimal128AndInt_ReturnsDecimal128()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/avg/
        //   "If at least one operand is a decimal, then the return type is a decimal."
        var col = CreateCollection("avg_mixed_r44");
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "g", "a" }, { "v", new BsonDecimal128(10m) } },
            new BsonDocument { { "_id", 2 }, { "g", "a" }, { "v", 20 } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument
            {
                { "_id", "$g" },
                { "avgVal", new BsonDocument("$avg", "$v") }
            })
            .First();

        Assert.Equal(BsonType.Decimal128, result["avgVal"].BsonType);
        Assert.Equal(15m, result["avgVal"].AsDecimal);
    }

    [Fact]
    public void Group_Avg_AllDoubles_ReturnsDouble()
    {
        var col = CreateCollection("avg_dbl_r44");
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "g", "a" }, { "v", 10.0 } },
            new BsonDocument { { "_id", 2 }, { "g", "a" }, { "v", 20.0 } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument
            {
                { "_id", "$g" },
                { "avgVal", new BsonDocument("$avg", "$v") }
            })
            .First();

        Assert.Equal(BsonType.Double, result["avgVal"].BsonType);
        Assert.Equal(15.0, result["avgVal"].AsDouble);
    }

    #endregion

    #region $indexOfBytes: negative start/end throws error

    [Fact]
    public void Aggregate_IndexOfBytes_NegativeStart_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfBytes/
        //   "If the <start> or <end> is a negative number, $indexOfBytes returns an error."
        var col = CreateCollection("idxb_neg_start_r44");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "s", "hello world" } });

        var ex = Assert.Throws<MongoCommandException>(() =>
            col.Aggregate()
                .Project(new BsonDocument("idx",
                    new BsonDocument("$indexOfBytes", new BsonArray { "$s", "world", -1 })))
                .First());

        Assert.Contains("non-negative", ex.Message);
    }

    [Fact]
    public void Aggregate_IndexOfBytes_NegativeEnd_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfBytes/
        //   "If the <start> or <end> is a negative number, $indexOfBytes returns an error."
        var col = CreateCollection("idxb_neg_end_r44");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "s", "hello world" } });

        var ex = Assert.Throws<MongoCommandException>(() =>
            col.Aggregate()
                .Project(new BsonDocument("idx",
                    new BsonDocument("$indexOfBytes", new BsonArray { "$s", "world", 0, -1 })))
                .First());

        Assert.Contains("non-negative", ex.Message);
    }

    #endregion

    #region $indexOfCP: negative start/end throws error

    [Fact]
    public void Aggregate_IndexOfCP_NegativeStart_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfCP/
        //   "If the <start> or <end> is a negative number, $indexOfCP returns an error."
        var col = CreateCollection("idxcp_neg_start_r44");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "s", "hello world" } });

        var ex = Assert.Throws<MongoCommandException>(() =>
            col.Aggregate()
                .Project(new BsonDocument("idx",
                    new BsonDocument("$indexOfCP", new BsonArray { "$s", "world", -1 })))
                .First());

        Assert.Contains("non-negative", ex.Message);
    }

    [Fact]
    public void Aggregate_IndexOfCP_NegativeEnd_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfCP/
        //   "If the <start> or <end> is a negative number, $indexOfCP returns an error."
        var col = CreateCollection("idxcp_neg_end_r44");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "s", "hello world" } });

        var ex = Assert.Throws<MongoCommandException>(() =>
            col.Aggregate()
                .Project(new BsonDocument("idx",
                    new BsonDocument("$indexOfCP", new BsonArray { "$s", "world", 0, -1 })))
                .First());

        Assert.Contains("non-negative", ex.Message);
    }

    #endregion

    #region $indexOfBytes/$indexOfCP: positive cases still work

    [Fact]
    public void Aggregate_IndexOfBytes_ValidArgs_ReturnsCorrectIndex()
    {
        var col = CreateCollection("idxb_valid_r44");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "s", "hello world" } });

        var result = col.Aggregate()
            .Project(new BsonDocument("idx",
                new BsonDocument("$indexOfBytes", new BsonArray { "$s", "world" })))
            .First();

        Assert.Equal(6, result["idx"].AsInt32);
    }

    [Fact]
    public void Aggregate_IndexOfCP_ValidArgs_ReturnsCorrectIndex()
    {
        var col = CreateCollection("idxcp_valid_r44");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "s", "hello world" } });

        var result = col.Aggregate()
            .Project(new BsonDocument("idx",
                new BsonDocument("$indexOfCP", new BsonArray { "$s", "world" })))
            .First();

        Assert.Equal(6, result["idx"].AsInt32);
    }

    #endregion
}
