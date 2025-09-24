// Copyright (C) 2025  Lukas Eßmann
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
using BinStash.Core.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinStash.Infrastructure.Data.Configurations;

public class ChunkEntityTypeConfiguration : IEntityTypeConfiguration<Chunk>
{
    public void Configure(EntityTypeBuilder<Chunk> builder)
    {
        builder.ToTable("Chunks", t =>
        {
            t.HasCheckConstraint("chk_chunks_checksum_len", "octet_length(\"Checksum\") = 32");
        });
        
        builder.HasKey(c => new { c.ChunkStoreId, c.Checksum });
        
        builder.Property(c => c.Checksum)
            .HasConversion(
                v => v.GetBytes(), // to database (byte[])
                v => new Hash32(v)) // from database (Hash32)
            .HasColumnType("bytea")
            .IsRequired();
        builder.Property(c => c.ChunkStoreId).IsRequired();
        builder.Property(c => c.Length).IsRequired();
    }
}