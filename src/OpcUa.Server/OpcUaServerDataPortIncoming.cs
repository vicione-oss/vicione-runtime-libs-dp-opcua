using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

public sealed class OpcUaServerDataPortIncoming : IExternalIncomingCommunication<OpcUaServerDataPortCommunication>, IAsyncDisposable
{
    private readonly IOpcUaServerInstanceManager _instanceManager;
    private readonly ILogger<IOpcUaServer> _serverLogger;
    private readonly OpcUaServerDataPortCommunication _communication;
    private readonly IOpcUaServer _server;

    public event Action<IReadOnlyCollection<ExternalValue>>? Received;

    public OpcUaServerDataPortIncoming(OpcUaServerDataPortCommunication communication, ILoggerFactory loggerFactory) : this(communication, OpcUaServerInstanceManager.Instance, loggerFactory.CreateLogger<IOpcUaServer>())
    { }

    internal OpcUaServerDataPortIncoming(OpcUaServerDataPortCommunication communication, IOpcUaServerInstanceManager instanceManager, ILogger<IOpcUaServer> logger)
    {
        _communication = communication;
        _instanceManager = instanceManager;
        _serverLogger = logger;
        _server = _instanceManager.GetOrRegisterOpcUaServer(_communication, this, _serverLogger);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _instanceManager.StartOpcUaServer(_communication, cancellationToken).ConfigureAwait(false);
        _server.ReceiveValue += ReceiveValue;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _server.ReceiveValue -= ReceiveValue;
        await _instanceManager.StopOpcUaServer(_communication, cancellationToken).ConfigureAwait(false);
    }

    private void ReceiveValue(string channel, DateTime timestamp, object value)
        => Received?.Invoke([new() { Channel = channel, Timestamp = timestamp, Value = value, Validity = 1, }]);

    public async ValueTask DisposeAsync()
        => await _instanceManager.ReleaseOpcUaServerAsync(_communication, this, CancellationToken.None).ConfigureAwait(false);
}
