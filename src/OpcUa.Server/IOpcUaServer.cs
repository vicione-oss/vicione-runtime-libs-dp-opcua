using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ViciOne.Suite.DataPort;

internal interface IOpcUaServer
{
    event Action<string, DateTime, object> ReceiveValue;
    void AddNodes(IReadOnlyCollection<Node> nodes);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task PublishValueAsync(string channel, object? value, DateTime timestamp, CancellationToken cancellationToken);
    Task SetNodeStatusAsync(string channel, object? value, CancellationToken cancellationToken);
}
