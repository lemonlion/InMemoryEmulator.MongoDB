using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

public class Round49BugFixTests
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public Round49BugFixTests()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round49");
        _collection = db.GetCollection<BsonDocument>("items");
    }

    // Bug 4: $convert ObjectId → date should extract the timestamp
    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/convert/
    //   "Returns a date that corresponds to the timestamp of the ObjectId."
    [Fact]
    public async Task Aggregate_Convert_ObjectIdToDate_ExtractsTimestamp()
    {
        var oid = new ObjectId("507f1f77bcf86cd799439011");
        await _collection.InsertOneAsync(new BsonDocument("_id", oid));

        var pipeline = new[]
        {
            BsonDocument.Parse("{ $project: { created: { $convert: { input: '$_id', to: 'date' } } } }")
        };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();

        // ObjectId "507f1f77bcf86cd799439011" has timestamp 2012-10-17T21:13:27Z
        var expectedDate = oid.CreationTime;
        var actualDate = doc["created"].ToUniversalTime();
        Assert.Equal(expectedDate, actualDate);
    }

    [Fact]
    public async Task Aggregate_Convert_ObjectIdToDate_NumericCode()
    {
        var oid = new ObjectId("507f1f77bcf86cd799439011");
        await _collection.InsertOneAsync(new BsonDocument("_id", oid));

        // to: 9 is "date" by BSON type code
        var pipeline = new[]
        {
            BsonDocument.Parse("{ $project: { created: { $convert: { input: '$_id', to: 9 } } } }")
        };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();

        var expectedDate = oid.CreationTime;
        var actualDate = doc["created"].ToUniversalTime();
        Assert.Equal(expectedDate, actualDate);
    }

    // Bug 8: $convert non-string to objectId should throw (or use onError)
    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/convert/
    //   Only "String" is listed as valid input type for objectId conversion.
    [Fact]
    public async Task Aggregate_Convert_IntToObjectId_UsesOnError()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1).Add("val", 12345));

        var pipeline = new[]
        {
            BsonDocument.Parse("{ $project: { result: { $convert: { input: '$val', to: 'objectId', onError: 'failed' } } } }")
        };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();

        Assert.Equal("failed", doc["result"].AsString);
    }

    // Bug 9: $convert with unknown type should throw (or use onError)
    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/convert/
    //   "to: Can be any valid expression that resolves to one of the following numeric or string identifiers."
    [Fact]
    public async Task Aggregate_Convert_UnknownType_UsesOnError()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1).Add("val", "hello"));

        var pipeline = new[]
        {
            BsonDocument.Parse("{ $project: { result: { $convert: { input: '$val', to: 'nonsense', onError: 'invalid_type' } } } }")
        };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();

        Assert.Equal("invalid_type", doc["result"].AsString);
    }

    [Fact]
    public async Task Aggregate_Convert_UnknownType_ThrowsWithoutOnError()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1).Add("val", "hello"));

        var pipeline = new[]
        {
            BsonDocument.Parse("{ $project: { result: { $convert: { input: '$val', to: 'nonsense' } } } }")
        };

        await Assert.ThrowsAsync<MongoCommandException>(async () =>
        {
            var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
            await result.ToListAsync();
        });
    }
}
