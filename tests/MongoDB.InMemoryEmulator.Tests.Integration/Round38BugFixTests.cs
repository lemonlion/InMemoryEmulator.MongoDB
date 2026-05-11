using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 38: Find Limit(0), $dateFromString format parameter
/// </summary>
public class Round38BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection(string name = "items")
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>(name);
    }

    #region Find().Limit(0) should return all documents

    [Fact]
    public void Find_Limit0_ReturnsAllDocuments()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/cursor.limit/
        //   "A limit value of 0 (i.e. .limit(0)) is equivalent to setting no limit."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 1 } },
            new BsonDocument { { "_id", 2 }, { "x", 2 } },
            new BsonDocument { { "_id", 3 }, { "x", 3 } },
            new BsonDocument { { "_id", 4 }, { "x", 4 } },
            new BsonDocument { { "_id", 5 }, { "x", 5 } }
        });

        var results = col.Find(FilterDefinition<BsonDocument>.Empty)
            .Limit(0)
            .ToList();

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void Find_LimitPositive_ReturnsLimitedDocuments()
    {
        // Sanity check: positive limit still works
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 1 } },
            new BsonDocument { { "_id", 2 }, { "x", 2 } },
            new BsonDocument { { "_id", 3 }, { "x", 3 } }
        });

        var results = col.Find(FilterDefinition<BsonDocument>.Empty)
            .Limit(2)
            .ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void FindOptions_Limit0_ReturnsAllDocuments()
    {
        // Test via Find with sort and limit=0 through fluent API
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 1 } },
            new BsonDocument { { "_id", 2 }, { "x", 2 } },
            new BsonDocument { { "_id", 3 }, { "x", 3 } }
        });

        // Using fluent API with sort and limit 0
        var results = col.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("x"))
            .Limit(0)
            .ToList();

        Assert.Equal(3, results.Count);
    }

    #endregion

    #region $dateFromString with format parameter

    [Fact]
    public void DateFromString_WithFormat_ParsesCorrectly()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateFromString/
        //   "format: Optional. The date format specification of the dateString."
        //   Uses strftime format like %Y-%m-%d
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "dateStr", "06-15-2018" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("parsed", new BsonDocument("$dateFromString",
                new BsonDocument
                {
                    { "dateString", "$dateStr" },
                    { "format", "%m-%d-%Y" }
                })))
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var parsedDate = results[0]["parsed"].ToUniversalTime();
        Assert.Equal(2018, parsedDate.Year);
        Assert.Equal(6, parsedDate.Month);
        Assert.Equal(15, parsedDate.Day);
    }

    [Fact]
    public void DateFromString_WithFormatYearMonthDay_ParsesCorrectly()
    {
        // Format: %Y/%m/%d %H:%M:%S
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "dateStr", "2020/03/25 14:30:00" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("parsed", new BsonDocument("$dateFromString",
                new BsonDocument
                {
                    { "dateString", "$dateStr" },
                    { "format", "%Y/%m/%d %H:%M:%S" }
                })))
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var parsedDate = results[0]["parsed"].ToUniversalTime();
        Assert.Equal(2020, parsedDate.Year);
        Assert.Equal(3, parsedDate.Month);
        Assert.Equal(25, parsedDate.Day);
        Assert.Equal(14, parsedDate.Hour);
        Assert.Equal(30, parsedDate.Minute);
        Assert.Equal(0, parsedDate.Second);
    }

    [Fact]
    public void DateFromString_WithTimezone_ConvertsToUtc()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateFromString/
        //   "timezone: Optional. The time zone to use to format the date."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "dateStr", "2017-02-08T12:00:00" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("parsed", new BsonDocument("$dateFromString",
                new BsonDocument
                {
                    { "dateString", "$dateStr" },
                    { "timezone", "+05:00" }
                })))
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var parsedDate = results[0]["parsed"].ToUniversalTime();
        // Input 12:00 in +05:00 → UTC is 07:00
        Assert.Equal(7, parsedDate.Hour);
    }

    [Fact]
    public void DateFromString_OnNull_ReturnsOnNullValue()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateFromString/
        //   "onNull: Optional. The value to return if the dateString is null or missing."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 } }); // no dateStr field

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("parsed", new BsonDocument("$dateFromString",
                new BsonDocument
                {
                    { "dateString", "$dateStr" },
                    { "onNull", "N/A" }
                })))
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal("N/A", results[0]["parsed"].AsString);
    }

    [Fact]
    public void DateFromString_OnError_ReturnsOnErrorValue()
    {
        // onError should be returned when parsing fails
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "dateStr", "not-a-date" } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("parsed", new BsonDocument("$dateFromString",
                new BsonDocument
                {
                    { "dateString", "$dateStr" },
                    { "onError", "INVALID" }
                })))
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal("INVALID", results[0]["parsed"].AsString);
    }

    #endregion
}
