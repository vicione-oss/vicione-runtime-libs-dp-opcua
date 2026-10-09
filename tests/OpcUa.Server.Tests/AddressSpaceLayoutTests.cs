using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Xunit;
using static ViciOne.Suite.DataPort.AddressSpaceLayoutAssertions;

namespace ViciOne.Suite.DataPort;

public class AddressSpaceLayout_With
{
    private readonly object _incoming = new();
    private readonly object _outgoing = new();

    /// <summary>
    /// The incoming and the outgoing data port of an engine each register their copy of the same
    /// tree, so a data point arrives twice under one id and is served as one variable.
    /// </summary>
    [Fact]
    public void Serves_the_data_point_of_both_ports_of_one_configuration_as_one_variable()
    {
        var tree = TestTree.Create("master");

        var layout = AddressSpaceLayout.Empty
            .With(_incoming, tree.WithChannels("in"))
            .With(_outgoing, tree.WithChannels("out"));

        layout.Nodes.Select(node => node.Path).Should().Equal("master", "master.variable");
        var variable = NodeAt(layout, "master.variable");
        variable.Channels.Should().BeEquivalentTo(new Dictionary<object, List<string>>
        {
            { _incoming, ["in"] },
            { _outgoing, ["out"] },
        });
    }

    /// <summary>
    /// A folder only groups what is below it, so two configurations may both put their data
    /// points into a folder of the same name.
    /// </summary>
    [Fact]
    public void Shares_a_folder_between_two_configurations()
    {
        var first = TestTree.Create("plant", "first");
        var second = TestTree.Create("plant", "second");

        var layout = AddressSpaceLayout.Empty
            .With(_incoming, first.WithChannels("first"))
            .With(_outgoing, second.WithChannels("second"));

        layout.Nodes.Select(node => node.Path).Should().Equal("plant", "plant.first", "plant.first.variable", "plant.second", "plant.second.variable");
    }

    /// <summary>
    /// Serving the data points of two configurations from one variable would mix their values, so
    /// a data point that lands on the path of another one is refused.
    /// </summary>
    [Fact]
    public void Refuses_a_data_point_at_the_path_of_a_data_point_of_another_configuration()
    {
        var layout = AddressSpaceLayout.Empty.With(_incoming, TestTree.Create("master").WithChannels("first"));

        var act = () => layout.With(_outgoing, TestTree.Create("master").WithChannels("second"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*'master.variable'*already served*");
    }

    [Fact]
    public void Refuses_a_folder_at_the_path_of_a_data_point()
    {
        var layout = AddressSpaceLayout.Empty.With(_incoming, TestTree.Create("master").WithChannels("value"));
        Node folder = new() { Id = Guid.NewGuid(), Name = "master", DesignId = OpcUaServerNodeDesignId.Folder };
        Node variableAsFolder = new() { Id = Guid.NewGuid(), ParentId = folder.Id, Name = "variable", DesignId = OpcUaServerNodeDesignId.Folder };

        var act = () => layout.With(_outgoing, [folder, variableAsFolder]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'master.variable'*folder*data point*");
    }

    [Fact]
    public void Leaves_the_layout_unchanged_when_it_refuses_a_data_port()
    {
        var layout = AddressSpaceLayout.Empty.With(_incoming, TestTree.Create("master").WithChannels("first"));

        var act = () => layout.With(_outgoing, TestTree.Create("master").WithChannels("second"));

        act.Should().Throw<InvalidOperationException>();
        var variable = NodeAt(layout, "master.variable");
        variable.Channels.Keys.Should().Equal(_incoming);
    }

    /// <summary>
    /// A channel of a data port carries the values of one variable; served from two, the values
    /// would go to whichever of them came last.
    /// </summary>
    [Fact]
    public void Refuses_a_channel_transferred_by_two_data_points_of_one_data_port()
    {
        Node folder = new() { Id = Guid.NewGuid(), Name = "master", DesignId = OpcUaServerNodeDesignId.Folder };

        var act = () => AddressSpaceLayout.Empty.With(_incoming,
        [
            folder,
            TestTree.Variable(folder, "first", "value"),
            TestTree.Variable(folder, "second", "value"),
        ]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'value'*'master.first'*'master.second'*");
    }

    [Fact]
    public void Serves_a_channel_a_data_point_names_twice_once()
    {
        var layout = AddressSpaceLayout.Empty.With(_incoming, TestTree.Create("master").WithChannels("value", "value"));

        var variable = NodeAt(layout, "master.variable");
        variable.Channels[_incoming].Should().Equal("value");
    }

    /// <summary>
    /// Every engine names its own channels, so two of them may well use the same name for
    /// variables of their own.
    /// </summary>
    [Fact]
    public void Serves_the_same_channel_of_two_data_ports_from_variables_of_their_own()
    {
        var layout = AddressSpaceLayout.Empty
            .With(_incoming, TestTree.Create("first").WithChannels("value"))
            .With(_outgoing, TestTree.Create("second").WithChannels("value"));

        var first = NodeAt(layout, "first.variable");
        var second = NodeAt(layout, "second.variable");
        first.Channels.Keys.Should().Equal(_incoming);
        second.Channels.Keys.Should().Equal(_outgoing);
    }
}

public class AddressSpaceLayout_Without
{
    private readonly object _released = new();
    private readonly object _remaining = new();

    [Fact]
    public void Serves_only_the_nodes_of_the_data_ports_that_remain()
    {
        var layout = AddressSpaceLayout.Empty
            .With(_released, TestTree.Create("plant", "released").WithChannels("value"))
            .With(_remaining, TestTree.Create("plant", "remaining").WithChannels("value"));

        var released = layout.Without(_released);

        released.Nodes.Select(node => node.Path).Should().Equal("plant", "plant.remaining", "plant.remaining.variable");
    }

    [Fact]
    public void Keeps_a_variable_another_port_of_the_configuration_still_serves()
    {
        var tree = TestTree.Create("master");
        var layout = AddressSpaceLayout.Empty
            .With(_released, tree.WithChannels("in"))
            .With(_remaining, tree.WithChannels("out"));

        var released = layout.Without(_released);

        var variable = NodeAt(released, "master.variable");
        variable.Channels.Keys.Should().Equal(_remaining);
    }

    /// <summary>
    /// Once a data point is released, its path is free for another configuration, which is what
    /// redeploying an engine whose data point changed comes down to.
    /// </summary>
    [Fact]
    public void Frees_the_path_of_a_released_data_point()
    {
        var layout = AddressSpaceLayout.Empty
            .With(_released, TestTree.Create("master").WithChannels("value"))
            .Without(_released);

        var act = () => layout.With(_remaining, TestTree.Create("master").WithChannels("value"));

        act.Should().NotThrow();
    }
}

internal static class AddressSpaceLayoutAssertions
{
    /// <summary>
    /// Asserts that <paramref name="layout"/> serves a node at <paramref name="path"/>, and returns it.
    /// </summary>
    internal static ServedNode NodeAt(AddressSpaceLayout layout, string path)
        => layout.Nodes.Should().ContainSingle(node => node.Path == path).Which;
}
