using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaClientConnectionTester_TestConnectionAsync
{
    [Collection(OpcUaTestEnvironment.Name)]
    public class Interoperability(OpcUaTestSystem opcUa)
    {
        [Fact]
        public Task Can_connect_to_server() => OpcUaClientConnectionTester.TestConnectionAsync(opcUa.Communication);
    }

    [Fact]
    public async Task Throws_if_test_connection_fails()
    {
        OpcUaClientDataPortCommunication communication = new();

        var act = FluentActions.Awaiting(() => OpcUaClientConnectionTester.TestConnectionAsync(communication));

        await act.Should().ThrowAsync<DataPortConnectionFailedException>().WithMessage("*fail*connect*opc ua*");
    }
}
