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

public class StorageClassDefaultMappingEntityTypeConfiguratuion : IEntityTypeConfiguration<StorageClassDefaultMapping>
{
    public void Configure(EntityTypeBuilder<StorageClassDefaultMapping> builder)
    {
        builder.ToTable("StorageClassDefaultMappings");

        builder.HasKey(scdm => scdm.StorageClassName);

        builder.Property(scdm => scdm.StorageClassName).IsRequired().HasMaxLength(32);
        builder.Property(scdm => scdm.ChunkStoreId).IsRequired();
        builder.Property(scdm => scdm.IsDefault).IsRequired();
        builder.Property(scdm => scdm.IsEnabled).IsRequired();
        builder.Property(scdm => scdm.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasIndex(scdm => scdm.ChunkStoreId);
        
        builder.HasOne<StorageClass>().WithMany().HasForeignKey(scdm => scdm.StorageClassName).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChunkStore>().WithMany().HasForeignKey(scdm => scdm.ChunkStoreId).OnDelete(DeleteBehavior.Cascade);
    }
}