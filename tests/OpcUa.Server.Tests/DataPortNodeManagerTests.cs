using System;
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

    [Theory]
    [InlineData(3.4, 2)]
    [InlineData(3, 2L)]
    [InlineData(3.4, 3.2f)]
    [InlineData(3L, 2.5)]
    public void Returns_false_if_bigger_than_a_maximum_of_another_numeric_type(object value, object maximum)
        => DataPortNodeManager.IsInRange(null, new Property { Value = maximum }, value).Should().BeFalse();

    [Theory]
    [InlineData(3.4, 4)]
    [InlineData(3, 4L)]
    [InlineData(3.4, 3.5f)]
    [InlineData(3L, 3.5)]
    public void Returns_false_if_smaller_than_a_minimum_of_another_numeric_type(object value, object minimum)
        => DataPortNodeManager.IsInRange(new Property { Value = minimum }, null, value).Should().BeFalse();

    [Theory]
    [InlineData(3.4, 2, 4L)]
    [InlineData(3, 2.5, 4f)]
    [InlineData(3L, 2f, 4.5)]
    public void Returns_true_if_within_limits_of_another_numeric_type(object value, object minimum, object maximum)
        => DataPortNodeManager.IsInRange(new Property { Value = minimum }, new Property { Value = maximum }, value).Should().BeTrue();

    [Theory]
    [InlineData(3, 3)]
    [InlineData(3.4, 3.4)]
    public void Returns_true_if_equal_to_a_limit(object value, object limit)
        => DataPortNodeManager.IsInRange(new Property { Value = limit }, new Property { Value = limit }, value).Should().BeTrue();

    [Fact]
    public void Returns_true_if_a_date_time_is_within_date_time_limits()
        => DataPortNodeManager.IsInRange(new Property { Value = new DateTime(2024, 1, 1) }, new Property { Value = new DateTime(2026, 1, 1) }, new DateTime(2025, 1, 1)).Should().BeTrue();

    [Fact]
    public void Returns_false_if_a_date_time_is_older_than_a_date_time_minimum()
        => DataPortNodeManager.IsInRange(new Property { Value = new DateTime(2026, 1, 1) }, null, new DateTime(2025, 1, 1)).Should().BeFalse();

    [Fact]
    public void Returns_false_if_a_date_time_is_limited_by_a_number()
        => DataPortNodeManager.IsInRange(null, new Property { Value = 5 }, new DateTime(2025, 1, 1)).Should().BeFalse();

    [Fact]
    public void Returns_false_if_a_number_is_limited_by_text()
        => DataPortNodeManager.IsInRange(null, new Property { Value = "5" }, 3).Should().BeFalse();

    [Fact]
    public void Compares_integers_that_no_longer_fit_a_double_exactly()
        => DataPortNodeManager.IsInRange(null, new Property { Value = 9007199254740992L }, 9007199254740993L).Should().BeFalse();

    [Fact]
    public void Compares_a_decimal_limit_without_rounding_it()
        => DataPortNodeManager.IsInRange(null, new Property { Value = 1m }, 1.0000000000000000000000000001m).Should().BeFalse();

    [Fact]
    public void Returns_false_if_not_a_number_is_checked_against_a_minimum()
        => DataPortNodeManager.IsInRange(new Property { Value = 0 }, null, double.NaN).Should().BeFalse();

    [Fact]
    public void Returns_false_if_a_value_beyond_the_decimal_range_passes_a_maximum()
        => DataPortNodeManager.IsInRange(null, new Property { Value = 1 }, 1e30d).Should().BeFalse();
}
