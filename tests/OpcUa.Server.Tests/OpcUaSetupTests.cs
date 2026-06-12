using AwesomeAssertions;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaSetup_CreateSecurityPolicies
{
    [Fact]
    public void Default_policy_does_not_contain_None()
    {
        var policies = OpcUaSetup.CreateSecurityPolicies(ServerSecurityPolicy.Basic256Sha256_SignAndEncrypt);

        policies.Should().NotContain(p => p.SecurityMode == MessageSecurityMode.None);
    }

    [Fact]
    public void None_policy_contains_only_None()
    {
        var policies = OpcUaSetup.CreateSecurityPolicies(ServerSecurityPolicy.None);

        policies.Should().ContainSingle()
            .Which.SecurityMode.Should().Be(MessageSecurityMode.None);
    }

    [Fact]
    public void Basic256Sha256_Sign_returns_Sign_with_Basic256Sha256()
    {
        var policies = OpcUaSetup.CreateSecurityPolicies(ServerSecurityPolicy.Basic256Sha256_Sign);

        var policy = policies.Should().ContainSingle().Which;
        policy.SecurityMode.Should().Be(MessageSecurityMode.Sign);
        policy.SecurityPolicyUri.Should().Be(SecurityPolicies.Basic256Sha256);
    }

    [Fact]
    public void Basic256Sha256_SignAndEncrypt_returns_SignAndEncrypt_with_Basic256Sha256()
    {
        var policies = OpcUaSetup.CreateSecurityPolicies(ServerSecurityPolicy.Basic256Sha256_SignAndEncrypt);

        var policy = policies.Should().ContainSingle().Which;
        policy.SecurityMode.Should().Be(MessageSecurityMode.SignAndEncrypt);
        policy.SecurityPolicyUri.Should().Be(SecurityPolicies.Basic256Sha256);
    }

    [Fact]
    public void Aes256_Sha256_RsaPss_SignAndEncrypt_returns_SignAndEncrypt_with_Aes256_Sha256_RsaPss()
    {
        var policies = OpcUaSetup.CreateSecurityPolicies(ServerSecurityPolicy.Aes256_Sha256_RsaPss_SignAndEncrypt);

        var policy = policies.Should().ContainSingle().Which;
        policy.SecurityMode.Should().Be(MessageSecurityMode.SignAndEncrypt);
        policy.SecurityPolicyUri.Should().Be(SecurityPolicies.Aes256_Sha256_RsaPss);
    }
}

public class OpcUaSetup_CreateConfiguration_SecurityPolicies
{
    [Fact]
    public void Default_communication_does_not_include_None_security_mode()
    {
        OpcUaServerDataPortCommunication communication = new();
        OpcUaServerDataPortProperties properties = new(communication);

        var configuration = OpcUaSetup.CreateConfiguration(properties);

        configuration.ServerConfiguration.SecurityPolicies
            .Should().NotContain(p => p.SecurityMode == MessageSecurityMode.None);
    }

    [Fact]
    public void None_security_policy_includes_None_security_mode()
    {
        OpcUaServerDataPortCommunication communication = new() { SecurityPolicy = 0 };
        OpcUaServerDataPortProperties properties = new(communication);

        var configuration = OpcUaSetup.CreateConfiguration(properties);

        configuration.ServerConfiguration.SecurityPolicies
            .Should().Contain(p => p.SecurityMode == MessageSecurityMode.None);
    }
}

public class OpcUaSetup_CreateConfiguration_TrustedPeerCertificates
{
    [Fact]
    public void TrustedPeerCertificates_is_configured_when_TrustedCertificatesStoreType_is_set()
    {
        OpcUaServerDataPortCommunication communication = new()
        {
            TrustedCertificatesStoreType = 2, // Directory
            TrustedCertificatesStorePath = "/trusted/peer/certs",
        };
        OpcUaServerDataPortProperties properties = new(communication);

        var configuration = OpcUaSetup.CreateConfiguration(properties);

        configuration.SecurityConfiguration.TrustedPeerCertificates.Should().NotBeNull();
        configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType.Should().Be(CertificateStoreType.Directory);
        configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath.Should().Be("/trusted/peer/certs");
    }

    [Fact]
    public void TrustedIssuerCertificates_is_not_set_by_TrustedCertificatesStoreType()
    {
        OpcUaServerDataPortCommunication communication = new()
        {
            TrustedCertificatesStoreType = 2, // Directory
            TrustedCertificatesStorePath = "/trusted/peer/certs",
            TrustedIssuerCertificatesStoreType = 0, // null
        };
        OpcUaServerDataPortProperties properties = new(communication);

        var configuration = OpcUaSetup.CreateConfiguration(properties);

        configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath.Should().BeNullOrEmpty();
    }
}

public class OpcUaSetup_CreateConfiguration_TrustedIssuerCertificates
{
    [Fact]
    public void TrustedIssuerCertificates_is_configured_when_TrustedIssuerCertificatesStoreType_is_set()
    {
        OpcUaServerDataPortCommunication communication = new()
        {
            TrustedIssuerCertificatesStoreType = 1, // X509Store
            TrustedIssuerCertificatesStorePath = "/trusted/issuer/certs",
        };
        OpcUaServerDataPortProperties properties = new(communication);

        var configuration = OpcUaSetup.CreateConfiguration(properties);

        configuration.SecurityConfiguration.TrustedIssuerCertificates.Should().NotBeNull();
        configuration.SecurityConfiguration.TrustedIssuerCertificates.StoreType.Should().Be(CertificateStoreType.X509Store);
        configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath.Should().Be("/trusted/issuer/certs");
    }

    [Fact]
    public void Both_TrustedPeerCertificates_and_TrustedIssuerCertificates_are_configured_independently()
    {
        OpcUaServerDataPortCommunication communication = new()
        {
            TrustedCertificatesStoreType = 2, // Directory
            TrustedCertificatesStorePath = "/trusted/peer/certs",
            TrustedIssuerCertificatesStoreType = 1, // X509Store
            TrustedIssuerCertificatesStorePath = "/trusted/issuer/certs",
        };
        OpcUaServerDataPortProperties properties = new(communication);

        var configuration = OpcUaSetup.CreateConfiguration(properties);

        configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType.Should().Be(CertificateStoreType.Directory);
        configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath.Should().Be("/trusted/peer/certs");
        configuration.SecurityConfiguration.TrustedIssuerCertificates.StoreType.Should().Be(CertificateStoreType.X509Store);
        configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath.Should().Be("/trusted/issuer/certs");
    }
}
