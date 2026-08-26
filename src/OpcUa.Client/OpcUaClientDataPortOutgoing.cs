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
    private readonly OpcUaClientDataPortCommunication _communication;
    private readonly ILogger<IOpcUaClient> _clientLogger;
    private readonly IOpcUaClientInstanceManager _instanceManager;
    private readonly Dictionary<string, (INode DataPortNode, NodeId NodeId)> _channelNodes = [];
    private IOpcUaClient? _client;

    public OpcUaClientDataPortOutgoing(OpcUaClientDataPortCommunication communication, ILoggerFactory loggerFactory) : this(communication, loggerFactory.CreateLogger<IOpcUaClient>(), OpcUaClientInstanceManager.Instance)
    { }

    internal OpcUaClientDataPortOutgoing(OpcUaClientDataPortCommunication communication, ILogger<IOpcUaClient> logger, IOpcUaClientInstanceManager instanceManager)
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
            _channelNodes.Clear();
            _client = null;
        }
    }

    public async Task SendAsync(ulong engineCycle, IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
    {
        if (_client is null)
            throw new InvalidOperationException("OPC UA client is not initialized.");

        await _client.WriteValuesAsync(ResolveValues(values), cancellationToken).ConfigureAwait(false);
    }

    // Resolving every channel first keeps an unmapped one from surfacing inside the write, once
    // part of the batch has already been submitted.
    private List<(NodeId NodeId, object? Value)> ResolveValues(IReadOnlyCollection<ExternalValue> values)
    {
        List<(NodeId NodeId, object? Value)> resolvedValues = new(values.Count);

        foreach (var value in values)
        {
            if (!_channelNodes.TryGetValue(value.Channel, out var channelNode))
                throw new InvalidOperationException($"Channel '{value.Channel}' is not mapped to an OPC UA node.");

            resolvedValues.Add((channelNode.NodeId, value.Value));
        }

        return resolvedValues;
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
            _channelNodes.Clear();
            _client = null;
        }
    }

    private static void CreateChannelNodes(IReadOnlyCollection<IReadOnlyCollection<INode>> routes, IReadOnlyCollection<OpcUaNode> opcNodes, Dictionary<string, (INode DataPortNode, NodeId NodeId)> channelNodes)
    {
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
            => dataPortNode.AffectedChannels.Count switch
            {
                1 => dataPortNode.AffectedChannels[0],
                0 => throw new InvalidOperationException($"Node '{dataPortNode.Name}' ({dataPortNode.Id}) has no affected channel."),
                _ => throw new InvalidOperationException($"Node '{dataPortNode.Name}' ({dataPortNode.Id}) has more than one affected channel: '{string.Join("', '", dataPortNode.AffectedChannels)}'.")
            };
    }
}
