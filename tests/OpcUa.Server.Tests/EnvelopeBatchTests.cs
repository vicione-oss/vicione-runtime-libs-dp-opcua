using System;
using System.Linq;
using AwesomeAssertions;
using Opc.Ua;
using ViciOne.ManagedEngine.ExternalCommunication;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class EnvelopeBatch_Of
{
    private static readonly DateTime s_timestamp = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
    private static readonly DateTime s_sourceTimestamp = new(2026, 3, 4, 5, 6, 0, DateTimeKind.Utc);

    private static EnvelopeChildren CreateChildren(Type statusCodeValueType)
    {
        var parent = EnvelopeTree.DataPoint("temperature", "value", "status", "sent");
        Node statusCode = new()
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            DesignId = OpcUaServerNodeDesignId.StatusCode,
            Name = "quality",
            ValueType = statusCodeValueType,
            AffectedChannels = ["status"],
        };

        return EnvelopeChildren.Create(
            [parent, statusCode, EnvelopeTree.Child(parent, OpcUaServerNodeDesignId.SourceTimestamp, "when", "sent")],
            [EnvelopeChildKind.StatusCode, EnvelopeChildKind.SourceTimestamp]);
    }

    /// <summary>
    /// A child is never more valid than the value it arrived with, and is reported for the same time.
    /// </summary>
    [Fact]
    public void Reports_each_child_for_the_timestamp_and_with_the_validity_of_its_value()
    {
        ExternalValue value = new() { Channel = "value", Value = 23.5, Timestamp = s_timestamp, Validity = 0, };

        var batch = EnvelopeBatch.Of(value, CreateChildren(typeof(uint)).Of("value"), StatusCodes.BadCommunicationError, s_sourceTimestamp, DateTime.MinValue);

        batch.Should().SatisfyRespectively(
            first => first.Should().BeSameAs(value),
            status =>
            {
                status.Channel.Should().Be("status");
                status.Value.Should().Be(StatusCodes.BadCommunicationError);
                status.Timestamp.Should().Be(s_timestamp);
                status.Validity.Should().Be(0);
            },
            sent =>
            {
                sent.Channel.Should().Be("sent");
                sent.Value.Should().Be(s_sourceTimestamp);
                sent.Timestamp.Should().Be(s_timestamp);
                sent.Validity.Should().Be(0);
            });
    }

    [Fact]
    public void Leaves_out_a_timestamp_the_far_side_did_not_send()
    {
        ExternalValue value = new() { Channel = "value", Value = 23.5, Timestamp = s_timestamp, Validity = 1, };

        var batch = EnvelopeBatch.Of(value, CreateChildren(typeof(uint)).Of("value"), StatusCodes.Good, DateTime.MinValue, DateTime.MinValue);

        batch.Select(entry => entry.Channel).Should().Equal("value", "status");
    }

    [Fact]
    public void Reports_a_status_code_linked_as_a_string_by_its_name()
    {
        ExternalValue value = new() { Channel = "value", Value = 23.5, Timestamp = s_timestamp, Validity = 1, };

        var batch = EnvelopeBatch.Of(value, CreateChildren(typeof(string)).Of("value"), StatusCodes.BadCommunicationError, DateTime.MinValue, DateTime.MinValue);

        batch.Should().ContainSingle(entry => entry.Channel == "status")
            .Which.Value.Should().Be("BadCommunicationError");
    }
}
