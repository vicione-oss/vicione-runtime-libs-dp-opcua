using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Opc.Ua;
using Opc.Ua.Client;
using Xunit;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// Covers the lifetime of the reconnect handler the client keeps for its keep-alive callback. A keep-alive arriving
/// once the client has let go of its session has to find nothing to do, rather than a handler that was disposed.
/// </summary>
[Collection(OpcUaTestEnvironment.Name)]
[Trait("Category", "Interoperability")]
public sealed class OpcUaClient_OnKeepAlive(OpcUaTestSystem opcUa)
{
    [Fact]
    public async Task Does_not_log_a_keep_alive_failure_after_a_disconnect_Async()
    {
        FakeLogger<IOpcUaClient> logger = new();
        using OpcUaClient client = new(opcUa.Communication, logger);

        await client.ConnectAsync(TestContext.Current.CancellationToken);
        await client.DisconnectAsync(TestContext.Current.CancellationToken);

        var keepAlive = BadKeepAlive();
        client.OnKeepAlive(Substitute.For<ISession>(), keepAlive);

        KeepAliveFailures(logger).Should().BeEmpty();

        // Set last in the callback, so it is only true when nothing before it threw.
        keepAlive.CancelKeepAlive.Should().BeTrue();
    }

    [Fact]
    public async Task Does_not_log_a_keep_alive_failure_after_repeated_connect_and_disconnect_cycles_Async()
    {
        FakeLogger<IOpcUaClient> logger = new();
        using OpcUaClient client = new(opcUa.Communication, logger);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            await client.ConnectAsync(TestContext.Current.CancellationToken);
            await client.DisconnectAsync(TestContext.Current.CancellationToken);

            client.OnKeepAlive(Substitute.For<ISession>(), BadKeepAlive());
        }

        KeepAliveFailures(logger).Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_log_a_keep_alive_failure_after_the_client_is_disposed_Async()
    {
        FakeLogger<IOpcUaClient> logger = new();
        OpcUaClient client = new(opcUa.Communication, logger);

        await client.ConnectAsync(TestContext.Current.CancellationToken);
        client.Dispose();

        client.OnKeepAlive(Substitute.For<ISession>(), BadKeepAlive());

        KeepAliveFailures(logger).Should().BeEmpty();
    }

    /// <summary>
    /// The other way round, so the teardown cannot be "simplified" into never keeping a handler at all.
    /// </summary>
    [Fact]
    public async Task Hands_a_bad_keep_alive_to_a_usable_handler_while_connected_Async()
    {
        FakeLogger<IOpcUaClient> logger = new();
        using OpcUaClient client = new(opcUa.Communication, logger);

        await client.ConnectAsync(TestContext.Current.CancellationToken);

        try
        {
            var keepAlive = BadKeepAlive();
            client.OnKeepAlive(Substitute.For<ISession>(), keepAlive);

            KeepAliveFailures(logger).Should().BeEmpty();
            keepAlive.CancelKeepAlive.Should().BeTrue();
        }
        finally
        {
            // Cancels the reconnect the keep-alive just started, well inside the reconnect interval.
            await client.DisconnectAsync(TestContext.Current.CancellationToken);
        }
    }

    private static KeepAliveEventArgs BadKeepAlive() => new(new ServiceResult(StatusCodes.BadConnectionClosed), ServerState.CommunicationFault, DateTime.UtcNow);

    // The captured log carries the SDK's output too: connecting installs the logger globally through Utils.SetLogger.
    private static IEnumerable<FakeLogRecord> KeepAliveFailures(FakeLogger logger) => logger.Collector.GetSnapshot().Where(record => record.Message.Contains("Keep alive action failed", StringComparison.Ordinal));
}

/// <summary>
/// Covers the browse of a server's address space. What a real server hands back is shaped by limits the client does
/// not control, so the tests here drive servers that page, cycle and nest.
/// </summary>
[Trait("Category", "Interoperability")]
public sealed class OpcUaClient_BrowseNodesAsync
{
    [Fact]
    public async Task Returns_every_child_of_a_folder_the_server_answers_in_pages_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.OversizedFolder = true);
        using OpcUaClient client = new(opcUa.Communication);

        await client.ConnectAsync(TestContext.Current.CancellationToken);

        try
        {
            var nodes = await client.BrowseNodesAsync(TestContext.Current.CancellationToken);

            var folder = Flatten(nodes).Should().ContainSingle(node => node.DisplayName == OpcUaTestNodeManager.OversizedFolderName).Subject;

            folder.Children.Should().HaveCount(OpcUaTestNodeManager.OversizedFolderChildCount);
        }
        finally
        {
            await client.DisconnectAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Returns_the_nodes_of_an_address_space_nested_to_the_maximum_depth_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.DeepFolderChainDepth = OpcUaClient.MaxDepth);
        using OpcUaClient client = new(opcUa.Communication);

        await client.ConnectAsync(TestContext.Current.CancellationToken);

        try
        {
            var nodes = await client.BrowseNodesAsync(TestContext.Current.CancellationToken);

            Flatten(nodes).Should().ContainSingle(node => node.DisplayName == OpcUaTestNodeManager.DeepChainFolderNameAt(OpcUaClient.MaxDepth));
        }
        finally
        {
            await client.DisconnectAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Fails_naming_the_path_when_the_address_space_is_nested_past_the_maximum_depth_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.DeepFolderChainDepth = OpcUaClient.MaxDepth + 1);
        using OpcUaClient client = new(opcUa.Communication);

        await client.ConnectAsync(TestContext.Current.CancellationToken);

        try
        {
            var browse = () => client.BrowseNodesAsync(TestContext.Current.CancellationToken);

            var message = (await browse.Should().ThrowAsync<InvalidOperationException>()).Which.Message;

            message.Should().Contain(OpcUaTestNodeManager.DeepChainFolderNameAt(OpcUaClient.MaxDepth + 1));
            message.Should().Contain(OpcUaTestNodeManager.DeepChainFolderNameAt(OpcUaClient.MaxDepth));
        }
        finally
        {
            await client.DisconnectAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task Stops_at_a_node_that_is_already_on_the_browsed_path_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.CyclicReferences = true);
        FakeLogger<IOpcUaClient> logger = new();
        using OpcUaClient client = new(opcUa.Communication, logger);

        await client.ConnectAsync(TestContext.Current.CancellationToken);

        try
        {
            var nodes = await client.BrowseNodesAsync(TestContext.Current.CancellationToken);

            var cycleEnd = Flatten(nodes).Should().ContainSingle(node => node.DisplayName == OpcUaTestNodeManager.SecondCycleFolderName).Subject;

            cycleEnd.Children.Should().BeEmpty();

            logger.Collector.GetSnapshot().Should().ContainSingle(record =>
                record.Level == LogLevel.Warning &&
                record.Message.Contains(OpcUaTestNodeManager.FirstCycleFolderName, StringComparison.Ordinal) &&
                record.Message.Contains(opcUa.Communication.ApplicationName, StringComparison.Ordinal) &&
                record.Message.Contains("already on that path", StringComparison.Ordinal));
        }
        finally
        {
            await client.DisconnectAsync(TestContext.Current.CancellationToken);
        }
    }

    private static IEnumerable<OpcUaNode> Flatten(IEnumerable<OpcUaNode> nodes) => nodes.SelectMany(node => Flatten(node.Children).Prepend(node));
}


/// <summary>
/// Drives the address space walk against a substituted session, so the traversal rules can be asserted without a
/// server. That a real server serves these shapes at all is covered by <see cref="OpcUaClient_BrowseNodesAsync"/>.
/// </summary>
public sealed class OpcUaClient_BrowseAddressSpaceAsync
{
    private const string ApplicationName = "OPC UA Unit Test Client";

    [Fact]
    public async Task Stops_at_a_node_that_is_already_on_the_current_path_Async()
    {
        FakeLogger<IOpcUaClient> logger = new();
        using var client = CreateClient(logger);
        var session = SessionBrowsing(new()
        {
            [ObjectIds.ObjectsFolder] = ["First",],
            [NodeIdOf("First")] = ["Second",],
            [NodeIdOf("Second")] = ["First",],
        });

        var nodes = await client.BrowseAddressSpaceAsync(session, TestContext.Current.CancellationToken);

        var second = nodes.Should().ContainSingle().Which.Children.Should().ContainSingle().Subject;

        second.DisplayName.Should().Be("Second");
        second.Children.Should().BeEmpty();

        logger.Collector.GetSnapshot().Should().ContainSingle(record =>
            record.Level == LogLevel.Warning &&
            record.Message.Contains(ApplicationName, StringComparison.Ordinal) &&
            record.Message.Contains("First", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Returns_a_chain_nested_to_the_maximum_depth_Async()
    {
        using var client = CreateClient();
        var session = SessionBrowsing(Chain(OpcUaClient.MaxDepth));

        var nodes = await client.BrowseAddressSpaceAsync(session, TestContext.Current.CancellationToken);

        Depth(nodes).Should().Be(OpcUaClient.MaxDepth);
    }

    [Fact]
    public async Task Fails_naming_the_path_when_a_chain_is_nested_past_the_maximum_depth_Async()
    {
        var client = CreateClient();
        var session = SessionBrowsing(Chain(OpcUaClient.MaxDepth + 1));

        try
        {
            var browse = client.Awaiting(browser => browser.BrowseAddressSpaceAsync(session, TestContext.Current.CancellationToken));

            var message = (await browse.Should().ThrowAsync<InvalidOperationException>()).Which.Message;

            message.Should().Contain(LevelName(OpcUaClient.MaxDepth + 1));
            message.Should().Contain(LevelName(OpcUaClient.MaxDepth));
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Fails_naming_the_node_when_the_server_rejects_a_browse_Async()
    {
        var client = CreateClient();
        var session = SessionFailing(StatusCodes.BadNodeIdUnknown);

        try
        {
            var browse = client.Awaiting(browser => browser.BrowseAddressSpaceAsync(session, TestContext.Current.CancellationToken));

            (await browse.Should().ThrowAsync<InvalidOperationException>()).Which.Message
                .Should().Contain(ObjectIds.ObjectsFolder.ToString());
        }
        finally
        {
            client.Dispose();
        }
    }

    private static OpcUaClient CreateClient(ILogger<IOpcUaClient>? logger = null)
        => new(new OpcUaClientDataPortCommunication { ApplicationName = ApplicationName, }, logger);

    /// <summary>Names a chain node for the depth it sits at, so an error message can be checked against that depth.</summary>
    private static string LevelName(int depth) => "Level" + depth.ToString("D2", CultureInfo.InvariantCulture);

    /// <summary>A single chain of folders whose deepest node sits <paramref name="depth"/> levels below the Objects folder.</summary>
    private static Dictionary<NodeId, string[]> Chain(int depth)
    {
        Dictionary<NodeId, string[]> children = new() { [ObjectIds.ObjectsFolder] = [LevelName(1),], };

        for (var level = 1; level < depth; level++)
            children[NodeIdOf(LevelName(level))] = [LevelName(level + 1),];

        return children;
    }

    private static int Depth(IReadOnlyCollection<OpcUaNode> nodes) => nodes.Count == 0 ? 0 : 1 + nodes.Max(node => Depth(node.Children));

    private static NodeId NodeIdOf(string displayName) => new(displayName, 0);

    private static ISession SessionBrowsing(Dictionary<NodeId, string[]> children)
        => Session(nodeId => (References(children.TryGetValue(nodeId, out var names) ? names : []), ServiceResult.Good));

    private static ISession SessionFailing(uint statusCode) => Session(_ => (null, new ServiceResult(statusCode)));

    private static ISession Session(Func<NodeId, (ReferenceDescriptionCollection? References, ServiceResult Error)> browse)
    {
        var session = Substitute.For<ISession>();
        session.NamespaceUris.Returns(new NamespaceTable());
        session.ManagedBrowseAsync(
                Arg.Any<RequestHeader>(), Arg.Any<ViewDescription>(), Arg.Any<IList<NodeId>>(), Arg.Any<uint>(),
                Arg.Any<BrowseDirection>(), Arg.Any<NodeId>(), Arg.Any<bool>(), Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var (references, error) = browse(call.Arg<IList<NodeId>>()[0]);

                IList<ReferenceDescriptionCollection> results = [references!,];
                IList<ServiceResult> errors = [error,];

                return (results, errors);
            });

        return session;
    }

    private static ReferenceDescriptionCollection References(string[] displayNames)
        => [.. displayNames.Select(name => new ReferenceDescription { NodeId = new ExpandedNodeId(NodeIdOf(name)), DisplayName = name, })];
}
