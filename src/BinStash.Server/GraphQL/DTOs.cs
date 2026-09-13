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
    public ChunkStoreChunkerGql? Chunker { get; init; }
    public ChunkStoreBackendSettingsGql? BackendSettings { get; init; }
}

public sealed class ChunkStoreStatsGql
{
    public required int TotalChunks { get; init; }
}

public sealed class ChunkStoreTypeInfoGql
{
    public required string Name { get; init; }
    public required int Value { get; init; }
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

/// <summary>
/// GraphQL representation of a background job (rebuild or upgrade).
/// </summary>
public sealed class BackgroundJobGql
{
    public required Guid Id { get; init; }
    public required string JobType { get; init; }
    public required string Status { get; init; }
    public Guid ChunkStoreId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? ErrorDetails { get; init; }

    /// <summary>Progress data for <c>ChunkStoreRebuild</c> jobs.</summary>
    public RebuildJobProgressGql? RebuildProgress { get; init; }

    /// <summary>Progress data for <c>ReleaseUpgrade</c> jobs.</summary>
    public UpgradeJobProgressGql? UpgradeProgress { get; init; }
}

public sealed class SendTestEmailResultGql
{
    public required bool Success { get; init; }
    public string? ProviderError { get; init; }
}

public sealed class InstanceStatsGql
{
    public required int UserCount { get; init; }
    public required int TenantCount { get; init; }
    public required int RepositoryCount { get; init; }
}

public sealed class EmailConfigGql
{
    public string? Provider { get; init; }
    public EmailSharedConfigGql? Shared { get; init; }
    public EmailBrevoConfigGql? Brevo { get; init; }
    public EmailSmtpConfigGql? Smtp { get; init; }
}

public sealed class EmailSharedConfigGql
{
    public string? FromEmail { get; init; }
    public string? SupportEmail { get; init; }
}

public sealed class EmailBrevoConfigGql
{
    public string? ApiKey { get; init; }
}

public sealed class EmailSmtpConfigGql
{
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Security { get; init; }
}

public sealed class TenancyConfigGql
{
    public string? Mode { get; init; }
    public string? DefaultTenantId { get; init; }
}

public sealed class DomainConfigGql
{
    public string? BaseUrl { get; init; }
}

public sealed class StorageClassDetailsGql
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required bool IsDeprecated { get; init; }
}

public sealed class StorageClassDefaultMappingGql
{
    public required string StorageClassName { get; init; }
    public required Guid ChunkStoreId { get; init; }
    public required bool IsDefault { get; init; }
    public required bool IsEnabled { get; init; }
}

public sealed class RepositoryConfigGql
{
    public required RepositoryDedupeConfigGql DedupeConfig { get; init; }
}

public sealed class RepositoryDedupeConfigGql
{
    public required string Chunker { get; init; }
    public int? MinChunkSize { get; init; }
    public int? AvgChunkSize { get; init; }
    public int? MaxChunkSize { get; init; }
    public int? ShiftCount { get; init; }
    public int? BoundaryCheckBytes { get; init; }
}

public sealed class RepositoryAccessGql
{
    public required short SubjectType { get; init; }
    public required Guid SubjectId { get; init; }
    public required string Role { get; init; }
    public required DateTimeOffset GrantedAt { get; init; }
}

public sealed class ApiKeyInfoGql
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public required bool IsActive { get; init; }
    public required IReadOnlyList<string> Scopes { get; init; }
}

public sealed class CreateApiKeyResultGql
{
    public required string DisplayName { get; init; }
    public required string Key { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class TenantMemberGql
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public DateTimeOffset? JoinedAt { get; init; }
}

public sealed class TenantStorageClassGql
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required bool IsDefault { get; init; }
}

public sealed class TenantInvitationPreviewGql
{
    public required Guid TenantId { get; init; }
    public required string TenantName { get; init; }
    public string? TenantSlug { get; init; }
    public required string Role { get; init; }
    public string? InvitedEmail { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed class RebuildJobProgressGql
{
    public int TotalBuckets { get; init; }
    public int ProcessedBuckets { get; init; }
    public int FailedBuckets { get; init; }
}

public sealed class UpgradeJobProgressGql
{
    public byte TargetSerializerVersion { get; init; }
    public int TotalReleases { get; init; }
    public int ProcessedReleases { get; init; }
    public int FailedReleases { get; init; }
    public int SkippedReleases { get; init; }
    public long BytesSaved { get; init; }
    public long BytesGrown { get; init; }
}