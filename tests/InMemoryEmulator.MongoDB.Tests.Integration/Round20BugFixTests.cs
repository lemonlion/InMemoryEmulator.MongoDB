using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

[Collection("Integration")]
public class Round20BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round20BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region BulkWrite ordered:false reports errors

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task BulkWrite_UnorderedWithDuplicateKey_ThrowsWithErrors()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.bulkWrite/
        //   "If ordered is set to false, documents are inserted in an unordered format
        //    and may be reordered by mongod to increase performance.
        //    Applications should not depend on ordering of inserts if using an unordered bulkWrite()."
        //   "unordered operations that result in errors will still report the errors."
        var col = _fixture.GetCollection<BsonDocument>("r20_bulkwrite_errors");

        // Pre-insert a document to cause duplicate key
        await col.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "val", "existing" } });

        var models = new WriteModel<BsonDocument>[]
        {
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 1 }, { "val", "dup" } }),   // duplicate key
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 2 }, { "val", "new" } }),   // should succeed
        };

        // MongoDB throws MongoBulkWriteException with errors for unordered bulk writes
        var ex = await Assert.ThrowsAsync<MongoBulkWriteException<BsonDocument>>(
            () => col.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false }));

        // The error should report the duplicate key error
        Assert.NotEmpty(ex.WriteErrors);

        // The second insert should have succeeded despite the first failing
        var doc2 = await col.Find(Builders<BsonDocument>.Filter.Eq("_id", 2)).FirstOrDefaultAsync();
        Assert.NotNull(doc2);
    }

    #endregion

    #region $all with $elemMatch

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task All_WithElemMatch_MatchesCorrectly()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/all/
        //   "Use $all with $elemMatch: Use $all with $elemMatch to match documents
        //    where the array field contains at least one element that matches each $elemMatch condition."
        var col = _fixture.GetCollection<BsonDocument>("r20_all_elemmatch");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument
            {
                { "_id", 1 },
                { "results", new BsonArray
                    {
                        new BsonDocument { { "product", "abc" }, { "score", 10 } },
                        new BsonDocument { { "product", "xyz" }, { "score", 5 } }
                    }
                }
            },
            new BsonDocument
            {
                { "_id", 2 },
                { "results", new BsonArray
                    {
                        new BsonDocument { { "product", "abc" }, { "score", 8 } },
                        new BsonDocument { { "product", "xyz" }, { "score", 7 } }
                    }
                }
            }
        });

        // Find documents where results array has at least one element matching score >= 8
        // AND at least one element matching product = "xyz"
        var filter = new BsonDocument("results", new BsonDocument("$all", new BsonArray
        {
            new BsonDocument("$elemMatch", new BsonDocument("score", new BsonDocument("$gte", 8))),
            new BsonDocument("$elemMatch", new BsonDocument("product", "xyz"))
        }));

        var results = await col.Find(filter).ToListAsync();
        Assert.Equal(2, results.Count); // Both docs match
    }

    #endregion

    #region $mod exact comparison

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Mod_UsesExactComparison_NotFuzzy()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/mod/
        //   "Changed in version 7.2: If the value of a field is not an integer,
        //    the $mod expression rounds the value towards zero to the nearest integer
        //    before performing the operation."
        var col = _fixture.GetCollection<BsonDocument>("r20_mod_exact");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 10 } },       // truncate(10)=10, 10 % 3 = 1 → match
            new BsonDocument { { "_id", 2 }, { "x", 11.9 } },     // truncate(11.9)=11, 11 % 3 = 2 → no match
        });

        var filter = Builders<BsonDocument>.Filter.Mod("x", 3, 1);
        var results = await col.Find(filter).ToListAsync();

        // Only doc with _id=1 should match (truncate(11.9)=11, 11%3=2 ≠ 1)
        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    #endregion

    #region BulkWrite ordered:false continues after error

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task BulkWrite_UnorderedContinuesAfterError()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.bulkWrite/
        //   "If ordered is false, the server processes all operations,
        //    and returns errors for any that fail."
        var col = _fixture.GetCollection<BsonDocument>("r20_bulkwrite_continue");

        await col.InsertOneAsync(new BsonDocument { { "_id", 1 } });

        var models = new WriteModel<BsonDocument>[]
        {
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 1 } }),  // dup key
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 2 } }),  // should succeed
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 1 } }),  // dup key again
            new InsertOneModel<BsonDocument>(new BsonDocument { { "_id", 3 } }),  // should succeed
        };

        var ex = await Assert.ThrowsAsync<MongoBulkWriteException<BsonDocument>>(
            () => col.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false }));

        // Should have exactly 2 errors (two dup key inserts)
        Assert.Equal(2, ex.WriteErrors.Count);

        // Both successful inserts should be in the collection
        var count = await col.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        Assert.Equal(3, count); // Original _id:1 + _id:2 + _id:3
    }

    #endregion
}
