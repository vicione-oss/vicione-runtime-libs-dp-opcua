using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using ViciOne.TreeBuilder.NodeTypes;
using ViciOne.TreeBuilder.Rules.Yaml;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class YamlTests
{
    [Fact]
    public void All_properties_of_communication_class_have_a_matching_yaml_property()
    {
        var metadata = RulesDeserializer.Deserialize("OpcUaClient.yaml");
        var dataPortCommunicationType = typeof(OpcUaClientDataPortCommunication);

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
    public void MaxDepth_of_the_client_node_matches_the_browse_depth_limit()
    {
        var metadata = RulesDeserializer.Deserialize("OpcUaClient.yaml");

        var clientNode = metadata.Root!.ChildNodes!.Single(child => child.Id == "OPCUA-Client");

        clientNode.MaxDepth.Should().Be(OpcUaClient.MaxDepth);
    }

    [Fact]
    public void DesignIds_should_be_in_yaml()
    {
        var metadata = RulesDeserializer.Deserialize("OpcUaClient.yaml");
        var nodeDesignIdType = typeof(OpcUaClientNodeDesignId);

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
}
