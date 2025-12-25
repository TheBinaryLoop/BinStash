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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Infrastructure.Data;

public class BinStashDbContext(DbContextOptions<BinStashDbContext> options)
    : IdentityDbContext<BinStashUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<ApiKey> ApiKeys { get; set; }
    public DbSet<Chunk> Chunks { get; set; }
    public DbSet<ChunkStore> ChunkStores { get; set; }
    public DbSet<FileDefinition> FileDefinitions { get; set; }
    public DbSet<IngestSession> IngestSessions { get; set; }
    public DbSet<Release> Releases { get; set; }
    public DbSet<ReleaseMetrics> ReleaseMetrics { get; set; }
    public DbSet<Repository> Repositories { get; set; }
    public DbSet<RepositoryRoleAssignment> RepositoryRoleAssignments { get; set; }
    public DbSet<ServiceAccount> ServiceAccounts { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantMember> TenantMembers { get; set; }
    public DbSet<TenantMemberInvitation> TenantMemberInvitations { get; set; }
    public DbSet<TenantRoleAssignment> TenantRoleAssignments { get; set; }
    public DbSet<UserGroup> UserGroups { get; set; }
    public DbSet<UserGroupMember> UserGroupMembers { get; set; }
    public DbSet<UserRefreshToken> UserRefreshTokens { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BinStashDbContext).Assembly);
    }
}