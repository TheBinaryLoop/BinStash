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

namespace BinStash.Core.Chunking;

public interface IChunker
{
    /// <summary>
    /// Reads a file and returns the ordered chunk map for it (excluding raw data).
    /// </summary>
    IReadOnlyList<ChunkMapEntry> GenerateChunkMap(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Reads a stream and returns the ordered chunk map for it (excluding raw data).
    /// </summary>
    IReadOnlyList<ChunkMapEntry> GenerateChunkMap(Stream stream, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Loads the actual chunk data for one or more chunk map entries from a file.
    /// </summary>
    Task<ChunkData> LoadChunkDataAsync(ChunkMapEntry chunkInfo, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Loads the actual chunk data for one or more chunk map entries from a stream.
    /// </summary>
    Task<ChunkData> LoadChunkDataAsync(Stream stream, ChunkMapEntry chunkInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recommends chunker settings based on the target folder's content.
    /// </summary>
    Task<RecommendationResult> RecommendChunkerSettingsForTargetAsync(string folderPath, ChunkAnalysisTarget target, Action<string>? log = null, CancellationToken cancellationToken = default);
}