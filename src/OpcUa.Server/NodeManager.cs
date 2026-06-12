using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace ViciOne.Suite.DataPort;

internal class NodeManager(IServerInternal server, ApplicationConfiguration configuration, string @namespace, TimeProvider timeProvider) : CustomNodeManager2(server, configuration, @namespace)
{
    private readonly SemaphoreSlim _publishSemaphore = new(1, 1);

    protected FolderState CreateFolder(NodeState? parent, string name)
    {
        var path = parent is null ? name : $"{parent.NodeId.Identifier}.{name}";
        QualifiedName browseName = new(name, NamespaceIndex);

        if (parent?.FindChild(null, browseName) is not FolderState folder)
        {
            folder = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new(path, NamespaceIndex),
                BrowseName = browseName,
                DisplayName = new("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None,
                Description = new("en", string.Empty),
                RolePermissions = [],
                UserRolePermissions = [],
            };

            parent?.AddChild(folder);
        }

        return folder;
    }

    protected BaseDataVariableState CreateVariable(NodeState parent, string name, NodeId dataType, int valueRank, bool readOnly, bool historizing)
    {
        var path = $"{parent.NodeId.Identifier}.{name}";
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
            variable.AccessLevel |= AccessLevels.CurrentWrite;
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

    protected static bool IsNaN(object value)
        => (value is double dvalue && double.IsNaN(dvalue)) || (value is float fvalue && float.IsNaN(fvalue));

    public async Task UpdateVariableStateAsync(BaseDataVariableState variable, StatusCode statusCode)
    {
        await _publishSemaphore.WaitAsync();

        try
        {
            if (IsNaN(variable.Value) && statusCode == StatusCodes.Good)
                statusCode = StatusCodes.BadWaitingForInitialData;

            variable.StatusCode = statusCode;
            // notifies any monitored items that the value has changed.
            variable.ClearChangeMasks(SystemContext, false);
        }
        finally
        {
            _publishSemaphore.Release();
        }
    }

    public async Task<bool> WriteVariableValueAsync(BaseDataVariableState variable, object? value, DateTime timeStamp, bool writeOnlyChanged)
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

            if (IsNaN(value))
            {
                variable.StatusCode = StatusCodes.BadWaitingForInitialData;
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
