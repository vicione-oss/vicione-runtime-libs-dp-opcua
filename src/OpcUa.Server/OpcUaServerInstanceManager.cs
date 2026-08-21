using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaServerInstanceManager : IOpcUaServerInstanceManager, IDisposable
{
    private readonly Dictionary<OpcUaServerDataPortCommunication, ServerInstance> _servers = new(new OpcUaServerDataPortCommunicationEqualityComparer());
    private readonly Func<OpcUaServerDataPortCommunication, ILogger<IOpcUaServer>, IOpcUaServer> _createServer;
    // Never disposed: a stop or release that passed the disposed check before Dispose ran still
    // waits on it, and finds nothing left to do once it gets in.
#pragma warning disable CA2213 // Verwerfbare Felder verwerfen
    private readonly SemaphoreSlim _semaphore = new(1, 1);
#pragma warning restore CA2213 // Verwerfbare Felder verwerfen
    private bool _disposed;

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
        ObjectDisposedException.ThrowIf(_disposed, this);

        _semaphore.Wait();

        try
        {
            if (_servers.TryGetValue(communication, out var entry))
            {
                if (entry.HasStarted)
                    throw new InvalidOperationException($"Cannot add nodes to the OPC UA server at '{communication.Server}:{communication.Port}' because it has already been started. Its address space is built once, when it starts; every data port using this server has to be disposed before another one can be added.");

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
        ObjectDisposedException.ThrowIf(_disposed, this);

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

            entry.HasStarted = true;
            entry.StartedInstances.Add(instance);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StopOpcUaServer(OpcUaServerDataPortCommunication communication, object instance, CancellationToken cancellationToken)
    {
        if (_disposed)
            return;

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
        if (_disposed)
            return;

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_servers.TryGetValue(communication, out var entry))
                return;

            entry.Instances.Remove(instance);

            if (entry.Instances.Count == 0)
            {
                await ShutDownAsync(communication, entry, cancellationToken).ConfigureAwait(false);
                _servers.Remove(communication);
                return;
            }

            if (entry.StartedInstances.Remove(instance) && entry.StartedInstances.Count == 0)
                await StopSafelyAsync(communication, entry, cancellationToken).ConfigureAwait(false);
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
            foreach (var (communication, entry) in _servers)
                // Task.Run keeps the shutdown off the caller's synchronization context, which
                // would otherwise deadlock against the blocking wait.
                Task.Run(() => ShutDownAsync(communication, entry, CancellationToken.None)).GetAwaiter().GetResult();
        }
        finally
        {
            _servers.Clear();
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Stops and disposes the server without rethrowing what the server itself throws, so that
    /// one server that refuses to shut down neither breaks the engine teardown nor keeps the
    /// remaining servers running.
    /// </summary>
    private static async Task ShutDownAsync(OpcUaServerDataPortCommunication communication, ServerInstance entry, CancellationToken cancellationToken)
    {
        if (entry.StartedInstances.Count > 0)
            await StopSafelyAsync(communication, entry, cancellationToken).ConfigureAwait(false);

        entry.StartedInstances.Clear();
        DisposeSafely(communication, entry);
    }

    private static async Task StopSafelyAsync(OpcUaServerDataPortCommunication communication, ServerInstance entry, CancellationToken cancellationToken)
    {
        try
        {
            await entry.Server.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Releasing runs from the data port's DisposeAsync, where a throw breaks the engine
            // teardown for every port behind this one.
            entry.Logger.LogServerShutdownFailed(communication.Server, communication.Port, exception);
        }
    }

    private static void DisposeSafely(OpcUaServerDataPortCommunication communication, ServerInstance entry)
    {
        try
        {
            (entry.Server as IDisposable)?.Dispose();
        }
        catch (Exception exception)
        {
            entry.Logger.LogServerShutdownFailed(communication.Server, communication.Port, exception);
        }
    }

    private class ServerInstance(IOpcUaServer server, ILogger<IOpcUaServer> logger, List<object> instances)
    {
        public IOpcUaServer Server { get; init; } = server;
        public ILogger<IOpcUaServer> Logger { get; init; } = logger;
        public bool HasStarted { get; set; }
        public HashSet<object> StartedInstances { get; init; } = [];
        public List<object> Instances { get; init; } = instances;
    }
}
