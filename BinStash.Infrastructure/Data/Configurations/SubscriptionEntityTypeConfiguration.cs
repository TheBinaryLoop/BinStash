// Copyright (C) 2025-2026  Lukas Eßmann
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

public class SubscriptionEntityTypeConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");
        
        builder.HasKey(s => s.Id);
        
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.BillingMode).IsRequired();
        builder.Property(s => s.Status).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(s => s.MinimumMonthlyFee).IsRequired();
        builder.Property(s => s.Currency).IsRequired();
        
        builder.HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId);
    }
}