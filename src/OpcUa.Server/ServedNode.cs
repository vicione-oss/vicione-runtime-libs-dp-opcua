using System.Collections.Generic;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// A folder or a variable of the address space.
/// </summary>
internal sealed class ServedNode(string path, string name, ServedNode? parent, INode definition, bool isVariable)
{
    private readonly Dictionary<object, IReadOnlyList<string>> _channels = [];

    /// <summary>
    /// The identifier of its node id: the names from the root down to it, joined with dots.
    /// </summary>
    public string Path { get; } = path;

    public string Name { get; } = name;

    public ServedNode? Parent { get; } = parent;

    /// <summary>
    /// The data point or folder the node was first registered for, which decides how it is served.
    /// </summary>
    public INode Definition { get; } = definition;

    public bool IsVariable { get; } = isVariable;

    /// <summary>
    /// The channels of a variable by the data port they belong to.
    /// </summary>
    public IReadOnlyDictionary<object, IReadOnlyList<string>> Channels => _channels;

    /// <summary>
    /// Whether <paramref name="node"/> is the data point or folder this node was registered for,
    /// as opposed to another one that happens to have the same path.
    /// </summary>
    public bool IsServedFor(INode node)
        => Definition.Id == node.Id;

    /// <summary>
    /// Adds the channels of a data port to the variable. Only the layout that creates the node
    /// calls this, while it is built, so a node is never changed once a layout serves it.
    /// </summary>
    internal void AddChannels(object owner, IReadOnlyList<string> channels)
        => _channels.Add(owner, channels);
}
