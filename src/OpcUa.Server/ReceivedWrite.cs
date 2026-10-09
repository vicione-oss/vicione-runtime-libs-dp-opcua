using System;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// A value a client wrote to a served variable.
/// </summary>
/// <param name="Channel">The channel the variable transfers the value on.</param>
/// <param name="Value">The value itself.</param>
/// <param name="Timestamp">The point in time the value is for: the source timestamp the client sent,
/// or the moment the write arrived when it sent none.</param>
/// <param name="StatusCode">The status the client wrote the value with.</param>
/// <param name="SourceTimestamp">The source timestamp the client sent, or
/// <see cref="DateTime.MinValue"/> when it sent none. Kept apart from <paramref name="Timestamp"/>
/// so that a Source timestamp child carries nothing rather than the receive time.</param>
internal readonly record struct ReceivedWrite(
    string Channel,
    object Value,
    DateTime Timestamp,
    StatusCode StatusCode,
    DateTime SourceTimestamp);
