using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Opc.Ua;
using ViciOne.ManagedEngine.Runtime;
using Xunit;

namespace ViciOne.Suite.DataPort;

internal static class OutgoingServerSetup
{
    private static readonly Guid s_folderId = Guid.Parse("d05ae944-d731-4cfc-ad02-0b3f35a207a4");
    private static readonly Guid s_dataPointId = Guid.Parse("a631ad22-3e31-483e-9242-8c4198864c6b");

    internal static OpcUaServerDataPortCommunication CreateCommunication(IReadOnlyCollection<Node>? nodes = default, Action<OpcUaServerDataPortProperties>? configure = default)
    {
        OpcUaServerDataPortCommunication communication = new()
        {
            Nodes = nodes ?? [],
        };
        OpcUaServerDataPortProperties properties = new(communication);
        if (configure is not null)
            configure(properties);
        return communication;
    }

    internal static OpcUaServerDataPortCommunication CreateCommunicationWithStatusCodeChild()
        => CreateCommunication(
            [
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.Folder,
                    Name = "parent",
                    Id = s_folderId,
                },
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.Variable,
                    Name = "child",
                    TransferredChannels = ["readonly", "statusChannel",],
                    AffectedChannels = ["readonly",],
                    Id = s_dataPointId,
                    ParentId = s_folderId,
                    ValueType = typeof(string),
                    Properties = new()
                    {
                        { OpcUaServerDataPortPropertyNames.ReadOnly, new() { Value = true } },
                    }
                },
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.StatusCode,
                    Name = "quality",
                    AffectedChannels = ["statusChannel",],
                    Id = Guid.Parse("5f0f1a3e-0b58-4d9b-8f2a-1ec2ef6f1d21"),
                    ParentId = s_dataPointId,
                    ValueType = typeof(string),
                },
            ]);

    internal static OpcUaServerDataPortCommunication CreateCommunicationWithSourceTimestampChild()
        => CreateCommunication(
            [
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.Folder,
                    Name = "parent",
                    Id = s_folderId,
                },
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.Variable,
                    Name = "child",
                    TransferredChannels = ["readonly", "sent",],
                    AffectedChannels = ["readonly",],
                    Id = s_dataPointId,
                    ParentId = s_folderId,
                    ValueType = typeof(string),
                },
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.SourceTimestamp,
                    Name = "when",
                    AffectedChannels = ["sent",],
                    Id = Guid.Parse("6e0d4b81-2c3a-45f7-9d18-4b5a6c7d8e9f"),
                    ParentId = s_dataPointId,
                    ValueType = typeof(DateTime),
                },
            ]);

    internal static OpcUaServerDataPortCommunication CreateCommunicationWithStatusCodeAndSourceTimestampChildren()
        => CreateCommunication(
            [
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.Folder,
                    Name = "parent",
                    Id = s_folderId,
                },
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.Variable,
                    Name = "child",
                    TransferredChannels = ["readonly", "statusChannel", "sent",],
                    AffectedChannels = ["readonly",],
                    Id = s_dataPointId,
                    ParentId = s_folderId,
                    ValueType = typeof(int),
                },
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.StatusCode,
                    Name = "quality",
                    AffectedChannels = ["statusChannel",],
                    Id = Guid.Parse("5f0f1a3e-0b58-4d9b-8f2a-1ec2ef6f1d21"),
                    ParentId = s_dataPointId,
                    ValueType = typeof(string),
                },
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.SourceTimestamp,
                    Name = "when",
                    AffectedChannels = ["sent",],
                    Id = Guid.Parse("6e0d4b81-2c3a-45f7-9d18-4b5a6c7d8e9f"),
                    ParentId = s_dataPointId,
                    ValueType = typeof(DateTime),
                },
            ]);

    internal static (IOpcUaServerInstanceManager InstanceManager, IOpcUaServer Server, ILogger<IOpcUaServer> Logger) SubstituteServer(OpcUaServerDataPortCommunication communication, ILogger<IOpcUaServer>? logger = null)
    {
        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        logger ??= Substitute.For<ILogger<IOpcUaServer>>();

        instanceManager.GetOrRegisterOpcUaServer(communication, Arg.Any<OpcUaServerDataPortOutgoing>(), logger).Returns(server);

        return (instanceManager, server, logger);
    }
}

public class OpcUaServerDataPortOutgoing_
{
    [Fact]
    public async Task Fails_the_connect_after_the_server_was_released_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunication();

        var server = Substitute.For<IOpcUaServer, IDisposable>();
        await using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortOutgoing dataPortOutgoing = new(communication, instanceManager, new FakeLogger<IOpcUaServer>());

        await dataPortOutgoing.DisposeAsync();

        await dataPortOutgoing.Awaiting(port => port.ConnectAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DependencyInjectionProviderFactory_can_create_instance_Async()
    {
        await using var providerFactory = new ConstructorProviderFactory();

        var communication = OutgoingServerSetup.CreateCommunication();

        var instance = providerFactory.CreateExternalOutgoing(
            communication,
            new(),
            Substitute.For<ILoggerFactory>(),
            Substitute.For<INameResolver>(),
            AssemblyLoadContext.Default);

        _ = instance.Should().NotBeNull().And.BeOfType<OpcUaServerDataPortOutgoing>();
    }

    [Fact]
    public async Task Registers_instance_correctly_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunication();

        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        var logger = Substitute.For<ILogger<IOpcUaServer>>();
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);
        instanceManager.GetOrRegisterOpcUaServer(communication, dataportOutgoing, logger).Returns(server);

        await dataportOutgoing.ConnectAsync(cancellation.Token);

        instanceManager.Received().GetOrRegisterOpcUaServer(communication, dataportOutgoing, logger);
    }

    [Fact]
    public async Task Releases_server_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunication();

        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        var logger = Substitute.For<ILogger<IOpcUaServer>>();
        using CancellationTokenSource cancellation = new();
        OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);
        instanceManager.GetOrRegisterOpcUaServer(communication, dataportOutgoing, logger).Returns(server);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.DisconnectAsync(cancellation.Token);

        await dataportOutgoing.DisposeAsync();

        Received.InOrder(async () =>
        {
            instanceManager.GetOrRegisterOpcUaServer(communication, dataportOutgoing, logger);
            await instanceManager.StartOpcUaServer(communication, dataportOutgoing, cancellation.Token);
            await instanceManager.StopOpcUaServer(communication, dataportOutgoing, cancellation.Token);
            await instanceManager.ReleaseOpcUaServerAsync(communication, dataportOutgoing, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Sends_value_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunication(
            [
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.Folder,
                    Name = "parent",
                    Id = Guid.Parse("d05ae944-d731-4cfc-ad02-0b3f35a207a4"),
                },
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.Variable,
                    Name = "child",
                    TransferredChannels = ["readonly",],
                    Id = Guid.Parse("a631ad22-3e31-483e-9242-8c4198864c6b"),
                    ParentId =  Guid.Parse("d05ae944-d731-4cfc-ad02-0b3f35a207a4"),
                    ValueType = typeof(string),
                    Properties = new()
                    {
                        { OpcUaServerDataPortPropertyNames.ReadOnly, new() { Value = true } },
                    },
                },
            ]);

        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        var logger = Substitute.For<ILogger<IOpcUaServer>>();
        using CancellationTokenSource cancellation = new();
        instanceManager.GetOrRegisterOpcUaServer(communication, Arg.Any<OpcUaServerDataPortOutgoing>(), logger).Returns(server);
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.SendAsync(0,
        [
            new()
            {
                Channel = "readonly",
                Timestamp = new DateTime(2023, 11, 20),
                Value = "value",
            }
        ], cancellation.Token);

        Received.InOrder(async () =>
        {
            instanceManager.GetOrRegisterOpcUaServer(communication, dataportOutgoing, logger);
            await instanceManager.StartOpcUaServer(communication, dataportOutgoing, cancellation.Token);
            await server.PublishValueAsync("readonly", "value", new DateTime(2023, 11, 20), null, cancellation.Token);
        });
    }

    [Fact]
    public async Task Sends_status_code_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunicationWithStatusCodeChild();
        var (instanceManager, server, logger) = OutgoingServerSetup.SubstituteServer(communication);
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.SendAsync(0,
            [
                new()
                {
                    Channel = "statusChannel",
                    Timestamp = new DateTime(2023, 11, 20),
                    Value = "Uncertain",
                }
            ], cancellation.Token);

        Received.InOrder(async () =>
        {
            instanceManager.GetOrRegisterOpcUaServer(communication, dataportOutgoing, logger);
            await server.SetNodeStatusAsync("readonly", StatusCodes.Uncertain, cancellation.Token);
        });
    }
}

public class OpcUaServerDataPortOutgoing_ctor
{
    [Fact]
    public void Refuses_a_tree_the_envelope_cannot_be_built_from()
    {
        var communication = OutgoingServerSetup.CreateCommunication(
            [
                new()
                {
                    DesignId = OpcUaServerNodeDesignId.StatusCode,
                    Name = "quality",
                    AffectedChannels = ["statusChannel",],
                    Id = Guid.Parse("5f0f1a3e-0b58-4d9b-8f2a-1ec2ef6f1d21"),
                    ParentId = Guid.Parse("a631ad22-3e31-483e-9242-8c4198864c6b"),
                },
            ]);

        var act = () => new OpcUaServerDataPortOutgoing(communication, Substitute.For<IOpcUaServerInstanceManager>(), Substitute.For<ILogger<IOpcUaServer>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'quality' is an envelope child of a node that is not part of this data port*");
    }
}

public class OpcUaServerDataPortOutgoing_SendAsync
{
    private static readonly DateTime s_timestamp = new(2023, 11, 20);

    /// <summary>
    /// The engine attributes the channel of an envelope child to the transferring parent, so a
    /// status code written into the value of the variable is the failure this routing prevents. A
    /// status that arrives with its value is served with it, in the same write.
    /// </summary>
    [Fact]
    public async Task Serves_a_status_code_with_the_value_of_its_cycle_and_not_as_a_value_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunicationWithStatusCodeChild();
        var (instanceManager, server, logger) = OutgoingServerSetup.SubstituteServer(communication);
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.SendAsync(0,
            [
                new() { Channel = "statusChannel", Timestamp = s_timestamp, Value = "Uncertain", },
                new() { Channel = "readonly", Timestamp = s_timestamp, Value = "value", },
            ], cancellation.Token);

        await server.DidNotReceive().PublishValueAsync("statusChannel", Arg.Any<object?>(), Arg.Any<DateTime>(), Arg.Any<StatusCode?>(), Arg.Any<CancellationToken>());
        await server.Received(1).PublishValueAsync("readonly", "value", s_timestamp, StatusCodes.Uncertain, cancellation.Token);
        await server.DidNotReceive().SetNodeStatusAsync(Arg.Any<string>(), Arg.Any<StatusCode>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The engine sends a channel only in the cycle it changes in, so a status code that arrived
    /// earlier is served with every later value of its variable.
    /// </summary>
    [Fact]
    public async Task Serves_a_later_value_with_the_status_code_linked_before_it_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunicationWithStatusCodeChild();
        var (instanceManager, server, logger) = OutgoingServerSetup.SubstituteServer(communication);
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.SendAsync(0, [new() { Channel = "statusChannel", Timestamp = s_timestamp, Value = "Uncertain", },], cancellation.Token);
        await dataportOutgoing.SendAsync(1, [new() { Channel = "readonly", Timestamp = s_timestamp, Value = "value", },], cancellation.Token);

        await server.Received(1).PublishValueAsync("readonly", "value", s_timestamp, StatusCodes.Uncertain, cancellation.Token);
    }

    [Fact]
    public async Task Serves_a_value_as_BadInternalError_when_its_status_code_cannot_be_read_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunicationWithStatusCodeChild();
        FakeLogger<IOpcUaServer> fakeLogger = new();
        var (instanceManager, server, logger) = OutgoingServerSetup.SubstituteServer(communication, fakeLogger);
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.SendAsync(0,
            [
                new() { Channel = "statusChannel", Timestamp = s_timestamp, Value = "0xZZ", },
                new() { Channel = "readonly", Timestamp = s_timestamp, Value = "value", },
            ], cancellation.Token);

        await server.Received(1).PublishValueAsync("readonly", "value", s_timestamp, StatusCodes.BadInternalError, cancellation.Token);
        fakeLogger.Collector.GetSnapshot().Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("'0xZZ'"));
    }

    /// <summary>
    /// A cancelled cycle serves nothing further: neither a value nor the status code of a variable
    /// that has no value in the cycle.
    /// </summary>
    [Theory]
    [InlineData("readonly", "value")]
    [InlineData("statusChannel", "Uncertain")]
    public async Task Serves_nothing_once_the_cycle_is_cancelled_Async(string channel, string value)
    {
        var communication = OutgoingServerSetup.CreateCommunicationWithStatusCodeChild();
        var (instanceManager, server, logger) = OutgoingServerSetup.SubstituteServer(communication);
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await dataportOutgoing.SendAsync(0, [new() { Channel = channel, Timestamp = s_timestamp, Value = value, },], cancellation.Token);

        await server.DidNotReceive().PublishValueAsync(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<DateTime>(), Arg.Any<StatusCode?>(), Arg.Any<CancellationToken>());
        await server.DidNotReceive().SetNodeStatusAsync(Arg.Any<string>(), Arg.Any<StatusCode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Serves_a_value_with_the_source_timestamp_linked_to_its_envelope_child_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunicationWithSourceTimestampChild();
        var (instanceManager, server, logger) = OutgoingServerSetup.SubstituteServer(communication);
        using CancellationTokenSource cancellation = new();
        var produced = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.SendAsync(0,
            [
                new() { Channel = "sent", Timestamp = s_timestamp, Value = produced, },
                new() { Channel = "readonly", Timestamp = s_timestamp, Value = "value", },
            ], cancellation.Token);

        await server.Received(1).PublishValueAsync("readonly", "value", produced, null, cancellation.Token);
    }

    [Fact]
    public async Task Serves_a_value_with_a_bad_status_code_together_with_its_source_timestamp_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunicationWithStatusCodeAndSourceTimestampChildren();
        var (instanceManager, server, logger) = OutgoingServerSetup.SubstituteServer(communication);
        using CancellationTokenSource cancellation = new();
        var produced = new DateTime(2026, 3, 4, 6, 4, 0, DateTimeKind.Utc);
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.SendAsync(0,
            [
                new() { Channel = "statusChannel", Timestamp = s_timestamp, Value = "BadSensorFailure", },
                new() { Channel = "sent", Timestamp = s_timestamp, Value = produced, },
                new() { Channel = "readonly", Timestamp = s_timestamp, Value = 4, },
            ], cancellation.Token);

        await server.Received(1).PublishValueAsync("readonly", 4, produced, StatusCodes.BadSensorFailure, cancellation.Token);
    }

    /// <summary>
    /// Nothing linked leaves the value served with the timestamp the engine gave it.
    /// </summary>
    [Fact]
    public async Task Serves_a_value_with_its_own_timestamp_when_no_source_timestamp_is_linked_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunicationWithSourceTimestampChild();
        var (instanceManager, server, logger) = OutgoingServerSetup.SubstituteServer(communication);
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.SendAsync(0,
            [
                new() { Channel = "readonly", Timestamp = s_timestamp, Value = "value", },
            ], cancellation.Token);

        await server.Received(1).PublishValueAsync("readonly", "value", s_timestamp, null, cancellation.Token);
    }

    /// <summary>
    /// A source timestamp belongs to the value it arrives with. The engine does not send a
    /// timestamp again that did not change, so carrying it over would serve a later value with
    /// the time of an earlier one.
    /// </summary>
    [Fact]
    public async Task Serves_a_value_of_a_later_cycle_with_its_own_timestamp_Async()
    {
        var communication = OutgoingServerSetup.CreateCommunicationWithSourceTimestampChild();
        var (instanceManager, server, logger) = OutgoingServerSetup.SubstituteServer(communication);
        using CancellationTokenSource cancellation = new();
        var produced = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var later = new DateTime(2026, 3, 4, 5, 7, 0, DateTimeKind.Utc);
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.SendAsync(0,
            [
                new() { Channel = "sent", Timestamp = s_timestamp, Value = produced, },
                new() { Channel = "readonly", Timestamp = s_timestamp, Value = "value", },
            ], cancellation.Token);
        await dataportOutgoing.SendAsync(1,
            [
                new() { Channel = "readonly", Timestamp = later, Value = "later value", },
            ], cancellation.Token);

        await server.Received(1).PublishValueAsync("readonly", "later value", later, null, cancellation.Token);
    }
}
