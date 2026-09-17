using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using ViciOne.Suite.DataPort;

var parentId = Guid.Parse("f57b4c50-1b4b-4513-a54a-e5274e7343e9");
var childId = Guid.Parse("eb093bbe-b340-40ee-91cc-e552b73f0c01");
var writeableChildId = Guid.Parse("6b4051bd-1c77-4191-b715-4a0abafd664e");
var statusId = Guid.Parse("0a2f3f6f-6d1e-4a2a-9a8c-2b7cf1d1f3a5");

OpcUaServerDataPortCommunication communication = new()
{
    Nodes =
    [
        new()
        {
            DesignId = OpcUaServerNodeDesignId.Folder,
            Id = parentId,
            Name = "parent",
        },
        new()
        {
            DesignId = OpcUaServerNodeDesignId.Variable,
            Id = childId,
            Name = "child",
            ParentId = parentId,
            TransferredChannels = ["child", "statusChannel"],
            AffectedChannels = ["child"],
            ValueType = typeof(int),
            Properties = new()
            {
                { OpcUaServerDataPortPropertyNames.ReadOnly, new() { Value = true } },
            }
        },
        new()
        {
            DesignId = OpcUaServerNodeDesignId.StatusCode,
            Id = statusId,
            Name = "quality",
            ParentId = childId,
            AffectedChannels = ["statusChannel"],
            ValueType = typeof(string),
        },
        new()
        {
            DesignId = OpcUaServerNodeDesignId.Variable,
            Id = writeableChildId,
            Name = "writableChild",
            ParentId = parentId,
            TransferredChannels = ["writableChild"],
            AffectedChannels = ["writableChild"],
            ValueType = typeof(int),
            Properties = new()
            {
                { OpcUaServerDataPortPropertyNames.Maximum, new() { Value = 35 } },
                { OpcUaServerDataPortPropertyNames.Minimum, new() { Value = -1 } },
            },
        },
    ],
};
_ = new OpcUaServerDataPortProperties(communication)
{
    ApplicationName = "OPC UA Server Standalone",
    ApplicationUri = "urn:localhost:OPCUA:DataPort",
    Namespace = "http://localhost:OPCUA/Server",
    Server = "0.0.0.0",
    Port = 4840,
    Endpoint = "ua/dataport",
    UserAuthenticationType = UserAuthenticationType.Basic,
    Password = "admin",
    User = "admin",
    ApplicationCertificateSubject = "CN=OPC UA Server Standalone, DC=localhost",
    ApplicationCertificatesStoreType = CertificateStoreType.X509Store,
    ApplicationCertificatesStorePath = "CurrentUser\\My",
    AutoAcceptUntrustedCertificates = true,
};

using var loggerFactory = LoggerFactory
    .Create(logging => logging.AddConsole());
await using OpcUaServerDataPortIncoming incoming = new(communication, loggerFactory);
await using OpcUaServerDataPortOutgoing outgoing = new(communication, loggerFactory);
using var cancelationTokenSource = new CancellationTokenSource();
var cylce = 0u;

await incoming.ConnectAsync(cancelationTokenSource.Token);
await outgoing.ConnectAsync(cancelationTokenSource.Token);

incoming.Received += (v) =>
{
    foreach (var item in v)
    {
        Console.WriteLine($"{item.Channel}: {item.Value} {item.Timestamp}");
    }
};

await outgoing.SendAsync(cylce++,
    [
        new()
        {
            Channel = "writableChild",
            Timestamp = DateTime.Now,
            Value = 2,
        },
        new()
        {
            Channel = "statusChannel",
            Timestamp = DateTime.Now,
            Value = nameof(StatusCodes.Good),
        },
    ], cancelationTokenSource.Token);

_ = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

    var statuses = new[]
    {
        nameof(StatusCodes.Good),
        nameof(StatusCodes.BadAlreadyExists),
        nameof(StatusCodes.BadArgumentsMissing),
        nameof(StatusCodes.Uncertain),
        nameof(StatusCodes.UncertainLastUsableValue),
        nameof(StatusCodes.Bad),
        nameof(StatusCodes.Good),
        nameof(StatusCodes.Good),
        nameof(StatusCodes.Good),
        nameof(StatusCodes.BadCommunicationError),
        nameof(StatusCodes.BadCommunicationError),
    };

    while (await timer.WaitForNextTickAsync(cancelationTokenSource.Token))
    {
        try
        {
            await outgoing.SendAsync(cylce++,
            [
                new()
                {
                    Channel = "child",
                    Timestamp = DateTime.Now,
                    Value = DateTime.Now.Second,
                },
                new()
                {
                    Channel = "statusChannel",
                    Timestamp = DateTime.Now,
                    Value = statuses[DateTime.Now.Second / 6],
                }
            ], cancelationTokenSource.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }
});

Console.ReadLine();
#pragma warning disable CA1849 // Asynchrone Methoden in einer asynchronen Methode aufrufen
cancelationTokenSource.Cancel();
#pragma warning restore CA1849 // Asynchrone Methoden in einer asynchronen Methode aufrufen
