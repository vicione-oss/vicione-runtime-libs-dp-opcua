using System;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaServerDataPortProperties(OpcUaServerDataPortCommunication communication)
{
    internal string ApplicationName
    {
        get => communication.ApplicationName;
        set => communication.ApplicationName = value;
    }

    internal string ApplicationUri
    {
        get => communication.ApplicationUri;
        set => communication.ApplicationUri = value;
    }

    internal string Namespace
    {
        get => communication.Namespace;
        set => communication.Namespace = value;
    }

    internal string Server
    {
        get => communication.Server;
        set => communication.Server = value;
    }

    internal int Port
    {
        get => communication.Port;
        set => communication.Port = value;
    }

    internal string Endpoint
    {
        get => communication.Endpoint;
        set => communication.Endpoint = value;
    }

    internal UserAuthenticationType UserAuthenticationType
    {
        get => communication.UserAuthenticationType switch
        {
            0 => UserAuthenticationType.Anonymous,
            1 => UserAuthenticationType.Basic,
            _ => throw new InvalidOperationException("Invalid user authentication type.")
        };
        set => communication.UserAuthenticationType = value switch
        {
            UserAuthenticationType.Anonymous => 0,
            UserAuthenticationType.Basic => 1,
            _ => throw new InvalidOperationException("Invalid user authentication type.")
        };
    }

    internal string? User
    {
        get => communication.User;
        set => communication.User = value;
    }

    internal string? Password
    {
        get => communication.Password;
        set => communication.Password = value;
    }

    internal string ApplicationCertificateSubject
    {
        get => communication.ApplicationCertificateSubject;
        set => communication.ApplicationCertificateSubject = value;
    }

    internal string? ApplicationCertificatesStoreType
    {
        get => communication.ApplicationCertificatesStoreType switch
        {
            0 => null,
            1 => CertificateStoreType.X509Store,
            2 => CertificateStoreType.Directory,
            _ => throw new InvalidOperationException("Invalid application store type.")
        };
        set => communication.ApplicationCertificatesStoreType = value switch
        {
            null => 0,
            CertificateStoreType.X509Store => 1,
            CertificateStoreType.Directory => 2,
            _ => throw new InvalidOperationException("Invalid application store type.")
        };
    }

    internal string ApplicationCertificatesStorePath
    {
        get => communication.ApplicationCertificatesStorePath;
        set => communication.ApplicationCertificatesStorePath = value;
    }

    internal string? TrustedCertificatesStoreType
    {
        get => communication.TrustedCertificatesStoreType switch
        {
            0 => null,
            1 => CertificateStoreType.X509Store,
            2 => CertificateStoreType.Directory,
            _ => throw new InvalidOperationException("Invalid trusted store type.")
        };
        set => communication.TrustedCertificatesStoreType = value switch
        {
            null => 0,
            CertificateStoreType.X509Store => 1,
            CertificateStoreType.Directory => 2,
            _ => throw new InvalidOperationException("Invalid trusted store type.")
        };
    }

    internal string TrustedCertificatesStorePath
    {
        get => communication.TrustedCertificatesStorePath ?? throw new InvalidOperationException("Trusted store path is not set.");
        set => communication.TrustedCertificatesStorePath = value;
    }

    internal string? TrustedIssuerCertificatesStoreType
    {
        get => communication.TrustedIssuerCertificatesStoreType switch
        {
            0 => null,
            1 => CertificateStoreType.X509Store,
            2 => CertificateStoreType.Directory,
            _ => throw new InvalidOperationException("Invalid trusted issuer store type.")
        };
        set => communication.TrustedIssuerCertificatesStoreType = value switch
        {
            null => 0,
            CertificateStoreType.X509Store => 1,
            CertificateStoreType.Directory => 2,
            _ => throw new InvalidOperationException("Invalid trusted issuer store type.")
        };
    }

    internal string TrustedIssuerCertificatesStorePath
    {
        get => communication.TrustedIssuerCertificatesStorePath ?? throw new InvalidOperationException("Trusted issuer store path is not set.");
        set => communication.TrustedIssuerCertificatesStorePath = value;
    }

    internal bool AutoAcceptUntrustedCertificates
    {
        get => communication.AutoAcceptUntrustedCertificates;
        set => communication.AutoAcceptUntrustedCertificates = value;
    }

    internal bool TransportQuotas
    {
        get => communication.TransportQuotas;
        set => communication.TransportQuotas = value;
    }

    internal ServerSecurityPolicy SecurityPolicy
    {
        get => communication.SecurityPolicy switch
        {
            0 => ServerSecurityPolicy.None,
            1 => ServerSecurityPolicy.Basic256Sha256_Sign,
            2 => ServerSecurityPolicy.Basic256Sha256_SignAndEncrypt,
            3 => ServerSecurityPolicy.Aes256_Sha256_RsaPss_SignAndEncrypt,
            _ => throw new InvalidOperationException("Invalid security policy.")
        };
        set => communication.SecurityPolicy = value switch
        {
            ServerSecurityPolicy.None => 0,
            ServerSecurityPolicy.Basic256Sha256_Sign => 1,
            ServerSecurityPolicy.Basic256Sha256_SignAndEncrypt => 2,
            ServerSecurityPolicy.Aes256_Sha256_RsaPss_SignAndEncrypt => 3,
            _ => throw new InvalidOperationException("Invalid security policy.")
        };
    }
}
