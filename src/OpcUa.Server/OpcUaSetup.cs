using System;
using System.Globalization;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal static class OpcUaSetup
{
    internal static ApplicationConfiguration CreateConfiguration(OpcUaServerDataPortProperties properties, TransportQuotasConfiguration? quotasConfig = null)
    {
        if (properties.MinPublishingInterval > properties.MaxPublishingInterval)
            throw new InvalidOperationException($"MinPublishingInterval ({properties.MinPublishingInterval}) must be less than or equal to MaxPublishingInterval ({properties.MaxPublishingInterval}).");

        var config = quotasConfig ?? TransportQuotasConfiguration.Default;
        ServerConfiguration serverConfiguration = new();
        serverConfiguration.BaseAddresses.Add($"opc.tcp://{properties.Server}:{properties.Port.ToString(CultureInfo.InvariantCulture)}/{properties.Endpoint}");
        serverConfiguration.ServerProfileArray =
        [
            "http://opcfoundation.org/UA-Profile/Server/StandardUAProfile",
            "http://opcfoundation.org/UA-Profile/Server/DataAccess",
            "http://opcfoundation.org/UA-Profile/Server/ClientRedundancy",
        ];
        serverConfiguration.SecurityPolicies = CreateSecurityPolicies(properties.SecurityPolicy);
        serverConfiguration.MaxRegistrationInterval = 0; // do not register itself
        serverConfiguration.MaxSessionCount = config.MaxSessionCount;
        serverConfiguration.MaxSubscriptionCount = config.MaxSubscriptionCount;
        serverConfiguration.MinSessionTimeout = config.MinSessionTimeout;
        serverConfiguration.MaxSessionTimeout = config.MaxSessionTimeout;
        serverConfiguration.MinPublishingInterval = properties.MinPublishingInterval;
        serverConfiguration.MaxPublishingInterval = properties.MaxPublishingInterval;

        serverConfiguration.UserTokenPolicies =
        [
            properties.UserAuthenticationType switch
            {
                UserAuthenticationType.Anonymous => new UserTokenPolicy(UserTokenType.Anonymous) { SecurityPolicyUri = SecurityPolicies.None },
                UserAuthenticationType.Basic => new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 },
                _ => throw new InvalidOperationException("Invalid user authentication type.")
            },
        ];

        ApplicationConfiguration configuration = new()
        {
            ApplicationName = properties.ApplicationName,
            ApplicationUri = properties.ApplicationUri,
            ApplicationType = ApplicationType.Server,
            TransportQuotas = properties.TransportQuotas ? CreateTransportQuotas(config) : null,
            ServerConfiguration = serverConfiguration,
        };

        if (properties.ApplicationCertificatesStoreType is not null)
        {
            configuration.SecurityConfiguration.ApplicationCertificate = new()
            {
                StoreType = properties.ApplicationCertificatesStoreType,
                StorePath = properties.ApplicationCertificatesStorePath,
                SubjectName = properties.ApplicationCertificateSubject,
            };
        }

        configuration.SecurityConfiguration.AddAppCertToTrustedStore = false;
        configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = properties.AutoAcceptUntrustedCertificates;

        if (properties.TrustedCertificatesStoreType is not null)
        {
            configuration.SecurityConfiguration.TrustedPeerCertificates = new()
            {
                StoreType = properties.TrustedCertificatesStoreType,
                StorePath = properties.TrustedCertificatesStorePath,
            };
        }
        if (properties.TrustedIssuerCertificatesStoreType is not null)
        {
            configuration.SecurityConfiguration.TrustedIssuerCertificates = new()
            {
                StoreType = properties.TrustedIssuerCertificatesStoreType,
                StorePath = properties.TrustedIssuerCertificatesStorePath,
            };
        }

        return configuration;
    }

    internal static ServerSecurityPolicyCollection CreateSecurityPolicies(ServerSecurityPolicy policy)
    {
        var (securityMode, securityPolicyUri) = policy switch
        {
            ServerSecurityPolicy.None => (MessageSecurityMode.None, SecurityPolicies.None),
            ServerSecurityPolicy.Basic256Sha256_Sign => (MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256),
            ServerSecurityPolicy.Basic256Sha256_SignAndEncrypt => (MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256),
            ServerSecurityPolicy.Aes256_Sha256_RsaPss_SignAndEncrypt => (MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Aes256_Sha256_RsaPss),
            _ => throw new InvalidOperationException($"Unsupported security policy: {policy}"),
        };

        return [new() { SecurityMode = securityMode, SecurityPolicyUri = securityPolicyUri }];
    }

    private static TransportQuotas CreateTransportQuotas(TransportQuotasConfiguration config) => new()
    {
        MaxStringLength = config.MaxStringLength,
        MaxByteStringLength = config.MaxByteStringLength,
        MaxArrayLength = config.MaxArrayLength,
        MaxMessageSize = config.MaxMessageSize,
        MaxBufferSize = config.MaxBufferSize,
        OperationTimeout = config.OperationTimeout,
    };
}
