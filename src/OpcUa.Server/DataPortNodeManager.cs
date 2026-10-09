using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Opc.Ua;
using Opc.Ua.Server;
using DataPortNode = ViciOne.Suite.DataPort.INode;

namespace ViciOne.Suite.DataPort;

internal sealed class DataPortNodeManager : NodeManager
{
    private static readonly Dictionary<Type, NodeId> s_dataTypeMapping = new()
    {
        { typeof(bool), DataTypeIds.Boolean },
        { typeof(sbyte), DataTypeIds.SByte },
        { typeof(byte), DataTypeIds.Byte },
        { typeof(short), DataTypeIds.Int16 },
        { typeof(ushort), DataTypeIds.UInt16 },
        { typeof(int), DataTypeIds.Int32 },
        { typeof(uint), DataTypeIds.UInt32 },
        { typeof(long), DataTypeIds.Int64 },
        { typeof(ulong), DataTypeIds.UInt64 },
        { typeof(string), DataTypeIds.String },
        { typeof(float), DataTypeIds.Float },
        { typeof(double), DataTypeIds.Double },
        { typeof(DateTime), DataTypeIds.DateTime },
        { typeof(byte[]), DataTypeIds.ByteString },
    };

    // The node of every path of the served layout. Only changed under the lock of the node manager.
    private readonly Dictionary<string, NodeState> _nodes = [];
    private readonly TimeProvider _timeProvider;

    // Read by publishes and client writes, which do not take the lock, so a change of the layout
    // replaces it as a whole instead of changing it in place.
    private volatile ServedVariables _served;

    public event Action<ReceivedWrite>? ReceiveValue;

    [SuppressMessage("Style", "IDE0290:Primären Konstruktor verwenden")]
    public DataPortNodeManager(IServerInternal server, ApplicationConfiguration configuration, AddressSpaceLayout layout, string @namespace, TimeProvider? timeProvider = null)
        : base(server, configuration, @namespace, timeProvider ?? TimeProvider.System)
    {
        _served = new(layout, []);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public BaseDataVariableState GetNodeState(object owner, string channel)
        => _served.ByChannel.TryGetValue((owner, channel), out var nodeState)
            ? nodeState : throw new InvalidOperationException($"Failed to find OPC UA node for {channel}.");

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
            externalReferences[ObjectIds.ObjectsFolder] = references = [];

        lock (Lock)
        {
            foreach (var folder in AddNodes(_served.Layout.Nodes))
                references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, folder.NodeId));

            _served = ServedVariables.Of(_served.Layout, _nodes);
        }
    }

    /// <summary>
    /// Moves the address space of the running server to <paramref name="layout"/>. A node both
    /// layouts serve is kept as it is, with its value, its status and the items a client monitors
    /// on it, so a data port that comes or goes leaves the nodes of every other one untouched.
    /// </summary>
    public void Apply(AddressSpaceLayout layout)
    {
        List<LocalReference> removedReferences = [];
        List<FolderState> addedRootFolders;

        lock (Lock)
        {
            RemoveNodes(SelectRemovedNodes(layout), removedReferences);
            addedRootFolders = AddNodes(SelectAddedNodes(layout));

            _served = ServedVariables.Of(layout, _nodes);
        }

        // The objects folder belongs to another node manager, which takes a lock of its own. Like
        // the DeleteNode of the stack, its references are only changed once this lock is released.
        if (removedReferences.Count > 0)
            Server.NodeManager.RemoveReferences(removedReferences);

        foreach (var folder in addedRootFolders)
            Server.NodeManager.AddReferences(ObjectIds.ObjectsFolder, [new NodeStateReference(ReferenceTypes.Organizes, false, folder.NodeId)]);
    }

    /// <summary>
    /// The nodes <paramref name="layout"/> no longer serves, together with everything below them,
    /// each one after its parent. A variable that comes back at its path for another data point is
    /// another variable, so it counts as removed as well.
    /// </summary>
    private List<ServedNode> SelectRemovedNodes(AddressSpaceLayout layout)
    {
        HashSet<string> removedPaths = [];
        List<ServedNode> removed = [];

        foreach (var node in _served.Layout.Nodes)
        {
            if (IsKept(node, layout) && !IsBelowAny(node, removedPaths))
                continue;

            removedPaths.Add(node.Path);
            removed.Add(node);
        }

        return removed;
    }

    private static bool IsKept(ServedNode node, AddressSpaceLayout layout)
        => layout.TryGetNode(node.Path, out var next)
            && next.IsVariable == node.IsVariable
            && (!node.IsVariable || next.IsServedFor(node.Definition));

    private List<ServedNode> SelectAddedNodes(AddressSpaceLayout layout)
    {
        List<ServedNode> added = [];

        foreach (var node in layout.Nodes)
        {
            if (!_nodes.ContainsKey(node.Path))
                added.Add(node);
        }

        return added;
    }

    private void RemoveNodes(List<ServedNode> removed, List<LocalReference> removedReferences)
    {
        HashSet<string> removedPaths = [.. removed.Select(node => node.Path)];

        foreach (var node in removed)
        {
            // The stack keeps the items a client monitors on a node it deletes, and goes on
            // reporting the last value. The status tells the client the node is gone, until a node
            // at the same path takes the items over.
            if (_nodes[node.Path] is BaseDataVariableState variable)
            {
                variable.StatusCode = StatusCodes.BadNodeIdUnknown;
                variable.ClearChangeMasks(SystemContext, false);
            }
        }

        foreach (var node in removed)
        {
            // Removing a node removes everything below it, so only the topmost ones are removed.
            if (IsBelowAny(node, removedPaths))
                continue;

            var nodeState = _nodes[node.Path];

            RemovePredefinedNode(SystemContext, nodeState, removedReferences);

            if (node.Parent is null)
                RemoveRootNotifier(nodeState);
        }

        foreach (var node in removed)
            _nodes.Remove(node.Path);
    }

    /// <summary>
    /// Creates the nodes below the ones already served, each one after its parent, and returns the
    /// folders among them that sit in the objects folder.
    /// </summary>
    private List<FolderState> AddNodes(IReadOnlyList<ServedNode> added)
    {
        HashSet<string> addedPaths = [.. added.Select(node => node.Path)];
        List<NodeState> topmost = [];
        List<FolderState> rootFolders = [];

        foreach (var node in added)
        {
            // The layout serves nothing but folders at the root.
            if (node.Parent is null)
            {
                var folder = CreateFolder(null, node.Path, node.Name);
                _nodes.Add(node.Path, folder);
                rootFolders.Add(folder);
                continue;
            }

            var nodeState = Create(node, _nodes[node.Parent.Path]);
            _nodes.Add(node.Path, nodeState);

            if (!IsBelowAny(node, addedPaths))
                topmost.Add(nodeState);
        }

        foreach (var folder in rootFolders)
        {
            folder.EventNotifier = EventNotifiers.SubscribeToEvents;
            folder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            AddRootNotifier(folder);
            AddPredefinedNode(SystemContext, folder);
        }

        // Indexing a node indexes everything below it, so only the topmost ones are indexed.
        foreach (var nodeState in topmost)
            AddPredefinedNode(SystemContext, nodeState);

        foreach (var node in added)
            TakeOverMonitoredItems(_nodes[node.Path]);

        return rootFolders;
    }

    /// <summary>
    /// Moves the items a client still monitors on a removed node with the same node id onto
    /// <paramref name="nodeState"/>, and reports its value to them. The stack binds an item to the
    /// node it monitors once, and binds every item created later for the same node id to that node
    /// as well, so without this a client subscribed across a redeploy would never see the values
    /// of the new node.
    /// </summary>
    private void TakeOverMonitoredItems(NodeState nodeState)
    {
        if (!MonitoredNodes.TryGetValue(nodeState.NodeId, out var monitoredNode))
            return;

        monitoredNode.Node.OnStateChanged = null;
        monitoredNode.Node.OnReportEvent = null;
        monitoredNode.Node = nodeState;

        // Each item marks the node it monitors the way subscribing it did, so a later unsubscribe
        // releases the node served now.
        if (monitoredNode.EventMonitoredItems is { } eventItems)
        {
            nodeState.OnReportEvent = monitoredNode.OnReportEvent;

            foreach (var item in eventItems)
            {
                MoveHandle(item, nodeState);
                nodeState.SetAreEventsMonitored(SystemContext, true, true);
            }
        }

        if (monitoredNode.DataChangeMonitoredItems is { } dataChangeItems)
        {
            nodeState.OnStateChanged = monitoredNode.OnMonitoredNodeChanged;

            foreach (var item in dataChangeItems)
            {
                MoveHandle(item, nodeState);
                monitoredNode.QueueValue(SystemContext, nodeState, item);
            }
        }
    }

    private static void MoveHandle(IMonitoredItem item, NodeState nodeState)
    {
        if (item.ManagerHandle is NodeHandle handle)
            handle.Node = nodeState;
    }

    /// <summary>
    /// Whether the parent of <paramref name="node"/> is one of <paramref name="paths"/>.
    /// </summary>
    private static bool IsBelowAny(ServedNode node, HashSet<string> paths)
        => node.Parent is not null && paths.Contains(node.Parent.Path);

    private NodeState Create(ServedNode node, NodeState parent)
        => node.IsVariable
            ? CreateDataPortVariable(node, parent)
            : CreateFolder(parent, node.Path, node.Name);

    private BaseDataVariableState CreateDataPortVariable(ServedNode node, NodeState parent)
    {
        var isReadOnly = IsReadOnly(node.Definition);
        var variableNode = CreateVariable(parent, node.Path, node.Name, GetDatatypeId(node.Definition.ValueType), ValueRanks.Scalar, isReadOnly, false);

        if (!isReadOnly)
            variableNode.OnWriteValue += OnWriteValue;

        return variableNode;
    }

    // The fallback matches the default the ruleset declares, so a hand-written configuration that
    // omits the property is served the same way as one the editor produced.
    private static bool IsReadOnly(DataPortNode node)
        => node.GetPropertyValueOrDefault(OpcUaServerDataPortPropertyNames.ReadOnly, true);

    private static NodeId GetDatatypeId(Type? valueType)
        => s_dataTypeMapping.TryGetValue(valueType ?? typeof(object), out var valueTypeId) ? valueTypeId : DataTypeIds.BaseDataType;

    private ServiceResult OnWriteValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding,
        ref object value, ref StatusCode statusCode, ref DateTime timestamp)
    {
        if (node is not BaseDataVariableState variable)
            return StatusCodes.BadNotTypeDefinition;

        var typeInfo = TypeInfo.IsInstanceOfDataType(value, variable.DataType, variable.ValueRank, context.NamespaceUris, context.TypeTable);

        if (typeInfo == null || typeInfo == TypeInfo.Unknown)
            return StatusCodes.BadTypeMismatch;

        if (node.NodeId.Identifier is not string identifier || !_served.Layout.TryGetNode(identifier, out var served) || !served.IsVariable)
            return ServiceResult.Create(StatusCodes.BadInternalError, "Failed to find configuration for {0}.", node.NodeId);

        served.Definition.TryGetProperty(OpcUaServerDataPortPropertyNames.Minimum, out var min);
        served.Definition.TryGetProperty(OpcUaServerDataPortPropertyNames.Maximum, out var max);

        if (!IsInRange(min, max, value))
            return StatusCodes.BadOutOfRange;

        // A client is free to write without a source timestamp. The variable is stamped with the
        // moment the write arrived, but the Source timestamp child reports only what the client
        // actually sent, so the two are carried apart.
        var sourceTimestamp = timestamp;

        if (timestamp == DateTime.MinValue)
            timestamp = _timeProvider.GetUtcNow().DateTime;

        foreach (var channels in served.Channels.Values)
        {
            foreach (var channel in channels)
                ReceiveValue?.Invoke(new(channel, value, timestamp, statusCode, sourceTimestamp));
        }

        return StatusCodes.Good;
    }

    internal static bool IsInRange(Property? min, Property? max, object value)
    {
        if (value is string)
            return true;

        return IsAtLeast(value, min?.Value) && IsAtMost(value, max?.Value);
    }

    private static bool IsAtLeast(object value, object? minimum)
        => minimum is null || (TryCompare(value, minimum, out var comparison) && comparison >= 0);

    private static bool IsAtMost(object value, object? maximum)
        => maximum is null || (TryCompare(value, maximum, out var comparison) && comparison <= 0);

    // Limits come from the configuration and values come off the wire, so the same number can arrive
    // as two different CLR types, and IComparable.CompareTo throws on a foreign one. A limit that is
    // neither a number nor of the value's own type cannot be evaluated at all; refusing the write is
    // safer than accepting a value whose limit was never checked.
    private static bool TryCompare(object value, object limit, out int comparison)
    {
        if (TryConvertToDecimal(value, out var exactValue) && TryConvertToDecimal(limit, out var exactLimit))
        {
            comparison = exactValue.CompareTo(exactLimit);
            return true;
        }

        if (TryConvertToDouble(value, out var number) && TryConvertToDouble(limit, out var limitNumber))
        {
            comparison = number.CompareTo(limitNumber);
            return true;
        }

        if (value.GetType() == limit.GetType() && value is IComparable comparable)
        {
            comparison = comparable.CompareTo(limit);
            return true;
        }

        comparison = 0;
        return false;
    }

    // decimal holds every integral type without loss, where double rounds anything past 2^53. It
    // cannot carry the comparison on its own: float and double reach this code carrying NaN, infinity
    // and a range decimal has no room for, and every one of those makes the conversion throw.
    private static bool TryConvertToDecimal(object value, out decimal number)
    {
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal)
        {
            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }

        number = 0;
        return false;
    }

    private static bool TryConvertToDouble(object value, out double number)
    {
        if (value is float or double || TryConvertToDecimal(value, out _))
        {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return true;
        }

        number = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var node in _nodes.Values)
                node.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The layout the address space serves, with the variable of every channel by the data port it
    /// belongs to. Two data ports may name the same channel, each for a variable of its own.
    /// </summary>
    private sealed class ServedVariables(AddressSpaceLayout layout, Dictionary<(object Owner, string Channel), BaseDataVariableState> byChannel)
    {
        public AddressSpaceLayout Layout { get; } = layout;

        public IReadOnlyDictionary<(object Owner, string Channel), BaseDataVariableState> ByChannel { get; } = byChannel;

        public static ServedVariables Of(AddressSpaceLayout layout, Dictionary<string, NodeState> nodes)
        {
            Dictionary<(object Owner, string Channel), BaseDataVariableState> byChannel = [];

            foreach (var node in layout.Nodes)
            {
                if (!node.IsVariable)
                    continue;

                foreach (var (owner, channels) in node.Channels)
                {
                    foreach (var channel in channels)
                        byChannel.Add((owner, channel), (BaseDataVariableState)nodes[node.Path]);
                }
            }

            return new(layout, byChannel);
        }
    }
}
