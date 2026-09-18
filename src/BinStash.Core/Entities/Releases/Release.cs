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

/// <summary>
/// A published version within a repository. What it contains lives on its
/// <see cref="Variants"/> — one per build target — rather than on the release itself.
/// </summary>
/// <remarks>
/// Variants are append-only: a target can be added to an existing release, but an existing one
/// is never replaced. A release is therefore never "finished", and nothing has to declare in
/// advance which targets it will have.
/// </remarks>
public class Release
{
    public Guid Id { get; set; }
    public required string Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Guid RepoId { get; set; }
    public virtual Repository Repository { get; set; } = null!;
    
    public string? Notes { get; set; }
    
    public string? CustomProperties { get; set; } = null;

    public virtual ICollection<ReleaseVariant> Variants { get; set; } = new List<ReleaseVariant>();
}