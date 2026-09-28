using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using ViciOne.Tree.Builder.Extensions;
using ViciOne.Tree.Builder.NodeTypes;
using ViciOne.Tree.Builder.Rules.Yaml;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class YamlTests
{
    [Fact]
    public void All_properties_of_communication_class_have_a_matching_yaml_property()
    {
        var metadata = RulesDeserializer.Deserialize("OpcUaServer.yaml");
        var dataPortCommunicationType = typeof(OpcUaServerDataPortCommunication);

        var yamlPropertyIds = metadata.PropertyTypes
            .Select(p => p.Id)
            .ToHashSet();

        var yamlNodeTypeIds = metadata.NodeTypes
            .Select(n => n.Id)
            .ToHashSet();

        var classPropertyNames = dataPortCommunicationType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .ToHashSet();

        classPropertyNames.Except(yamlPropertyIds.Union(yamlNodeTypeIds))
            .Should().BeEmpty();
    }

    [Fact]
    public void DesignIds_should_be_in_yaml()
    {
        var metadata = RulesDeserializer.Deserialize("OpcUaServer.yaml");
        var nodeDesignIdType = typeof(OpcUaServerNodeDesignId);

        var yamlDesignIds = metadata.NodeTypes
            .OfType<DataPortTreeNodeType>()
            .Where(n => !string.IsNullOrEmpty(n.MappingId))
            .Select(n => n.MappingId)
            .ToHashSet();

        var expectedDesignIds = nodeDesignIdType
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToHashSet();

        expectedDesignIds.Except(yamlDesignIds)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("DataPointBool")]
    [InlineData("DataPointInteger")]
    [InlineData("DataPointFloat")]
    [InlineData("DataPointDateTime")]
    [InlineData("DataPointString")]
    [InlineData("DataPointBinary")]
    public void Offers_the_envelope_children_below_every_data_point(string nodeTypeId)
    {
        var metadata = RulesDeserializer.Deserialize("OpcUaServer.yaml");

        metadata.IsEnvelopeParent(nodeTypeId).Should().BeTrue();
        metadata.GetEnvelopeChildren(nodeTypeId).Select(child => child.Id)
            .Should().Equal(OpcUaServerNodeDesignId.StatusCode, OpcUaServerNodeDesignId.SourceTimestamp);

        metadata.NodeTypes.Single(n => n.Id == nodeTypeId).ChildNodes
            .Should().OnlyContain(child => child.MaxInstances == 1);
    }

    /// <summary>
    /// A folder is an address space object, not a value, so it has no envelope to hang a child on.
    /// </summary>
    [Fact]
    public void Offers_no_envelope_children_below_a_folder()
        => RulesDeserializer.Deserialize("OpcUaServer.yaml").IsEnvelopeParent(OpcUaServerNodeDesignId.Folder).Should().BeFalse();

    /// <summary>
    /// Asked the way the cluster editor and the deployment ask it: which way the value of a child
    /// travels, and in which of those directions the engine may link it.
    /// </summary>
    [Theory]
    [InlineData(OpcUaServerNodeDesignId.StatusCode, new[] { DataPortTransferDirection.Outbound, DataPortTransferDirection.Inbound, }, new[] { DataPortTransferDirection.Outbound, DataPortTransferDirection.Inbound, })]
    [InlineData(OpcUaServerNodeDesignId.SourceTimestamp, new[] { DataPortTransferDirection.Outbound, DataPortTransferDirection.Inbound, }, new[] { DataPortTransferDirection.Outbound, DataPortTransferDirection.Inbound, })]
    public void Declares_the_directions_of_an_envelope_child(string childId, DataPortTransferDirection[] transfer, DataPortTransferDirection[] link)
    {
        var metadata = RulesDeserializer.Deserialize("OpcUaServer.yaml");

        var parents = metadata.GetEnvelopeParents(childId);

        parents.Should().NotBeEmpty();
        parents.Should().AllSatisfy(parent =>
        {
            metadata.GetEnvelopeTransferDirections(parent.Id, childId).Should().BeEquivalentTo(transfer);
            metadata.GetEnvelopeLinkDirections(parent.Id, childId).Should().BeEquivalentTo(link);
        });
    }

    /// <summary>
    /// A server sets the server timestamp of the values it serves and refuses one a client writes,
    /// so it has nothing to offer in either direction and the ruleset does not name the child at all.
    /// </summary>
    [Fact]
    public void Offers_no_server_timestamp()
    {
        var metadata = RulesDeserializer.Deserialize("OpcUaServer.yaml");

        metadata.NodeTypes.Should().NotContain(node => node.Id == EnvelopeNodeDesignId.ServerTimestamp);
        metadata.GetEnvelopeParents(EnvelopeNodeDesignId.ServerTimestamp).Should().BeEmpty();
    }

    /// <summary>
    /// Each child declares the type of the value it carries. A status code is the unsigned integer
    /// OPC UA transmits, and the name of that code for a configuration that prefers to read it.
    /// </summary>
    [Theory]
    [InlineData(OpcUaServerNodeDesignId.StatusCode, new[] { "UInt32", "String", })]
    [InlineData(OpcUaServerNodeDesignId.SourceTimestamp, new[] { "DateTime", })]
    public void Declares_the_value_an_envelope_child_carries(string childId, string[] dataTypes)
    {
        var metadata = RulesDeserializer.Deserialize("OpcUaServer.yaml");

        metadata.IsEnvelopeChild(childId).Should().BeTrue();
        metadata.IsValueNode(childId).Should().BeTrue();
        metadata.IsMarkerEnvelopeChild(childId).Should().BeFalse();

        metadata.NodeTypes.Single(n => n.Id == childId).DataTypes.Should().Equal(dataTypes);
    }
}
