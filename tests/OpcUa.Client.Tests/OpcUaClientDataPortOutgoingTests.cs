using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ViciOne.ManagedEngine.ExternalCommunication;
using ViciOne.ManagedEngine.Runtime;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaClientDataPortOutgoing_
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
            UserAuthenticationType = UserAuthenticationType.Basic,
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

        var instance = providerFactory.CreateExternalOutgoing(
            new OpcUaClientDataPortCommunication(),
            new(),
            Substitute.For<ILoggerFactory>(),
            Substitute.For<INameResolver>(),
            AssemblyLoadContext.Default);

        _ = instance.Should().NotBeNull().And.BeOfType<OpcUaClientDataPortOutgoing>();
    }

    [Fact]
    public async Task Connects_Async()
    {
        var communication = CreateCommunication();

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        var logger = Substitute.For<ILogger<IOpcUaClient>>();
        using CancellationTokenSource cancellation = new();
        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.ConnectAsync(cancellation.Token);

        await instanceManager.Received(1).GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token);
    }

    [Fact]
    public async Task Disconnects_Async()
    {
        var communication = CreateCommunication();

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        var logger = Substitute.For<ILogger<IOpcUaClient>>();
        using CancellationTokenSource cancellation = new();
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, Arg.Any<object>(), logger, cancellation.Token).Returns(opcUaClient);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);

        await opcUaDataport.ConnectAsync(cancellation.Token);
        await opcUaDataport.DisconnectAsync(cancellation.Token);

        await instanceManager.Received(1).ReleaseOpcUaClientAsync(communication, opcUaDataport, cancellation.Token);
    }

    [Fact]
    public async Task Sends_value_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("a7bc6fae-99bc-4a18-9d4b-9ea4630f4a61"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
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
                ReferenceDescription = new()
                {
                    DisplayName = "test",
                    NodeId = new("ns=2;s=test"),
                }
            },
        ]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);
        await opcUaDataport.ConnectAsync(cancellation.Token);

        var value = new ExternalValue()
        {
            Channel = "channel",
            Value = 2,
        };

        await opcUaDataport.SendAsync(0, [value], cancellation.Token);

        await opcUaClient.Received(1).WriteValuesAsync(Arg.Do<IEnumerable<(Opc.Ua.NodeId, object?)>>(l =>
        {
            var (nodeId, value) = l.Single();
            nodeId.Should().Be(new Opc.Ua.NodeId("ns=2;s=test"));
            value.Should().Be(2);
        }), cancellation.Token);
    }

    [Fact]
    public async Task Sends_child_node_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("99ecc27b-5a7c-4776-9528-4fd573118074"),
                    DesignId = OpcUaClientNodeDesignId.Folder,
                    Name = "root",
                },
                new()
                {
                    Id = Guid.Parse("a7bc6fae-99bc-4a18-9d4b-9ea4630f4a61"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    AffectedChannels =
                    [
                        "channel",
                    ],
                    Name = "test",
                    ParentId = Guid.Parse("99ecc27b-5a7c-4776-9528-4fd573118074"),
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
                ReferenceDescription = new()
                {
                    DisplayName = "root",
                    NodeId = new("ns=2;s=parent"),
                },
                Children =
                [
                    new()
                    {
                        ReferenceDescription = new()
                        {
                            DisplayName = "test",
                            NodeId = new("ns=2;s=parent/test"),
                        }
                    },
                ],
            }
        ]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);
        await opcUaDataport.ConnectAsync(cancellation.Token);

        var value = new ExternalValue()
        {
            Channel = "channel",
            Value = 2,
        };

        await opcUaDataport.SendAsync(0, [value], cancellation.Token);

        await opcUaClient.Received(1).WriteValuesAsync(Arg.Do<IEnumerable<(Opc.Ua.NodeId, object?)>>(l =>
        {
            var (nodeId, value) = l.Single();
            nodeId.Should().Be(new Opc.Ua.NodeId("ns=2;s=parent/test"));
            value.Should().Be(2);
        }), cancellation.Token);
    }

    [Fact]
    public async Task Maps_all_channels_on_retry_after_a_failed_connect_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("a7bc6fae-99bc-4a18-9d4b-9ea4630f4a61"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    AffectedChannels =
                    [
                        "channel1",
                    ],
                    Name = "test1",
                },
                new()
                {
                    Id = Guid.Parse("cbf0f7d6-5e6a-4b62-9a19-1b2fbb0ac0e8"),
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
            ReferenceDescription = new()
            {
                DisplayName = "test1",
                NodeId = new("ns=2;s=test1"),
            },
        };
        OpcUaNode secondNode = new()
        {
            ReferenceDescription = new()
            {
                DisplayName = "test2",
                NodeId = new("ns=2;s=test2"),
            },
        };

        IReadOnlyCollection<OpcUaNode> incompleteNodes = [firstNode,];
        IReadOnlyCollection<OpcUaNode> completeNodes = [firstNode, secondNode,];
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(incompleteNodes, completeNodes);

        List<(Opc.Ua.NodeId NodeId, object? Value)> writtenValues = [];
        opcUaClient.When(c => c.WriteValuesAsync(Arg.Any<IEnumerable<(Opc.Ua.NodeId, object?)>>(), cancellation.Token))
            .Do(c => writtenValues = [.. (IEnumerable<(Opc.Ua.NodeId, object?)>)c[0]!]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();
        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaDataport.SendAsync(0, [new() { Channel = "channel1", Value = 1, }, new() { Channel = "channel2", Value = 2, },], cancellation.Token);

        writtenValues.Should().BeEquivalentTo(
        [
            (new Opc.Ua.NodeId("ns=2;s=test1"), (object?)1),
            (new Opc.Ua.NodeId("ns=2;s=test2"), (object?)2),
        ]);
    }

    [Fact]
    public async Task Releases_the_client_when_connecting_fails_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("a7bc6fae-99bc-4a18-9d4b-9ea4630f4a61"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
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
                ReferenceDescription = new()
                {
                    DisplayName = "other",
                    NodeId = new("ns=2;s=other"),
                },
            },
        ]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();

        await instanceManager.Received(1).ReleaseOpcUaClientAsync(communication, opcUaDataport, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Warns_if_UA_node_doesnt_exist_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("99ecc27b-5a7c-4776-9528-4fd573118074"),
                    Name = "root",
                },
                new()
                {
                    Id = Guid.Parse("a7bc6fae-99bc-4a18-9d4b-9ea4630f4a61"),
                    TransferredChannels =
                    [
                        "channel",
                    ],
                    Name = "test",
                    ParentId = Guid.Parse("99ecc27b-5a7c-4776-9528-4fd573118074"),
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
                ReferenceDescription = new()
                {
                    DisplayName = "root",
                    NodeId = new("ns=2;s=parent"),
                },
                Children =
                [
                    new()
                    {
                        ReferenceDescription = new()
                        {
                            DisplayName = "notTest",
                            NodeId = new("ns=2;s=parent/test"),
                        }
                    },
                ],
            }
        ]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();
    }
}
