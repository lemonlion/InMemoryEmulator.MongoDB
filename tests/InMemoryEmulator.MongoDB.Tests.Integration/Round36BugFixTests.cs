using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 36: Edge cases in $push sort, $unwind, $project, and filter operators
/// </summary>
public class Round36BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection()
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>("items");
    }

    #region $push with $each empty + $sort (sort existing array)

    [Fact]
    public void Push_EmptyEachWithSort_SortsExistingArray()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/push/
        //   "If the value of $each is an empty array, $push does nothing to the array.
        //    However, the $sort and $slice modifiers still apply."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 50, 10, 30, 20 } }
        });

        var update = new BsonDocument("$push", new BsonDocument("scores",
            new BsonDocument
            {
                { "$each", new BsonArray() },
                { "$sort", 1 }
            }));
        col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        var arr = result["scores"].AsBsonArray;
        Assert.Equal(4, arr.Count);
        Assert.Equal(10, arr[0].AsInt32);
        Assert.Equal(20, arr[1].AsInt32);
        Assert.Equal(30, arr[2].AsInt32);
        Assert.Equal(50, arr[3].AsInt32);
    }

    [Fact]
    public void Push_EmptyEachWithSlice_TruncatesExistingArray()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/push/
        //   "$slice limits the number of array elements."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "tags", new BsonArray { "a", "b", "c", "d", "e" } }
        });

        var update = new BsonDocument("$push", new BsonDocument("tags",
            new BsonDocument
            {
                { "$each", new BsonArray() },
                { "$slice", 3 }
            }));
        col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        var arr = result["tags"].AsBsonArray;
        Assert.Equal(3, arr.Count);
        Assert.Equal("a", arr[0].AsString);
        Assert.Equal("b", arr[1].AsString);
        Assert.Equal("c", arr[2].AsString);
    }

    #endregion

    #region $unwind includeArrayIndex

    [Fact]
    public void Unwind_IncludeArrayIndex_AddsIndexField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/unwind/
        //   "includeArrayIndex: Optional. The name of a new field to hold the array index."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray { "a", "b", "c" } }
        });

        var pipeline = new BsonDocument[]
        {
            new("$unwind", new BsonDocument
            {
                { "path", "$items" },
                { "includeArrayIndex", "idx" }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal(3, results.Count);
        Assert.Equal(0, results[0]["idx"].ToInt64());
        Assert.Equal(1, results[1]["idx"].ToInt64());
        Assert.Equal(2, results[2]["idx"].ToInt64());
        Assert.Equal("a", results[0]["items"].AsString);
        Assert.Equal("b", results[1]["items"].AsString);
        Assert.Equal("c", results[2]["items"].AsString);
    }

    [Fact]
    public void Unwind_PreserveNullAndEmptyArrays_NullField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/unwind/
        //   "preserveNullAndEmptyArrays: If true, if the path is null, missing, or an empty array,
        //    $unwind outputs the document."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "items", BsonNull.Value } });
        col.InsertOne(new BsonDocument { { "_id", 2 } }); // missing field
        col.InsertOne(new BsonDocument { { "_id", 3 }, { "items", new BsonArray() } });

        var pipeline = new BsonDocument[]
        {
            new("$unwind", new BsonDocument
            {
                { "path", "$items" },
                { "preserveNullAndEmptyArrays", true }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal(3, results.Count);
        // null field stays null
        Assert.Equal(BsonNull.Value, results[0]["items"]);
        // missing stays missing
        Assert.False(results[1].Contains("items"));
        // empty array → field removed
        Assert.False(results[2].Contains("items"));
    }

    #endregion

    #region $project with computed expression + exclusion

    [Fact]
    public void Project_ComputedExpression_IncludesResult()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/project/
        //   "Use $project to ... compute new fields."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "price", 10 },
            { "qty", 3 },
            { "name", "Widget" }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "name", 1 },
                { "total", new BsonDocument("$multiply", new BsonArray { "$price", "$qty" }) }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
        Assert.Equal("Widget", results[0]["name"].AsString);
        Assert.Equal(30, results[0]["total"].ToInt32());
        Assert.False(results[0].Contains("price"));
        Assert.False(results[0].Contains("qty"));
    }

    #endregion

    #region $group with $first/$last preserves order

    [Fact]
    public void Group_First_ReturnsFirstInGroup()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/first/
        //   "$first returns the value from the first document for each group."
        //   Order depends on preceding $sort stage.
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "cat", "A" }, { "val", 10 } },
            new BsonDocument { { "_id", 2 }, { "cat", "A" }, { "val", 20 } },
            new BsonDocument { { "_id", 3 }, { "cat", "B" }, { "val", 30 } },
            new BsonDocument { { "_id", 4 }, { "cat", "B" }, { "val", 40 } },
        });

        var pipeline = new BsonDocument[]
        {
            new("$sort", new BsonDocument("val", 1)),
            new("$group", new BsonDocument
            {
                { "_id", "$cat" },
                { "firstVal", new BsonDocument("$first", "$val") },
                { "lastVal", new BsonDocument("$last", "$val") }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal(2, results.Count);

        var groupA = results.First(r => r["_id"] == "A");
        var groupB = results.First(r => r["_id"] == "B");
        Assert.Equal(10, groupA["firstVal"].AsInt32);
        Assert.Equal(20, groupA["lastVal"].AsInt32);
        Assert.Equal(30, groupB["firstVal"].AsInt32);
        Assert.Equal(40, groupB["lastVal"].AsInt32);
    }

    #endregion

    #region $addToSet with nested documents

    [Fact]
    public void AddToSet_NestedDocuments_NoDuplicates()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/addToSet/
        //   "Adds a value to an array unless the value is already present."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "qty", 1 } }
                }
            }
        });

        // Try to add the same document again — should not duplicate
        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.AddToSet("items",
                new BsonDocument { { "name", "a" }, { "qty", 1 } }));

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        var arr = result["items"].AsBsonArray;
        Assert.Single(arr);

        // Add a different document — should add
        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.AddToSet("items",
                new BsonDocument { { "name", "b" }, { "qty", 2 } }));

        result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        arr = result["items"].AsBsonArray;
        Assert.Equal(2, arr.Count);
    }

    #endregion

    #region $jsonSchema oneOf

    [Fact]
    public void JsonSchema_OneOf_MatchesExactlyOne()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/jsonSchema/
        //   "oneOf: Must match exactly ONE of the schemas."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "x", 10 } });        // int
        col.InsertOne(new BsonDocument { { "_id", 2 }, { "x", "hello" } });   // string
        col.InsertOne(new BsonDocument { { "_id", 3 }, { "x", 5.5 } });       // double — matches both "number" schemas if poorly constructed

        // oneOf: exactly one of "int" or "double"
        var schema = new BsonDocument("oneOf", new BsonArray
        {
            new BsonDocument("properties", new BsonDocument("x", new BsonDocument("bsonType", "int"))),
            new BsonDocument("properties", new BsonDocument("x", new BsonDocument("bsonType", "double")))
        });

        var results = col.Find(new BsonDocument("$jsonSchema", schema)).ToList();
        // _id=1 matches int only, _id=3 matches double only — both match exactly one
        // _id=2 matches neither — excluded
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r["_id"].AsInt32 == 1);
        Assert.Contains(results, r => r["_id"].AsInt32 == 3);
    }

    #endregion

    #region $slice with inclusion mode

    [Fact]
    public void Slice_WithInclusion_OnlyReturnsSpecifiedFields()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/projection/slice/
        //   "When combined with other inclusion projections, only specified fields are returned."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "name", "test" },
            { "scores", new BsonArray { 10, 20, 30, 40, 50 } },
            { "extra", "should not appear" }
        });

        // Inclusion mode: _id + name + sliced scores
        var projection = new BsonDocument
        {
            { "name", 1 },
            { "scores", new BsonDocument("$slice", 2) }
        };
        var result = col.Find(Builders<BsonDocument>.Filter.Empty)
            .Project<BsonDocument>(projection)
            .First();

        Assert.Equal(1, result["_id"].AsInt32);
        Assert.Equal("test", result["name"].AsString);
        var arr = result["scores"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal(10, arr[0].AsInt32);
        Assert.Equal(20, arr[1].AsInt32);
        Assert.False(result.Contains("extra"));
    }

    #endregion

    #region $push with negative $position

    [Fact]
    public void Push_NegativePosition_InsertsFromEnd()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/position/
        //   "A negative value indicates position starting from the end of the array."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "arr", new BsonArray { 1, 2, 3, 4, 5 } }
        });

        var update = new BsonDocument("$push", new BsonDocument("arr",
            new BsonDocument
            {
                { "$each", new BsonArray { 99 } },
                { "$position", -2 }
            }));
        col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", 1), update);

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        var arr = result["arr"].AsBsonArray;
        // position -2 means 2 from the end: [1, 2, 3, 99, 4, 5]
        Assert.Equal(6, arr.Count);
        Assert.Equal(new BsonArray { 1, 2, 3, 99, 4, 5 }, arr);
    }

    #endregion

    #region $pull with operators

    [Fact]
    public void Pull_WithInOperator_RemovesMatchingElements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/pull/
        //   "Remove all elements that match a specified query."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 5, 10, 15, 20 } }
        });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument("$pull", new BsonDocument("scores",
                new BsonDocument("$in", new BsonArray { 5, 15 }))));

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        var arr = result["scores"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal(10, arr[0].AsInt32);
        Assert.Equal(20, arr[1].AsInt32);
    }

    [Fact]
    public void Pull_WithGteOperator_RemovesMatchingElements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/pull/
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "scores", new BsonArray { 5, 10, 15, 20 } }
        });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument("$pull", new BsonDocument("scores",
                new BsonDocument("$gte", 15))));

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        var arr = result["scores"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal(5, arr[0].AsInt32);
        Assert.Equal(10, arr[1].AsInt32);
    }

    #endregion

    #region $group with $sum on array field

    [Fact]
    public void Group_SumOnNestedArrayField_SumsEachDoc()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sum/
        //   "When used in $group, $sum returns the collective sum for each group."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "cat", "A" }, { "amount", 100 } },
            new BsonDocument { { "_id", 2 }, { "cat", "A" }, { "amount", 200 } },
            new BsonDocument { { "_id", 3 }, { "cat", "B" }, { "amount", 50 } },
        });

        var pipeline = new BsonDocument[]
        {
            new("$group", new BsonDocument
            {
                { "_id", "$cat" },
                { "total", new BsonDocument("$sum", "$amount") }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var groupA = results.First(r => r["_id"] == "A");
        var groupB = results.First(r => r["_id"] == "B");
        Assert.Equal(300, groupA["total"].ToInt32());
        Assert.Equal(50, groupB["total"].ToInt32());
    }

    #endregion

    #region $lookup with pipeline

    [Fact]
    public void Lookup_WithPipeline_FiltersCorrectly()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/lookup/
        //   "Performs a left outer join using a pipeline ($lookup with let/pipeline form)."
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        var orders = db.GetCollection<BsonDocument>("orders");
        var products = db.GetCollection<BsonDocument>("products");

        products.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "name", "Widget" }, { "price", 10 } },
            new BsonDocument { { "_id", 2 }, { "name", "Gadget" }, { "price", 25 } },
            new BsonDocument { { "_id", 3 }, { "name", "Gizmo" }, { "price", 50 } },
        });

        orders.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "maxPrice", 30 }
        });

        var pipeline = new BsonDocument[]
        {
            new("$lookup", new BsonDocument
            {
                { "from", "products" },
                { "let", new BsonDocument("max", "$maxPrice") },
                { "pipeline", new BsonArray
                    {
                        new BsonDocument("$match", new BsonDocument("$expr",
                            new BsonDocument("$lte", new BsonArray { "$price", "$$max" })))
                    }
                },
                { "as", "affordableProducts" }
            })
        };

        var results = orders.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        var prods = results[0]["affordableProducts"].AsBsonArray;
        Assert.Equal(2, prods.Count); // Widget (10) and Gadget (25) are <= 30
    }

    #endregion

    #region Update with array filters $[identifier]

    [Fact]
    public void Update_ArrayFilter_UpdatesMatchingElements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/positional-filtered/
        //   "The filtered positional operator $[<identifier>] identifies the array elements
        //    that match the arrayFilters conditions."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "grades", new BsonArray { 85, 92, 78, 95, 60 } }
        });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument("$set", new BsonDocument("grades.$[elem]", 100)),
            new UpdateOptions
            {
                ArrayFilters = new List<ArrayFilterDefinition>
                {
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(
                        new BsonDocument("elem", new BsonDocument("$gte", 90)))
                }
            });

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        var arr = result["grades"].AsBsonArray;
        // 92 and 95 should be replaced with 100
        Assert.Equal(new BsonArray { 85, 100, 78, 100, 60 }, arr);
    }

    #endregion

    #region $group with $$ROOT

    [Fact]
    public void Group_PushRoot_CollectsEntireDocuments()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/aggregation-variables/
        //   "$$ROOT references the root document."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "cat", "A" }, { "val", 10 } },
            new BsonDocument { { "_id", 2 }, { "cat", "A" }, { "val", 20 } },
            new BsonDocument { { "_id", 3 }, { "cat", "B" }, { "val", 30 } },
        });

        var pipeline = new BsonDocument[]
        {
            new("$group", new BsonDocument
            {
                { "_id", "$cat" },
                { "docs", new BsonDocument("$push", "$$ROOT") }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var groupA = results.First(r => r["_id"] == "A");
        var docs = groupA["docs"].AsBsonArray;
        Assert.Equal(2, docs.Count);
        Assert.Contains(docs, d => d["val"].AsInt32 == 10);
        Assert.Contains(docs, d => d["val"].AsInt32 == 20);
    }

    #endregion

    #region $set deep dot-notation creating intermediate docs

    [Fact]
    public void Set_DeepDotNotation_CreatesIntermediateDocs()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/set/
        //   "If the field does not exist, $set will add a new field with the specified value,
        //    provided that the new field does not violate a type constraint."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 } });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Set("a.b.c", 42));

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        Assert.Equal(42, result["a"]["b"]["c"].AsInt32);
    }

    #endregion

    #region $count aggregation stage

    [Fact]
    public void Aggregate_CountStage_ReturnsDocumentCount()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/count/
        //   "Passes a document to the next stage that contains a count of the number of documents."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "status", "active" } },
            new BsonDocument { { "_id", 2 }, { "status", "active" } },
            new BsonDocument { { "_id", 3 }, { "status", "inactive" } },
        });

        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument("status", "active")),
            new("$count", "activeCount")
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        Assert.Equal(2, results[0]["activeCount"].AsInt32);
    }

    #endregion

    #region $project exclusion with _id expression

    [Fact]
    public void Project_ExcludeId_NoIdInResult()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/project/
        //   "You can exclude the _id field."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "name", "Alice" }, { "age", 30 } });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument { { "_id", 0 }, { "name", 1 } })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        Assert.False(results[0].Contains("_id"));
        Assert.Equal("Alice", results[0]["name"].AsString);
        Assert.False(results[0].Contains("age"));
    }

    #endregion

    #region $project exclusion mode (remove specific fields)

    [Fact]
    public void Project_ExclusionMode_RemovesFields()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/project/
        //   "Specify 0 to suppress a field."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 }, { "name", "Alice" }, { "age", 30 }, { "email", "a@b.com" }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument { { "age", 0 }, { "email", 0 } })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
        Assert.Equal("Alice", results[0]["name"].AsString);
        Assert.False(results[0].Contains("age"));
        Assert.False(results[0].Contains("email"));
    }

    #endregion

    #region $cond expression

    [Fact]
    public void Project_CondExpression_Computes()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/cond/
        //   "Evaluates a boolean expression and returns one of two specified expressions."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "score", 90 } },
            new BsonDocument { { "_id", 2 }, { "score", 50 } },
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "grade", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$gte", new BsonArray { "$score", 70 }),
                        "pass",
                        "fail"
                    })
                }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal("pass", results.First(r => r["_id"] == 1)["grade"].AsString);
        Assert.Equal("fail", results.First(r => r["_id"] == 2)["grade"].AsString);
    }

    #endregion

    #region Update $pop operator

    [Fact]
    public void Pop_First_RemovesFirstElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/pop/
        //   "Removes the first or last element of an array."
        //   "$pop: -1 removes the first element, $pop: 1 removes the last."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "arr", new BsonArray { 10, 20, 30 } }
        });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument("$pop", new BsonDocument("arr", -1)));

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        Assert.Equal(new BsonArray { 20, 30 }, result["arr"].AsBsonArray);
    }

    [Fact]
    public void Pop_Last_RemovesLastElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/update/pop/
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "arr", new BsonArray { 10, 20, 30 } }
        });

        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument("$pop", new BsonDocument("arr", 1)));

        var result = col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).First();
        Assert.Equal(new BsonArray { 10, 20 }, result["arr"].AsBsonArray);
    }

    #endregion

    #region $expr in find filter

    [Fact]
    public void Find_ExprFilter_ComparesFields()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/expr/
        //   "Allows the use of aggregation expressions within the query language."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "budget", 100 }, { "spent", 80 } },
            new BsonDocument { { "_id", 2 }, { "budget", 100 }, { "spent", 120 } },
            new BsonDocument { { "_id", 3 }, { "budget", 50 }, { "spent", 50 } },
        });

        // Find docs where spent > budget
        var filter = new BsonDocument("$expr",
            new BsonDocument("$gt", new BsonArray { "$spent", "$budget" }));

        var results = col.Find(filter).ToList();
        Assert.Single(results);
        Assert.Equal(2, results[0]["_id"].AsInt32);
    }

    #endregion

    #region $addFields with nested path

    [Fact]
    public void AddFields_NestedPath_CreatesStructure()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/addFields/
        //   "Adds new fields to documents. Can use dot notation to add to embedded documents."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "name", "test" } });

        var pipeline = new BsonDocument[]
        {
            new("$addFields", new BsonDocument("meta.created", "$$NOW"))
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        Assert.True(results[0].Contains("meta"));
        Assert.True(results[0]["meta"].AsBsonDocument.Contains("created"));
    }

    #endregion

    #region $concat with null returns null

    [Fact]
    public void Project_ConcatWithNull_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/concat/
        //   "If any of the arguments resolve to a value of null or refer to a field that is missing,
        //    $concat returns null."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "first", "John" } }); // "last" is missing

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "fullName", new BsonDocument("$concat", new BsonArray { "$first", " ", "$last" }) }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        Assert.Equal(BsonNull.Value, results[0]["fullName"]);
    }

    #endregion

    #region $ifNull with missing field

    [Fact]
    public void Project_IfNull_ReturnsReplacement()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/ifNull/
        //   "Evaluates input expressions for null values and returns the first one that is not null."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "desc", BsonNull.Value } });
        col.InsertOne(new BsonDocument { { "_id", 2 }, { "desc", "exists" } });
        col.InsertOne(new BsonDocument { { "_id", 3 } }); // field missing

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "description", new BsonDocument("$ifNull", new BsonArray { "$desc", "N/A" }) }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal("N/A", results.First(r => r["_id"] == 1)["description"].AsString);
        Assert.Equal("exists", results.First(r => r["_id"] == 2)["description"].AsString);
        Assert.Equal("N/A", results.First(r => r["_id"] == 3)["description"].AsString);
    }

    #endregion

    #region Filter $nor operator

    [Fact]
    public void Find_Nor_ExcludesMatchingDocs()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/nor/
        //   "$nor performs a logical NOR operation on an array of one or more query expressions
        //    and selects the documents that fail all the query expressions."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "x", 1 }, { "y", 1 } },
            new BsonDocument { { "_id", 2 }, { "x", 1 }, { "y", 2 } },
            new BsonDocument { { "_id", 3 }, { "x", 2 }, { "y", 1 } },
            new BsonDocument { { "_id", 4 }, { "x", 2 }, { "y", 2 } },
        });

        // Neither x==1 nor y==1
        var filter = new BsonDocument("$nor", new BsonArray
        {
            new BsonDocument("x", 1),
            new BsonDocument("y", 1)
        });

        var results = col.Find(filter).ToList();
        Assert.Single(results);
        Assert.Equal(4, results[0]["_id"].AsInt32);
    }

    #endregion

    #region $arrayElemAt out of bounds

    [Fact]
    public void ArrayElemAt_OutOfBounds_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/arrayElemAt/
        //   "If the idx exceeds the array bounds, $arrayElemAt does not return any result."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "arr", new BsonArray { 10, 20, 30 } }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "elem", new BsonDocument("$arrayElemAt", new BsonArray { "$arr", 5 }) }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        // Out of bounds returns missing/null
        Assert.True(!results[0].Contains("elem") || results[0]["elem"] == BsonNull.Value);
    }

    #endregion

    #region $group $avg with all null

    [Fact]
    public void Group_AvgAllNull_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/avg/
        //   "$avg ignores non-numeric values."
        //   When all values are null/non-numeric, result should be null.
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "cat", "A" }, { "val", BsonNull.Value } },
            new BsonDocument { { "_id", 2 }, { "cat", "A" } }, // field missing
        });

        var pipeline = new BsonDocument[]
        {
            new("$group", new BsonDocument
            {
                { "_id", "$cat" },
                { "average", new BsonDocument("$avg", "$val") }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        Assert.Equal(BsonNull.Value, results[0]["average"]);
    }

    #endregion

    #region $match with $not

    [Fact]
    public void Find_NotWithRegex_ExcludesMatches()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/not/
        //   "$not performs a logical NOT operation on the specified operator-expression."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "name", "apple" } },
            new BsonDocument { { "_id", 2 }, { "name", "banana" } },
            new BsonDocument { { "_id", 3 }, { "name", "avocado" } },
        });

        // Find names that don't start with "a"
        var filter = new BsonDocument("name",
            new BsonDocument("$not", new BsonRegularExpression("^a")));

        var results = col.Find(filter).ToList();
        Assert.Single(results);
        Assert.Equal("banana", results[0]["name"].AsString);
    }

    #endregion

    #region $filter array operator

    [Fact]
    public void Project_Filter_ReturnsMatchingElements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/filter/
        //   "Selects a subset of an array to return based on the specified condition."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "a" }, { "price", 5 } },
                    new BsonDocument { { "name", "b" }, { "price", 15 } },
                    new BsonDocument { { "name", "c" }, { "price", 25 } }
                }
            }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "expensive", new BsonDocument("$filter", new BsonDocument
                    {
                        { "input", "$items" },
                        { "as", "item" },
                        { "cond", new BsonDocument("$gt", new BsonArray { "$$item.price", 10 }) }
                    })
                }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        var arr = results[0]["expensive"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal("b", arr[0]["name"].AsString);
        Assert.Equal("c", arr[1]["name"].AsString);
    }

    #endregion

    #region $map array operator

    [Fact]
    public void Project_Map_TransformsElements()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/map/
        //   "Applies an expression to each element in an array and returns an array with the results."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "prices", new BsonArray { 10, 20, 30 } }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "doubled", new BsonDocument("$map", new BsonDocument
                    {
                        { "input", "$prices" },
                        { "as", "p" },
                        { "in", new BsonDocument("$multiply", new BsonArray { "$$p", 2 }) }
                    })
                }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        var arr = results[0]["doubled"].AsBsonArray;
        Assert.Equal(3, arr.Count);
        Assert.Equal(20, arr[0].ToInt32());
        Assert.Equal(40, arr[1].ToInt32());
        Assert.Equal(60, arr[2].ToInt32());
    }

    #endregion

    #region $reduce array operator

    [Fact]
    public void Project_Reduce_AccumulatesValue()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/reduce/
        //   "Applies an expression to each element in an array and combines them into a single value."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "nums", new BsonArray { 1, 2, 3, 4, 5 } }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "total", new BsonDocument("$reduce", new BsonDocument
                    {
                        { "input", "$nums" },
                        { "initialValue", 0 },
                        { "in", new BsonDocument("$add", new BsonArray { "$$value", "$$this" }) }
                    })
                }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Single(results);
        Assert.Equal(15, results[0]["total"].ToInt32());
    }

    #endregion

    #region Distinct with filter

    [Fact]
    public void Distinct_WithFilter_ReturnsFilteredDistinct()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.distinct/
        //   "Finds the distinct values for a specified field across a single collection."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "cat", "A" }, { "active", true } },
            new BsonDocument { { "_id", 2 }, { "cat", "B" }, { "active", true } },
            new BsonDocument { { "_id", 3 }, { "cat", "A" }, { "active", false } },
            new BsonDocument { { "_id", 4 }, { "cat", "C" }, { "active", true } },
        });

        var results = col.Distinct<string>("cat",
            Builders<BsonDocument>.Filter.Eq("active", true)).ToList();
        Assert.Equal(3, results.Count);
        Assert.Contains("A", results);
        Assert.Contains("B", results);
        Assert.Contains("C", results);
    }

    #endregion

    #region $sortByCount stage

    [Fact]
    public void Aggregate_SortByCount_GroupsAndSorts()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/sortByCount/
        //   "Groups incoming documents based on the value of a specified expression,
        //    then computes the count of documents in each distinct group."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "tag", "mongo" } },
            new BsonDocument { { "_id", 2 }, { "tag", "redis" } },
            new BsonDocument { { "_id", 3 }, { "tag", "mongo" } },
            new BsonDocument { { "_id", 4 }, { "tag", "mongo" } },
            new BsonDocument { { "_id", 5 }, { "tag", "redis" } },
        });

        var pipeline = new BsonDocument[]
        {
            new("$sortByCount", "$tag")
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal(2, results.Count);
        // Sorted by count descending
        Assert.Equal("mongo", results[0]["_id"].AsString);
        Assert.Equal(3, results[0]["count"].AsInt32);
        Assert.Equal("redis", results[1]["_id"].AsString);
        Assert.Equal(2, results[1]["count"].AsInt32);
    }

    #endregion

    #region $split string operator

    [Fact]
    public void Project_Split_SplitsString()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/split/
        //   "Splits a string by a specified delimiter into an array of substrings."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "csv", "a,b,c,d" } });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "parts", new BsonDocument("$split", new BsonArray { "$csv", "," }) }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var arr = results[0]["parts"].AsBsonArray;
        Assert.Equal(4, arr.Count);
        Assert.Equal("a", arr[0].AsString);
        Assert.Equal("d", arr[3].AsString);
    }

    #endregion

    #region $regexMatch

    [Fact]
    public void Project_RegexMatch_ReturnsBool()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/regexMatch/
        //   "Performs a regular expression (regex) pattern matching and returns true or false."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "email", "test@example.com" } },
            new BsonDocument { { "_id", 2 }, { "email", "invalid" } },
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "valid", new BsonDocument("$regexMatch", new BsonDocument
                    {
                        { "input", "$email" },
                        { "regex", "@.*\\." }
                    })
                }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.True(results.First(r => r["_id"] == 1)["valid"].AsBoolean);
        Assert.False(results.First(r => r["_id"] == 2)["valid"].AsBoolean);
    }

    #endregion

    #region $round

    [Fact]
    public void Project_Round_RoundsToPlace()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/round/
        //   "Rounds a number to a specified decimal place."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "val", 3.456 } });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "rounded", new BsonDocument("$round", new BsonArray { "$val", 1 }) }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal(3.5, results[0]["rounded"].ToDouble(), 5);
    }

    #endregion

    #region $dateToString

    [Fact]
    public void Project_DateToString_FormatsDate()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateToString/
        //   "Converts a date object to a string according to a user-specified format."
        var col = CreateCollection();
        var date = new DateTime(2023, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "date", new BsonDateTime(date) } });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "formatted", new BsonDocument("$dateToString", new BsonDocument
                    {
                        { "format", "%Y-%m-%d" },
                        { "date", "$date" }
                    })
                }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal("2023-06-15", results[0]["formatted"].AsString);
    }

    #endregion

    #region $toInt / $toString type conversion

    [Fact]
    public void Project_ToInt_ConvertsString()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/toInt/
        //   "Converts a value to an integer."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "numStr", "42" } });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "num", new BsonDocument("$toInt", "$numStr") }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal(42, results[0]["num"].AsInt32);
    }

    [Fact]
    public void Project_ToString_ConvertsInt()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/toString/
        //   "Converts a value to a string."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "num", 42 } });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "str", new BsonDocument("$toString", "$num") }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal("42", results[0]["str"].AsString);
    }

    #endregion

    #region Set operators (setIntersection, setUnion, setDifference)

    [Fact]
    public void Project_SetIntersection_ReturnsCommon()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setIntersection/
        //   "Returns a set with elements that appear in all of the input sets."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonArray { 1, 2, 3 } },
            { "b", new BsonArray { 2, 3, 4 } }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "common", new BsonDocument("$setIntersection", new BsonArray { "$a", "$b" }) }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var arr = results[0]["common"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Contains(new BsonInt32(2), arr);
        Assert.Contains(new BsonInt32(3), arr);
    }

    [Fact]
    public void Project_SetUnion_ReturnsCombined()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setUnion/
        //   "Returns a set with elements that appear in any of the input sets."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonArray { 1, 2 } },
            { "b", new BsonArray { 2, 3 } }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "all", new BsonDocument("$setUnion", new BsonArray { "$a", "$b" }) }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var arr = results[0]["all"].AsBsonArray;
        Assert.Equal(3, arr.Count);
        Assert.Contains(new BsonInt32(1), arr);
        Assert.Contains(new BsonInt32(2), arr);
        Assert.Contains(new BsonInt32(3), arr);
    }

    [Fact]
    public void Project_SetDifference_ReturnsOnlyInFirst()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/setDifference/
        //   "Returns a set with elements that appear in the first set but not in the second."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "a", new BsonArray { 1, 2, 3, 4 } },
            { "b", new BsonArray { 2, 4 } }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "diff", new BsonDocument("$setDifference", new BsonArray { "$a", "$b" }) }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var arr = results[0]["diff"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Contains(new BsonInt32(1), arr);
        Assert.Contains(new BsonInt32(3), arr);
    }

    #endregion

    #region $objectToArray / $arrayToObject

    [Fact]
    public void Project_ObjectToArray_ConvertsKVPairs()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/objectToArray/
        //   "Converts a document to an array of key-value pairs."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "data", new BsonDocument { { "x", 10 }, { "y", 20 } } }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "kvPairs", new BsonDocument("$objectToArray", "$data") }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var arr = results[0]["kvPairs"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal("x", arr[0]["k"].AsString);
        Assert.Equal(10, arr[0]["v"].AsInt32);
        Assert.Equal("y", arr[1]["k"].AsString);
        Assert.Equal(20, arr[1]["v"].AsInt32);
    }

    [Fact]
    public void Project_ArrayToObject_ConvertsToDoc()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/arrayToObject/
        //   "Converts an array of key-value pairs to a document."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "pairs", new BsonArray
                {
                    new BsonDocument { { "k", "name" }, { "v", "test" } },
                    new BsonDocument { { "k", "age" }, { "v", 25 } }
                }
            }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "obj", new BsonDocument("$arrayToObject", "$pairs") }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var obj = results[0]["obj"].AsBsonDocument;
        Assert.Equal("test", obj["name"].AsString);
        Assert.Equal(25, obj["age"].AsInt32);
    }

    #endregion

    #region $replaceAll string operator

    [Fact]
    public void Project_ReplaceAll_ReplacesOccurrences()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/replaceAll/
        //   "Replaces all instances of a search string in an input string with a replacement string."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "text", "foo-bar-foo-baz" } });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument
            {
                { "result", new BsonDocument("$replaceAll", new BsonDocument
                    {
                        { "input", "$text" },
                        { "find", "foo" },
                        { "replacement", "qux" }
                    })
                }
            })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        Assert.Equal("qux-bar-qux-baz", results[0]["result"].AsString);
    }

    #endregion

    #region Sort with mixed types / null values

    [Fact]
    public void Sort_NullValues_SortBeforeValues()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/bson-type-comparison-order/
        //   "MinKey (internal type) < Null < Numbers < String < Object < Array < ... < MaxKey"
        //   Missing fields sort equivalently to null.
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", 10 } },
            new BsonDocument { { "_id", 2 }, { "val", BsonNull.Value } },
            new BsonDocument { { "_id", 3 } }, // missing val
            new BsonDocument { { "_id", 4 }, { "val", 5 } },
        });

        var results = col.Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("val"))
            .ToList();

        // Null/missing sort before numbers
        Assert.True(results[0]["_id"].AsInt32 == 2 || results[0]["_id"].AsInt32 == 3);
        Assert.True(results[1]["_id"].AsInt32 == 2 || results[1]["_id"].AsInt32 == 3);
        // Then numbers in order
        Assert.Equal(4, results[2]["_id"].AsInt32); // val=5
        Assert.Equal(1, results[3]["_id"].AsInt32); // val=10
    }

    [Fact]
    public void Sort_MixedNumericTypes_SortsCorrectly()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/bson-type-comparison-order/
        //   "Numbers (ints, longs, doubles, decimals) are compared by their numeric value."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "val", new BsonInt32(10) } },
            new BsonDocument { { "_id", 2 }, { "val", new BsonDouble(2.5) } },
            new BsonDocument { { "_id", 3 }, { "val", new BsonInt64(7) } },
            new BsonDocument { { "_id", 4 }, { "val", new BsonDouble(10.1) } },
        });

        var results = col.Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("val"))
            .ToList();

        Assert.Equal(2, results[0]["_id"].AsInt32); // 2.5
        Assert.Equal(3, results[1]["_id"].AsInt32); // 7
        Assert.Equal(1, results[2]["_id"].AsInt32); // 10
        Assert.Equal(4, results[3]["_id"].AsInt32); // 10.1
    }

    #endregion

    #region $in filter with regex

    [Fact]
    public void Find_InWithRegex_MatchesPattern()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/in/
        //   "You can also specify regular expression objects in the $in array."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "name", "apple" } },
            new BsonDocument { { "_id", 2 }, { "name", "banana" } },
            new BsonDocument { { "_id", 3 }, { "name", "avocado" } },
            new BsonDocument { { "_id", 4 }, { "name", "cherry" } },
        });

        // Match names that start with 'a' or equal 'cherry'
        var filter = new BsonDocument("name", new BsonDocument("$in", new BsonArray
        {
            new BsonRegularExpression("^a"),
            "cherry"
        }));

        var results = col.Find(filter).ToList();
        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r["_id"] == 1);
        Assert.Contains(results, r => r["_id"] == 3);
        Assert.Contains(results, r => r["_id"] == 4);
    }

    #endregion

    #region $exists with dot notation

    [Fact]
    public void Find_ExistsWithDotNotation_ChecksNested()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/query/exists/
        //   "When <boolean> is true, $exists matches the documents that contain the field."
        var col = CreateCollection();
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "meta", new BsonDocument("tag", "A") } },
            new BsonDocument { { "_id", 2 }, { "meta", new BsonDocument("other", "B") } },
            new BsonDocument { { "_id", 3 } }, // no meta at all
        });

        var results = col.Find(new BsonDocument("meta.tag", new BsonDocument("$exists", true))).ToList();
        Assert.Single(results);
        Assert.Equal(1, results[0]["_id"].AsInt32);
    }

    #endregion

    #region FindOneAndUpdate with upsert

    [Fact]
    public void FindOneAndUpdate_Upsert_CreatesDoc()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/method/db.collection.findOneAndUpdate/
        //   "When upsert is true and no document matches, creates a new document."
        var col = CreateCollection();

        var result = col.FindOneAndUpdate(
            Builders<BsonDocument>.Filter.Eq("name", "new"),
            Builders<BsonDocument>.Update.Set("status", "active").Set("name", "new"),
            new FindOneAndUpdateOptions<BsonDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            });

        Assert.NotNull(result);
        Assert.Equal("new", result["name"].AsString);
        Assert.Equal("active", result["status"].AsString);

        // Verify it's in the collection
        Assert.Equal(1, col.CountDocuments(Builders<BsonDocument>.Filter.Empty));
    }

    #endregion

    #region Nested field projection through array

    [Fact]
    public void Find_NestedFieldProjectionThroughArray_ProjectsEachElement()
    {
        // Ref: https://www.mongodb.com/docs/manual/tutorial/project-fields-from-query-results/
        //   "For fields in an embedded document, you can specify the field using dot notation.
        //    If the path traverses an array, each element's projection is applied."
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "items", new BsonArray
                {
                    new BsonDocument { { "name", "A" }, { "qty", 10 }, { "price", 5 } },
                    new BsonDocument { { "name", "B" }, { "qty", 20 }, { "price", 8 } }
                }
            }
        });

        // Project only items.name — each array element should only have "name"
        var projection = new BsonDocument { { "items.name", 1 } };
        var result = col.Find(Builders<BsonDocument>.Filter.Empty)
            .Project<BsonDocument>(projection)
            .First();

        Assert.Equal(1, result["_id"].AsInt32);
        var arr = result["items"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal("A", arr[0]["name"].AsString);
        Assert.Equal("B", arr[1]["name"].AsString);
        // qty and price should be excluded from each element
        Assert.False(arr[0].AsBsonDocument.Contains("qty"));
        Assert.False(arr[0].AsBsonDocument.Contains("price"));
    }

    [Fact]
    public void Aggregate_ProjectNestedFieldThroughArray_ProjectsEachElement()
    {
        // Same as above but via aggregation $project
        var col = CreateCollection();
        col.InsertOne(new BsonDocument
        {
            { "_id", 1 },
            { "orders", new BsonArray
                {
                    new BsonDocument { { "id", 100 }, { "status", "shipped" }, { "total", 50 } },
                    new BsonDocument { { "id", 101 }, { "status", "pending" }, { "total", 30 } }
                }
            }
        });

        var pipeline = new BsonDocument[]
        {
            new("$project", new BsonDocument { { "orders.status", 1 } })
        };

        var results = col.Aggregate<BsonDocument>(pipeline).ToList();
        var arr = results[0]["orders"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal("shipped", arr[0]["status"].AsString);
        Assert.Equal("pending", arr[1]["status"].AsString);
        Assert.False(arr[0].AsBsonDocument.Contains("total"));
    }

    #endregion
}
