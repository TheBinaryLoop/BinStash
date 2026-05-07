// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Diagnostics;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Helpers;
using Microsoft.EntityFrameworkCore;
using Path = System.IO.Path;

namespace BinStash.Server.HostedServices;

public sealed class ChunkStoreProbeService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ChunkStoreProbeCache _cache;
    private readonly ILogger<ChunkStoreProbeService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(15);
    private readonly int _maxConcurrency = 4;

    public ChunkStoreProbeService(IServiceProvider services, ChunkStoreProbeCache cache, ILogger<ChunkStoreProbeService> logger)
    {
        _services = services;
        _cache = cache;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var semaphore = new SemaphoreSlim(_maxConcurrency);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<ChunkStore> stores;

                try
                {
                    using var scope = _services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
                    stores = await db.ChunkStores.AsNoTracking().ToListAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to query chunk stores");

                    try
                    {
                        await Task.Delay(_interval, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    continue;
                }

                var tasks = stores.Select(async s =>
                {
                    await semaphore.WaitAsync(stoppingToken);
                    try
                    {
                        return await ProbeChunkStoreAsync(s, stoppingToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToArray();

                ChunkStoreProbeResult[] results;
                try
                {
                    results = await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _cache.Update(results);

                try
                {
                    await Task.Delay(_interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    private static async Task<ChunkStoreProbeResult> ProbeChunkStoreAsync(ChunkStore store, CancellationToken cancellationToken)
    {
        var ts = DateTimeOffset.UtcNow;

        // Only local-folder stores can be probed via filesystem; other types should implement their own probe logic
        if (store.BackendSettings is not LocalFolderBackendSettings localSettings)
        {
            return new(store.Id, store.Name, $"[{store.Type}]", "Healthy", "Non-local store probing not yet implemented", 0, null, 0, 0, ts);
        }

        var root = localSettings.Path;

        try
        {
            if (!Directory.Exists(root))
                return new(store.Id, store.Name, root, "Unhealthy", "RootPath does not exist", 0, null, 0, 0, ts);

            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            var free = drive.AvailableFreeSpace;
            var total = drive.TotalSize;

            if (store.MinFreeBytes is { } min && free < min)
                return new(store.Id, store.Name, root, "Degraded", $"Low disk space: {free} < {min}", free, total, 0, 0, ts);

            if (store.ProbeMode == ProbeMode.ReadOnly)
                return new(store.Id, store.Name, root, "Healthy", null, free, total, 0, 0, ts);

            var healthDir = Path.Combine(root, ".health");
            Directory.CreateDirectory(healthDir);

            var file = Path.Combine(healthDir, $"probe-{store.Id}-{Guid.NewGuid():N}.bin");
            var data = new byte[16 * 1024];
            Random.Shared.NextBytes(data);

            var sw = Stopwatch.StartNew();
            await using (var fs = new FileStream(
                             file,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await fs.WriteAsync(data, cancellationToken);
                await fs.FlushAsync(cancellationToken);
            }
            sw.Stop();
            var writeMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var readBack = await File.ReadAllBytesAsync(file, cancellationToken);
            sw.Stop();
            var readMs = sw.Elapsed.TotalMilliseconds;

            File.Delete(file);

            if (!readBack.AsSpan().SequenceEqual(data))
                return new(store.Id, store.Name, root, "Unhealthy", "Readback mismatch", free, total, writeMs, readMs, ts);

            return new(store.Id, store.Name, root, "Healthy", null, free, total, writeMs, readMs, ts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            return new(store.Id, store.Name, root, "Unhealthy", ex.Message, 0, null, 0, 0, ts);
        }
        catch (IOException ex)
        {
            return new(store.Id, store.Name, root, "Unhealthy", ex.Message, 0, null, 0, 0, ts);
        }
        catch (Exception ex)
        {
            return new(store.Id, store.Name, root, "Unhealthy", ex.ToString(), 0, null, 0, 0, ts);
        }
    }
}
