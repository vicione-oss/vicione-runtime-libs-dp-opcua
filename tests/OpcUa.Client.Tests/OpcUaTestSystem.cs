using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Xunit;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// Starts a real OPC UA server for tests. The server binds loopback on an ephemeral port and publishes the endpoint
/// it actually got through <see cref="Communication"/>, so several test projects can run side by side and nothing
/// collides with an OPC UA product already listening on the default port.
/// </summary>
[SuppressMessage("Maintainability", "CA1515:Erwägen Sie, öffentliche Typen intern zu machen.")]
public sealed class OpcUaTestSystem : IAsyncLifetime
{
    private const string LoopbackHost = "127.0.0.1";

    private readonly StandardServer _server = new();

    internal OpcUaClientDataPortCommunication Communication { get; } = new();

    public OpcUaTestSystem()
    {
        Communication.ApplicationName = "OPC UA Test Server";
        Communication.ApplicationUri = "urn:localhost:OPCUA:DataPortTest";
        Communication.Server = LoopbackHost;
        Communication.Endpoint = "ua/dataport/test";
    }

    public async ValueTask InitializeAsync()
    {
        Communication.Port = FreeTcpPort();

        ServerConfiguration serverConfiguration = new();
        serverConfiguration.BaseAddresses.Add($"opc.tcp://{LoopbackHost}:{Communication.Port.ToString(CultureInfo.InvariantCulture)}/{Communication.Endpoint}");
        serverConfiguration.ServerProfileArray =
        [
            "http://opcfoundation.org/UA-Profile/Server/StandardUAProfile",
            "http://opcfoundation.org/UA-Profile/Server/DataAccess",
            "http://opcfoundation.org/UA-Profile/Server/ClientRedundancy",
        ];
        serverConfiguration.SecurityPolicies =
        [
            new() { SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None, },
        ];
        serverConfiguration.UserTokenPolicies =
        [
            new UserTokenPolicy(UserTokenType.Anonymous) { SecurityPolicyUri = SecurityPolicies.None, },
        ];

        ApplicationConfiguration appConfiguration = new()
        {
            ApplicationName = Communication.ApplicationName,
            ApplicationUri = Communication.ApplicationUri,
            ApplicationType = ApplicationType.Server,
            TransportQuotas = new TransportQuotas(),
            ServerConfiguration = serverConfiguration,
        };
        appConfiguration.SecurityConfiguration.ApplicationCertificate = new()
        {
            StoreType = CertificateStoreType.X509Store,
        };
        ApplicationInstance applicationInstance = new(appConfiguration);
        await applicationInstance.CheckApplicationInstanceCertificates(false, CertificateFactory.DefaultLifeTime);

        _server.Start(appConfiguration);

        PublishResolvedEndpoint();
    }

    public ValueTask DisposeAsync()
    {
        _server.Stop();
        _server.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Takes the endpoint the running server actually serves over from the server itself. The stack rewrites base
    /// addresses while starting, so the configured values are not necessarily the ones a client has to dial.
    /// </summary>
    private void PublishResolvedEndpoint()
    {
        var baseAddress = _server.CurrentInstance.EndpointAddresses.First();

        Communication.Server = baseAddress.Host;
        Communication.Port = baseAddress.Port;
        Communication.Endpoint = baseAddress.AbsolutePath.TrimStart('/');
    }

    /// <summary>
    /// Asks the operating system for a free loopback port by binding one and letting it go again.
    /// </summary>
    private static int FreeTcpPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
