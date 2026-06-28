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

using System.Text.Json.Serialization;

namespace BinStash.Core.Entities;

/// <summary>
/// Base class for chunk store backend configuration.
/// Each <see cref="ChunkStoreType"/> has a corresponding concrete settings class.
/// Serialized as JSON in the database.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(LocalFolderBackendSettings), "LocalFolder")]
public abstract class ChunkStoreBackendSettings;

/// <summary>
/// Settings for a local-folder-based chunk store backend.
/// Chunks are stored as pack files on a locally accessible filesystem path.
/// </summary>
public sealed class LocalFolderBackendSettings : ChunkStoreBackendSettings
{
    /// <summary>
    /// The root directory path where pack files, index files, and release definitions are stored.
    /// </summary>
    public required string Path { get; init; }
}
