using System;
using System.Collections.Generic;
using Opc.Ua;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// A received value together with the values of its envelope children.
/// </summary>
internal static class EnvelopeBatch
{
    /// <summary>
    /// The value followed by one value per envelope child, raised as one batch so the engine sees a
    /// value and the envelope it arrived with in the same cycle. A child is reported for the time of
    /// its parent and is never more valid than it. A timestamp of <see cref="DateTime.MinValue"/> is
    /// one the far side did not send.
    /// </summary>
    internal static List<ExternalValue> Of(ExternalValue value, IReadOnlyList<EnvelopeChild> children, StatusCode statusCode, DateTime sourceTimestamp, DateTime serverTimestamp)
    {
        List<ExternalValue> values = new(children.Count + 1) { value };

        foreach (var child in children)
        {
            if (ValueOf(child, statusCode, sourceTimestamp, serverTimestamp) is not { } childValue)
                continue;

            values.Add(new()
            {
                Channel = child.Channel,
                Timestamp = value.Timestamp,
                Value = childValue,
                Validity = value.Validity,
            });
        }

        return values;
    }

    /// <summary>
    /// The value an envelope child carries, or <c>null</c> for a timestamp the far side did not send:
    /// a timestamp child reports only what the far side set, never the fallback the value itself is
    /// reported for. A status code is the unsigned integer OPC UA transmits, or the name of that code
    /// when the tree links the child as a string.
    /// </summary>
    private static object? ValueOf(EnvelopeChild child, StatusCode statusCode, DateTime sourceTimestamp, DateTime serverTimestamp)
        => child.Kind switch
        {
            EnvelopeChildKind.StatusCode when child.ValueType == typeof(string) => OpcUaStatusCodes.NameOf(statusCode),
            EnvelopeChildKind.StatusCode => statusCode.Code,
            EnvelopeChildKind.SourceTimestamp => Sent(sourceTimestamp),
            EnvelopeChildKind.ServerTimestamp => Sent(serverTimestamp),
            _ => throw new ArgumentOutOfRangeException(nameof(child), child.Kind, "Unknown envelope child kind."),
        };

    private static object? Sent(DateTime timestamp)
        => timestamp == DateTime.MinValue ? null : timestamp;
}
