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
    private readonly Dictionary<string, NodeId> _channelNodes = [];
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
        if (_client is null)
        {
            _client = await _instanceManager.GetOrRegisterOpcUaClientAsync(_communication, this, _clientLogger, cancellationToken).ConfigureAwait(false);
            CreateChannelNodes(_communication.Nodes.GetRoutes(), await _client.BrowseNodesAsync(cancellationToken).ConfigureAwait(false), _channelNodes);
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

    public async Task SendAsync(ulong engineCycle, IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
    {
        if (_client is null)
            throw new InvalidOperationException("OPC UA client is not initialized.");

        await _client.WriteValuesAsync(ConvertValues(), cancellationToken).ConfigureAwait(false);

        IEnumerable<(NodeId NodeId, object? Value)> ConvertValues()
        {
            foreach (var value in values)
                yield return (_channelNodes[value.Channel], value.Value);
        }
    }

    private static void CreateChannelNodes(IReadOnlyCollection<IReadOnlyCollection<INode>> routes, IReadOnlyCollection<OpcUaNode> opcNodes, Dictionary<string, NodeId> channelNodes)
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

                var channel = dataPortNode.AffectedChannels.SingleOrDefault()
                    ?? throw new InvalidOperationException($"Node '{dataPortNode.Name}' ({dataPortNode.Id}) has more than one affected channel.");

                channelNodes.Add(channel, (NodeId)GetOpcUaNode(dataPortNode).ReferenceDescription.NodeId);
            }

            currentOpcNodes = opcNodes;
        }

        OpcUaNode GetOpcUaNode(INode dataPortNode)
            => currentOpcNodes.FirstOrDefault(n => n.ReferenceDescription.DisplayName.Text == dataPortNode.Name)
                ?? throw new InvalidOperationException($"Cannot find node '{dataPortNode.Name}' ({dataPortNode.Id}) in OPC UA server.");
    }
}
