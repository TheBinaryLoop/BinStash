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
using BinStash.Contracts.Release;
using BinStash.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinStash.Infrastructure.Data.Configurations;

public class ReleaseVariantEntityTypeConfiguration : IEntityTypeConfiguration<ReleaseVariant>
{
    public void Configure(EntityTypeBuilder<ReleaseVariant> builder)
    {
        builder.ToTable("ReleaseVariants");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.TargetKey).IsRequired().HasMaxLength(ReleaseTarget.MaxLength);
        builder.Property(v => v.TargetAttributes).HasColumnType("jsonb").IsRequired(false);
        builder.Property(v => v.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(v => v.SerializerVersion).IsRequired().HasDefaultValue(0);

        builder.Property(v => v.ReleaseDefinitionChecksum)
            .HasConversion(
                v => v.GetBytes(), // to database (byte[])
                v => new Hash32(v)) // from database (Hash32)
            .HasColumnType("bytea")
            .IsRequired();

        builder.HasOne(v => v.Release)
            .WithMany(r => r.Variants)
            .HasForeignKey(v => v.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Targets are canonicalized to one spelling before they get here, so a plain unique index
        // is what enforces "a target is published once". It is also what makes a build matrix
        // safe: five agents racing to add five targets to one release either all succeed or fail
        // on the duplicate they actually are.
        builder.HasIndex(v => new { v.ReleaseId, v.TargetKey }).IsUnique();
    }
}
