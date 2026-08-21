using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using ViciOne.ManagedEngine.ExternalCommunication;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
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
    public async Task Fails_the_connect_after_the_server_was_released_Async()
    {
        var communication = CreateCommunication();

        var server = Substitute.For<IOpcUaServer, IDisposable>();
        using OpcUaServerInstanceManager instanceManager = new((_, _) => server);
        OpcUaServerDataPortIncoming dataPort = new(communication, instanceManager, new FakeLogger<IOpcUaServer>());

        await dataPort.DisposeAsync();

        await dataPort.Awaiting(port => port.ConnectAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await dataPort.Awaiting(port => port.DisconnectAsync(TestContext.Current.CancellationToken))
            .Should().NotThrowAsync();
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

        server.ReceiveValue += Raise.Event<Action<ReceivedWrite>>(new ReceivedWrite("writeable", "value", new DateTime(2023, 11, 20), Opc.Ua.StatusCodes.Good, new DateTime(2023, 11, 20)));

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

public class OpcUaServerDataPortIncoming_ReceiveValue
{
    private static OpcUaServerDataPortCommunication CreateCommunicationWithChildren()
    {
        var dataPointId = Guid.Parse("a631ad22-3e31-483e-9242-8c4198864c6b");

        return new()
        {
            Nodes =
            [
                new()
                {
                    Id = dataPointId,
                    DesignId = OpcUaServerNodeDesignId.Variable,
                    Name = "child",
                    ValueType = typeof(int),
                    AffectedChannels = ["value"],
                    TransferredChannels = ["value", "status", "sent"],
                },
                new()
                {
                    Id = Guid.Parse("9b2e5c1a-3f4d-4a7b-8c6e-0d1f2a3b4c5d"),
                    ParentId = dataPointId,
                    DesignId = OpcUaServerNodeDesignId.StatusCode,
                    Name = "quality",
                    ValueType = typeof(uint),
                    AffectedChannels = ["status"],
                },
                new()
                {
                    Id = Guid.Parse("7c1f4a2e-5b3d-4e6f-8a90-1b2c3d4e5f60"),
                    ParentId = dataPointId,
                    DesignId = OpcUaServerNodeDesignId.SourceTimestamp,
                    Name = "when",
                    ValueType = typeof(DateTime),
                    AffectedChannels = ["sent"],
                },
            ],
        };
    }

    [Fact]
    public async Task Forwards_the_timestamp_of_a_write_beside_the_value_Async()
    {
        var communication = CreateCommunicationWithChildren();
        var written = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        var logger = Substitute.For<ILogger<IOpcUaServer>>();
        instanceManager.GetOrRegisterOpcUaServer(communication, Arg.Any<OpcUaServerDataPortIncoming>(), logger).Returns(server);
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortIncoming dataportIncoming = new(communication, instanceManager, logger);

        IReadOnlyCollection<ExternalValue> received = [];
        dataportIncoming.Received += values => received = values;

        await dataportIncoming.ConnectAsync(cancellation.Token);
        server.ReceiveValue += Raise.Event<Action<ReceivedWrite>>(new ReceivedWrite("value", 42, written, Opc.Ua.StatusCodes.Good, written));

        received.Should().ContainSingle(value => value.Channel == "value")
            .Which.Should().Match<ExternalValue>(value => Equals(value.Value, 42) && value.Timestamp == written);
        received.Should().ContainSingle(value => value.Channel == "sent")
            .Which.Should().Match<ExternalValue>(sent => Equals(sent.Value, written) && sent.Timestamp == written);
    }

    [Fact]
    public async Task Forwards_the_status_code_a_client_wrote_beside_the_value_Async()
    {
        var communication = CreateCommunicationWithChildren();
        var written = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        var logger = Substitute.For<ILogger<IOpcUaServer>>();
        instanceManager.GetOrRegisterOpcUaServer(communication, Arg.Any<OpcUaServerDataPortIncoming>(), logger).Returns(server);
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortIncoming dataportIncoming = new(communication, instanceManager, logger);

        IReadOnlyCollection<ExternalValue> received = [];
        dataportIncoming.Received += values => received = values;

        await dataportIncoming.ConnectAsync(cancellation.Token);
        server.ReceiveValue += Raise.Event<Action<ReceivedWrite>>(
            new ReceivedWrite("value", 42, written, Opc.Ua.StatusCodes.UncertainLastUsableValue, written));

        received.Should().SatisfyRespectively(
            value => value.Channel.Should().Be("value"),
            status =>
            {
                status.Channel.Should().Be("status");
                status.Value.Should().Be(Opc.Ua.StatusCodes.UncertainLastUsableValue);
            },
            sent => sent.Channel.Should().Be("sent"));
    }

    /// <summary>
    /// A client may write without a source timestamp. The value is still stamped with the moment the
    /// write arrived, but the child reports only what the client sent, so the engine cannot read a
    /// receive time as a production time.
    /// </summary>
    [Fact]
    public async Task Forwards_no_source_timestamp_for_a_write_that_carried_none_Async()
    {
        var communication = CreateCommunicationWithChildren();
        var arrived = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var instanceManager = Substitute.For<IOpcUaServerInstanceManager>();
        var server = Substitute.For<IOpcUaServer>();
        var logger = Substitute.For<ILogger<IOpcUaServer>>();
        instanceManager.GetOrRegisterOpcUaServer(communication, Arg.Any<OpcUaServerDataPortIncoming>(), logger).Returns(server);
        using CancellationTokenSource cancellation = new();
        await using OpcUaServerDataPortIncoming dataportIncoming = new(communication, instanceManager, logger);

        IReadOnlyCollection<ExternalValue> received = [];
        dataportIncoming.Received += values => received = values;

        await dataportIncoming.ConnectAsync(cancellation.Token);
        server.ReceiveValue += Raise.Event<Action<ReceivedWrite>>(
            new ReceivedWrite("value", 42, arrived, Opc.Ua.StatusCodes.Good, DateTime.MinValue));

        received.Should().NotContain(value => value.Channel == "sent");
        received.Should().ContainSingle(value => value.Channel == "value")
            .Which.Timestamp.Should().Be(arrived);
    }
}
