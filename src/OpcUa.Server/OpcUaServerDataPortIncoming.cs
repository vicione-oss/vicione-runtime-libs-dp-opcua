using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

public sealed class OpcUaServerDataPortIncoming : IExternalIncomingCommunication<OpcUaServerDataPortCommunication>, IAsyncDisposable
{
    private static readonly EnvelopeChildKind[] s_servedKinds = [EnvelopeChildKind.StatusCode, EnvelopeChildKind.SourceTimestamp];

    private readonly IOpcUaServerInstanceManager _instanceManager;
    private readonly ILogger<IOpcUaServer> _serverLogger;
    private readonly OpcUaServerDataPortCommunication _communication;
    private readonly EnvelopeChildren _envelopeChildren;
    private readonly IOpcUaServer _server;
    private bool _subscribed;

    public event Action<IReadOnlyCollection<ExternalValue>>? Received;

    public OpcUaServerDataPortIncoming(OpcUaServerDataPortCommunication communication, ILoggerFactory loggerFactory) : this(communication, OpcUaServerInstanceManager.Instance, loggerFactory.CreateLogger<IOpcUaServer>())
    { }

    internal OpcUaServerDataPortIncoming(OpcUaServerDataPortCommunication communication, IOpcUaServerInstanceManager instanceManager, ILogger<IOpcUaServer> logger)
    {
        _communication = communication;
        _instanceManager = instanceManager;
        _serverLogger = logger;

        // Resolving the tree before the server is registered keeps a configuration the port cannot
        // serve from reaching the address space at all.
        _envelopeChildren = EnvelopeChildren.Create(communication.Nodes, s_servedKinds);

        _server = _instanceManager.GetOrRegisterOpcUaServer(_communication, this, _serverLogger);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _instanceManager.StartOpcUaServer(_communication, this, cancellationToken).ConfigureAwait(false);

        if (_subscribed)
            return;

        _server.ReceiveValue += ReceiveValue;
        _subscribed = true;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        // The server only carries subscribers once it has started, so unsubscribing after a failed
        // connect throws and would keep the teardown from ever reaching the stop below.
        if (_subscribed)
        {
            _server.ReceiveValue -= ReceiveValue;
            _subscribed = false;
        }

        await _instanceManager.StopOpcUaServer(_communication, this, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Raises the written value and the values of its envelope children as one batch. A write only
    /// reaches here once the server has accepted it, which is what makes the value valid - the status
    /// code is what the client asserted about the value, not what this server thinks of the write.
    /// </summary>
    private void ReceiveValue(ReceivedWrite write)
        => Received?.Invoke(EnvelopeBatch.Of(
            new() { Channel = write.Channel, Timestamp = write.Timestamp, Value = write.Value, Validity = 1, },
            _envelopeChildren.Of(write.Channel),
            write.StatusCode,
            write.SourceTimestamp,
            serverTimestamp: DateTime.MinValue));

    public async ValueTask DisposeAsync()
        => await _instanceManager.ReleaseOpcUaServerAsync(_communication, this, CancellationToken.None).ConfigureAwait(false);
}
