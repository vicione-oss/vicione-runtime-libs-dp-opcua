using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaServerInstanceManager_
{
    /// <summary>
    /// No server in these tests ever listens — <see cref="IOpcUaServer"/> is substituted throughout — so the port
    /// only has to differ between communications that are meant to differ.
    /// </summary>
    private const int UnboundTestPort = 55555;

    private readonly ILogger<IOpcUaServer> _logger = Substitute.For<ILogger<IOpcUaServer>>();

    private static OpcUaServerDataPortCommunication CreateCommunication(Action<OpcUaServerDataPortProperties>? configure = default, IReadOnlyCollection<Node>? nodes = null)
    {
        OpcUaServerDataPortCommunication communication = new() { Nodes = nodes ?? [] };
        OpcUaServerDataPortProperties properties = new(communication)
        {
            ApplicationName = "Test",
            ApplicationUri = "urn:uadataport:OPCUA:Test",
            Namespace = "http://localhost/test",
            Server = "localhost",
            Port = UnboundTestPort,
            Endpoint = "ua/dataport",
        };
        if (configure is not null)
            configure(properties);
        return communication;
    }

    [Fact]
    public async Task Creates_new_server()
    {
        using OpcUaServerInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaServer>());
        using CancellationTokenSource cancellation = new();

        OpcUaServerDataPortCommunication communication = new();
        var server = instanceManager.GetOrRegisterOpcUaServer(communication, new(), _logger);
        await instanceManager.StartOpcUaServer(communication, cancellation.Token);

        server.Should().NotBeNull();
        await server.Received(1).StartAsync(cancellation.Token);

        await instanceManager.StopOpcUaServer(communication, cancellation.Token);
        await server.Received(1).StopAsync(cancellation.Token);
    }

    [Fact]
    public async Task Returns_existing_server()
    {
        var communication1 = CreateCommunication();
        var communication2 = CreateCommunication();

        using OpcUaServerInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaServer>());
        using CancellationTokenSource cancellation = new();

        var server1 = instanceManager.GetOrRegisterOpcUaServer(communication1, new(), _logger);
        var server2 = instanceManager.GetOrRegisterOpcUaServer(communication2, new(), _logger);
        await instanceManager.StartOpcUaServer(communication1, cancellation.Token);
        await instanceManager.StartOpcUaServer(communication2, cancellation.Token);

        server1.Should().BeSameAs(server2);
        await server1.Received(1).StartAsync(cancellation.Token);

        await instanceManager.StopOpcUaServer(communication1, cancellation.Token);
        await instanceManager.StopOpcUaServer(communication1, cancellation.Token);
    }

    [Fact]
    public void Shares_instance_and_respects_nodes()
    {
        List<Node> nodes1 = [];
        List<Node> nodes2 = [];
        var communication1 = CreateCommunication(nodes: nodes1);
        var communication2 = CreateCommunication(nodes: nodes2);

        var server = Substitute.For<IOpcUaServer>();
        var createdServer = false;
        using OpcUaServerInstanceManager instanceManager = new((_, _) =>
        {
            if (createdServer)
                throw new InvalidOperationException("Server should only be created once.");
            createdServer = true;
            return server;
        });

        var server1 = instanceManager.GetOrRegisterOpcUaServer(communication1, new(), _logger);
        var server2 = instanceManager.GetOrRegisterOpcUaServer(communication2, new(), _logger);

        server1.Should().BeSameAs(server2);
        server1.Should().Be(server);
        server.Received().AddNodes(nodes1);
        server.Received().AddNodes(nodes2);
    }

    [Fact]
    public async Task Creates_new_server_for_different_communication()
    {
        var communication1 = CreateCommunication();
        var communication2 = CreateCommunication(properties => properties.User = "admin1");

        using OpcUaServerInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaServer>());
        var instanceHandle = new object();
        using CancellationTokenSource cancellation = new();

        var server1 = instanceManager.GetOrRegisterOpcUaServer(communication1, instanceHandle, _logger);
        var server2 = instanceManager.GetOrRegisterOpcUaServer(communication2, instanceHandle, _logger);
        await instanceManager.StartOpcUaServer(communication1, cancellation.Token);
        await instanceManager.StartOpcUaServer(communication2, cancellation.Token);

        server1.Should().NotBeSameAs(server2);
        await server1.Received(1).StartAsync(cancellation.Token);
        await server2.Received(1).StartAsync(cancellation.Token);

        await instanceManager.StopOpcUaServer(communication1, cancellation.Token);
        await instanceManager.StopOpcUaServer(communication2, cancellation.Token);
    }

    [Fact]
    public async Task Releases_server()
    {
        var communication = CreateCommunication();

        using OpcUaServerInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaServer, IDisposable>());
        var instanceHandle = new object();
        using CancellationTokenSource cancellation = new();

        var server1 = instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, _logger);
        await instanceManager.ReleaseOpcUaServerAsync(communication, instanceHandle, cancellation.Token);

        var server2 = instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, _logger);

        server1.Should().NotBeSameAs(server2);
        ((IDisposable)server1).Received(1).Dispose();
        ((IDisposable)server2).DidNotReceiveWithAnyArgs().Dispose();
    }

    [Fact]
    public async Task Only_releases_correct_server()
    {
        var communication1 = CreateCommunication();
        var communication2 = CreateCommunication(properties =>
        {
            properties.Password = "password1";
            properties.User = "admin1";
        });

        using OpcUaServerInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaServer, IDisposable>());
        var instanceHandle = new object();
        using CancellationTokenSource cancellation = new();

        var server1 = instanceManager.GetOrRegisterOpcUaServer(communication1, instanceHandle, _logger);
        var server2 = instanceManager.GetOrRegisterOpcUaServer(communication2, instanceHandle, _logger);

        await instanceManager.ReleaseOpcUaServerAsync(communication1, instanceHandle, cancellation.Token);

        var server3 = instanceManager.GetOrRegisterOpcUaServer(communication1, instanceHandle, _logger);
        var server4 = instanceManager.GetOrRegisterOpcUaServer(communication2, instanceHandle, _logger);

        server1.Should().NotBeNull();
        server2.Should().NotBeNull();
        server3.Should().NotBeNull();
        server4.Should().NotBeNull();
        server1.Should().NotBeSameAs(server3);
        server2.Should().BeSameAs(server4);
        ((IDisposable)server1).Received(1).Dispose();
        ((IDisposable)server2).DidNotReceiveWithAnyArgs().Dispose();
        ((IDisposable)server3).DidNotReceiveWithAnyArgs().Dispose();
        ((IDisposable)server4).DidNotReceiveWithAnyArgs().Dispose();
    }
}
