using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ViciOne.ManagedEngine.Runtime;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaServerDataPortOutgoing_
{
    private static OpcUaServerDataPortCommunication CreateCommunication(IReadOnlyCollection<Node>? nodes = default, Action<OpcUaServerDataPortProperties>? configure = default)
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

    [Fact]
    public async Task DependencyInjectionProviderFactory_can_create_instance_Async()
    {
        await using var providerFactory = new ConstructorProviderFactory();

        var communication = CreateCommunication();

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
        var communication = CreateCommunication();

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
        var communication = CreateCommunication();

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
            await instanceManager.StartOpcUaServer(communication, cancellation.Token);
            await instanceManager.StopOpcUaServer(communication, cancellation.Token);
            await instanceManager.ReleaseOpcUaServerAsync(communication, dataportOutgoing, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Sends_value_Async()
    {
        var communication = CreateCommunication(
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
        ], [], cancellation.Token);

        Received.InOrder(async () =>
        {
            instanceManager.GetOrRegisterOpcUaServer(communication, dataportOutgoing, logger);
            await instanceManager.StartOpcUaServer(communication, cancellation.Token);
            await server.PublishValueAsync("readonly", "value", new DateTime(2023, 11, 20), cancellation.Token);
        });
    }

    [Fact]
    public async Task Sends_status_code_Async()
    {
        var communication = CreateCommunication(
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
                    AffectedChannels = ["readonly",],
                    Id = Guid.Parse("a631ad22-3e31-483e-9242-8c4198864c6b"),
                    ParentId =  Guid.Parse("d05ae944-d731-4cfc-ad02-0b3f35a207a4"),
                    ValueType = typeof(string),
                    Properties = new()
                    {
                        { OpcUaServerDataPortPropertyNames.Status, new() { Value  = "Good", Channels = ["statusChannel"] } },
                        { OpcUaServerDataPortPropertyNames.ReadOnly, new() { Value = true } },
                    }
                },
            ]);

        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        var logger = Substitute.For<ILogger<IOpcUaServer>>();
        using CancellationTokenSource cancellation = new();
        instanceManager.GetOrRegisterOpcUaServer(communication, Arg.Any<OpcUaServerDataPortOutgoing>(), logger).Returns(server);
        await using OpcUaServerDataPortOutgoing dataportOutgoing = new(communication, instanceManager, logger);

        await dataportOutgoing.ConnectAsync(cancellation.Token);
        await dataportOutgoing.SendAsync(0, [],
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
            await server.SetNodeStatusAsync("readonly", "Uncertain", cancellation.Token);
        });
    }
}
