using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Testing;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaServer_HandleAnonymousAccess
{
    /// <summary>
    /// The token policy the server advertises asks a client for a user name, but a client is free
    /// to present an anonymous token anyway. Accepting it would hand every caller a session on a
    /// server configured to authenticate its users.
    /// </summary>
    [Fact]
    public void Rejects_the_session_when_the_server_authenticates_users()
    {
        using var server = CreateServer(userAuthenticationType: 1);

        var act = () => server.HandleAnonymousAccess("session-1");

        act.Should().Throw<ServiceResultException>()
            .Which.StatusCode.Should().Be(StatusCodes.BadIdentityTokenRejected);
    }

    [Fact]
    public void Grants_the_anonymous_role_when_the_server_allows_anonymous_access()
    {
        using var server = CreateServer(userAuthenticationType: 0);

        var identity = server.HandleAnonymousAccess("session-1");

        identity.TokenType.Should().Be(UserTokenType.Anonymous);
    }

    private static OpcUaServer CreateServer(byte userAuthenticationType)
    {
        OpcUaServerDataPortCommunication communication = new()
        {
            UserAuthenticationType = userAuthenticationType,
            User = "admin",
            Password = "secret",
            Nodes = [],
        };

        return new(communication, new FakeLogger<IOpcUaServer>());
    }
}

public class OpcUaServer_AreCredentialsValid
{
    [Fact]
    public void Correct_credentials_return_true()
    {
        var result = OpcUaServer.AreCredentialsValid("admin", "admin", "secret", "secret");

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("admin", "wrong", "secret", "secret")]
    [InlineData("admin", "admin", "secret", "wrong")]
    [InlineData("admin", "wrong", "secret", "wrong")]
    [InlineData("admin", "", "secret", "secret")]
    [InlineData("admin", "admin", "secret", "")]
    public void Incorrect_credentials_return_false(string expectedUser, string actualUser, string expectedPassword, string actualPassword)
    {
        var result = OpcUaServer.AreCredentialsValid(expectedUser, actualUser, expectedPassword, actualPassword);

        result.Should().BeFalse();
    }

    [Fact]
    public void Null_credentials_matching_return_true()
    {
        var result = OpcUaServer.AreCredentialsValid(null, null, null, null);

        result.Should().BeTrue();
    }

    [Fact]
    public void Null_expected_vs_empty_actual_return_true()
    {
        var result = OpcUaServer.AreCredentialsValid(null, "", null, "");

        result.Should().BeTrue();
    }

    [Fact]
    public void Null_password_vs_non_null_password_returns_false()
    {
        var result = OpcUaServer.AreCredentialsValid("admin", "admin", null, "secret");

        result.Should().BeFalse();
    }
}

public class OpcUaServer_RouteWrite
{
    private static readonly ReceivedWrite s_write = new("value", 3.4d, new DateTime(2026, 10, 9), StatusCodes.Good, new DateTime(2026, 10, 9));

    /// <summary>
    /// Every engine names its own channels, so two data ports on one server may name a channel the
    /// same. A write reaches the data port its variable belongs to and no other.
    /// </summary>
    [Fact]
    public void Hands_a_write_to_the_data_port_it_belongs_to_alone()
    {
        using var server = CreateServer();
        object owner = new();
        List<ReceivedWrite> owned = [];
        List<ReceivedWrite> other = [];
        server.ReceiveWrites(owner, owned.Add);
        server.ReceiveWrites(new object(), other.Add);

        server.RouteWrite(owner, s_write);

        owned.Should().Equal(s_write);
        other.Should().BeEmpty();
    }

    [Fact]
    public void Hands_no_write_to_a_data_port_that_stopped_receiving()
    {
        using var server = CreateServer();
        object owner = new();
        List<ReceivedWrite> received = [];
        server.ReceiveWrites(owner, received.Add);

        server.StopReceivingWrites(owner);
        server.RouteWrite(owner, s_write);

        received.Should().BeEmpty();
    }

    private static OpcUaServer CreateServer()
        => new(new OpcUaServerDataPortCommunication { Nodes = [] }, new FakeLogger<IOpcUaServer>());
}
