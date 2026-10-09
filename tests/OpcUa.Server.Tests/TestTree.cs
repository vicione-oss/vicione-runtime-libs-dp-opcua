using System;
using System.Collections.Generic;
using System.Linq;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// A folder, any folders below it, and a single variable at the bottom.
/// </summary>
internal sealed class TestTree
{
    private readonly List<Node> _folders;
    private readonly Guid _variableId = Guid.NewGuid();

    private TestTree(List<Node> folders) => _folders = folders;

    internal static TestTree Create(params string[] folderNames)
    {
        List<Node> folders = [];
        Node? parent = null;

        foreach (var name in folderNames)
        {
            parent = new() { Id = Guid.NewGuid(), ParentId = parent?.Id, Name = name, DesignId = OpcUaServerNodeDesignId.Folder };
            folders.Add(parent);
        }

        return new(folders);
    }

    /// <summary>
    /// A copy of the tree whose variable transfers <paramref name="channels"/>, as the tree of one
    /// of the data ports of a configuration.
    /// </summary>
    internal List<Node> WithChannels(params string[] channels)
    {
        var parent = _folders[^1];

        return [.. _folders.Select(Copy), Variable(parent, "variable", _variableId, channels)];
    }

    internal static Node Variable(Node parent, string name, params string[] channels)
        => Variable(parent, name, Guid.NewGuid(), channels);

    internal static Node Variable(Node parent, string name, Guid id, params string[] channels) => new()
    {
        Id = id,
        ParentId = parent.Id,
        Name = name,
        DesignId = OpcUaServerNodeDesignId.Variable,
        ValueType = typeof(double),
        TransferredChannels = [.. channels],
        AffectedChannels = [.. channels],
        Properties = new() { { OpcUaServerDataPortPropertyNames.ReadOnly, new Property { Value = false } } },
    };

    private static Node Copy(Node folder) => new()
    {
        Id = folder.Id,
        ParentId = folder.ParentId,
        Name = folder.Name,
        DesignId = folder.DesignId,
    };
}
