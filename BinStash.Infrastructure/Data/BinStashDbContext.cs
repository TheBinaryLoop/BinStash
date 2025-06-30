using BinStash.Core.Entities;
using Microsoft.EntityFrameworkCore;
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

namespace BinStash.Infrastructure.Data;

public class BinStashDbContext : DbContext
{
    public DbSet<ChunkStore> ChunkStores { get; set; }
    public DbSet<Repository> Repositories { get; set; }
    public DbSet<Release> Releases { get; set; }
    public DbSet<Chunk> Chunks { get; set; }

    public BinStashDbContext(DbContextOptions<BinStashDbContext> options) : base(options) {}
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BinStashDbContext).Assembly);
    }
}