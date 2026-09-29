using System;
using System.Threading.Tasks;

namespace ViciOne.Suite.DataPort;

public sealed class OpcUaClientConnectionTester : IDataPortConnectionTester<OpcUaClientDataPortCommunication>
{
    public static async Task TestConnectionAsync(OpcUaClientDataPortCommunication communication)
    {
        try
        {
            await using var opcUaClient = new OpcUaClient(communication, null);

            await opcUaClient.ConnectAsync(default).ConfigureAwait(false);
            await opcUaClient.DisconnectAsync(default).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new DataPortConnectionFailedException("Failed to connect to OPC UA.", ex);
        }
    }
}
