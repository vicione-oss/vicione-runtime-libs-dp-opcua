using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace ViciOne.Suite.DataPort;

internal class NodeManager(IServerInternal server, ApplicationConfiguration configuration, string @namespace, TimeProvider timeProvider) : CustomNodeManager2(server, configuration, @namespace)
{
    private readonly SemaphoreSlim _publishSemaphore = new(1, 1);

    // The path is the identifier of the node id. It is passed in rather than built from the names
    // here, so that the layout, which resolves a client write by it, is the only one to build it.
    protected FolderState CreateFolder(NodeState? parent, string path, string name)
    {
        FolderState folder = new(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.Organizes,
            TypeDefinitionId = ObjectTypeIds.FolderType,
            NodeId = new(path, NamespaceIndex),
            BrowseName = new(name, NamespaceIndex),
            DisplayName = new("en", name),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            EventNotifier = EventNotifiers.None,
            Description = new("en", string.Empty),
            RolePermissions = [],
            UserRolePermissions = [],
        };

        parent?.AddChild(folder);

        return folder;
    }

    protected BaseDataVariableState CreateVariable(NodeState parent, string path, string name, NodeId dataType, int valueRank, bool readOnly, bool historizing)
    {
        var variable = new BaseDataVariableState(parent)
        {
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.Organizes,
            NodeId = new(path, NamespaceIndex),
            BrowseName = new(name, NamespaceIndex),
            Description = new("en", string.Empty),
            DisplayName = new("en", name),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            DataType = dataType,
            ValueRank = valueRank,
            RolePermissions = [],
            UserRolePermissions = [],
            AccessLevel = AccessLevels.CurrentRead
        };

        if (!readOnly)
        {
            // StatusWrite and TimestampWrite advertise that a client may write a status and a source
            // timestamp with the value, which is what a client reads to decide whether to offer it.
            // This stack accepts both at the node either way; a ServerTimestamp it always refuses.
            variable.AccessLevel |= AccessLevels.CurrentWrite | AccessLevels.StatusWrite | AccessLevels.TimestampWrite;
        }
        if (historizing)
            variable.AccessLevel |= AccessLevels.HistoryRead;

        variable.UserAccessLevel = variable.AccessLevel;
        variable.Historizing = historizing;
        variable.Value = GetDefault(TypeInfo.GetSystemType(dataType, EncodeableFactory.GlobalFactory));
        variable.StatusCode = StatusCodes.BadWaitingForInitialData;
        variable.Timestamp = timeProvider.GetUtcNow().DateTime;

        if (valueRank == ValueRanks.OneDimension)
            variable.ArrayDimensions = new([0]);
        else if (valueRank == ValueRanks.TwoDimensions)
            variable.ArrayDimensions = new([0, 0]);

        parent.AddChild(variable);

        return variable;
    }

    private static object? GetDefault(Type type)
        => type == null ? null : type.IsValueType ? Activator.CreateInstance(type) : null;

    /// <summary>
    /// Serves a variable with the status linked to it, as it is.
    /// </summary>
    public async Task UpdateVariableStateAsync(BaseDataVariableState variable, StatusCode statusCode)
    {
        await _publishSemaphore.WaitAsync();

        try
        {
            variable.StatusCode = statusCode;
            // notifies any monitored items that the value has changed.
            variable.ClearChangeMasks(SystemContext, false);
        }
        finally
        {
            _publishSemaphore.Release();
        }
    }

    /// <summary>
    /// Serves a value with the status linked to it, as it is. Without one, the first value clears the
    /// status the variable waits for its initial data with.
    /// </summary>
    public async Task<bool> WriteVariableValueAsync(BaseDataVariableState variable, object? value, DateTime timeStamp, StatusCode? statusCode, bool writeOnlyChanged)
    {
        if (writeOnlyChanged && variable.Value.Equals(value))
            return false;

        if (variable.DataType == DataTypeIds.String && value == null)
            value = string.Empty;

        if (value == null)
            return false;

        await _publishSemaphore.WaitAsync();

        try
        {
            variable.Value = value;
            variable.Timestamp = timeStamp;

            if (statusCode is { } linkedStatusCode)
            {
                variable.StatusCode = linkedStatusCode;
            }
            else if (variable.StatusCode == StatusCodes.BadWaitingForInitialData)
            {
                variable.StatusCode = StatusCodes.Good;
            }

            // notifies any monitored items that the value has changed.
            variable.ClearChangeMasks(SystemContext, false);
        }
        finally
        {
            _publishSemaphore.Release();
        }

        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _publishSemaphore?.Dispose();

        base.Dispose(disposing);
    }
}
