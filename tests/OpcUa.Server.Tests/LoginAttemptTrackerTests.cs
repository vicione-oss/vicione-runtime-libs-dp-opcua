using System;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class LoginAttemptTracker_IsLockedOut
{
    [Fact]
    public void Returns_false_when_no_failed_attempts()
    {
        LoginAttemptTracker tracker = new();

        tracker.IsLockedOut("user").Should().BeFalse();
    }

    [Fact]
    public void Returns_false_when_below_max_failed_attempts()
    {
        LoginAttemptTracker tracker = new();

        for (var i = 0; i < 4; i++)
            tracker.RecordFailure("user");

        tracker.IsLockedOut("user").Should().BeFalse();
    }

    [Fact]
    public void Returns_true_after_max_failed_attempts()
    {
        LoginAttemptTracker tracker = new();

        for (var i = 0; i < 5; i++)
            tracker.RecordFailure("user");

        tracker.IsLockedOut("user").Should().BeTrue();
    }

    [Fact]
    public void Returns_false_after_lockout_expiry()
    {
        FakeTimeProvider timeProvider = new();
        LoginAttemptTracker tracker = new(timeProvider: timeProvider);

        for (var i = 0; i < 5; i++)
            tracker.RecordFailure("user");

        tracker.IsLockedOut("user").Should().BeTrue();

        timeProvider.Advance(TimeSpan.FromSeconds(1801));

        tracker.IsLockedOut("user").Should().BeFalse();
    }

    [Fact]
    public void Does_not_affect_other_users()
    {
        LoginAttemptTracker tracker = new();

        for (var i = 0; i < 5; i++)
            tracker.RecordFailure("attacker");

        tracker.IsLockedOut("attacker").Should().BeTrue();
        tracker.IsLockedOut("legitimate-user").Should().BeFalse();
    }
}

public class LoginAttemptTracker_ResetFailures
{
    [Fact]
    public void Successful_login_resets_the_counter()
    {
        LoginAttemptTracker tracker = new();

        for (var i = 0; i < 4; i++)
            tracker.RecordFailure("user");

        tracker.ResetFailures("user");

        for (var i = 0; i < 4; i++)
            tracker.RecordFailure("user");

        tracker.IsLockedOut("user").Should().BeFalse();
    }

    [Fact]
    public void Reset_on_unknown_user_does_not_throw()
    {
        LoginAttemptTracker tracker = new();

        var act = () => tracker.ResetFailures("unknown");

        act.Should().NotThrow();
    }
}

public class LoginAttemptTracker_GetLockoutEnd
{
    [Fact]
    public void Returns_null_when_no_failures()
    {
        LoginAttemptTracker tracker = new();

        tracker.GetLockoutEnd("user").Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_below_max_failed_attempts()
    {
        LoginAttemptTracker tracker = new();

        for (var i = 0; i < 4; i++)
            tracker.RecordFailure("user");

        tracker.GetLockoutEnd("user").Should().BeNull();
    }

    [Fact]
    public void Returns_last_attempt_plus_lockout_duration_after_max_failures()
    {
        FakeTimeProvider timeProvider = new();
        LoginAttemptTracker tracker = new(timeProvider: timeProvider);

        for (var i = 0; i < 5; i++)
            tracker.RecordFailure("user");

        var expected = timeProvider.GetUtcNow() + TimeSpan.FromSeconds(1800);

        tracker.GetLockoutEnd("user").Should().Be(expected);
    }
}

public class LoginAttemptTracker_GetRemainingDelay
{
    [Fact]
    public void Returns_zero_when_no_failures()
    {
        LoginAttemptTracker tracker = new();

        tracker.GetRemainingDelay("user").Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Returns_full_delay_immediately_after_failure()
    {
        FakeTimeProvider timeProvider = new();
        LoginAttemptTracker tracker = new(timeProvider: timeProvider);

        tracker.RecordFailure("user");
        tracker.GetRemainingDelay("user").Should().Be(TimeSpan.FromMilliseconds(200)); // 2^1 * 100

        tracker.RecordFailure("user");
        tracker.GetRemainingDelay("user").Should().Be(TimeSpan.FromMilliseconds(400)); // 2^2 * 100

        tracker.RecordFailure("user");
        tracker.GetRemainingDelay("user").Should().Be(TimeSpan.FromMilliseconds(800)); // 2^3 * 100
    }

    [Fact]
    public void Returns_zero_after_delay_has_elapsed()
    {
        FakeTimeProvider timeProvider = new();
        LoginAttemptTracker tracker = new(timeProvider: timeProvider);

        tracker.RecordFailure("user"); // delay = 200 ms

        timeProvider.Advance(TimeSpan.FromMilliseconds(200));

        tracker.GetRemainingDelay("user").Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Returns_remaining_portion_of_delay()
    {
        FakeTimeProvider timeProvider = new();
        LoginAttemptTracker tracker = new(timeProvider: timeProvider);

        tracker.RecordFailure("user"); // delay = 200 ms

        timeProvider.Advance(TimeSpan.FromMilliseconds(50));

        tracker.GetRemainingDelay("user").Should().Be(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void Delay_is_capped_at_30_seconds()
    {
        FakeTimeProvider timeProvider = new();
        LoginAttemptTracker tracker = new(timeProvider: timeProvider);

        for (var i = 0; i < 20; i++)
            tracker.RecordFailure("user");

        tracker.GetRemainingDelay("user").Should().Be(TimeSpan.FromSeconds(30));
    }
}

public class LoginAttemptTracker_Eviction
{
    [Fact]
    public void Evicts_expired_entries_when_capacity_is_exceeded()
    {
        FakeTimeProvider timeProvider = new();
        LoginAttemptTracker tracker = new(timeProvider: timeProvider);

        // Fill tracker with 10_000 users that will be expired
        for (var i = 0; i < 10_000; i++)
            tracker.RecordFailure($"old-user-{i}");

        // Advance past lockout duration so all entries are expired
        timeProvider.Advance(TimeSpan.FromSeconds(1801));

        // Adding one more user should trigger eviction of all expired entries
        tracker.RecordFailure("new-user");

        tracker.GetFailCount("new-user").Should().Be(1);
        tracker.GetFailCount("old-user-0").Should().Be(0);
    }

    [Fact]
    public void Evicts_oldest_entries_when_all_are_still_active()
    {
        FakeTimeProvider timeProvider = new();
        LoginAttemptTracker tracker = new(timeProvider: timeProvider);

        // Fill tracker to capacity, advancing time slightly between entries
        // so they have distinct timestamps
        for (var i = 0; i < 10_000; i++)
        {
            tracker.RecordFailure($"user-{i}");
            timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        }

        // Adding one more exceeds the limit; oldest entries should be evicted
        tracker.RecordFailure("newest-user");

        tracker.GetFailCount("newest-user").Should().Be(1);
        // The very first user should have been evicted as the oldest
        tracker.GetFailCount("user-0").Should().Be(0);
        // A recent user should still be tracked
        tracker.GetFailCount("user-9999").Should().Be(1);
    }

    [Fact]
    public void Does_not_evict_entries_when_below_capacity()
    {
        LoginAttemptTracker tracker = new();

        for (var i = 0; i < 100; i++)
            tracker.RecordFailure($"user-{i}");

        // All entries should still be present
        for (var i = 0; i < 100; i++)
            tracker.GetFailCount($"user-{i}").Should().Be(1);
    }
}
