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

using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinStash.Infrastructure.Data.Configurations;

public class FileDefinitionEntityTypeConfiguration : IEntityTypeConfiguration<FileDefinition>
{
    public void Configure(EntityTypeBuilder<FileDefinition> builder)
    {
        builder.ToTable("FileDefinitions", t =>
        {
            t.HasCheckConstraint("chk_file_definitions_checksum_len", "octet_length(\"Checksum\") = 32");
        });
        
        builder.HasKey(fd => new { fd.ChunkStoreId, fd.Checksum });
        
        builder.Property(fd => fd.Checksum)
            .HasConversion(
                v => v.GetBytes(), // to database (byte[])
                v => new Hash32(v)) // from database (Hash32)
            .HasColumnType("bytea")
            .IsRequired();
        builder.Property(fd => fd.ChunkStoreId).IsRequired();
        builder.Property(fd => fd.Length).IsRequired();
    }
}