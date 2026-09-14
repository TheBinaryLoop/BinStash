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

public class TenantStorageSnapshotConfiguration : IEntityTypeConfiguration<TenantStorageSnapshot>
{
    public void Configure(EntityTypeBuilder<TenantStorageSnapshot> builder)
    {
        builder.ToTable("TenantStorageSnapshots");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ComputedAt).IsRequired();
        builder.Property(x => x.UniqueLogicalBytes).IsRequired();
        builder.Property(x => x.LogicalBytes).IsRequired();

        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Every read is "the newest snapshot for this tenant" — the quota gate and the usage
        // page both ask only that. Descending so the answer is the index's first row.
        builder.HasIndex(x => new { x.TenantId, x.ComputedAt })
            .IsDescending(false, true);
    }
}
