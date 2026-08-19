using System;
using System.Collections.Generic;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaNode
{
    internal required NodeId NodeId { get; init; }
    internal required string DisplayName { get; init; }
    internal IReadOnlyCollection<OpcUaNode> Children { get; init; } = [];

    internal static NodeId ResolveNodeId(ExpandedNodeId nodeId, NamespaceTable namespaceUris, string nodeName)
        => ExpandedNodeId.ToNodeId(nodeId, namespaceUris)
            ?? throw new InvalidOperationException($"Cannot resolve node id '{nodeId}' of OPC UA node '{nodeName}'. Its namespace is not known to this session.");
}
