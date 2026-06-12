using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaServer_VerifyPassword_Logging
{
    private static OpcUaServer CreateServer(FakeLogger<IOpcUaServer> logger, string user = "admin", string password = "secret")
    {
        OpcUaServerDataPortCommunication communication = new()
        {
            User = user,
            Password = password,
            UserAuthenticationType = 1,
            Nodes = [],
        };

        return new OpcUaServer(communication, logger);
    }

    private static UserNameIdentityToken CreateToken(string user, string password) => new()
    {
        UserName = user,
        DecryptedPassword = password,
    };

    [Fact]
    public void Logs_warning_on_failed_login()
    {
        FakeLogger<IOpcUaServer> logger = new();
        using var server = CreateServer(logger);
        var token = CreateToken("admin", "wrong");

        var act = () => server.VerifyPassword(token, "session-1");

        act.Should().Throw<ServiceResultException>();
        logger.Collector.GetSnapshot().Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("Failed authentication attempt") &&
            e.Message.Contains("admin") &&
            e.Message.Contains("session-1"));
    }

    [Fact]
    public void Logs_information_on_successful_login()
    {
        FakeLogger<IOpcUaServer> logger = new();
        using var server = CreateServer(logger);
        var token = CreateToken("admin", "secret");

        server.VerifyPassword(token, "session-2");

        logger.Collector.GetSnapshot().Should().ContainSingle(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("authenticated successfully") &&
            e.Message.Contains("admin") &&
            e.Message.Contains("session-2"));
    }
}

public class OpcUaServer_HandleAnonymousAccess_Logging
{
    [Fact]
    public void Logs_warning_on_anonymous_access()
    {
        FakeLogger<IOpcUaServer> logger = new();
        OpcUaServerDataPortCommunication communication = new()
        {
            UserAuthenticationType = 0,
            Nodes = [],
        };
        using OpcUaServer server = new(communication, logger);

        server.HandleAnonymousAccess("session-3");

        logger.Collector.GetSnapshot().Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("Anonymous access used") &&
            e.Message.Contains("session-3"));
    }
}

public class OpcUaServer_LogInsecureConfigurationWarnings
{
    [Fact]
    public void Logs_warning_when_anonymous_access_is_configured()
    {
        FakeLogger<IOpcUaServer> logger = new();
        OpcUaServerDataPortCommunication communication = new()
        {
            UserAuthenticationType = 0,
            Nodes = [],
        };
        using OpcUaServer server = new(communication, logger);

        server.LogInsecureConfigurationWarnings();

        logger.Collector.GetSnapshot().Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("anonymous access enabled"));
    }

    [Fact]
    public void Logs_warning_when_auto_accept_untrusted_certificates_is_enabled()
    {
        FakeLogger<IOpcUaServer> logger = new();
        OpcUaServerDataPortCommunication communication = new()
        {
            UserAuthenticationType = 1,
            AutoAcceptUntrustedCertificates = true,
            Nodes = [],
        };
        using OpcUaServer server = new(communication, logger);

        server.LogInsecureConfigurationWarnings();

        logger.Collector.GetSnapshot().Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("AutoAcceptUntrustedCertificates=true") &&
            e.Message.Contains("man-in-the-middle"));
    }

    [Fact]
    public void Logs_warning_when_transport_quotas_are_disabled()
    {
        FakeLogger<IOpcUaServer> logger = new();
        OpcUaServerDataPortCommunication communication = new()
        {
            UserAuthenticationType = 1,
            TransportQuotas = false,
            Nodes = [],
        };
        using OpcUaServer server = new(communication, logger);

        server.LogInsecureConfigurationWarnings();

        logger.Collector.GetSnapshot().Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("TransportQuotas disabled") &&
            e.Message.Contains("denial-of-service"));
    }

    [Fact]
    public void Logs_warning_when_security_policy_is_None()
    {
        FakeLogger<IOpcUaServer> logger = new();
        OpcUaServerDataPortCommunication communication = new()
        {
            UserAuthenticationType = 1,
            SecurityPolicy = 0, // None
            Nodes = [],
        };
        using OpcUaServer server = new(communication, logger);

        server.LogInsecureConfigurationWarnings();

        logger.Collector.GetSnapshot().Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("SecurityPolicy=None"));
    }

    [Fact]
    public void Does_not_log_security_policy_warning_when_policy_is_secure()
    {
        FakeLogger<IOpcUaServer> logger = new();
        OpcUaServerDataPortCommunication communication = new()
        {
            UserAuthenticationType = 1,
            SecurityPolicy = 2, // Basic256Sha256_SignAndEncrypt (default)
            Nodes = [],
        };
        using OpcUaServer server = new(communication, logger);

        server.LogInsecureConfigurationWarnings();

        logger.Collector.GetSnapshot().Should().NotContain(e =>
            e.Message.Contains("SecurityPolicy=None"));
    }
}
