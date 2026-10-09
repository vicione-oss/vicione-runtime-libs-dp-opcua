using System;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// The part of a subscribed <see cref="DataValue"/> the data port serves to the engine.
/// </summary>
/// <param name="Value">The value itself.</param>
/// <param name="Timestamp">The point in time to report the value for, which is the first of the two
/// timestamps the server set, or the moment the notification was published when it set neither.</param>
/// <param name="StatusCode">The quality the server gave the value.</param>
/// <param name="SourceTimestamp">The point in time the server says the value was produced, or
/// <see cref="DateTime.MinValue"/> when it sent none.</param>
/// <param name="ServerTimestamp">The point in time the server says it processed the value, or
/// <see cref="DateTime.MinValue"/> when it sent none.</param>
internal readonly record struct OpcUaValue(
    object? Value,
    DateTime Timestamp,
    StatusCode StatusCode,
    DateTime SourceTimestamp,
    DateTime ServerTimestamp)
{
    /// <summary>
    /// Reads what the notification carries, keeping the two timestamps the server sent apart from
    /// the one the value is reported for. A server is free to set neither, and a data point
    /// reporting year 1 is worse than one reporting the time its value was delivered.
    /// </summary>
    internal static OpcUaValue Of(DataValue dataValue, DateTime publishTime)
        => new(
            dataValue.Value,
            FirstSetOf(dataValue.SourceTimestamp, dataValue.ServerTimestamp, publishTime),
            dataValue.StatusCode,
            dataValue.SourceTimestamp,
            dataValue.ServerTimestamp);

    private static DateTime FirstSetOf(DateTime source, DateTime server, DateTime published)
    {
        if (source != DateTime.MinValue)
            return source;

        return server != DateTime.MinValue ? server : published;
    }
}
