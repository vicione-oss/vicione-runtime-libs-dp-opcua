using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaClient(OpcUaClientDataPortCommunication communication, ILogger<IOpcUaClient>? logger = null) : IOpcUaClient, IDisposable
{
    private const int SessionTimeout = 15_000;
    private const int KeepAliveInterval = 5_000;
    private const int ReconnectInterval = 5_000;
    private const int SubscriptionMinLifetimeInterval = 15_000;

    internal const int MaxDepth = 64;

    private readonly OpcUaClientDataPortProperties _properties = new(communication);
    private readonly ILogger<IOpcUaClient>? _logger = logger;
    private readonly SemaphoreSlim _sessionSemaphore = new(1, 1);

    private ISession? _session;
    private SessionReconnectHandler? _reconnectHandler;
#pragma warning disable CA2213 // Verwerfbare Felder verwerfen
    private Subscription? _subscription;
#pragma warning restore CA2213 // Verwerfbare Felder verwerfen

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _sessionSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (_session is null)
            {
                if (_logger is not null)
                    Utils.SetLogger(_logger);

                var configuration = OpcUaSetup.CreateConfiguration(_properties, SessionTimeout);
                await configuration.ValidateConfigAsync(cancellationToken);

                _session = await CreateSessionAsync(configuration, _properties, cancellationToken);

                _reconnectHandler?.Dispose();
                _reconnectHandler = new(true);
                _session.KeepAlive += OnKeepAlive;
            }
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    internal void OnKeepAlive(ISession session, KeepAliveEventArgs e)
    {
        try
        {
            if (ServiceResult.IsBad(e.Status))
            {
                _reconnectHandler?.BeginReconnect(session, ReconnectInterval, OnReconnectCompleted);
                e.CancelKeepAlive = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogKeepAliveFailure(_properties.ApplicationName, ex);
        }
    }

    private void OnReconnectCompleted(object? sender, EventArgs e)
    {
        _sessionSemaphore.Wait();

        try
        {
            if (_session is not null && _reconnectHandler?.Session is not null)
            {
                if (_reconnectHandler.Session != _session)
                {
                    var oldSession = _session;
                    _session = _reconnectHandler.Session;

                    var newSubscription = _session.Subscriptions.FirstOrDefault();

                    if (newSubscription is not null && _subscription != newSubscription)
                    {
                        _subscription = newSubscription;
                        _logger?.LogSubscriptionExchanged(_properties.ApplicationName);
                    }

                    oldSession.Dispose();
                }
            }
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _sessionSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (_session is not null)
            {
                if (_logger is not null && Utils.Logger == _logger)
                    Utils.SetLogger(new TraceEventLogger());

                // Before the close, not just before the dispose: a close can still trip the keep-alive, and the reconnect that starts there would outlive the CancelReconnect below.
                _session.KeepAlive -= OnKeepAlive;

                try
                {
                    _reconnectHandler?.CancelReconnect();
                    await _session.CloseAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogCloseSessionFailure(_properties.ApplicationName, ex);
                }
                finally
                {
                    _reconnectHandler?.Dispose();
                    _reconnectHandler = null;

                    _session.Dispose();
                    _session = null;
                }
            }
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    public async Task<IReadOnlyCollection<OpcUaNode>> BrowseNodesAsync(CancellationToken cancellationToken)
    {
        await _sessionSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (_session is null)
                throw new InvalidOperationException("OPC UA client session is not initialized.");

            return await BrowseAddressSpaceAsync(_session, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    internal Task<IReadOnlyCollection<OpcUaNode>> BrowseAddressSpaceAsync(ISession session, CancellationToken cancellationToken)
        => BrowseChildrenAsync(session, [(ObjectIds.ObjectsFolder, BrowseNames.ObjectsFolder),], cancellationToken);

    /// <summary>
    /// Browses the node <paramref name="currentPath"/> ends at. A node that is already on that path would be browsed
    /// forever, so it is left out; a node reachable through two paths is not a cycle and is still mirrored under both.
    /// The path is mutated as the recursion descends, which only holds while the children are browsed one at a time.
    /// </summary>
    private async Task<IReadOnlyCollection<OpcUaNode>> BrowseChildrenAsync(ISession session, List<(NodeId NodeId, string DisplayName)> currentPath, CancellationToken cancellationToken)
    {
        var references = await BrowseReferencesAsync(session, currentPath[^1].NodeId, cancellationToken).ConfigureAwait(false);

        List<OpcUaNode> nodes = new(references.Count);

        foreach (var reference in references)
        {
            var displayName = reference.DisplayName?.Text ?? string.Empty;
            var nodeId = OpcUaNode.ResolveNodeId(reference.NodeId, session.NamespaceUris, displayName);

            if (currentPath.Exists(entry => nodeId.Equals(entry.NodeId)))
            {
                _logger?.LogBrowseCycleSkipped(_properties.ApplicationName, displayName, FormatPath(currentPath));
                continue;
            }

            var childDepth = currentPath.Count;

            if (childDepth > MaxDepth)
                throw new InvalidOperationException($"OPC UA nodes more than {MaxDepth} levels below the Objects folder are not browsed. The node '{displayName}' below '{FormatPath(currentPath)}' is deeper than that.");

            currentPath.Add((nodeId, displayName));

            nodes.Add(new()
            {
                NodeId = nodeId,
                DisplayName = displayName,
                Children = await BrowseChildrenAsync(session, currentPath, cancellationToken).ConfigureAwait(false),
            });

            currentPath.RemoveAt(currentPath.Count - 1);
        }

        return nodes;
    }

    private static string FormatPath(List<(NodeId NodeId, string DisplayName)> currentPath) => string.Join('/', currentPath.Select(entry => entry.DisplayName));

    /// <summary>
    /// A server is free to answer a browse with only part of a node's references and a continuation point for the
    /// rest, whatever maximum the request asks for, which is why a plain browse is not enough. A point left behind by
    /// a cancelled browse is the server's to time out.
    /// </summary>
    private static async Task<ReferenceDescriptionCollection> BrowseReferencesAsync(ISession session, NodeId nodeId, CancellationToken cancellationToken)
    {
        var (references, errors) = await session.ManagedBrowseAsync(null, null, [nodeId,], 0u, BrowseDirection.Forward,
            ReferenceTypeIds.HierarchicalReferences, true,
            (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method, cancellationToken).ConfigureAwait(false);

        if (ServiceResult.IsBad(errors[0]))
            throw new InvalidOperationException($"Cannot browse OPC UA node '{nodeId}': {errors[0]}.");

        return references[0];
    }

    public async Task SubscribeAsync(NodeId nodeId, Action<object?, DateTime> callback, CancellationToken cancellationToken)
    {
        await _sessionSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (_session is null)
                throw new InvalidOperationException("OPC UA client session is not initialized.");

            if (_subscription is null)
            {
                _subscription = new(_session.DefaultSubscription)
                {
                    PublishingEnabled = true,
                    PublishingInterval = _properties.SubscriptionPublishingInterval,
                    MinLifetimeInterval = SubscriptionMinLifetimeInterval,
                };
                _session.AddSubscription(_subscription);
                await _subscription.CreateAsync(cancellationToken).ConfigureAwait(false);
            }

            MonitoredItem monitoredItem = new(_subscription.DefaultItem)
            {
                StartNodeId = nodeId,
                AttributeId = Attributes.Value,
            };
            monitoredItem.Notification += (s, e) =>
            {
                if (e.NotificationValue is not MonitoredItemNotification notificationValue)
                    return;
                callback(notificationValue.Value.Value, notificationValue.Message.PublishTime);
            };

            _subscription.AddItem(monitoredItem);
            await _subscription.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    public async Task WriteValuesAsync(IEnumerable<(NodeId NodeId, object? Value)> values, CancellationToken cancellationToken)
    {
        await _sessionSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (_session is null)
                throw new InvalidOperationException("OPC UA client session is not initialized.");

            WriteValueCollection nodesToWrite = [];

            foreach (var (nodeId, value) in values)
            {
                nodesToWrite.Add(new()
                {
                    AttributeId = Attributes.Value,
                    NodeId = nodeId,
                    Value = new DataValue() { Value = value, },
                });
            }

            var response = await _session.WriteAsync(null, nodesToWrite, cancellationToken);

            if (StatusCode.IsNotGood(response.ResponseHeader.ServiceResult))
                throw new InvalidOperationException($"Failed to write values: {response.ResponseHeader.ServiceResult}");

            for (var i = 0; i < nodesToWrite.Count; i++)
            {
                var result = response.Results[i];

                if (StatusCode.IsNotGood(result))
                    _logger?.LogWriteFailure(nodesToWrite[i].NodeId.Identifier.ToString() ?? string.Empty, result.ToString());
            }
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    private static async Task<Session> CreateSessionAsync(ApplicationConfiguration configuration, OpcUaClientDataPortProperties properties, CancellationToken cancellationToken)
    {
        var endpointConfiguration = EndpointConfiguration.Create(configuration);
        var discoveryEdnpoint = CoreClientUtils.SelectEndpoint(configuration, $"opc.tcp://{properties.Server}:{properties.Port.ToString(CultureInfo.InvariantCulture)}/{properties.Endpoint}", properties.UserAuthenticationType is not UserAuthenticationType.Anonymous);
        ConfiguredEndpoint endpoint = new(null, discoveryEdnpoint, endpointConfiguration);

        var session = await Session.Create(
            configuration,
            endpoint,
            true,
            false,
            configuration.ApplicationName,
            Convert.ToUInt32(configuration.ClientConfiguration.DefaultSessionTimeout),
            properties.UserAuthenticationType switch
            {
                UserAuthenticationType.Anonymous => new UserIdentity(),
                UserAuthenticationType.Basic => new UserIdentity(properties.User, properties.Password),
                _ => throw new InvalidOperationException("Invalid user authentication type."),
            },
            null,
            cancellationToken);

        session.KeepAliveInterval = KeepAliveInterval;
        session.DeleteSubscriptionsOnClose = true;
        session.TransferSubscriptionsOnReconnect = true;

        return session;
    }

    public void Dispose()
    {
        if (_session is not null)
        {
            _session.KeepAlive -= OnKeepAlive;
            _session.Dispose();
            _session = null;
        }

        _reconnectHandler?.Dispose();
        _reconnectHandler = null;

        _sessionSemaphore.Dispose();
    }
}
