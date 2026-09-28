using System;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// A value the data port writes to an OPC UA node.
/// </summary>
/// <param name="NodeId">The node the value is written to.</param>
/// <param name="Value">The value itself.</param>
/// <param name="StatusCode">The status the value is written with. A server may refuse a written
/// status with BadWriteNotSupported, whatever the access level of the node advertises, and then the
/// value does not arrive either.</param>
/// <param name="SourceTimestamp">The point in time the value was produced, or
/// <see cref="DateTime.MinValue"/> to leave it to the receiving server. A server may refuse a
/// written timestamp the same way.</param>
internal readonly record struct OpcUaWrite(NodeId NodeId, object? Value, StatusCode StatusCode, DateTime SourceTimestamp);
