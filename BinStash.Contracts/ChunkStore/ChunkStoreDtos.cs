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

namespace BinStash.Contracts.ChunkStore;

public class ChunkStoreSummaryDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
}

public class ChunkStoreDetailDto : ChunkStoreSummaryDto
{
    public required string Type { get; set; }
    public required ChunkStoreChunkerDto Chunker { get; set; }
    public required ChunkStoreBackendSettingsDto BackendSettings { get; set; }
    public required Dictionary<string, object> Stats { get; set; }
}

public class ChunkStoreStatsDto
{
    public required int TotalChunks { get; set; }
    // StorageUsed
    // DeduplicationRatio
    // Chunks ingested (last 7 days; daily)
    // Storage growth (last 7 days; daily)
}

public class ChunkStoreChunkerDto
{
    public string Type { get; set; } = string.Empty;
    public int? MinChunkSize { get; set; }
    public int? AvgChunkSize { get; set; }
    public int? MaxChunkSize { get; set; }
}

/// <summary>
/// Polymorphic backend settings DTO.
/// The <see cref="Type"/> discriminator determines which optional properties are populated.
/// </summary>
public class ChunkStoreBackendSettingsDto
{
    /// <summary>
    /// The backend type discriminator (e.g., "Local", "S3").
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Local filesystem path. Populated when <see cref="Type"/> is "Local".
    /// </summary>
    public string? LocalPath { get; set; }

    // S3 backend fields (populated when Type is "S3")

    /// <summary>S3 bucket name.</summary>
    public string? S3Bucket { get; set; }

    /// <summary>Optional key prefix inside the bucket (e.g. "binstash/prod/").</summary>
    public string? S3Prefix { get; set; }

    /// <summary>AWS region (e.g. "eu-central-1"). Required for AWS S3; omit when <see cref="S3ServiceUrl"/> is set.</summary>
    public string? S3Region { get; set; }

    /// <summary>Custom service URL for S3-compatible providers (MinIO, Cloudflare R2, Backblaze B2, etc.).</summary>
    public string? S3ServiceUrl { get; set; }

    /// <summary>AWS Access Key ID. When null the AWS default credential chain is used (IAM role, env vars, shared credentials).</summary>
    public string? S3AccessKeyId { get; set; }

    /// <summary>Force path-style addressing (required for MinIO and some S3-compatible providers).</summary>
    public bool? S3ForcePathStyle { get; set; }

    /// <summary>Local directory for index/buffer cache. Falls back to OS temp when null.</summary>
    public string? S3LocalCachePath { get; set; }
}

public class CreateChunkStoreDto
{
    public required string Name { get; set; }
    public required string Type { get; set; }

    /// <summary>
    /// Local filesystem path. Required when <see cref="Type"/> is "Local".
    /// </summary>
    public string? LocalPath { get; set; }

    public ChunkStoreChunkerDto? Chunker { get; set; }

    // S3 backend fields (required/optional when Type is "S3")

    /// <summary>S3 bucket name. Required when <see cref="Type"/> is "S3".</summary>
    public string? S3Bucket { get; set; }

    /// <summary>Optional key prefix inside the bucket.</summary>
    public string? S3Prefix { get; set; }

    /// <summary>AWS region. Required for AWS S3; omit when <see cref="S3ServiceUrl"/> is set.</summary>
    public string? S3Region { get; set; }

    /// <summary>Custom service URL for S3-compatible providers.</summary>
    public string? S3ServiceUrl { get; set; }

    /// <summary>AWS Access Key ID. When null the AWS default credential chain is used.</summary>
    public string? S3AccessKeyId { get; set; }

    /// <summary>AWS Secret Access Key. When null the AWS default credential chain is used.</summary>
    public string? S3SecretAccessKey { get; set; }

    /// <summary>Force path-style addressing (MinIO, Cloudflare R2, Backblaze B2).</summary>
    public bool? S3ForcePathStyle { get; set; }

    /// <summary>Local directory for index/buffer cache. Falls back to OS temp when null.</summary>
    public string? S3LocalCachePath { get; set; }
}

public class ChunkStoreMissingChunkSyncInfoDto
{
    public required List<string> ChunkChecksums { get; set; }
}

public class ChunkUploadDto
{
    public required string Checksum { get; set; }
    public required byte[] Data { get; set; }
}
