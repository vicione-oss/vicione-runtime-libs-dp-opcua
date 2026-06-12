using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaServerInstanceManager : IOpcUaServerInstanceManager, IDisposable
{
    private readonly Dictionary<OpcUaServerDataPortCommunication, ServerInstance> _servers = new(new OpcUaServerDataPortCommunicationEqualityComparer());
    private readonly Func<OpcUaServerDataPortCommunication, ILogger<IOpcUaServer>, IOpcUaServer> _createServer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public static readonly OpcUaServerInstanceManager Instance = new();

    public OpcUaServerInstanceManager() : this((c, l) => new OpcUaServer(c, l))
    { }

    internal OpcUaServerInstanceManager(Func<OpcUaServerDataPortCommunication, ILogger<IOpcUaServer>, IOpcUaServer> createServer)
    {
        _createServer = createServer;

        Utils.SetTraceOutput(Utils.TraceOutput.Off);
        Utils.SetTraceMask(Utils.TraceMasks.None);
    }

    public IOpcUaServer GetOrRegisterOpcUaServer(OpcUaServerDataPortCommunication communication, object instance, ILogger<IOpcUaServer> logger)
    {
        _semaphore.Wait();

        try
        {
            if (_servers.TryGetValue(communication, out var entry))
            {
                entry.Server.AddNodes(communication.Nodes);
                entry.Instances.Add(instance);

                return entry.Server;
            }

            var server = _createServer(communication, logger);

            server.AddNodes(communication.Nodes);
            _servers.Add(communication, new(server, [instance]));

            return server;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StartOpcUaServer(OpcUaServerDataPortCommunication communication, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_servers.TryGetValue(communication, out var entry))
            {
                if (entry.Started == 0)
                    await entry.Server.StartAsync(cancellationToken).ConfigureAwait(false);
                entry.Started++;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StopOpcUaServer(OpcUaServerDataPortCommunication communication, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_servers.TryGetValue(communication, out var entry))
            {
                entry.Started--;
                if (entry.Started == 0)
                    await entry.Server.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ReleaseOpcUaServerAsync(OpcUaServerDataPortCommunication communication, object instance, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_servers.TryGetValue(communication, out var entry))
            {
                entry.Instances.Remove(instance);

                if (entry.Instances.Count == 0)
                {
                    (entry.Server as IDisposable)?.Dispose();
                    _servers.Remove(communication);
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
            foreach (var server in _servers.Values.Select(e => e.Server))
                (server as IDisposable)?.Dispose();
        }
        finally
        {
            _semaphore.Release();
        }

        _semaphore.Dispose();
    }

    private class ServerInstance(IOpcUaServer server, List<object> instances)
    {
        public IOpcUaServer Server { get; init; } = server;
        public int Started { get; set; }
        public List<object> Instances { get; init; } = instances;
    }
}
