using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaServerInstanceManager : IOpcUaServerInstanceManager, IAsyncDisposable
{
    private readonly Dictionary<OpcUaServerDataPortCommunication, ServerInstance> _servers = new(new OpcUaServerDataPortCommunicationEqualityComparer());
    private readonly Func<OpcUaServerDataPortCommunication, ILogger<IOpcUaServer>, IOpcUaServer> _createServer;
    // Never disposed: a stop or release that passed the disposed check before DisposeAsync ran still
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

        // Called from the data port constructors, so it cannot await. Registering itself only builds
        // the server and adds nodes; a start or stop in progress still holds the caller up.
        _semaphore.Wait();

        try
        {
            // A running server serves the nodes of the data port right away.
            if (_servers.TryGetValue(communication, out var entry))
            {
                entry.Server.AddNodes(instance, communication.Nodes);
                entry.Instances.Add(instance);

                return entry.Server;
            }

            var server = _createServer(communication, logger);

            try
            {
                server.AddNodes(instance, communication.Nodes);
            }
            catch
            {
                (server as IDisposable)?.Dispose();
                throw;
            }

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

            if (!entry.Instances.Remove(instance))
                return;

            if (entry.Instances.Count == 0)
            {
                await ShutDownAsync(communication, entry, cancellationToken).ConfigureAwait(false);
                _servers.Remove(communication);
                return;
            }

            if (entry.StartedInstances.Remove(instance) && entry.StartedInstances.Count == 0)
                await StopSafelyAsync(communication, entry, cancellationToken).ConfigureAwait(false);

            RemoveNodesSafely(communication, entry, instance);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            foreach (var (communication, entry) in _servers)
                await ShutDownAsync(communication, entry, CancellationToken.None).ConfigureAwait(false);
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

    /// <summary>
    /// Called from the DisposeAsync of the data port, where a throw breaks the engine teardown.
    /// </summary>
    private static void RemoveNodesSafely(OpcUaServerDataPortCommunication communication, ServerInstance entry, object instance)
    {
        try
        {
            entry.Server.RemoveNodes(instance);
        }
        catch (Exception exception)
        {
            entry.Logger.LogNodeRemovalFailed(communication.Server, communication.Port, exception);
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
        public HashSet<object> StartedInstances { get; init; } = [];
        public List<object> Instances { get; init; } = instances;
    }
}
