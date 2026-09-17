using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal interface IOpcUaServer
{
    event Action<ReceivedWrite> ReceiveValue;
    void AddNodes(IReadOnlyCollection<Node> nodes);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Serves a value with the status linked to it, or with none when <paramref name="statusCode"/>
    /// is <c>null</c>.
    /// </summary>
    Task PublishValueAsync(string channel, object? value, DateTime timestamp, StatusCode? statusCode, CancellationToken cancellationToken);

    Task SetNodeStatusAsync(string channel, StatusCode statusCode, CancellationToken cancellationToken);
}
