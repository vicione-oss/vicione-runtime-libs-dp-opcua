using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Xunit;

namespace ViciOne.Suite.DataPort;

[Trait("Category", "Interoperability")]
public sealed class OpcUaTestSystem_
{
    [Fact]
    public async Task Serves_siblings_that_share_a_display_name_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.DuplicateDisplayNames = true);
        using var session = await ConnectAsync(opcUa);

        var children = Browse(session, RootFolderId(session));

        var duplicates = children.Where(reference => reference.DisplayName.Text == OpcUaTestNodeManager.DuplicateDisplayName).ToList();

        duplicates.Should().HaveCount(2);
        duplicates.Select(reference => reference.BrowseName.Name).Should().BeEquivalentTo(
        [
            OpcUaTestNodeManager.FirstDuplicateBrowseName,
            OpcUaTestNodeManager.SecondDuplicateBrowseName,
        ]);
    }

    [Fact]
    public async Task Serves_a_node_whose_browse_name_and_display_name_differ_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.MismatchedBrowseName = true);
        using var session = await ConnectAsync(opcUa);

        var children = Browse(session, RootFolderId(session));

        var mismatched = children.Should().ContainSingle(reference => reference.BrowseName.Name == OpcUaTestNodeManager.MismatchedBrowseName).Which;

        mismatched.DisplayName.Text.Should().Be(OpcUaTestNodeManager.MismatchedDisplayName);
        mismatched.DisplayName.Text.Should().NotBe(mismatched.BrowseName.Name);
    }

    [Fact]
    public async Task Serves_a_folder_that_needs_more_than_one_browse_response_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.OversizedFolder = true);
        using var session = await ConnectAsync(opcUa);

        var folderId = FindChild(session, RootFolderId(session), OpcUaTestNodeManager.OversizedFolderName);

        session.Browse(null, null, folderId, 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true,
            (uint)NodeClass.Variable | (uint)NodeClass.Object, out var continuationPoint, out var firstPage);

        // The first response is short and carries a continuation point, which is the case the client has to follow.
        continuationPoint.Should().NotBeNullOrEmpty();
        firstPage.Should().HaveCount((int)OpcUaTestNodeManager.MaxReferencesPerBrowse);

        ReferenceDescriptionCollection all = [.. firstPage];

        while (continuationPoint is not null && continuationPoint.Length > 0)
        {
            session.BrowseNext(null, false, continuationPoint, out continuationPoint, out var nextPage);
            all.AddRange(nextPage);
        }

        all.Should().HaveCount(OpcUaTestNodeManager.OversizedFolderChildCount);
    }

    [Fact]
    public async Task Serves_a_reference_to_an_absolute_node_id_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.AbsoluteReference = true);
        using var session = await ConnectAsync(opcUa);

        var sourceId = FindChild(session, RootFolderId(session), OpcUaTestNodeManager.AbsoluteReferenceSourceName);

        var reference = Browse(session, sourceId).Should().ContainSingle().Which;

        reference.NodeId.IsAbsolute.Should().BeTrue();
        reference.NodeId.NamespaceUri.Should().Be(OpcUaTestNodeManager.PrimaryNamespaceUri);

        // The reference has to survive the node class filter the client browses with, or the client never sees it.
        reference.BrowseName.Name.Should().Be(OpcUaTestNodeManager.AbsoluteReferenceTargetName);

        // Resolving it against the session's namespace table is what turns it into something usable.
        ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris).Should().NotBeNull();
    }

    [Fact]
    public async Task Serves_the_same_namespace_at_a_different_index_after_a_restart_Async()
    {
        ushort indexInOrder;
        await using (var opcUa = await OpcUaTestSystem.StartAsync(options => options.DuplicateDisplayNames = true))
        {
            using var session = await ConnectAsync(opcUa);
            indexInOrder = (ushort)session.NamespaceUris.GetIndex(OpcUaTestNodeManager.PrimaryNamespaceUri);
        }

        ushort indexSwapped;
        await using (var opcUa = await OpcUaTestSystem.StartAsync(options =>
        {
            options.DuplicateDisplayNames = true;
            options.SwapNamespaceOrder = true;
        }))
        {
            using var session = await ConnectAsync(opcUa);
            indexSwapped = (ushort)session.NamespaceUris.GetIndex(OpcUaTestNodeManager.PrimaryNamespaceUri);
        }

        indexSwapped.Should().NotBe(indexInOrder);
    }

    private static NodeId RootFolderId(ISession session) => FindChild(session, ObjectIds.ObjectsFolder, OpcUaTestNodeManager.RootFolderName);

    private static NodeId FindChild(ISession session, NodeId parent, string browseName)
    {
        var reference = Browse(session, parent).Should().ContainSingle(reference => reference.BrowseName.Name == browseName).Which;

        return ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
    }

    /// <summary>
    /// Browses the way the production client does, node class mask included — a mask changes which references a server
    /// is able to return, so testing without one would not test what the client sees.
    /// </summary>
    private static ReferenceDescriptionCollection Browse(ISession session, NodeId nodeId)
    {
        session.Browse(null, null, nodeId, 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true,
            (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method, out _, out var references);

        return references;
    }

    /// <summary>
    /// Opens a plain anonymous session against the fixture, bypassing the production client entirely.
    /// </summary>
    private static async Task<ISession> ConnectAsync(OpcUaTestSystem opcUa)
    {
        ApplicationConfiguration configuration = new()
        {
            ApplicationName = "OPC UA Fixture Test Client",
            ApplicationUri = "urn:localhost:OPCUA:DataPortFixtureTest",
            ApplicationType = ApplicationType.Client,
            TransportQuotas = new(),
            ClientConfiguration = new() { DefaultSessionTimeout = 60_000, },
        };
        configuration.SecurityConfiguration.ApplicationCertificate = new() { StoreType = CertificateStoreType.X509Store, };
        configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;

        await configuration.Validate(ApplicationType.Client);
        ApplicationInstance applicationInstance = new(configuration);
        await applicationInstance.CheckApplicationInstanceCertificates(false, CertificateFactory.DefaultLifeTime);

        var url = $"opc.tcp://{opcUa.Communication.Server}:{opcUa.Communication.Port.ToString(CultureInfo.InvariantCulture)}/{opcUa.Communication.Endpoint}";
        var selectedEndpoint = CoreClientUtils.SelectEndpoint(configuration, url, false);
        ConfiguredEndpoint endpoint = new(null, selectedEndpoint, EndpointConfiguration.Create(configuration));

        return await Session.Create(configuration, endpoint, true, false, configuration.ApplicationName, 60_000, new UserIdentity(), null);
    }
}
