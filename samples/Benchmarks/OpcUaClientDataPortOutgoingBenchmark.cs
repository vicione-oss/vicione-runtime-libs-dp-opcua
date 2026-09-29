using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using ViciOne.ManagedEngine.ExternalCommunication;
using ViciOne.Suite.DataPort;

namespace Benchmarks;

[SuppressMessage("Maintainability", "CA1515:Erwägen Sie, öffentliche Typen intern zu machen.", Justification = "Must be public for BenchmarkDotNet")]
public class OpcUaClientDataPortOutgoingBenchmark : IDisposable
{
    private OpcUaClientDataPortOutgoing? _communication;
    private readonly List<ExternalValue> _data = [];
    private bool _disposedValue;
    private readonly LoggerFactory _loggerFactory = new();

    public int DataSize { get; private set; } = 25000;

    [GlobalSetup]
    public async Task Setup()
    {
        List<Node> nodes = [];

        var ifmNode = new Node()
        {
            Name = "ifm",
            DesignId = OpcUaClientNodeDesignId.Folder,
            Id = Guid.NewGuid(),
        };

        nodes.Add(ifmNode);

        var vseNode = new Node()
        {
            Name = "VSE",
            DesignId = OpcUaClientNodeDesignId.Folder,
            Id = Guid.NewGuid(),
            ParentId = ifmNode.Id,
        };

        nodes.Add(vseNode);

        var vseIpNode = new Node()
        {
            Name = "__VSE_IP_NODE__",
            DesignId = OpcUaClientNodeDesignId.Folder,
            Id = Guid.NewGuid(),
            ParentId = vseNode.Id,
        };

        nodes.Add(vseIpNode);

        var inputsNode = new Node()
        {
            Name = "Inputs",
            DesignId = OpcUaClientNodeDesignId.Folder,
            Id = Guid.NewGuid(),
            ParentId = vseIpNode.Id,
        };

        nodes.Add(inputsNode);

        var externalNode = new Node()
        {
            Name = "External",
            DesignId = OpcUaClientNodeDesignId.Folder,
            Id = Guid.NewGuid(),
            ParentId = inputsNode.Id,
        };

        nodes.Add(externalNode);

        var input01Node = new Node()
        {
            Name = "Input01",
            DesignId = OpcUaClientNodeDesignId.Folder,
            Id = Guid.NewGuid(),
            ParentId = externalNode.Id,
        };

        nodes.Add(input01Node);

        var valueNode = new Node()
        {
            Name = "Value",
            DesignId = OpcUaClientNodeDesignId.Variable,
            Id = Guid.NewGuid(),
            ParentId = input01Node.Id,
            ValueType = typeof(double),
        };

        nodes.Add(valueNode);

        valueNode.AffectedChannels.Add("affectedChannel");
        valueNode.TransferredChannels.Add("transferredChannel");

        for (var i = 0; i < DataSize; i++)
        {
            _data.Add(new ExternalValue
            {
                Channel = "affectedChannel",
                Value = i * 0.001d,
            });
        }

        OpcUaClientDataPortCommunication communication = new()
        {
            Nodes = nodes,
        };
        _ = new OpcUaClientDataPortProperties(communication)
        {
            Endpoint = "__MOEMBED_UA_SERVER__",
            Server = "127.0.0.1:48401",
            UserAuthenticationType = UserAuthenticationType.Basic,
            Password = "admin",
            User = "admin",
            ApplicationName = "Benchmark",
            ApplicationCertificatesStoreType = Opc.Ua.CertificateStoreType.Directory,
            ApplicationCertificatesStorePath = Path.Combine("%CommonApplicationData%", "opcua", "cert-stores"),
        };
        _communication = new OpcUaClientDataPortOutgoing(communication, _loggerFactory);

        await _communication.ConnectAsync(CancellationToken.None);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_communication is not null)
            await _communication.DisconnectAsync(CancellationToken.None);
        _communication = null;
        _data.Clear();
    }

    [Benchmark]
    [SuppressMessage("Naming", "CA1707:Bezeichner dürfen keine Unterstriche enthalten")]
    public async Task OpcUaClient_External_Outgoing_Async()
    {
        if (_communication == null)
        {
            return;
        }

        await _communication.SendAsync(0, _data, CancellationToken.None);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
                _loggerFactory.Dispose();
            _disposedValue = true;
        }
    }
}
