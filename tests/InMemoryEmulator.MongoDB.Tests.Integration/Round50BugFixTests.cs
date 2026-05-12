using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

public class Round50BugFixTests
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public Round50BugFixTests()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round50");
        _collection = db.GetCollection<BsonDocument>("items");
    }

    // Bug 1: $unwind includeArrayIndex returns 0 for scalar values — should return null
    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/unwind/
    //   Example shows: { "_id": 3, "sizes": "M", "arrayIndex": null } for scalar field
    [Fact]
    public async Task Aggregate_Unwind_IncludeArrayIndex_ScalarValue_ReturnsNull()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1)
            .Add("item", "ABC")
            .Add("sizes", "M")); // scalar, not array

        var pipeline = new[]
        {
            BsonDocument.Parse(@"{ $unwind: { path: '$sizes', includeArrayIndex: 'idx', preserveNullAndEmptyArrays: true } }")
        };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();

        // Per MongoDB docs, scalar fields get arrayIndex: null (not 0)
        Assert.Equal(BsonNull.Value, doc["idx"]);
    }

    // Bug 3: $lookup concise correlated syntax ignores localField/foreignField
    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/lookup/#correlated-subqueries-using-concise-syntax
    //   MongoDB 5.0+ allows localField + foreignField + pipeline together.
    //   The equality match is performed BEFORE the pipeline runs.
    [Fact]
    public async Task Aggregate_Lookup_ConciseCorrelated_UsesLocalAndForeignField()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round50_lookup");
        var orders = db.GetCollection<BsonDocument>("orders");
        var products = db.GetCollection<BsonDocument>("products");

        await orders.InsertManyAsync(new[]
        {
            new BsonDocument("_id", 1).Add("product_id", "P1").Add("qty", 5),
            new BsonDocument("_id", 2).Add("product_id", "P2").Add("qty", 3),
        });
        await products.InsertManyAsync(new[]
        {
            new BsonDocument("_id", "P1").Add("name", "Widget").Add("stock", 100),
            new BsonDocument("_id", "P2").Add("name", "Gadget").Add("stock", 0),
            new BsonDocument("_id", "P3").Add("name", "Doohickey").Add("stock", 50),
        });

        // Concise correlated: localField + foreignField + pipeline
        // Should first match on product_id == _id, THEN filter by pipeline
        var pipeline = new[]
        {
            BsonDocument.Parse(@"{ $lookup: {
                from: 'products',
                localField: 'product_id',
                foreignField: '_id',
                pipeline: [
                    { $match: { stock: { $gt: 0 } } }
                ],
                as: 'matched_products'
            }}")
        };
        var result = await orders.AggregateAsync<BsonDocument>(pipeline);
        var docs = await result.ToListAsync();

        // Order 1 (P1): product P1 has stock=100 > 0, should match
        var order1 = docs.First(d => d["_id"] == 1);
        Assert.Single(order1["matched_products"].AsBsonArray);
        Assert.Equal("Widget", order1["matched_products"][0]["name"].AsString);

        // Order 2 (P2): product P2 has stock=0, NOT > 0, should NOT match
        var order2 = docs.First(d => d["_id"] == 2);
        Assert.Empty(order2["matched_products"].AsBsonArray);
    }

    // Bug 4: $max update operator doesn't distinguish missing vs null field
    // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/max/
    //   "If the field does not exist, the $max operator sets the field to the specified value."
    //   If field is null and update value is less than null in BSON order, should NOT update.
    [Fact]
    public async Task Update_Max_FieldIsNull_DoesNotUpdateWithLesserValue()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1).Add("score", BsonNull.Value));

        // BsonMinKey < BsonNull in BSON comparison order
        // $max should NOT update null to MinKey because MinKey < null
        var update = Builders<BsonDocument>.Update.Max("score", BsonMinKey.Value);
        await _collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            update);

        var doc = await _collection.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(BsonNull.Value, doc["score"]); // should remain null
    }

    [Fact]
    public async Task Update_Max_FieldIsMissing_SetsValue()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1));

        // Field doesn't exist — $max always sets
        var update = Builders<BsonDocument>.Update.Max("score", 10);
        await _collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            update);

        var doc = await _collection.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(10, doc["score"].AsInt32);
    }

    [Fact]
    public async Task Update_Max_FieldExists_UpdatesOnlyIfGreater()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1).Add("score", 50));

        // 30 < 50, should NOT update
        await _collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Max("score", 30));

        var doc = await _collection.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(50, doc["score"].AsInt32);

        // 70 > 50, should update
        await _collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Max("score", 70));

        doc = await _collection.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).FirstAsync();
        Assert.Equal(70, doc["score"].AsInt32);
    }
}
