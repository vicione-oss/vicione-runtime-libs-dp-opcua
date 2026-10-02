using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using ViciOne.ManagedEngine.ExternalCommunication;
using ViciOne.ManagedEngine.Runtime;
using Xunit;

namespace ViciOne.Suite.DataPort;

internal static class OutgoingClientSetup
{
    internal static OpcUaClientDataPortCommunication CreateCommunication(IReadOnlyCollection<Node>? nodes = default, Action<OpcUaClientDataPortProperties>? configure = default)
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

    internal static OpcUaClientDataPortCommunication CreateCommunicationWithEnvelopeChildren()
    {
        var dataPointId = Guid.Parse("81c3bbad-6326-4122-b195-10aab9d75949");

        return CreateCommunication(
            [
                new()
                {
                    Id = dataPointId,
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    Name = "test",
                    ValueType = typeof(double),
                    AffectedChannels = ["value"],
                    TransferredChannels = ["value", "status", "sent"],
                },
                new()
                {
                    Id = Guid.Parse("2f4f9d5c-7c2b-4a52-9a8e-4f3d5a2b1c60"),
                    ParentId = dataPointId,
                    DesignId = OpcUaClientNodeDesignId.StatusCode,
                    Name = "quality",
                    ValueType = typeof(uint),
                    AffectedChannels = ["status"],
                },
                new()
                {
                    Id = Guid.Parse("3a6b0e7d-8d3c-4b63-ab9f-50416b3c2d71"),
                    ParentId = dataPointId,
                    DesignId = OpcUaClientNodeDesignId.SourceTimestamp,
                    Name = "when",
                    ValueType = typeof(DateTime),
                    AffectedChannels = ["sent"],
                },
            ]);
    }

    internal static (OpcUaClientDataPortOutgoing Port, List<OpcUaWrite> Written, CancellationTokenSource Cancellation) CreatePortWithEnvelopeChildren(ILogger<IOpcUaClient>? logger = null)
    {
        var communication = CreateCommunicationWithEnvelopeChildren();
        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        logger ??= Substitute.For<ILogger<IOpcUaClient>>();
        CancellationTokenSource cancellation = new();
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(
        [
            new()
            {
                DisplayName = "test",
                NodeId = new("ns=2;s=test"),
            },
        ]);

        List<OpcUaWrite> written = [];
        opcUaClient.When(c => c.WriteValuesAsync(Arg.Any<IEnumerable<OpcUaWrite>>(), cancellation.Token))
            .Do(c => written.AddRange((IEnumerable<OpcUaWrite>)c[0]));

        var port = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, port, logger, cancellation.Token).Returns(opcUaClient);

        return (port, written, cancellation);
    }

    internal static ExternalValue Value(string channel, object value)
        => new() { Channel = channel, Timestamp = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), Value = value, };
}

public class OpcUaClientDataPortOutgoing_
{
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
        var communication = OutgoingClientSetup.CreateCommunication();

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
        var communication = OutgoingClientSetup.CreateCommunication();

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
        var communication = OutgoingClientSetup.CreateCommunication(
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
                DisplayName = "test",
                NodeId = new("ns=2;s=test"),
            },
        ]);

        List<OpcUaWrite> writtenValues = [];
        opcUaClient.When(c => c.WriteValuesAsync(Arg.Any<IEnumerable<OpcUaWrite>>(), cancellation.Token))
            .Do(c => writtenValues = [.. (IEnumerable<OpcUaWrite>)c[0]!]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);
        await opcUaDataport.ConnectAsync(cancellation.Token);

        var value = new ExternalValue()
        {
            Channel = "channel",
            Value = 2,
        };

        await opcUaDataport.SendAsync(0, [value], cancellation.Token);

        writtenValues.Should().BeEquivalentTo([new OpcUaWrite(new Opc.Ua.NodeId("ns=2;s=test"), 2, Opc.Ua.StatusCodes.Good, DateTime.MinValue),]);
    }

    [Fact]
    public async Task Sends_child_node_Async()
    {
        var communication = OutgoingClientSetup.CreateCommunication(
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
                DisplayName = "root",
                NodeId = new("ns=2;s=parent"),
                Children =
                [
                    new()
                    {
                        DisplayName = "test",
                        NodeId = new("ns=2;s=parent/test"),
                    },
                ],
            }
        ]);

        List<OpcUaWrite> writtenValues = [];
        opcUaClient.When(c => c.WriteValuesAsync(Arg.Any<IEnumerable<OpcUaWrite>>(), cancellation.Token))
            .Do(c => writtenValues = [.. (IEnumerable<OpcUaWrite>)c[0]!]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);
        await opcUaDataport.ConnectAsync(cancellation.Token);

        var value = new ExternalValue()
        {
            Channel = "channel",
            Value = 2,
        };

        await opcUaDataport.SendAsync(0, [value], cancellation.Token);

        writtenValues.Should().BeEquivalentTo([new OpcUaWrite(new Opc.Ua.NodeId("ns=2;s=parent/test"), 2, Opc.Ua.StatusCodes.Good, DateTime.MinValue),]);
    }

    [Fact]
    public async Task Sends_value_after_a_reconnect_Async()
    {
        var communication = OutgoingClientSetup.CreateCommunication(
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
                DisplayName = "test",
                NodeId = new("ns=2;s=test"),
            },
        ]);

        List<OpcUaWrite> writtenValues = [];
        opcUaClient.When(c => c.WriteValuesAsync(Arg.Any<IEnumerable<OpcUaWrite>>(), cancellation.Token))
            .Do(c => writtenValues = [.. (IEnumerable<OpcUaWrite>)c[0]!]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.ConnectAsync(cancellation.Token);
        await opcUaDataport.DisconnectAsync(cancellation.Token);
        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaDataport.SendAsync(0, [new() { Channel = "channel", Value = 2, },], cancellation.Token);

        writtenValues.Should().BeEquivalentTo([new OpcUaWrite(new Opc.Ua.NodeId("ns=2;s=test"), 2, Opc.Ua.StatusCodes.Good, DateTime.MinValue),]);
    }

    [Fact]
    public async Task Maps_all_channels_on_retry_after_a_failed_connect_Async()
    {
        var communication = OutgoingClientSetup.CreateCommunication(
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

        List<OpcUaWrite> writtenValues = [];
        opcUaClient.When(c => c.WriteValuesAsync(Arg.Any<IEnumerable<OpcUaWrite>>(), cancellation.Token))
            .Do(c => writtenValues = [.. (IEnumerable<OpcUaWrite>)c[0]!]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();
        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaDataport.SendAsync(0, [new() { Channel = "channel1", Value = 1, }, new() { Channel = "channel2", Value = 2, },], cancellation.Token);

        writtenValues.Should().BeEquivalentTo(
        [
            new OpcUaWrite(new Opc.Ua.NodeId("ns=2;s=test1"), 1, Opc.Ua.StatusCodes.Good, DateTime.MinValue),
            new OpcUaWrite(new Opc.Ua.NodeId("ns=2;s=test2"), 2, Opc.Ua.StatusCodes.Good, DateTime.MinValue),
        ]);
    }

    [Fact]
    public async Task Releases_the_client_when_connecting_fails_Async()
    {
        var communication = OutgoingClientSetup.CreateCommunication(
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
                DisplayName = "other",
                NodeId = new("ns=2;s=other"),
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
        var communication = OutgoingClientSetup.CreateCommunication(
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
                DisplayName = "root",
                NodeId = new("ns=2;s=parent"),
                Children =
                [
                    new()
                    {
                        DisplayName = "notTest",
                        NodeId = new("ns=2;s=parent/test"),
                    },
                ],
            }
        ]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*Cannot find node 'test'*");
    }

    [Fact]
    public async Task Fails_the_send_when_a_channel_is_not_mapped_Async()
    {
        var communication = OutgoingClientSetup.CreateCommunication(
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
                DisplayName = "test",
                NodeId = new("ns=2;s=test"),
            },
        ]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);
        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaDataport.Awaiting(x => x.SendAsync(0, [new() { Channel = "unmapped", Value = 2, },], cancellation.Token)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*'unmapped'*not mapped*");

        await opcUaClient.DidNotReceive().WriteValuesAsync(Arg.Any<IEnumerable<OpcUaWrite>>(), cancellation.Token);
    }

    /// <summary>
    /// A data port linked in both directions receives every data point of the other direction too,
    /// with no channel of its own. The server need not even have a node for it.
    /// </summary>
    [Fact]
    public async Task Skips_a_node_that_has_no_affected_channel_Async()
    {
        var communication = OutgoingClientSetup.CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("a7bc6fae-99bc-4a18-9d4b-9ea4630f4a61"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    Name = "inbound",
                },
                new()
                {
                    Id = Guid.Parse("c4e8a1f3-5b7d-4f2a-8e6c-1a3b5d7f9e20"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    Name = "outbound",
                    AffectedChannels = ["channel"],
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
                DisplayName = "outbound",
                NodeId = new("ns=2;s=outbound"),
            },
        ]);
        List<OpcUaWrite> written = [];
        opcUaClient.When(c => c.WriteValuesAsync(Arg.Any<IEnumerable<OpcUaWrite>>(), cancellation.Token))
            .Do(c => written.AddRange((IEnumerable<OpcUaWrite>)c[0]));

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.ConnectAsync(cancellation.Token);
        await opcUaDataport.SendAsync(0, [new() { Channel = "channel", Value = 2, },], cancellation.Token);

        written.Should().ContainSingle().Which.NodeId.Should().Be(new Opc.Ua.NodeId("ns=2;s=outbound"));
    }

    [Fact]
    public async Task Fails_the_connect_when_a_node_has_more_than_one_affected_channel_Async()
    {
        var communication = OutgoingClientSetup.CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("a7bc6fae-99bc-4a18-9d4b-9ea4630f4a61"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    AffectedChannels =
                    [
                        "channel1",
                        "channel2",
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
                DisplayName = "test",
                NodeId = new("ns=2;s=test"),
            },
        ]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*'test'*more than one affected channel*'channel1', 'channel2'*");
    }

    [Fact]
    public async Task Fails_the_connect_when_two_nodes_affect_the_same_channel_Async()
    {
        var communication = OutgoingClientSetup.CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("a7bc6fae-99bc-4a18-9d4b-9ea4630f4a61"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    AffectedChannels =
                    [
                        "channel",
                    ],
                    Name = "first",
                },
                new()
                {
                    Id = Guid.Parse("cbf0f7d6-5e6a-4b62-9a19-1b2fbb0ac0e8"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    AffectedChannels =
                    [
                        "channel",
                    ],
                    Name = "second",
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
                DisplayName = "first",
                NodeId = new("ns=2;s=first"),
            },
            new()
            {
                DisplayName = "second",
                NodeId = new("ns=2;s=second"),
            },
        ]);

        var opcUaDataport = new OpcUaClientDataPortOutgoing(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*'channel'*'first'*'second'*");
    }
}

public class OpcUaClientDataPortOutgoing_ConnectAsync
{
    /// <summary>
    /// The address space has no node of an envelope child, and a data point that carries two of them
    /// ends two node routes - so mapping per route would report the data point as its own duplicate.
    /// </summary>
    [Fact]
    public async Task Maps_a_data_point_with_envelope_children_once_Async()
    {
        var (port, written, cancellation) = OutgoingClientSetup.CreatePortWithEnvelopeChildren();
        using var tokenSource = cancellation;

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(0, [OutgoingClientSetup.Value("value", 23.5),], cancellation.Token);

        written.Should().ContainSingle()
            .Which.Should().Match<OpcUaWrite>(write => write.NodeId.ToString() == "ns=2;s=test" && Equals(write.Value, 23.5));
    }
}

public class OpcUaClientDataPortOutgoing_SendAsync
{
    /// <summary>
    /// A status code has no write of its own; it replaces the status the value of its parent is
    /// written with, which is good when nothing is linked.
    /// </summary>
    [Fact]
    public async Task Writes_a_value_with_the_status_code_linked_to_its_envelope_child_Async()
    {
        var (port, written, cancellation) = OutgoingClientSetup.CreatePortWithEnvelopeChildren();
        using var tokenSource = cancellation;

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(0, [OutgoingClientSetup.Value("status", "BadCommunicationError"), OutgoingClientSetup.Value("value", 23.5),], cancellation.Token);

        written.Should().ContainSingle()
            .Which.Should().Be(new OpcUaWrite(new Opc.Ua.NodeId("ns=2;s=test"), 23.5, Opc.Ua.StatusCodes.BadCommunicationError, DateTime.MinValue));
    }

    [Fact]
    public async Task Writes_a_value_as_good_when_no_status_code_is_linked_Async()
    {
        var (port, written, cancellation) = OutgoingClientSetup.CreatePortWithEnvelopeChildren();
        using var tokenSource = cancellation;

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(0, [OutgoingClientSetup.Value("value", 23.5),], cancellation.Token);

        written.Should().ContainSingle()
            .Which.StatusCode.Should().Be(new Opc.Ua.StatusCode(Opc.Ua.StatusCodes.Good));
    }

    [Fact]
    public async Task Writes_a_value_as_BadInternalError_when_its_status_code_cannot_be_read_Async()
    {
        FakeLogger<IOpcUaClient> logger = new();
        var (port, written, cancellation) = OutgoingClientSetup.CreatePortWithEnvelopeChildren(logger);
        using var tokenSource = cancellation;

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(0, [OutgoingClientSetup.Value("status", "0xZZ"), OutgoingClientSetup.Value("value", 23.5),], cancellation.Token);

        written.Should().ContainSingle()
            .Which.StatusCode.Should().Be(new Opc.Ua.StatusCode(Opc.Ua.StatusCodes.BadInternalError));
        logger.Collector.GetSnapshot().Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("'0xZZ'"));
    }

    /// <summary>
    /// The engine sends a channel only in the cycle it changes in, so a status code that arrives
    /// without its parent's value has to be written with the next value of that node.
    /// </summary>
    [Fact]
    public async Task Writes_the_last_status_code_with_a_later_value_Async()
    {
        var (port, written, cancellation) = OutgoingClientSetup.CreatePortWithEnvelopeChildren();
        using var tokenSource = cancellation;

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(0, [OutgoingClientSetup.Value("status", 0x80FF0000u),], cancellation.Token);
        await port.SendAsync(1, [OutgoingClientSetup.Value("value", 23.5),], cancellation.Token);

        written.Should().ContainSingle()
            .Which.Should().Be(new OpcUaWrite(new Opc.Ua.NodeId("ns=2;s=test"), 23.5, 0x80FF0000u, DateTime.MinValue));
    }

    [Fact]
    public async Task Writes_nothing_for_a_status_code_that_arrives_without_a_value_Async()
    {
        var (port, written, cancellation) = OutgoingClientSetup.CreatePortWithEnvelopeChildren();
        using var tokenSource = cancellation;

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(0, [OutgoingClientSetup.Value("status", "Uncertain"),], cancellation.Token);

        written.Should().BeEmpty();
    }

    [Fact]
    public async Task Writes_a_value_with_the_source_timestamp_linked_to_its_envelope_child_Async()
    {
        var (port, written, cancellation) = OutgoingClientSetup.CreatePortWithEnvelopeChildren();
        using var tokenSource = cancellation;
        var produced = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(0, [OutgoingClientSetup.Value("sent", produced), OutgoingClientSetup.Value("value", 23.5),], cancellation.Token);

        written.Should().ContainSingle()
            .Which.SourceTimestamp.Should().Be(produced);
    }

    /// <summary>
    /// An unset source timestamp is what makes the receiving server stamp the value itself.
    /// </summary>
    [Fact]
    public async Task Writes_no_source_timestamp_when_none_is_linked_Async()
    {
        var (port, written, cancellation) = OutgoingClientSetup.CreatePortWithEnvelopeChildren();
        using var tokenSource = cancellation;

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(0, [OutgoingClientSetup.Value("value", 23.5),], cancellation.Token);

        written.Should().ContainSingle()
            .Which.SourceTimestamp.Should().Be(DateTime.MinValue);
    }

    /// <summary>
    /// A source timestamp belongs to the value it arrives with. The engine does not send a
    /// timestamp again that did not change, so carrying it over would stamp a later value with
    /// the time of an earlier one.
    /// </summary>
    [Fact]
    public async Task Writes_a_value_of_a_later_cycle_without_a_source_timestamp_Async()
    {
        var (port, written, cancellation) = OutgoingClientSetup.CreatePortWithEnvelopeChildren();
        using var tokenSource = cancellation;
        var produced = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(0, [OutgoingClientSetup.Value("sent", produced), OutgoingClientSetup.Value("value", 23.5),], cancellation.Token);
        written.Clear();
        await port.SendAsync(1, [OutgoingClientSetup.Value("value", 24.0),], cancellation.Token);

        written.Should().ContainSingle()
            .Which.SourceTimestamp.Should().Be(DateTime.MinValue);
    }
}

public class OpcUaClientDataPortOutgoing_DisconnectAsync
{
    /// <summary>
    /// A session goes and the node ids go with it, but what the engine last said about a value does
    /// not. Forgetting it would write the next value as good on a data point whose last known status
    /// was bad, and the engine sends a channel only in the cycle it changes in.
    /// </summary>
    [Fact]
    public async Task Writes_the_status_code_it_was_told_before_a_reconnect_Async()
    {
        var (port, written, cancellation) = OutgoingClientSetup.CreatePortWithEnvelopeChildren();
        using var tokenSource = cancellation;

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(0, [OutgoingClientSetup.Value("status", 0x80FF0000u),], cancellation.Token);
        await port.DisconnectAsync(cancellation.Token);

        await port.ConnectAsync(cancellation.Token);
        await port.SendAsync(1, [OutgoingClientSetup.Value("value", 23.5),], cancellation.Token);

        written.Should().ContainSingle()
            .Which.StatusCode.Should().Be(new Opc.Ua.StatusCode(0x80FF0000u));
    }
}
