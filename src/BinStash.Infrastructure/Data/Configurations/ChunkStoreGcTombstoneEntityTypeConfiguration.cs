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

using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinStash.Infrastructure.Data.Configurations;

public class ChunkStoreGcTombstoneEntityTypeConfiguration : IEntityTypeConfiguration<ChunkStoreGcTombstone>
{
    public void Configure(EntityTypeBuilder<ChunkStoreGcTombstone> builder)
    {
        builder.ToTable("ChunkStoreGcTombstones", t =>
        {
            t.HasCheckConstraint("chk_gc_tombstones_checksum_len", "octet_length(\"Checksum\") = 32");
        });

        // Keyed the same way as Chunks/FileDefinitions so that resurrecting an object is a
        // single point lookup on the hash the ingest path already has in hand.
        builder.HasKey(t => new { t.ChunkStoreId, t.Category, t.Checksum });

        builder.Property(t => t.Checksum)
            .HasConversion(
                v => v.GetBytes(),
                v => new Hash32(v))
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(t => t.Category)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(t => t.BucketPrefix).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(t => t.RunId).IsRequired();
        builder.Property(t => t.QuarantinedAt).IsRequired();
        builder.Property(t => t.EligibleAt).IsRequired();
        builder.Property(t => t.PackFileNo).IsRequired();
        builder.Property(t => t.PackOffset).IsRequired();
        builder.Property(t => t.PackLength).IsRequired();
        builder.Property(t => t.LogicalLength).IsRequired();
        builder.Property(t => t.CompressedLength).IsRequired().HasDefaultValue(0);

        // The reclaim phase asks "what is collectable in this bucket now?", which is a range
        // scan over eligibility within one store; the pack-file column orders the work by the
        // file it will have to rewrite.
        builder.HasIndex(t => new { t.ChunkStoreId, t.Category, t.BucketPrefix, t.EligibleAt });
        builder.HasIndex(t => new { t.ChunkStoreId, t.EligibleAt });
        builder.HasIndex(t => t.RunId);
    }
}
