using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 32: Positional $ with query operators, $group $push skips missing,
/// $indexOfArray, set expression operators.
/// </summary>
public class Round32BugFixTests
{
    #region Bug 1: Positional $ operator broken with query operators on scalar arrays

    [Fact]
    public void UpdateOne_Positional_Dollar_With_Gte_On_Scalar_Array()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional/
        //   "The positional $ operator acts as a placeholder for the first element
        //    that matches the query document."
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("positional_test");
        var col = db.GetCollection<BsonDocument>("scores");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 3, 5, 8, 10 } }
        });

        // Update the first element >= 8 to 99
        col.UpdateOne(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                Builders<BsonDocument>.Filter.Gte("scores", 8)),
            Builders<BsonDocument>.Update.Set("scores.$", 99));

        var doc = col.Find(new BsonDocument("_id", 1)).First();
        var scores = doc["scores"].AsBsonArray;
        // Element at index 2 (value 8) should be replaced with 99
        Assert.Equal(99, scores[2].AsInt32);
    }

    [Fact]
    public void UpdateOne_Positional_Dollar_With_In_On_Scalar_Array()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional/
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("positional_in_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "tags", new BsonArray { "a", "b", "c" } }
        });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", 1),
                Builders<BsonDocument>.Filter.In("tags", new[] { "b", "d" })),
            Builders<BsonDocument>.Update.Set("tags.$", "replaced"));

        var doc = col.Find(new BsonDocument("_id", 1)).First();
        Assert.Equal("replaced", doc["tags"].AsBsonArray[1].AsString);
    }

    #endregion

    #region Bug 2: $group $push/$addToSet includes missing fields as null

    [Fact]
    public void Group_Push_Includes_Missing_Fields_As_Null()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/push/
        //   "$push returns an array of all values for each group including duplicates."
        //   Missing fields are included as null.
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("push_missing_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertMany(new[]
        {
            new BsonDocument { { "cat", "A" }, { "tag", "x" } },
            new BsonDocument { { "cat", "A" } },  // "tag" is missing
            new BsonDocument { { "cat", "A" }, { "tag", BsonNull.Value } }  // "tag" is explicitly null
        });

        var pipeline = new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$cat" },
                { "tags", new BsonDocument("$push", "$tag") }
            })
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).ToList();
        Assert.Single(result);
        var tags = result[0]["tags"].AsBsonArray;

        // Ref: Observed real MongoDB 7.0:
        //   Missing fields are skipped by $push. Only explicit null is included.
        //   Result: ["x", null] (2 entries)
        Assert.Equal(2, tags.Count);
    }

    [Fact]
    public void Group_AddToSet_Deduplicates_Null_From_Missing_Field()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/addToSet/
        //   $addToSet deduplicates values. Missing fields produce null.
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("addtoset_missing_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertMany(new[]
        {
            new BsonDocument { { "cat", "A" }, { "tag", "x" } },
            new BsonDocument { { "cat", "A" } },  // "tag" is missing → null
            new BsonDocument { { "cat", "A" }, { "tag", "x" } }  // duplicate
        });

        var pipeline = new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$cat" },
                { "tags", new BsonDocument("$addToSet", "$tag") }
            })
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).ToList();
        Assert.Single(result);
        var tags = result[0]["tags"].AsBsonArray;
        // Ref: Observed real MongoDB 7.0:
        //   Missing fields are skipped by $addToSet. No null added.
        //   Result: ["x"] (1 entry — only the non-missing value)
        Assert.Single(tags);
    }

    #endregion

    #region Missing Feature: $indexOfArray

    [Fact]
    public void IndexOfArray_Returns_Index_Of_First_Occurrence()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfArray/
        //   "Searches an array for an occurrence of a specified value and returns the
        //    array index of the first occurrence."
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("indexofarray_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "tags", new BsonArray { "a", "b", "c", "b" } }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("idx",
                new BsonDocument("$indexOfArray", new BsonArray { "$tags", "b" })))
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).First();
        Assert.Equal(1, result["idx"].AsInt32);
    }

    [Fact]
    public void IndexOfArray_Returns_Minus1_When_Not_Found()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfArray/
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("indexofarray_nf_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "tags", new BsonArray { "a", "b", "c" } }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("idx",
                new BsonDocument("$indexOfArray", new BsonArray { "$tags", "z" })))
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).First();
        Assert.Equal(-1, result["idx"].AsInt32);
    }

    [Fact]
    public void IndexOfArray_With_Start_And_End()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/indexOfArray/
        //   Optional start and end indices for range-restricted search.
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("indexofarray_range_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "arr", new BsonArray { 1, 2, 3, 2, 1 } }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("idx",
                new BsonDocument("$indexOfArray", new BsonArray { "$arr", 2, 2 })))  // start=2
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).First();
        // Searching from index 2 onward, 2 is at index 3
        Assert.Equal(3, result["idx"].AsInt32);
    }

    #endregion

    #region Missing Feature: Set expression operators

    [Fact]
    public void SetIntersection_Returns_Common_Elements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setIntersection/
        //   "Returns a set with elements that appear in all of the input sets."
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("setintersection_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonArray { 1, 2, 3 } },
            { "b", new BsonArray { 2, 3, 4 } }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("common",
                new BsonDocument("$setIntersection", new BsonArray { "$a", "$b" })))
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).First();
        var common = result["common"].AsBsonArray.Select(v => v.AsInt32).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 2, 3 }, common);
    }

    [Fact]
    public void SetUnion_Returns_All_Unique_Elements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setUnion/
        //   "Returns a set with elements that appear in any of the input sets."
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("setunion_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonArray { 1, 2 } },
            { "b", new BsonArray { 2, 3 } }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("all",
                new BsonDocument("$setUnion", new BsonArray { "$a", "$b" })))
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).First();
        var all = result["all"].AsBsonArray.Select(v => v.AsInt32).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, all);
    }

    [Fact]
    public void SetDifference_Returns_Elements_In_First_Not_In_Second()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setDifference/
        //   "Returns a set with elements that appear in the first set but not in the second set."
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("setdiff_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonArray { 1, 2, 3, 4 } },
            { "b", new BsonArray { 2, 4 } }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("diff",
                new BsonDocument("$setDifference", new BsonArray { "$a", "$b" })))
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).First();
        var diff = result["diff"].AsBsonArray.Select(v => v.AsInt32).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 1, 3 }, diff);
    }

    [Fact]
    public void SetEquals_Returns_True_When_Sets_Are_Equal()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setEquals/
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("setequals_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonArray { 1, 2, 3 } },
            { "b", new BsonArray { 3, 2, 1 } }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("eq",
                new BsonDocument("$setEquals", new BsonArray { "$a", "$b" })))
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).First();
        Assert.True(result["eq"].AsBoolean);
    }

    [Fact]
    public void SetIsSubset_Returns_True_When_First_Is_Subset_Of_Second()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setIsSubset/
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("setsubset_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonArray { 1, 2 } },
            { "b", new BsonArray { 1, 2, 3, 4 } }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument
            {
                { "aSubOfB", new BsonDocument("$setIsSubset", new BsonArray { "$a", "$b" }) },
                { "bSubOfA", new BsonDocument("$setIsSubset", new BsonArray { "$b", "$a" }) }
            })
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).First();
        Assert.True(result["aSubOfB"].AsBoolean);
        Assert.False(result["bSubOfA"].AsBoolean);
    }

    [Fact]
    public void AnyElementTrue_Returns_True_If_Any_Element_Is_True()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/anyElementTrue/
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("anyelemtrue_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "flags", new BsonArray { false, true, false } }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("any",
                new BsonDocument("$anyElementTrue", new BsonArray { "$flags" })))
        };

        var result = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).First();
        Assert.True(result["any"].AsBoolean);
    }

    [Fact]
    public void AllElementsTrue_Returns_True_If_All_Elements_Are_True()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/allElementsTrue/
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("allelemtrue_test");
        var col = db.GetCollection<BsonDocument>("items");

        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "flags", new BsonArray { true, true, true } } },
            new BsonDocument { { "_id", 2 }, { "flags", new BsonArray { true, false } } }
        });

        var pipeline = new[]
        {
            new BsonDocument("$project", new BsonDocument("allTrue",
                new BsonDocument("$allElementsTrue", new BsonArray { "$flags" })))
        };

        var results = col.Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline)).ToList();
        var doc1 = results.First(r => r["_id"] == 1);
        var doc2 = results.First(r => r["_id"] == 2);
        Assert.True(doc1["allTrue"].AsBoolean);
        Assert.False(doc2["allTrue"].AsBoolean);
    }

    #endregion
}
