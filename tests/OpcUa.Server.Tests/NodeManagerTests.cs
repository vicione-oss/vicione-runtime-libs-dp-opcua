using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using NSubstitute;
using Opc.Ua;
using Opc.Ua.Server;
using Xunit;

namespace ViciOne.Suite.DataPort;

internal static class SingleVariableNodeManager
{
    internal const string Channel = "value";

    internal static DataPortNodeManager Create()
    {
        Node folder = new() { Id = Guid.NewGuid(), Name = "folder", DesignId = OpcUaServerNodeDesignId.Folder };
        Node variable = new()
        {
            Id = Guid.NewGuid(),
            ParentId = folder.Id,
            Name = "variable",
            DesignId = OpcUaServerNodeDesignId.Variable,
            ValueType = typeof(double),
            TransferredChannels = [Channel],
        };

        var server = Substitute.For<IServerInternal>();
        server.NamespaceUris.Returns(new NamespaceTable());
        server.DefaultSystemContext.Returns(_ => new ServerSystemContext(server));

        DataPortNodeManager manager = new(server, new ApplicationConfiguration { ServerConfiguration = new() }, [[folder, variable]], "urn:test");
        manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

        return manager;
    }
}

public class NodeManager_WriteVariableValueAsync
{
    private static readonly DateTime s_timestamp = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    [Fact]
    public async Task Serves_a_linked_status_code_as_it_is_even_on_a_value_that_is_not_a_number_Async()
    {
        using var manager = SingleVariableNodeManager.Create();
        var variable = manager.GetNodeState(SingleVariableNodeManager.Channel);

        await manager.WriteVariableValueAsync(variable, double.NaN, s_timestamp, StatusCodes.Good, false);

        variable.StatusCode.Should().Be(new StatusCode(StatusCodes.Good));
    }

    /// <summary>
    /// A variable starts out waiting for its initial data, which the first value clears. A linked
    /// status that says so anyway is served as it is.
    /// </summary>
    [Fact]
    public async Task Keeps_a_linked_status_code_the_value_would_otherwise_clear_Async()
    {
        using var manager = SingleVariableNodeManager.Create();
        var variable = manager.GetNodeState(SingleVariableNodeManager.Channel);

        await manager.WriteVariableValueAsync(variable, 3.4d, s_timestamp, StatusCodes.BadWaitingForInitialData, false);

        variable.StatusCode.Should().Be(new StatusCode(StatusCodes.BadWaitingForInitialData));
    }

    [Fact]
    public async Task Serves_the_first_value_as_good_when_no_status_code_is_linked_Async()
    {
        using var manager = SingleVariableNodeManager.Create();
        var variable = manager.GetNodeState(SingleVariableNodeManager.Channel);

        await manager.WriteVariableValueAsync(variable, 3.4d, s_timestamp, null, false);

        variable.StatusCode.Should().Be(new StatusCode(StatusCodes.Good));
    }

    /// <summary>
    /// Not a number is a value of a float and a double like any other. A Bad status would make the
    /// stack serve it as no value at all.
    /// </summary>
    [Fact]
    public async Task Serves_a_value_that_is_not_a_number_as_good_when_no_status_code_is_linked_Async()
    {
        using var manager = SingleVariableNodeManager.Create();
        var variable = manager.GetNodeState(SingleVariableNodeManager.Channel);

        await manager.WriteVariableValueAsync(variable, double.NaN, s_timestamp, null, false);

        variable.StatusCode.Should().Be(new StatusCode(StatusCodes.Good));
        variable.Value.Should().Be(double.NaN);
    }
}

public class NodeManager_UpdateVariableStateAsync
{
    [Fact]
    public async Task Serves_a_linked_status_code_as_it_is_even_on_a_value_that_is_not_a_number_Async()
    {
        using var manager = SingleVariableNodeManager.Create();
        var variable = manager.GetNodeState(SingleVariableNodeManager.Channel);
        await manager.WriteVariableValueAsync(variable, double.NaN, new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), null, false);

        await manager.UpdateVariableStateAsync(variable, StatusCodes.Good);

        variable.StatusCode.Should().Be(new StatusCode(StatusCodes.Good));
    }
}
