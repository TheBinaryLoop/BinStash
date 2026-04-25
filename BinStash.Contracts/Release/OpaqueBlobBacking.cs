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

using BinStash.Contracts.Hashing;

namespace BinStash.Contracts.Release;

public sealed class OpaqueBlobBacking : ArtifactBacking
{
    /// <summary>
    /// The file-content hash (<c>BLAKE3(file bytes)</c>). Populated for V1–V4 packages.
    /// Null for V5 packages (use <see cref="StorageKey"/> instead).
    /// </summary>
    public Hash32? ContentHash { get; set; }

    /// <summary>
    /// The FileDef pack-store key (<c>BLAKE3(FileDefinitionRecord blob)</c>).
    /// Populated for V5 packages. Null for V1–V4 packages (server falls back to DB lookup).
    /// </summary>
    public Hash32? StorageKey { get; set; }

    public long? Length { get; set; }
}