using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 45: $stdDevPop/$stdDevSamp Decimal128 return type when inputs include Decimal128
/// </summary>
public class Round45BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection(string name = "items")
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>(name);
    }

    #region $stdDevPop returns Decimal128 when any input is Decimal128

    [Fact]
    public void Group_StdDevPop_Decimal128Input_ReturnsDecimal128()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/stdDevPop/
        //   "Result Type: $stdDevPop returns the population standard deviation of the input values as a decimal."
        var col = CreateCollection("sdp_dec_r45");
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "g", "a" }, { "v", new BsonDecimal128(10m) } },
            new BsonDocument { { "_id", 2 }, { "g", "a" }, { "v", new BsonDecimal128(20m) } },
            new BsonDocument { { "_id", 3 }, { "g", "a" }, { "v", new BsonDecimal128(30m) } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument
            {
                { "_id", "$g" },
                { "sd", new BsonDocument("$stdDevPop", "$v") }
            })
            .First();

        Assert.Equal(BsonType.Decimal128, result["sd"].BsonType);
        // StdDev of [10, 20, 30] = sqrt(((10-20)^2 + (20-20)^2 + (30-20)^2) / 3) = sqrt(200/3) ≈ 8.165
        var sd = result["sd"].AsDecimal;
        Assert.True(sd > 8.1m && sd < 8.2m);
    }

    [Fact]
    public void Group_StdDevPop_AllDoubles_ReturnsDouble()
    {
        var col = CreateCollection("sdp_dbl_r45");
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "g", "a" }, { "v", 10.0 } },
            new BsonDocument { { "_id", 2 }, { "g", "a" }, { "v", 20.0 } },
            new BsonDocument { { "_id", 3 }, { "g", "a" }, { "v", 30.0 } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument
            {
                { "_id", "$g" },
                { "sd", new BsonDocument("$stdDevPop", "$v") }
            })
            .First();

        Assert.Equal(BsonType.Double, result["sd"].BsonType);
    }

    #endregion

    #region $stdDevSamp returns Decimal128 when any input is Decimal128

    [Fact]
    public void Group_StdDevSamp_Decimal128Input_ReturnsDecimal128()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/stdDevSamp/
        //   Same return type behavior as $stdDevPop.
        var col = CreateCollection("sds_dec_r45");
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "g", "a" }, { "v", new BsonDecimal128(10m) } },
            new BsonDocument { { "_id", 2 }, { "g", "a" }, { "v", new BsonDecimal128(20m) } },
            new BsonDocument { { "_id", 3 }, { "g", "a" }, { "v", new BsonDecimal128(30m) } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument
            {
                { "_id", "$g" },
                { "sd", new BsonDocument("$stdDevSamp", "$v") }
            })
            .First();

        Assert.Equal(BsonType.Decimal128, result["sd"].BsonType);
        // Sample stddev of [10, 20, 30] = sqrt(((10-20)^2 + (20-20)^2 + (30-20)^2) / 2) = sqrt(100) = 10
        Assert.Equal(10m, result["sd"].AsDecimal);
    }

    [Fact]
    public void Group_StdDevSamp_MixedDecimal128AndInt_ReturnsDecimal128()
    {
        var col = CreateCollection("sds_mixed_r45");
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "g", "a" }, { "v", new BsonDecimal128(10m) } },
            new BsonDocument { { "_id", 2 }, { "g", "a" }, { "v", 20 } },
            new BsonDocument { { "_id", 3 }, { "g", "a" }, { "v", 30 } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument
            {
                { "_id", "$g" },
                { "sd", new BsonDocument("$stdDevSamp", "$v") }
            })
            .First();

        Assert.Equal(BsonType.Decimal128, result["sd"].BsonType);
    }

    [Fact]
    public void Group_StdDevSamp_AllDoubles_ReturnsDouble()
    {
        var col = CreateCollection("sds_dbl_r45");
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "g", "a" }, { "v", 10.0 } },
            new BsonDocument { { "_id", 2 }, { "g", "a" }, { "v", 20.0 } },
            new BsonDocument { { "_id", 3 }, { "g", "a" }, { "v", 30.0 } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument
            {
                { "_id", "$g" },
                { "sd", new BsonDocument("$stdDevSamp", "$v") }
            })
            .First();

        Assert.Equal(BsonType.Double, result["sd"].BsonType);
    }

    #endregion
}
