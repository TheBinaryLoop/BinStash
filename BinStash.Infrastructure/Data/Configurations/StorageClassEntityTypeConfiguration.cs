// Copyright (C) 2025  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using BinStash.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinStash.Infrastructure.Data.Configurations;

public class StorageClassEntityTypeConfiguration : IEntityTypeConfiguration<StorageClass>
{
    public void Configure(EntityTypeBuilder<StorageClass> builder)
    {
        builder.ToTable("StorageClasses");

        builder.HasKey(sc => sc.Name);

        builder.Property(sc => sc.Name).IsRequired().HasMaxLength(32);
        builder.Property(sc => sc.DisplayName).IsRequired().HasMaxLength(128);
        builder.Property(sc => sc.Description).HasMaxLength(1024);
        builder.Property(sc => sc.IsDeprecated).IsRequired();
        builder.Property(sc => sc.MaxChunkBytes).IsRequired().HasDefaultValue(16 * 1024 * 1024);
    }
}