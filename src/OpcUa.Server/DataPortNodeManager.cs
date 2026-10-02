using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

    private readonly Dictionary<string, (BaseDataVariableState Node, List<string> Channels, Property? MinProperty, Property? MaxProperty)> _nodesConfiguration = [];
    private readonly Dictionary<string, BaseDataVariableState> _channelNodes = [];
    private readonly List<NodeState> _nodes = [];
    private readonly IReadOnlyCollection<IReadOnlyCollection<DataPortNode>> _routes;
    private readonly HashSet<string> _envelopeChildChannels;
    private readonly TimeProvider _timeProvider;

    public event Action<ReceivedWrite>? ReceiveValue;

    [SuppressMessage("Style", "IDE0290:Primären Konstruktor verwenden")]
    public DataPortNodeManager(IServerInternal server, ApplicationConfiguration configuration, IReadOnlyCollection<IReadOnlyCollection<DataPortNode>> routes, string @namespace, TimeProvider? timeProvider = null)
        : base(server, configuration, @namespace, timeProvider ?? TimeProvider.System)
    {
        _routes = routes;
        _envelopeChildChannels = CollectEnvelopeChildChannels(routes);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public BaseDataVariableState GetNodeState(string channel)
        => _channelNodes.TryGetValue(channel, out var nodeState)
            ? nodeState : throw new InvalidOperationException($"Failed to find OPC UA node for {channel}.");

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
            externalReferences[ObjectIds.ObjectsFolder] = references = [];

        ApplyCommunicationNodes(CreateVariableConfig, out var rootNodes, out var nodes);
        _nodes.AddRange(nodes);
        AddRootNodes(references, rootNodes);
    }

    private void AddRootNodes(IList<IReference> references, List<FolderState> nodes)
    {
        foreach (var folder in nodes)
        {
            folder.EventNotifier = EventNotifiers.SubscribeToEvents;

            folder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, folder.NodeId));
            AddRootNotifier(folder);
            AddPredefinedNode(SystemContext, folder);
        }
    }

    private void ApplyCommunicationNodes(Action<DataPortNode, BaseDataVariableState> createVariableConfig, out List<FolderState> rootNodes, out List<NodeState> nodes)
    {
        Dictionary<Guid, NodeState> cache = [];
        nodes = [];
        rootNodes = [];

        foreach (var route in _routes)
        {
            NodeState? previousNode = null;

            foreach (var dataPortNode in route)
            {
                // An envelope child addresses the value of its parent variable, so it is not a node
                // of the address space and a client never browses to it.
                if (EnvelopeChildren.IsEnvelopeChild(dataPortNode.DesignId))
                    continue;

                if (cache.TryGetValue(dataPortNode.Id, out var node))
                {
                    previousNode = node;
                    continue;
                }

                var currentNode = Create(dataPortNode, previousNode, createVariableConfig);

                if (previousNode is null && currentNode is FolderState folder)
                    rootNodes.Add(folder);

                cache.Add(dataPortNode.Id, currentNode);
                nodes.Add(currentNode);

                previousNode = currentNode;
            }
        }
    }

    private NodeState Create(DataPortNode node, NodeState? parent, Action<DataPortNode, BaseDataVariableState> createVariableConfig)
        => node.DesignId switch
        {
            OpcUaServerNodeDesignId.Folder => CreateFolder(parent, node.Name),
            OpcUaServerNodeDesignId.Variable => CreateDataPortVariable(node, parent, createVariableConfig),
            _ => throw new InvalidOperationException($"Node design id '{node.DesignId}' is not supported."),
        };

    private BaseDataVariableState CreateDataPortVariable(DataPortNode node, NodeState? parent, Action<DataPortNode, BaseDataVariableState> createVariableConfig)
    {
        if (parent is null)
            throw new InvalidOperationException("Variable must have a parent.");

        // The fallback matches the default the ruleset declares, so a hand-written configuration
        // that omits the property is served the same way as one the editor produced.
        var isReadOnly = node.GetPropertyValueOrDefault(OpcUaServerDataPortPropertyNames.ReadOnly, true);
        var variableNode = CreateVariable(parent, node.Name, GetDatatypeId(node.ValueType), ValueRanks.Scalar, isReadOnly, false);

        if (!isReadOnly)
            variableNode.OnWriteValue += OnWriteValue;

        createVariableConfig(node, variableNode);

        return variableNode;
    }

    private void CreateVariableConfig(DataPortNode node, BaseDataVariableState variableNode)
    {
        node.TryGetProperty(OpcUaServerDataPortPropertyNames.Minimum, out var min);
        node.TryGetProperty(OpcUaServerDataPortPropertyNames.Maximum, out var max);

        // An envelope child has no transfer of its own, so the engine attributes its channel to
        // this variable. Serving a value on one of those channels would write a status code or a
        // timestamp where the value belongs, and would raise a client write on the child's channel.
        var channels = SelectOwnChannels(node);

        _nodesConfiguration.Add((string)variableNode.NodeId.Identifier, (variableNode, channels, min, max));

        foreach (var channel in channels)
            _channelNodes.Add(channel, variableNode);
    }

    private List<string> SelectOwnChannels(DataPortNode node)
    {
        List<string> channels = new(node.TransferredChannels.Count);

        foreach (var channel in node.TransferredChannels)
        {
            if (!_envelopeChildChannels.Contains(channel))
                channels.Add(channel);
        }

        return channels;
    }

    /// <summary>
    /// The channels of every envelope child in the routes. Both ports register their tree with the
    /// server, so a child linked both ways carries one channel per direction here.
    /// </summary>
    private static HashSet<string> CollectEnvelopeChildChannels(IReadOnlyCollection<IReadOnlyCollection<DataPortNode>> routes)
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

        if (node.NodeId.Identifier is not string identifier || !_nodesConfiguration.TryGetValue(identifier, out var config))
            return ServiceResult.Create(StatusCodes.BadInternalError, "Failed to find configuration for {0}.", node.NodeId);

        if (!IsInRange(config.MinProperty, config.MaxProperty, value))
            return StatusCodes.BadOutOfRange;

        // The source timestamp follows the clock of the client, which may be off by any amount, so
        // the value is reported for the moment the write arrived. The Source timestamp child reports
        // only what the client actually sent, so the two are carried apart. A write without one is
        // served with the arrival time as its source timestamp.
        var sourceTimestamp = timestamp;
        var arrived = _timeProvider.GetUtcNow().DateTime;

        if (timestamp == DateTime.MinValue)
            timestamp = arrived;

        foreach (var channel in config.Channels)
            ReceiveValue?.Invoke(new(channel, value, arrived, statusCode, sourceTimestamp));

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
            foreach (var node in _nodes)
                node.Dispose();
        }

        base.Dispose(disposing);
    }
}
