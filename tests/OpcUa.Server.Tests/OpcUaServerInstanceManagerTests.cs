using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaServerInstanceManager_
{
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
            Port = 55555,
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
        var instanceHandle = new object();
        var server = instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, _logger);
        await instanceManager.StartOpcUaServer(communication, instanceHandle, cancellation.Token);

        server.Should().NotBeNull();
        await server.Received(1).StartAsync(cancellation.Token);

        await instanceManager.StopOpcUaServer(communication, instanceHandle, cancellation.Token);
        await server.Received(1).StopAsync(cancellation.Token);
    }

    [Fact]
    public async Task Returns_existing_server()
    {
        var communication1 = CreateCommunication();
        var communication2 = CreateCommunication();

        using OpcUaServerInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaServer>());
        using CancellationTokenSource cancellation = new();

        var instanceHandle1 = new object();
        var instanceHandle2 = new object();
        var server1 = instanceManager.GetOrRegisterOpcUaServer(communication1, instanceHandle1, _logger);
        var server2 = instanceManager.GetOrRegisterOpcUaServer(communication2, instanceHandle2, _logger);
        await instanceManager.StartOpcUaServer(communication1, instanceHandle1, cancellation.Token);
        await instanceManager.StartOpcUaServer(communication2, instanceHandle2, cancellation.Token);

        server1.Should().BeSameAs(server2);
        await server1.Received(1).StartAsync(cancellation.Token);

        await instanceManager.StopOpcUaServer(communication1, instanceHandle1, cancellation.Token);
        await instanceManager.StopOpcUaServer(communication1, instanceHandle2, cancellation.Token);
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
        await instanceManager.StartOpcUaServer(communication1, instanceHandle, cancellation.Token);
        await instanceManager.StartOpcUaServer(communication2, instanceHandle, cancellation.Token);

        server1.Should().NotBeSameAs(server2);
        await server1.Received(1).StartAsync(cancellation.Token);
        await server2.Received(1).StartAsync(cancellation.Token);

        await instanceManager.StopOpcUaServer(communication1, instanceHandle, cancellation.Token);
        await instanceManager.StopOpcUaServer(communication2, instanceHandle, cancellation.Token);
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

public class OpcUaServerInstanceManager_Dispose
{
    [Fact]
    public async Task Stops_a_running_server_before_disposing_it_Async()
    {
        var server = Substitute.For<IOpcUaServer, IDisposable>();
        OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();
        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, new FakeLogger<IOpcUaServer>());
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);
        instanceManager.Dispose();

        Received.InOrder(async () =>
        {
            await server.StopAsync(Arg.Any<CancellationToken>());
            ((IDisposable)server).Dispose();
        });
    }

    [Fact]
    public async Task Does_not_stop_a_server_that_was_never_started_Async()
    {
        var server = Substitute.For<IOpcUaServer, IDisposable>();
        OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();

        instanceManager.GetOrRegisterOpcUaServer(communication, new(), new FakeLogger<IOpcUaServer>());
        instanceManager.Dispose();

        await server.DidNotReceiveWithAnyArgs().StopAsync(TestContext.Current.CancellationToken);
        ((IDisposable)server).Received(1).Dispose();
    }

    [Fact]
    public async Task Disposes_every_server_when_one_of_them_cannot_be_stopped_Async()
    {
        FakeLogger<IOpcUaServer> logger = new();
        var failingServer = Substitute.For<IOpcUaServer, IDisposable>();
        failingServer.StopAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Shutdown failed.")));
        var server = Substitute.For<IOpcUaServer, IDisposable>();
        OpcUaServerDataPortCommunication failingCommunication = new() { Server = "localhost", Port = 55555 };
        OpcUaServerDataPortCommunication communication = new() { Server = "localhost", Port = 55556 };
        OpcUaServerInstanceManager instanceManager = new((c, _) => c.Port == failingCommunication.Port ? failingServer : server);
        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(failingCommunication, instanceHandle, logger);
        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, logger);
        await instanceManager.StartOpcUaServer(failingCommunication, instanceHandle, TestContext.Current.CancellationToken);
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);

        instanceManager.Dispose();

        ((IDisposable)failingServer).Received(1).Dispose();
        ((IDisposable)server).Received(1).Dispose();
        logger.LatestRecord.Message.Should().Match("*localhost:55555*");
    }

    [Fact]
    public void Disposes_every_server_when_one_of_them_cannot_be_disposed()
    {
        FakeLogger<IOpcUaServer> logger = new();
        var failingServer = Substitute.For<IOpcUaServer, IDisposable>();
        ((IDisposable)failingServer).When(disposable => disposable.Dispose())
            .Do(_ => throw new InvalidOperationException("Dispose failed."));
        var server = Substitute.For<IOpcUaServer, IDisposable>();
        OpcUaServerDataPortCommunication failingCommunication = new() { Server = "localhost", Port = 55555 };
        OpcUaServerDataPortCommunication communication = new() { Server = "localhost", Port = 55556 };
        OpcUaServerInstanceManager instanceManager = new((c, _) => c.Port == failingCommunication.Port ? failingServer : server);

        instanceManager.GetOrRegisterOpcUaServer(failingCommunication, new(), logger);
        instanceManager.GetOrRegisterOpcUaServer(communication, new(), logger);

        instanceManager.Dispose();

        ((IDisposable)failingServer).Received(1).Dispose();
        ((IDisposable)server).Received(1).Dispose();
        logger.LatestRecord.Message.Should().Match("*localhost:55555*");
    }

}

public class OpcUaServerInstanceManager_ReleaseOpcUaServerAsync
{
    [Fact]
    public async Task Stops_a_running_server_before_disposing_it_Async()
    {
        var server = Substitute.For<IOpcUaServer, IDisposable>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();
        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, new FakeLogger<IOpcUaServer>());
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);
        await instanceManager.ReleaseOpcUaServerAsync(communication, instanceHandle, TestContext.Current.CancellationToken);

        Received.InOrder(async () =>
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
            ((IDisposable)server).Dispose();
        });
    }

    [Fact]
    public async Task Does_not_stop_a_server_that_was_never_started_Async()
    {
        var server = Substitute.For<IOpcUaServer, IDisposable>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();
        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, new FakeLogger<IOpcUaServer>());
        await instanceManager.ReleaseOpcUaServerAsync(communication, instanceHandle, TestContext.Current.CancellationToken);

        await server.DidNotReceiveWithAnyArgs().StopAsync(TestContext.Current.CancellationToken);
        ((IDisposable)server).Received(1).Dispose();
    }

    [Fact]
    public async Task Stops_the_server_when_the_last_started_data_port_is_released_Async()
    {
        var server = Substitute.For<IOpcUaServer, IDisposable>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();
        var startedInstance = new object();
        var idleInstance = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, startedInstance, new FakeLogger<IOpcUaServer>());
        instanceManager.GetOrRegisterOpcUaServer(communication, idleInstance, new FakeLogger<IOpcUaServer>());
        await instanceManager.StartOpcUaServer(communication, startedInstance, TestContext.Current.CancellationToken);
        await instanceManager.ReleaseOpcUaServerAsync(communication, startedInstance, TestContext.Current.CancellationToken);

        await server.Received(1).StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Keeps_the_server_running_while_another_started_data_port_remains_Async()
    {
        var server = Substitute.For<IOpcUaServer, IDisposable>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();
        var releasedInstance = new object();
        var remainingInstance = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, releasedInstance, new FakeLogger<IOpcUaServer>());
        instanceManager.GetOrRegisterOpcUaServer(communication, remainingInstance, new FakeLogger<IOpcUaServer>());
        await instanceManager.StartOpcUaServer(communication, releasedInstance, TestContext.Current.CancellationToken);
        await instanceManager.StartOpcUaServer(communication, remainingInstance, TestContext.Current.CancellationToken);
        await instanceManager.ReleaseOpcUaServerAsync(communication, releasedInstance, TestContext.Current.CancellationToken);

        await server.DidNotReceiveWithAnyArgs().StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Releases_a_server_that_cannot_be_stopped_Async()
    {
        FakeLogger<IOpcUaServer> logger = new();
        var server = Substitute.For<IOpcUaServer, IDisposable>();
        server.StopAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Shutdown failed.")));
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new() { Server = "localhost", Port = 55555 };
        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, logger);
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);
        await instanceManager.Awaiting(manager => manager.ReleaseOpcUaServerAsync(communication, instanceHandle, TestContext.Current.CancellationToken))
            .Should().NotThrowAsync();

        var register = () => instanceManager.GetOrRegisterOpcUaServer(communication, new(), new FakeLogger<IOpcUaServer>());

        ((IDisposable)server).Received(1).Dispose();
        register.Should().NotThrow();
        logger.LatestRecord.Level.Should().Be(LogLevel.Error);
        logger.LatestRecord.Message.Should().Match("*localhost:55555*");
    }
}

public class OpcUaServerInstanceManager_GetOrRegisterOpcUaServer
{
    [Fact]
    public async Task Names_the_started_server_a_further_data_port_cannot_register_with_Async()
    {
        var server = Substitute.For<IOpcUaServer>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new() { Server = "localhost", Port = 55555 };

        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, new FakeLogger<IOpcUaServer>());
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);

        var register = () => instanceManager.GetOrRegisterOpcUaServer(communication, new(), new FakeLogger<IOpcUaServer>());

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*localhost:55555*already been started*disposed*");
    }

    [Fact]
    public async Task Refuses_a_further_data_port_after_the_server_was_started_and_stopped_Async()
    {
        var server = Substitute.For<IOpcUaServer>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new() { Server = "localhost", Port = 55555 };
        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, new FakeLogger<IOpcUaServer>());
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);
        await instanceManager.StopOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);

        var register = () => instanceManager.GetOrRegisterOpcUaServer(communication, new(), new FakeLogger<IOpcUaServer>());

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*localhost:55555*already been started*disposed*");
    }
}

public class OpcUaServerInstanceManager_StartOpcUaServer
{
    [Fact]
    public async Task Fails_when_the_server_is_not_registered_Async()
    {
        var server = Substitute.For<IOpcUaServer>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new() { Server = "localhost", Port = 55555 };

        await instanceManager.Awaiting(manager => manager.StartOpcUaServer(communication, new(), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*localhost:55555*no longer registered*");

        await server.DidNotReceiveWithAnyArgs().StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Starts_the_server_again_after_a_failed_start_Async()
    {
        var server = Substitute.For<IOpcUaServer>();
        server.StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Server did not initialize correctly.")), _ => Task.CompletedTask);
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();

        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, new FakeLogger<IOpcUaServer>());
        await instanceManager.Awaiting(manager => manager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);

        await server.Received(2).StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_not_stop_a_server_whose_start_failed_Async()
    {
        var server = Substitute.For<IOpcUaServer>();
        server.StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Server did not initialize correctly.")));
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();
        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, new FakeLogger<IOpcUaServer>());
        await instanceManager.Awaiting(manager => manager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await instanceManager.StopOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);

        await server.DidNotReceiveWithAnyArgs().StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Keeps_the_server_running_when_a_data_port_whose_start_failed_is_stopped_Async()
    {
        var server = Substitute.For<IOpcUaServer>();
        server.StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Server did not initialize correctly.")), _ => Task.CompletedTask);
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();
        var failedInstance = new object();
        var startedInstance = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, failedInstance, new FakeLogger<IOpcUaServer>());
        instanceManager.GetOrRegisterOpcUaServer(communication, startedInstance, new FakeLogger<IOpcUaServer>());
        await instanceManager.Awaiting(manager => manager.StartOpcUaServer(communication, failedInstance, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await instanceManager.StartOpcUaServer(communication, startedInstance, TestContext.Current.CancellationToken);
        await instanceManager.StopOpcUaServer(communication, failedInstance, TestContext.Current.CancellationToken);

        await server.DidNotReceiveWithAnyArgs().StopAsync(TestContext.Current.CancellationToken);
    }
}

public class OpcUaServerInstanceManager_StopOpcUaServer
{
    [Fact]
    public async Task Starts_the_server_after_more_stops_than_starts_Async()
    {
        var server = Substitute.For<IOpcUaServer>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();

        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, new FakeLogger<IOpcUaServer>());
        await instanceManager.StopOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);
        await instanceManager.StopOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);
        await instanceManager.StopOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);

        await server.Received(1).StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_not_start_a_server_again_whose_stop_failed_Async()
    {
        var server = Substitute.For<IOpcUaServer>();
        server.StopAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Shutdown failed.")));
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();
        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, new FakeLogger<IOpcUaServer>());
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);
        await instanceManager.Awaiting(manager => manager.StopOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);

        await server.Received(1).StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Stops_the_server_again_after_a_failed_stop_Async()
    {
        var server = Substitute.For<IOpcUaServer>();
        server.StopAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Shutdown failed.")), _ => Task.CompletedTask);
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new();
        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, new FakeLogger<IOpcUaServer>());
        await instanceManager.StartOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);
        await instanceManager.Awaiting(manager => manager.StopOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await instanceManager.StopOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);

        await server.Received(2).StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Logs_a_stop_of_a_server_that_is_not_running_Async()
    {
        FakeLogger<IOpcUaServer> logger = new();
        var server = Substitute.For<IOpcUaServer>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new() { Server = "localhost", Port = 55555 };
        var instanceHandle = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, instanceHandle, logger);
        await instanceManager.StopOpcUaServer(communication, instanceHandle, TestContext.Current.CancellationToken);

        await server.DidNotReceiveWithAnyArgs().StopAsync(TestContext.Current.CancellationToken);
        logger.LatestRecord.Level.Should().Be(LogLevel.Debug);
        logger.LatestRecord.Message.Should().Match("*localhost:55555*is not running*");
    }

    [Fact]
    public async Task Warns_when_a_data_port_that_did_not_start_the_server_stops_it_Async()
    {
        FakeLogger<IOpcUaServer> logger = new();
        var server = Substitute.For<IOpcUaServer>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortCommunication communication = new() { Server = "localhost", Port = 55555 };
        var startedInstance = new object();
        var idleInstance = new object();

        instanceManager.GetOrRegisterOpcUaServer(communication, startedInstance, logger);
        instanceManager.GetOrRegisterOpcUaServer(communication, idleInstance, logger);
        await instanceManager.StartOpcUaServer(communication, startedInstance, TestContext.Current.CancellationToken);
        await instanceManager.StopOpcUaServer(communication, idleInstance, TestContext.Current.CancellationToken);

        await server.DidNotReceiveWithAnyArgs().StopAsync(TestContext.Current.CancellationToken);
        logger.LatestRecord.Level.Should().Be(LogLevel.Warning);
        logger.LatestRecord.Message.Should().Match("*localhost:55555*not one of those keeping it running*");
    }
}
