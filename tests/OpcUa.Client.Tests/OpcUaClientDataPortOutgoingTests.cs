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
