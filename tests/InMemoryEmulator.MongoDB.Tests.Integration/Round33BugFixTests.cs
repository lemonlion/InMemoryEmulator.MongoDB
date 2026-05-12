using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 33: Date operators and bitwise operators
/// - BUG: "quarter" unit missing from $dateDiff/$dateAdd/$dateSubtract
/// - MISSING: $dateTrunc, $dateFromParts, $dateToParts
/// - MISSING: $week, $isoWeek, $isoWeekYear date extractors
/// - MISSING: $bitAnd, $bitOr, $bitNot, $bitXor
/// </summary>
public class Round33BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>("items");
    }

    #region Quarter unit bug

    [Fact]
    public void DateDiff_Quarter_ReturnsCorrectQuarters()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateDiff/
        //   unit: "year quarter week month day hour minute second millisecond"
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "start", new BsonDateTime(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc)) },
            { "end", new BsonDateTime(new DateTime(2021, 10, 1, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("diff",
                new BsonDocument("$dateDiff", new BsonDocument
                {
                    { "startDate", "$start" },
                    { "endDate", "$end" },
                    { "unit", "quarter" }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        // Jan→Oct = 9 months = 3 quarters
        Assert.Equal(3, results[0]["diff"].AsInt64);
    }

    [Fact]
    public void DateAdd_Quarter_AddsCorrectMonths()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateAdd/
        //   unit supports "quarter"
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2021, 1, 15, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$dateAdd", new BsonDocument
                {
                    { "startDate", "$date" },
                    { "unit", "quarter" },
                    { "amount", 2 }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        // Adding 2 quarters = 6 months → July 15
        var expected = new DateTime(2021, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["result"].ToUniversalTime());
    }

    [Fact]
    public void DateSubtract_Quarter_SubtractsCorrectMonths()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateSubtract/
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2021, 10, 1, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$dateSubtract", new BsonDocument
                {
                    { "startDate", "$date" },
                    { "unit", "quarter" },
                    { "amount", 1 }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        // Subtracting 1 quarter = 3 months → July 1
        var expected = new DateTime(2021, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["result"].ToUniversalTime());
    }

    #endregion

    #region $dateTrunc

    [Fact]
    public void DateTrunc_Month_TruncatesToStartOfMonth()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateTrunc/
        //   "month: $dateTrunc returns the ISODate for the start of the first day of the month in date."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2021, 3, 20, 11, 30, 5, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("truncated",
                new BsonDocument("$dateTrunc", new BsonDocument
                {
                    { "date", "$date" },
                    { "unit", "month" }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        var expected = new DateTime(2021, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["truncated"].ToUniversalTime());
    }

    [Fact]
    public void DateTrunc_Year_TruncatesToStartOfYear()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateTrunc/
        //   "year: $dateTrunc returns the ISODate for the start of January 1 for the year in date."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2021, 6, 15, 10, 30, 0, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("truncated",
                new BsonDocument("$dateTrunc", new BsonDocument
                {
                    { "date", "$date" },
                    { "unit", "year" }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        var expected = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["truncated"].ToUniversalTime());
    }

    [Fact]
    public void DateTrunc_Hour_TruncatesToStartOfHour()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateTrunc/
        //   "hour: $dateTrunc returns the ISODate for the start of the hour in date."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2021, 3, 20, 11, 30, 5, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("truncated",
                new BsonDocument("$dateTrunc", new BsonDocument
                {
                    { "date", "$date" },
                    { "unit", "hour" }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        var expected = new DateTime(2021, 3, 20, 11, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["truncated"].ToUniversalTime());
    }

    [Fact]
    public void DateTrunc_Quarter_TruncatesToStartOfQuarter()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateTrunc/
        //   "quarter: returns the start of the first day of the calendar quarter in date."
        //   Quarters: Jan-Mar, Apr-Jun, Jul-Sep, Oct-Dec
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2021, 5, 20, 11, 30, 5, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("truncated",
                new BsonDocument("$dateTrunc", new BsonDocument
                {
                    { "date", "$date" },
                    { "unit", "quarter" }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        // May is in Q2 (Apr-Jun), so truncates to April 1
        var expected = new DateTime(2021, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["truncated"].ToUniversalTime());
    }

    [Fact]
    public void DateTrunc_Week_TruncatesToStartOfWeek()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateTrunc/
        //   "week: returns the start of the startOfWeek day in date. Default startOfWeek is Sunday."
        var col = CreateCollection();
        // Wednesday March 17, 2021
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2021, 3, 17, 11, 30, 5, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("truncated",
                new BsonDocument("$dateTrunc", new BsonDocument
                {
                    { "date", "$date" },
                    { "unit", "week" }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        // Previous Sunday is March 14, 2021
        var expected = new DateTime(2021, 3, 14, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["truncated"].ToUniversalTime());
    }

    [Fact]
    public void DateTrunc_WithBinSize_TruncatesToBinnedPeriod()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateTrunc/
        //   "If binSize is 2 and unit is hour, the time period is two hours."
        //   "For 2021-03-20T11:30:05Z, $dateTrunc returns 2021-03-20T10:00:00Z"
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2021, 3, 20, 11, 30, 5, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("truncated",
                new BsonDocument("$dateTrunc", new BsonDocument
                {
                    { "date", "$date" },
                    { "unit", "hour" },
                    { "binSize", 2 }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        var expected = new DateTime(2021, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["truncated"].ToUniversalTime());
    }

    #endregion

    #region $dateFromParts

    [Fact]
    public void DateFromParts_BasicCalendar_ConstructsDate()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateFromParts/
        //   "Constructs and returns a Date object given the date's constituent properties."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 } });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("date",
                new BsonDocument("$dateFromParts", new BsonDocument
                {
                    { "year", 2017 },
                    { "month", 2 },
                    { "day", 8 },
                    { "hour", 12 }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        var expected = new DateTime(2017, 2, 8, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["date"].ToUniversalTime());
    }

    [Fact]
    public void DateFromParts_DefaultsToJanuary1Midnight()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateFromParts/
        //   month defaults to 1, day defaults to 1, hour/minute/second/millisecond default to 0
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 } });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("date",
                new BsonDocument("$dateFromParts", new BsonDocument
                {
                    { "year", 2021 }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        var expected = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["date"].ToUniversalTime());
    }

    [Fact]
    public void DateFromParts_IsoWeekDate_ConstructsDate()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateFromParts/
        //   ISO week date form with isoWeekYear, isoWeek, isoDayOfWeek
        //   Example: isoWeekYear:2017, isoWeek:6, isoDayOfWeek:3, hour:12 → 2017-02-08T12:00:00Z
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 } });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("date",
                new BsonDocument("$dateFromParts", new BsonDocument
                {
                    { "isoWeekYear", 2017 },
                    { "isoWeek", 6 },
                    { "isoDayOfWeek", 3 },
                    { "hour", 12 }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        var expected = new DateTime(2017, 2, 8, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, results[0]["date"].ToUniversalTime());
    }

    #endregion

    #region $dateToParts

    [Fact]
    public void DateToParts_Calendar_ReturnsConstituents()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateToParts/
        //   "Returns a document that contains the constituent parts of a given Date value"
        //   Returns { year, month, day, hour, minute, second, millisecond }
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2017, 1, 1, 1, 29, 9, 123, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("parts",
                new BsonDocument("$dateToParts", new BsonDocument("date", "$date")))));

        var results = col.Aggregate(pipeline).ToList();
        var parts = results[0]["parts"].AsBsonDocument;
        Assert.Equal(2017, parts["year"].AsInt32);
        Assert.Equal(1, parts["month"].AsInt32);
        Assert.Equal(1, parts["day"].AsInt32);
        Assert.Equal(1, parts["hour"].AsInt32);
        Assert.Equal(29, parts["minute"].AsInt32);
        Assert.Equal(9, parts["second"].AsInt32);
        Assert.Equal(123, parts["millisecond"].AsInt32);
    }

    [Fact]
    public void DateToParts_Iso8601_ReturnsIsoFields()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateToParts/
        //   With iso8601:true, returns { isoWeekYear, isoWeek, isoDayOfWeek, hour, minute, second, millisecond }
        //   2017-01-01 → isoWeekYear:2016, isoWeek:52, isoDayOfWeek:7
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2017, 1, 1, 1, 29, 9, 123, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("parts",
                new BsonDocument("$dateToParts", new BsonDocument
                {
                    { "date", "$date" },
                    { "iso8601", true }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        var parts = results[0]["parts"].AsBsonDocument;
        Assert.Equal(2016, parts["isoWeekYear"].AsInt32);
        Assert.Equal(52, parts["isoWeek"].AsInt32);
        Assert.Equal(7, parts["isoDayOfWeek"].AsInt32);
        Assert.Equal(1, parts["hour"].AsInt32);
        Assert.Equal(29, parts["minute"].AsInt32);
        Assert.Equal(9, parts["second"].AsInt32);
        Assert.Equal(123, parts["millisecond"].AsInt32);
    }

    #endregion

    #region $week, $isoWeek, $isoWeekYear

    [Fact]
    public void Week_ReturnsWeekOfYear_SundayBased()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/week/
        //   "Returns the week of the year for a date as a number between 0 and 53."
        //   "Weeks begin on Sundays, and week 1 begins with the first Sunday of the year."
        //   Jan 1 2014 was a Wednesday → week 0
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2014, 1, 1, 8, 15, 39, 736, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("week",
                new BsonDocument("$week", "$date"))));

        var results = col.Aggregate(pipeline).ToList();
        Assert.Equal(0, results[0]["week"].AsInt32);
    }

    [Fact]
    public void IsoWeek_ReturnsIso8601WeekNumber()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/isoWeek/
        //   "Returns the week number in ISO 8601 format, ranging from 1 to 53."
        //   2006-10-24 → isoWeek: 43
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2006, 10, 24, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("isoWeek",
                new BsonDocument("$isoWeek", "$date"))));

        var results = col.Aggregate(pipeline).ToList();
        Assert.Equal(43, results[0]["isoWeek"].AsInt32);
    }

    [Fact]
    public void IsoWeekYear_ReturnsIso8601YearNumber()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/isoWeekYear/
        //   2016-01-01 → isoWeekYear: 2015 (because ISO week year starts on Monday of week 1)
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("isoYear",
                new BsonDocument("$isoWeekYear", "$date"))));

        var results = col.Aggregate(pipeline).ToList();
        Assert.Equal(2015, results[0]["isoYear"].AsInt32);
    }

    #endregion

    #region Bitwise operators

    [Fact]
    public void BitAnd_ReturnsCorrectResult()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/bitAnd/
        //   "Returns the result of a bitwise and operation on an array of int or long values."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", 0b1111 }, { "b", 0b1010 } });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$bitAnd", new BsonArray { "$a", "$b" }))));

        var results = col.Aggregate(pipeline).ToList();
        Assert.Equal(0b1010, results[0]["result"].AsInt32);
    }

    [Fact]
    public void BitOr_ReturnsCorrectResult()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/bitOr/
        //   "Returns the result of a bitwise or operation on an array of int or long values."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", 0b1100 }, { "b", 0b0011 } });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$bitOr", new BsonArray { "$a", "$b" }))));

        var results = col.Aggregate(pipeline).ToList();
        Assert.Equal(0b1111, results[0]["result"].AsInt32);
    }

    [Fact]
    public void BitXor_ReturnsCorrectResult()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/bitXor/
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", 0b1100 }, { "b", 0b1010 } });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$bitXor", new BsonArray { "$a", "$b" }))));

        var results = col.Aggregate(pipeline).ToList();
        Assert.Equal(0b0110, results[0]["result"].AsInt32);
    }

    [Fact]
    public void BitNot_ReturnsCorrectResult()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/bitNot/
        //   "Returns the result of a bitwise not operation on a single argument."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", 0 } });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$bitNot", "$a"))));

        var results = col.Aggregate(pipeline).ToList();
        // ~0 = -1 in two's complement
        Assert.Equal(-1, results[0]["result"].AsInt32);
    }

    #endregion
}
