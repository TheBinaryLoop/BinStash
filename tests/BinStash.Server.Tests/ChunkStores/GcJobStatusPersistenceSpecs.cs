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

using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Tests.ChunkStores;

/// <summary>
/// Pins the reason every write to a garbage-collection job row goes through
/// <c>BackgroundJobs.Update(job)</c> rather than relying on change tracking.
/// </summary>
/// <remarks>
/// The collector calls <c>ChangeTracker.Clear()</c> during sweep and reclaim so the tracker does
/// not grow with hundreds of thousands of tombstones. That also detaches the job row it loaded at
/// the start, after which mutating it and calling SaveChanges persists nothing — and does so
/// silently, which is what makes it dangerous. A run still broadcast its progress over the
/// subscription, so the UI kept moving while the database row stopped, and the terminal status
/// never landed: the job stayed Running for ever, was re-queued by the startup resume on every
/// restart, and blocked every later collection through the "is this store busy" guard.
///
/// <para>
/// These tests assert the EF behaviour itself, because that behaviour is the trap. If a future
/// change removes the re-attach as redundant, the first test here is the explanation of why it
/// is not.
/// </para>
/// </remarks>
public sealed class GcJobStatusPersistenceSpecs : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly BinStashDbContext _db;

    public GcJobStatusPersistenceSpecs()
    {
        var options = new DbContextOptionsBuilder<BinStashDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BinStashDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Guid> SeedRunningJobAsync()
    {
        var job = new BackgroundJob
        {
            Id = Guid.NewGuid(),
            JobType = BackgroundJobTypes.ChunkStoreGc,
            Status = BackgroundJobStatus.Running,
            JobData = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.BackgroundJobs.Add(job);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        return job.Id;
    }

    [Fact]
    public async Task Clearing_the_tracker_silently_discards_a_later_write_to_a_tracked_job()
    {
        var jobId = await SeedRunningJobAsync();

        var job = await _db.BackgroundJobs.FirstAsync(j => j.Id == jobId, Ct);

        // What the sweep and reclaim phases do between loading the job and completing it.
        _db.ChangeTracker.Clear();

        job.Status = BackgroundJobStatus.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(Ct);

        _db.ChangeTracker.Clear();
        var reloaded = await _db.BackgroundJobs.AsNoTracking().FirstAsync(j => j.Id == jobId, Ct);

        reloaded.Status.Should().Be(
            BackgroundJobStatus.Running,
            "a detached entity is not saved, and nothing about the call says so");
    }

    [Fact]
    public async Task Re_attaching_the_job_makes_the_terminal_status_persist()
    {
        var jobId = await SeedRunningJobAsync();

        var job = await _db.BackgroundJobs.FirstAsync(j => j.Id == jobId, Ct);

        _db.ChangeTracker.Clear();

        job.Status = BackgroundJobStatus.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        _db.BackgroundJobs.Update(job);
        await _db.SaveChangesAsync(Ct);

        _db.ChangeTracker.Clear();
        var reloaded = await _db.BackgroundJobs.AsNoTracking().FirstAsync(j => j.Id == jobId, Ct);

        reloaded.Status.Should().Be(BackgroundJobStatus.Completed);
        reloaded.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_job_left_running_is_what_the_startup_resume_picks_up()
    {
        // The consequence the re-attach exists to prevent: the resume query matches on status
        // alone, so a completed run whose status never persisted is indistinguishable from one
        // that was interrupted, and is re-queued on every restart.
        var jobId = await SeedRunningJobAsync();

        var resumable = await _db.BackgroundJobs
            .Where(j => j.JobType == BackgroundJobTypes.ChunkStoreGc
                        && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.Running))
            .Select(j => j.Id)
            .ToListAsync(Ct);

        resumable.Should().Contain(jobId);
    }
}
