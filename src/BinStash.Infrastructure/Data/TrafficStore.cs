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
using BinStash.Core.Traffic;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BinStash.Infrastructure.Data;

/// <summary>
/// Persists accumulated traffic into <c>TenantTrafficSamples</c> and keeps the series bounded.
/// </summary>
/// <remarks>
/// Written as raw SQL rather than through the change tracker because every operation here is a set
/// operation the database can do in one statement — and one of them, the accumulate, has to be an
/// atomic read-add-write. Loading a row, adding to it and saving would drop whichever replica lost
/// the race; <c>ON CONFLICT DO UPDATE</c> with a self-referencing increment cannot.
/// </remarks>
public sealed class TrafficStore(BinStashDbContext db) : ITrafficStore
{
    private const string AccumulateSql = """
        INSERT INTO "TenantTrafficSamples" ("TenantId", "BucketStartUtc", "Grain", "IngressBytes", "EgressBytes", "RequestCount")
        SELECT t.tenant_id, t.bucket, @hourly, t.ingress, t.egress, t.count
        FROM UNNEST(@tenantIds, @buckets, @ingress, @egress, @counts)
            AS t(tenant_id, bucket, ingress, egress, count)
        ON CONFLICT ("TenantId", "BucketStartUtc", "Grain") DO UPDATE SET
            "IngressBytes" = "TenantTrafficSamples"."IngressBytes" + EXCLUDED."IngressBytes",
            "EgressBytes"  = "TenantTrafficSamples"."EgressBytes"  + EXCLUDED."EgressBytes",
            "RequestCount" = "TenantTrafficSamples"."RequestCount" + EXCLUDED."RequestCount"
        """;

    private const string RollUpSql = """
        INSERT INTO "TenantTrafficSamples" ("TenantId", "BucketStartUtc", "Grain", "IngressBytes", "EgressBytes", "RequestCount")
        SELECT "TenantId",
               date_trunc('day', "BucketStartUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
               @daily,
               SUM("IngressBytes"),
               SUM("EgressBytes"),
               SUM("RequestCount")
        FROM "TenantTrafficSamples"
        WHERE "Grain" = @hourly AND "BucketStartUtc" < @cutoff
        GROUP BY "TenantId", date_trunc('day', "BucketStartUtc" AT TIME ZONE 'UTC')
        ON CONFLICT ("TenantId", "BucketStartUtc", "Grain") DO UPDATE SET
            "IngressBytes" = "TenantTrafficSamples"."IngressBytes" + EXCLUDED."IngressBytes",
            "EgressBytes"  = "TenantTrafficSamples"."EgressBytes"  + EXCLUDED."EgressBytes",
            "RequestCount" = "TenantTrafficSamples"."RequestCount" + EXCLUDED."RequestCount"
        """;

    private const string DropFoldedSql = """DELETE FROM "TenantTrafficSamples" WHERE "Grain" = @hourly AND "BucketStartUtc" < @cutoff""";

    private const string PruneSql = """DELETE FROM "TenantTrafficSamples" WHERE "Grain" = @daily AND "BucketStartUtc" < @cutoff""";

    public async Task AccumulateAsync(IReadOnlyCollection<TrafficDelta> deltas, CancellationToken ct = default)
    {
        if (deltas.Count == 0)
            return;

        // Zipped arrays rather than a statement per delta: a busy instance flushes one bucket per
        // active tenant per interval, and this keeps that a single round trip.
        var tenantIds = new Guid[deltas.Count];
        var buckets = new DateTime[deltas.Count];
        var ingress = new long[deltas.Count];
        var egress = new long[deltas.Count];
        var counts = new long[deltas.Count];

        var i = 0;
        foreach (var delta in deltas)
        {
            tenantIds[i] = delta.TenantId;
            buckets[i] = delta.BucketStartUtc.UtcDateTime;
            ingress[i] = delta.IngressBytes;
            egress[i] = delta.EgressBytes;
            counts[i] = delta.RequestCount;
            i++;
        }

        await db.Database.ExecuteSqlRawAsync(AccumulateSql,
        [
            Grain("hourly", TrafficGrain.Hourly),
            new NpgsqlParameter("tenantIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = tenantIds },
            new NpgsqlParameter("buckets", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = buckets },
            new NpgsqlParameter("ingress", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = ingress },
            new NpgsqlParameter("egress", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = egress },
            new NpgsqlParameter("counts", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = counts }
        ], ct);
    }

    public async Task RollUpHourlyAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default)
    {
        // Fold and delete share a transaction: a failure between them would leave the daily row
        // written and the hourly rows still present to be folded again on the next pass.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlRawAsync(RollUpSql,
            [Grain("hourly", TrafficGrain.Hourly), Grain("daily", TrafficGrain.Daily), Cutoff(cutoffUtc)], ct);

        await db.Database.ExecuteSqlRawAsync(DropFoldedSql,
            [Grain("hourly", TrafficGrain.Hourly), Cutoff(cutoffUtc)], ct);

        await transaction.CommitAsync(ct);
    }

    public async Task PruneDailyAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default)
        => await db.Database.ExecuteSqlRawAsync(PruneSql, [Grain("daily", TrafficGrain.Daily), Cutoff(cutoffUtc)], ct);

    private static NpgsqlParameter Grain(string name, TrafficGrain grain) => new(name, NpgsqlDbType.Integer) { Value = (int)grain };

    private static NpgsqlParameter Cutoff(DateTimeOffset cutoffUtc) => new("cutoff", NpgsqlDbType.TimestampTz) { Value = cutoffUtc.UtcDateTime };
}
