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

using System.Globalization;
using BinStash.Core.Storage.Gc;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace BinStash.Server.Tests.ChunkStores;

/// <summary>
/// Verifies that the configuration keys the instance-settings mutation writes are the ones
/// <see cref="GarbageCollectionOptions"/> binds from.
///
/// <para>
/// Writer and reader are coupled only by these strings: the mutation sets
/// <c>ChunkStoreGc:Schedule:Enabled</c> and the scheduler reads a bound options object. Nothing
/// fails if those drift — the save succeeds, the UI redisplays the saved value, and the schedule
/// simply never changes. That is the failure this guards, which is why it asserts on the literal
/// keys rather than going through the service.
/// </para>
/// </summary>
public sealed class GcConfigBindingSpecs
{
    private static GarbageCollectionOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var options = new GarbageCollectionOptions();
        configuration.GetSection(GarbageCollectionOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void Schedule_keys_written_by_the_mutation_bind_onto_the_options()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["ChunkStoreGc:Schedule:Enabled"] = "true",
            ["ChunkStoreGc:Schedule:Interval"] = TimeSpan.FromHours(6).ToString("c", CultureInfo.InvariantCulture),
            ["ChunkStoreGc:Schedule:WindowStartHourUtc"] = "22",
            ["ChunkStoreGc:Schedule:WindowEndHourUtc"] = "4",
            ["ChunkStoreGc:Schedule:DryRun"] = "true",
            ["ChunkStoreGc:Schedule:SkipReclaim"] = "false",
            ["ChunkStoreGc:RetentionWindow"] = TimeSpan.FromHours(48).ToString("c", CultureInfo.InvariantCulture),
        });

        options.Schedule.Enabled.Should().BeTrue();
        options.Schedule.Interval.Should().Be(TimeSpan.FromHours(6));
        options.Schedule.WindowStartHourUtc.Should().Be(22);
        options.Schedule.WindowEndHourUtc.Should().Be(4);
        options.Schedule.DryRun.Should().BeTrue();
        options.Schedule.SkipReclaim.Should().BeFalse();
        options.RetentionWindow.Should().Be(TimeSpan.FromHours(48));
    }

    [Fact]
    public void An_interval_of_a_whole_day_round_trips_through_the_stored_format()
    {
        // TimeSpan's "c" format renders 24 hours as "1.00:00:00". The binder has to read that
        // back as a day, not fail or truncate — and 24 hours is the default cadence, so this is
        // the single most likely value to be stored.
        var stored = TimeSpan.FromHours(24).ToString("c", CultureInfo.InvariantCulture);
        stored.Should().Be("1.00:00:00");

        var options = Bind(new Dictionary<string, string?> { ["ChunkStoreGc:Schedule:Interval"] = stored });

        options.Schedule.Interval.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void A_sub_hour_interval_round_trips()
    {
        var stored = TimeSpan.FromHours(0.5).ToString("c", CultureInfo.InvariantCulture);

        Bind(new Dictionary<string, string?> { ["ChunkStoreGc:Schedule:Interval"] = stored })
            .Schedule.Interval.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Absent_configuration_leaves_the_code_defaults_in_place()
    {
        // Nothing about the schedule ships in appsettings.json, precisely so the database-backed
        // provider is free to supply it. These are therefore the values a fresh instance runs on.
        var options = Bind([]);

        options.Schedule.Enabled.Should().BeFalse("collection must be opt-in");
        options.Schedule.Interval.Should().Be(TimeSpan.FromHours(24));
        options.Schedule.WindowStartHourUtc.Should().BeNull();
        options.Schedule.WindowEndHourUtc.Should().BeNull();
        options.RetentionWindow.Should().Be(TimeSpan.FromHours(24));
        options.Invoking(x => x.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Clearing_the_window_leaves_no_bound_hours()
    {
        // The mutation clears a window by writing nulls; the binder must produce "no window"
        // rather than a half-configured one.
        var options = Bind(new Dictionary<string, string?>
        {
            ["ChunkStoreGc:Schedule:Enabled"] = "true",
            ["ChunkStoreGc:Schedule:WindowStartHourUtc"] = null,
            ["ChunkStoreGc:Schedule:WindowEndHourUtc"] = null,
        });

        options.Schedule.WindowStartHourUtc.Should().BeNull();
        options.Schedule.WindowEndHourUtc.Should().BeNull();
        options.Schedule.IsWithinWindow(DateTimeOffset.UtcNow).Should().BeTrue();
    }
}
