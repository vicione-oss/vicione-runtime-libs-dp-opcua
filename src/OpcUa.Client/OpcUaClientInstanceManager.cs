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
    private readonly SemaphoreSlim _semaphore = new(1, 1);

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
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_clients.TryGetValue(communication, out var clientTuple))
            {
                clientTuple.Instances.Add(instance);
                return clientTuple.Client;
            }

            var client = _createClient(communication, logger);
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

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
        _semaphore.Wait();

        try
        {
            foreach (var (client, _) in _clients.Values)
                (client as IDisposable)?.Dispose();
        }
        finally
        {
            _semaphore.Release();
        }

        _semaphore.Dispose();
    }
}
