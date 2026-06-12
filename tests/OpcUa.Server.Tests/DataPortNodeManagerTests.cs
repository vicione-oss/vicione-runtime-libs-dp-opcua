using AwesomeAssertions;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class DataPortNodeManager_IsInRange
{
    [Theory]
    [InlineData("value")]
    [InlineData(3)]
    [InlineData(3L)]
    [InlineData(3.4)]
    [InlineData(3.4f)]
    public void Returns_true_if_no_range_is_set(object value)
        => DataPortNodeManager.IsInRange(null, null, value).Should().BeTrue();

    [Theory]
    [InlineData(3, 2)]
    [InlineData(3L, 2L)]
    [InlineData(3.4, 3.2)]
    [InlineData(3.4f, 3.2f)]
    public void Returns_false_if_bigger_than_maximum(object value, object maximum)
        => DataPortNodeManager.IsInRange(null, new Property { Value = maximum }, value).Should().BeFalse();

    [Theory]
    [InlineData(3, 4)]
    [InlineData(3L, 4L)]
    [InlineData(3.4, 3.5)]
    [InlineData(3.4f, 3.5f)]
    public void Returns_false_if_smaller_than_minimum(object value, object minimum) => DataPortNodeManager.IsInRange(new Property { Value = minimum }, null, value).Should().BeFalse();

    [Theory]
    [InlineData(3, 2, 4)]
    [InlineData(3L, 2L, 4L)]
    [InlineData(3.4, 3.3, 3.5)]
    [InlineData(3.4f, 3.3f, 3.5f)]
    public void Returns_true_if_within_limits(object value, object minimum, object maximum)
        => DataPortNodeManager.IsInRange(new Property { Value = minimum }, new Property { Value = maximum }, value).Should().BeTrue();

    [Fact]
    public void Does_not_compare_string_length()
        => DataPortNodeManager.IsInRange(new Property { Value = "short string" }, null, "very long string").Should().BeTrue();

    [Fact]
    public void Does_not_compare_empty_string()
        => DataPortNodeManager.IsInRange(null, new Property { Value = string.Empty }, "any string").Should().BeTrue();

    [Fact]
    public void Does_not_compare_strings()
        => DataPortNodeManager.IsInRange(null, new Property { Value = "3" }, "4").Should().BeTrue();
}
