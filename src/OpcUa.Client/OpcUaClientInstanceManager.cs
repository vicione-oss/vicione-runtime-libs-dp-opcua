using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaClientInstanceManager : IOpcUaClientInstanceManager, IDisposable
{
    private readonly Dictionary<OpcUaClientDataPortCommunication, (IOpcUaClient Client, List<object> Instances)> _clients = new(new OpcUaClientDataPortCommunicationEqualityComparer());
    private readonly Func<OpcUaClientDataPortCommunication, ILogger<IOpcUaClient>, IOpcUaClient> _createClient;
    // Never disposed: a stop or release that passed the disposed check before Dispose ran still
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

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
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
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ReleaseOpcUaClientAsync(OpcUaClientDataPortCommunication communication, object instance, CancellationToken cancellationToken)
    {
        if (_disposed)
            return;

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_clients.TryGetValue(communication, out var clientTuple))
            {
                clientTuple.Instances.Remove(instance);

                if (clientTuple.Instances.Count == 0)
                {
                    await clientTuple.Client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                    (clientTuple.Client as IDisposable)?.Dispose();
                    _clients.Remove(communication);
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _semaphore.Wait();

        try
        {
            foreach (var (client, _) in _clients.Values)
            {
                // Task.Run keeps the close off the caller's synchronization context, which would
                // otherwise deadlock against the blocking wait.
                Task.Run(() => client.DisconnectAsync(CancellationToken.None)).GetAwaiter().GetResult();
                (client as IDisposable)?.Dispose();
            }
        }
        finally
        {
            _clients.Clear();
            _semaphore.Release();
        }
    }
}
