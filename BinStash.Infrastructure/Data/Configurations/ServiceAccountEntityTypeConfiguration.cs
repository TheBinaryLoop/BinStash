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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinStash.Infrastructure.Data.Configurations;

public class ServiceAccountEntityTypeConfiguration : IEntityTypeConfiguration<ServiceAccount>
{
    public void Configure(EntityTypeBuilder<ServiceAccount> builder)
    {
        builder.ToTable("ServiceAccounts");
        
        builder.HasKey(sa => sa.Id);
        
        builder.Property(sa => sa.Name).IsRequired().HasMaxLength(256);
        builder.Property(sa => sa.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(sa => sa.ExpiresAt).IsRequired(false);
        builder.Property(sa => sa.Disabled).IsRequired().HasDefaultValue(false);

        builder.HasIndex(sa => new { sa.TenantId, sa.Name }).IsUnique();
        builder.HasIndex(sa => sa.TenantId);
        
        builder.HasOne(sa => sa.Tenant).WithMany().HasForeignKey(sa => sa.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(sa => sa.ApiKeys).WithOne(ak => ak.ServiceAccount).HasForeignKey(ak => ak.ServiceAccountId).IsRequired();
    }
}