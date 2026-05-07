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

public class TenantMemberInvitationEntityTypeConfiguration : IEntityTypeConfiguration<TenantMemberInvitation>
{
    public void Configure(EntityTypeBuilder<TenantMemberInvitation> builder)
    {
        builder.ToTable("TenantMemberInvitations");
        
        builder.HasKey(tmi => tmi.Id);
        
        builder.Property(tmi => tmi.Id).ValueGeneratedNever();
        builder.Property(tmi => tmi.TenantId).IsRequired();
        builder.Property(tmi => tmi.InviterId).IsRequired();
        builder.Property(tmi => tmi.InviteeEmail).IsRequired().HasMaxLength(256);
        builder.Property(tmi => tmi.Roles).IsRequired();
        builder.Property(tmi => tmi.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(tmi => tmi.ExpiresAt).IsRequired();
        builder.Property(tmi => tmi.Code).IsRequired().HasMaxLength(256);
        
        builder.HasOne(tmi => tmi.Tenant).WithMany().HasForeignKey(tmi => tmi.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(tmi => tmi.Inviter).WithMany().HasForeignKey(tmi => tmi.InviterId).OnDelete(DeleteBehavior.Cascade);
    }
}