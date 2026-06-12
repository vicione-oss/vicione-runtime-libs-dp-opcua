using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

internal interface IOpcUaClientInstanceManager
{
    Task<IOpcUaClient> GetOrRegisterOpcUaClientAsync(OpcUaClientDataPortCommunication configuration, object instance, ILogger<IOpcUaClient> logger, CancellationToken cancellationToken);
    Task ReleaseOpcUaClientAsync(OpcUaClientDataPortCommunication communication, object instance, CancellationToken cancellationToken);
}
