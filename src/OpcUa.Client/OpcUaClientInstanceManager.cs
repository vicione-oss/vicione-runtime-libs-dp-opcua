using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaClientInstanceManager : IOpcUaClientInstanceManager, IAsyncDisposable
{
    private readonly Dictionary<OpcUaClientDataPortCommunication, (IOpcUaClient Client, List<object> Instances)> _clients = new(new OpcUaClientDataPortCommunicationEqualityComparer());
    private readonly Dictionary<OpcUaClientDataPortCommunication, Task> _closing = new(new OpcUaClientDataPortCommunicationEqualityComparer());
    private readonly Func<OpcUaClientDataPortCommunication, ILogger<IOpcUaClient>, IOpcUaClient> _createClient;
    // Never disposed: a stop or release that passed the disposed check before DisposeAsync ran still
    // waits on it, and finds nothing left to do once it gets in.
#pragma warning disable CA2213 // Verwerfbare Felder verwerfen
    private readonly SemaphoreSlim _semaphore = new(1, 1);
#pragma warning restore CA2213 // Verwerfbare Felder verwerfen
    private bool _disposed;

    public static readonly OpcUaClientInstanceManager Instance = new();

    public OpcUaClientInstanceManager() : this((c, l) => new OpcUaClient(c, l))
    { }

    internal OpcUaClientInstanceManager(Func<OpcUaClientDataPortCommunication, ILogger<IOpcUaClient>, IOpcUaClient> createClient)
    {
        _createClient = createClient;

        Utils.SetTraceOutput(Utils.TraceOutput.Off);
        Utils.SetTraceMask(Utils.TraceMasks.None);
    }

    public async Task<IOpcUaClient> GetOrRegisterOpcUaClientAsync(OpcUaClientDataPortCommunication communication, object instance, ILogger<IOpcUaClient> logger, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (true)
        {
            Task? closing;

            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (!_closing.TryGetValue(communication, out closing) || closing.IsCompleted)
                {
                    _closing.Remove(communication);
                    return await RegisterAsync(communication, instance, logger, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _semaphore.Release();
            }

            // A released client for the same endpoint is still closing outside the lock; a new one
            // connects only after it is gone.
            await closing.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IOpcUaClient> RegisterAsync(OpcUaClientDataPortCommunication communication, object instance, ILogger<IOpcUaClient> logger, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(communication, out var clientTuple))
        {
            clientTuple.Instances.Add(instance);
            return clientTuple.Client;
        }

        var client = _createClient(communication, logger);

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            (client as IDisposable)?.Dispose();
            throw;
        }

        _clients.Add(communication, (client, [instance,]));

        return client;
    }

    public async Task ReleaseOpcUaClientAsync(OpcUaClientDataPortCommunication communication, object instance, CancellationToken cancellationToken)
    {
        if (_disposed)
            return;

        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IOpcUaClient? released;

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            released = Unregister(communication, instance, closed.Task);
        }
        finally
        {
            _semaphore.Release();
        }

        if (released is null)
            return;

        try
        {
            await CloseAsync(released, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            closed.SetResult();
        }
    }

    /// <summary>
    /// Removes <paramref name="instance"/> from the client for <paramref name="communication"/> and
    /// returns that client once no instance uses it any more, marking it as closing until
    /// <paramref name="closed"/> completes.
    /// </summary>
    private IOpcUaClient? Unregister(OpcUaClientDataPortCommunication communication, object instance, Task closed)
    {
        if (!_clients.TryGetValue(communication, out var clientTuple))
            return null;

        clientTuple.Instances.Remove(instance);

        if (clientTuple.Instances.Count > 0)
            return null;

        _clients.Remove(communication);
        _closing[communication] = closed;

        return clientTuple.Client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        IOpcUaClient[] clients;

        await _semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            clients = [.. _clients.Values.Select(entry => entry.Client),];
        }
        finally
        {
            _clients.Clear();
            _closing.Clear();
            _semaphore.Release();
        }

        await Task.WhenAll(clients.Select(client => CloseAsync(client, CancellationToken.None))).ConfigureAwait(false);
    }

    private static async Task CloseAsync(IOpcUaClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }
}
