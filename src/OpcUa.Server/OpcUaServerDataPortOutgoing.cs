using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

public sealed class OpcUaServerDataPortOutgoing : IExternalOutgoingCommunication<OpcUaServerDataPortCommunication>, IAsyncDisposable
{
    private static readonly EnvelopeChildKind[] s_servedKinds = [EnvelopeChildKind.StatusCode, EnvelopeChildKind.SourceTimestamp];

    private readonly IOpcUaServerInstanceManager _instanceManager;
    private readonly ILogger<IOpcUaServer> _serverLogger;
    private readonly OpcUaServerDataPortCommunication _communication;
    private readonly EnvelopeChildren _envelopeChildren;

    // Read and written only before the first await of SendAsync. The engine starts the send cycles
    // of a port one after another, so each cycle resolves its values against the envelope the
    // cycles before it left, even while an earlier one is still being served.
    private readonly Dictionary<string, StatusCode> _statusCodes = [];

    private readonly IOpcUaServer _server;

    public OpcUaServerDataPortOutgoing(OpcUaServerDataPortCommunication communication, ILoggerFactory loggerFactory) : this(communication, OpcUaServerInstanceManager.Instance, loggerFactory.CreateLogger<IOpcUaServer>())
    { }

    internal OpcUaServerDataPortOutgoing(OpcUaServerDataPortCommunication communication, IOpcUaServerInstanceManager instanceManager, ILogger<IOpcUaServer> logger)
    {
        _communication = communication;
        _instanceManager = instanceManager;
        _serverLogger = logger;

        // Resolving the tree before the server is registered keeps a configuration the port cannot
        // serve from reaching the address space at all.
        _envelopeChildren = EnvelopeChildren.Create(communication.Nodes, s_servedKinds);

        _server = _instanceManager.GetOrRegisterOpcUaServer(_communication, this, _serverLogger);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
        => await _instanceManager.StartOpcUaServer(_communication, this, cancellationToken).ConfigureAwait(false);

    public async Task DisconnectAsync(CancellationToken cancellationToken)
        => await _instanceManager.StopOpcUaServer(_communication, this, cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
        => await _instanceManager.ReleaseOpcUaServerAsync(_communication, this, CancellationToken.None).ConfigureAwait(false);

    public async Task SendAsync(ulong engineCycle, IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
    {
        try
        {
            RememberStatusCodes(values);

            var servedValues = ResolveServedValues(values, SourceTimestampsOf(values));
            var statusCodesWithoutValue = ResolveStatusCodesWithoutValue(values, servedValues);

            await ServeValuesAsync(servedValues, cancellationToken);
            await ApplyStatusCodesAsync(statusCodesWithoutValue, cancellationToken);
        }
        catch (Exception ex)
        {
            _serverLogger.LogSendFailure(_communication.Server, _communication.Port, ex);
        }
    }

    /// <summary>
    /// A status code has no write of its own; it is served with the value of its parent. It is a
    /// state that holds until it changes, and the engine sends a channel only in the cycle it
    /// changes in, so the last one is remembered and served with every value of that variable
    /// until another arrives.
    /// </summary>
    private void RememberStatusCodes(IReadOnlyCollection<ExternalValue> values)
    {
        foreach (var value in values)
        {
            if (_envelopeChildren.TryGetChild(value.Channel, out var child) && child.Kind == EnvelopeChildKind.StatusCode)
                _statusCodes[child.ParentChannel] = OpcUaStatusCodes.ConvertToStatusCode(value.Value, _serverLogger);
        }
    }

    /// <summary>
    /// The source timestamps of this cycle by the channel of their parent. A timestamp belongs to
    /// the value it arrives with and is not remembered: the engine does not send one again that did
    /// not change, so carrying it over would serve a later value with the time of an earlier one.
    /// </summary>
    private Dictionary<string, DateTime> SourceTimestampsOf(IReadOnlyCollection<ExternalValue> values)
    {
        Dictionary<string, DateTime> sourceTimestamps = [];

        foreach (var value in values)
        {
            if (_envelopeChildren.TryGetChild(value.Channel, out var child)
                && child.Kind == EnvelopeChildKind.SourceTimestamp
                && value.Value is DateTime sourceTimestamp)
            {
                sourceTimestamps[child.ParentChannel] = sourceTimestamp;
            }
        }

        return sourceTimestamps;
    }

    private List<ServedValue> ResolveServedValues(IReadOnlyCollection<ExternalValue> values, Dictionary<string, DateTime> sourceTimestamps)
    {
        List<ServedValue> servedValues = new(values.Count);

        foreach (var value in values)
        {
            if (!_envelopeChildren.IsChildChannel(value.Channel))
                servedValues.Add(new(value.Channel, value.Value, SourceTimestampOf(value, sourceTimestamps), StatusCodeOf(value.Channel)));
        }

        return servedValues;
    }

    /// <summary>
    /// The status codes of this cycle whose variable receives no value in it. A variable that does
    /// is served its status together with the value.
    /// </summary>
    private List<ServedStatusCode> ResolveStatusCodesWithoutValue(IReadOnlyCollection<ExternalValue> values, List<ServedValue> servedValues)
    {
        HashSet<string> channelsWithValue = [.. servedValues.Select(servedValue => servedValue.Channel)];
        List<ServedStatusCode> statusCodes = [];

        foreach (var value in values)
        {
            if (_envelopeChildren.TryGetChild(value.Channel, out var child)
                && child.Kind == EnvelopeChildKind.StatusCode
                && !channelsWithValue.Contains(child.ParentChannel))
            {
                statusCodes.Add(new(child.ParentChannel, _statusCodes[child.ParentChannel]));
            }
        }

        return statusCodes;
    }

    /// <summary>
    /// The point in time a variable reports its value was produced. No source timestamp in the
    /// cycle of the value means it is served with the timestamp the engine gave it.
    /// </summary>
    private static DateTime SourceTimestampOf(ExternalValue value, Dictionary<string, DateTime> sourceTimestamps)
        => sourceTimestamps.TryGetValue(value.Channel, out var sourceTimestamp) ? sourceTimestamp : value.Timestamp;

    /// <summary>
    /// The status a value is served with, or <c>null</c> when none is linked.
    /// </summary>
    private StatusCode? StatusCodeOf(string channel)
        => _statusCodes.TryGetValue(channel, out var statusCode) ? statusCode : null;

    private async Task ServeValuesAsync(List<ServedValue> servedValues, CancellationToken cancellationToken)
    {
        foreach (var servedValue in servedValues)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            await _server.PublishValueAsync(this, servedValue.Channel, servedValue.Value, servedValue.SourceTimestamp, servedValue.StatusCode, cancellationToken);
        }
    }

    private async Task ApplyStatusCodesAsync(List<ServedStatusCode> statusCodes, CancellationToken cancellationToken)
    {
        foreach (var (channel, statusCode) in statusCodes)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            await _server.SetNodeStatusAsync(this, channel, statusCode, cancellationToken);
        }
    }

    private readonly record struct ServedValue(string Channel, object? Value, DateTime SourceTimestamp, StatusCode? StatusCode);

    private readonly record struct ServedStatusCode(string Channel, StatusCode StatusCode);
}
