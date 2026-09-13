// Copyright (C) 2025-2026  Lukas Eßmann
// 
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
// 
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see <https://www.gnu.org/licenses/>.

using BinStash.Core.Storage.Gc;
using FluentAssertions;

namespace BinStash.Core.Tests.Storage;

/// <summary>
/// Tests for the decision the garbage-collection scheduler makes on every tick.
///
/// <para>
/// This is the whole of the schedule's logic, deliberately kept as two pure functions so it can
/// be tested without a clock, a database or a host. The failure modes it guards are asymmetric:
/// collecting too eagerly costs I/O at the wrong hour, while never collecting is silent and only
/// shows up as a disk that stopped shrinking.
/// </para>
/// </summary>
public sealed class GcScheduleOptionsTests
{
    private static DateTimeOffset At(int hourUtc, int day = 1)
        => new(2026, 9, day, hourUtc, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Disabled_schedule_is_never_due()
    {
        var schedule = new GcScheduleOptions { Enabled = false, Interval = TimeSpan.FromHours(1) };

        schedule.IsDue(At(12), lastRunStartedAt: null).Should().BeFalse();
    }

    [Fact]
    public void Store_that_has_never_been_collected_is_due_immediately()
    {
        var schedule = new GcScheduleOptions { Enabled = true, Interval = TimeSpan.FromHours(24) };

        schedule.IsDue(At(12), lastRunStartedAt: null).Should().BeTrue();
    }

    [Fact]
    public void Store_collected_inside_the_interval_is_not_due_again()
    {
        var schedule = new GcScheduleOptions { Enabled = true, Interval = TimeSpan.FromHours(24) };

        schedule.IsDue(At(12, day: 2), lastRunStartedAt: At(18, day: 1)).Should().BeFalse();
    }

    [Fact]
    public void Interval_is_measured_from_the_previous_run_start()
    {
        var schedule = new GcScheduleOptions { Enabled = true, Interval = TimeSpan.FromHours(24) };

        // Exactly one interval later: due, rather than due only after it is exceeded. A store
        // collected at 02:00 must be collectable again at 02:00, not drift an hour every day.
        schedule.IsDue(At(2, day: 2), lastRunStartedAt: At(2, day: 1)).Should().BeTrue();
    }

    [Fact]
    public void No_window_means_any_hour_is_acceptable()
    {
        var schedule = new GcScheduleOptions { Enabled = true };

        for (var hour = 0; hour < 24; hour++)
            schedule.IsWithinWindow(At(hour)).Should().BeTrue($"hour {hour} should be inside an unset window");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(8, false)]
    [InlineData(9, true)]
    [InlineData(16, true)]
    [InlineData(17, false)]
    [InlineData(23, false)]
    public void Same_day_window_admits_only_its_own_hours(int hour, bool expected)
    {
        var schedule = new GcScheduleOptions
        {
            Enabled = true,
            WindowStartHourUtc = 9,
            WindowEndHourUtc = 17
        };

        schedule.IsWithinWindow(At(hour)).Should().Be(expected);
    }

    [Theory]
    [InlineData(21, false)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(12, false)]
    public void Window_crossing_midnight_wraps_instead_of_closing(int hour, bool expected)
    {
        // The overnight case, and the one a naive start <= hour < end check gets exactly backwards.
        var schedule = new GcScheduleOptions
        {
            Enabled = true,
            WindowStartHourUtc = 22,
            WindowEndHourUtc = 4
        };

        schedule.IsWithinWindow(At(hour)).Should().Be(expected);
    }

    [Fact]
    public void Half_configured_window_is_treated_as_no_window()
    {
        // Failing to run is the silent failure, so a half-filled form must not produce a window
        // that never opens.
        var startOnly = new GcScheduleOptions { Enabled = true, WindowStartHourUtc = 22 };
        var endOnly = new GcScheduleOptions { Enabled = true, WindowEndHourUtc = 4 };

        startOnly.IsWithinWindow(At(12)).Should().BeTrue();
        endOnly.IsWithinWindow(At(12)).Should().BeTrue();
    }

    [Fact]
    public void Window_whose_ends_are_equal_is_treated_as_no_window()
    {
        var schedule = new GcScheduleOptions
        {
            Enabled = true,
            WindowStartHourUtc = 3,
            WindowEndHourUtc = 3
        };

        schedule.IsWithinWindow(At(15)).Should().BeTrue();
    }

    [Fact]
    public void Due_store_outside_the_window_waits()
    {
        var schedule = new GcScheduleOptions
        {
            Enabled = true,
            Interval = TimeSpan.FromHours(24),
            WindowStartHourUtc = 22,
            WindowEndHourUtc = 4
        };

        schedule.IsDue(At(12, day: 3), lastRunStartedAt: At(23, day: 1)).Should().BeFalse();
        schedule.IsDue(At(23, day: 3), lastRunStartedAt: At(23, day: 1)).Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_a_non_positive_interval_only_when_enabled()
    {
        var enabled = new GcScheduleOptions { Enabled = true, Interval = TimeSpan.Zero };
        var disabled = new GcScheduleOptions { Enabled = false, Interval = TimeSpan.Zero };

        enabled.Invoking(x => x.Validate()).Should().Throw<ArgumentOutOfRangeException>();
        disabled.Invoking(x => x.Validate()).Should().NotThrow();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Validate_rejects_hours_outside_a_day(int hour)
    {
        new GcScheduleOptions { WindowStartHourUtc = hour }
            .Invoking(x => x.Validate()).Should().Throw<ArgumentOutOfRangeException>();

        new GcScheduleOptions { WindowEndHourUtc = hour }
            .Invoking(x => x.Validate()).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Options_validation_reaches_the_schedule()
    {
        // The schedule is nested, so it is only as validated as the parent remembers to be.
        var options = new GarbageCollectionOptions
        {
            Schedule = new GcScheduleOptions { Enabled = true, Interval = TimeSpan.FromMinutes(-1) }
        };

        options.Invoking(x => x.Validate()).Should().Throw<ArgumentOutOfRangeException>();
    }
}
