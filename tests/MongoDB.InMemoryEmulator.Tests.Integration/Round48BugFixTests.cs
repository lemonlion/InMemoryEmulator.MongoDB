using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

public class Round48BugFixTests
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public Round48BugFixTests()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("test_round48");
        _collection = db.GetCollection<BsonDocument>("items");
    }

    // Bug 1: $in aggregation expression — cross-type numeric comparison
    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/in/
    //   "$in: Returns a boolean indicating whether a specified value is in an array."
    //   MongoDB uses value-based comparison: 5 (int) == 5.0 (double)
    [Fact]
    public async Task Aggregate_InExpression_CrossTypeNumeric_ReturnsTrue()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1)
            .Add("val", new BsonInt32(5))
            .Add("arr", new BsonArray { new BsonDouble(5.0), new BsonDouble(6.0) }));

        var pipeline = new[] { BsonDocument.Parse("{ $project: { found: { $in: ['$val', '$arr'] } } }") };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();

        Assert.True(doc["found"].AsBoolean);
    }

    [Fact]
    public async Task Aggregate_InExpression_Int64InDoubleArray_ReturnsTrue()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1)
            .Add("val", new BsonInt64(10))
            .Add("arr", new BsonArray { new BsonDouble(10.0), new BsonDouble(20.0) }));

        var pipeline = new[] { BsonDocument.Parse("{ $project: { found: { $in: ['$val', '$arr'] } } }") };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();

        Assert.True(doc["found"].AsBoolean);
    }

    // Bug 5: $addToSet in $group — cross-type numeric deduplication
    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/addToSet/
    //   "Returns an array of all unique values that results from applying an expression to each document."
    //   5 (int) and 5.0 (double) are the same value → deduplicated.
    [Fact]
    public async Task Aggregate_AddToSet_CrossTypeNumeric_Deduplicates()
    {
        await _collection.InsertManyAsync(new[]
        {
            new BsonDocument("_id", 1).Add("grp", "A").Add("val", new BsonInt32(5)),
            new BsonDocument("_id", 2).Add("grp", "A").Add("val", new BsonDouble(5.0)),
            new BsonDocument("_id", 3).Add("grp", "A").Add("val", new BsonInt64(5)),
        });

        var pipeline = new[]
        {
            BsonDocument.Parse("{ $group: { _id: '$grp', vals: { $addToSet: '$val' } } }")
        };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();
        var vals = doc["vals"].AsBsonArray;

        Assert.Single(vals);
    }

    // Bug 6: $setUnion — cross-type numeric deduplication
    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setUnion/
    //   "Takes two or more arrays and returns an array containing the elements that appear in any input array."
    //   int 2 and double 2.0 are the same element.
    [Fact]
    public async Task Aggregate_SetUnion_CrossTypeNumeric_Deduplicates()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1)
            .Add("a", new BsonArray { new BsonInt32(1), new BsonInt32(2) })
            .Add("b", new BsonArray { new BsonDouble(2.0), new BsonDouble(3.0) }));

        var pipeline = new[] { BsonDocument.Parse("{ $project: { result: { $setUnion: ['$a', '$b'] } } }") };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();
        var arr = doc["result"].AsBsonArray;

        Assert.Equal(3, arr.Count); // [1, 2, 3] — not [1, 2, 2.0, 3]
    }

    [Fact]
    public async Task Aggregate_SetIntersection_CrossTypeNumeric_FindsMatch()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1)
            .Add("a", new BsonArray { new BsonInt32(1), new BsonInt32(2), new BsonInt32(3) })
            .Add("b", new BsonArray { new BsonDouble(2.0), new BsonDouble(4.0) }));

        var pipeline = new[] { BsonDocument.Parse("{ $project: { result: { $setIntersection: ['$a', '$b'] } } }") };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();
        var arr = doc["result"].AsBsonArray;

        Assert.Single(arr); // [2] — value 2 matches 2.0
    }

    // Bug 2: $dateToString — %Z produces +00:00 instead of +0000
    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateToString/
    //   "%Z: The minute offset from UTC as a number. For example, if the offset is +530, the return string will be +0530."
    [Fact]
    public async Task Aggregate_DateToString_PercentZ_NoColon()
    {
        await _collection.InsertOneAsync(new BsonDocument("_id", 1)
            .Add("d", new BsonDateTime(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc))));

        var pipeline = new[]
        {
            BsonDocument.Parse("{ $project: { formatted: { $dateToString: { date: '$d', format: '%H:%M%Z' } } } }")
        };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();

        Assert.Equal("10:30+0000", doc["formatted"].AsString);
    }

    // Bug 7: $push in $group — $$REMOVE sentinel leaks into results
    // Ref: https://www.mongodb.com/docs/manual/reference/aggregation-variables/
    //   "$$REMOVE evaluates to the missing value... Use the $$REMOVE variable to conditionally exclude a field."
    [Fact]
    public async Task Aggregate_Push_WithRemove_ExcludesRemovedValues()
    {
        await _collection.InsertManyAsync(new[]
        {
            new BsonDocument("_id", 1).Add("grp", "A").Add("val", 10).Add("include", true),
            new BsonDocument("_id", 2).Add("grp", "A").Add("val", 20).Add("include", false),
            new BsonDocument("_id", 3).Add("grp", "A").Add("val", 30).Add("include", true),
        });

        var pipeline = new[]
        {
            BsonDocument.Parse(@"{ $group: { _id: '$grp', vals: { $push: { $cond: ['$include', '$val', '$$REMOVE'] } } } }")
        };
        var result = await _collection.AggregateAsync<BsonDocument>(pipeline);
        var doc = (await result.ToListAsync()).First();
        var vals = doc["vals"].AsBsonArray;

        Assert.Equal(2, vals.Count);
        Assert.Contains(new BsonInt32(10), vals);
        Assert.Contains(new BsonInt32(30), vals);
    }
}
