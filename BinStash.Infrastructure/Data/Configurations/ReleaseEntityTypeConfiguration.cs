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

public class ReleaseEntityTypeConfiguration : IEntityTypeConfiguration<Release>
{
    public void Configure(EntityTypeBuilder<Release> builder)
    {
        builder.ToTable("Releases");
        
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("Id").ValueGeneratedNever();
        builder.Property(r => r.Version).IsRequired().HasMaxLength(256);
        builder.Property(r => r.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(r => r.ReleaseDefinitionChecksum)
            .HasConversion(
                v => v.GetBytes(), // to database (byte[])
                v => new Hash32(v)) // from database (Hash32)
            .HasColumnType("bytea")
            .IsRequired();
        builder.Property(r => r.CustomProperties).HasColumnType("jsonb").IsRequired(false);

        builder.HasOne(r => r.Repository)
            .WithMany(repo => repo.Releases)
            .HasForeignKey(r => r.RepoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}