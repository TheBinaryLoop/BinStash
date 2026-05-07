// Copyright (C) 2025-2026  Lukas Eßmann
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

namespace BinStash.Core.Entities;

public class Repository
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string StorageClass { get; set; } = "default";
    public Guid ChunkStoreId { get; set; }
    public virtual required ChunkStore ChunkStore { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public virtual ICollection<Release> Releases { get; set; } = new List<Release>();
}