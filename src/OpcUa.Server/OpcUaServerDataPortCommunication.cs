using System.Diagnostics.CodeAnalysis;
using ViciOne.ManagedEngine.Communication;

namespace ViciOne.Suite.DataPort;

[Communication("opcuaserverdataport")]
public sealed record class OpcUaServerDataPortCommunication : DataPortCommunication
{
    public string ApplicationName { get; set; } = string.Empty;
    [SuppressMessage("Design", "CA1056:URI-ähnliche Eigenschaften dürfen keine Zeichenfolgen sein")]
    public string ApplicationUri { get; set; } = string.Empty;

    public string Namespace { get; set; } = string.Empty;

    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 4840;
    public string Endpoint { get; set; } = string.Empty;

    public byte UserAuthenticationType { get; set; }

    public string? User { get; set; }
    public string? Password { get; set; }

    public string ApplicationCertificateSubject { get; set; } = string.Empty;

    public byte ApplicationCertificatesStoreType { get; set; } = 1;
    public string ApplicationCertificatesStorePath { get; set; } = string.Empty;

    public byte TrustedCertificatesStoreType { get; set; }
    public string? TrustedCertificatesStorePath { get; set; }

    public byte TrustedIssuerCertificatesStoreType { get; set; }
    public string? TrustedIssuerCertificatesStorePath { get; set; }

    public bool AutoAcceptUntrustedCertificates { get; set; }

    public bool TransportQuotas { get; set; } = true;

    public byte SecurityPolicy { get; set; } = 2;

    public int MinPublishingInterval { get; set; } = 100;
    public int MaxPublishingInterval { get; set; } = 1000;
}
