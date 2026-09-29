using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaClientInstanceManager_
{
    private readonly ILogger<IOpcUaClient> _logger = Substitute.For<ILogger<IOpcUaClient>>();

    private static OpcUaClientDataPortCommunication CreateCommunication(Action<OpcUaClientDataPortProperties>? configure = default)
    {
        OpcUaClientDataPortCommunication communication = new();
        OpcUaClientDataPortProperties properties = new(communication)
        {
            ApplicationName = "test",
            Endpoint = "opc.tcp://test",
            Server = "test",
            UserAuthenticationType = UserAuthenticationType.Anonymous,
            ApplicationCertificatesStoreType = Opc.Ua.CertificateStoreType.Directory,
            ApplicationCertificatesStorePath = Path.Combine("%CommonApplicationData%", "opcua", "cert-stores"),
        };
        if (configure is not null)
            configure(properties);
        return communication;
    }

    [Fact]
    public async Task Creates_new_client()
    {
        await using OpcUaClientInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaClient>());
        using CancellationTokenSource cancellation = new();

        var client = await instanceManager.GetOrRegisterOpcUaClientAsync(new(), new(), _logger, cancellation.Token);

        client.Should().NotBeNull();
        await client.Received(1).ConnectAsync(Arg.Is(cancellation.Token));
    }

    [Fact]
    public async Task Returns_existing_client()
    {
        var communication1 = CreateCommunication();
        var communication2 = CreateCommunication();

        await using OpcUaClientInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaClient>());
        using CancellationTokenSource cancellation = new();

        var client1 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication1, new(), _logger, cancellation.Token);
        var client2 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication2, new(), _logger, cancellation.Token);

        client1.Should().BeSameAs(client2);
        await client1.Received(1).ConnectAsync(Arg.Is(cancellation.Token));
    }

    [Fact]
    public async Task Creates_new_client_for_different_communication()
    {
        var communication1 = CreateCommunication(properties => properties.Endpoint = "opc.tcp://test1");
        var communication2 = CreateCommunication(properties => properties.Endpoint = "opc.tcp://test2");

        await using OpcUaClientInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaClient>());
        var instanceHandle = new object();
        using CancellationTokenSource cancellation = new();

        var client1 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication1, instanceHandle, _logger, cancellation.Token);
        var client2 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication2, instanceHandle, _logger, cancellation.Token);

        client1.Should().NotBeSameAs(client2);
        await client1.Received(1).ConnectAsync(Arg.Is(cancellation.Token));
        await client2.Received(1).ConnectAsync(Arg.Is(cancellation.Token));
    }

    [Fact]
    public async Task Does_not_register_client_that_fails_to_connect()
    {
        var communication = CreateCommunication();

        var failingClient = Substitute.For<IOpcUaClient>();
        failingClient.ConnectAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Connect failed."));
        var workingClient = Substitute.For<IOpcUaClient>();
        var clients = new Queue<IOpcUaClient>([failingClient, workingClient,]);

        await using OpcUaClientInstanceManager instanceManager = new((_, _) => clients.Dequeue());
        var instanceHandle = new object();
        using CancellationTokenSource cancellation = new();

        await instanceManager.Awaiting(x => x.GetOrRegisterOpcUaClientAsync(communication, instanceHandle, _logger, cancellation.Token))
            .Should().ThrowAsync<InvalidOperationException>();

        var client = await instanceManager.GetOrRegisterOpcUaClientAsync(communication, instanceHandle, _logger, cancellation.Token);

        client.Should().BeSameAs(workingClient);
        await workingClient.Received(1).ConnectAsync(Arg.Is(cancellation.Token));
    }

    [Fact]
    public async Task Disposes_client_that_fails_to_connect()
    {
        var communication = CreateCommunication();

        var failingClient = Substitute.For<IOpcUaClient, IAsyncDisposable>();
        failingClient.ConnectAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Connect failed."));

        await using OpcUaClientInstanceManager instanceManager = new((_, _) => failingClient);
        using CancellationTokenSource cancellation = new();

        await instanceManager.Awaiting(x => x.GetOrRegisterOpcUaClientAsync(communication, new(), _logger, cancellation.Token))
            .Should().ThrowAsync<InvalidOperationException>();

        await ((IAsyncDisposable)failingClient).Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Disconnects_clients_on_dispose()
    {
        var communication = CreateCommunication();

        var client = Substitute.For<IOpcUaClient>();
        OpcUaClientInstanceManager instanceManager = new((_, _) => client);
        using CancellationTokenSource cancellation = new();

        _ = await instanceManager.GetOrRegisterOpcUaClientAsync(communication, new(), _logger, cancellation.Token);

        await instanceManager.DisposeAsync();

        await client.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disposes_every_client_when_one_fails_to_disconnect_Async()
    {
        var failingClient = Substitute.For<IOpcUaClient, IAsyncDisposable>();
        failingClient.DisconnectAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Disconnect failed."));
        var workingClient = Substitute.For<IOpcUaClient, IAsyncDisposable>();
        var clients = new Queue<IOpcUaClient>([failingClient, workingClient,]);
        OpcUaClientInstanceManager instanceManager = new((_, _) => clients.Dequeue());

        _ = await instanceManager.GetOrRegisterOpcUaClientAsync(CreateCommunication(properties => properties.Endpoint = "opc.tcp://test1"), new(), _logger, TestContext.Current.CancellationToken);
        _ = await instanceManager.GetOrRegisterOpcUaClientAsync(CreateCommunication(properties => properties.Endpoint = "opc.tcp://test2"), new(), _logger, TestContext.Current.CancellationToken);

        var dispose = instanceManager.DisposeAsync().AsTask();

        await dispose.Awaiting(d => d).Should().ThrowAsync<InvalidOperationException>();
        await ((IAsyncDisposable)failingClient).Received(1).DisposeAsync();
        await ((IAsyncDisposable)workingClient).Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Releases_client()
    {
        var communication = CreateCommunication();

        await using OpcUaClientInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaClient>());
        var instanceHandle = new object();
        using CancellationTokenSource cancellation = new();

        var client1 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication, instanceHandle, _logger, TestContext.Current.CancellationToken);

        await instanceManager.ReleaseOpcUaClientAsync(communication, instanceHandle, cancellation.Token);

        var client2 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication, instanceHandle, _logger, TestContext.Current.CancellationToken);

        client1.Should().NotBeSameAs(client2);
        await client1.Received(1).DisconnectAsync(Arg.Is(cancellation.Token));
        await client2.DidNotReceiveWithAnyArgs().DisconnectAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Registers_other_client_while_a_released_client_is_still_disconnecting_Async()
    {
        var communication1 = CreateCommunication(properties => properties.Endpoint = "opc.tcp://test1");
        var communication2 = CreateCommunication(properties => properties.Endpoint = "opc.tcp://test2");

        TaskCompletionSource disconnect = new();
        var slowClient = Substitute.For<IOpcUaClient>();
        slowClient.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(disconnect.Task);
        var clients = new Queue<IOpcUaClient>([slowClient, Substitute.For<IOpcUaClient>(),]);

        await using OpcUaClientInstanceManager instanceManager = new((_, _) => clients.Dequeue());
        var instanceHandle = new object();

        _ = await instanceManager.GetOrRegisterOpcUaClientAsync(communication1, instanceHandle, _logger, TestContext.Current.CancellationToken);
        var release = instanceManager.ReleaseOpcUaClientAsync(communication1, instanceHandle, TestContext.Current.CancellationToken);

        var register = instanceManager.GetOrRegisterOpcUaClientAsync(communication2, instanceHandle, _logger, TestContext.Current.CancellationToken);

        try
        {
            await register.Awaiting(r => r.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Should().NotThrowAsync();
        }
        finally
        {
            disconnect.SetResult();
            await release;
            await register;
        }
    }

    [Fact]
    public async Task Connects_a_client_for_the_same_endpoint_only_after_the_released_one_disconnected_Async()
    {
        var communication = CreateCommunication();

        TaskCompletionSource disconnect = new();
        var slowClient = Substitute.For<IOpcUaClient>();
        slowClient.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(disconnect.Task);
        var nextClient = Substitute.For<IOpcUaClient>();
        var disconnectedBeforeConnect = false;
        nextClient.ConnectAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            disconnectedBeforeConnect = disconnect.Task.IsCompleted;
            return Task.CompletedTask;
        });
        var clients = new Queue<IOpcUaClient>([slowClient, nextClient,]);

        await using OpcUaClientInstanceManager instanceManager = new((_, _) => clients.Dequeue());
        var instanceHandle = new object();

        _ = await instanceManager.GetOrRegisterOpcUaClientAsync(communication, instanceHandle, _logger, TestContext.Current.CancellationToken);
        var release = instanceManager.ReleaseOpcUaClientAsync(communication, instanceHandle, TestContext.Current.CancellationToken);
        var register = instanceManager.GetOrRegisterOpcUaClientAsync(communication, instanceHandle, _logger, TestContext.Current.CancellationToken);

        disconnect.SetResult();
        await release;
        var client = await register;

        client.Should().BeSameAs(nextClient);
        disconnectedBeforeConnect.Should().BeTrue();
    }

    [Fact]
    public async Task Only_releases_correct_client()
    {
        var communication1 = CreateCommunication();
        var communication2 = CreateCommunication(properties =>
        {
            properties.Password = "password1";
            properties.User = "admin1";
        });

        await using OpcUaClientInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaClient>());
        var instanceHandle = new object();
        using CancellationTokenSource cancellation = new();

        var client1 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication1, instanceHandle, _logger, TestContext.Current.CancellationToken);
        var client2 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication2, instanceHandle, _logger, TestContext.Current.CancellationToken);

        await instanceManager.ReleaseOpcUaClientAsync(communication1, instanceHandle, cancellation.Token);

        var client3 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication1, instanceHandle, _logger, TestContext.Current.CancellationToken);
        var client4 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication2, instanceHandle, _logger, TestContext.Current.CancellationToken);

        client1.Should().NotBeNull();
        client2.Should().NotBeNull();
        client3.Should().NotBeNull();
        client4.Should().NotBeNull();
        client1.Should().NotBeSameAs(client3);
        client2.Should().BeSameAs(client4);
        await client1.Received(1).DisconnectAsync(Arg.Is(cancellation.Token));
        await client2.DidNotReceiveWithAnyArgs().DisconnectAsync(TestContext.Current.CancellationToken);
        await client3.DidNotReceiveWithAnyArgs().DisconnectAsync(TestContext.Current.CancellationToken);
        await client4.DidNotReceiveWithAnyArgs().DisconnectAsync(TestContext.Current.CancellationToken);
    }
}

public class OpcUaClientInstanceManager_DisposeAsync
{
    private readonly ILogger<IOpcUaClient> _logger = Substitute.For<ILogger<IOpcUaClient>>();

    [Fact]
    public async Task Disposes_every_client_once_when_disposed_twice_Async()
    {
        var client = Substitute.For<IOpcUaClient, IAsyncDisposable>();
        OpcUaClientInstanceManager instanceManager = new((_, _) => client);
        OpcUaClientDataPortCommunication communication = new();

        _ = await instanceManager.GetOrRegisterOpcUaClientAsync(communication, new(), _logger, TestContext.Current.CancellationToken);
        await instanceManager.DisposeAsync();
        await instanceManager.DisposeAsync();

        await ((IAsyncDisposable)client).Received(1).DisposeAsync();
    }
}

public class OpcUaClientInstanceManager_GetOrRegisterOpcUaClientAsync
{
    private readonly ILogger<IOpcUaClient> _logger = Substitute.For<ILogger<IOpcUaClient>>();

    [Fact]
    public async Task Refuses_a_data_port_after_the_manager_was_disposed_Async()
    {
        var client = Substitute.For<IOpcUaClient, IAsyncDisposable>();
        OpcUaClientInstanceManager instanceManager = new((_, _) => client);
        OpcUaClientDataPortCommunication communication = new();

        await instanceManager.DisposeAsync();

        await instanceManager.Awaiting(manager => manager.GetOrRegisterOpcUaClientAsync(communication, new(), _logger, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ObjectDisposedException>()
            .WithMessage("*OpcUaClientInstanceManager*");
    }
}

public class OpcUaClientInstanceManager_ReleaseOpcUaClientAsync
{
    private readonly ILogger<IOpcUaClient> _logger = Substitute.For<ILogger<IOpcUaClient>>();

    [Fact]
    public async Task Ignores_a_release_after_the_manager_was_disposed_Async()
    {
        var client = Substitute.For<IOpcUaClient, IAsyncDisposable>();
        OpcUaClientInstanceManager instanceManager = new((_, _) => client);
        OpcUaClientDataPortCommunication communication = new();
        var instanceHandle = new object();

        _ = await instanceManager.GetOrRegisterOpcUaClientAsync(communication, instanceHandle, _logger, TestContext.Current.CancellationToken);
        await instanceManager.DisposeAsync();
        await instanceManager.ReleaseOpcUaClientAsync(communication, instanceHandle, TestContext.Current.CancellationToken);

        await ((IAsyncDisposable)client).Received(1).DisposeAsync();
    }
}
