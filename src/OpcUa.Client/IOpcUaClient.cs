using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal interface IOpcUaClient
{
    Task<IReadOnlyCollection<OpcUaNode>> BrowseNodesAsync(CancellationToken cancellationToken);
    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task SubscribeAsync(NodeId nodeId, Action<OpcUaValue> callback, CancellationToken cancellationToken);
    Task WriteValuesAsync(IEnumerable<OpcUaWrite> writes, CancellationToken cancellationToken);
}
