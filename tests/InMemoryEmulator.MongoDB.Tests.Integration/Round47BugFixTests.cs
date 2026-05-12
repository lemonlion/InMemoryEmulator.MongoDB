using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Round 47: $regexFind/$regexFindAll captures population
/// </summary>
public class Round47BugFixTests
{
    private static IMongoCollection<BsonDocument> CreateCollection(string name = "items")
    {
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        return db.GetCollection<BsonDocument>(name);
    }

    #region $regexFind populates captures from groups

    [Fact]
    public void Aggregate_RegexFind_WithCaptureGroups_PopulatesCaptures()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/regexFind/
        //   "captures: An array that contains the matching string for each identified capture group."
        var col = CreateCollection("rf_cap_r47");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "text", "line 1 is here" } });

        var result = col.Aggregate()
            .Project(new BsonDocument("m",
                new BsonDocument("$regexFind", new BsonDocument
                {
                    { "input", "$text" },
                    { "regex", @"line (\d+) (\w+)" }
                })))
            .First();

        var m = result["m"].AsBsonDocument;
        Assert.Equal("line 1 is", m["match"].AsString);
        Assert.Equal(0, m["idx"].AsInt32);
        var captures = m["captures"].AsBsonArray;
        Assert.Equal(2, captures.Count);
        Assert.Equal("1", captures[0].AsString);
        Assert.Equal("is", captures[1].AsString);
    }

    [Fact]
    public void Aggregate_RegexFind_NoCaptureGroups_EmptyCaptures()
    {
        var col = CreateCollection("rf_nocap_r47");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "text", "hello world" } });

        var result = col.Aggregate()
            .Project(new BsonDocument("m",
                new BsonDocument("$regexFind", new BsonDocument
                {
                    { "input", "$text" },
                    { "regex", "world" }
                })))
            .First();

        var m = result["m"].AsBsonDocument;
        Assert.Equal("world", m["match"].AsString);
        Assert.Equal(6, m["idx"].AsInt32);
        Assert.Empty(m["captures"].AsBsonArray);
    }

    [Fact]
    public void Aggregate_RegexFind_OptionalGroupNotMatched_ReturnsNull()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/regexFind/
        //   An unmatched optional group should be null in captures
        var col = CreateCollection("rf_optgrp_r47");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "text", "abc" } });

        var result = col.Aggregate()
            .Project(new BsonDocument("m",
                new BsonDocument("$regexFind", new BsonDocument
                {
                    { "input", "$text" },
                    { "regex", @"(a)(z)?(b)" }
                })))
            .First();

        var m = result["m"].AsBsonDocument;
        Assert.Equal("ab", m["match"].AsString);
        var captures = m["captures"].AsBsonArray;
        Assert.Equal(3, captures.Count);
        Assert.Equal("a", captures[0].AsString);
        Assert.True(captures[1].IsBsonNull); // optional group (z)? didn't match
        Assert.Equal("b", captures[2].AsString);
    }

    #endregion

    #region $regexFindAll populates captures from groups

    [Fact]
    public void Aggregate_RegexFindAll_WithCaptureGroups_PopulatesCaptures()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/operator/aggregation/regexFindAll/
        //   "captures: An array that contains the matching string for each identified capture group."
        var col = CreateCollection("rfa_cap_r47");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "text", "item1:10 item2:20" } });

        var result = col.Aggregate()
            .Project(new BsonDocument("matches",
                new BsonDocument("$regexFindAll", new BsonDocument
                {
                    { "input", "$text" },
                    { "regex", @"(\w+):(\d+)" }
                })))
            .First();

        var matches = result["matches"].AsBsonArray;
        Assert.Equal(2, matches.Count);

        var m0 = matches[0].AsBsonDocument;
        Assert.Equal("item1:10", m0["match"].AsString);
        Assert.Equal(2, m0["captures"].AsBsonArray.Count);
        Assert.Equal("item1", m0["captures"][0].AsString);
        Assert.Equal("10", m0["captures"][1].AsString);

        var m1 = matches[1].AsBsonDocument;
        Assert.Equal("item2:20", m1["match"].AsString);
        Assert.Equal("item2", m1["captures"][0].AsString);
        Assert.Equal("20", m1["captures"][1].AsString);
    }

    [Fact]
    public void Aggregate_RegexFindAll_NoMatch_EmptyArray()
    {
        var col = CreateCollection("rfa_nomatch_r47");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "text", "hello" } });

        var result = col.Aggregate()
            .Project(new BsonDocument("matches",
                new BsonDocument("$regexFindAll", new BsonDocument
                {
                    { "input", "$text" },
                    { "regex", @"xyz" }
                })))
            .First();

        Assert.Empty(result["matches"].AsBsonArray);
    }

    #endregion
}
