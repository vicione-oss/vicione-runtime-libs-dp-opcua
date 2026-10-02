using System;
using AwesomeAssertions;
using Opc.Ua;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaValue_Of
{
    private static readonly DateTime s_sourceTimestamp = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
    private static readonly DateTime s_serverTimestamp = new(2026, 3, 4, 5, 6, 8, DateTimeKind.Utc);
    private static readonly DateTime s_publishTime = new(2026, 3, 4, 5, 6, 9, DateTimeKind.Utc);

    /// <summary>
    /// A source timestamp follows the clock of the device that produced the value, which may be off
    /// by any amount in either direction. It reaches the engine on its own, beside the value.
    /// </summary>
    [Fact]
    public void Reports_a_value_for_its_server_timestamp_rather_than_its_source_timestamp()
    {
        var value = OpcUaValue.Of(new DataValue { Value = 23.5, SourceTimestamp = s_sourceTimestamp, ServerTimestamp = s_serverTimestamp }, s_publishTime);

        value.Timestamp.Should().Be(s_serverTimestamp);
        value.SourceTimestamp.Should().Be(s_sourceTimestamp);
    }

    [Fact]
    public void Reports_a_value_for_its_publish_time_when_the_server_sent_no_server_timestamp()
        => OpcUaValue.Of(new DataValue { Value = 23.5, SourceTimestamp = s_sourceTimestamp }, s_publishTime)
            .Timestamp.Should().Be(s_publishTime);

    /// <summary>
    /// A server is free to set neither timestamp, and a data point reporting year 1 is worse than one
    /// reporting the time its value was delivered. The timestamps the server sent stay unset.
    /// </summary>
    [Fact]
    public void Reports_a_value_for_its_publish_time_when_the_server_sent_no_timestamp()
    {
        var value = OpcUaValue.Of(new DataValue { Value = 23.5 }, s_publishTime);

        value.Timestamp.Should().Be(s_publishTime);
        value.SourceTimestamp.Should().Be(DateTime.MinValue);
        value.ServerTimestamp.Should().Be(DateTime.MinValue);
    }
}
