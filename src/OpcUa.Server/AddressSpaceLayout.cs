using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// The nodes a server serves for the data ports registered with it, by the path their node ids
/// are made of. A layout is never changed; registering or releasing a data port makes a new one,
/// so the address space of a running server can be moved from one to the next while a client
/// write still resolves against the layout it arrived under.
/// </summary>
/// <remarks>
/// Every data port of an engine registers its own copy of the same tree, so a data point reaches
/// the layout once per port, under the same id. Those copies are served as one variable that
/// carries the channels of every port. A data point with an id of its own that lands on a path
/// already taken belongs to another configuration, and serving both from one variable would mix
/// the values of the two, so it is refused. A folder only groups what is below it and is shared
/// by every data port that names it.
/// <para>
/// A shared data point is served with the data type and the access level of the copy registered
/// first, for as long as any data port serves it. A changed data point changes every deployment of
/// its configuration, and all of them are torn down before any is deployed again, so a changed copy
/// never registers beside an unchanged one. Nothing refuses it, though: it would be served with the
/// type of the unchanged copy.
/// </para>
/// </remarks>
internal sealed class AddressSpaceLayout
{
    private readonly List<KeyValuePair<object, IReadOnlyCollection<Node>>> _registrations;
    private readonly Dictionary<string, ServedNode> _nodesByPath = [];
    private readonly List<ServedNode> _nodes = [];

    public static AddressSpaceLayout Empty { get; } = new([]);

    private AddressSpaceLayout(List<KeyValuePair<object, IReadOnlyCollection<Node>>> registrations)
    {
        _registrations = registrations;

        foreach (var (owner, nodes) in registrations)
            Register(owner, nodes);
    }

    /// <summary>
    /// Every served node, each one after its parent.
    /// </summary>
    public IReadOnlyList<ServedNode> Nodes => _nodes;

    public bool TryGetNode(string path, [MaybeNullWhen(false)] out ServedNode node)
        => _nodesByPath.TryGetValue(path, out node);

    /// <summary>
    /// The layout that also serves the nodes of <paramref name="owner"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A node of <paramref name="owner"/> cannot be
    /// served beside the nodes already registered.</exception>
    public AddressSpaceLayout With(object owner, IReadOnlyCollection<Node> nodes)
        => new([.. _registrations, new(owner, nodes)]);

    /// <summary>
    /// The layout that no longer serves the nodes of <paramref name="owner"/>.
    /// </summary>
    public AddressSpaceLayout Without(object owner)
        => new([.. _registrations.Where(registration => !Equals(registration.Key, owner))]);

    private void Register(object owner, IReadOnlyCollection<Node> nodes)
    {
        var routes = nodes.GetRoutes();
        var envelopeChildChannels = CollectEnvelopeChildChannels(routes);

        // A route repeats every node above its leaf, so a node the owner shares between routes is
        // resolved once.
        Dictionary<Guid, ServedNode> resolved = [];
        Dictionary<string, ServedNode> claimedChannels = [];

        foreach (var route in routes)
        {
            ServedNode? parent = null;

            foreach (var node in route)
            {
                // An envelope child addresses the value of its parent variable, so it is not a node
                // of the address space and a client never browses to it.
                if (EnvelopeChildren.IsEnvelopeChild(node.DesignId))
                    continue;

                if (!resolved.TryGetValue(node.Id, out var served))
                {
                    served = Serve(owner, node, parent, envelopeChildChannels, claimedChannels);
                    resolved.Add(node.Id, served);
                }

                parent = served;
            }
        }
    }

    private ServedNode Serve(object owner, INode node, ServedNode? parent, HashSet<string> envelopeChildChannels, Dictionary<string, ServedNode> claimedChannels)
    {
        var isVariable = node.DesignId switch
        {
            OpcUaServerNodeDesignId.Folder => false,
            OpcUaServerNodeDesignId.Variable => true,
            _ => throw new InvalidOperationException($"Node design id '{node.DesignId}' is not supported."),
        };

        if (isVariable && parent is null)
            throw new InvalidOperationException("Variable must have a parent.");

        var path = parent is null ? node.Name : $"{parent.Path}.{node.Name}";

        if (_nodesByPath.TryGetValue(path, out var served))
            EnsureSharable(served, node, isVariable);
        else
            served = Add(path, node, parent, isVariable);

        if (isVariable)
            served.AddChannels(owner, ClaimChannels(served, SelectOwnChannels(node, envelopeChildChannels), claimedChannels));

        return served;
    }

    /// <summary>
    /// A data port publishes a channel to a single variable, so a channel cannot belong to two.
    /// </summary>
    private static List<string> ClaimChannels(ServedNode served, List<string> channels, Dictionary<string, ServedNode> claimedChannels)
    {
        foreach (var channel in channels)
        {
            if (!claimedChannels.TryAdd(channel, served))
                throw new InvalidOperationException($"The channel '{channel}' is transferred by both the data point '{claimedChannels[channel].Path}' and the data point '{served.Path}'. A channel can only be transferred by one data point.");
        }

        return channels;
    }

    private static void EnsureSharable(ServedNode served, INode node, bool isVariable)
    {
        if (served.IsVariable != isVariable)
            throw new InvalidOperationException($"The node '{served.Path}' is served both as a folder and as a data point. Every node of an OPC UA server needs a path of its own.");

        if (isVariable && !served.IsServedFor(node))
            throw new InvalidOperationException($"The data point '{served.Path}' is already served for another data point at the same path. Every data point of an OPC UA server needs a path of its own.");
    }

    private ServedNode Add(string path, INode node, ServedNode? parent, bool isVariable)
    {
        ServedNode served = new(path, node.Name, parent, node, isVariable);

        _nodesByPath.Add(path, served);
        _nodes.Add(served);

        return served;
    }

    /// <summary>
    /// An envelope child has no transfer of its own, so the engine attributes its channel to the
    /// variable. Serving a value on one of those channels would write a status code or a timestamp
    /// where the value belongs, and would raise a client write on the child's channel.
    /// </summary>
    private static List<string> SelectOwnChannels(INode node, HashSet<string> envelopeChildChannels)
    {
        List<string> channels = new(node.TransferredChannels.Count);

        foreach (var channel in node.TransferredChannels.Distinct())
        {
            if (!envelopeChildChannels.Contains(channel))
                channels.Add(channel);
        }

        return channels;
    }

    private static HashSet<string> CollectEnvelopeChildChannels(IReadOnlyCollection<IReadOnlyCollection<INode>> routes)
    {
        HashSet<string> channels = [];

        foreach (var route in routes)
        {
            foreach (var node in route)
            {
                if (EnvelopeChildren.IsEnvelopeChild(node.DesignId))
                    channels.UnionWith(node.AffectedChannels);
            }
        }

        return channels;
    }
}
