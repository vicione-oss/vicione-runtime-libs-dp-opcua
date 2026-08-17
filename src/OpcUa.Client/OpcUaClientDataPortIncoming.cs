using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

public sealed class OpcUaClientDataPortIncoming : IExternalIncomingCommunication<OpcUaClientDataPortCommunication>
{
    private readonly OpcUaClientDataPortCommunication _communication;
    private readonly IOpcUaClientInstanceManager _instanceManager;
    private readonly ILogger<IOpcUaClient> _clientLogger;
    private IOpcUaClient? _client;

    public event Action<IReadOnlyCollection<ExternalValue>>? Received;

    public OpcUaClientDataPortIncoming(OpcUaClientDataPortCommunication communication, ILoggerFactory loggerFactory) : this(communication, loggerFactory.CreateLogger<IOpcUaClient>(), OpcUaClientInstanceManager.Instance)
    { }

    internal OpcUaClientDataPortIncoming(OpcUaClientDataPortCommunication communication, ILogger<IOpcUaClient> logger, IOpcUaClientInstanceManager instanceManager)
    {
        _communication = communication;
        _instanceManager = instanceManager;
        _clientLogger = logger;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            return;

        _client = await _instanceManager.GetOrRegisterOpcUaClientAsync(_communication, this, _clientLogger, cancellationToken).ConfigureAwait(false);

        try
        {
            await SubscribeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollBackConnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            await _instanceManager.ReleaseOpcUaClientAsync(_communication, this, cancellationToken).ConfigureAwait(false);
            _client = null;
        }
    }

    private async Task RollBackConnectAsync()
    {
        try
        {
            // Without a token so that a connect cancelled by the caller is rolled back too.
            await _instanceManager.ReleaseOpcUaClientAsync(_communication, this, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _clientLogger.LogReleaseAfterFailedConnectFailure(_communication.ApplicationName, ex);
        }
        finally
        {
            _client = null;
        }
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
            throw new InvalidOperationException("Connection must be initialized");

        var browsedNodes = await _client.BrowseNodesAsync(cancellationToken);
        var currentOpcNodes = browsedNodes;

        foreach (var route in _communication.Nodes.GetRoutes())
        {
            foreach (var dataPortNode in route)
            {
                if (dataPortNode.DesignId == OpcUaClientNodeDesignId.Folder)
                {
                    currentOpcNodes = GetOpcUaNode(dataPortNode).Children;
                    continue;
                }

                var channel = dataPortNode.AffectedChannels.SingleOrDefault()
                    ?? throw new InvalidOperationException($"Node '{dataPortNode.Name}' ({dataPortNode.Id}) has more than one affected channel.");

                await _client.SubscribeAsync((NodeId)GetOpcUaNode(dataPortNode).ReferenceDescription.NodeId, (value, timestamp) =>
                    Received?.Invoke([new() {
                        Channel = channel,
                        Value = value,
                        Timestamp = timestamp,
                        Validity = 1,
                    }]), cancellationToken)
                    .ConfigureAwait(false);
            }

            currentOpcNodes = browsedNodes;
        }

        OpcUaNode GetOpcUaNode(INode dataPortNode)
            => currentOpcNodes.FirstOrDefault(n => n.ReferenceDescription.DisplayName.Text == dataPortNode.Name)
                ?? throw new InvalidOperationException($"Cannot find node '{dataPortNode.Name}' ({dataPortNode.Id}) in OPC UA server.");
    }
}
