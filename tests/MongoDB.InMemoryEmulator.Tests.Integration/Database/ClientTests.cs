using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using AwesomeAssertions;

namespace MongoDB.InMemoryEmulator.Tests.Integration.Database;

/// <summary>
/// Phase 1 integration tests: Client-level operations (ListDatabases, DropDatabase).
/// </summary>
[Collection("Integration")]
public class ClientTests : IAsyncLifetime
{
    private readonly MongoDbSession _session;
    private ITestCollectionFixture _fixture = null!;

    public ClientTests(MongoDbSession session)
    {
        _session = session;
    }

    public ValueTask InitializeAsync()
    {
        _fixture = TestFixtureFactory.Create(_session);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public void GetDatabase_returns_database_instance()
    {
        var db = _fixture.Client.GetDatabase("test_db");
        db.Should().NotBeNull();
        db.DatabaseNamespace.DatabaseName.Should().Be("test_db");
    }

    [Fact]
    public void Client_settings_are_accessible()
    {
        var settings = _fixture.Client.Settings;
        settings.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task ListDatabaseNames_includes_databases_with_data()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/listDatabases/
        //   "The listDatabases command provides a list of all existing databases."
        var client = _fixture.Client;
        var dbName = $"listdb_test_{Guid.NewGuid():N}";
        var db = client.GetDatabase(dbName);
        var collection = db.GetCollection<TestDoc>("data");
        await collection.InsertOneAsync(new TestDoc { Name = "Data" });

        try
        {
            var cursor = await client.ListDatabaseNamesAsync();
            var names = await cursor.ToListAsync();
            names.Should().Contain(dbName);
        }
        finally
        {
            await client.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    [Trait(TestTraits.Target, TestTraits.All)]
    public async Task DropDatabase_removes_database()
    {
        // Ref: https://www.mongodb.com/docs/manual/reference/command/dropDatabase/
        //   "The dropDatabase command drops the current database."
        var client = _fixture.Client;
        var dbName = $"drop_db_test_{Guid.NewGuid():N}";
        var db = client.GetDatabase(dbName);
        await db.GetCollection<TestDoc>("data").InsertOneAsync(new TestDoc { Name = "WillBeDropped" });

        await client.DropDatabaseAsync(dbName);

        var cursor = await client.ListDatabaseNamesAsync();
        var names = await cursor.ToListAsync();
        names.Should().NotContain(dbName);
    }
}
