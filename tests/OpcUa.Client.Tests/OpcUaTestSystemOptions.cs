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
}
