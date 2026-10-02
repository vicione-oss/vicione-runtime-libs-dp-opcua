using System;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// The part of a subscribed <see cref="DataValue"/> the data port serves to the engine.
/// </summary>
/// <param name="Value">The value itself.</param>
/// <param name="Timestamp">The point in time to report the value for, which is the moment the server
/// processed it, or the moment the notification was published when the server did not say.</param>
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
    /// the one the value is reported for. The source timestamp follows the clock of the device that
    /// produced the value, which may be off by any amount, so the value is reported for the moment
    /// the server processed it. A server is free not to say, and a data point reporting year 1 is
    /// worse than one reporting the time its value was delivered.
    /// </summary>
    internal static OpcUaValue Of(DataValue dataValue, DateTime publishTime)
        => new(
            dataValue.Value,
            dataValue.ServerTimestamp != DateTime.MinValue ? dataValue.ServerTimestamp : publishTime,
            dataValue.StatusCode,
            dataValue.SourceTimestamp,
            dataValue.ServerTimestamp);
}
