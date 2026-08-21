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
            _servers.Add(communication, new(server, logger, [instance]));

            return server;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StartOpcUaServer(OpcUaServerDataPortCommunication communication, object instance, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // A data port that reaches this without an entry is stale: its server was released
            // while it still held a reference to it. Reporting a successful connect would leave the
            // engine cycling a data port whose server is gone.
            if (!_servers.TryGetValue(communication, out var entry))
                throw new InvalidOperationException($"Cannot start the OPC UA server at '{communication.Server}:{communication.Port}' because this data port is no longer registered with it. The data port has to be disposed and created again.");

            if (entry.StartedInstances.Count == 0)
                await entry.Server.StartAsync(cancellationToken).ConfigureAwait(false);

            entry.StartedInstances.Add(instance);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StopOpcUaServer(OpcUaServerDataPortCommunication communication, object instance, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Stopping and releasing a communication that is no longer registered is ordinary
            // teardown noise: another data port released the last instance before this one ran.
            if (!_servers.TryGetValue(communication, out var entry))
                return;

            if (!entry.StartedInstances.Contains(instance))
            {
                LogStopWithoutMatchingStart(communication, entry);
                return;
            }

            // Mirrors the start: the set only changes once the server has really stopped. Dropping
            // the data port after a failed stop would leave the entry believing the server is down
            // while it still holds its socket, and the next connect would fail on the listener.
            if (entry.StartedInstances.Count == 1)
                await entry.Server.StopAsync(cancellationToken).ConfigureAwait(false);

            entry.StartedInstances.Remove(instance);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static void LogStopWithoutMatchingStart(OpcUaServerDataPortCommunication communication, ServerInstance entry)
    {
        if (entry.StartedInstances.Count == 0)
            entry.Logger.LogStopWithoutStart(communication.Server, communication.Port);
        else
            entry.Logger.LogStopByDataPortThatIsNotKeepingItRunning(communication.Server, communication.Port);
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

    private class ServerInstance(IOpcUaServer server, ILogger<IOpcUaServer> logger, List<object> instances)
    {
        public IOpcUaServer Server { get; init; } = server;
        public ILogger<IOpcUaServer> Logger { get; init; } = logger;
        public HashSet<object> StartedInstances { get; init; } = [];
        public List<object> Instances { get; init; } = instances;
    }
}
