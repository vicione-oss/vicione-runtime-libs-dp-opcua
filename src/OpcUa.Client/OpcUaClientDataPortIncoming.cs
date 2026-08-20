using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

        // Resolving every node first keeps a failing resolution from leaving monitored items on the
        // client, which is shared with the outgoing side and outlives the roll back.
        foreach (var (channel, opcUaNode) in ResolveChannelNodes(_communication.Nodes.GetRoutes(), browsedNodes))
        {
            await _client.SubscribeAsync(opcUaNode.NodeId, (value, timestamp) =>
                Received?.Invoke([new() {
                    Channel = channel,
                    Value = value,
                    Timestamp = timestamp,
                    Validity = 1,
                }]), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static List<(string Channel, OpcUaNode OpcUaNode)> ResolveChannelNodes(IReadOnlyCollection<IReadOnlyCollection<INode>> routes, IReadOnlyCollection<OpcUaNode> opcNodes)
    {
        List<(string Channel, OpcUaNode OpcUaNode)> channelNodes = [];
        var currentOpcNodes = opcNodes;

        foreach (var route in routes)
        {
            foreach (var dataPortNode in route)
            {
                if (dataPortNode.DesignId == OpcUaClientNodeDesignId.Folder)
                {
                    currentOpcNodes = GetOpcUaNode(dataPortNode).Children;
                    continue;
                }

                var channel = GetAffectedChannel(dataPortNode);

                channelNodes.Add((channel, GetOpcUaNode(dataPortNode)));
            }

            currentOpcNodes = opcNodes;
        }

        return channelNodes;

        OpcUaNode GetOpcUaNode(INode dataPortNode)
            => currentOpcNodes.FirstOrDefault(n => n.DisplayName == dataPortNode.Name)
                ?? throw new InvalidOperationException($"Cannot find node '{dataPortNode.Name}' ({dataPortNode.Id}) in OPC UA server.");

        static string GetAffectedChannel(INode dataPortNode)
            => dataPortNode.AffectedChannels.Count switch
            {
                1 => dataPortNode.AffectedChannels[0],
                0 => throw new InvalidOperationException($"Node '{dataPortNode.Name}' ({dataPortNode.Id}) has no affected channel."),
                _ => throw new InvalidOperationException($"Node '{dataPortNode.Name}' ({dataPortNode.Id}) has more than one affected channel: '{string.Join("', '", dataPortNode.AffectedChannels)}'.")
            };
    }
}
