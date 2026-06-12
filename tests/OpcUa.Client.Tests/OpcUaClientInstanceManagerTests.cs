using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
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
        using OpcUaClientInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaClient>());
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

        using OpcUaClientInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaClient>());
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

        using OpcUaClientInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaClient>());
        var instanceHandle = new object();
        using CancellationTokenSource cancellation = new();

        var client1 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication1, instanceHandle, _logger, cancellation.Token);
        var client2 = await instanceManager.GetOrRegisterOpcUaClientAsync(communication2, instanceHandle, _logger, cancellation.Token);

        client1.Should().NotBeSameAs(client2);
        await client1.Received(1).ConnectAsync(Arg.Is(cancellation.Token));
        await client2.Received(1).ConnectAsync(Arg.Is(cancellation.Token));
    }

    [Fact]
    public async Task Releases_client()
    {
        var communication = CreateCommunication();

        using OpcUaClientInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaClient>());
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
    public async Task Only_releases_correct_client()
    {
        var communication1 = CreateCommunication();
        var communication2 = CreateCommunication(properties =>
        {
            properties.Password = "password1";
            properties.User = "admin1";
        });

        using OpcUaClientInstanceManager instanceManager = new((_, _) => Substitute.For<IOpcUaClient>());
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
