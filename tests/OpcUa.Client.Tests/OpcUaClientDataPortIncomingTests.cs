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
using ViciOne.ManagedEngine.ExternalCommunication;
using ViciOne.ManagedEngine.Runtime;
using Xunit;

namespace ViciOne.Suite.DataPort;

internal static class IncomingClientSetup
{
    private static readonly Guid s_dataPointId = Guid.Parse("81c3bbad-6326-4122-b195-10aab9d75949");

    internal static OpcUaClientDataPortCommunication CreateCommunicationWithEnvelopeChildren(Type statusCodeValueType)
        => new()
        {
            Nodes =
            [
                new()
                {
                    Id = s_dataPointId,
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    Name = "test",
                    ValueType = typeof(double),
                    AffectedChannels = ["value"],
                    TransferredChannels = ["value", "status", "sent", "processed"],
                },
                new()
                {
                    Id = Guid.Parse("2f4f9d5c-7c2b-4a52-9a8e-4f3d5a2b1c60"),
                    ParentId = s_dataPointId,
                    DesignId = OpcUaClientNodeDesignId.StatusCode,
                    Name = "quality",
                    ValueType = statusCodeValueType,
                    AffectedChannels = ["status"],
                },
                new()
                {
                    Id = Guid.Parse("3a6b0e7d-8d3c-4b63-ab9f-50416b3c2d71"),
                    ParentId = s_dataPointId,
                    DesignId = OpcUaClientNodeDesignId.SourceTimestamp,
                    Name = "when",
                    ValueType = typeof(DateTime),
                    AffectedChannels = ["sent"],
                },
                new()
                {
                    Id = Guid.Parse("5d8c2f1b-9e4a-4c7d-b2f6-7a3e1c5d9b04"),
                    ParentId = s_dataPointId,
                    DesignId = OpcUaClientNodeDesignId.ServerTimestamp,
                    Name = "processed",
                    ValueType = typeof(DateTime),
                    AffectedChannels = ["processed"],
                },
            ],
        };
}

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

        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test", Arg.Any<Action<OpcUaValue>>(), cancellation.Token);
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

        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test", Arg.Any<Action<OpcUaValue>>(), cancellation.Token);
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
        opcUaClient.SubscribeAsync(Arg.Any<Opc.Ua.NodeId>(), Arg.Any<Action<OpcUaValue>>(), cancellation.Token)
            .Returns(_ => ++subscribeCalls == 1
                ? Task.FromException(new InvalidOperationException("Subscribe failed."))
                : Task.CompletedTask);

        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();
        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaClient.Received(2).SubscribeAsync("ns=2;s=test", Arg.Any<Action<OpcUaValue>>(), cancellation.Token);
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

        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test1", Arg.Any<Action<OpcUaValue>>(), cancellation.Token);
        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test2", Arg.Any<Action<OpcUaValue>>(), cancellation.Token);
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

        Action<OpcUaValue>? subscribeAction = null;
        opcUaClient.When(c => c.SubscribeAsync(Arg.Any<Opc.Ua.NodeId>(), Arg.Any<Action<OpcUaValue>>(), cancellation.Token))
            .Do(c => subscribeAction = c[1] as Action<OpcUaValue>);
        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        var receivedValues = false;
        opcUaDataport.Received += (values) => { receivedValues = values.Any(v => v.Channel == "channel" && (v.Value?.Equals(1.4) ?? false)); };

        await opcUaDataport.ConnectAsync(cancellation.Token);

        subscribeAction.Should().NotBeNull();
        subscribeAction(new(1.4, new DateTime(2023, 10, 12, 15, 27, 30), Opc.Ua.StatusCodes.Good, new DateTime(2023, 10, 12, 15, 27, 30), DateTime.MinValue));

        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test", Arg.Any<Action<OpcUaValue>>(), cancellation.Token);
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

        Action<OpcUaValue>? subscribeAction = null;
        opcUaClient.When(c => c.SubscribeAsync(Arg.Any<Opc.Ua.NodeId>(), Arg.Any<Action<OpcUaValue>>(), cancellation.Token))
            .Do(c => subscribeAction = c[1] as Action<OpcUaValue>);
        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        var receivedValues = false;
        opcUaDataport.Received += (values) => { receivedValues = values.Any(v => v.Channel == "channel" && (v.Value?.Equals(1.4) ?? false)); };

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();
        subscribeAction.Should().BeNull();
        receivedValues.Should().BeFalse();
    }

    /// <summary>
    /// A data port linked in both directions receives every data point of the other direction too,
    /// with no channel of its own. The server need not even have a node for it.
    /// </summary>
    [Fact]
    public async Task Skips_a_node_that_has_no_affected_channel_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("81c3bbad-6326-4122-b195-10aab9d75949"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    Name = "outbound",
                },
                new()
                {
                    Id = Guid.Parse("6b1e4a2c-3d5f-4e7a-9c8b-0d2f4a6c8e13"),
                    DesignId = OpcUaClientNodeDesignId.Variable,
                    Name = "inbound",
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
                DisplayName = "inbound",
                NodeId = new("ns=2;s=inbound"),
            },
        ]);

        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaClient.Received(1).SubscribeAsync(Arg.Any<Opc.Ua.NodeId>(), Arg.Any<Action<OpcUaValue>>(), cancellation.Token);
        await opcUaClient.Received(1).SubscribeAsync(new Opc.Ua.NodeId("ns=2;s=inbound"), Arg.Any<Action<OpcUaValue>>(), cancellation.Token);
    }

    [Fact]
    public async Task Fails_the_connect_when_a_node_has_more_than_one_affected_channel_Async()
    {
        var communication = CreateCommunication(
            [
                new()
                {
                    Id = Guid.Parse("81c3bbad-6326-4122-b195-10aab9d75949"),
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

        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.Awaiting(x => x.ConnectAsync(cancellation.Token)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*'test'*more than one affected channel*'channel1', 'channel2'*");
    }
}

public class OpcUaClientDataPortIncoming_ConnectAsync
{
    /// <summary>
    /// A data point that carries children ends one node route per child, and the address space has
    /// no node of a child at all. Walking a route per child would subscribe the data point twice and
    /// double every value it receives, and looking a child up would fail the connect outright.
    /// </summary>
    [Fact]
    public async Task Subscribes_a_data_point_with_envelope_children_once_Async()
    {
        var communication = IncomingClientSetup.CreateCommunicationWithEnvelopeChildren(typeof(uint));

        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        NullLogger<IOpcUaClient> logger = new();
        using CancellationTokenSource cancellation = new();
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(
        [
            new()
            {
                DisplayName = "test",
                NodeId = new("ns=2;s=test"),
            },
        ]);
        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        await opcUaDataport.ConnectAsync(cancellation.Token);

        await opcUaClient.Received(1).SubscribeAsync(Arg.Any<Opc.Ua.NodeId>(), Arg.Any<Action<OpcUaValue>>(), cancellation.Token);
    }
}

public class OpcUaClientDataPortIncoming_ReceiveValue
{
    private static readonly DateTime s_sourceTimestamp = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
    private static readonly DateTime s_serverTimestamp = new(2026, 3, 4, 5, 6, 9, DateTimeKind.Utc);

    private static async Task<IReadOnlyCollection<ExternalValue>> ReceiveAsync(OpcUaClientDataPortCommunication communication, OpcUaValue value)
    {
        var instanceManager = Substitute.For<IOpcUaClientInstanceManager>();
        var opcUaClient = Substitute.For<IOpcUaClient>();
        NullLogger<IOpcUaClient> logger = new();
        using CancellationTokenSource cancellation = new();
        opcUaClient.BrowseNodesAsync(cancellation.Token).Returns(
        [
            new()
            {
                DisplayName = "test",
                NodeId = new("ns=2;s=test"),
            },
        ]);

        Action<OpcUaValue>? subscribeAction = null;
        opcUaClient.When(c => c.SubscribeAsync(Arg.Any<Opc.Ua.NodeId>(), Arg.Any<Action<OpcUaValue>>(), cancellation.Token))
            .Do(c => subscribeAction = c[1] as Action<OpcUaValue>);
        var opcUaDataport = new OpcUaClientDataPortIncoming(communication, logger, instanceManager);
        instanceManager.GetOrRegisterOpcUaClientAsync(communication, opcUaDataport, logger, cancellation.Token).Returns(opcUaClient);

        IReadOnlyCollection<ExternalValue> received = [];
        opcUaDataport.Received += values => received = values;

        await opcUaDataport.ConnectAsync(cancellation.Token);

        subscribeAction.Should().NotBeNull();
        subscribeAction(value);

        await opcUaClient.Received(1).SubscribeAsync("ns=2;s=test", Arg.Any<Action<OpcUaValue>>(), cancellation.Token);

        return received;
    }

    /// <summary>
    /// The value and the parts of its envelope reach the engine as one batch, so a status code is
    /// never a cycle behind the value it belongs to.
    /// </summary>
    [Fact]
    public async Task Forwards_the_data_point_and_its_envelope_children_in_one_batch_Async()
    {
        var received = await ReceiveAsync(
            IncomingClientSetup.CreateCommunicationWithEnvelopeChildren(typeof(uint)),
            new(23.5, s_sourceTimestamp, Opc.Ua.StatusCodes.UncertainLastUsableValue, s_sourceTimestamp, DateTime.MinValue));

        received.Should().SatisfyRespectively(
            value =>
            {
                value.Channel.Should().Be("value");
                value.Value.Should().Be(23.5);
                value.Timestamp.Should().Be(s_sourceTimestamp);
            },
            status =>
            {
                status.Channel.Should().Be("status");
                status.Value.Should().Be(Opc.Ua.StatusCodes.UncertainLastUsableValue);
                status.Timestamp.Should().Be(s_sourceTimestamp);
            },
            sent =>
            {
                sent.Channel.Should().Be("sent");
                sent.Value.Should().Be(s_sourceTimestamp);
                sent.Timestamp.Should().Be(s_sourceTimestamp);
            });
    }

    /// <summary>
    /// The name has to be one the other side converts back. StatusCode.ToString() is not: it appends
    /// the info bits of a code that carries any, and returns the empty string for a code the stack has
    /// no name for. The name of a code carries no info bits of its own.
    /// </summary>
    [Theory]
    [InlineData(Opc.Ua.StatusCodes.BadCommunicationError, "BadCommunicationError")]
    [InlineData(Opc.Ua.StatusCodes.UncertainLastUsableValue, "UncertainLastUsableValue")]
    [InlineData(0x00000600u, "Good")]
    [InlineData(0x80FF0000u, "0x80FF0000")]
    public async Task Forwards_a_status_code_as_its_name_when_the_tree_links_it_as_a_string_Async(uint statusCode, string expected)
    {
        var received = await ReceiveAsync(
            IncomingClientSetup.CreateCommunicationWithEnvelopeChildren(typeof(string)),
            new(23.5, s_sourceTimestamp, statusCode, s_sourceTimestamp, DateTime.MinValue));

        received.Should().ContainSingle(value => value.Channel == "status")
            .Which.Value.Should().Be(expected);
    }

    /// <summary>
    /// The two timestamps an OPC UA value carries mean different things, so each child reports the
    /// one it names rather than whichever the server happened to set.
    /// </summary>
    [Fact]
    public async Task Forwards_each_timestamp_on_the_child_that_names_it_Async()
    {
        var received = await ReceiveAsync(
            IncomingClientSetup.CreateCommunicationWithEnvelopeChildren(typeof(uint)),
            new(23.5, s_sourceTimestamp, Opc.Ua.StatusCodes.Good, s_sourceTimestamp, s_serverTimestamp));

        received.Should().ContainSingle(value => value.Channel == "sent")
            .Which.Value.Should().Be(s_sourceTimestamp);
        received.Should().ContainSingle(value => value.Channel == "processed")
            .Which.Value.Should().Be(s_serverTimestamp);
    }

    /// <summary>
    /// A server is free to set neither timestamp. The child then carries nothing rather than the
    /// fallback the value itself is reported for, which would read as a time the server never gave.
    /// </summary>
    [Fact]
    public async Task Forwards_no_timestamp_the_server_did_not_send_Async()
    {
        var publishTime = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);

        var received = await ReceiveAsync(
            IncomingClientSetup.CreateCommunicationWithEnvelopeChildren(typeof(uint)),
            new(23.5, publishTime, Opc.Ua.StatusCodes.Good, DateTime.MinValue, DateTime.MinValue));

        received.Should().NotContain(value => value.Channel == "sent");
        received.Should().NotContain(value => value.Channel == "processed");
        received.Should().ContainSingle(value => value.Channel == "value")
            .Which.Timestamp.Should().Be(publishTime);
    }
}
