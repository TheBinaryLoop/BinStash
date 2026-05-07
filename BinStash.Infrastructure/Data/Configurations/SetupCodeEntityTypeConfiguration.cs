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

public class SetupCodeEntityTypeConfiguration : IEntityTypeConfiguration<SetupCode>
{
    public void Configure(EntityTypeBuilder<SetupCode> builder)
    {
        builder.ToTable("SetupCodes");
        
        builder.HasKey(sc => sc.Id);
        
        builder.Property(sc => sc.Id).IsRequired().ValueGeneratedOnAdd();
        builder.Property(sc => sc.CodeHash).IsRequired().HasMaxLength(256);
        builder.Property(sc => sc.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(sc => sc.ExpiresAt).IsRequired();
        builder.Property(sc => sc.AttemptCount).IsRequired();
    }
}