using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Opc.Ua;
using Opc.Ua.Server;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class DataPortNodeManager_IsInRange
{
    [Theory]
    [InlineData("value")]
    [InlineData(3)]
    [InlineData(3L)]
    [InlineData(3.4)]
    [InlineData(3.4f)]
    public void Returns_true_if_no_range_is_set(object value)
        => DataPortNodeManager.IsInRange(null, null, value).Should().BeTrue();

    [Theory]
    [InlineData(3, 2)]
    [InlineData(3L, 2L)]
    [InlineData(3.4, 3.2)]
    [InlineData(3.4f, 3.2f)]
    public void Returns_false_if_bigger_than_maximum(object value, object maximum)
        => DataPortNodeManager.IsInRange(null, new Property { Value = maximum }, value).Should().BeFalse();

    [Theory]
    [InlineData(3, 4)]
    [InlineData(3L, 4L)]
    [InlineData(3.4, 3.5)]
    [InlineData(3.4f, 3.5f)]
    public void Returns_false_if_smaller_than_minimum(object value, object minimum) => DataPortNodeManager.IsInRange(new Property { Value = minimum }, null, value).Should().BeFalse();

    [Theory]
    [InlineData(3, 2, 4)]
    [InlineData(3L, 2L, 4L)]
    [InlineData(3.4, 3.3, 3.5)]
    [InlineData(3.4f, 3.3f, 3.5f)]
    public void Returns_true_if_within_limits(object value, object minimum, object maximum)
        => DataPortNodeManager.IsInRange(new Property { Value = minimum }, new Property { Value = maximum }, value).Should().BeTrue();

    [Fact]
    public void Does_not_compare_string_length()
        => DataPortNodeManager.IsInRange(new Property { Value = "short string" }, null, "very long string").Should().BeTrue();

    [Fact]
    public void Does_not_compare_empty_string()
        => DataPortNodeManager.IsInRange(null, new Property { Value = string.Empty }, "any string").Should().BeTrue();

    [Fact]
    public void Does_not_compare_strings()
        => DataPortNodeManager.IsInRange(null, new Property { Value = "3" }, "4").Should().BeTrue();

    [Theory]
    [InlineData(3.4, 2)]
    [InlineData(3, 2L)]
    [InlineData(3.4, 3.2f)]
    [InlineData(3L, 2.5)]
    public void Returns_false_if_bigger_than_a_maximum_of_another_numeric_type(object value, object maximum)
        => DataPortNodeManager.IsInRange(null, new Property { Value = maximum }, value).Should().BeFalse();

    [Theory]
    [InlineData(3.4, 4)]
    [InlineData(3, 4L)]
    [InlineData(3.4, 3.5f)]
    [InlineData(3L, 3.5)]
    public void Returns_false_if_smaller_than_a_minimum_of_another_numeric_type(object value, object minimum)
        => DataPortNodeManager.IsInRange(new Property { Value = minimum }, null, value).Should().BeFalse();

    [Theory]
    [InlineData(3.4, 2, 4L)]
    [InlineData(3, 2.5, 4f)]
    [InlineData(3L, 2f, 4.5)]
    public void Returns_true_if_within_limits_of_another_numeric_type(object value, object minimum, object maximum)
        => DataPortNodeManager.IsInRange(new Property { Value = minimum }, new Property { Value = maximum }, value).Should().BeTrue();

    [Theory]
    [InlineData(3, 3)]
    [InlineData(3.4, 3.4)]
    public void Returns_true_if_equal_to_a_limit(object value, object limit)
        => DataPortNodeManager.IsInRange(new Property { Value = limit }, new Property { Value = limit }, value).Should().BeTrue();

    [Fact]
    public void Returns_true_if_a_date_time_is_within_date_time_limits()
        => DataPortNodeManager.IsInRange(new Property { Value = new DateTime(2024, 1, 1) }, new Property { Value = new DateTime(2026, 1, 1) }, new DateTime(2025, 1, 1)).Should().BeTrue();

    [Fact]
    public void Returns_false_if_a_date_time_is_older_than_a_date_time_minimum()
        => DataPortNodeManager.IsInRange(new Property { Value = new DateTime(2026, 1, 1) }, null, new DateTime(2025, 1, 1)).Should().BeFalse();

    [Fact]
    public void Returns_false_if_a_date_time_is_limited_by_a_number()
        => DataPortNodeManager.IsInRange(null, new Property { Value = 5 }, new DateTime(2025, 1, 1)).Should().BeFalse();

    [Fact]
    public void Returns_false_if_a_number_is_limited_by_text()
        => DataPortNodeManager.IsInRange(null, new Property { Value = "5" }, 3).Should().BeFalse();

    [Fact]
    public void Compares_integers_that_no_longer_fit_a_double_exactly()
        => DataPortNodeManager.IsInRange(null, new Property { Value = 9007199254740992L }, 9007199254740993L).Should().BeFalse();

    [Fact]
    public void Compares_a_decimal_limit_without_rounding_it()
        => DataPortNodeManager.IsInRange(null, new Property { Value = 1m }, 1.0000000000000000000000000001m).Should().BeFalse();

    [Fact]
    public void Returns_false_if_not_a_number_is_checked_against_a_minimum()
        => DataPortNodeManager.IsInRange(new Property { Value = 0 }, null, double.NaN).Should().BeFalse();

    [Fact]
    public void Returns_false_if_a_value_beyond_the_decimal_range_passes_a_maximum()
        => DataPortNodeManager.IsInRange(null, new Property { Value = 1 }, 1e30d).Should().BeFalse();
}

public class DataPortNodeManager_OnWriteValue
{
    private const string Channel = "channel";
    private const string SecondChannel = "second channel";

    private static readonly DateTime s_writeTimestamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void Publishes_the_written_value_to_every_mapped_channel()
    {
        using var manager = CreateNodeManager(typeof(double));
        List<(string Channel, DateTime Timestamp, object Value, StatusCode StatusCode)> received = [];
        manager.ReceiveValue += write => received.Add((write.Channel, write.Timestamp, write.Value, write.StatusCode));
        var node = manager.GetNodeState(Channel);

        var result = Write(node, node, 3.4d);

        result.StatusCode.Code.Should().Be(StatusCodes.Good);
        received.Should().BeEquivalentTo([(Channel, s_writeTimestamp, 3.4d), (SecondChannel, s_writeTimestamp, 3.4d)]);
    }

    public static TheoryData<Type, object> ValuesOfEveryValueType => new()
    {
        { typeof(bool), true },
        { typeof(sbyte), (sbyte)-3 },
        { typeof(byte), (byte)3 },
        { typeof(short), (short)-3 },
        { typeof(ushort), (ushort)3 },
        { typeof(int), -3 },
        { typeof(uint), 3u },
        { typeof(long), -3L },
        { typeof(ulong), 3UL },
        { typeof(float), 3.4f },
        { typeof(double), 3.4d },
        { typeof(string), "text" },
        { typeof(DateTime), new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc) },
        { typeof(byte[]), new byte[] { 1, 2, 3 } },
    };

    [Theory]
    [MemberData(nameof(ValuesOfEveryValueType))]
    public void Accepts_a_write_of_the_value_type_of_the_data_point(Type valueType, object value)
    {
        using var manager = CreateNodeManager(valueType);
        var node = manager.GetNodeState(Channel);

        var result = Write(node, node, value);

        result.StatusCode.Code.Should().Be(StatusCodes.Good);
    }

    [Fact]
    public void Stamps_the_current_time_if_the_write_carries_no_timestamp()
    {
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        using var manager = CreateNodeManager(typeof(double), timeProvider: timeProvider);
        var node = manager.GetNodeState(Channel);
        var timestamp = DateTime.MinValue;

        var result = Write(node, node, 3.4d, ref timestamp);

        result.StatusCode.Code.Should().Be(StatusCodes.Good);
        timestamp.Should().Be(timeProvider.GetUtcNow().DateTime);
    }

    [Fact]
    public void Returns_bad_out_of_range_if_the_value_passes_a_maximum_of_another_numeric_type()
    {
        using var manager = CreateNodeManager(typeof(double), maximum: new Property { Value = 2 });
        var node = manager.GetNodeState(Channel);

        var result = Write(node, node, 3.4d);

        result.StatusCode.Code.Should().Be(StatusCodes.BadOutOfRange);
    }

    [Fact]
    public void Returns_bad_type_mismatch_if_the_value_does_not_match_the_node_data_type()
    {
        using var manager = CreateNodeManager(typeof(double));
        var node = manager.GetNodeState(Channel);

        var result = Write(node, node, "not a number");

        result.StatusCode.Code.Should().Be(StatusCodes.BadTypeMismatch);
    }

    [Fact]
    public void Returns_bad_not_type_definition_if_the_node_is_not_a_variable()
    {
        using var manager = CreateNodeManager(typeof(double));
        var node = manager.GetNodeState(Channel);
        using FolderState folder = new(null) { NodeId = new NodeId("folder", 1) };

        var result = Write(node, folder, 3.4d);

        result.StatusCode.Code.Should().Be(StatusCodes.BadNotTypeDefinition);
    }

    [Fact]
    public void Returns_bad_internal_error_if_the_node_has_no_configuration()
    {
        using var manager = CreateNodeManager(typeof(double));
        var node = manager.GetNodeState(Channel);
        using var unconfigured = CreateVariable(new NodeId("unconfigured", 1));

        var result = Write(node, unconfigured, 3.4d);

        result.StatusCode.Code.Should().Be(StatusCodes.BadInternalError);
    }

    [Fact]
    public void Returns_bad_internal_error_if_the_node_id_is_not_text()
    {
        using var manager = CreateNodeManager(typeof(double));
        var node = manager.GetNodeState(Channel);
        using var numeric = CreateVariable(new NodeId(42u, 1));

        var result = Write(node, numeric, 3.4d);

        result.StatusCode.Code.Should().Be(StatusCodes.BadInternalError);
    }

    [Fact]
    public void Accepts_the_status_code_a_client_writes_with_the_value()
    {
        using var manager = CreateNodeManager(typeof(double));
        List<(string Channel, object Value, StatusCode StatusCode)> received = [];
        manager.ReceiveValue += write => received.Add((write.Channel, write.Value, write.StatusCode));

        var result = WriteThroughStack(
            manager.GetNodeState(Channel),
            new DataValue { Value = 3.4d, StatusCode = StatusCodes.BadCommunicationError, SourceTimestamp = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), });

        StatusCode.IsGood(result.StatusCode).Should().BeTrue();
        received.Should().Contain((Channel, 3.4d, new StatusCode(StatusCodes.BadCommunicationError)));
    }

    /// <summary>
    /// A read-only data point grants no CurrentWrite, so the stack refuses the write before it
    /// reaches the callback, and neither the value nor its status arrives.
    /// </summary>
    [Fact]
    public void Refuses_a_write_to_a_read_only_data_point()
    {
        using var manager = CreateNodeManager(typeof(double), readOnly: true);
        var received = 0;
        manager.ReceiveValue += _ => received++;

        var result = WriteThroughStack(manager.GetNodeState(Channel), new DataValue { Value = 3.4d, StatusCode = StatusCodes.BadCommunicationError, });

        result.StatusCode.Should().Be(new StatusCode(StatusCodes.BadNotWritable));
        received.Should().Be(0);
    }

    /// <summary>
    /// A server sets the server timestamp of the values it serves, so the stack refuses a write that
    /// carries one - and refuses the value with it. That is why no Server timestamp child is offered
    /// on the server in either direction.
    /// </summary>
    [Fact]
    public void Refuses_a_write_that_carries_a_server_timestamp()
    {
        using var manager = CreateNodeManager(typeof(double));
        var received = 0;
        manager.ReceiveValue += _ => received++;

        var result = WriteThroughStack(manager.GetNodeState(Channel), new DataValue { Value = 3.4d, ServerTimestamp = new DateTime(2026, 3, 4, 5, 6, 8, DateTimeKind.Utc), });

        result.StatusCode.Should().Be(new StatusCode(StatusCodes.BadWriteNotSupported));
        received.Should().Be(0);
    }

    /// <summary>
    /// The engine attributes the channel of an envelope child to its parent variable, so a client
    /// write is raised on the channels of the variable alone.
    /// </summary>
    [Fact]
    public void Raises_no_write_on_the_channel_of_an_envelope_child()
    {
        using var manager = CreateNodeManager(typeof(double), statusCodeChildChannel: SecondChannel);
        List<string> channels = [];
        manager.ReceiveValue += write => channels.Add(write.Channel);
        var node = manager.GetNodeState(Channel);

        Write(node, node, 3.4d);

        channels.Should().Equal(Channel);
    }

    private static ServiceResult Write(BaseDataVariableState variable, NodeState target, object value)
    {
        var timestamp = s_writeTimestamp;
        return Write(variable, target, value, ref timestamp);
    }

    private static ServiceResult Write(BaseDataVariableState variable, NodeState target, object value, ref DateTime timestamp)
    {
        StatusCode statusCode = StatusCodes.Good;

        return variable.OnWriteValue(CreateContext(), target, NumericRange.Empty, null, ref value, ref statusCode, ref timestamp);
    }

    // The stack refuses a variable without CurrentWrite, and a value that carries a server
    // timestamp, before the callback runs, so these writes go through the stack itself.
    private static ServiceResult WriteThroughStack(BaseDataVariableState variable, DataValue value)
        => variable.WriteAttribute(CreateContext(), Attributes.Value, NumericRange.Empty, value);

    private static SystemContext CreateContext()
        => new() { NamespaceUris = new NamespaceTable(), TypeTable = CreateTypeTable() };

    // The SDK asks the type tree whether the written value's data type is the node's data type, even
    // when the two are the same built-in type. Answering by identity is what a populated type tree
    // does for the scalar built-ins these nodes carry.
    private static ITypeTable CreateTypeTable()
    {
        var typeTable = Substitute.For<ITypeTable>();
        typeTable.IsTypeOf(Arg.Any<NodeId>(), Arg.Any<NodeId>()).Returns(call => call.ArgAt<NodeId>(0) == call.ArgAt<NodeId>(1));

        return typeTable;
    }

    private static BaseDataVariableState CreateVariable(NodeId nodeId)
        => new(null) { NodeId = nodeId, DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar };

    private static DataPortNodeManager CreateNodeManager(Type valueType, Property? minimum = null, Property? maximum = null, TimeProvider? timeProvider = null, bool readOnly = false, string? statusCodeChildChannel = null)
    {
        Node folder = new() { Id = Guid.NewGuid(), Name = "folder", DesignId = OpcUaServerNodeDesignId.Folder };
        Node variable = new()
        {
            Id = Guid.NewGuid(),
            ParentId = folder.Id,
            Name = "variable",
            DesignId = OpcUaServerNodeDesignId.Variable,
            ValueType = valueType,
            TransferredChannels = [Channel, SecondChannel],
            Properties = CreateProperties(minimum, maximum, readOnly),
        };
        List<INode> route = [folder, variable];

        if (statusCodeChildChannel is not null)
            route.Add(new Node { Id = Guid.NewGuid(), ParentId = variable.Id, Name = "quality", DesignId = OpcUaServerNodeDesignId.StatusCode, AffectedChannels = [statusCodeChildChannel] });

        var server = Substitute.For<IServerInternal>();
        server.NamespaceUris.Returns(new NamespaceTable());
        server.DefaultSystemContext.Returns(_ => new ServerSystemContext(server));

        DataPortNodeManager manager = new(server, new ApplicationConfiguration { ServerConfiguration = new() }, [route], "urn:test", timeProvider);
        manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

        return manager;
    }

    private static Dictionary<string, Property> CreateProperties(Property? minimum, Property? maximum, bool readOnly)
    {
        Dictionary<string, Property> properties = [];

        if (minimum is not null)
            properties.Add(OpcUaServerDataPortPropertyNames.Minimum, minimum);
        if (maximum is not null)
            properties.Add(OpcUaServerDataPortPropertyNames.Maximum, maximum);
        properties.Add(OpcUaServerDataPortPropertyNames.ReadOnly, new Property { Value = readOnly });

        return properties;
    }
}

public class DataPortNodeManager_CreateAddressSpace
{
    private const string ValueChannel = "value";
    private const string ChildChannel = "child";

    /// <summary>
    /// An envelope child addresses the value of its parent variable, so it is neither a node a
    /// client can browse to nor a channel the server publishes a value on. The engine attributes
    /// its channel to the transferring parent, which is what makes the second half necessary.
    /// </summary>
    [Theory]
    [InlineData(OpcUaServerNodeDesignId.StatusCode)]
    [InlineData(OpcUaServerNodeDesignId.SourceTimestamp)]
    [InlineData(EnvelopeNodeDesignId.ServerTimestamp)]
    public void Serves_the_variable_but_not_its_envelope_child(string childDesignId)
    {
        using var manager = CreateNodeManager(childDesignId, [ChildChannel]);

        var act = () => manager.GetNodeState(ChildChannel);

        manager.GetNodeState(ValueChannel).BrowseName.Name.Should().Be("variable");
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{ChildChannel}*");
    }

    /// <summary>
    /// A client checks every write against the data type a variable advertises, so a variable must
    /// advertise the built-in type its values are served as.
    /// </summary>
    [Theory]
    [InlineData(typeof(bool), DataTypes.Boolean)]
    [InlineData(typeof(sbyte), DataTypes.SByte)]
    [InlineData(typeof(byte), DataTypes.Byte)]
    [InlineData(typeof(short), DataTypes.Int16)]
    [InlineData(typeof(ushort), DataTypes.UInt16)]
    [InlineData(typeof(int), DataTypes.Int32)]
    [InlineData(typeof(uint), DataTypes.UInt32)]
    [InlineData(typeof(long), DataTypes.Int64)]
    [InlineData(typeof(ulong), DataTypes.UInt64)]
    [InlineData(typeof(float), DataTypes.Float)]
    [InlineData(typeof(double), DataTypes.Double)]
    [InlineData(typeof(string), DataTypes.String)]
    [InlineData(typeof(DateTime), DataTypes.DateTime)]
    [InlineData(typeof(byte[]), DataTypes.ByteString)]
    public void Advertises_the_data_type_of_the_value_type(Type valueType, uint dataType)
    {
        using var manager = CreateNodeManager(OpcUaServerNodeDesignId.StatusCode, [ChildChannel], valueType: valueType);

        manager.GetNodeState(ValueChannel).DataType.Should().Be(new NodeId(dataType));
    }

    /// <summary>
    /// A value type without a built-in counterpart is served as any type rather than as a
    /// <c>DataValue</c>, which no value written by a client ever is.
    /// </summary>
    [Fact]
    public void Advertises_the_base_data_type_for_a_value_type_without_a_built_in_type()
    {
        using var manager = CreateNodeManager(OpcUaServerNodeDesignId.StatusCode, [ChildChannel], valueType: typeof(Guid[]));

        manager.GetNodeState(ValueChannel).DataType.Should().Be(DataTypeIds.BaseDataType);
    }

    /// <summary>
    /// Both ports register their tree with the server, so a child linked both ways arrives with one
    /// channel per direction, and neither may be served as the value of its variable.
    /// </summary>
    [Fact]
    public void Serves_neither_channel_of_a_child_linked_both_ways()
    {
        using var manager = CreateNodeManager(OpcUaServerNodeDesignId.StatusCode, ["status-out", "status-in"]);

        var outbound = () => manager.GetNodeState("status-out");
        var inbound = () => manager.GetNodeState("status-in");

        outbound.Should().Throw<InvalidOperationException>();
        inbound.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// A client reads the access level to decide what it may write. The value of a writable data
    /// point may carry a status and a source timestamp, so both are advertised.
    /// </summary>
    [Fact]
    public void Advertises_that_a_status_and_a_source_timestamp_may_be_written()
    {
        using var manager = CreateNodeManager(OpcUaServerNodeDesignId.StatusCode, [ChildChannel]);

        var accessLevel = manager.GetNodeState(ValueChannel).AccessLevel;

        (accessLevel & AccessLevels.StatusWrite).Should().NotBe(0);
        (accessLevel & AccessLevels.TimestampWrite).Should().NotBe(0);
    }

    /// <summary>
    /// A read-only data point advertises no write at all.
    /// </summary>
    [Fact]
    public void Advertises_no_write_on_a_read_only_data_point()
    {
        using var manager = CreateNodeManager(OpcUaServerNodeDesignId.StatusCode, [ChildChannel], readOnly: true);

        var accessLevel = manager.GetNodeState(ValueChannel).AccessLevel;

        (accessLevel & AccessLevels.CurrentWrite).Should().Be(0);
        (accessLevel & AccessLevels.StatusWrite).Should().Be(0);
        (accessLevel & AccessLevels.TimestampWrite).Should().Be(0);
    }

    /// <summary>
    /// A configuration that carries no <c>Read only</c> property at all - a hand-written one, or one
    /// written before the property existed - is read-only too, so that the answer does not depend on
    /// which of the two the data point came from.
    /// </summary>
    [Fact]
    public void Advertises_no_write_on_a_data_point_without_the_read_only_property()
    {
        using var manager = CreateNodeManager(OpcUaServerNodeDesignId.StatusCode, [ChildChannel], readOnly: null);

        var accessLevel = manager.GetNodeState(ValueChannel).AccessLevel;

        (accessLevel & AccessLevels.CurrentWrite).Should().Be(0);
        (accessLevel & AccessLevels.StatusWrite).Should().Be(0);
        (accessLevel & AccessLevels.TimestampWrite).Should().Be(0);
    }

    /// <param name="childDesignId">The design of the envelope child below the data point.</param>
    /// <param name="childChannels">The channels the envelope child is linked to.</param>
    /// <param name="readOnly">
    /// The value of the <c>Read only</c> property, or <c>null</c> for a data point that does not
    /// carry the property at all.
    /// </param>
    /// <param name="valueType">The value type of the data point, <see cref="double"/> when omitted.</param>
    private static DataPortNodeManager CreateNodeManager(string childDesignId, List<string> childChannels, bool? readOnly = false, Type? valueType = null)
    {
        Node folder = new() { Id = Guid.NewGuid(), Name = "folder", DesignId = OpcUaServerNodeDesignId.Folder };
        Node variable = new()
        {
            Id = Guid.NewGuid(),
            ParentId = folder.Id,
            Name = "variable",
            DesignId = OpcUaServerNodeDesignId.Variable,
            ValueType = valueType ?? typeof(double),
            AffectedChannels = [ValueChannel],
            TransferredChannels = [ValueChannel, .. childChannels],
            Properties = readOnly is { } value
                ? new() { { OpcUaServerDataPortPropertyNames.ReadOnly, new Property { Value = value } } }
                : [],
        };
        Node child = new()
        {
            Id = Guid.NewGuid(),
            ParentId = variable.Id,
            Name = "child",
            DesignId = childDesignId,
            AffectedChannels = childChannels,
        };

        var server = Substitute.For<IServerInternal>();
        server.NamespaceUris.Returns(new NamespaceTable());
        server.DefaultSystemContext.Returns(_ => new ServerSystemContext(server));

        DataPortNodeManager manager = new(server, new ApplicationConfiguration { ServerConfiguration = new() }, [[folder, variable, child]], "urn:test");
        manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

        return manager;
    }
}
