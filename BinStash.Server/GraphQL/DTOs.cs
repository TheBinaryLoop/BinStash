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

namespace BinStash.Server.GraphQL;

public sealed class RepositoryGql
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string StorageClass { get; init; }
    public ChunkStoreChunkerGql? Chunker { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class ChunkStoreChunkerGql
{
    public required string Type { get; init; }
    public int? MinChunkSize { get; init; }
    public int? AvgChunkSize { get; init; }
    public int? MaxChunkSize { get; init; }
}

public sealed class ReleaseGql
{
    public required Guid Id { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Notes { get; init; }
    public Guid RepoId { get; init; }
    public object? CustomProperties { get; init; }
}

public sealed class ReleaseMetricsGql
{
    public required int ChunksInRelease { get; set; }
    public required int NewChunks { get; set; }

    // Full logical size of the release as users see it
    public required ulong TotalLogicalBytes { get; set; }

    // Unique uncompressed bytes newly added by this release
    public required long NewUniqueLogicalBytes { get; set; }

    // Unique compressed bytes newly added by this release
    public required long NewCompressedBytes { get; set; }

    // Total metadata bytes for full release package
    public required int MetaBytesFull { get; set; }

    // Reserved for later diff/patch metadata
    //public int MetaBytesFullDiff { get; set; }

    public required int ComponentsInRelease { get; set; }
    public required int FilesInRelease { get; set; }

    // Derived-but-stored metrics for easy querying/charting
    public required double IncrementalCompressionRatio { get; set; }
    public required double IncrementalDeduplicationRatio { get; set; }
    public required double IncrementalEffectiveRatio { get; set; }

    public required long CompressionSavedBytes { get; set; }
    public required long DeduplicationSavedBytes { get; set; }

    public required double NewDataPercent { get; set; }
}

public sealed class ServiceAccountGql
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class UserGql
{
    public required Guid Id { get; init; }
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    // TODO: List of all roles for this user (instance and tenant)
    public required bool IsEmailVerified { get; init; }
    public required bool IsOnboardingCompleted { get; init; }
}

public sealed class TenantGql
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; set; }
    public required  DateTimeOffset CreatedAt { get; init; }
    
    public DateTimeOffset? JoinedAt { get; init; }
}

public sealed class ChunkStoreGql
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public ChunkStoreBackendSettingsGql? BackendSettings { get; init; }
}

/// <summary>
/// GraphQL representation of backend-specific chunk store settings.
/// Uses a flat key-value model for extensibility across backend types.
/// </summary>
public sealed class ChunkStoreBackendSettingsGql
{
    /// <summary>
    /// The backend type discriminator (e.g. "LocalFolder").
    /// </summary>
    public required string BackendType { get; init; }

    /// <summary>
    /// Local filesystem path — only set when <see cref="BackendType"/> is "LocalFolder".
    /// </summary>
    public string? LocalPath { get; init; }
}