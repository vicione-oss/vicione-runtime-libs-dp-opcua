using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using NSubstitute;
using Opc.Ua;
using Opc.Ua.Server;
using Xunit;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// Several engines register their data ports with one server, and each of them is deployed and
/// redeployed while the others keep the server running.
/// </summary>
public class DataPortNodeManager_Apply
{
    private const string Channel = "value";

    private static readonly DateTime s_timestamp = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    private readonly object _running = new();
    private readonly object _deployed = new();
    private readonly MasterNodeManager _masterNodeManager;
    private readonly IServerInternal _server;

    public DataPortNodeManager_Apply()
    {
        _server = Substitute.For<IServerInternal>();
        _server.NamespaceUris.Returns(new NamespaceTable());
        _server.ServerUris.Returns(new StringTable());
        _server.DefaultSystemContext.Returns(_ => new ServerSystemContext(_server));
        _masterNodeManager = Substitute.ForPartsOf<MasterNodeManager>(_server, new ApplicationConfiguration { ServerConfiguration = new() }, null, Array.Empty<INodeManager>());
        _server.NodeManager.Returns(_masterNodeManager);
    }

    [Fact]
    public void Serves_the_nodes_of_a_data_port_registered_while_the_server_runs()
    {
        var layout = AddressSpaceLayout.Empty.With(_running, TestTree.Create("running").WithChannels(Channel));
        using var manager = CreateNodeManager(layout);

        manager.Apply(layout.With(_deployed, TestTree.Create("deployed").WithChannels(Channel)));

        manager.Find(NodeIdOf(manager, "deployed.variable")).Should().BeSameAs(manager.GetNodeState(_deployed, Channel));
        _masterNodeManager.Received(1).AddReferences(ObjectIds.ObjectsFolder, Arg.Is<IList<IReference>>(references =>
            references.Count == 1 && (NodeId)references[0].TargetId == NodeIdOf(manager, "deployed") && !references[0].IsInverse));
    }

    /// <summary>
    /// A client monitoring the nodes of the running engine keeps its items, and the value they
    /// last reported, while another engine is deployed beside it.
    /// </summary>
    [Fact]
    public async Task Keeps_the_variables_of_the_other_data_ports_as_they_are_Async()
    {
        var layout = AddressSpaceLayout.Empty.With(_running, TestTree.Create("running").WithChannels(Channel));
        using var manager = CreateNodeManager(layout);
        var variable = manager.GetNodeState(_running, Channel);
        await manager.WriteVariableValueAsync(variable, 3.4d, s_timestamp, null, false);

        manager.Apply(layout.With(_deployed, TestTree.Create("deployed").WithChannels(Channel)));

        manager.GetNodeState(_running, Channel).Should().BeSameAs(variable);
        manager.Find(NodeIdOf(manager, "running.variable")).Should().BeSameAs(variable);
        variable.Value.Should().Be(3.4d);
        variable.StatusCode.Should().Be(new StatusCode(StatusCodes.Good));
    }

    [Fact]
    public void Adds_the_variable_of_a_data_port_to_a_folder_another_one_serves()
    {
        var layout = AddressSpaceLayout.Empty.With(_running, TestTree.Create("plant", "running").WithChannels(Channel));
        using var manager = CreateNodeManager(layout);

        manager.Apply(layout.With(_deployed, TestTree.Create("plant", "deployed").WithChannels(Channel)));

        var plant = manager.Find(NodeIdOf(manager, "plant"));
        plant.FindChild(manager.SystemContext, new QualifiedName("deployed", manager.NamespaceIndex)).Should().NotBeNull();
        manager.Find(NodeIdOf(manager, "plant.deployed.variable")).Should().BeSameAs(manager.GetNodeState(_deployed, Channel));
        _masterNodeManager.DidNotReceiveWithAnyArgs().AddReferences(default!, default!);
    }

    [Fact]
    public void Removes_the_nodes_of_a_released_data_port()
    {
        var layout = AddressSpaceLayout.Empty
            .With(_running, TestTree.Create("running").WithChannels(Channel))
            .With(_deployed, TestTree.Create("deployed").WithChannels(Channel));
        using var manager = CreateNodeManager(layout);

        manager.Apply(layout.Without(_deployed));

        var released = () => manager.GetNodeState(_deployed, Channel);
        manager.Find(NodeIdOf(manager, "deployed")).Should().BeNull();
        manager.Find(NodeIdOf(manager, "deployed.variable")).Should().BeNull();
        released.Should().Throw<InvalidOperationException>();
        manager.Find(NodeIdOf(manager, "running.variable")).Should().BeSameAs(manager.GetNodeState(_running, Channel));
    }

    [Fact]
    public void Keeps_a_folder_another_data_port_still_serves()
    {
        var layout = AddressSpaceLayout.Empty
            .With(_running, TestTree.Create("plant", "running").WithChannels(Channel))
            .With(_deployed, TestTree.Create("plant", "deployed").WithChannels(Channel));
        using var manager = CreateNodeManager(layout);

        manager.Apply(layout.Without(_deployed));

        var plant = manager.Find(NodeIdOf(manager, "plant"));
        plant.Should().NotBeNull();
        plant.FindChild(manager.SystemContext, new QualifiedName("deployed", manager.NamespaceIndex)).Should().BeNull();
        manager.Find(NodeIdOf(manager, "plant.deployed")).Should().BeNull();
        manager.Find(NodeIdOf(manager, "plant.running.variable")).Should().NotBeNull();
    }

    /// <summary>
    /// The stack keeps the items a client monitors on a deleted node and goes on reporting the
    /// last value it had, as if the data point were still being served.
    /// </summary>
    [Fact]
    public async Task Reports_a_removed_variable_as_unknown_Async()
    {
        var layout = AddressSpaceLayout.Empty
            .With(_running, TestTree.Create("running").WithChannels(Channel))
            .With(_deployed, TestTree.Create("deployed").WithChannels(Channel));
        using var manager = CreateNodeManager(layout);
        var variable = manager.GetNodeState(_deployed, Channel);
        await manager.WriteVariableValueAsync(variable, 3.4d, s_timestamp, null, false);

        manager.Apply(layout.Without(_deployed));

        variable.StatusCode.Should().Be(new StatusCode(StatusCodes.BadNodeIdUnknown));
    }

    /// <summary>
    /// Redeploying an engine whose data point changed releases the old one before the new one
    /// takes its path, and the new one is served as declared.
    /// </summary>
    [Fact]
    public void Serves_a_data_point_that_takes_the_path_of_a_released_one_as_declared()
    {
        var layout = AddressSpaceLayout.Empty
            .With(_running, TestTree.Create("running").WithChannels(Channel))
            .With(_deployed, TestTree.Create("deployed").WithChannels(Channel));
        using var manager = CreateNodeManager(layout);
        var released = manager.GetNodeState(_deployed, Channel);
        Node folder = new() { Id = Guid.NewGuid(), Name = "deployed", DesignId = OpcUaServerNodeDesignId.Folder };
        Node redeployed = new()
        {
            Id = Guid.NewGuid(),
            ParentId = folder.Id,
            Name = "variable",
            DesignId = OpcUaServerNodeDesignId.Variable,
            ValueType = typeof(int),
            TransferredChannels = [Channel],
        };
        var redeployedPort = new object();

        manager.Apply(layout.Without(_deployed));
        manager.Apply(layout.Without(_deployed).With(redeployedPort, [folder, redeployed]));

        var variable = manager.GetNodeState(redeployedPort, Channel);
        variable.Should().NotBeSameAs(released);
        variable.DataType.Should().Be(DataTypeIds.Int32);
        manager.Find(NodeIdOf(manager, "deployed.variable")).Should().BeSameAs(variable);
    }

    [Fact]
    public void Raises_no_client_write_for_a_released_data_port_that_shared_the_variable()
    {
        var tree = TestTree.Create("master");
        var layout = AddressSpaceLayout.Empty
            .With(_running, tree.WithChannels("in"))
            .With(_deployed, tree.WithChannels("out"));
        using var manager = CreateNodeManager(layout);
        var variable = manager.GetNodeState(_running, "in");

        manager.Apply(layout.Without(_deployed));
        List<string> received = [];
        manager.ReceiveValue += write => received.Add(write.Channel);
        Write(variable, 3.4d);

        manager.GetNodeState(_running, "in").Should().BeSameAs(variable);
        received.Should().Equal("in");
    }

    private static NodeId NodeIdOf(DataPortNodeManager manager, string path)
        => new(path, manager.NamespaceIndex);

    private static void Write(BaseDataVariableState variable, object value)
    {
        StatusCode statusCode = StatusCodes.Good;
        var timestamp = s_timestamp;
        SystemContext context = new() { NamespaceUris = new NamespaceTable(), TypeTable = CreateTypeTable() };

        variable.OnWriteValue(context, variable, NumericRange.Empty, null, ref value, ref statusCode, ref timestamp);
    }

    // The SDK asks the type tree whether the written value's data type is the node's data type.
    private static ITypeTable CreateTypeTable()
    {
        var typeTable = Substitute.For<ITypeTable>();
        typeTable.IsTypeOf(Arg.Any<NodeId>(), Arg.Any<NodeId>()).Returns(call => call.ArgAt<NodeId>(0) == call.ArgAt<NodeId>(1));

        return typeTable;
    }

    private DataPortNodeManager CreateNodeManager(AddressSpaceLayout layout)
    {
        DataPortNodeManager manager = new(_server, new ApplicationConfiguration { ServerConfiguration = new() }, layout, "urn:test");
        manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

        return manager;
    }
}
