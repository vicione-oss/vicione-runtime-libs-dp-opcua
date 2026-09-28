namespace ViciOne.Suite.DataPort;

/// <summary>
/// The design ids of the nodes that address the envelope of their parent's value instead of a node
/// of their own. Both providers name the same ids, so the shared envelope code recognises a child
/// without knowing which of them it is running in - but neither has to offer all of them.
/// </summary>
internal static class EnvelopeNodeDesignId
{
    internal const string StatusCode = "StatusCode";
    internal const string SourceTimestamp = "SourceTimestamp";
    internal const string ServerTimestamp = "ServerTimestamp";
}
