using System;
using AwesomeAssertions;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

public sealed class OpcUaNode_ResolveNodeId
{
    private const string KnownNamespaceUri = "http://vicione.test/opcua/known";
    private const string UnknownNamespaceUri = "http://vicione.test/opcua/unknown";

    private static NamespaceTable CreateNamespaceUris()
    {
        NamespaceTable namespaceUris = new();
        namespaceUris.Append("http://vicione.test/opcua/other");
        namespaceUris.Append(KnownNamespaceUri);

        return namespaceUris;
    }

    [Fact]
    public void Keeps_the_namespace_index_of_a_relative_node_id()
    {
        var resolved = OpcUaNode.ResolveNodeId(new ExpandedNodeId(new NodeId("test", 1)), CreateNamespaceUris(), "test");

        resolved.Should().Be(new NodeId("test", 1));
    }

    [Fact]
    public void Resolves_an_absolute_node_id_against_the_namespace_table()
    {
        var nodeId = new ExpandedNodeId(new NodeId("test", 1), KnownNamespaceUri);

        var resolved = OpcUaNode.ResolveNodeId(nodeId, CreateNamespaceUris(), "test");

        nodeId.IsAbsolute.Should().BeTrue();

        // The URI sits at index 2, not at the index the id carries, so a resolution that skipped the table shows up here.
        resolved.Should().Be(new NodeId("test", 2));
    }

    [Fact]
    public void Names_the_node_and_the_namespace_of_an_unresolvable_node_id()
    {
        var act = () => OpcUaNode.ResolveNodeId(new ExpandedNodeId(new NodeId("test", 1), UnknownNamespaceUri), CreateNamespaceUris(), "sensor");

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;

        message.Should().Contain("sensor").And.Contain(UnknownNamespaceUri);
    }
}
