namespace ViciOne.Suite.DataPort;

/// <summary>
/// Which part of the OPC UA value of its parent an envelope child addresses. Each names exactly one
/// field of a <c>DataValue</c>, so a child never has to say which of several it happens to carry.
/// </summary>
internal enum EnvelopeChildKind
{
    /// <summary>The quality the value carries.</summary>
    StatusCode,

    /// <summary>The point in time the value was produced.</summary>
    SourceTimestamp,

    /// <summary>
    /// The point in time the serving OPC UA server processed the value. Only a client can read one;
    /// a server sets its own and refuses one a client writes.
    /// </summary>
    ServerTimestamp,
}
