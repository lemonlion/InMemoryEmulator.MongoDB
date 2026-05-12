using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 40: $all regex, DropOne non-existent, $group $sum type preservation, $exists truthy
/// </summary>
public class Round40BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection(string name = "items")
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>(name);
    }

    #region $all with regex matching

    [Fact]
    public void Filter_All_WithRegex_MatchesArrayElements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/all/
        //   "{ tags: { $all: [/^ssl/] } }" matches documents whose tags array has an element starting with "ssl"
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "tags", new BsonArray { "ssl-cert", "http" } } },
            new BsonDocument { { "_id", 2 }, { "tags", new BsonArray { "http", "ftp" } } },
            new BsonDocument { { "_id", 3 }, { "tags", new BsonArray { "ssl-proxy", "ssl-cert" } } },
        });

        var filter = new BsonDocument("tags", new BsonDocument("$all", new BsonArray { new BsonRegularExpression("^ssl") }));
        var results = col.Find(filter).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r["_id"] == 1);
        Assert.Contains(results, r => r["_id"] == 3);
    }

    [Fact]
    public void Filter_All_WithRegexAndLiteral_MatchesBoth()
    {
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "tags", new BsonArray { "ssl-cert", "http" } } },
            new BsonDocument { { "_id", 2 }, { "tags", new BsonArray { "ssl-cert", "ftp" } } },
        });

        // Must match both: regex /^ssl/ AND literal "http"
        var filter = new BsonDocument("tags", new BsonDocument("$all", new BsonArray
        {
            new BsonRegularExpression("^ssl"),
            "http"
        }));
        var results = col.Find(filter).ToList();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    #endregion

    #region DropOne non-existent index throws

    [Fact]
    public void Indexes_DropOne_NonExistent_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.dropIndex/
        //   "If you specify a name that does not correspond to an existing index, the method errors."
        var col = CreateCollection();

        var ex = Assert.Throws<MongoCommandException>(() =>
            col.Indexes.DropOne("nonexistent_index_name"));

        Assert.Contains("index not found", ex.Message);
    }

    [Fact]
    public void Indexes_DropOne_ExistingIndex_Succeeds()
    {
        var col = CreateCollection();
        col.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("x"),
            new CreateIndexOptions { Name = "x_1" }));

        // Should not throw
        col.Indexes.DropOne("x_1");

        var indexes = col.Indexes.List().ToList();
        Assert.DoesNotContain(indexes, idx => idx["name"].AsString == "x_1");
    }

    #endregion

    #region $group $sum type preservation

    [Fact]
    public void Aggregate_GroupSum_Literal1_ReturnsInt32()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sum/
        //   "Returns an integer when all values are integers and sum fits."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 1 } },
            new BsonDocument { { "_id", 2 }, { "x", 2 } },
            new BsonDocument { { "_id", 3 }, { "x", 3 } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "count", new BsonDocument("$sum", 1) } })
            .First();

        Assert.Equal(BsonType.Int32, result["count"].BsonType);
        Assert.Equal(3, result["count"].AsInt32);
    }

    [Fact]
    public void Aggregate_GroupSum_Int32Fields_ReturnsInt32()
    {
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", 10 } },
            new BsonDocument { { "_id", 2 }, { "val", 20 } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$val") } })
            .First();

        Assert.Equal(BsonType.Int32, result["total"].BsonType);
        Assert.Equal(30, result["total"].AsInt32);
    }

    [Fact]
    public void Aggregate_GroupSum_WithInt64_ReturnsInt64()
    {
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", (long)100 } },
            new BsonDocument { { "_id", 2 }, { "val", 50 } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$val") } })
            .First();

        Assert.Equal(BsonType.Int64, result["total"].BsonType);
        Assert.Equal(150L, result["total"].AsInt64);
    }

    [Fact]
    public void Aggregate_GroupSum_WithDecimal128_ReturnsDecimal128()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sum/
        //   "Returns a decimal when any value is a decimal."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", new BsonDecimal128(1.5m) } },
            new BsonDocument { { "_id", 2 }, { "val", new BsonDecimal128(2.5m) } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$val") } })
            .First();

        Assert.Equal(BsonType.Decimal128, result["total"].BsonType);
        Assert.Equal(4.0m, result["total"].AsDecimal);
    }

    [Fact]
    public void Aggregate_GroupSum_WithDouble_ReturnsDouble()
    {
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", 1.5 } },
            new BsonDocument { { "_id", 2 }, { "val", 2.5 } },
        });

        var result = col.Aggregate()
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$val") } })
            .First();

        Assert.Equal(BsonType.Double, result["total"].BsonType);
        Assert.Equal(4.0, result["total"].AsDouble);
    }

    #endregion

    #region $exists truthy values

    [Fact]
    public void Filter_Exists_WithInt1_MatchesExistingField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/exists/
        //   "$exists accepts truthy/falsy values"
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 42 } },
            new BsonDocument { { "_id", 2 } },
        });

        var filter = new BsonDocument("x", new BsonDocument("$exists", 1));
        var results = col.Find(filter).ToList();

        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    [Fact]
    public void Filter_Exists_WithInt0_MatchesMissingField()
    {
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 42 } },
            new BsonDocument { { "_id", 2 } },
        });

        var filter = new BsonDocument("x", new BsonDocument("$exists", 0));
        var results = col.Find(filter).ToList();

        Assert.Single(results);
        Assert.Equal(2, results[0]["_id"].AsInt32);
    }

    #endregion
}
