using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Testing;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using ViciOne.ManagedEngine.ExternalCommunication;
using Xunit;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// The data ports of several engines serve their data points on one server. Engines are deployed,
/// released and redeployed while a client is connected to the server the others keep running.
/// </summary>
internal sealed class SharedOpcUaServer : IAsyncDisposable
{
    private const string Namespace = "http://localhost/two-engines";
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    private readonly string _certificateDirectory = Path.Combine(Path.GetTempPath(), "dp-opcua-tests", Guid.NewGuid().ToString("N"));
    private readonly OpcUaServerInstanceManager _instanceManager = new();
    private readonly int _port = FreeTcpPort();
    private readonly Dictionary<object, Func<Task>> _releases = [];
    private readonly List<Subscription> _subscriptions = [];
    private ISession? _session;

    internal const string Channel = "value";

    internal static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    internal ISession Session => _session ?? throw new InvalidOperationException("The client is not connected.");

    internal async Task<OpcUaServerDataPortOutgoing> DeployAsync(string engine, double value)
    {
        OpcUaServerDataPortOutgoing dataPort = new(CreateCommunication(engine), _instanceManager, new FakeLogger<IOpcUaServer>());
        _releases.Add(dataPort, async () =>
        {
            await dataPort.DisconnectAsync(CancellationToken.None);
            await dataPort.DisposeAsync();
        });

        await dataPort.ConnectAsync(CancellationToken);
        await SendAsync(dataPort, value);

        return dataPort;
    }

    internal async Task ReleaseAsync(object dataPort)
    {
        await _releases[dataPort]();
        _releases.Remove(dataPort);
    }

    internal static async Task SendAsync(OpcUaServerDataPortOutgoing dataPort, double value)
        => await dataPort.SendAsync(0, [new ExternalValue { Channel = Channel, Value = value, Timestamp = DateTime.UtcNow, }], CancellationToken);

    /// <summary>
    /// Every engine serves its data points in a folder of its own, on the same server, and names its
    /// channels as it likes.
    /// </summary>
    private OpcUaServerDataPortCommunication CreateCommunication(string engine)
    {
        Node folder = new() { Id = Guid.NewGuid(), Name = engine, DesignId = OpcUaServerNodeDesignId.Folder };
        Node variable = new()
        {
            Id = Guid.NewGuid(),
            ParentId = folder.Id,
            Name = "variable",
            DesignId = OpcUaServerNodeDesignId.Variable,
            ValueType = typeof(double),
            TransferredChannels = [Channel],
            AffectedChannels = [Channel],
        };

        OpcUaServerDataPortCommunication communication = new() { Nodes = [folder, variable] };
        _ = new OpcUaServerDataPortProperties(communication)
        {
            ApplicationName = "Two engines test server",
            ApplicationUri = "urn:localhost:OPCUA:TwoEnginesTestServer",
            Namespace = Namespace,
            Server = IPAddress.Loopback.ToString(),
            Port = _port,
            Endpoint = "ua/dataport/test",
            UserAuthenticationType = UserAuthenticationType.Anonymous,
            SecurityPolicy = ServerSecurityPolicy.None,
            ApplicationCertificateSubject = "CN=Two engines test server",
            ApplicationCertificatesStoreType = CertificateStoreType.Directory,
            ApplicationCertificatesStorePath = Path.Combine(_certificateDirectory, "server"),
            AutoAcceptUntrustedCertificates = true,
        };

        return communication;
    }

    internal async Task ConnectAsync()
    {
        ApplicationConfiguration configuration = new()
        {
            ApplicationName = "Two engines test client",
            ApplicationUri = "urn:localhost:OPCUA:TwoEnginesTestClient",
            ApplicationType = ApplicationType.Client,
            TransportQuotas = new(),
            ClientConfiguration = new() { DefaultSessionTimeout = 60_000 },
        };
        configuration.SecurityConfiguration.ApplicationCertificate = new()
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = Path.Combine(_certificateDirectory, "client"),
            SubjectName = "CN=Two engines test client",
        };
        configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;

        await configuration.Validate(ApplicationType.Client);
        await new ApplicationInstance(configuration).CheckApplicationInstanceCertificates(false, CertificateFactory.DefaultLifeTime, CancellationToken);

        var url = $"opc.tcp://{IPAddress.Loopback}:{_port.ToString(CultureInfo.InvariantCulture)}/ua/dataport/test";
        ConfiguredEndpoint endpoint = new(null, CoreClientUtils.SelectEndpoint(configuration, url, false), EndpointConfiguration.Create(configuration));

        _session = await Opc.Ua.Client.Session.Create(configuration, endpoint, false, false, configuration.ApplicationName, 60_000, new UserIdentity(), null, CancellationToken);
    }

    private NodeId NodeIdOf(string path)
        => new(path, (ushort)Session.NamespaceUris.GetIndex(Namespace));

    internal async Task<List<string>> BrowseObjectsAsync()
    {
        var (references, errors) = await Session.ManagedBrowseAsync(null, null, [ObjectIds.ObjectsFolder], 0u, BrowseDirection.Forward,
            ReferenceTypeIds.HierarchicalReferences, true, (uint)NodeClass.Object, CancellationToken);

        ServiceResult.IsGood(errors[0]).Should().BeTrue();

        return [.. references[0].Select(reference => reference.BrowseName.Name)];
    }

    internal async Task<DataValue> ReadAsync(string path)
    {
        var response = await Session.ReadAsync(null, 0, TimestampsToReturn.Both, [new ReadValueId { NodeId = NodeIdOf(path), AttributeId = Attributes.Value }], CancellationToken);

        return response.Results[0];
    }

    internal async Task<MonitoredValues> MonitorAsync(string path)
    {
        Subscription subscription = new(Session.DefaultSubscription) { PublishingEnabled = true, PublishingInterval = 50 };
        _subscriptions.Add(subscription);
        Session.AddSubscription(subscription);
        await subscription.CreateAsync(CancellationToken);

        MonitoredValues values = new();
        MonitoredItem item = new(subscription.DefaultItem) { StartNodeId = NodeIdOf(path), AttributeId = Attributes.Value, SamplingInterval = 0 };
        item.Notification += (_, e) =>
        {
            if (e.NotificationValue is MonitoredItemNotification notification)
                values.Add(notification.Value);
        };

        subscription.AddItem(item);
        await subscription.ApplyChangesAsync(CancellationToken);

        return values;
    }

    private static int FreeTcpPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.CloseAsync(CancellationToken.None);
            _session.Dispose();
        }

        foreach (var subscription in _subscriptions)
            subscription.Dispose();

        foreach (var release in _releases.Values)
            await release();

        await _instanceManager.DisposeAsync();

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

    internal sealed class MonitoredValues
    {
        private readonly Channel<DataValue> _values = System.Threading.Channels.Channel.CreateUnbounded<DataValue>();

        public void Add(DataValue value) => _values.Writer.TryWrite(value);

        public async Task<DataValue> WaitForAsync(Func<DataValue, bool> predicate)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(s_timeout);

            await foreach (var value in _values.Reader.ReadAllAsync(timeout.Token))
            {
                if (predicate(value))
                    return value;
            }

            throw new InvalidOperationException("The monitored item stopped reporting.");
        }
    }
}
