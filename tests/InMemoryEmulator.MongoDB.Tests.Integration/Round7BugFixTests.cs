using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 7 bug fix tests covering:
/// 1. $multiply type preservation (Int32/Int64 when all inputs are integers)
/// 2. $mod type preservation (Int32/Int64 when both operands are integers)
/// 3. $subtract type preservation (Int32/Int64 for numeric subtraction)
/// 4. $add type preservation (Int32/Int64 for numeric addition)
/// 5. $elemMatch projection with scalar array elements
/// </summary>
[Collection("Integration")]
public class Round7BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round7BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Bug 1: $multiply type preservation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Multiply_Int32Inputs_ReturnsInt32()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/multiply/
        //   "The arguments can be any valid expression as long as they resolve to numbers."
        //   Type promotion: integer → long → double → decimal
        var col = _fixture.GetCollection<BsonDocument>("mul_int");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", 3 }, { "b", 4 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "product", new BsonDocument("$multiply", new BsonArray { "$a", "$b" }) }
            }))
            .FirstAsync();

        Assert.Equal(12, result["product"].ToInt32());
        Assert.True(result["product"].BsonType == BsonType.Int32 || result["product"].BsonType == BsonType.Int64,
            $"Expected Int32 or Int64 but got {result["product"].BsonType}");
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Multiply_Int64Input_ReturnsInt64()
    {
        var col = _fixture.GetCollection<BsonDocument>("mul_long");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", (long)5 }, { "b", 3 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "product", new BsonDocument("$multiply", new BsonArray { "$a", "$b" }) }
            }))
            .FirstAsync();

        Assert.Equal(BsonType.Int64, result["product"].BsonType);
        Assert.Equal(15L, result["product"].AsInt64);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Multiply_DoubleInput_ReturnsDouble()
    {
        var col = _fixture.GetCollection<BsonDocument>("mul_dbl");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", 3 }, { "b", 2.5 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "product", new BsonDocument("$multiply", new BsonArray { "$a", "$b" }) }
            }))
            .FirstAsync();

        Assert.Equal(BsonType.Double, result["product"].BsonType);
        Assert.Equal(7.5, result["product"].AsDouble);
    }

    #endregion

    #region Bug 2: $mod type preservation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Mod_Int32Inputs_ReturnsInt32()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/mod/
        var col = _fixture.GetCollection<BsonDocument>("mod_int");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", 10 }, { "b", 3 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "remainder", new BsonDocument("$mod", new BsonArray { "$a", "$b" }) }
            }))
            .FirstAsync();

        Assert.Equal(1, result["remainder"].ToInt32());
        Assert.True(result["remainder"].BsonType == BsonType.Int32 || result["remainder"].BsonType == BsonType.Int64,
            $"Expected Int32 or Int64 but got {result["remainder"].BsonType}");
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Mod_DoubleInput_ReturnsDouble()
    {
        var col = _fixture.GetCollection<BsonDocument>("mod_dbl");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", 10.5 }, { "b", 3 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "remainder", new BsonDocument("$mod", new BsonArray { "$a", "$b" }) }
            }))
            .FirstAsync();

        Assert.Equal(BsonType.Double, result["remainder"].BsonType);
        Assert.Equal(1.5, result["remainder"].AsDouble);
    }

    #endregion

    #region Bug 3: $subtract type preservation (numeric)

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Subtract_Int32Inputs_ReturnsInt32()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/subtract/
        var col = _fixture.GetCollection<BsonDocument>("sub_int");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", 10 }, { "b", 3 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "diff", new BsonDocument("$subtract", new BsonArray { "$a", "$b" }) }
            }))
            .FirstAsync();

        Assert.Equal(7, result["diff"].ToInt32());
        Assert.True(result["diff"].BsonType == BsonType.Int32 || result["diff"].BsonType == BsonType.Int64,
            $"Expected Int32 or Int64 but got {result["diff"].BsonType}");
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Subtract_Int64Input_ReturnsInt64()
    {
        var col = _fixture.GetCollection<BsonDocument>("sub_long");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", (long)100 }, { "b", 30 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "diff", new BsonDocument("$subtract", new BsonArray { "$a", "$b" }) }
            }))
            .FirstAsync();

        Assert.Equal(BsonType.Int64, result["diff"].BsonType);
        Assert.Equal(70L, result["diff"].AsInt64);
    }

    #endregion

    #region Bug 4: $add type preservation (numeric)

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Add_Int32Inputs_ReturnsInt32()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/add/
        var col = _fixture.GetCollection<BsonDocument>("add_int");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", 5 }, { "b", 7 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "total", new BsonDocument("$add", new BsonArray { "$a", "$b" }) }
            }))
            .FirstAsync();

        Assert.Equal(12, result["total"].ToInt32());
        Assert.True(result["total"].BsonType == BsonType.Int32 || result["total"].BsonType == BsonType.Int64,
            $"Expected Int32 or Int64 but got {result["total"].BsonType}");
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Add_Int64Input_ReturnsInt64()
    {
        var col = _fixture.GetCollection<BsonDocument>("add_long");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", (long)100 }, { "b", 50 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "total", new BsonDocument("$add", new BsonArray { "$a", "$b" }) }
            }))
            .FirstAsync();

        Assert.Equal(BsonType.Int64, result["total"].BsonType);
        Assert.Equal(150L, result["total"].AsInt64);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Add_DoubleInput_ReturnsDouble()
    {
        var col = _fixture.GetCollection<BsonDocument>("add_dbl");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "a", 5 }, { "b", 2.5 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "total", new BsonDocument("$add", new BsonArray { "$a", "$b" }) }
            }))
            .FirstAsync();

        Assert.Equal(BsonType.Double, result["total"].BsonType);
        Assert.Equal(7.5, result["total"].AsDouble);
    }

    #endregion

    #region Bug 5: $elemMatch projection with scalar array elements

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ElemMatch_Projection_ScalarElements_MatchesFirst()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/elemMatch/
        //   "The $elemMatch operator limits the contents of an <array> field from the query results
        //    to contain only the first element matching the $elemMatch condition."
        var col = _fixture.GetCollection<BsonDocument>("elemmatch_scalar");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 75, 92, 88, 95 } }
        });

        var filter = Builders<BsonDocument>.Filter.Eq("_id", 1);
        var projection = Builders<BsonDocument>.Projection
            .ElemMatch<BsonValue>("scores", new BsonDocument("$gte", 90));

        var result = await col.Find(filter).Project(projection).FirstAsync();

        Assert.True(result.Contains("scores"));
        var scores = result["scores"].AsBsonArray;
        Assert.Single(scores);
        Assert.Equal(92, scores[0].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ElemMatch_Projection_ScalarStrings_MatchesFirst()
    {
        var col = _fixture.GetCollection<BsonDocument>("elemmatch_str");
        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "tags", new BsonArray { "alpha", "beta", "gamma" } }
        });

        var filter = Builders<BsonDocument>.Filter.Eq("_id", 1);
        var projection = Builders<BsonDocument>.Projection
            .ElemMatch<BsonValue>("tags", new BsonDocument("$eq", "beta"));

        var result = await col.Find(filter).Project(projection).FirstAsync();

        Assert.True(result.Contains("tags"));
        var tags = result["tags"].AsBsonArray;
        Assert.Single(tags);
        Assert.Equal("beta", tags[0].AsString);
    }

    #endregion
}
