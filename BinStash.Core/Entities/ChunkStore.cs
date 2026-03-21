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
    

    // TODO: Make the options somehow linked to the type
    public ChunkStoreType Type { get; private set; }
    
    // Settings for the local chunk store type
    public string LocalPath { get; private set; }
    
    public ProbeMode ProbeMode { get; set; } = ProbeMode.ReadWrite;

    public long? MinFreeBytes { get; set; }
    
    private ChunkStore() { } // EF
    
    public ChunkStore(string name, ChunkStoreType type, string localPath)
    {
        Id = Guid.CreateVersion7();
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
        LocalPath = localPath ?? throw new ArgumentNullException(nameof(localPath));
        ProbeMode = ProbeMode.ReadWrite;
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