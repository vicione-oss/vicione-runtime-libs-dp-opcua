using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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
/// <remarks>
/// Used as a shared collection fixture through <see cref="OpcUaTestEnvironment"/> for the plain, unsecured server.
/// A test that needs more asks for its own instance through <see cref="StartAsync"/>.
/// </remarks>
[SuppressMessage("Maintainability", "CA1515:Erwägen Sie, öffentliche Typen intern zu machen.")]
public sealed class OpcUaTestSystem : IAsyncLifetime
{
    private const string LoopbackHost = "127.0.0.1";

    /// <summary>Credentials the server accepts while <see cref="OpcUaTestSystemOptions.SecureEndpoint"/> is on.</summary>
    internal const string UserName = "tester";

    /// <inheritdoc cref="UserName"/>
    internal const string Password = "tester-secret";

    private readonly OpcUaTestSystemOptions _options;
    private readonly string _certificateDirectory = Path.Combine(Path.GetTempPath(), "dp-opcua-tests", Guid.NewGuid().ToString("N"));
    private readonly StandardServer _server = new();

    internal OpcUaClientDataPortCommunication Communication { get; } = new();

    /// <summary>
    /// A directory certificate store holding this server's certificate. A client can be pointed at it to trust this
    /// server specifically, rather than switching untrusted certificates on. Populated for a secure endpoint only.
    /// </summary>
    internal string TrustedPeerStorePath => Path.Combine(_certificateDirectory, "client", "trusted");

    public OpcUaTestSystem()
        : this(new())
    {
    }

    private OpcUaTestSystem(OpcUaTestSystemOptions options)
    {
        _options = options;

        Communication.ApplicationName = "OPC UA Test Server";
        Communication.ApplicationUri = "urn:localhost:OPCUA:DataPortTest";
        Communication.Server = LoopbackHost;
        Communication.Endpoint = "ua/dataport/test";
    }

    /// <summary>
    /// Starts a server configured by <paramref name="configure"/> for a test that needs more than the shared fixture's
    /// plain server. Dispose it with <c>await using</c>.
    /// </summary>
    internal static async Task<OpcUaTestSystem> StartAsync(Action<OpcUaTestSystemOptions>? configure = default)
    {
        OpcUaTestSystemOptions options = new();
        configure?.Invoke(options);

        OpcUaTestSystem testSystem = new(options);

        try
        {
            await testSystem.InitializeAsync();
            return testSystem;
        }
        catch
        {
            await testSystem.DisposeAsync();
            throw;
        }
    }

    public async ValueTask InitializeAsync()
    {
        Communication.Port = FreeTcpPort();

        var appConfiguration = CreateApplicationConfiguration();

        ApplicationInstance applicationInstance = new(appConfiguration);
        await applicationInstance.CheckApplicationInstanceCertificates(false, CertificateFactory.DefaultLifeTime);

        if (_options.SecureEndpoint)
            await ExportServerCertificateAsync(appConfiguration.SecurityConfiguration.ApplicationCertificate);

        _server.Start(appConfiguration);

        if (_options.SecureEndpoint)
            _server.CurrentInstance.SessionManager.ImpersonateUser += ImpersonateUser;

        PublishResolvedEndpoint();
    }

    public ValueTask DisposeAsync()
    {
        _server.Stop();
        _server.Dispose();

        DeleteCertificateDirectory();

        return ValueTask.CompletedTask;
    }

    private ApplicationConfiguration CreateApplicationConfiguration()
    {
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

        if (_options.SecureEndpoint)
        {
            serverConfiguration.SecurityPolicies.Insert(0, new() { SecurityMode = MessageSecurityMode.SignAndEncrypt, SecurityPolicyUri = SecurityPolicies.Basic256Sha256, });
            serverConfiguration.UserTokenPolicies.Insert(0, new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256, });
        }

        ApplicationConfiguration appConfiguration = new()
        {
            ApplicationName = Communication.ApplicationName,
            ApplicationUri = Communication.ApplicationUri,
            ApplicationType = ApplicationType.Server,
            TransportQuotas = new TransportQuotas(),
            ServerConfiguration = serverConfiguration,
        };

        if (!_options.SecureEndpoint)
        {
            appConfiguration.SecurityConfiguration.ApplicationCertificate = new()
            {
                StoreType = CertificateStoreType.X509Store,
            };

            return appConfiguration;
        }

        // A directory store, so the certificate can be handed to a client as a file. The server's own trust decisions
        // are not what the secure tests are about, so it accepts whatever client certificate turns up.
        appConfiguration.SecurityConfiguration.ApplicationCertificate = new()
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = Path.Combine(_certificateDirectory, "server", "own"),
            SubjectName = Communication.ApplicationName,
        };
        appConfiguration.SecurityConfiguration.TrustedPeerCertificates = new()
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = Path.Combine(_certificateDirectory, "server", "trusted"),
        };
        appConfiguration.SecurityConfiguration.TrustedIssuerCertificates = new()
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = Path.Combine(_certificateDirectory, "server", "issuer"),
        };
        appConfiguration.SecurityConfiguration.RejectedCertificateStore = new()
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = Path.Combine(_certificateDirectory, "server", "rejected"),
        };
        appConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;

        return appConfiguration;
    }

    /// <summary>
    /// Copies the public part of the server certificate into <see cref="TrustedPeerStorePath"/>.
    /// </summary>
    private async Task ExportServerCertificateAsync(CertificateIdentifier applicationCertificate)
    {
        var certificate = applicationCertificate.Certificate ?? await applicationCertificate.Find(false, null);

        CertificateIdentifier trustedPeerStore = new()
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = TrustedPeerStorePath,
        };

        using var store = trustedPeerStore.OpenStore();
        await store.Add(X509CertificateLoader.LoadCertificate(certificate.RawData), null);
    }

    /// <summary>
    /// Accepts <see cref="UserName"/> and <see cref="Password"/>, and nothing else. Mirrors the production server's
    /// handler in shape: set the identity, or throw.
    /// </summary>
    private static void ImpersonateUser(Session session, ImpersonateEventArgs args)
    {
        if (args.NewIdentity is not UserNameIdentityToken userNameToken)
            throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid, "The test server expects a username token.");

        if (userNameToken.UserName != UserName || userNameToken.DecryptedPassword != Password)
            throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "Invalid username or password.");

        args.Identity = new UserIdentity(userNameToken);
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

    private void DeleteCertificateDirectory()
    {
        try
        {
            if (Directory.Exists(_certificateDirectory))
                Directory.Delete(_certificateDirectory, true);
        }
        catch (IOException)
        {
            // A leftover temporary directory must not fail a test run.
        }
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
