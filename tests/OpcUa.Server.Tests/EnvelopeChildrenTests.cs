using System;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace ViciOne.Suite.DataPort;

internal static class EnvelopeTree
{
    internal static Node Folder(string name)
        => new()
        {
            Id = Guid.NewGuid(),
            DesignId = OpcUaServerNodeDesignId.Folder,
            Name = name,
        };

    internal static Node DataPoint(string name, string channel, params string[] childChannels)
        => new()
        {
            Id = Guid.NewGuid(),
            DesignId = OpcUaServerNodeDesignId.Variable,
            Name = name,
            ValueType = typeof(double),
            AffectedChannels = [channel],
            TransferredChannels = [channel, .. childChannels],
        };

    internal static Node Child(Node parent, string designId, string name, string? channel = null)
        => new()
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            DesignId = designId,
            Name = name,
            AffectedChannels = channel is null ? [] : [channel],
        };
}

public class EnvelopeChildren_Create
{
    [Fact]
    public void Maps_a_served_child_to_its_own_channel_and_to_the_channel_of_its_parent()
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "status");
        var child = EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality", "status");

        var children = EnvelopeChildren.Create([parent, child], [EnvelopeChildKind.StatusCode]);

        children.TryGetChild("status", out var resolved).Should().BeTrue();
        resolved.Kind.Should().Be(EnvelopeChildKind.StatusCode);
        resolved.Channel.Should().Be("status");
        resolved.ParentChannel.Should().Be("value");
        children.Of("value").Should().ContainSingle().Which.Should().Be(resolved);
    }

    /// <summary>
    /// A data port is handed the whole tree, including kinds it has no field for, such as a Server
    /// timestamp on the outgoing side of the client. A child the port cannot serve is passed over
    /// rather than refused.
    /// </summary>
    [Fact]
    public void Passes_over_a_child_of_a_kind_the_port_does_not_serve()
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "status");
        var child = EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality", "status");

        var children = EnvelopeChildren.Create([parent, child], [EnvelopeChildKind.SourceTimestamp]);

        children.TryGetChild("status", out _).Should().BeFalse();
        children.Of("value").Should().BeEmpty();
    }

    [Fact]
    public void Accepts_a_tree_without_envelope_children()
        => EnvelopeChildren.Create([EnvelopeTree.DataPoint("temperature", "value")], [EnvelopeChildKind.StatusCode])
            .Of("value").Should().BeEmpty();

    [Fact]
    public void Rejects_a_child_whose_parent_is_not_part_of_the_data_port()
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "status");
        var child = EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality", "status");

        var act = () => EnvelopeChildren.Create([child], [EnvelopeChildKind.StatusCode]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'quality' is an envelope child of a node that is not part of this data port*");
    }

    [Fact]
    public void Rejects_a_child_of_a_child()
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "status", "status-of-status");
        var child = EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality", "status");
        var grandChild = EnvelopeTree.Child(child, OpcUaServerNodeDesignId.StatusCode, "quality of quality", "status-of-status");

        var act = () => EnvelopeChildren.Create([parent, child, grandChild], [EnvelopeChildKind.StatusCode]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'quality' is an envelope child and cannot have envelope children of its own*");
    }

    /// <summary>
    /// A data point in a folder group transfers no channel of its own — its folder does — so there
    /// is no value of its own for a child to describe.
    /// </summary>
    [Fact]
    public void Rejects_a_parent_that_transfers_no_value_of_its_own()
    {
        Node parent = new()
        {
            Id = Guid.NewGuid(),
            DesignId = OpcUaServerNodeDesignId.Variable,
            Name = "temperature",
            AffectedChannels = ["value"],
            TransferredChannels = ["status"],
        };
        var child = EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality", "status");

        var act = () => EnvelopeChildren.Create([parent, child], [EnvelopeChildKind.StatusCode]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'temperature' has envelope children but transfers no value of its own*");
    }

    [Theory]
    [InlineData(OpcUaServerNodeDesignId.StatusCode)]
    [InlineData(OpcUaServerNodeDesignId.SourceTimestamp)]
    public void Rejects_more_than_one_child_of_the_same_kind(string designId)
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "first", "second");

        var act = () => EnvelopeChildren.Create(
            [
                parent,
                EnvelopeTree.Child(parent, designId, "one", "first"),
                EnvelopeTree.Child(parent, designId, "two", "second"),
            ],
            [EnvelopeChildKind.StatusCode, EnvelopeChildKind.SourceTimestamp]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'temperature' has more than one '{designId}' envelope child*");
    }

    /// <summary>
    /// A port is handed the whole tree, with channels filled in only for its own direction, so a
    /// data point linked the other way arrives here with no channel on itself or on its children.
    /// Refusing it would stop the other direction of the same data port.
    /// </summary>
    [Fact]
    public void Passes_over_a_parent_that_is_not_linked_in_this_direction()
    {
        Node parent = new()
        {
            Id = Guid.NewGuid(),
            DesignId = OpcUaServerNodeDesignId.Variable,
            Name = "temperature",
            ValueType = typeof(double),
        };

        var act = () => EnvelopeChildren.Create(
            [parent, EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality")],
            [EnvelopeChildKind.StatusCode]);

        act.Should().NotThrow().Which.Of(string.Empty).Should().BeEmpty();
    }

    /// <summary>
    /// A child the engine linked nothing to exchanges no value, which is a node too many rather
    /// than a tree the port has to refuse to start on.
    /// </summary>
    [Fact]
    public void Passes_over_a_child_the_engine_linked_no_channel_to()
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value");
        var child = EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality");

        var children = EnvelopeChildren.Create([parent, child], [EnvelopeChildKind.StatusCode]);

        children.Of("value").Should().BeEmpty();
    }

    [Fact]
    public void Rejects_two_children_on_the_same_channel()
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "shared");

        var act = () => EnvelopeChildren.Create(
            [
                parent,
                EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality", "shared"),
                EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.SourceTimestamp, "sent", "shared"),
            ],
            [EnvelopeChildKind.StatusCode, EnvelopeChildKind.SourceTimestamp]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'quality' and 'sent' are envelope children of the same channel*");
    }

    /// <summary>
    /// Every violation is reported together, so a wrong tree is fixed in one pass rather than one
    /// message at a time.
    /// </summary>
    [Fact]
    public void Reports_every_violation_at_once()
    {
        var grouped = EnvelopeTree.DataPoint("temperature", "value", "status");
        var orphaned = EnvelopeTree.DataPoint("pressure", "other", "orphan-status");

        var act = () => EnvelopeChildren.Create(
            [
                grouped,
                EnvelopeTree.Child(grouped, OpcUaServerNodeDesignId.StatusCode, "quality", "status"),
                EnvelopeTree.Child(grouped, OpcUaServerNodeDesignId.StatusCode, "quality again", "status"),
                EnvelopeTree.Child(orphaned, OpcUaServerNodeDesignId.StatusCode, "orphan", "orphan-status"),
            ],
            [EnvelopeChildKind.StatusCode]);

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;

        message.Should().StartWith("The OPC UA data port cannot be started with this configuration:");
        message.Should().Contain("'temperature' has more than one 'StatusCode' envelope child")
            .And.Contain("'quality' and 'quality again' are envelope children of the same channel")
            .And.Contain("'orphan' is an envelope child of a node that is not part of this data port");
    }
}

public class EnvelopeChildren_Of
{
    [Fact]
    public void Returns_the_children_of_a_parent_channel_in_the_order_the_tree_declares_them()
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "status", "sent");

        var children = EnvelopeChildren.Create(
            [
                parent,
                EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality", "status"),
                EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.SourceTimestamp, "when", "sent"),
            ],
            [EnvelopeChildKind.StatusCode, EnvelopeChildKind.SourceTimestamp]);

        children.Of("value").Select(child => child.Node.Name).Should().Equal("quality", "when");
    }

    [Fact]
    public void Returns_nothing_for_a_channel_without_children()
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "status");

        var children = EnvelopeChildren.Create(
            [
                parent,
                EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality", "status"),
                EnvelopeTree.DataPoint("pressure", "other"),
            ],
            [EnvelopeChildKind.StatusCode]);

        children.Of("other").Should().BeEmpty();
    }
}

public class EnvelopeChildren_TryGetChild
{
    /// <summary>
    /// The engine mints a connector per direction, so a child a tree links both ways owns one
    /// channel per direction, and each of them belongs to the child rather than to its parent.
    /// </summary>
    [Fact]
    public void Maps_every_channel_a_child_is_linked_on()
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "status-out", "status-in");
        Node child = new()
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            DesignId = OpcUaServerNodeDesignId.StatusCode,
            Name = "quality",
            AffectedChannels = ["status-out", "status-in"],
        };

        var children = EnvelopeChildren.Create([parent, child], [EnvelopeChildKind.StatusCode]);

        children.IsChildChannel("status-out").Should().BeTrue();
        children.IsChildChannel("status-in").Should().BeTrue();
        children.IsChildChannel("value").Should().BeFalse();
    }

    [Fact]
    public void Does_not_find_the_channel_of_the_parent()
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "status");
        var child = EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.StatusCode, "quality", "status");

        var children = EnvelopeChildren.Create([parent, child], [EnvelopeChildKind.StatusCode]);

        children.TryGetChild("value", out _).Should().BeFalse();
        children.IsChildChannel("value").Should().BeFalse();
        children.IsChildChannel("status").Should().BeTrue();
    }
}

public class EnvelopeChildren_IsEnvelopeChild
{
    [Theory]
    [InlineData(OpcUaServerNodeDesignId.StatusCode, true)]
    [InlineData(OpcUaServerNodeDesignId.SourceTimestamp, true)]
    [InlineData(EnvelopeNodeDesignId.ServerTimestamp, true)]
    [InlineData(OpcUaServerNodeDesignId.Variable, false)]
    [InlineData(OpcUaServerNodeDesignId.Folder, false)]
    [InlineData(null, false)]
    public void Recognises_a_node_that_addresses_the_envelope_of_its_parent(string? designId, bool isChild)
        => EnvelopeChildren.IsEnvelopeChild(designId).Should().Be(isChild);
}
