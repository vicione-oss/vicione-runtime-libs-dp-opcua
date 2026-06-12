using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

public sealed class OpcUaServerDataPortOutgoing : DataPortOutgoingCommunicationWithPropertyHandling<OpcUaServerDataPortCommunication>, IAsyncDisposable
{
    private readonly IOpcUaServerInstanceManager _instanceManager;
    private readonly ILogger<IOpcUaServer> _serverLogger;
    private readonly OpcUaServerDataPortCommunication _communication;
    private readonly Dictionary<string, string> _statusChannelNodeChannel = [];
    private readonly IOpcUaServer _server;

    public OpcUaServerDataPortOutgoing(OpcUaServerDataPortCommunication communication, ILoggerFactory loggerFactory) : this(communication, OpcUaServerInstanceManager.Instance, loggerFactory.CreateLogger<IOpcUaServer>())
    { }

    internal OpcUaServerDataPortOutgoing(OpcUaServerDataPortCommunication communication, IOpcUaServerInstanceManager instanceManager, ILogger<IOpcUaServer> logger) : base(communication)
    {
        _communication = communication;
        _instanceManager = instanceManager;
        _serverLogger = logger;

        foreach (var node in communication.Nodes.Where(n => n.DesignId is OpcUaServerNodeDesignId.Variable))
        {
            if (node.Properties.TryGetValue(OpcUaServerDataPortPropertyNames.Status, out var statusProperty))
            {
                foreach (var statusChannel in statusProperty.Channels)
                    _statusChannelNodeChannel.Add(statusChannel, node.AffectedChannels.Single());
            }
        }

        _server = _instanceManager.GetOrRegisterOpcUaServer(_communication, this, _serverLogger);
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken)
        => await _instanceManager.StartOpcUaServer(_communication, cancellationToken).ConfigureAwait(false);

    public override async Task DisconnectAsync(CancellationToken cancellationToken)
        => await _instanceManager.StopOpcUaServer(_communication, cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
        => await _instanceManager.ReleaseOpcUaServerAsync(_communication, this, CancellationToken.None).ConfigureAwait(false);

    public override async Task SendAsync(ulong engineCycle, IReadOnlyCollection<ExternalValue> processValues, IReadOnlyCollection<ExternalValue> propertyValues, CancellationToken cancellationToken)
    {
        try
        {
            if (_server is null)
                throw new InvalidOperationException("Connection must be initialized");

            foreach (var processValue in processValues)
            {
                await _server.PublishValueAsync(processValue.Channel, processValue.Value, processValue.Timestamp, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    return;
            }

            foreach (var propertyValue in propertyValues)
            {
                if (_statusChannelNodeChannel.TryGetValue(propertyValue.Channel, out var nodeChannel))
                    await _server.SetNodeStatusAsync(nodeChannel, propertyValue.Value, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    return;
            }
        }
        catch (Exception ex)
        {
            _serverLogger.LogSendFailure(_communication.Server, _communication.Port, ex);
        }
    }
}
