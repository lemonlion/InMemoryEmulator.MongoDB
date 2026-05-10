using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 8 bug fix tests covering:
/// 1. $cond with invalid argument count throws proper error
/// </summary>
[Collection("Integration")]
public class Round8BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round8BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region $cond validation

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Cond_ArrayForm_TooFewArgs_ThrowsDescriptiveError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/cond/
        //   "$cond requires exactly 3 arguments in array form: [if, then, else]"
        var col = _fixture.GetCollection<BsonDocument>("cond_invalid");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "x", 5 } });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument
            {
                { "result", new BsonDocument("$cond", new BsonArray { false, "yes" }) } // only 2 args, false condition hits missing 3rd
            })
        };

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => col.Aggregate<BsonDocument>(pipeline).ToListAsync());

        // Should get a descriptive MongoDB-style error, not ArgumentOutOfRangeException
        Assert.DoesNotContain("ArgumentOutOfRange", ex.GetType().Name);
        Assert.Contains("$cond", ex.Message);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Cond_ArrayForm_ValidThreeArgs_Works()
    {
        var col = _fixture.GetCollection<BsonDocument>("cond_valid");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "score", 85 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "grade", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$gte", new BsonArray { "$score", 80 }),
                        "pass",
                        "fail"
                    })
                }
            }))
            .FirstAsync();

        Assert.Equal("pass", result["grade"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Cond_DocumentForm_Works()
    {
        var col = _fixture.GetCollection<BsonDocument>("cond_doc_form");
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "score", 45 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "grade", new BsonDocument("$cond", new BsonDocument
                    {
                        { "if", new BsonDocument("$gte", new BsonArray { "$score", 50 }) },
                        { "then", "pass" },
                        { "else", "fail" }
                    })
                }
            }))
            .FirstAsync();

        Assert.Equal("fail", result["grade"].AsString);
    }

    #endregion
}
