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

using System.Security.Cryptography;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.HostedServices;

public sealed class SetupBootstrapper(IServiceProvider sp, ILogger<SetupBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<SetupCode>>();

        var state = await db.SetupStates.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (state is null)
        {
            state = new SetupState
            {
                Id = 1,
                IsInitialized = false,
                StartedAt = DateTimeOffset.UtcNow
            };
            db.SetupStates.Add(state);
            await db.SaveChangesAsync(ct);
        }

        if (state.IsInitialized)
            return;

        var code = await db.SetupCodes.SingleOrDefaultAsync(x => x.Id == 1, ct);
        var now = DateTimeOffset.UtcNow;

        var needsNew =
            code is null ||
            code.ExpiresAt <= now;

        if (!needsNew)
            return;

        var plain = GenerateCode();

        if (code is null)
        {
            code = new SetupCode
            {
                Id = 1
            };
            db.SetupCodes.Add(code);
        }

        code.CreatedAt = now;
        code.ExpiresAt = now.AddHours(8);
        code.AttemptCount = 0;
        code.LockedUntil = null;
        code.ConsumedAt = null;
        code.CodeHash = hasher.HashPassword(code, plain);

        await db.SaveChangesAsync(ct);

        logger.LogWarning("SETUP CODE: {Code} (expires {ExpiresAt:u})", plain, code.ExpiresAt);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    
    private static string GenerateCode()
    {
        // 12 chars base32-ish with dashes: XXXX-XXXX-XXXX-XXXX
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> buf = stackalloc char[19]; // 16 chars + 3 dashes
        Span<byte> rnd = stackalloc byte[16];
        RandomNumberGenerator.Fill(rnd);

        var j = 0;
        for (var i = 0; i < 16; i++)
        {
            if (i is 4 or 8 or 12) buf[j++] = '-';
            buf[j++] = alphabet[rnd[i] % alphabet.Length];
        }
        return new string(buf[..j]);
    }
}