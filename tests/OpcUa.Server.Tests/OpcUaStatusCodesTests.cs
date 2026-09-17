using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaStatusCodes_ConvertToStatusCode
{
    [Theory]
    [InlineData("Good", StatusCodes.Good)]
    [InlineData(0u, StatusCodes.Good)]
    [InlineData("0x00000000", StatusCodes.Good)]
    [InlineData("0x800C0000", StatusCodes.BadShutdown)]
    [InlineData("0X800C0000", StatusCodes.BadShutdown)]
    [InlineData("2148270080", StatusCodes.BadShutdown)]
    [InlineData("GoodEdited_DependentValueChanged", StatusCodes.GoodEdited_DependentValueChanged)]
    [InlineData("GoodEditedDependentValueChanged", StatusCodes.GoodEdited_DependentValueChanged)]
    [InlineData(0x01160000u, StatusCodes.GoodEdited_DependentValueChanged)]
    public void Returns_correct_statuscode(object? value, uint expected)
        => OpcUaStatusCodes.ConvertToStatusCode(value).Should().Be(expected);

    /// <summary>
    /// A text that names no status code is served as bad rather than as good, empty text included,
    /// and a malformed or oversized hexadecimal number is reported like any other unknown name.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("good")]
    [InlineData("Gribledib")]
    [InlineData("0x")]
    [InlineData("0xZZ")]
    [InlineData("0x1FFFFFFFF")]
    public void Returns_BadInternalError_and_warns_if_no_status_code_has_that_name(string value)
    {
        FakeLogger<DataPortNodeManager> logger = new();

        var result = OpcUaStatusCodes.ConvertToStatusCode(value, logger);

        result.Should().Be(StatusCodes.BadInternalError);
        logger.LatestRecord.Level.Should().Be(LogLevel.Warning);
        logger.LatestRecord.Message.Should().Match($"No OPC UA status code is named '{value}'*BadInternalError*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    [InlineData(5L)]
    public void Returns_BadInternalError_and_warns_if_status_cannot_be_parsed(object? value)
    {
        FakeLogger<DataPortNodeManager> logger = new();

        var result = OpcUaStatusCodes.ConvertToStatusCode(value, logger);

        result.Should().Be(StatusCodes.BadInternalError);
        logger.LatestRecord.Level.Should().Be(LogLevel.Warning);
        logger.LatestRecord.Message.Should().Match("Cannot read*as an OPC UA status code*BadInternalError*");
    }
}

public class OpcUaStatusCodes_NameOf
{
    /// <summary>
    /// A code with info bits set is named by its code alone, so 0x00000600 (Good at a high limit)
    /// is named Good.
    /// </summary>
    [Theory]
    [InlineData(StatusCodes.BadNotFound, "BadNotFound")]
    [InlineData(StatusCodes.GoodEdited_DependentValueChanged, "GoodEdited_DependentValueChanged")]
    [InlineData(0x00000600u, "Good")]
    [InlineData(0x80FF0000u, "0x80FF0000")]
    public void Returns_the_name_of_a_code_or_its_number_when_it_has_none(uint statusCode, string expected)
        => OpcUaStatusCodes.NameOf(statusCode).Should().Be(expected);
}
