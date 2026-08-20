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
        List<(string Channel, DateTime Timestamp, object Value)> received = [];
        manager.ReceiveValue += (channel, timestamp, value) => received.Add((channel, timestamp, value));
        var node = manager.GetNodeState(Channel);

        var result = Write(node, node, 3.4d);

        result.StatusCode.Code.Should().Be(StatusCodes.Good);
        received.Should().BeEquivalentTo([(Channel, s_writeTimestamp, 3.4d), (SecondChannel, s_writeTimestamp, 3.4d)]);
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

    private static ServiceResult Write(BaseDataVariableState variable, NodeState target, object value)
    {
        var timestamp = s_writeTimestamp;
        return Write(variable, target, value, ref timestamp);
    }

    private static ServiceResult Write(BaseDataVariableState variable, NodeState target, object value, ref DateTime timestamp)
    {
        SystemContext context = new() { NamespaceUris = new NamespaceTable(), TypeTable = CreateTypeTable() };
        StatusCode statusCode = StatusCodes.Good;

        return variable.OnWriteValue(context, target, NumericRange.Empty, null, ref value, ref statusCode, ref timestamp);
    }

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

    private static DataPortNodeManager CreateNodeManager(Type valueType, Property? minimum = null, Property? maximum = null, TimeProvider? timeProvider = null)
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
            Properties = CreateLimits(minimum, maximum),
        };

        var server = Substitute.For<IServerInternal>();
        server.NamespaceUris.Returns(new NamespaceTable());
        server.DefaultSystemContext.Returns(_ => new ServerSystemContext(server));

        DataPortNodeManager manager = new(server, new ApplicationConfiguration { ServerConfiguration = new() }, [[folder, variable]], "urn:test", timeProvider);
        manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

        return manager;
    }

    private static Dictionary<string, Property> CreateLimits(Property? minimum, Property? maximum)
    {
        Dictionary<string, Property> limits = [];

        if (minimum is not null)
            limits.Add(OpcUaServerDataPortPropertyNames.Minimum, minimum);
        if (maximum is not null)
            limits.Add(OpcUaServerDataPortPropertyNames.Maximum, maximum);

        return limits;
    }
}
