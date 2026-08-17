using AwesomeAssertions;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaSetup_CreateConfiguration_TrustedPeerCertificates
{
    private const int SessionTimeout = 60000;

    [Fact]
    public void TrustedPeerCertificates_is_configured_when_TrustedCertificatesStoreType_is_set()
    {
        OpcUaClientDataPortCommunication communication = new()
        {
            TrustedCertificatesStoreType = 2, // Directory
            TrustedCertificatesStorePath = "/trusted/peer/certs",
        };
        OpcUaClientDataPortProperties properties = new(communication);

        var configuration = OpcUaSetup.CreateConfiguration(properties, SessionTimeout);

        configuration.SecurityConfiguration.TrustedPeerCertificates.Should().NotBeNull();
        configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType.Should().Be(CertificateStoreType.Directory);
        configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath.Should().Be("/trusted/peer/certs");
    }

    [Fact]
    public void TrustedIssuerCertificates_is_not_set_by_TrustedCertificatesStoreType()
    {
        OpcUaClientDataPortCommunication communication = new()
        {
            TrustedCertificatesStoreType = 2, // Directory
            TrustedCertificatesStorePath = "/trusted/peer/certs",
            TrustedIssuerCertificatesStoreType = 0, // null
        };
        OpcUaClientDataPortProperties properties = new(communication);

        var configuration = OpcUaSetup.CreateConfiguration(properties, SessionTimeout);

        configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath.Should().BeNullOrEmpty();
    }
}

public class OpcUaSetup_CreateConfiguration_TrustedIssuerCertificates
{
    private const int SessionTimeout = 60000;

    [Fact]
    public void TrustedIssuerCertificates_is_configured_when_TrustedIssuerCertificatesStoreType_is_set()
    {
        OpcUaClientDataPortCommunication communication = new()
        {
            TrustedIssuerCertificatesStoreType = 1, // X509Store
            TrustedIssuerCertificatesStorePath = "/trusted/issuer/certs",
        };
        OpcUaClientDataPortProperties properties = new(communication);

        var configuration = OpcUaSetup.CreateConfiguration(properties, SessionTimeout);

        configuration.SecurityConfiguration.TrustedIssuerCertificates.Should().NotBeNull();
        configuration.SecurityConfiguration.TrustedIssuerCertificates.StoreType.Should().Be(CertificateStoreType.X509Store);
        configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath.Should().Be("/trusted/issuer/certs");
    }

    [Fact]
    public void Both_TrustedPeerCertificates_and_TrustedIssuerCertificates_are_configured_independently()
    {
        OpcUaClientDataPortCommunication communication = new()
        {
            TrustedCertificatesStoreType = 2, // Directory
            TrustedCertificatesStorePath = "/trusted/peer/certs",
            TrustedIssuerCertificatesStoreType = 1, // X509Store
            TrustedIssuerCertificatesStorePath = "/trusted/issuer/certs",
        };
        OpcUaClientDataPortProperties properties = new(communication);

        var configuration = OpcUaSetup.CreateConfiguration(properties, SessionTimeout);

        configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType.Should().Be(CertificateStoreType.Directory);
        configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath.Should().Be("/trusted/peer/certs");
        configuration.SecurityConfiguration.TrustedIssuerCertificates.StoreType.Should().Be(CertificateStoreType.X509Store);
        configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath.Should().Be("/trusted/issuer/certs");
    }
}
