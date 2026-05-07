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

public class BackgroundJobEntityTypeConfiguration : IEntityTypeConfiguration<BackgroundJob>
{
    public void Configure(EntityTypeBuilder<BackgroundJob> builder)
    {
        builder.ToTable("BackgroundJobs");

        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).HasColumnName("Id").ValueGeneratedNever();

        builder.Property(j => j.JobType).IsRequired().HasMaxLength(64);
        builder.Property(j => j.Status).IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(j => j.JobData).HasColumnType("jsonb").IsRequired(false);
        builder.Property(j => j.ProgressData).HasColumnType("jsonb").IsRequired(false);
        builder.Property(j => j.ErrorDetails).HasColumnType("jsonb").IsRequired(false);

        builder.Property(j => j.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(j => j.StartedAt).IsRequired(false);
        builder.Property(j => j.CompletedAt).IsRequired(false);

        builder.HasIndex(j => j.JobType);
        builder.HasIndex(j => j.Status);
    }
}
