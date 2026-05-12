using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

public class Round51BugFixTests
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public Round51BugFixTests()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round51");
        _collection = db.GetCollection<BsonDocument>("items");
    }

    [Fact]
    public async Task Aggregate_ToDate_ObjectId_ExtractsTimestamp()
    {
        var oid = new ObjectId("507f1f77bcf86cd799439011");
        await _collection.InsertOneAsync(new BsonDocument("_id", oid));

        var pipeline = new[]
        {
            BsonDocument.Parse("{ $project: { created: { $toDate: '$_id' } } }")
        };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();

        var expectedDate = oid.CreationTime;
        var actualDate = doc["created"].ToUniversalTime();
        Assert.Equal(expectedDate, actualDate);
    }

    [Fact]
    public async Task InsertMany_Ordered_DuplicateKey_PreservesErrorCode()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1).Add("x", 1));

        var ex = await Assert.ThrowsAsync<MongoBulkWriteException<BsonDocument>>(async () =>
        {
            await _collection.InsertManyAsync(new[]
            {
                new BsonDocument("_id", 2).Add("x", 2),
                new BsonDocument("_id", 1).Add("x", 3),
                new BsonDocument("_id", 3).Add("x", 4),
            }, new InsertManyOptions { IsOrdered = true });
        });

        Assert.Single(ex.WriteErrors);
        Assert.Equal(11000, ex.WriteErrors[0].Code);
    }
}