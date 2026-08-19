using Opc.Ua;
using Opc.Ua.Server;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// The server <see cref="OpcUaTestSystem"/> starts. It only differs from the stock server when an address space switch
/// is on, so the shared fixture keeps serving exactly what it served before these switches existed.
/// </summary>
internal sealed class OpcUaTestServer(OpcUaTestSystemOptions options) : StandardServer
{
    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration) => options.HasTestAddressSpace
        ? new MasterNodeManager(server, configuration, null, new OpcUaTestNodeManager(server, options))
        : base.CreateMasterNodeManager(server, configuration);
}
