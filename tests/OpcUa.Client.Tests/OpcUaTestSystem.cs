using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Xunit;

namespace ViciOne.Suite.DataPort;

[SuppressMessage("Maintainability", "CA1515:Erwägen Sie, öffentliche Typen intern zu machen.")]
public sealed class OpcUaTestSystem : IAsyncLifetime
{
    private readonly StandardServer _server = new();

    internal OpcUaClientDataPortCommunication Communication { get; } = new();

    public OpcUaTestSystem()
    {
        Communication.ApplicationName = "OPC UA Test Server";
        Communication.ApplicationUri = "urn:localhost:OPCUA:DataPortTest";
        Communication.Server = "localhost";
        Communication.Port = 4840;
        Communication.Endpoint = "ua/dataport/test";
    }

    public async ValueTask InitializeAsync()
    {
        ServerConfiguration serverConfiguration = new();
        serverConfiguration.BaseAddresses.Add($"opc.tcp://0.0.0.0:{Communication.Port.ToString(CultureInfo.InvariantCulture)}/{Communication.Endpoint}");
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
    }

    public ValueTask DisposeAsync()
    {
        _server.Stop();
        _server.Dispose();
        return ValueTask.CompletedTask;
    }
}
