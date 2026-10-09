using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

/// <remarks>
/// The <c>owner</c> of nodes and channels is the data port that registered them, the instance it
/// registers itself with at the <see cref="IOpcUaServerInstanceManager"/>.
/// </remarks>
internal interface IOpcUaServer
{
    event Action<ReceivedWrite> ReceiveValue;

    /// <summary>
    /// Serves the nodes of <paramref name="owner"/> beside the ones the server already serves,
    /// right away if it is running.
    /// </summary>
    /// <exception cref="InvalidOperationException">A node of <paramref name="owner"/> cannot be
    /// served beside them. The nodes are checked before the address space changes, so the server
    /// serves nothing of <paramref name="owner"/> then.</exception>
    void AddNodes(object owner, IReadOnlyCollection<Node> nodes);

    /// <summary>
    /// Stops serving the nodes of <paramref name="owner"/> that no other data port serves.
    /// </summary>
    void RemoveNodes(object owner);

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Serves a value with the status linked to it, or with none when <paramref name="statusCode"/>
    /// is <c>null</c>.
    /// </summary>
    Task PublishValueAsync(object owner, string channel, object? value, DateTime timestamp, StatusCode? statusCode, CancellationToken cancellationToken);

    Task SetNodeStatusAsync(object owner, string channel, StatusCode statusCode, CancellationToken cancellationToken);
}
