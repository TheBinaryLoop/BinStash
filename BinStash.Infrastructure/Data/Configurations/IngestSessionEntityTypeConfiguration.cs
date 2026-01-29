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
//

using BinStash.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinStash.Infrastructure.Data.Configurations;

public class IngestSessionEntityTypeConfiguration : IEntityTypeConfiguration<IngestSession>
{
    public void Configure(EntityTypeBuilder<IngestSession> builder)
    {
        builder.ToTable("IngestSessions");
        
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.RepoId).IsRequired();
        builder.Property(e => e.StartedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.LastUpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.ExpiresAt).IsRequired();
        builder.Property(e => e.State).IsRequired();
        builder.Property(e => e.ChunksSeenTotal).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.ChunksSeenUnique).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.ChunksSeenNew).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.DataSizeTotal).IsRequired().HasDefaultValue(0L);
        builder.Property(e => e.DataSizeUnique).IsRequired().HasDefaultValue(0L);
        builder.Property(e => e.FilesSeenTotal).IsRequired().HasDefaultValue(0L);
        builder.Property(e => e.FilesSeenUnique).IsRequired().HasDefaultValue(0L);
        builder.Property(e => e.FilesSeenNew).IsRequired().HasDefaultValue(0L);
        builder.Property(e => e.MetadataSize).IsRequired().HasDefaultValue(0L);
        
        
        builder.HasIndex(e => e.State);
        
        builder.HasOne(e => e.Repository)
            .WithMany()
            .HasForeignKey(e => e.RepoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}