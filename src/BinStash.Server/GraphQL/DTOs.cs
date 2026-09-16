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

/// <summary>
/// Per-repository totals, for answering "which repository is using the quota?" without
/// paging through every release.
/// </summary>
/// <remarks>
/// Logical bytes only, deliberately. This is tenant-facing, and deduplicated or compressed
/// figures for a repository depend on what other tenants sharing the chunk store have stored.
/// Logical size is both leak-free and the quantity the workspace is billed on.
/// </remarks>
public sealed class RepositoryMetricsGql
{
    public required int ReleaseCount { get; init; }
    public required long TotalLogicalBytes { get; init; }

    /// <summary>When the most recent release was published, or null if there are none.</summary>
    public DateTimeOffset? LastReleaseAt { get; init; }

    /// <summary>A repository with no releases — a known zero rather than an absent value.</summary>
    public static RepositoryMetricsGql Empty { get; } = new()
    {
        ReleaseCount = 0,
        TotalLogicalBytes = 0,
        LastReleaseAt = null
    };
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

    /// <summary>
    /// Publisher-supplied metadata (the CLI's <c>-p key=value</c>).
    /// </summary>
    /// <remarks>
    /// A typed list rather than the <c>Any</c> scalar. Any's result coercion rejects both
    /// JsonDocument and plain dictionaries, and it gives generated clients an untyped
    /// `unknown` — whereas these are a flat string map in practice.
    /// </remarks>
    public List<ReleaseCustomPropertyGql>? CustomProperties { get; init; }
}

/// <summary>
/// One build target of a release — what a client actually downloads.
/// </summary>
/// <remarks>
/// Every release has at least one, so a client can render the list unconditionally rather than
/// branching on whether a release "uses" targets. A release that declares none has exactly one
/// variant, keyed <c>default</c>.
/// </remarks>
public sealed class ReleaseVariantGql
{
    public required Guid Id { get; init; }

    /// <summary>The canonical target key, unique within the release.</summary>
    public required string TargetKey { get; init; }

    /// <summary>True when this is the unnamed target of a release that declares none.</summary>
    public required bool IsDefault { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Logical size of this target's payload, or null if it was never measured.</summary>
    public ulong? TotalLogicalBytes { get; init; }

    public int? FilesInVariant { get; init; }
}

public sealed class ReleaseCustomPropertyGql
{
    public required string Key { get; init; }

    /// <summary>Non-scalar values are rendered back to compact JSON.</summary>
    public required string Value { get; init; }
}

public sealed class ReleaseMetricsGql
{
    // Describes THIS release only.
    //
    // Metrics derived from what was already in the chunk store are deliberately absent:
    // new chunks, newly-added unique/compressed bytes, dedup ratios, saved bytes and
    // "new data percent". Tenants share a chunk store, so those numbers are a function of
    // OTHER tenants' content — publishing a release and reading back "0% new data" tells
    // you another tenant already stored byte-identical content. That is a cross-tenant
    // side channel, and it is also not what a tenant is billed on (billing is on
    // undeduplicated, uncompressed logical bytes).
    //
    // These figures remain meaningful instance-wide and belong on instance-admin surfaces.
    public required int ChunksInRelease { get; set; }

    // Full logical size of the release as users see it. This is the billable quantity.
    public required ulong TotalLogicalBytes { get; set; }

    // Total metadata bytes for full release package
    public required int MetaBytesFull { get; set; }

    public required int ComponentsInRelease { get; set; }
    public required int FilesInRelease { get; set; }
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

    /// <summary>
    /// How the health probe tests this store — <c>ReadWrite</c> or <c>ReadOnly</c>.
    /// </summary>
    /// <remarks>
    /// This governs the <em>probe</em>, not the store: a <c>ReadWrite</c> probe writes a small
    /// file into <c>.health/</c> every 15 seconds, reads it back and deletes it, while a
    /// <c>ReadOnly</c> probe reports free space and skips that round trip. Ingest is never gated
    /// on it — a store set to <c>ReadOnly</c> still accepts uploads.
    ///
    /// <para>
    /// Worth surfacing because the two answer different questions: under a <c>ReadOnly</c> probe
    /// a healthy verdict means "the path exists and has space", not "writes to it work".
    /// </para>
    /// </remarks>
    public required string ProbeMode { get; init; }

    /// <summary>
    /// Free space below which the store stops accepting writes, or null for no floor.
    /// </summary>
    public long? MinFreeBytes { get; init; }
}

/// <summary>
/// What a chunk store holds and what it costs on disk.
/// </summary>
/// <remarks>
/// Instance-admin only, and that is a boundary rather than an oversight. Tenants share a chunk
/// store, so deduplication and compression figures here are a function of every tenant's content
/// at once: publishing them per workspace would leak what other workspaces store. Tenant-facing
/// surfaces report undeduplicated logical bytes, which is also what they are billed on.
///
/// <para>
/// Everything except <see cref="TotalChunks"/> comes from the most recent hourly snapshot, so it
/// is accurate as of <see cref="CollectedAt"/> rather than as of the request. Recomputing it
/// live would mean walking the whole store on a page load.
/// </para>
/// </remarks>
public sealed class ChunkStoreStatsGql
{
    public required int TotalChunks { get; init; }

    /// <summary>
    /// When the snapshot these figures come from was taken. Null means no snapshot has been
    /// collected yet — a store younger than the hourly collection interval — and every field
    /// below is then zero rather than unknown, which is why the timestamp is worth rendering.
    /// </summary>
    public DateTimeOffset? CollectedAt { get; init; }

    // --- Counts ---
    public long ChunkCount { get; init; }
    public long FileDefinitionCount { get; init; }
    public long ReleaseCount { get; init; }

    // --- Physical footprint ---
    public long ChunkPackBytes { get; init; }
    public long FileDefinitionPackBytes { get; init; }
    public long ReleasePackageBytes { get; init; }
    public long IndexBytes { get; init; }

    /// <summary>Everything the store occupies on the volume, including indexes and metadata.</summary>
    public long PhysicalBytesTotal { get; init; }

    // --- Logical sizes ---

    /// <summary>Summed size of every release as published — what tenants are billed on.</summary>
    public long TotalLogicalBytes { get; init; }
    public long UniqueFileBytes { get; init; }
    public long UniqueLogicalChunkBytes { get; init; }
    public long UniqueCompressedChunkBytes { get; init; }
    public long ReferencedUniqueChunkBytes { get; init; }

    // --- Efficiency ---
    public double CompressionRatio { get; init; }
    public double DeduplicationRatio { get; init; }

    /// <summary>Logical bytes stored per physical byte used — the headline "what is this worth" number.</summary>
    public double EffectiveStorageRatio { get; init; }

    public long CompressionSavedBytes { get; init; }
    public long DeduplicationSavedBytes { get; init; }

    // --- On-disk layout ---
    public int ChunkPackFileCount { get; init; }
    public int FileDefinitionPackFileCount { get; init; }
    public int ReleasePackageFileCount { get; init; }
    public int IndexFileCount { get; init; }

    // --- Volume ---

    /// <summary>
    /// Capacity of the volume the store sits on. Zero when the backend cannot report it.
    /// Surfaced because a chunk store filling its disk fails ingest for every tenant on it, and
    /// that is the one number an instance admin needs <em>before</em> it happens.
    /// </summary>
    public long VolumeTotalBytes { get; init; }
    public long VolumeFreeBytes { get; init; }

    // --- Averages ---
    public long AvgChunkSize { get; init; }
    public long AvgCompressedChunkSize { get; init; }
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

    /// <summary>Progress data for <c>ChunkStoreGc</c> jobs.</summary>
    public GcJobProgressGql? GcProgress { get; init; }
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
    public required int ReleaseCount { get; init; }
    public required int ChunkStoreCount { get; init; }

    /// <summary>
    /// Logical bytes across every workspace — the sum of what tenants are billed on.
    /// </summary>
    public required long TotalLogicalBytes { get; init; }

    /// <summary>
    /// What those releases actually occupy across all chunk stores, from the latest snapshot of
    /// each. Instance-wide, so unlike the per-tenant view it can be set against
    /// <see cref="TotalLogicalBytes"/> without leaking one tenant's content to another.
    /// </summary>
    public required long TotalPhysicalBytes { get; init; }

    /// <summary>
    /// Free space on the tightest chunk-store volume, or null when no store has reported one.
    /// The instance's real storage headroom is its worst store, not its total.
    /// </summary>
    public long? MinVolumeFreeBytes { get; init; }

    /// <summary>Name of the store <see cref="MinVolumeFreeBytes"/> refers to.</summary>
    public string? MinVolumeFreeChunkStoreName { get; init; }
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

/// <summary>
/// The instance's unattended chunk-store collection settings.
/// </summary>
/// <remarks>
/// <see cref="RetentionHours"/> is not part of the schedule — it applies to every run — but it is
/// surfaced alongside it because it is the setting that decides how long "collected" stays
/// reversible, and reading a cadence without it tells you when bytes get quarantined but not when
/// they are actually gone.
/// </remarks>
public sealed class GcConfigGql
{
    public required bool Enabled { get; init; }
    public required double IntervalHours { get; init; }

    /// <summary>Start of the daily window in which runs may begin, in whole UTC hours. Null means any hour.</summary>
    public int? WindowStartHourUtc { get; init; }

    /// <summary>End of that window, exclusive. Smaller than the start means the window crosses midnight.</summary>
    public int? WindowEndHourUtc { get; init; }

    public required bool DryRun { get; init; }
    public required bool SkipReclaim { get; init; }
    public required double RetentionHours { get; init; }

    /// <summary>
    /// When the scheduler would next consider a store that is idle right now — null when the
    /// schedule is disabled. An estimate: the run still has to clear the per-store interval and
    /// find no other maintenance job in flight.
    /// </summary>
    public DateTimeOffset? NextEligibleAt { get; init; }
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

/// <summary>
/// A workspace a given repository could be moved into: one the caller administers that is
/// configured for the repository's existing chunk store.
/// </summary>
public sealed class RepositoryMoveTargetGql
{
    public required Guid TenantId { get; init; }
    public required string TenantName { get; init; }
    public required string TenantSlug { get; init; }

    /// <summary>The storage class the repository would be labelled with in that workspace.</summary>
    public required string StorageClassName { get; init; }

    /// <summary>True when that workspace already holds a repository of the same name.</summary>
    public required bool NameConflict { get; init; }
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

/// <summary>
/// Progress of an online chunk-store garbage-collection run.
///
/// <para>
/// Quarantined and reclaimed are deliberately separate numbers. Quarantining is reversible and
/// happens in the run that finds the content unreachable; reclaiming destroys bytes and happens
/// only once the retention window has passed, usually in a later run. A run that reports a large
/// quarantine and no reclaim has not failed — it has not waited long enough yet.
/// </para>
/// </summary>
public sealed class GcJobProgressGql
{
    /// <summary>Current phase: Snapshot, Mark, Sweep, Reclaim or Completed.</summary>
    public required string Phase { get; init; }

    public int TotalBuckets { get; init; }
    public int ProcessedBuckets { get; init; }
    public int TotalReleases { get; init; }
    public int MarkedReleases { get; init; }

    /// <summary>
    /// File definitions expanded to their chunks. This is the long half of marking and is not
    /// proportional to the release count, so it carries its own progress.
    /// </summary>
    public long ResolvedFileDefinitions { get; init; }

    public int ProcessedFileDefinitionGroups { get; init; }
    public int TotalFileDefinitionGroups { get; init; }

    /// <summary>Distinct objects found reachable from the releases on this store.</summary>
    public long ReachableObjects { get; init; }

    /// <summary>Objects hidden from deduplication by this run. Still recoverable.</summary>
    public long QuarantinedObjects { get; init; }
    public long QuarantinedBytes { get; init; }

    /// <summary>Objects whose bytes this run destroyed.</summary>
    public long ReclaimedObjects { get; init; }

    /// <summary>
    /// Size of the dead pack entries this run dropped. Not the same as space returned to the
    /// volume — see <see cref="PackBytesDeleted"/> for that.
    /// </summary>
    public long ReclaimedBytes { get; init; }

    public int PacksCompacted { get; init; }
    public int PacksDeleted { get; init; }

    /// <summary>Bytes actually returned to the filesystem by unlinking superseded pack files.</summary>
    public long PackBytesDeleted { get; init; }

    /// <summary>
    /// Quarantined objects an ingest needed again and took back. Persistently non-zero means the
    /// retention window is short relative to how long ingests run.
    /// </summary>
    public long ResurrectedObjects { get; init; }

    public bool DryRun { get; init; }
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