using System.Diagnostics.CodeAnalysis;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// Opt-in switches for <see cref="OpcUaTestSystem"/>. Everything defaults to off, so the shared fixture stays the
/// small, plain, unsecured server that most tests want; a test that needs an awkward server asks for exactly the one
/// behaviour it is about.
/// </summary>
[SuppressMessage("Maintainability", "CA1515:Erwägen Sie, öffentliche Typen intern zu machen.")]
public sealed class OpcUaTestSystemOptions
{
    /// <summary>
    /// Serves an encrypted endpoint with username authentication alongside the unsecured one, and exports the server
    /// certificate to <see cref="OpcUaTestSystem.TrustedPeerStorePath"/> so a client can be pointed at a trust store
    /// instead of accepting untrusted certificates wholesale.
    /// </summary>
    public bool SecureEndpoint { get; set; }

    /// <summary>
    /// Adds two sibling variables that share a <c>DisplayName</c> but have distinct <c>BrowseName</c>s. A real server
    /// is free to do this: only the browse name is required to be unique among siblings.
    /// </summary>
    public bool DuplicateDisplayNames { get; set; }

    /// <summary>
    /// Adds a variable whose <c>BrowseName</c> and <c>DisplayName</c> differ, which is the normal case for servers
    /// that localise their display names.
    /// </summary>
    public bool MismatchedBrowseName { get; set; }

    /// <summary>
    /// Adds a folder with more children than the server returns in one browse response, so a client only sees all of
    /// them by following the continuation point.
    /// </summary>
    public bool OversizedFolder { get; set; }

    /// <summary>
    /// Adds a reference whose target is an absolute <c>ExpandedNodeId</c> — it carries a namespace URI instead of an
    /// index, and so has to be resolved against the session's namespace table before it can be used as a node id.
    /// </summary>
    public bool AbsoluteReference { get; set; }

    /// <summary>
    /// Registers the test namespaces in reverse order, so the same namespace URI ends up at a different index than it
    /// had on another start. Namespace indices are per-session and must never be persisted.
    /// </summary>
    public bool SwapNamespaceOrder { get; set; }

    /// <summary>
    /// Whether any switch needs the test node manager. While this is false the server serves its stock address space,
    /// which is what the shared fixture wants.
    /// </summary>
    internal bool HasTestAddressSpace => DuplicateDisplayNames || MismatchedBrowseName || OversizedFolder || AbsoluteReference || SwapNamespaceOrder;
}
