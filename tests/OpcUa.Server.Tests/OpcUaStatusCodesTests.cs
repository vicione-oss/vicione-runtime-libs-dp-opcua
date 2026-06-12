using AwesomeAssertions;
using Microsoft.Extensions.Logging.Testing;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaStatusCodes_GetOpcStatus
{
    [Theory]
    [InlineData("", StatusCodes.Good)]
    [InlineData("Good", StatusCodes.Good)]
    [InlineData(0u, StatusCodes.Good)]
    [InlineData("0x00000000", StatusCodes.Good)]
    [InlineData("0x800C0000", StatusCodes.BadShutdown)]
    [InlineData("GoodEdited_DependentValueChanged", StatusCodes.GoodEdited_DependentValueChanged)]
    [InlineData("GoodEditedDependentValueChanged", StatusCodes.GoodEdited_DependentValueChanged)]
    [InlineData(0x01160000u, StatusCodes.GoodEdited_DependentValueChanged)]
    public void Returns_correct_statuscode(object? value, uint expected)
        => OpcUaStatusCodes.ConvertToStatusCode(value).Should().Be(expected);

    [Fact]
    public void Returns_BadInternalError_if_status_string_does_not_exist()
    {
        FakeLogger<DataPortNodeManager> logger = new();

        var result = OpcUaStatusCodes.ConvertToStatusCode("Gribledib", logger);

        result.Should().Be(StatusCodes.BadInternalError);
        logger.LatestRecord.Message.Should().Match("*not*Gribledib*BadInternalError*");
    }

    [Fact]
    public void Returns_BadInternalError_if_status_cannot_be_parsed()
    {
        FakeLogger<DataPortNodeManager> logger = new();

        var result = OpcUaStatusCodes.ConvertToStatusCode(new object(), logger);

        result.Should().Be(StatusCodes.BadInternalError);
        logger.LatestRecord.Message.Should().Match($"Unable*{new object()}*BadInternalError*");
    }
}
