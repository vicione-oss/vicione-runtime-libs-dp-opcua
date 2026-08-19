using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Opc.Ua;
using Opc.Ua.Server;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// Builds the address spaces that <see cref="OpcUaTestSystemOptions"/> asks for. Every shape here is something a
/// conforming OPC UA server is allowed to do, so a client has to cope with all of them.
/// </summary>
[SuppressMessage("Reliability", "CA2000:Objekte verwerfen, bevor Bereich verloren geht",
    Justification = "Created nodes are handed to the predefined node tree, which disposes them with the node manager.")]
internal sealed class OpcUaTestNodeManager : CustomNodeManager2
{
    internal const string PrimaryNamespaceUri = "http://vicione.test/opcua/primary";
    internal const string SecondaryNamespaceUri = "http://vicione.test/opcua/secondary";

    internal const string RootFolderName = "TestNodes";

    /// <summary>The display name both duplicate siblings carry.</summary>
    internal const string DuplicateDisplayName = "Duplicate";

    internal const string FirstDuplicateBrowseName = "FirstDuplicate";
    internal const string SecondDuplicateBrowseName = "SecondDuplicate";

    internal const string MismatchedBrowseName = "MismatchedBrowse";
    internal const string MismatchedDisplayName = "Mismatched display name";

    internal const string OversizedFolderName = "OversizedFolder";

    /// <summary>How many children the oversized folder holds — deliberately more than one response returns.</summary>
    internal const int OversizedFolderChildCount = 25;

    /// <summary>How many references the server hands back per browse response while the oversized folder is on.</summary>
    internal const uint MaxReferencesPerBrowse = 10;

    internal const string AbsoluteReferenceSourceName = "AbsoluteReferenceSource";
    internal const string AbsoluteReferenceTargetName = "AbsoluteReferenceTarget";

    private readonly OpcUaTestSystemOptions _options;

    internal OpcUaTestNodeManager(IServerInternal server, OpcUaTestSystemOptions options)
        : base(server, TestNamespaceUris(options)) => _options = options;

    /// <summary>
    /// The namespace URIs in registration order, which is what decides their index in the session's namespace table.
    /// </summary>
    private static string[] TestNamespaceUris(OpcUaTestSystemOptions options) => options.SwapNamespaceOrder
        ? [SecondaryNamespaceUri, PrimaryNamespaceUri]
        : [PrimaryNamespaceUri, SecondaryNamespaceUri];

    /// <summary>
    /// Applies the two switches a browse response has to carry.
    /// <para>
    /// <see cref="OpcUaTestSystemOptions.OversizedFolder"/> caps how many references leave in a single response, so the
    /// folder can only be read in full by following the continuation point. The client asks for an unlimited number of
    /// references, so paging is the server's choice to make — which is exactly the case worth testing.
    /// </para>
    /// <para>
    /// <see cref="OpcUaTestSystemOptions.AbsoluteReference"/> rewrites the target of one reference into its absolute
    /// form. The stack will not do this for a reference declared as absolute in the address space: it cannot report the
    /// node class of a target it treats as remote, so such a reference is dropped from any browse that filters on node
    /// class — which every browse this client makes does. Rewriting the response instead produces what a server
    /// federating another address space returns: a fully populated reference whose node id carries a namespace URI and
    /// therefore has to be resolved before it can be used.
    /// </para>
    /// </summary>
    public override void Browse(OperationContext context, ref ContinuationPoint continuationPoint, IList<ReferenceDescription> references)
    {
        if (_options.OversizedFolder
            && continuationPoint is not null
            && (continuationPoint.MaxResultsToReturn == 0 || continuationPoint.MaxResultsToReturn > MaxReferencesPerBrowse))
        {
            continuationPoint.MaxResultsToReturn = MaxReferencesPerBrowse;
        }

        base.Browse(context, ref continuationPoint, references);

        if (!_options.AbsoluteReference)
            return;

        foreach (var reference in references)
        {
            if (reference.NodeId.IsAbsolute || reference.BrowseName?.Name != AbsoluteReferenceTargetName)
                continue;

            reference.NodeId = new ExpandedNodeId(reference.NodeId.Identifier, 0,
                Server.NamespaceUris.GetString(reference.NodeId.NamespaceIndex), 0);
        }
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            var root = CreateFolder(null, RootFolderName, RootFolderName);
            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            AddExternalReference(ObjectIds.ObjectsFolder, ReferenceTypeIds.Organizes, false, root.NodeId, externalReferences);

            if (_options.DuplicateDisplayNames)
            {
                CreateVariable(root, FirstDuplicateBrowseName, DuplicateDisplayName);
                CreateVariable(root, SecondDuplicateBrowseName, DuplicateDisplayName);
            }

            if (_options.MismatchedBrowseName)
                CreateVariable(root, MismatchedBrowseName, MismatchedDisplayName);

            if (_options.OversizedFolder)
            {
                var folder = CreateFolder(root, OversizedFolderName, OversizedFolderName);

                for (var i = 0; i < OversizedFolderChildCount; i++)
                {
                    var name = $"Child{i.ToString("D2", CultureInfo.InvariantCulture)}";
                    CreateVariable(folder, name, name);
                }
            }

            if (_options.AbsoluteReference)
            {
                var source = CreateFolder(root, AbsoluteReferenceSourceName, AbsoluteReferenceSourceName);
                CreateVariable(source, AbsoluteReferenceTargetName, AbsoluteReferenceTargetName);
            }

            AddPredefinedNode(SystemContext, root);
        }
    }

    private FolderState CreateFolder(NodeState? parent, string browseName, string displayName)
    {
        FolderState folder = new(parent)
        {
            NodeId = new NodeId(browseName, NamespaceIndexes[0]),
            BrowseName = new QualifiedName(browseName, NamespaceIndexes[0]),
            DisplayName = displayName,
            TypeDefinitionId = ObjectTypeIds.FolderType,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            EventNotifier = EventNotifiers.None,
        };

        parent?.AddChild(folder);

        return folder;
    }

    private BaseDataVariableState CreateVariable(NodeState parent, string browseName, string displayName)
    {
        BaseDataVariableState variable = new(parent)
        {
            NodeId = new NodeId(browseName, NamespaceIndexes[0]),
            BrowseName = new QualifiedName(browseName, NamespaceIndexes[0]),
            DisplayName = displayName,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            DataType = DataTypeIds.Int32,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentReadOrWrite,
            UserAccessLevel = AccessLevels.CurrentReadOrWrite,
            Value = 0,
        };

        parent.AddChild(variable);

        return variable;
    }
}
