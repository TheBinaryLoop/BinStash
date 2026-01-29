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

public class ChunkStoreEntityTypeConfiguration : IEntityTypeConfiguration<ChunkStore>
{
    public void Configure(EntityTypeBuilder<ChunkStore> builder)
    {
        builder.ToTable("ChunkStores");

        builder.HasKey(cs => cs.Id);

        builder.Property(cs => cs.Id).HasColumnName("Id").ValueGeneratedNever();
        builder.Property(cs => cs.Name).IsRequired().HasMaxLength(256);
        builder.Property(cs => cs.Type).IsRequired();
        builder.Property(cs => cs.LocalPath).IsRequired();

        builder.OwnsOne(cs => cs.ChunkerOptions, chunker =>
        {
            chunker.Property(c => c.Type).HasColumnName("Chunker_Type").IsRequired();
            chunker.Property(c => c.MinChunkSize).HasColumnName("Chunker_MinChunkSize");
            chunker.Property(c => c.AvgChunkSize).HasColumnName("Chunker_AvgChunkSize");
            chunker.Property(c => c.MaxChunkSize).HasColumnName("Chunker_MaxChunkSize");
            chunker.Property(c => c.ShiftCount).HasColumnName("Chunker_ShiftCount");
            chunker.Property(c => c.BoundaryCheckBytes).HasColumnName("Chunker_BoundaryCheckBytes");
        });
    }
}