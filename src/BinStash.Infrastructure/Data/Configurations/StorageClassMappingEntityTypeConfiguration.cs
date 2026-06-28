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

public class StorageClassMappingEntityTypeConfiguration : IEntityTypeConfiguration<StorageClassMapping>
{
    public void Configure(EntityTypeBuilder<StorageClassMapping> builder)
    {
        builder.ToTable("StorageClassMappings");

        builder.HasKey(scm => new { scm.TenantId, scm.StorageClassName });

        builder.Property(scm => scm.TenantId).IsRequired();
        builder.Property(scm => scm.StorageClassName).IsRequired().HasMaxLength(32);
        builder.Property(scm => scm.ChunkStoreId).IsRequired();
        builder.Property(scm => scm.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasIndex(scm => new { scm.TenantId, scm.IsDefault }).IsUnique().HasFilter("\"IsDefault\" = true"); // Only one default per tenant
        builder.HasIndex(scm => scm.ChunkStoreId);
        
        builder.HasOne<Tenant>().WithMany().HasForeignKey(scm => scm.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StorageClass>().WithMany().HasForeignKey(scm => scm.StorageClassName).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChunkStore>().WithMany().HasForeignKey(scm => scm.ChunkStoreId).OnDelete(DeleteBehavior.Cascade);
    }
}