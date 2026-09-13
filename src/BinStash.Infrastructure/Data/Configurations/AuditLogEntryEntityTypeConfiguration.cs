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

public class AuditLogEntryEntityTypeConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("Id").ValueGeneratedNever();

        builder.Property(e => e.TenantId).IsRequired(false);
        builder.Property(e => e.OccurredAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(e => e.Action).IsRequired().HasMaxLength(96);

        builder.Property(e => e.ActorType).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.ActorId).IsRequired(false);
        builder.Property(e => e.ActorDisplay).IsRequired(false).HasMaxLength(320);

        builder.Property(e => e.TargetType).IsRequired(false).HasMaxLength(64);
        builder.Property(e => e.TargetId).IsRequired(false).HasMaxLength(128);
        builder.Property(e => e.TargetName).IsRequired(false).HasMaxLength(512);

        builder.Property(e => e.Outcome).IsRequired().HasConversion<string>().HasMaxLength(32);

        builder.Property(e => e.IpAddress).IsRequired(false).HasMaxLength(64);
        builder.Property(e => e.UserAgent).IsRequired(false).HasMaxLength(512);

        builder.Property(e => e.Metadata).HasColumnType("jsonb").IsRequired(false);

        // The audit view is always "this tenant, newest first", optionally narrowed by action.
        builder.HasIndex(e => new { e.TenantId, e.OccurredAt });
        builder.HasIndex(e => e.Action);
        builder.HasIndex(e => e.OccurredAt);
    }
}
