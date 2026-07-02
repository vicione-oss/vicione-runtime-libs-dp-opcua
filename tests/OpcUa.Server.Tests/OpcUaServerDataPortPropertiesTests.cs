using System;
using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaServerDataPortProperties_ApplicationName
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string applicationName)
    {
        OpcUaServerDataPortProperties properties = new(new() { ApplicationName = applicationName, });

        properties.ApplicationName.Should().Be(applicationName);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string applicationName)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            ApplicationName = applicationName,
        };

        communication.ApplicationName.Should().Be(applicationName);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "ViciOne Suite" },
    };
}

[SuppressMessage("Design", "CA1054:URI-ähnliche Parameter dürfen keine Zeichenfolgen sein")]
public class OpcUaServerDataPortProperties_ApplicationUri
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string applicationUri)
    {
        OpcUaServerDataPortProperties properties = new(new() { ApplicationUri = applicationUri, });

        properties.ApplicationUri.Should().Be(applicationUri);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string applicationUri)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            ApplicationUri = applicationUri,
        };

        communication.ApplicationUri.Should().Be(applicationUri);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "http://vicione.com" },
    };
}

public class OpcUaServerDataPortProperties_Namespace
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string @namespace)
    {
        OpcUaServerDataPortProperties properties = new(new() { Namespace = @namespace, });

        properties.Namespace.Should().Be(@namespace);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string @namespace)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            Namespace = @namespace,
        };

        communication.Namespace.Should().Be(@namespace);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "http://vicione.com" },
    };
}

public class OpcUaServerDataPortProperties_Server
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string server)
    {
        OpcUaServerDataPortProperties properties = new(new() { Server = server, });

        properties.Server.Should().Be(server);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string server)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            Server = server,
        };

        communication.Server.Should().Be(server);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "opcau-host.industry" },
    };
}

public class OpcUaServerDataPortProperties_Port
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(int port)
    {
        OpcUaServerDataPortProperties properties = new(new() { Port = port, });

        properties.Port.Should().Be(port);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(int port)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            Port = port,
        };

        communication.Port.Should().Be(port);
    }

    public static TheoryData<int> Conversion() => new()
    {
        { 0124 },
    };
}

public class OpcUaServerDataPortProperties_Endpoint
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string endpoint)
    {
        OpcUaServerDataPortProperties properties = new(new() { Endpoint = endpoint, });

        properties.Endpoint.Should().Be(endpoint);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string endpoint)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            Endpoint = endpoint,
        };

        communication.Endpoint.Should().Be(endpoint);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "ua/api" },
    };
}

public class OpcUaServerDataPortProperties_UserAuthenticationType
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(byte userAuthenticationType, UserAuthenticationType type)
    {
        OpcUaServerDataPortCommunication communication = new() { UserAuthenticationType = userAuthenticationType, };

        OpcUaServerDataPortProperties properties = new(communication);

        properties.UserAuthenticationType.Should().Be(type);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(byte userAuthenticationType, UserAuthenticationType type)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            UserAuthenticationType = type,
        };

        communication.UserAuthenticationType.Should().Be(userAuthenticationType);
    }

    [Fact]
    public void Setter_throws_for_invalid_value()
    {
        OpcUaServerDataPortCommunication communication = new();

        OpcUaServerDataPortProperties properties = new(communication);

        properties.Invoking(p => p.UserAuthenticationType = (UserAuthenticationType)2)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid user authentication type.");
    }

    [Fact]
    public void Getter_throws_for_invalid_value()
    {
        OpcUaServerDataPortCommunication communication = new() { UserAuthenticationType = 2, };

        OpcUaServerDataPortProperties properties = new(communication);

        properties.Invoking(p => _ = p.UserAuthenticationType)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid user authentication type.");
    }

    public static TheoryData<byte, UserAuthenticationType> Conversion()
        => new()
        {
            { 0, UserAuthenticationType.Anonymous },
            { 1, UserAuthenticationType.Basic },
        };
}

public class OpcUaServerDataPortProperties_User
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string user)
    {
        OpcUaServerDataPortProperties properties = new(new() { User = user, });

        properties.User.Should().Be(user);
    }

    [Theory]
    [MemberData(nameof(Conversion))]

    public void Setter_sets_User(string user)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            User = user,
        };

        communication.User.Should().Be(user);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "superuser" },
    };
}

public class OpcUaServerDataPortProperties_Password
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string password)
    {
        OpcUaServerDataPortProperties properties = new(new() { Password = password, });

        properties.Password.Should().Be(password);
    }

    [Theory]
    [MemberData(nameof(Conversion))]

    public void Setter_sets_Password(string password)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            Password = password,
        };

        communication.Password.Should().Be(password);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "_:;#+*a1" },
    };
}

public class OpcUaServerDataPortProperties_ApplicationCertificateSubject
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string applicationCertificateSubject)
    {
        OpcUaServerDataPortProperties properties = new(new() { ApplicationCertificateSubject = applicationCertificateSubject, });

        properties.ApplicationCertificateSubject.Should().Be(applicationCertificateSubject);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string applicationCertificateSubject)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            ApplicationCertificateSubject = applicationCertificateSubject,
        };

        communication.ApplicationCertificateSubject.Should().Be(applicationCertificateSubject);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "CN=ApplicationName" },
    };
}

public class OpcUaServerDataPortProperties_ApplicationCertificatesStoreType
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(byte applicationCertificatesStoreType, string? storeType)
    {
        OpcUaServerDataPortCommunication communication = new() { ApplicationCertificatesStoreType = applicationCertificatesStoreType, };

        OpcUaServerDataPortProperties properties = new(communication);

        properties.ApplicationCertificatesStoreType.Should().Be(storeType);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(byte applicationCertificatesStoreType, string? storeType)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            ApplicationCertificatesStoreType = storeType,
        };

        communication.ApplicationCertificatesStoreType.Should().Be(applicationCertificatesStoreType);
    }

    [Fact]
    public void Setter_throws_for_invalid_value()
    {
        OpcUaServerDataPortCommunication communication = new();

        OpcUaServerDataPortProperties properties = new(communication);

        properties.Invoking(p => p.ApplicationCertificatesStoreType = "invalid")
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid application store type.");
    }

    [Fact]
    public void Getter_throws_for_invalid_value()
    {
        OpcUaServerDataPortCommunication communication = new() { ApplicationCertificatesStoreType = 9, };

        OpcUaServerDataPortProperties properties = new(communication);

        properties.Invoking(p => _ = p.ApplicationCertificatesStoreType)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid application store type.");
    }

    public static TheoryData<byte, string?> Conversion() => new()
    {
        {0, null },
        { 1, CertificateStoreType.X509Store },
        { 2, CertificateStoreType.Directory },
    };
}

public class OpcUaServerDataPortProperties_ApplicationCertificatesStorePath
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string applicationCertificatesStorePath)
    {
        OpcUaServerDataPortProperties properties = new(new() { ApplicationCertificatesStorePath = applicationCertificatesStorePath, });

        properties.ApplicationCertificatesStorePath.Should().Be(applicationCertificatesStorePath);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string applicationCertificatesStorePath)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            ApplicationCertificatesStorePath = applicationCertificatesStorePath,
        };

        communication.ApplicationCertificatesStorePath.Should().Be(applicationCertificatesStorePath);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "/usr/shared/certs" },
    };
}

public class OpcUaServerDataPortProperties_TrustedCertificatesStoreType
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(byte trustedCertificatesStoreType, string? storeType)
    {
        OpcUaServerDataPortCommunication communication = new() { TrustedCertificatesStoreType = trustedCertificatesStoreType, };

        OpcUaServerDataPortProperties properties = new(communication);

        properties.TrustedCertificatesStoreType.Should().Be(storeType);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(byte trustedCertificatesStoreType, string? storeType)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            TrustedCertificatesStoreType = storeType,
        };

        communication.TrustedCertificatesStoreType.Should().Be(trustedCertificatesStoreType);
    }

    [Fact]
    public void Setter_throws_for_invalid_value()
    {
        OpcUaServerDataPortCommunication communication = new();

        OpcUaServerDataPortProperties properties = new(communication);

        properties.Invoking(p => p.TrustedCertificatesStoreType = "invalid")
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid trusted store type.");
    }

    [Fact]
    public void Getter_throws_for_invalid_value()
    {
        OpcUaServerDataPortCommunication communication = new() { TrustedCertificatesStoreType = 3, };

        OpcUaServerDataPortProperties properties = new(communication);

        properties.Invoking(p => _ = p.TrustedCertificatesStoreType)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid trusted store type.");
    }

    public static TheoryData<byte, string?> Conversion() => new()
    {
        { 0, null },
        { 1, CertificateStoreType.X509Store },
        { 2, CertificateStoreType.Directory },
    };
}

public class OpcUaServerDataPortProperties_TrustedCertificatesStorePath
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string trustedCertificatesStorePath)
    {
        OpcUaServerDataPortProperties properties = new(new() { TrustedCertificatesStorePath = trustedCertificatesStorePath, });

        properties.TrustedCertificatesStorePath.Should().Be(trustedCertificatesStorePath);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string trustedCertificatesStorePath)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            TrustedCertificatesStorePath = trustedCertificatesStorePath,
        };

        communication.TrustedCertificatesStorePath.Should().Be(trustedCertificatesStorePath);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "/usr/shared/certs" },
    };
}

public class OpcUaServerDataPortProperties_TrustedIssuerCertificatesStoreType
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(byte trustedIssuerCertificatesStoreType, string? storeType)
    {
        OpcUaServerDataPortCommunication communication = new() { TrustedIssuerCertificatesStoreType = trustedIssuerCertificatesStoreType, };

        OpcUaServerDataPortProperties properties = new(communication);

        properties.TrustedIssuerCertificatesStoreType.Should().Be(storeType);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(byte trustedIssuerCertificatesStoreType, string? storeType)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            TrustedIssuerCertificatesStoreType = storeType,
        };

        communication.TrustedIssuerCertificatesStoreType.Should().Be(trustedIssuerCertificatesStoreType);
    }

    [Fact]
    public void Setter_throws_for_invalid_value()
    {
        OpcUaServerDataPortCommunication communication = new();

        OpcUaServerDataPortProperties properties = new(communication);

        properties.Invoking(p => p.TrustedIssuerCertificatesStoreType = "invalid")
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid trusted issuer store type.");
    }

    [Fact]
    public void Getter_throws_for_invalid_value()
    {
        OpcUaServerDataPortCommunication communication = new() { TrustedIssuerCertificatesStoreType = 3, };

        OpcUaServerDataPortProperties properties = new(communication);

        properties.Invoking(p => _ = p.TrustedIssuerCertificatesStoreType)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid trusted issuer store type.");
    }

    public static TheoryData<byte, string?> Conversion() => new()
    {
        { 0, null },
        { 1, CertificateStoreType.X509Store },
        { 2, CertificateStoreType.Directory },
    };
}

public class OpcUaServerDataPortProperties_TrustedIssuerCertificatesStorePath
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string trustedIssuerCertificatesStorePath)
    {
        OpcUaServerDataPortProperties properties = new(new() { TrustedIssuerCertificatesStorePath = trustedIssuerCertificatesStorePath, });

        properties.TrustedIssuerCertificatesStorePath.Should().Be(trustedIssuerCertificatesStorePath);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string trustedIssuerCertificatesStorePath)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            TrustedIssuerCertificatesStorePath = trustedIssuerCertificatesStorePath,
        };

        communication.TrustedIssuerCertificatesStorePath.Should().Be(trustedIssuerCertificatesStorePath);
    }

    public static TheoryData<string> Conversion() => new()
    {
        { string.Empty },
        { "/usr/shared/certs" },
    };
}

public class OpcUaServerDataPortProperties_AutoAcceptUntrustedCertificates
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(bool autoAcceptUntrustedCertificates)
    {
        OpcUaServerDataPortProperties properties = new(new() { AutoAcceptUntrustedCertificates = autoAcceptUntrustedCertificates, });

        properties.AutoAcceptUntrustedCertificates.Should().Be(autoAcceptUntrustedCertificates);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(bool autoAcceptUntrustedCertificates)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            AutoAcceptUntrustedCertificates = autoAcceptUntrustedCertificates,
        };

        communication.AutoAcceptUntrustedCertificates.Should().Be(autoAcceptUntrustedCertificates);
    }

    public static TheoryData<bool> Conversion() => new()
    {
        { true },
        { false },
    };
}

public class OpcUaServerDataPortProperties_TransportQuotas
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(bool transportQuotas)
    {
        OpcUaServerDataPortProperties properties = new(new() { TransportQuotas = transportQuotas, });

        properties.TransportQuotas.Should().Be(transportQuotas);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(bool transportQuotas)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            TransportQuotas = transportQuotas,
        };

        communication.TransportQuotas.Should().Be(transportQuotas);
    }

    public static TheoryData<bool> Conversion() => new()
    {
        { true },
        { false },
    };
}

public class OpcUaServerDataPortProperties_SecurityPolicy
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(byte securityPolicy, ServerSecurityPolicy policy)
    {
        OpcUaServerDataPortCommunication communication = new() { SecurityPolicy = securityPolicy, };

        OpcUaServerDataPortProperties properties = new(communication);

        properties.SecurityPolicy.Should().Be(policy);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(byte securityPolicy, ServerSecurityPolicy policy)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            SecurityPolicy = policy,
        };

        communication.SecurityPolicy.Should().Be(securityPolicy);
    }

    [Fact]
    public void Setter_throws_for_invalid_value()
    {
        OpcUaServerDataPortCommunication communication = new();

        OpcUaServerDataPortProperties properties = new(communication);

        properties.Invoking(p => p.SecurityPolicy = (ServerSecurityPolicy)99)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid security policy.");
    }

    [Fact]
    public void Getter_throws_for_invalid_value()
    {
        OpcUaServerDataPortCommunication communication = new() { SecurityPolicy = 99, };

        OpcUaServerDataPortProperties properties = new(communication);

        properties.Invoking(p => _ = p.SecurityPolicy)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid security policy.");
    }

    public static TheoryData<byte, ServerSecurityPolicy> Conversion()
        => new()
        {
            { 0, ServerSecurityPolicy.None },
            { 1, ServerSecurityPolicy.Basic256Sha256_Sign },
            { 2, ServerSecurityPolicy.Basic256Sha256_SignAndEncrypt },
            { 3, ServerSecurityPolicy.Aes256_Sha256_RsaPss_SignAndEncrypt },
        };
}

public class OpcUaServerDataPortProperties_MinPublishingInterval
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(int minPublishingInterval)
    {
        OpcUaServerDataPortProperties properties = new(new() { MinPublishingInterval = minPublishingInterval, });

        properties.MinPublishingInterval.Should().Be(minPublishingInterval);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(int minPublishingInterval)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            MinPublishingInterval = minPublishingInterval,
        };

        communication.MinPublishingInterval.Should().Be(minPublishingInterval);
    }

    [Fact]
    public void Default_value_is_100()
    {
        OpcUaServerDataPortCommunication communication = new();

        OpcUaServerDataPortProperties properties = new(communication);

        properties.MinPublishingInterval.Should().Be(100);
    }

    public static TheoryData<int> Conversion() => new()
    {
        { 50 },
        { 100 },
        { 500 },
        { 60000 },
    };
}

public class OpcUaServerDataPortProperties_MaxPublishingInterval
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(int maxPublishingInterval)
    {
        OpcUaServerDataPortProperties properties = new(new() { MaxPublishingInterval = maxPublishingInterval, });

        properties.MaxPublishingInterval.Should().Be(maxPublishingInterval);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(int maxPublishingInterval)
    {
        OpcUaServerDataPortCommunication communication = new();

        _ = new OpcUaServerDataPortProperties(communication)
        {
            MaxPublishingInterval = maxPublishingInterval,
        };

        communication.MaxPublishingInterval.Should().Be(maxPublishingInterval);
    }

    [Fact]
    public void Default_value_is_1000()
    {
        OpcUaServerDataPortCommunication communication = new();

        OpcUaServerDataPortProperties properties = new(communication);

        properties.MaxPublishingInterval.Should().Be(1000);
    }

    public static TheoryData<int> Conversion() => new()
    {
        { 100 },
        { 1000 },
        { 60000 },
        { 3600000 },
    };
}
