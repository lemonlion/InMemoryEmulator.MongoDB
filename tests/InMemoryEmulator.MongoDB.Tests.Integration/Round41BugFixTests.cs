using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 41: $inc/$mul Decimal128 precedence, ReplaceOne change stream gating, CreateCollection duplicate error
/// </summary>
public class Round41BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection(string name = "items")
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>(name);
    }

    #region $inc type precedence: Decimal128 > Double

    [Fact]
    public void Inc_Decimal128Field_ByDouble_ReturnsDecimal128()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/inc/
        //   "Decimal128 has the highest type precedence."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", new BsonDecimal128(10.5m) } });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument("$inc", new BsonDocument("val", 2.5)));  // Double increment

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        Assert.Equal(BsonType.Decimal128, result["val"].BsonType);
    }

    [Fact]
    public void Inc_DoubleField_ByDecimal128_ReturnsDecimal128()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", 10.5 } }); // Double

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument("$inc", new BsonDocument("val", new BsonDecimal128(2.5m))));

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        Assert.Equal(BsonType.Decimal128, result["val"].BsonType);
    }

    #endregion

    #region $mul type precedence: Decimal128 > Double

    [Fact]
    public void Mul_Decimal128Field_ByDouble_ReturnsDecimal128()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/mul/
        //   "Decimal128 has the highest type precedence."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", new BsonDecimal128(3.0m) } });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument("$mul", new BsonDocument("val", 2.0)));  // Double multiplier

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        Assert.Equal(BsonType.Decimal128, result["val"].BsonType);
    }

    #endregion

    #region ReplaceOne does not emit change event when unmodified

    [Fact]
    public void ReplaceOne_SameDocument_ModifiedCountIsZero()
    {
        // Ref: https://www.mongodb.com/docs/manual/changeStreams/
        //   "Change stream events are only emitted when the document is actually modified."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "x", 42 } });

        // Replace with identical document
        var result = col.ReplaceOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument { { "_id", 1 }, { "x", 42 } });

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(0, result.ModifiedCount);
    }

    [Fact]
    public void ReplaceOne_DifferentDocument_ModifiedCountIsOne()
    {
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "x", 42 } });

        var result = col.ReplaceOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument { { "_id", 1 }, { "x", 99 } });

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(1, result.ModifiedCount);
    }

    #endregion

    #region CreateCollection duplicate throws

    [Fact]
    public void CreateCollection_Duplicate_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/create/
        //   "If the collection already exists, returns NamespaceExists (48)."
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");

        db.CreateCollection("mycol");

        var ex = Assert.Throws<MongoCommandException>(() => db.CreateCollection("mycol"));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void CreateCollection_DifferentNames_Succeeds()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");

        db.CreateCollection("col1");
        db.CreateCollection("col2");  // Should not throw

        var names = db.ListCollectionNames().ToList();
        Assert.Contains("col1", names);
        Assert.Contains("col2", names);
    }

    #endregion
}
