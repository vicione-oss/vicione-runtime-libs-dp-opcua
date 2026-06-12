using System;
using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaClientDataPortCommunicationEqualityComparer_Equals
{
    [Fact]
    public void Returns_true_when_both_are_null()
    {
        OpcUaClientDataPortCommunicationEqualityComparer comparer = new();

        var result = comparer.Equals(null, null);

        result.Should().BeTrue();
    }

    [Fact]
    public void Returns_false_when_one_is_null()
    {
        OpcUaClientDataPortCommunicationEqualityComparer comparer = new();

        var result = comparer.Equals(new OpcUaClientDataPortCommunication(), null);

        result.Should().BeFalse();
    }

    [Fact]
    public void Returns_true_when_all_properties_are_equal()
    {
        OpcUaClientDataPortCommunicationEqualityComparer comparer = new();
        OpcUaClientDataPortCommunication x = new()
        {
            ApplicationName = "ApplicationName",
            ApplicationUri = "ApplicationUri",
            Server = "Server",
            Port = 123,
            Endpoint = "Endpoint",
            UserAuthenticationType = 0,
            User = "User",
            Password = "Password",
            ApplicationCertificateSubject = "CN=ApplicationName",
            ApplicationCertificatesStoreType = 0,
            ApplicationCertificatesStorePath = "StorePath",
            TrustedCertificatesStoreType = 0,
            TrustedCertificatesStorePath = "StorePath",
            TrustedIssuerCertificatesStoreType = 0,
            TrustedIssuerCertificatesStorePath = "StorePath",
            AutoAcceptUntrustedCertificates = true,
        };
        OpcUaClientDataPortCommunication y = new()
        {
            ApplicationName = "ApplicationName",
            ApplicationUri = "ApplicationUri",
            Server = "Server",
            Port = 123,
            Endpoint = "Endpoint",
            UserAuthenticationType = 0,
            User = "User",
            Password = "Password",
            ApplicationCertificateSubject = "CN=ApplicationName",
            ApplicationCertificatesStoreType = 0,
            ApplicationCertificatesStorePath = "StorePath",
            TrustedCertificatesStoreType = 0,
            TrustedCertificatesStorePath = "StorePath",
            TrustedIssuerCertificatesStoreType = 0,
            TrustedIssuerCertificatesStorePath = "StorePath",
            AutoAcceptUntrustedCertificates = true,
        };

        var result = comparer.Equals(x, y);

        result.Should().BeTrue();
    }

    [Fact]
    public void Ignores_nodes_property()
    {
        OpcUaClientDataPortCommunicationEqualityComparer comparer = new();
        OpcUaClientDataPortCommunication x = new()
        {
            ApplicationName = "ApplicationName",
            ApplicationUri = "ApplicationUri",
            Server = "Server",
            Port = 123,
            Endpoint = "Endpoint",
            UserAuthenticationType = 0,
            User = "User",
            Password = "Password",
            ApplicationCertificateSubject = "CN=ApplicationName",
            ApplicationCertificatesStoreType = 0,
            ApplicationCertificatesStorePath = "StorePath",
            TrustedCertificatesStoreType = 0,
            TrustedCertificatesStorePath = "StorePath",
            TrustedIssuerCertificatesStoreType = 0,
            TrustedIssuerCertificatesStorePath = "StorePath",
            AutoAcceptUntrustedCertificates = true,
        };
        OpcUaClientDataPortCommunication y = new()
        {
            ApplicationName = "ApplicationName",
            ApplicationUri = "ApplicationUri",
            Server = "Server",
            Port = 123,
            Endpoint = "Endpoint",
            UserAuthenticationType = 0,
            User = "User",
            Password = "Password",
            ApplicationCertificateSubject = "CN=ApplicationName",
            ApplicationCertificatesStoreType = 0,
            ApplicationCertificatesStorePath = "StorePath",
            TrustedCertificatesStoreType = 0,
            TrustedCertificatesStorePath = "StorePath",
            TrustedIssuerCertificatesStoreType = 0,
            TrustedIssuerCertificatesStorePath = "StorePath",
            AutoAcceptUntrustedCertificates = true,
            Nodes = [new() { Id = Guid.NewGuid(), }],
        };

        var result = comparer.Equals(x, y);

        result.Should().BeTrue();
    }


    [Theory]
    [MemberData(nameof(GetDifferentCommunications))]
    [SuppressMessage("Usage", "xUnit1044:Avoid using TheoryData type arguments that are not serializable")]
    public void Returns_false_when_any_property_is_different(OpcUaClientDataPortCommunication x, OpcUaClientDataPortCommunication y)
    {
        OpcUaClientDataPortCommunicationEqualityComparer comparer = new();

        var result = comparer.Equals(x, y);

        result.Should().BeFalse();
    }

    public static TheoryData<OpcUaClientDataPortCommunication, OpcUaClientDataPortCommunication> GetDifferentCommunications()
        => new()
        {
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.ApplicationName = "different") },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.ApplicationUri = "different") },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.Server = "different") },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.Port = 124) },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.Endpoint = "different") },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.UserAuthenticationType = 1) },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.User = "different") },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.Password = "different") },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.ApplicationCertificateSubject = "different") },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.ApplicationCertificatesStoreType = 1) },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.ApplicationCertificatesStorePath = "different") },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.TrustedCertificatesStoreType = 1) },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.TrustedCertificatesStorePath = "different") },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.TrustedIssuerCertificatesStoreType = 1) },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.TrustedIssuerCertificatesStorePath = "different") },
            { CreateDefaultCommunication(), CreateDefaultCommunication(c => c.AutoAcceptUntrustedCertificates = false) },
        };

    private static OpcUaClientDataPortCommunication CreateDefaultCommunication(Action<OpcUaClientDataPortCommunication>? modify = null)
    {
        OpcUaClientDataPortCommunication communication = new()
        {
            ApplicationName = "ApplicationName",
            ApplicationUri = "ApplicationUri",
            Server = "Server",
            Port = 123,
            Endpoint = "Endpoint",
            UserAuthenticationType = 0,
            User = "User",
            Password = "Password",
            ApplicationCertificateSubject = "CN=ApplicationName",
            ApplicationCertificatesStoreType = 0,
            ApplicationCertificatesStorePath = "StorePath",
            TrustedCertificatesStoreType = 0,
            TrustedCertificatesStorePath = "StorePath",
            TrustedIssuerCertificatesStoreType = 0,
            TrustedIssuerCertificatesStorePath = "StorePath",
            AutoAcceptUntrustedCertificates = true,
        };

        modify?.Invoke(communication);

        return communication;
    }
}
