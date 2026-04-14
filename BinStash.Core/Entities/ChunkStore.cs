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

public class ChunkStore
{
    public Guid Id { get; }
    public string Name { get; private set; }

    // Chunker settings
    public ChunkerOptions ChunkerOptions { get; init; } = ChunkerOptions.Default(ChunkerType.FastCdc);

    /// <summary>
    /// The storage backend type for this chunk store.
    /// Determines how <see cref="BackendSettings"/> is interpreted.
    /// </summary>
    public ChunkStoreType Type { get; private set; }

    /// <summary>
    /// Backend-specific configuration, serialized as JSON.
    /// The concrete type is determined by <see cref="Type"/>.
    /// </summary>
    public ChunkStoreBackendSettings BackendSettings { get; private set; }

    public ProbeMode ProbeMode { get; set; } = ProbeMode.ReadWrite;

    public long? MinFreeBytes { get; set; }

    // ReSharper disable once UnusedMember.Local
    private ChunkStore() { } // EF

    public ChunkStore(string name, ChunkStoreType type, ChunkStoreBackendSettings backendSettings)
    {
        Id = Guid.CreateVersion7();
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
        BackendSettings = backendSettings ?? throw new ArgumentNullException(nameof(backendSettings));
        ProbeMode = ProbeMode.ReadWrite;
    }

    /// <summary>
    /// Returns the backend settings cast to the expected concrete type.
    /// Throws <see cref="InvalidOperationException"/> if the settings type does not match.
    /// </summary>
    public T GetBackendSettings<T>() where T : ChunkStoreBackendSettings
    {
        return BackendSettings as T
            ?? throw new InvalidOperationException(
                $"Expected backend settings of type '{typeof(T).Name}' but found '{BackendSettings.GetType().Name}'.");
    }
}

public enum ChunkStoreType
{
    Local
}

public enum ProbeMode
{
    ReadOnly,
    ReadWrite
}
