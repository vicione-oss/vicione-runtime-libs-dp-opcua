using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ViciOne.ManagedEngine.Runtime;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaServerDataPortIncoming_
{
    private static OpcUaServerDataPortCommunication CreateCommunication(IReadOnlyCollection<Node>? nodes = default, Action<OpcUaServerDataPortProperties>? configure = default)
    {
        OpcUaServerDataPortCommunication communication = new()
        {
            Nodes = nodes ?? [],
        };
        OpcUaServerDataPortProperties properties = new(communication)
        {
            Password = "password",
            User = "admin",
        };
        if (configure is not null)
            configure(properties);
        return communication;
    }

    [Fact]
    public async Task DependencyInjectionProviderFactory_can_create_instance_Async()
    {
        await using var providerFactory = new ConstructorProviderFactory();

        var communication = CreateCommunication();

        var instance = providerFactory.CreateExternalIncoming(
            communication,
            new(),
            Substitute.For<ILoggerFactory>(),
            Substitute.For<INameResolver>(),
            AssemblyLoadContext.Default);

        _ = instance.Should().NotBeNull().And.BeOfType<OpcUaServerDataPortIncoming>();
    }

    [Fact]
    public async Task Registers_instance_correctly_Async()
    {
        var communication = CreateCommunication();

        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        var logger = Substitute.For<ILogger<IOpcUaServer>>();
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortIncoming dataPortIncoming = new(communication, instanceManager, logger);
        instanceManager.GetOrRegisterOpcUaServer(communication, dataPortIncoming, logger).Returns(server);

        await dataPortIncoming.ConnectAsync(cancellation.Token);

        instanceManager.Received(1).GetOrRegisterOpcUaServer(communication, dataPortIncoming, logger);
    }

    [Fact]
    public async Task Subscribes_incoming_correctly_Async()
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
                    Name = "writeableChild",
                    TransferredChannels = ["writeable",],
                    Id = Guid.Parse("a631ad22-3e31-483e-9242-8c4198864c6b"),
                    ParentId =  Guid.Parse("d05ae944-d731-4cfc-ad02-0b3f35a207a4"),
                    ValueType = typeof(string),
                },
            ]);

        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        var logger = Substitute.For<ILogger<IOpcUaServer>>();
        using CancellationTokenSource cancellation = new();
        instanceManager.GetOrRegisterOpcUaServer(communication, Arg.Any<OpcUaServerDataPortIncoming>(), logger).Returns(server);
        await using OpcUaServerDataPortIncoming dataportIncoming = new(communication, instanceManager, logger);
        await dataportIncoming.ConnectAsync(cancellation.Token);

        var hasBeenCalled = false;
        dataportIncoming.Received += e =>
        {
            hasBeenCalled = e.Any(v => ((string)(v.Value ?? string.Empty)) == "value" && v.Timestamp == new DateTime(2023, 11, 20));
        };

        server.ReceiveValue += Raise.Event<Action<string, DateTime, object>>(["writeable", new DateTime(2023, 11, 20), "value",]);

        hasBeenCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Releases_server_Async()
    {
        var communication = CreateCommunication();

        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        var logger = Substitute.For<ILogger<IOpcUaServer>>();
        using CancellationTokenSource cancellation = new();
        OpcUaServerDataPortIncoming dataportIncoming = new(communication, instanceManager, logger);
        instanceManager.GetOrRegisterOpcUaServer(communication, dataportIncoming, logger).Returns(server);

        await dataportIncoming.ConnectAsync(cancellation.Token);
        await dataportIncoming.DisconnectAsync(cancellation.Token);
        await dataportIncoming.DisposeAsync();

        Received.InOrder(async () =>
        {
            instanceManager.GetOrRegisterOpcUaServer(communication, dataportIncoming, logger);
            await instanceManager.StartOpcUaServer(communication, cancellation.Token);
            await instanceManager.StopOpcUaServer(communication, cancellation.Token);
            await instanceManager.ReleaseOpcUaServerAsync(communication, dataportIncoming, default);
        });
    }
}
