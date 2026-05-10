using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace MongoDB.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Round 31: DropCollection metadata leak, Aggregate bypasses view pipeline,
/// schema validation not enforced on writes, RenameCollection metadata migration.
/// </summary>
public class Round31BugFixTests
{
    #region Bug 1: DropCollection SDK path leaks metadata

    [Fact]
    public void DropCollection_SDK_removes_validator_metadata()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/drop/
        //   "Removes an entire collection from a database." — all metadata must go.
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("drop_sdk_val_test");

        // Create collection with a validator via RunCommand
        db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "create", "validated" },
            { "validator", new BsonDocument("age", new BsonDocument("$gte", 18)) },
            { "validationAction", "error" }
        });

        // Drop via SDK path (not RunCommand)
        db.DropCollection("validated");

        // Recreate without validator
        db.CreateCollection("validated");
        var collection = db.GetCollection<BsonDocument>("validated");

        // Insert a document that would fail the old validator — should succeed
        collection.InsertOne(new BsonDocument { { "age", 5 } });
        Assert.Equal(1, collection.CountDocuments(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public void DropCollection_SDK_removes_view_from_ListCollectionNames()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/drop/
        //   Drop should remove views as well.
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("drop_sdk_view_test");

        // Create a source collection and a view
        var source = db.GetCollection<BsonDocument>("source");
        source.InsertOne(new BsonDocument { { "x", 1 } });

        db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "create", "myView" },
            { "viewOn", "source" },
            { "pipeline", new BsonArray { new BsonDocument("$match", new BsonDocument("x", 1)) } }
        });

        // Drop the view via SDK path
        db.DropCollection("myView");

        // View should no longer appear in collection names
        var names = db.ListCollectionNames().ToList();
        Assert.DoesNotContain("myView", names);
    }

    #endregion

    #region Bug 2: Aggregate bypasses view pipeline

    [Fact]
    public void Aggregate_on_view_applies_view_pipeline()
    {
        // Ref: https://www.mongodb.com/docs/manual/core/views/
        //   "When clients query a view, MongoDB appends the client query to the underlying pipeline."
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("agg_on_view_test");

        // Create source collection with mixed data
        var source = db.GetCollection<BsonDocument>("products");
        source.InsertMany(new[]
        {
            new BsonDocument { { "name", "A" }, { "active", true } },
            new BsonDocument { { "name", "B" }, { "active", false } },
            new BsonDocument { { "name", "C" }, { "active", true } }
        });

        // Create a view that only shows active products
        db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "create", "activeProducts" },
            { "viewOn", "products" },
            { "pipeline", new BsonArray { new BsonDocument("$match", new BsonDocument("active", true)) } }
        });

        // Aggregate on the view with an empty pipeline
        var viewCol = db.GetCollection<BsonDocument>("activeProducts");
        var results = viewCol.Aggregate(
            PipelineDefinition<BsonDocument, BsonDocument>.Create(Array.Empty<BsonDocument>())).ToList();

        // Should only see active products (2), not all 3
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r["active"].AsBoolean));
    }

    #endregion

    #region Bug 3: Schema validation not enforced on writes

    [Fact]
    public void InsertOne_rejects_document_that_fails_schema_validation()
    {
        // Ref: https://www.mongodb.com/docs/manual/core/schema-validation/
        //   "MongoDB can apply validation rules during inserts and updates."
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("schema_insert_test");

        // Create collection with validator: age must be >= 18
        db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "create", "users" },
            { "validator", new BsonDocument("age", new BsonDocument("$gte", 18)) },
            { "validationAction", "error" }
        });

        var collection = db.GetCollection<BsonDocument>("users");

        // Inserting invalid document should throw with error code 121
        var ex = Assert.Throws<MongoWriteException>(() =>
            collection.InsertOne(new BsonDocument { { "name", "Minor" }, { "age", 10 } }));
        Assert.Equal(121, ex.WriteError.Code);
    }

    [Fact]
    public void UpdateOne_rejects_result_that_fails_schema_validation()
    {
        // Ref: https://www.mongodb.com/docs/manual/core/schema-validation/
        //   "MongoDB can apply validation rules during inserts and updates."
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("schema_upd_test");

        db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "create", "users" },
            { "validator", new BsonDocument("age", new BsonDocument("$gte", 18)) },
            { "validationAction", "error" }
        });

        var collection = db.GetCollection<BsonDocument>("users");
        collection.InsertOne(new BsonDocument { { "name", "Adult" }, { "age", 25 } });

        // Update to invalid value should throw
        var ex = Assert.Throws<MongoWriteException>(() =>
            collection.UpdateOne(
                Builders<BsonDocument>.Filter.Eq("name", "Adult"),
                Builders<BsonDocument>.Update.Set("age", 10)));
        Assert.Equal(121, ex.WriteError.Code);
    }

    [Fact]
    public void ReplaceOne_rejects_document_that_fails_schema_validation()
    {
        // Ref: https://www.mongodb.com/docs/manual/core/schema-validation/
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("schema_repl_test");

        db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "create", "users" },
            { "validator", new BsonDocument("age", new BsonDocument("$gte", 18)) },
            { "validationAction", "error" }
        });

        var collection = db.GetCollection<BsonDocument>("users");
        collection.InsertOne(new BsonDocument { { "name", "Adult" }, { "age", 25 } });

        // Replace with invalid doc should throw
        var ex = Assert.Throws<MongoWriteException>(() =>
            collection.ReplaceOne(
                Builders<BsonDocument>.Filter.Eq("name", "Adult"),
                new BsonDocument { { "name", "Adult" }, { "age", 5 } }));
        Assert.Equal(121, ex.WriteError.Code);
    }

    #endregion

    #region Bug 4: RenameCollection doesn't migrate metadata

    [Fact]
    public void RenameCollection_preserves_schema_validator()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/renameCollection/
        //   "Changes the name of an existing collection." — all properties survive.
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("rename_val_test");

        // Create collection with validator
        db.RunCommand<BsonDocument>(new BsonDocument
        {
            { "create", "old_col" },
            { "validator", new BsonDocument("age", new BsonDocument("$gte", 18)) },
            { "validationAction", "error" }
        });

        var oldCol = db.GetCollection<BsonDocument>("old_col");
        oldCol.InsertOne(new BsonDocument { { "name", "Adult" }, { "age", 25 } });

        // Rename the collection
        db.RenameCollection("old_col", "new_col");

        // The validator should follow the collection
        var newCol = db.GetCollection<BsonDocument>("new_col");
        var ex = Assert.Throws<MongoWriteException>(() =>
            newCol.InsertOne(new BsonDocument { { "name", "Minor" }, { "age", 5 } }));
        Assert.Equal(121, ex.WriteError.Code);
    }

    #endregion
}
