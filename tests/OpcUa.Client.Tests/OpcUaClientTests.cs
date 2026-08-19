using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
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
