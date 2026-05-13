using global::MongoDB.Bson;
using global::MongoDB.Driver;
using global::MongoDB.Driver.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InMemoryEmulator.MongoDB.Tests.Integration;

/// <summary>
/// Tests for command monitoring events (CommandStartedEvent, CommandSucceededEvent, CommandFailedEvent).
/// </summary>
/// <remarks>
/// Ref: https://www.mongodb.com/docs/drivers/csharp/current/fundamentals/logging/
///   "The driver raises CommandStartedEvent, CommandSucceededEvent, and CommandFailedEvent
///    for each command sent to the server."
/// </remarks>
public class CommandMonitoringTests
{
    [Fact]
    public void InsertOne_fires_CommandStartedEvent_and_CommandSucceededEvent()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();
        var succeededEvents = new List<CommandSucceededEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
            builder.Subscribe<CommandSucceededEvent>(e => succeededEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");

        // Act
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "item", "widget" } });

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("insert", startedEvents[0].CommandName);
        Assert.Equal("testdb", startedEvents[0].DatabaseNamespace.DatabaseName);
        Assert.NotNull(startedEvents[0].Command);
        Assert.Equal("orders", startedEvents[0].Command["insert"].AsString);

        Assert.Single(succeededEvents);
        Assert.Equal("insert", succeededEvents[0].CommandName);
        Assert.True(succeededEvents[0].Duration >= TimeSpan.Zero);
    }

    [Fact]
    public void Find_fires_CommandStartedEvent_with_find_command()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();
        var succeededEvents = new List<CommandSucceededEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
            builder.Subscribe<CommandSucceededEvent>(e => succeededEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertOne(new BsonDocument { { "_id", 1 } });
        startedEvents.Clear();
        succeededEvents.Clear();

        // Act
        col.Find(Builders<BsonDocument>.Filter.Eq("_id", 1)).ToList();

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("find", startedEvents[0].CommandName);
        Assert.Equal("orders", startedEvents[0].Command["find"].AsString);

        Assert.Single(succeededEvents);
        Assert.Equal("find", succeededEvents[0].CommandName);
    }

    [Fact]
    public void DeleteOne_fires_command_events()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();
        var succeededEvents = new List<CommandSucceededEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
            builder.Subscribe<CommandSucceededEvent>(e => succeededEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertOne(new BsonDocument { { "_id", 1 } });
        startedEvents.Clear();
        succeededEvents.Clear();

        // Act
        col.DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", 1));

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("delete", startedEvents[0].CommandName);
        Assert.Equal("orders", startedEvents[0].Command["delete"].AsString);

        Assert.Single(succeededEvents);
        Assert.Equal("delete", succeededEvents[0].CommandName);
    }

    [Fact]
    public void UpdateOne_fires_command_events()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();
        var succeededEvents = new List<CommandSucceededEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
            builder.Subscribe<CommandSucceededEvent>(e => succeededEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "qty", 5 } });
        startedEvents.Clear();
        succeededEvents.Clear();

        // Act
        col.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Set("qty", 10));

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("update", startedEvents[0].CommandName);
        Assert.Equal("orders", startedEvents[0].Command["update"].AsString);

        Assert.Single(succeededEvents);
        Assert.Equal("update", succeededEvents[0].CommandName);
    }

    [Fact]
    public void Aggregate_fires_command_events()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();
        var succeededEvents = new List<CommandSucceededEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
            builder.Subscribe<CommandSucceededEvent>(e => succeededEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "status", "A" } });
        startedEvents.Clear();
        succeededEvents.Clear();

        // Act
        col.Aggregate().Match(Builders<BsonDocument>.Filter.Eq("status", "A")).ToList();

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("aggregate", startedEvents[0].CommandName);
        Assert.Equal("orders", startedEvents[0].Command["aggregate"].AsString);

        Assert.Single(succeededEvents);
        Assert.Equal("aggregate", succeededEvents[0].CommandName);
    }

    [Fact]
    public void ReplaceOne_fires_command_events()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "item", "old" } });
        startedEvents.Clear();

        // Act
        col.ReplaceOne(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument { { "_id", 1 }, { "item", "new" } });

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("update", startedEvents[0].CommandName);
        Assert.Equal("orders", startedEvents[0].Command["update"].AsString);
    }

    [Fact]
    public void FindOneAndDelete_fires_command_events()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertOne(new BsonDocument { { "_id", 1 } });
        startedEvents.Clear();

        // Act
        col.FindOneAndDelete(Builders<BsonDocument>.Filter.Eq("_id", 1));

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("findAndModify", startedEvents[0].CommandName);
    }

    [Fact]
    public void FindOneAndUpdate_fires_command_events()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "qty", 5 } });
        startedEvents.Clear();

        // Act
        col.FindOneAndUpdate(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            Builders<BsonDocument>.Update.Set("qty", 10));

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("findAndModify", startedEvents[0].CommandName);
    }

    [Fact]
    public void FindOneAndReplace_fires_command_events()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertOne(new BsonDocument { { "_id", 1 }, { "item", "old" } });
        startedEvents.Clear();

        // Act
        col.FindOneAndReplace(
            Builders<BsonDocument>.Filter.Eq("_id", 1),
            new BsonDocument { { "_id", 1 }, { "item", "new" } });

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("findAndModify", startedEvents[0].CommandName);
    }

    [Fact]
    public void DeleteMany_fires_command_events()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 } },
            new BsonDocument { { "_id", 2 } }
        });
        startedEvents.Clear();

        // Act
        col.DeleteMany(Builders<BsonDocument>.Filter.Empty);

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("delete", startedEvents[0].CommandName);
    }

    [Fact]
    public void UpdateMany_fires_command_events()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertMany(new[]
        {
            new BsonDocument { { "_id", 1 }, { "qty", 5 } },
            new BsonDocument { { "_id", 2 }, { "qty", 5 } }
        });
        startedEvents.Clear();

        // Act
        col.UpdateMany(
            Builders<BsonDocument>.Filter.Empty,
            Builders<BsonDocument>.Update.Set("qty", 10));

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("update", startedEvents[0].CommandName);
    }

    [Fact]
    public void CommandFailedEvent_fires_when_operation_throws()
    {
        // Arrange
        var failedEvents = new List<CommandFailedEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandFailedEvent>(e => failedEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");
        col.InsertOne(new BsonDocument { { "_id", 1 } });

        // Act — duplicate key should fail
        Assert.ThrowsAny<MongoWriteException>(() =>
            col.InsertOne(new BsonDocument { { "_id", 1 } }));

        // Assert
        Assert.Single(failedEvents);
        Assert.Equal("insert", failedEvents[0].CommandName);
        Assert.NotNull(failedEvents[0].Failure);
    }

    [Fact]
    public void No_events_fired_when_no_subscribers_configured()
    {
        // Arrange — client with no subscriber
        var client = new InMemoryMongoClient();
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");

        // Act — should work fine without any exceptions
        col.InsertOne(new BsonDocument { { "_id", 1 } });
        col.Find(Builders<BsonDocument>.Filter.Empty).ToList();
        col.DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", 1));

        // Assert — no exceptions thrown, operations work normally
        Assert.Empty(col.Find(Builders<BsonDocument>.Filter.Empty).ToList());
    }

    [Fact]
    public void DI_UseInMemoryMongoDB_supports_ClusterConfigurator()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.UseInMemoryMongoDB(options =>
        {
            options.DatabaseName = "testdb";
            options.AddCollection<BsonDocument>("orders");
            options.ClusterConfigurator = builder =>
            {
                builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
            };
        });

        var provider = services.BuildServiceProvider();
        var col = provider.GetRequiredService<IMongoCollection<BsonDocument>>();

        // Act
        col.InsertOne(new BsonDocument { { "_id", 1 } });

        // Assert
        Assert.Single(startedEvents);
        Assert.Equal("insert", startedEvents[0].CommandName);
    }

    [Fact]
    public void RequestId_increments_across_operations()
    {
        // Arrange
        var startedEvents = new List<CommandStartedEvent>();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe<CommandStartedEvent>(e => startedEvents.Add(e));
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");

        // Act
        col.InsertOne(new BsonDocument { { "_id", 1 } });
        col.InsertOne(new BsonDocument { { "_id", 2 } });

        // Assert
        Assert.Equal(2, startedEvents.Count);
        Assert.True(startedEvents[1].RequestId > startedEvents[0].RequestId);
    }

    [Fact]
    public void IEventSubscriber_interface_can_be_used_directly()
    {
        // Arrange
        var subscriber = new TestEventSubscriber();

        var client = new InMemoryMongoClient(commandEventSubscribers: builder =>
        {
            builder.Subscribe(subscriber);
        });
        var db = client.GetDatabase("testdb");
        var col = db.GetCollection<BsonDocument>("orders");

        // Act
        col.InsertOne(new BsonDocument { { "_id", 1 } });

        // Assert
        Assert.Single(subscriber.StartedEvents);
        Assert.Equal("insert", subscriber.StartedEvents[0].CommandName);
    }

    private class TestEventSubscriber : IEventSubscriber
    {
        public List<CommandStartedEvent> StartedEvents { get; } = new();
        public List<CommandSucceededEvent> SucceededEvents { get; } = new();

        public bool TryGetEventHandler<TEvent>(out Action<TEvent> handler)
        {
            if (typeof(TEvent) == typeof(CommandStartedEvent))
            {
                handler = (Action<TEvent>)(object)(Action<CommandStartedEvent>)(e => StartedEvents.Add(e));
                return true;
            }
            if (typeof(TEvent) == typeof(CommandSucceededEvent))
            {
                handler = (Action<TEvent>)(object)(Action<CommandSucceededEvent>)(e => SucceededEvents.Add(e));
                return true;
            }
            handler = null!;
            return false;
        }
    }
}
