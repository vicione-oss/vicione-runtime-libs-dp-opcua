using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

internal interface IOpcUaServerInstanceManager
{
    IOpcUaServer GetOrRegisterOpcUaServer(OpcUaServerDataPortCommunication configuration, object instance, ILogger<IOpcUaServer> logger);
    Task ReleaseOpcUaServerAsync(OpcUaServerDataPortCommunication configuration, object instance, CancellationToken cancellationToken);
    Task StartOpcUaServer(OpcUaServerDataPortCommunication communication, object instance, CancellationToken cancellationToken);
    Task StopOpcUaServer(OpcUaServerDataPortCommunication communication, object instance, CancellationToken cancellationToken);
}
