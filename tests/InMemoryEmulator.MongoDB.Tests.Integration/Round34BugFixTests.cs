using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 34: $isoDayOfWeek, $bit type fix, $setField $$REMOVE, $unsetField, $median/$percentile
/// </summary>
public class Round34BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>("items");
    }

    #region $isoDayOfWeek

    [Fact]
    public void IsoDayOfWeek_Monday_Returns1()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/isoDayOfWeek/
        //   "Returns the weekday number in ISO 8601 format, ranging from 1 (for Monday) to 7 (for Sunday)."
        var col = CreateCollection();
        // 2024-01-01 is a Monday
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("dow",
                new BsonDocument("$isoDayOfWeek", "$date"))));

        var results = col.Aggregate(pipeline).ToList();
        Assert.Equal(1, results[0]["dow"].AsInt32); // Monday = 1
    }

    [Fact]
    public void IsoDayOfWeek_Sunday_Returns7()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/isoDayOfWeek/
        var col = CreateCollection();
        // 2024-01-07 is a Sunday
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "date", new BsonDateTime(new DateTime(2024, 1, 7, 0, 0, 0, DateTimeKind.Utc)) }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("dow",
                new BsonDocument("$isoDayOfWeek", "$date"))));

        var results = col.Aggregate(pipeline).ToList();
        Assert.Equal(7, results[0]["dow"].AsInt32); // Sunday = 7
    }

    #endregion

    #region $bit update type fix

    [Fact]
    public void Bit_OnMissingField_WithLongOperand_CreatesInt64()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/bit/
        //   When the field doesn't exist, the result type should match the operand type.
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 } });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument("$bit", new BsonDocument("flags", new BsonDocument("or", (long)5))));

        var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        Assert.Equal(BsonType.Int64, doc["flags"].BsonType);
        Assert.Equal(5L, doc["flags"].AsInt64);
    }

    [Fact]
    public void Bit_OnMissingField_WithIntOperand_CreatesInt32()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/bit/
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 } });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument("$bit", new BsonDocument("flags", new BsonDocument("or", 5))));

        var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        Assert.Equal(BsonType.Int32, doc["flags"].BsonType);
        Assert.Equal(5, doc["flags"].AsInt32);
    }

    #endregion

    #region $setField with $$REMOVE

    [Fact]
    public void SetField_WithRemove_RemovesField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setField/
        //   "If the value resolves to $$REMOVE, the field is removed from the document."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "name", "test" }, { "secret", "hidden" } });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$setField", new BsonDocument
                {
                    { "field", "secret" },
                    { "input", "$$ROOT" },
                    { "value", "$$REMOVE" }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        Assert.False(results[0]["result"].AsBsonDocument.Contains("secret"));
    }

    #endregion

    #region $unsetField

    [Fact]
    public void UnsetField_RemovesFieldFromDocument()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/unsetField/
        //   "$unsetField is an alias for $setField with value: $$REMOVE"
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "name", "test" }, { "temp", "data" } });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$project", new BsonDocument("result",
                new BsonDocument("$unsetField", new BsonDocument
                {
                    { "field", "temp" },
                    { "input", "$$ROOT" }
                }))));

        var results = col.Aggregate(pipeline).ToList();
        Assert.False(results[0]["result"].AsBsonDocument.Contains("temp"));
        Assert.True(results[0]["result"].AsBsonDocument.Contains("name"));
    }

    #endregion

    #region $median and $percentile

    [Fact]
    public void Median_ReturnsMedianValue()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/median/
        //   "Returns an approximation of the median, the 50th percentile."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "score", 10 } },
            new BsonDocument { { "_id", 2 }, { "score", 20 } },
            new BsonDocument { { "_id", 3 }, { "score", 30 } },
            new BsonDocument { { "_id", 4 }, { "score", 40 } },
            new BsonDocument { { "_id", 5 }, { "score", 50 } }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "mid", new BsonDocument("$median", new BsonDocument
                    {
                        { "input", "$score" },
                        { "method", "approximate" }
                    })
                }
            }));

        var results = col.Aggregate(pipeline).ToList();
        Assert.Equal(30.0, results[0]["mid"].ToDouble());
    }

    [Fact]
    public void Percentile_Returns25thAnd75th()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/percentile/
        //   "Returns an array of the approximate percentile values."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "score", 10 } },
            new BsonDocument { { "_id", 2 }, { "score", 20 } },
            new BsonDocument { { "_id", 3 }, { "score", 30 } },
            new BsonDocument { { "_id", 4 }, { "score", 40 } },
            new BsonDocument { { "_id", 5 }, { "score", 50 } }
        });

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "pctls", new BsonDocument("$percentile", new BsonDocument
                    {
                        { "input", "$score" },
                        { "p", new BsonArray { 0.25, 0.75 } },
                        { "method", "approximate" }
                    })
                }
            }));

        var results = col.Aggregate(pipeline).ToList();
        var arr = results[0]["pctls"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        // 25th percentile ≈ 20, 75th ≈ 40
        Assert.Equal(20.0, arr[0].ToDouble());
        Assert.Equal(40.0, arr[1].ToDouble());
    }

    #endregion
}
