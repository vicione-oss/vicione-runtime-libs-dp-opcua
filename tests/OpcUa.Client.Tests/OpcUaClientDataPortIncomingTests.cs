using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ViciOne.ManagedEngine.Runtime;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaClientDataPortIncoming_
{
    private static OpcUaClientDataPortCommunication CreateCommunication(IReadOnlyCollection<Node>? nodes = default, Action<OpcUaClientDataPortProperties>? configure = default)
    {
        OpcUaClientDataPortCommunication communication = new()
        {
            Nodes = nodes ?? [],
        };
        OpcUaClientDataPortProperties properties = new(communication)
        {
            Endpoint = "opc.tcp://localhost:55555/opc/ua",
            ApplicationName = "test",
            Server = "localhost",
            Password = "password",
            User = "user",
            UserAuthenticationType = UserAuthenticationType.Anonymous,
            ApplicationCertificatesStoreType = Opc.Ua.CertificateStoreType.Directory,
            ApplicationCertificatesStorePath = Path.Combine("%CommonApplicationData%", "opcua", "cert-stores"),
        };
        if (configure is not null)
            configure(properties);
        return communication;
    }

    [Fact]
    public async Task DependencyInjectionProviderFactory_can_create_instance_Async()
    {
        await using var providerFactory = new ConstructorProviderFactory();

        var instance = providerFactory.CreateExternalIncoming(
            new OpcUaClientDataPortCommunication(),
            new(),
            Substitute.For<ILoggerFactory>(),
            Substitute.For<INameResolver>(),
            AssemblyLoadContext.Default);

        _ = instance.Should().NotBeNull().And.BeOfType<OpcUaClientDataPortIncoming>();
    }

    [Fact]
    public async Task Connects_Async()
    {
        var communication = CreateCommunication();

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        var logger = Substitute.For<ILogger<IOpcUaClient>>();
        using CancellationTokenSource cancellation = new();
        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.ConnectAsync(cancellation.Token);

        await instanceManager.Received(1).GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token);
    }

    [Fact]
    public async Task Subscribes_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    AffectedChannels =
                    [
                        "channel",
                    ],
                    Name = "test",
                }
            ]);

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        var logger = Substitute.For<ILogger<IOpcUaClient>>();
        using CancellationTokenSource cancellation = new();
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(
        [
            new()
            {
                DisplayName = "test",
                NodeId = new("ns=2;s=test"),
            }
        ]);
        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test", Arg.Any<Action<object?, DateTime>>(), cancellation.Token);
    }

    [Fact]
    public async Task Subscribes_child_node_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("bdf70063-8bbc-4616-b84d-520ba4676e16"),
                    DesignId = OpcUaClientNodeDesignId.Folder,
                    Name = "parent",
                },
                new()
                {
                    Id = Guid.Parse("81c3bbad-6326-4122-b195-10aab9d75949"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    ParentId = Guid.Parse("bdf70063-8bbc-4616-b84d-520ba4676e16"),
                    AffectedChannels =
                    [
                        "channel",
                    ],
                    Name = "test",
                },
            ]);

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        var logger = Substitute.For<ILogger<IOpcUaClient>>();
        using CancellationTokenSource cancellation = new();
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(
        [
            new()
            {
                DisplayName = "parent",
                NodeId = new("ns=2;s=parent"),
                Children =
                [
                    new()
                    {
                        DisplayName = "test",
                        NodeId = new("ns=2;s=test"),
                    },
                ],
            },
        ]);
        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test", Arg.Any<Action<object?, DateTime>>(), cancellation.Token);
    }

    [Fact]
    public async Task Subscribes_on_retry_after_a_failed_subscribe_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    AffectedChannels =
                    [
                        "channel",
                    ],
                    Name = "test",
                }
            ]);

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        var logger = Substitute.For<ILogger<IOpcUaClient>>();
        using CancellationTokenSource cancellation = new();
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(
        [
            new()
            {
                DisplayName = "test",
                NodeId = new("ns=2;s=test"),
            }
        ]);

        var subscribeCalls = 0;
        opcUaClient.SubscribeAsync(Arg.Any<Opc.Ua.NodeId>(), Arg.Any<Action<object?, DateTime>>(), cancellation.Token)
            .Returns(_ => ++subscribeCalls == 1
                ? Task.FromException(new InvalidOperationException("Subscribe failed."))
                : Task.CompletedTask);

        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();
        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaClient.Received(2).SubscribeAsync("ns=2;s=test", Arg.Any<Action<object?, DateTime>>(), cancellation.Token);
    }

    [Fact]
    public async Task Subscribes_every_node_once_when_a_node_is_missing_on_the_first_connect_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("2c4e3e4e-0a1a-4a2f-9a35-0b7de6b0f1a1"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    AffectedChannels =
                    [
                        "channel1",
                    ],
                    Name = "test1",
                },
                new()
                {
                    Id = Guid.Parse("5d6f2b18-9a53-4a0e-8f7c-2f0b1a3d4c5e"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    AffectedChannels =
                    [
                        "channel2",
                    ],
                    Name = "test2",
                },
            ]);

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        var logger = Substitute.For<ILogger<IOpcUaClient>>();
        using CancellationTokenSource cancellation = new();

        OpcUaNode firstNode = new()
        {
            DisplayName = "test1",
            NodeId = new("ns=2;s=test1"),
        };
        OpcUaNode secondNode = new()
        {
            DisplayName = "test2",
            NodeId = new("ns=2;s=test2"),
        };

        IReadOnlyCollection<OpcUaNode> incompleteNodes = [firstNode,];
        IReadOnlyCollection<OpcUaNode> completeNodes = [firstNode, secondNode,];
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(incompleteNodes, completeNodes);

        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();
        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test1", Arg.Any<Action<object?, DateTime>>(), cancellation.Token);
        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test2", Arg.Any<Action<object?, DateTime>>(), cancellation.Token);
    }

    [Fact]
    public async Task Releases_the_client_when_connecting_fails_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    AffectedChannels =
                    [
                        "channel",
                    ],
                    Name = "test",
                }
            ]);

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        var logger = Substitute.For<ILogger<IOpcUaClient>>();
        using CancellationTokenSource cancellation = new();
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(
        [
            new()
            {
                DisplayName = "other",
                NodeId = new("ns=2;s=other"),
            }
        ]);

        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();

        await instanceManager.Received(1).ReleaseOpcUaClientAsync(communication, opcUaDataport, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disconnects_Async()
    {
        var communication = CreateCommunication();

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        var logger = Substitute.For<ILogger<IOpcUaClient>>();
        using CancellationTokenSource cancellation = new();
        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.ConnectAsync(cancellation.Token);
        await opcUaDataport.DisconnectAsync(cancellation.Token);

        await instanceManager.Received(1).ReleaseOpcUaClientAsync(communication, opcUaDataport, cancellation.Token);
    }

    [Fact]
    public async Task Receives_values_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("bdf70063-8bbc-4616-b84d-520ba4676e16"),
                    DesignId = OpcUaClientNodeDesignId.Folder,
                    Name = "parent",
                },
                new()
                {
                    Id = Guid.Parse("81c3bbad-6326-4122-b195-10aab9d75949"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    ParentId = Guid.Parse("bdf70063-8bbc-4616-b84d-520ba4676e16"),
                    AffectedChannels =
                    [
                        "channel",
                    ],
                    Name = "test",
                },
            ]);

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        var logger = Substitute.For<ILogger<IOpcUaClient>>();
        using CancellationTokenSource cancellation = new();
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(
        [
            new()
            {
                DisplayName = "parent",
                NodeId = new("ns=2;s=parent"),
                Children =
                [
                    new()
                    {
                        DisplayName = "test",
                        NodeId = new("ns=2;s=test"),
                    },
                ],
            },
        ]);

        Action<object?, DateTime>? subscribeAction = null;
        opcUaClient.When(c => c.SubscribeAsync(Arg.Any<Opc.Ua.NodeId>(), Arg.Any<Action<object?, DateTime>>(), cancellation.Token))
            .Do(c => subscribeAction = c[1] as Action<object?, DateTime>);
        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        var receivedValues = false;
        opcUaDataport.Received += (values) => { receivedValues = values.Any(v => v.Channel == "channel" && (v.Value?.Equals(1.4) ?? false)); };

        await opcUaDataport.ConnectAsync(cancellation.Token);

        subscribeAction.Should().NotBeNull();
        subscribeAction(1.4, new DateTime(2023, 10, 12, 15, 27, 30));

        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test", Arg.Any<Action<object?, DateTime>>(), cancellation.Token);
        receivedValues.Should().BeTrue();
    }

    [Fact]
    public async Task Warns_if_UA_node_is_missing_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("bdf70063-8bbc-4616-b84d-520ba4676e16"),
                    DesignId = OpcUaClientNodeDesignId.Folder,
                    Name = "parent",
                },
                new()
                {
                    Id = Guid.Parse("81c3bbad-6326-4122-b195-10aab9d75949"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    ParentId = Guid.Parse("bdf70063-8bbc-4616-b84d-520ba4676e16"),
                    AffectedChannels =
                    [
                        "channel",
                    ],
                    Name = "test",
                },
            ]);

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        NullLogger<IOpcUaClient> logger = new();
        using CancellationTokenSource cancellation = new();
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(
        [
            new()
            {
                DisplayName = "parent",
                NodeId = new("ns=2;s=parent"),
                Children =
                [
                    new()
                    {
                        DisplayName = "notTest",
                        NodeId = new("ns=2;s=test"),
                    },
                ],
            },
        ]);

        Action<object?, DateTime>? subscribeAction = null;
        opcUaClient.When(c => c.SubscribeAsync(Arg.Any<Opc.Ua.NodeId>(), Arg.Any<Action<object?, DateTime>>(), cancellation.Token))
            .Do(c => subscribeAction = c[1] as Action<object?, DateTime>);
        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        var receivedValues = false;
        opcUaDataport.Received += (values) => { receivedValues = values.Any(v => v.Channel == "channel" && (v.Value?.Equals(1.4) ?? false)); };

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();
        subscribeAction.Should().BeNull();
        receivedValues.Should().BeFalse();
    }
}
