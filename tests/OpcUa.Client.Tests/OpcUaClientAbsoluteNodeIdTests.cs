using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// Drives a data port against a real server that answers a browse with an absolute node id, the form an aggregating
/// server uses for references into another address space.
/// </summary>
[Trait("Category", "Interoperability")]
public sealed class OpcUaClient_AbsoluteNodeIds
{
    [Fact]
    public async Task Subscribes_to_a_node_that_is_browsed_under_an_absolute_node_id_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.AbsoluteReference = true);

        var rootFolderId = Guid.Parse("6c6a52ba-3b1b-4f0a-9a2f-2c2e8bd0f7a1");
        var sourceFolderId = Guid.Parse("0f3d0ab0-1f4d-4a9a-8f2c-9b6f0a3d5c72");

        IReadOnlyCollection<Node> nodes =
        [
            new()
            {
                Id = rootFolderId,
                DesignId = OpcUaClientNodeDesignId.Folder,
                Name = OpcUaTestNodeManager.RootFolderName,
            },
            new()
            {
                Id = sourceFolderId,
                ParentId = rootFolderId,
                DesignId = OpcUaClientNodeDesignId.Folder,
                Name = OpcUaTestNodeManager.AbsoluteReferenceSourceName,
            },
            new()
            {
                Id = Guid.Parse("4a3f8e1d-5c2b-4d6e-9f0a-7b8c1d2e3f40"),
                ParentId = sourceFolderId,
                DesignId = OpcUaClientNodeDesignId.Variable,
                AffectedChannels = ["channel",],
                Name = OpcUaTestNodeManager.AbsoluteReferenceTargetName,
            },
        ];

        var communication = opcUa.Communication with { Nodes = nodes, };
        await using OpcUaClientInstanceManager instanceManager = new();
        OpcUaClientDataPortIncoming dataPort = new(communication, NullLogger<IOpcUaClient>.Instance, instanceManager);

        try
        {
            await dataPort.Awaiting(port => port.ConnectAsync(TestContext.Current.CancellationToken)).Should().NotThrowAsync();
        }
        finally
        {
            await dataPort.DisconnectAsync(TestContext.Current.CancellationToken);
        }
    }
}
