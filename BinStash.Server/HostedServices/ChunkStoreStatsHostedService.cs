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

using BinStash.Infrastructure.Data;
using BinStash.Server.Services.ChunkStores;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.HostedServices;

public sealed class ChunkStoreStatsHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ChunkStoreStatsHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch
            {
                // TODO: inject logger
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
        var collector = scope.ServiceProvider.GetRequiredService<ChunkStoreStatsCollector>();

        var chunkStoreIds = await db.ChunkStores
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var chunkStoreId in chunkStoreIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await collector.CollectAndStoreAsync(chunkStoreId, cancellationToken);
        }
    }
}