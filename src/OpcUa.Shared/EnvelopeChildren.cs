using System;
using System.Collections.Generic;
using System.Linq;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// The envelope children of a node tree, resolved once before the port runs.
/// </summary>
/// <remarks>
/// The tree editor cannot express everything an envelope child has to satisfy: it does not know
/// which of the children a given port serves, and it cannot see the channels the engine linked.
/// Those rules are checked here, and every violation is reported together so a wrong tree is fixed
/// in one pass rather than one message at a time.
/// </remarks>
internal sealed class EnvelopeChildren
{
    private static readonly EnvelopeChild[] s_none = [];

    private readonly Dictionary<string, EnvelopeChild[]> _byParentChannel;
    private readonly Dictionary<string, EnvelopeChild> _byChannel;

    private EnvelopeChildren(Dictionary<string, EnvelopeChild[]> byParentChannel, Dictionary<string, EnvelopeChild> byChannel)
    {
        _byParentChannel = byParentChannel;
        _byChannel = byChannel;
    }

    /// <summary>
    /// Whether a node addresses the envelope of its parent rather than a node of its own, which
    /// decides whether the tree walk looks it up in the OPC UA address space.
    /// </summary>
    internal static bool IsEnvelopeChild(string? designId)
        => KindOf(designId) is not null;

    /// <summary>The children this port serves for the value on <paramref name="parentChannel"/>.</summary>
    internal IReadOnlyList<EnvelopeChild> Of(string parentChannel)
        => _byParentChannel.TryGetValue(parentChannel, out var children) ? children : s_none;

    internal bool TryGetChild(string channel, out EnvelopeChild child)
        => _byChannel.TryGetValue(channel, out child!);

    /// <summary>
    /// Whether a channel belongs to an envelope child. The engine attributes a child's channel to
    /// the transferring parent, so a port that resolves values by channel has to tell the two apart.
    /// </summary>
    internal bool IsChildChannel(string channel)
        => _byChannel.ContainsKey(channel);

    /// <param name="nodes">Every node of the port, not only the envelope children.</param>
    /// <param name="servedKinds">The kinds this port exchanges values for. A child of any other kind
    /// is validated with the rest but not indexed: a port is handed the whole tree, and a kind it has
    /// no field for, such as a Server timestamp on the outgoing side of the client, must never reach
    /// it. A child the engine linked no channel to is not indexed either: it exchanges nothing, which
    /// is a node too many rather than an error.</param>
    /// <exception cref="InvalidOperationException">The tree carries an envelope no port could serve.
    /// The message names every violation.</exception>
    internal static EnvelopeChildren Create(IReadOnlyCollection<INode> nodes, IReadOnlyCollection<EnvelopeChildKind> servedKinds)
    {
        var nodesById = IndexById(nodes);
        var childrenByParent = GroupByParent(nodes, nodesById);

        Refuse(FindViolations(childrenByParent, nodesById, servedKinds));

        return new(MapParentChannels(childrenByParent, servedKinds), MapChannels(childrenByParent, servedKinds));
    }

    private static Dictionary<Guid, INode> IndexById(IReadOnlyCollection<INode> nodes)
    {
        Dictionary<Guid, INode> nodesById = new(nodes.Count);

        foreach (var node in nodes)
            nodesById[node.Id] = node;

        return nodesById;
    }

    private static Dictionary<Guid, List<EnvelopeChild>> GroupByParent(IReadOnlyCollection<INode> nodes, Dictionary<Guid, INode> nodesById)
    {
        Dictionary<Guid, List<EnvelopeChild>> childrenByParent = [];

        foreach (var node in nodes)
        {
            if (KindOf(node.DesignId) is not { } kind || node.ParentId is not { } parentId)
                continue;

            if (!childrenByParent.TryGetValue(parentId, out var children))
                childrenByParent.Add(parentId, children = []);

            nodesById.TryGetValue(parentId, out var parent);
            children.Add(EnvelopeChild.Create(node, kind, GetChannels(node, parent), OwnChannelOf(parent)));
        }

        return childrenByParent;
    }

    /// <summary>
    /// A child has no transfer of its own, so the engine attributes its channels to the transferring
    /// parent. The channels the two have in common are the ones the child's value is exchanged on.
    /// </summary>
    private static List<string> GetChannels(INode? node, INode? parent)
    {
        List<string> channels = [];

        if (node is null || parent is null)
            return channels;

        foreach (var channel in node.AffectedChannels)
        {
            if (parent.TransferredChannels.Contains(channel))
                channels.Add(channel);
        }

        return channels;
    }

    /// <summary>
    /// The one channel a node both affects and transfers, which is the channel of its own value.
    /// </summary>
    private static string OwnChannelOf(INode? node)
        => GetChannels(node, node) is [var channel, ..] ? channel : string.Empty;

    private static Dictionary<string, EnvelopeChild[]> MapParentChannels(Dictionary<Guid, List<EnvelopeChild>> childrenByParent, IReadOnlyCollection<EnvelopeChildKind> servedKinds)
    {
        Dictionary<string, EnvelopeChild[]> byParentChannel = [];

        foreach (var (_, children) in childrenByParent)
        {
            var exchangeable = SelectExchangeable(children, servedKinds);

            if (exchangeable.Count != 0)
                byParentChannel[exchangeable[0].ParentChannel] = [.. exchangeable];
        }

        return byParentChannel;
    }

    private static Dictionary<string, EnvelopeChild> MapChannels(Dictionary<Guid, List<EnvelopeChild>> childrenByParent, IReadOnlyCollection<EnvelopeChildKind> servedKinds)
    {
        Dictionary<string, EnvelopeChild> byChannel = [];

        foreach (var (_, children) in childrenByParent)
        {
            foreach (var child in SelectExchangeable(children, servedKinds))
            {
                foreach (var channel in child.Channels)
                    byChannel[channel] = child;
            }
        }

        return byChannel;
    }

    /// <summary>
    /// The children this port actually exchanges a value for: of a kind it serves, and linked to a
    /// channel to exchange it on.
    /// </summary>
    private static List<EnvelopeChild> SelectExchangeable(List<EnvelopeChild> children, IReadOnlyCollection<EnvelopeChildKind> servedKinds)
    {
        List<EnvelopeChild> exchangeable = new(children.Count);

        foreach (var child in children)
        {
            if (child.Channels.Count != 0 && servedKinds.Contains(child.Kind))
                exchangeable.Add(child);
        }

        return exchangeable;
    }

    private static List<string> FindViolations(Dictionary<Guid, List<EnvelopeChild>> childrenByParent, Dictionary<Guid, INode> nodesById, IReadOnlyCollection<EnvelopeChildKind> servedKinds)
    {
        List<string> violations = [];

        foreach (var (parentId, children) in childrenByParent)
        {
            if (!nodesById.TryGetValue(parentId, out var parent))
            {
                AddOrphanViolations(children, violations);
                continue;
            }

            AddParentViolations(parent, children, violations);
            AddDuplicateKindViolations(parent, children, violations);
        }

        AddSharedChannelViolations(childrenByParent, servedKinds, violations);

        return violations;
    }

    private static void AddOrphanViolations(List<EnvelopeChild> children, List<string> violations)
    {
        foreach (var child in children)
            violations.Add($"'{child.Node.Name}' is an envelope child of a node that is not part of this data port.");
    }

    private static void AddParentViolations(INode parent, List<EnvelopeChild> children, List<string> violations)
    {
        if (KindOf(parent.DesignId) is not null)
        {
            violations.Add($"'{parent.Name}' is an envelope child and cannot have envelope children of its own.");
            return;
        }

        // A port is handed the whole tree, with channels filled in only for the direction it runs
        // in, so a data point linked the other way carries its children here without a channel on
        // either of them. That parent is not linked in this direction rather than unable to carry
        // what it holds, and refusing it would stop the other direction of the same data port.
        if (!children.Exists(child => child.Channels.Count != 0))
            return;

        if (OwnChannelOf(parent).Length == 0)
            violations.Add($"'{parent.Name}' has envelope children but transfers no value of its own, so there is nothing to carry them.");
    }

    private static void AddDuplicateKindViolations(INode parent, List<EnvelopeChild> children, List<string> violations)
    {
        HashSet<EnvelopeChildKind> kinds = [];

        foreach (var child in children)
        {
            if (!kinds.Add(child.Kind))
                violations.Add($"'{parent.Name}' has more than one '{child.Kind}' envelope child.");
        }
    }

    /// <summary>
    /// Two children on one channel would exchange the same engine value as two parts of the
    /// envelope, of which only the last one could be served.
    /// </summary>
    private static void AddSharedChannelViolations(Dictionary<Guid, List<EnvelopeChild>> childrenByParent, IReadOnlyCollection<EnvelopeChildKind> servedKinds, List<string> violations)
    {
        Dictionary<string, EnvelopeChild> byChannel = [];

        foreach (var (_, children) in childrenByParent)
        {
            foreach (var child in SelectExchangeable(children, servedKinds))
            {
                foreach (var channel in child.Channels)
                {
                    if (!byChannel.TryAdd(channel, child))
                        violations.Add($"'{byChannel[channel].Node.Name}' and '{child.Node.Name}' are envelope children of the same channel, which a value cannot be carried on twice.");
                }
            }
        }
    }

    private static void Refuse(List<string> violations)
    {
        if (violations.Count == 0)
            return;

        throw new InvalidOperationException(
            $"The OPC UA data port cannot be started with this configuration:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", violations)}");
    }

    private static EnvelopeChildKind? KindOf(string? designId)
        => designId switch
        {
            EnvelopeNodeDesignId.StatusCode => EnvelopeChildKind.StatusCode,
            EnvelopeNodeDesignId.SourceTimestamp => EnvelopeChildKind.SourceTimestamp,
            EnvelopeNodeDesignId.ServerTimestamp => EnvelopeChildKind.ServerTimestamp,
            _ => null,
        };
}
