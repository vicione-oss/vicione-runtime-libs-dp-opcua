using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

public sealed class OpcUaClientDataPortOutgoing : IExternalOutgoingCommunication<OpcUaClientDataPortCommunication>
{
    private static readonly EnvelopeChildKind[] s_servedKinds = [EnvelopeChildKind.StatusCode, EnvelopeChildKind.SourceTimestamp];

    private readonly OpcUaClientDataPortCommunication _communication;
    private readonly ILogger<IOpcUaClient> _clientLogger;
    private readonly IOpcUaClientInstanceManager _instanceManager;
    private readonly Dictionary<string, (INode DataPortNode, NodeId NodeId)> _channelNodes = [];

    // Read and written only before the first await of SendAsync. The engine starts the send cycles
    // of a port one after another, so each cycle resolves its writes against the envelope the
    // cycles before it left, even while an earlier one is still being written.
    private readonly Dictionary<string, StatusCode> _statusCodes = [];

    private readonly EnvelopeChildren _envelopeChildren;
    private IOpcUaClient? _client;

    public OpcUaClientDataPortOutgoing(OpcUaClientDataPortCommunication communication, ILoggerFactory loggerFactory) : this(communication, loggerFactory.CreateLogger<IOpcUaClient>(), OpcUaClientInstanceManager.Instance)
    { }

    internal OpcUaClientDataPortOutgoing(OpcUaClientDataPortCommunication communication, ILogger<IOpcUaClient> logger, IOpcUaClientInstanceManager instanceManager)
    {
        _communication = communication;
        _instanceManager = instanceManager;
        _clientLogger = logger;

        _envelopeChildren = EnvelopeChildren.Create(communication.Nodes, s_servedKinds);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            return;

        _client = await _instanceManager.GetOrRegisterOpcUaClientAsync(_communication, this, _clientLogger, cancellationToken).ConfigureAwait(false);

        try
        {
            CreateChannelNodes(_communication.Nodes.GetRoutes(), await _client.BrowseNodesAsync(cancellationToken).ConfigureAwait(false), _channelNodes);
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
            // The node ids are bound to the session and go with it. The status code the engine
            // last sent is not: dropping it would write the next value as good on a data point
            // whose last known status was bad.
            _channelNodes.Clear();
            _client = null;
        }
    }

    public async Task SendAsync(ulong engineCycle, IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
    {
        if (_client is null)
            throw new InvalidOperationException("OPC UA client is not initialized.");

        RememberStatusCodes(values);

        await _client.WriteValuesAsync(ResolveWrites(values, SourceTimestampsOf(values)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A status code has no write of its own; it is written with the value of its parent. It is a
    /// state that holds until it changes, and the engine sends a channel only in the cycle it
    /// changes in, so the last one is remembered and written with every value of that node until
    /// another arrives.
    /// </summary>
    private void RememberStatusCodes(IReadOnlyCollection<ExternalValue> values)
    {
        foreach (var value in values)
        {
            if (_envelopeChildren.TryGetChild(value.Channel, out var child) && child.Kind == EnvelopeChildKind.StatusCode)
                _statusCodes[child.ParentChannel] = OpcUaStatusCodes.ConvertToStatusCode(value.Value, _clientLogger);
        }
    }

    /// <summary>
    /// The source timestamps of this cycle by the channel of their parent. A timestamp belongs to
    /// the value it arrives with and is not remembered: the engine does not send one again that did
    /// not change, so carrying it over would write a later value for the time of an earlier one.
    /// </summary>
    private Dictionary<string, DateTime> SourceTimestampsOf(IReadOnlyCollection<ExternalValue> values)
    {
        Dictionary<string, DateTime> sourceTimestamps = [];

        foreach (var value in values)
        {
            if (_envelopeChildren.TryGetChild(value.Channel, out var child)
                && child.Kind == EnvelopeChildKind.SourceTimestamp
                && value.Value is DateTime sourceTimestamp)
            {
                sourceTimestamps[child.ParentChannel] = sourceTimestamp;
            }
        }

        return sourceTimestamps;
    }

    // Resolving every channel first keeps an unmapped one from surfacing inside the write, once
    // part of the batch has already been submitted.
    private List<OpcUaWrite> ResolveWrites(IReadOnlyCollection<ExternalValue> values, Dictionary<string, DateTime> sourceTimestamps)
    {
        List<OpcUaWrite> writes = new(values.Count);

        foreach (var value in values)
        {
            if (_envelopeChildren.IsChildChannel(value.Channel))
                continue;

            if (!_channelNodes.TryGetValue(value.Channel, out var channelNode))
                throw new InvalidOperationException($"Channel '{value.Channel}' is not mapped to an OPC UA node.");

            writes.Add(new(channelNode.NodeId, value.Value, StatusCodeOf(value.Channel), SourceTimestampOf(value.Channel, sourceTimestamps)));
        }

        return writes;
    }

    /// <summary>
    /// The status a value is written with. Nothing linked means the value is written as good, which
    /// is what a <see cref="DataValue"/> carries when no status is set on it.
    /// </summary>
    private StatusCode StatusCodeOf(string channel)
        => _statusCodes.TryGetValue(channel, out var statusCode) ? statusCode : StatusCodes.Good;

    /// <summary>
    /// The point in time a value is written for. No source timestamp in the cycle of the value
    /// leaves it unset, which is what a <see cref="DataValue"/> carries when no timestamp is set on
    /// it and what makes the receiving server stamp the value itself.
    /// </summary>
    private static DateTime SourceTimestampOf(string channel, Dictionary<string, DateTime> sourceTimestamps)
        => sourceTimestamps.TryGetValue(channel, out var sourceTimestamp) ? sourceTimestamp : DateTime.MinValue;

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
            _channelNodes.Clear();
            _client = null;
        }
    }

    private static void CreateChannelNodes(IReadOnlyCollection<IReadOnlyCollection<INode>> routes, IReadOnlyCollection<OpcUaNode> opcNodes, Dictionary<string, (INode DataPortNode, NodeId NodeId)> channelNodes)
    {
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
                // it to write to. A data point that carries children also ends more than one route,
                // and mapping it twice would report it as its own duplicate.
                if (EnvelopeChildren.IsEnvelopeChild(dataPortNode.DesignId) || !resolved.Add(dataPortNode.Id))
                    continue;

                // A data port linked in both directions is handed the data points of the other
                // direction too, without a channel of their own.
                if (dataPortNode.AffectedChannels.Count == 0)
                    continue;

                var channel = GetAffectedChannel(dataPortNode);
                var opcUaNode = GetOpcUaNode(dataPortNode);

                if (channelNodes.TryGetValue(channel, out var mappedNode))
                    throw new InvalidOperationException($"Channel '{channel}' is affected by node '{mappedNode.DataPortNode.Name}' ({mappedNode.DataPortNode.Id}) and node '{dataPortNode.Name}' ({dataPortNode.Id}).");

                channelNodes.Add(channel, (dataPortNode, opcUaNode.NodeId));
            }

            currentOpcNodes = opcNodes;
        }

        OpcUaNode GetOpcUaNode(INode dataPortNode)
            => currentOpcNodes.FirstOrDefault(n => n.DisplayName == dataPortNode.Name)
                ?? throw new InvalidOperationException($"Cannot find node '{dataPortNode.Name}' ({dataPortNode.Id}) in OPC UA server.");

        static string GetAffectedChannel(INode dataPortNode)
            => dataPortNode.AffectedChannels.Count == 1
                ? dataPortNode.AffectedChannels[0]
                : throw new InvalidOperationException($"Node '{dataPortNode.Name}' ({dataPortNode.Id}) has more than one affected channel: '{string.Join("', '", dataPortNode.AffectedChannels)}'.");
    }
}
