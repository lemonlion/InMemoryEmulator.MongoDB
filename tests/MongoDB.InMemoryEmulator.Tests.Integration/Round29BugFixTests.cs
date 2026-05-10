using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

[Collection("Integration")]
public class Round29BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round29BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #region Bug 1: Dot-notation inclusion projection into arrays

    // Ref: https://www.mongodb.com/docs/manual/tutorial/project-fields-from-query-results/
    //   "You can use dot notation to project specific fields inside documents embedded in an array."
    //   e.g., { "items.name": 1 } should project the name field from each element of the items array.

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Find_Projection_DotNotation_Into_Array_Inclusion()
    {
        var col = _fixture.GetCollection<BsonDocument>("proj_dot_arr_incl");

        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "A" }, { "price", 10 } },
                    new BsonDocument { { "name", "B" }, { "price", 20 } }
                }
            }
        });

        var projection = Builders<BsonDocument>.Projection.Include("items.name");
        var result = await col.Find(FilterDefinition<BsonDocument>.Empty)
            .Project(projection)
            .FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.True(result.Contains("items"), "Result should contain the 'items' field");
        var items = result["items"].AsBsonArray;
        Assert.Equal(2, items.Count);
        // Each element should only have the "name" field
        Assert.Equal("A", items[0].AsBsonDocument["name"].AsString);
        Assert.Equal("B", items[1].AsBsonDocument["name"].AsString);
        Assert.False(items[0].AsBsonDocument.Contains("price"), "price should be excluded from projected array elements");
        Assert.False(items[1].AsBsonDocument.Contains("price"), "price should be excluded from projected array elements");
    }

    #endregion

    #region Bug 2: Dot-notation exclusion projection into arrays

    // Ref: https://www.mongodb.com/docs/manual/tutorial/project-fields-from-query-results/
    //   "You can use dot notation to suppress fields in embedded documents."
    //   e.g., { "items.price": 0 } should remove price from each array element.

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Find_Projection_DotNotation_Into_Array_Exclusion()
    {
        var col = _fixture.GetCollection<BsonDocument>("proj_dot_arr_excl");

        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "A" }, { "price", 10 } },
                    new BsonDocument { { "name", "B" }, { "price", 20 } }
                }
            }
        });

        var projection = Builders<BsonDocument>.Projection.Exclude("items.price");
        var result = await col.Find(FilterDefinition<BsonDocument>.Empty)
            .Project(projection)
            .FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.True(result.Contains("items"), "Result should contain the 'items' field");
        var items = result["items"].AsBsonArray;
        Assert.Equal(2, items.Count);
        Assert.Equal("A", items[0].AsBsonDocument["name"].AsString);
        Assert.Equal("B", items[1].AsBsonDocument["name"].AsString);
        Assert.False(items[0].AsBsonDocument.Contains("price"), "price should be excluded");
        Assert.False(items[1].AsBsonDocument.Contains("price"), "price should be excluded");
    }

    #endregion

    #region Bug 3: GridFS creates a chunk for empty files

    // Ref: https://www.mongodb.com/docs/manual/core/gridfs/#gridfs-chunks
    //   "GridFS divides the file into chunks... For files with length zero,
    //    there are no documents in the chunks collection."

    [Fact]
    [Trait(TestTraits.Target, TestTraits.InMemoryOnly)]
    public void GridFS_EmptyFile_HasNoChunks()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        var bucket = new InMemoryGridFSBucket(db, new GridFSBucketOptions { BucketName = "fs" });
        var chunksCollection = db.GetCollection<BsonDocument>("fs.chunks");

        var fileId = bucket.UploadFromBytes("empty.bin", Array.Empty<byte>());

        var chunks = chunksCollection.Find(Builders<BsonDocument>.Filter.Eq("files_id", fileId)).ToList();
        Assert.Empty(chunks);

        // file doc should still exist with length 0
        var filesCollection = db.GetCollection<BsonDocument>("fs.files");
        var fileDoc = filesCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", fileId)).FirstOrDefault();
        Assert.NotNull(fileDoc);
        Assert.Equal(0L, fileDoc["length"].ToInt64());
    }

    #endregion

    #region Bug 4: Aggregation $project with dot-notation into arrays

    // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/project/
    //   "You can use dot notation to include fields in embedded documents."
    //   When a field path traverses an array, only the specified sub-field
    //   is projected from each array element.

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Aggregate_Project_DotNotation_Into_Array()
    {
        var col = _fixture.GetCollection<BsonDocument>("agg_proj_dot_arr");

        await col.InsertOneAsync(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "A" }, { "price", 10 } },
                    new BsonDocument { { "name", "B" }, { "price", 20 } }
                }
            }
        });

        var pipeline = new BsonDocument[]
        {
            new BsonDocument("$project", new BsonDocument("items.name", 1))
        };

        var results = await col.AggregateAsync<BsonDocument>(
            PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline));
        var result = await results.FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.True(result.Contains("items"), "Result should contain 'items' field");
        var items = result["items"].AsBsonArray;
        Assert.Equal(2, items.Count);
        Assert.Equal("A", items[0].AsBsonDocument["name"].AsString);
        Assert.Equal("B", items[1].AsBsonDocument["name"].AsString);
        Assert.False(items[0].AsBsonDocument.Contains("price"), "price should not be in projected result");
    }

    #endregion
}
