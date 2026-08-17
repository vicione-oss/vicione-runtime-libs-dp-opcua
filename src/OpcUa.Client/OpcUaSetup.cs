using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace ViciOne.Suite.DataPort;

internal static class OpcUaSetup
{
    internal static ApplicationConfiguration CreateConfiguration(OpcUaClientDataPortProperties properties, int sessionTimeout)
    {
        var configuration = new ApplicationConfiguration()
        {
            ApplicationName = properties.ApplicationName,
            ApplicationUri = properties.ApplicationUri,
            ApplicationType = ApplicationType.Client,
            TransportQuotas = new(),
            ClientConfiguration = new()
            {
                DefaultSessionTimeout = sessionTimeout,
            },
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

    internal static async Task ValidateConfigAsync(this ApplicationConfiguration configuration, CancellationToken cancellationToken)
    {
        await configuration.Validate(ApplicationType.Client).ConfigureAwait(false);

        ApplicationInstance applicationInstance = new(configuration);

        if (!await applicationInstance.CheckApplicationInstanceCertificates(false, CertificateFactory.DefaultLifeTime, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Server certificate is not valid.");
    }
}
