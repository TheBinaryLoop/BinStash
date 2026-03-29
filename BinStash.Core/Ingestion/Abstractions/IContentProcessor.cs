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
using BinStash.Core.Chunking;
using BinStash.Core.Entities;
using BinStash.Core.Ingestion.Models;

namespace BinStash.Core.Ingestion.Abstractions;

public interface IContentProcessor
{
    StorageHashingResult HashStorageWorkItems(IngestionResult ingestionResult, int degreeOfParallelism = 0);
    ChunkMapGenerationResult GenerateChunkMaps(StorageHashingResult  hashingResult, IngestionResult ingestionResult, IChunker chunker, IReadOnlySet<Hash32> missingContentHashes, int degreeOfParallelism = 0);
}