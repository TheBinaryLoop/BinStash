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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinStash.Infrastructure.Data.Configurations;

public class TenantTrafficSampleEntityTypeConfiguration : IEntityTypeConfiguration<TenantTrafficSample>
{
    public void Configure(EntityTypeBuilder<TenantTrafficSample> builder)
    {
        builder.ToTable("TenantTrafficSamples");

        // The natural key is also the upsert's conflict target: flushes from several replicas add
        // into the same bucket, so the bucket has to be identifiable without a surrogate id.
        builder.HasKey(e => new { e.TenantId, e.BucketStartUtc, e.Grain });

        builder.Property(e => e.Grain).IsRequired().HasConversion<int>();

        builder.Property(e => e.IngressBytes).IsRequired();
        builder.Property(e => e.EgressBytes).IsRequired();
        builder.Property(e => e.RequestCount).IsRequired();

        // Every read is "one grain, one window, newest first" — either for one tenant (the
        // workspace chart) or across all of them (the instance chart).
        builder.HasIndex(e => new { e.Grain, e.BucketStartUtc });
    }
}
