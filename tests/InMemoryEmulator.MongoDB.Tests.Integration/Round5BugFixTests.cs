using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 5 bug fix tests covering:
/// 1. $$REMOVE in $addFields/$project excludes fields (not null)
/// 2. InsertMany publishes change stream events
/// 3. $convert onNull parameter
/// 4. $ltrim/$rtrim with chars trims correct side
/// 5. $regexMatch/$regexFind/$regexFindAll handle BsonRegularExpression
/// 6. $dateToString %j produces day-of-year
/// 7. $convert with numeric to parameter (BSON type codes)
/// </summary>
[Collection("Integration")]
public class Round5BugFixTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public Round5BugFixTests(MongoDbSession session) => _session = session;

    public async ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PipelineDefinition<ChangeStreamDocument<BsonDocument>, BsonDocument> RawPipeline()
    {
        return new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .As<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>, BsonDocument>();
    }

    #region Bug 1: $$REMOVE in $addFields/$project

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task AddFields_RemoveVariable_ExcludesField()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/aggregation-variables/
        //   "$$REMOVE evaluates to the missing value. Allows for the exclusion of fields
        //    in $addFields and $project stages."
        var col = _fixture.GetCollection<BsonDocument>("remove_var");
        await col.InsertOneAsync(new BsonDocument { { "a", 1 }, { "b", 2 }, { "c", 3 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$addFields", new BsonDocument
            {
                { "b", "$$REMOVE" }
            }))
            .FirstAsync();

        // $$REMOVE should exclude the field entirely, not set it to null
        Assert.True(result.Contains("a"));
        Assert.False(result.Contains("b"), "$$REMOVE should exclude field 'b' from document");
        Assert.True(result.Contains("c"));
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Project_RemoveVariable_ConditionalExclusion()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/aggregation-variables/
        //   "$$REMOVE ... Allows for the exclusion of fields in $addFields and $project stages."
        var col = _fixture.GetCollection<BsonDocument>("remove_var_project");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "name", "Alice" }, { "score", 85 } },
            new BsonDocument { { "name", "Bob" }, { "score", 45 } },
        });

        // Conditionally exclude 'score' if below 50
        var results = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "_id", 0 },
                { "name", 1 },
                { "score", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$gte", new BsonArray { "$score", 50 }),
                        "$score",
                        "$$REMOVE"
                    })
                }
            }))
            .ToListAsync();

        var alice = results.First(r => r["name"] == "Alice");
        var bob = results.First(r => r["name"] == "Bob");

        Assert.True(alice.Contains("score"), "Alice score >= 50, should be included");
        Assert.Equal(85, alice["score"].AsInt32);
        Assert.False(bob.Contains("score"), "Bob score < 50, should be excluded by $$REMOVE");
    }

    #endregion

    #region Bug 2: InsertMany change stream events

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task InsertMany_PublishesChangeStreamEvents()
    {
        // Ref: https://www.mongodb.com/docs/manual/changeStreams/
        //   "Change streams allow applications to access real-time data changes."
        var col = _fixture.GetCollection<BsonDocument>("insertmany_cs");

        using var cursor = await col.WatchAsync(RawPipeline(),
            new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup });

        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "x", 1 } },
            new BsonDocument { { "x", 2 } },
            new BsonDocument { { "x", 3 } },
        });

        var events = await ChangeStreamHelper.WaitForEventsAsync(cursor, 3);
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal("insert", e["operationType"].AsString));
    }

    #endregion

    #region Bug 3: $convert onNull

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Convert_OnNull_ReturnsSpecifiedDefault()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/convert/
        //   "onNull: The value to return if the input is null or missing."
        var col = _fixture.GetCollection<BsonDocument>("convert_onnull");
        await col.InsertOneAsync(new BsonDocument { { "val", BsonNull.Value } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "converted", new BsonDocument("$convert", new BsonDocument
                    {
                        { "input", "$val" },
                        { "to", "int" },
                        { "onNull", -1 }
                    })
                }
            }))
            .FirstAsync();

        Assert.Equal(-1, result["converted"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Convert_OnNull_MissingField_ReturnsDefault()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/convert/
        //   "onNull: The value to return if the input is null or missing."
        var col = _fixture.GetCollection<BsonDocument>("convert_onnull_missing");
        await col.InsertOneAsync(new BsonDocument { { "other", 1 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "converted", new BsonDocument("$convert", new BsonDocument
                    {
                        { "input", "$nonexistent" },
                        { "to", "string" },
                        { "onNull", "N/A" }
                    })
                }
            }))
            .FirstAsync();

        Assert.Equal("N/A", result["converted"].AsString);
    }

    #endregion

    #region Bug 4: $ltrim/$rtrim with chars

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Ltrim_WithChars_OnlyTrimsLeading()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/ltrim/
        //   "Removes characters from the beginning of a string."
        var col = _fixture.GetCollection<BsonDocument>("ltrim_chars");
        await col.InsertOneAsync(new BsonDocument { { "val", "xxHelloxx" } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "trimmed", new BsonDocument("$ltrim", new BsonDocument
                    {
                        { "input", "$val" },
                        { "chars", "x" }
                    })
                }
            }))
            .FirstAsync();

        // $ltrim should only trim leading 'x', not trailing
        Assert.Equal("Helloxx", result["trimmed"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Rtrim_WithChars_OnlyTrimsTrailing()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/rtrim/
        //   "Removes characters from the end of a string."
        var col = _fixture.GetCollection<BsonDocument>("rtrim_chars");
        await col.InsertOneAsync(new BsonDocument { { "val", "xxHelloxx" } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "trimmed", new BsonDocument("$rtrim", new BsonDocument
                    {
                        { "input", "$val" },
                        { "chars", "x" }
                    })
                }
            }))
            .FirstAsync();

        // $rtrim should only trim trailing 'x', not leading
        Assert.Equal("xxHello", result["trimmed"].AsString);
    }

    #endregion

    #region Bug 5: $regexMatch with BsonRegularExpression

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task RegexMatch_WithBsonRegex_Works()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/regexMatch/
        //   "regex: The regular expression. Can be specified as a string or regex object."
        var col = _fixture.GetCollection<BsonDocument>("regex_bson");
        await col.InsertManyAsync(new[]
        {
            new BsonDocument { { "text", "Hello World" } },
            new BsonDocument { { "text", "goodbye" } },
        });

        var results = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "matched", new BsonDocument("$regexMatch", new BsonDocument
                    {
                        { "input", "$text" },
                        { "regex", new BsonRegularExpression("hello", "i") }
                    })
                }
            }))
            .ToListAsync();

        var hello = results.First(r => r["matched"].AsBoolean);
        var goodbye = results.First(r => !r["matched"].AsBoolean);
        Assert.NotNull(hello);
        Assert.NotNull(goodbye);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task RegexFind_WithBsonRegex_Returns()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/regexFind/
        //   "regex: The regular expression."
        var col = _fixture.GetCollection<BsonDocument>("regex_find_bson");
        await col.InsertOneAsync(new BsonDocument { { "text", "abc123def456" } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "found", new BsonDocument("$regexFind", new BsonDocument
                    {
                        { "input", "$text" },
                        { "regex", new BsonRegularExpression(@"\d+") }
                    })
                }
            }))
            .FirstAsync();

        Assert.Equal("123", result["found"]["match"].AsString);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task RegexFindAll_WithBsonRegex_ReturnsAll()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/regexFindAll/
        //   "regex: The regular expression."
        var col = _fixture.GetCollection<BsonDocument>("regex_findall_bson");
        await col.InsertOneAsync(new BsonDocument { { "text", "abc123def456" } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "found", new BsonDocument("$regexFindAll", new BsonDocument
                    {
                        { "input", "$text" },
                        { "regex", new BsonRegularExpression(@"\d+") }
                    })
                }
            }))
            .FirstAsync();

        var arr = result["found"].AsBsonArray;
        Assert.Equal(2, arr.Count);
        Assert.Equal("123", arr[0]["match"].AsString);
        Assert.Equal("456", arr[1]["match"].AsString);
    }

    #endregion

    #region Bug 6: $dateToString %j format

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task DateToString_DayOfYear_Format()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/dateToString/
        //   "%j: Day of year (3 digits, zero padded). Valid values: 001-366."
        var col = _fixture.GetCollection<BsonDocument>("datefmt_doy");
        // Feb 1 = day 32 of the year
        await col.InsertOneAsync(new BsonDocument { { "dt", new BsonDateTime(new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc)) } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "doy", new BsonDocument("$dateToString", new BsonDocument
                    {
                        { "format", "%j" },
                        { "date", "$dt" }
                    })
                }
            }))
            .FirstAsync();

        Assert.Equal("032", result["doy"].AsString);
    }

    #endregion

    #region Bug 7: $convert with numeric to

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Convert_NumericTo_BsonTypeCode()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/convert/
        //   "to: Can be ... a numeric BSON type identifier."
        //   1=double, 2=string, 8=bool, 16=int, 18=long, 19=decimal
        var col = _fixture.GetCollection<BsonDocument>("convert_numto");
        await col.InsertOneAsync(new BsonDocument { { "val", "42" } });

        // Convert to int using numeric type code 16
        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "asInt", new BsonDocument("$convert", new BsonDocument
                    {
                        { "input", "$val" },
                        { "to", 16 }  // 16 = int32
                    })
                }
            }))
            .FirstAsync();

        Assert.Equal(BsonType.Int32, result["asInt"].BsonType);
        Assert.Equal(42, result["asInt"].AsInt32);
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task Convert_NumericTo_Double()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/convert/
        //   "to: ... 1=double"
        var col = _fixture.GetCollection<BsonDocument>("convert_numto_double");
        await col.InsertOneAsync(new BsonDocument { { "val", 42 } });

        var result = await col.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$project", new BsonDocument
            {
                { "asDbl", new BsonDocument("$convert", new BsonDocument
                    {
                        { "input", "$val" },
                        { "to", 1 }  // 1 = double
                    })
                }
            }))
            .FirstAsync();

        Assert.Equal(BsonType.Double, result["asDbl"].BsonType);
        Assert.Equal(42.0, result["asDbl"].AsDouble);
    }

    #endregion
}
