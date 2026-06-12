using System;
using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaClientDataPortProperties_ApplicationName
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string applicationName)
    {
        OpcUaClientDataPortProperties properties = new(new() { ApplicationName = applicationName, });

        properties.ApplicationName.Should().Be(applicationName);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string applicationName)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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
public class OpcUaClientDataPortProperties_ApplicationUri
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string applicationUri)
    {
        OpcUaClientDataPortProperties properties = new(new() { ApplicationUri = applicationUri, });

        properties.ApplicationUri.Should().Be(applicationUri);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string applicationUri)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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

public class OpcUaClientDataPortProperties_Server
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string server)
    {
        OpcUaClientDataPortProperties properties = new(new() { Server = server, });

        properties.Server.Should().Be(server);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string server)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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

public class OpcUaClientDataPortProperties_Port
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(int port)
    {
        OpcUaClientDataPortProperties properties = new(new() { Port = port, });

        properties.Port.Should().Be(port);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(int port)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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

public class OpcUaClientDataPortProperties_Endpoint
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string endpoint)
    {
        OpcUaClientDataPortProperties properties = new(new() { Endpoint = endpoint, });

        properties.Endpoint.Should().Be(endpoint);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string endpoint)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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

public class OpcUaClientDataPortProperties_UserAuthenticationType
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(byte userAuthenticationType, UserAuthenticationType type)
    {
        OpcUaClientDataPortCommunication communication = new() { UserAuthenticationType = userAuthenticationType, };

        OpcUaClientDataPortProperties properties = new(communication);

        properties.UserAuthenticationType.Should().Be(type);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(byte userAuthenticationType, UserAuthenticationType type)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
        {
            UserAuthenticationType = type,
        };

        communication.UserAuthenticationType.Should().Be(userAuthenticationType);
    }

    [Fact]
    public void Setter_throws_for_invalid_value()
    {
        OpcUaClientDataPortCommunication communication = new();

        OpcUaClientDataPortProperties properties = new(communication);

        properties.Invoking(p => p.UserAuthenticationType = (UserAuthenticationType)2)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid user authentication type.");
    }

    [Fact]
    public void Getter_throws_for_invalid_value()
    {
        OpcUaClientDataPortCommunication communication = new() { UserAuthenticationType = 2, };

        OpcUaClientDataPortProperties properties = new(communication);

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

public class OpcUaClientDataPortProperties_User
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string user)
    {
        OpcUaClientDataPortProperties properties = new(new() { User = user, });

        properties.User.Should().Be(user);
    }

    [Theory]
    [MemberData(nameof(Conversion))]

    public void Setter_sets_User(string user)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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

public class OpcUaClientDataPortProperties_Password
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string password)
    {
        OpcUaClientDataPortProperties properties = new(new() { Password = password, });

        properties.Password.Should().Be(password);
    }

    [Theory]
    [MemberData(nameof(Conversion))]

    public void Setter_sets_Password(string password)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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

public class OpcUaClientDataPortProperties_ApplicationCertificateSubject
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string applicationCertificateSubject)
    {
        OpcUaClientDataPortProperties properties = new(new() { ApplicationCertificateSubject = applicationCertificateSubject, });

        properties.ApplicationCertificateSubject.Should().Be(applicationCertificateSubject);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string applicationCertificateSubject)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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

public class OpcUaClientDataPortProperties_ApplicationCertificatesStoreType
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(byte applicationCertificatesStoreType, string? storeType)
    {
        OpcUaClientDataPortCommunication communication = new() { ApplicationCertificatesStoreType = applicationCertificatesStoreType, };

        OpcUaClientDataPortProperties properties = new(communication);

        properties.ApplicationCertificatesStoreType.Should().Be(storeType);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(byte applicationCertificatesStoreType, string? storeType)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
        {
            ApplicationCertificatesStoreType = storeType,
        };

        communication.ApplicationCertificatesStoreType.Should().Be(applicationCertificatesStoreType);
    }

    [Fact]
    public void Setter_throws_for_invalid_value()
    {
        OpcUaClientDataPortCommunication communication = new();

        OpcUaClientDataPortProperties properties = new(communication);

        properties.Invoking(p => p.ApplicationCertificatesStoreType = "invalid")
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid application store type.");
    }

    [Fact]
    public void Getter_throws_for_invalid_value()
    {
        OpcUaClientDataPortCommunication communication = new() { ApplicationCertificatesStoreType = 9, };

        OpcUaClientDataPortProperties properties = new(communication);

        properties.Invoking(p => _ = p.ApplicationCertificatesStoreType)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid application store type.");
    }

    public static TheoryData<byte, string?> Conversion() => new()
    {
        { 0, null },
        { 1, CertificateStoreType.X509Store },
        { 2, CertificateStoreType.Directory },
    };
}

public class OpcUaClientDataPortProperties_ApplicationCertificatesStorePath
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string applicationCertificatesStorePath)
    {
        OpcUaClientDataPortProperties properties = new(new() { ApplicationCertificatesStorePath = applicationCertificatesStorePath, });

        properties.ApplicationCertificatesStorePath.Should().Be(applicationCertificatesStorePath);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string applicationCertificatesStorePath)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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

public class OpcUaClientDataPortProperties_TrustedCertificatesStoreType
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(byte trustedCertificatesStoreType, string? storeType)
    {
        OpcUaClientDataPortCommunication communication = new() { TrustedCertificatesStoreType = trustedCertificatesStoreType, };

        OpcUaClientDataPortProperties properties = new(communication);

        properties.TrustedCertificatesStoreType.Should().Be(storeType);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(byte trustedCertificatesStoreType, string? storeType)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
        {
            TrustedCertificatesStoreType = storeType,
        };

        communication.TrustedCertificatesStoreType.Should().Be(trustedCertificatesStoreType);
    }

    [Fact]
    public void Setter_throws_for_invalid_value()
    {
        OpcUaClientDataPortCommunication communication = new();

        OpcUaClientDataPortProperties properties = new(communication);

        properties.Invoking(p => p.TrustedCertificatesStoreType = "invalid")
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid trusted store type.");
    }

    [Fact]
    public void Getter_throws_for_invalid_value()
    {
        OpcUaClientDataPortCommunication communication = new() { TrustedCertificatesStoreType = 3, };

        OpcUaClientDataPortProperties properties = new(communication);

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

public class OpcUaClientDataPortProperties_TrustedCertificatesStorePath
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string trustedCertificatesStorePath)
    {
        OpcUaClientDataPortProperties properties = new(new() { TrustedCertificatesStorePath = trustedCertificatesStorePath, });

        properties.TrustedCertificatesStorePath.Should().Be(trustedCertificatesStorePath);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string trustedCertificatesStorePath)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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

public class OpcUaClientDataPortProperties_TrustedIssuerCertificatesStoreType
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(byte trustedIssuerCertificatesStoreType, string? storeType)
    {
        OpcUaClientDataPortCommunication communication = new() { TrustedIssuerCertificatesStoreType = trustedIssuerCertificatesStoreType, };

        OpcUaClientDataPortProperties properties = new(communication);

        properties.TrustedIssuerCertificatesStoreType.Should().Be(storeType);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(byte trustedIssuerCertificatesStoreType, string? storeType)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
        {
            TrustedIssuerCertificatesStoreType = storeType,
        };

        communication.TrustedIssuerCertificatesStoreType.Should().Be(trustedIssuerCertificatesStoreType);
    }

    [Fact]
    public void Setter_throws_for_invalid_value()
    {
        OpcUaClientDataPortCommunication communication = new();

        OpcUaClientDataPortProperties properties = new(communication);

        properties.Invoking(p => p.TrustedIssuerCertificatesStoreType = "invalid")
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid trusted issuer store type.");
    }

    [Fact]
    public void Getter_throws_for_invalid_value()
    {
        OpcUaClientDataPortCommunication communication = new() { TrustedIssuerCertificatesStoreType = 3, };

        OpcUaClientDataPortProperties properties = new(communication);

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

public class OpcUaClientDataPortProperties_TrustedIssuerCertificatesStorePath
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(string trustedIssuerCertificatesStorePath)
    {
        OpcUaClientDataPortProperties properties = new(new() { TrustedIssuerCertificatesStorePath = trustedIssuerCertificatesStorePath, });

        properties.TrustedIssuerCertificatesStorePath.Should().Be(trustedIssuerCertificatesStorePath);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(string trustedIssuerCertificatesStorePath)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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

public class OpcUaClientDataPortProperties_AutoAcceptUntrustedCertificates
{
    [Theory]
    [MemberData(nameof(Conversion))]
    public void Getter_returns_value(bool autoAcceptUntrustedCertificates)
    {
        OpcUaClientDataPortProperties properties = new(new() { AutoAcceptUntrustedCertificates = autoAcceptUntrustedCertificates, });

        properties.AutoAcceptUntrustedCertificates.Should().Be(autoAcceptUntrustedCertificates);
    }

    [Theory]
    [MemberData(nameof(Conversion))]
    public void Setter_sets_value(bool autoAcceptUntrustedCertificates)
    {
        OpcUaClientDataPortCommunication communication = new();

        _ = new OpcUaClientDataPortProperties(communication)
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
