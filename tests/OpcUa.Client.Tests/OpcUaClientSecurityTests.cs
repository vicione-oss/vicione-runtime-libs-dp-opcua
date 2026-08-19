using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// Connects to a secured endpoint of a real <see cref="OpcUaTestSystem"/> server with certificate validation on.
/// </summary>
[Trait("Category", "Interoperability")]
public sealed class OpcUaClient_TrustedPeerCertificates : IDisposable
{
    private readonly string _clientCertificateDirectory = Path.Combine(Path.GetTempPath(), "dp-opcua-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// A client that trusts the server's certificate through its own trusted peer store connects to an encrypted
    /// endpoint without accepting untrusted certificates.
    /// </summary>
    [Fact]
    public async Task Connects_to_a_secure_endpoint_when_the_server_certificate_is_trusted_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.SecureEndpoint = true);

        var communication = CreateCommunication(opcUa, opcUa.TrustedPeerStorePath);

        await OpcUaClientConnectionTester.TestConnectionAsync(communication);
    }

    /// <summary>
    /// The same client against an empty trusted peer store must be rejected. This is the half that proves the store is
    /// consulted at all rather than the connection succeeding for unrelated reasons.
    /// </summary>
    [Fact]
    public async Task Fails_to_connect_when_the_server_certificate_is_not_trusted_Async()
    {
        await using var opcUa = await OpcUaTestSystem.StartAsync(options => options.SecureEndpoint = true);

        var communication = CreateCommunication(opcUa, Path.Combine(_clientCertificateDirectory, "trusted"));

        var act = FluentActions.Awaiting(() => OpcUaClientConnectionTester.TestConnectionAsync(communication));

        var exception = await act.Should().ThrowAsync<DataPortConnectionFailedException>();

        var serviceResult = exception.WithInnerException<ServiceResultException>().Which;
        serviceResult.StatusCode.Should().Be(StatusCodes.BadCertificateUntrusted);
    }

    private OpcUaClientDataPortCommunication CreateCommunication(OpcUaTestSystem opcUa, string trustedCertificatesStorePath)
    {
        OpcUaClientDataPortCommunication communication = new();
        _ = new OpcUaClientDataPortProperties(communication)
        {
            ApplicationName = "OPC UA Test Client",
            ApplicationUri = "urn:localhost:OPCUA:DataPortTestClient",
            Server = opcUa.Communication.Server,
            Port = opcUa.Communication.Port,
            Endpoint = opcUa.Communication.Endpoint,

            // Basic authentication is what makes the client select the secured endpoint today.
            UserAuthenticationType = UserAuthenticationType.Basic,
            User = OpcUaTestSystem.UserName,
            Password = OpcUaTestSystem.Password,

            ApplicationCertificatesStoreType = CertificateStoreType.Directory,
            ApplicationCertificatesStorePath = Path.Combine(_clientCertificateDirectory, "own"),
            ApplicationCertificateSubject = "OPC UA Test Client",

            TrustedCertificatesStoreType = CertificateStoreType.Directory,
            TrustedCertificatesStorePath = trustedCertificatesStorePath,

            AutoAcceptUntrustedCertificates = false,
        };
        return communication;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_clientCertificateDirectory))
                Directory.Delete(_clientCertificateDirectory, true);
        }
        catch (IOException)
        {
            // A leftover temporary directory must not fail a test run.
        }
    }
}
