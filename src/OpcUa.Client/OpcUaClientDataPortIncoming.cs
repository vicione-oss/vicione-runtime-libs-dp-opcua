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
    private static readonly EnvelopeChildKind[] s_servedKinds = [EnvelopeChildKind.StatusCode, EnvelopeChildKind.SourceTimestamp, EnvelopeChildKind.ServerTimestamp];

    private readonly OpcUaClientDataPortCommunication _communication;
    private readonly IOpcUaClientInstanceManager _instanceManager;
    private readonly ILogger<IOpcUaClient> _clientLogger;
    private readonly EnvelopeChildren _envelopeChildren;
    private IOpcUaClient? _client;

    public event Action<IReadOnlyCollection<ExternalValue>>? Received;

    public OpcUaClientDataPortIncoming(OpcUaClientDataPortCommunication communication, ILoggerFactory loggerFactory) : this(communication, loggerFactory.CreateLogger<IOpcUaClient>(), OpcUaClientInstanceManager.Instance)
    { }

    internal OpcUaClientDataPortIncoming(OpcUaClientDataPortCommunication communication, ILogger<IOpcUaClient> logger, IOpcUaClientInstanceManager instanceManager)
    {
        _communication = communication;
        _instanceManager = instanceManager;
        _clientLogger = logger;

        // Resolving the tree before the first connect keeps a configuration the port cannot serve
        // from reaching a server at all.
        _envelopeChildren = EnvelopeChildren.Create(communication.Nodes, s_servedKinds);
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
            var children = _envelopeChildren.Of(channel);

            await _client.SubscribeAsync(opcUaNode.NodeId, value => ReceiveValue(channel, children, value), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Raises the value of the data point and the values of its envelope children as one batch. The
    /// value is reported valid whatever status the server sent with it; that status reaches the
    /// engine on the Status code child.
    /// </summary>
    private void ReceiveValue(string channel, IReadOnlyList<EnvelopeChild> children, OpcUaValue value)
        => Received?.Invoke(EnvelopeBatch.Of(
            new() { Channel = channel, Value = value.Value, Timestamp = value.Timestamp, Validity = 1, },
            children,
            value.StatusCode,
            value.SourceTimestamp,
            value.ServerTimestamp));

    private static List<(string Channel, OpcUaNode OpcUaNode)> ResolveChannelNodes(IReadOnlyCollection<IReadOnlyCollection<INode>> routes, IReadOnlyCollection<OpcUaNode> opcNodes)
    {
        List<(string Channel, OpcUaNode OpcUaNode)> channelNodes = [];
        HashSet<Guid> resolved = [];
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

                // An envelope child addresses the value of its parent, so the server has no node of
                // it to subscribe to. A data point that carries children also ends more than one
                // route, and subscribing it once per route would double every value it receives.
                if (EnvelopeChildren.IsEnvelopeChild(dataPortNode.DesignId) || !resolved.Add(dataPortNode.Id))
                    continue;

                // A data port linked in both directions is handed the data points of the other
                // direction too, without a channel of their own.
                if (dataPortNode.AffectedChannels.Count == 0)
                    continue;

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
            => dataPortNode.AffectedChannels.Count == 1
                ? dataPortNode.AffectedChannels[0]
                : throw new InvalidOperationException($"Node '{dataPortNode.Name}' ({dataPortNode.Id}) has more than one affected channel: '{string.Join("', '", dataPortNode.AffectedChannels)}'.");
    }
}
