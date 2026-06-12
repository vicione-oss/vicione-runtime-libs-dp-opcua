using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Opc.Ua;
using Opc.Ua.Server;
using DataPortNode = ViciOne.Suite.DataPort.INode;

namespace ViciOne.Suite.DataPort;

internal sealed class DataPortNodeManager : NodeManager
{
    private static readonly Dictionary<Type, NodeId> s_dataTypeMapping = new()
    {
        { typeof(bool), DataTypeIds.Boolean },
        { typeof(int), DataTypeIds.Int32 },
        { typeof(long), DataTypeIds.Int64 },
        { typeof(string), DataTypeIds.String },
        { typeof(float), DataTypeIds.Float },
        { typeof(double), DataTypeIds.Double },
    };

    private readonly Dictionary<string, (BaseDataVariableState Node, List<string> Channels, Property? MinProperty, Property? MaxProperty)> _nodesConfiguration = [];
    private readonly Dictionary<string, BaseDataVariableState> _channelNodes = [];
    private readonly List<NodeState> _nodes = [];
    private readonly IReadOnlyCollection<IReadOnlyCollection<DataPortNode>> _routes;
    private readonly TimeProvider _timeProvider;

    public event Action<string, DateTime, object>? ReceiveValue;

    [SuppressMessage("Style", "IDE0290:Primären Konstruktor verwenden")]
    public DataPortNodeManager(IServerInternal server, ApplicationConfiguration configuration, IReadOnlyCollection<IReadOnlyCollection<DataPortNode>> routes, string @namespace, TimeProvider? timeProvider = null)
        : base(server, configuration, @namespace, timeProvider ?? TimeProvider.System)
    {
        _routes = routes;
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

        var isReadOnly = node.GetPropertyValueOrDefault(OpcUaServerDataPortPropertyNames.ReadOnly, false);
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

        _nodesConfiguration.Add((string)variableNode.NodeId.Identifier, (variableNode, node.TransferredChannels, min, max));

        foreach (var channel in node.TransferredChannels)
            _channelNodes.Add(channel, variableNode);
    }

    private static NodeId GetDatatypeId(Type? valueType)
        => s_dataTypeMapping.TryGetValue(valueType ?? typeof(object), out var valueTypeId) ? valueTypeId : DataTypeIds.DataValue;

    private ServiceResult OnWriteValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding,
        ref object value, ref StatusCode statusCode, ref DateTime timestamp)
    {
        if (node is not BaseDataVariableState variable)
            return StatusCodes.BadNotTypeDefinition;

        var typeInfo = TypeInfo.IsInstanceOfDataType(value, variable.DataType, variable.ValueRank, context.NamespaceUris, context.TypeTable);

        if (typeInfo == null || typeInfo == TypeInfo.Unknown)
            return StatusCodes.BadTypeMismatch;

        var identifier = (string)node.NodeId.Identifier;

        if (!_nodesConfiguration.TryGetValue(identifier, out var config))
            throw new InvalidOperationException($"Failed to find configuration for {identifier}.");

        if (!IsInRange(config.MinProperty, config.MaxProperty, value))
            return StatusCodes.BadOutOfRange;

        if (timestamp == DateTime.MinValue)
            timestamp = _timeProvider.GetUtcNow().DateTime;

        foreach (var channel in config.Channels)
            ReceiveValue?.Invoke(channel, timestamp, value);

        return StatusCodes.Good;
    }

    internal static bool IsInRange(Property? min, Property? max, object value)
    {
        if (value is string)
            return true;

        if (value is IComparable comparable)
        {
            if (min?.Value is not null && comparable.CompareTo(min.Value) < 0)
                return false;
            if (max?.Value is not null && comparable.CompareTo(max.Value) > 0)
                return false;
        }

        return true;
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
