using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 42: Aggregation arithmetic Decimal128 type preservation, $dateDiff week/quarter boundary counting
/// </summary>
public class Round42BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection(string name = "items")
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>(name);
    }

    #region $add preserves Decimal128

    [Fact]
    public void Aggregate_Add_Decimal128_ReturnsDecimal128()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/add/
        //   "Type promotion: integer → long → double → decimal"
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", new BsonDecimal128(1.1m) }, { "b", new BsonDecimal128(2.2m) } });

        var result = col.Aggregate()
            .Project(new BsonDocument("total", new BsonDocument("$add", new BsonArray { "$a", "$b" })))
            .First();

        Assert.Equal(BsonType.Decimal128, result["total"].BsonType);
        Assert.Equal(3.3m, result["total"].AsDecimal);
    }

    [Fact]
    public void Aggregate_Add_Decimal128PlusInt_ReturnsDecimal128()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", new BsonDecimal128(10.5m) }, { "b", 5 } });

        var result = col.Aggregate()
            .Project(new BsonDocument("total", new BsonDocument("$add", new BsonArray { "$a", "$b" })))
            .First();

        Assert.Equal(BsonType.Decimal128, result["total"].BsonType);
    }

    #endregion

    #region $subtract preserves Decimal128

    [Fact]
    public void Aggregate_Subtract_Decimal128_ReturnsDecimal128()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", new BsonDecimal128(5.5m) }, { "b", new BsonDecimal128(2.2m) } });

        var result = col.Aggregate()
            .Project(new BsonDocument("diff", new BsonDocument("$subtract", new BsonArray { "$a", "$b" })))
            .First();

        Assert.Equal(BsonType.Decimal128, result["diff"].BsonType);
    }

    #endregion

    #region $multiply preserves Decimal128

    [Fact]
    public void Aggregate_Multiply_Decimal128_ReturnsDecimal128()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", new BsonDecimal128(3.0m) }, { "b", new BsonDecimal128(2.0m) } });

        var result = col.Aggregate()
            .Project(new BsonDocument("product", new BsonDocument("$multiply", new BsonArray { "$a", "$b" })))
            .First();

        Assert.Equal(BsonType.Decimal128, result["product"].BsonType);
        Assert.Equal(6.0m, result["product"].AsDecimal);
    }

    #endregion

    #region $mod preserves Decimal128

    [Fact]
    public void Aggregate_Mod_Decimal128_ReturnsDecimal128()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", new BsonDecimal128(10.5m) }, { "b", new BsonDecimal128(3.0m) } });

        var result = col.Aggregate()
            .Project(new BsonDocument("rem", new BsonDocument("$mod", new BsonArray { "$a", "$b" })))
            .First();

        Assert.Equal(BsonType.Decimal128, result["rem"].BsonType);
    }

    #endregion

    #region $dateDiff week boundary counting

    [Fact]
    public void Aggregate_DateDiff_Week_CountsSundayBoundaries()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateDiff/
        //   Week counts boundaries crossed, default startOfWeek is Sunday.
        // Jan 2021: Jan 1 = Friday, Sundays are Jan 3, 10, 17, 24, 31
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "start", new BsonDateTime(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc)) },
            { "end", new BsonDateTime(new DateTime(2021, 1, 31, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var result = col.Aggregate()
            .Project(new BsonDocument("weeks", new BsonDocument("$dateDiff", new BsonDocument
            {
                { "startDate", "$start" },
                { "endDate", "$end" },
                { "unit", "week" }
            })))
            .First();

        // 30 days from Jan 1 (Fri) to Jan 31 (Sun). Sundays crossed: Jan 3, 10, 17, 24, 31 = 5
        Assert.Equal(5, result["weeks"].AsInt64);
    }

    [Fact]
    public void Aggregate_DateDiff_Week_NotNaiveDivision()
    {
        // Jan 2 (Sat) to Jan 4 (Mon) - crosses 1 Sunday boundary (Jan 3)
        // Naive 2/7 = 0, but correct = 1
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "start", new BsonDateTime(new DateTime(2021, 1, 2, 0, 0, 0, DateTimeKind.Utc)) },
            { "end", new BsonDateTime(new DateTime(2021, 1, 4, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var result = col.Aggregate()
            .Project(new BsonDocument("weeks", new BsonDocument("$dateDiff", new BsonDocument
            {
                { "startDate", "$start" },
                { "endDate", "$end" },
                { "unit", "week" }
            })))
            .First();

        // From Jan 2 (Saturday) to Jan 4 (Monday) crosses one Sunday (Jan 3)
        Assert.Equal(1, result["weeks"].AsInt64);
    }

    [Fact]
    public void Aggregate_DateDiff_Week_SameDayNoBoundary()
    {
        // Mon to Fri same week — no Sunday boundary crossed
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "start", new BsonDateTime(new DateTime(2021, 1, 4, 0, 0, 0, DateTimeKind.Utc)) },  // Mon
            { "end", new BsonDateTime(new DateTime(2021, 1, 8, 0, 0, 0, DateTimeKind.Utc)) }      // Fri
        });

        var result = col.Aggregate()
            .Project(new BsonDocument("weeks", new BsonDocument("$dateDiff", new BsonDocument
            {
                { "startDate", "$start" },
                { "endDate", "$end" },
                { "unit", "week" }
            })))
            .First();

        Assert.Equal(0, result["weeks"].AsInt64);
    }

    #endregion

    #region $dateDiff quarter boundary counting

    [Fact]
    public void Aggregate_DateDiff_Quarter_CrossesBoundary()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateDiff/
        //   "quarter" counts quarter boundaries (Jan 1, Apr 1, Jul 1, Oct 1) crossed.
        // Mar 15 to Apr 15 crosses Apr 1 boundary → 1 quarter
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "start", new BsonDateTime(new DateTime(2021, 3, 15, 0, 0, 0, DateTimeKind.Utc)) },
            { "end", new BsonDateTime(new DateTime(2021, 4, 15, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var result = col.Aggregate()
            .Project(new BsonDocument("quarters", new BsonDocument("$dateDiff", new BsonDocument
            {
                { "startDate", "$start" },
                { "endDate", "$end" },
                { "unit", "quarter" }
            })))
            .First();

        Assert.Equal(1, result["quarters"].AsInt64);
    }

    [Fact]
    public void Aggregate_DateDiff_Quarter_MultipleYears()
    {
        // Jan 2020 to Oct 2021: Q1 2020 to Q4 2021
        // (2021*4 + (10-1)/3) - (2020*4 + (1-1)/3) = (8084+3) - (8080+0) = 8087 - 8080 = 7
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "start", new BsonDateTime(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)) },
            { "end", new BsonDateTime(new DateTime(2021, 10, 1, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var result = col.Aggregate()
            .Project(new BsonDocument("quarters", new BsonDocument("$dateDiff", new BsonDocument
            {
                { "startDate", "$start" },
                { "endDate", "$end" },
                { "unit", "quarter" }
            })))
            .First();

        Assert.Equal(7, result["quarters"].AsInt64);
    }

    #endregion
}
