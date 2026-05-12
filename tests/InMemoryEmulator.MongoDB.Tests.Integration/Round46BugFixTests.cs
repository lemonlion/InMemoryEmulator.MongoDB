using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 46: $slice negative count validation, $project inclusion/exclusion mix validation
/// </summary>
public class Round46BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection(string name = "items")
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>(name);
    }

    #region $slice: negative count with position throws error

    [Fact]
    public void Aggregate_Slice_NegativeCountWithPosition_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/slice/
        //   "If <position> is specified, <n> must resolve to a positive integer."
        var col = CreateCollection("slice_neg_r46");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "arr", new BsonArray { 1, 2, 3, 4, 5 } } });

        var ex = Assert.Throws<MongoCommandException>(() =>
            col.Aggregate()
                .Project(new BsonDocument("s",
                    new BsonDocument("$slice", new BsonArray { "$arr", 1, -2 })))
                .First());

        Assert.Contains("positive", ex.Message);
    }

    [Fact]
    public void Aggregate_Slice_PositiveCountWithPosition_Works()
    {
        var col = CreateCollection("slice_pos_r46");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "arr", new BsonArray { 1, 2, 3, 4, 5 } } });

        var result = col.Aggregate()
            .Project(new BsonDocument("s",
                new BsonDocument("$slice", new BsonArray { "$arr", 1, 2 })))
            .First();

        Assert.Equal(new BsonArray { 2, 3 }, result["s"].AsBsonArray);
    }

    [Fact]
    public void Aggregate_Slice_NegativeN_TwoArgs_ReturnsLastN()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/slice/
        //   "If negative, $slice returns up to the last |n| elements"
        var col = CreateCollection("slice_neg2_r46");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "arr", new BsonArray { 1, 2, 3, 4, 5 } } });

        var result = col.Aggregate()
            .Project(new BsonDocument("s",
                new BsonDocument("$slice", new BsonArray { "$arr", -3 })))
            .First();

        Assert.Equal(new BsonArray { 3, 4, 5 }, result["s"].AsBsonArray);
    }

    #endregion

    #region $project: cannot mix inclusion and exclusion

    [Fact]
    public void Aggregate_Project_MixInclusionExclusion_Throws()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/project/
        //   "If you specify the exclusion of a field other than _id, you cannot employ any other
        //    $project specification forms."
        var col = CreateCollection("proj_mix_r46");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", 1 }, { "b", 2 }, { "c", 3 } });

        var ex = Assert.Throws<MongoCommandException>(() =>
            col.Aggregate()
                .Project(new BsonDocument { { "a", 1 }, { "b", 0 } })
                .First());

        Assert.Contains("exclusion", ex.Message);
    }

    [Fact]
    public void Aggregate_Project_InclusionWithIdExclusion_ValidAndWorks()
    {
        // _id: 0 with inclusions is valid
        var col = CreateCollection("proj_id_excl_r46");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", 10 }, { "b", 20 } });

        var result = col.Aggregate()
            .Project(new BsonDocument { { "_id", 0 }, { "a", 1 } })
            .First();

        Assert.False(result.Contains("_id"));
        Assert.Equal(10, result["a"].AsInt32);
        Assert.False(result.Contains("b"));
    }

    [Fact]
    public void Aggregate_Project_PureExclusion_Works()
    {
        var col = CreateCollection("proj_excl_r46");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", 10 }, { "b", 20 }, { "c", 30 } });

        var result = col.Aggregate()
            .Project(new BsonDocument { { "b", 0 } })
            .First();

        Assert.Equal(1, result["_id"].AsInt32);
        Assert.Equal(10, result["a"].AsInt32);
        Assert.False(result.Contains("b"));
        Assert.Equal(30, result["c"].AsInt32);
    }

    [Fact]
    public void Aggregate_Project_ExclusionWithExpression_Throws()
    {
        // Mixing exclusion with expressions is also invalid
        var col = CreateCollection("proj_excl_expr_r46");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "a", 10 }, { "b", 20 } });

        var ex = Assert.Throws<MongoCommandException>(() =>
            col.Aggregate()
                .Project(new BsonDocument { { "a", 0 }, { "computed", new BsonDocument("$add", new BsonArray { "$b", 1 }) } })
                .First());

        Assert.Contains("exclusion", ex.Message);
    }

    #endregion
}
