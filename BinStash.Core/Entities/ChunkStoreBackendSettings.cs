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
[JsonDerivedType(typeof(S3BackendSettings), "S3")]
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

/// <summary>
/// Settings for an S3-compatible chunk store backend.
/// Chunks are buffered locally into pack files and flushed to S3 via multipart upload.
/// Reads use S3 ranged GET against a memory-cached index, keeping API costs minimal.
/// </summary>
/// <remarks>
/// <para><b>AWS S3:</b> Set <see cref="Region"/>. Leave <see cref="AccessKeyId"/> and
/// <see cref="SecretAccessKey"/> null to use the AWS default credential chain
/// (IAM role, environment variables, shared credentials file).</para>
/// <para><b>MinIO / Cloudflare R2 / Backblaze B2:</b> Set <see cref="ServiceUrl"/> and
/// set <see cref="ForcePathStyle"/> to <c>true</c>. Provide explicit credentials.</para>
/// </remarks>
public sealed class S3BackendSettings : ChunkStoreBackendSettings
{
    /// <summary>
    /// The S3 bucket name where pack files and indices are stored.
    /// </summary>
    public required string BucketName { get; init; }

    /// <summary>
    /// Optional key prefix applied to all objects (e.g. <c>"binstash/prod/"</c>).
    /// Defaults to an empty string (root of the bucket).
    /// </summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>
    /// Custom service endpoint URL for S3-compatible providers such as MinIO, Cloudflare R2,
    /// or Backblaze B2 (e.g. <c>"https://minio.example.com"</c>).
    /// When null, the AWS S3 service endpoint for <see cref="Region"/> is used.
    /// </summary>
    public string? ServiceUrl { get; init; }

    /// <summary>
    /// AWS region (e.g. <c>"eu-central-1"</c>).
    /// Required for AWS S3. May be omitted when <see cref="ServiceUrl"/> is set.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    /// AWS Access Key ID.
    /// When null, the AWS default credential chain is used
    /// (IAM role, <c>AWS_ACCESS_KEY_ID</c> environment variable, shared credentials file).
    /// </summary>
    public string? AccessKeyId { get; init; }

    /// <summary>
    /// AWS Secret Access Key.
    /// When null, the AWS default credential chain is used.
    /// </summary>
    public string? SecretAccessKey { get; init; }

    /// <summary>
    /// Local directory path used for write-buffer pack files and the downloaded index cache.
    /// When null, a subdirectory of <see cref="Path.GetTempPath"/> is used, keyed by the chunk store ID.
    /// Setting this to a persistent path (not temp) allows index reuse across process restarts.
    /// </summary>
    public string? LocalCachePath { get; init; }

    /// <summary>
    /// When <c>true</c>, forces path-style S3 URLs (<c>http://host/bucket/key</c>) instead of
    /// virtual-hosted-style (<c>http://bucket.host/key</c>).
    /// Required for MinIO and some other S3-compatible providers.
    /// </summary>
    public bool ForcePathStyle { get; init; } = false;

    /// <summary>
    /// Maximum pack file size in bytes before rotation triggers and the current pack is uploaded.
    /// Defaults to 4 GiB (same as the local backend).
    /// </summary>
    public long MaxPackSizeBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Size of each part in a multipart upload, in bytes.
    /// Must be at least 5 MiB (AWS minimum). Defaults to 16 MiB.
    /// </summary>
    public long MultipartPartSizeBytes { get; init; } = 16L * 1024 * 1024;
}

