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

namespace BinStash.Core.Entities;

/// <summary>
/// An append-only record of a security- or configuration-relevant action.
/// </summary>
/// <remarks>
/// Actor and target are deliberately denormalised (<see cref="ActorDisplay"/>,
/// <see cref="TargetName"/>) rather than being foreign keys. An audit trail has to stay readable
/// after the user, service account or repository it refers to has been deleted, which a join
/// cannot guarantee. <see cref="ActorId"/>/<see cref="TargetId"/> are kept for correlation only.
/// </remarks>
public class AuditLogEntry
{
    public Guid Id { get; set; }

    /// <summary>Owning tenant, or <c>null</c> for instance-scoped events (e.g. SMTP reconfigured).</summary>
    public Guid? TenantId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Dotted action key, e.g. <c>repository.created</c>. See <see cref="AuditActions"/>.</summary>
    public string Action { get; set; } = string.Empty;

    public AuditActorType ActorType { get; set; } = AuditActorType.User;

    public Guid? ActorId { get; set; }

    /// <summary>Human-readable actor label captured at write time (email, service-account name, "system").</summary>
    public string? ActorDisplay { get; set; }

    /// <summary>Type of the affected object, e.g. <c>Repository</c>.</summary>
    public string? TargetType { get; set; }

    public string? TargetId { get; set; }

    /// <summary>Human-readable target label captured at write time.</summary>
    public string? TargetName { get; set; }

    public AuditOutcome Outcome { get; set; } = AuditOutcome.Success;

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Free-form JSON detail (changed fields, roles granted, …). Never store secrets here.</summary>
    public string? Metadata { get; set; }
}

public enum AuditActorType
{
    User = 0,
    ServiceAccount = 1,
    System = 2,
    Anonymous = 3
}

public enum AuditOutcome
{
    Success = 0,
    Denied = 1,
    Failed = 2
}

/// <summary>Well-known <see cref="AuditLogEntry.Action"/> values.</summary>
public static class AuditActions
{
    /// <summary>
    /// A release was published. This is the provenance record for the artifact store —
    /// who put what into it — and it is written from the ingest path, not from GraphQL.
    /// </summary>
    public const string ReleasePublished = "release.published";

    /// <summary>
    /// A release package (or a component/file slice of one) was downloaded. This is the
    /// egress record, and the same event the billing plugin meters.
    /// </summary>
    public const string ReleaseDownloaded = "release.downloaded";

    public const string RepositoryCreated = "repository.created";
    public const string RepositoryUpdated = "repository.updated";

    /// <summary>
    /// A repository changed tenant. Written to both the source and the target tenant, because the
    /// log is tenant-scoped and a repository leaving matters as much as one arriving.
    /// </summary>
    public const string RepositoryMoved = "repository.moved";

    public const string RepositoryAccessGranted = "repository.access.granted";
    public const string RepositoryAccessRevoked = "repository.access.revoked";

    public const string ServiceAccountCreated = "service_account.created";
    public const string ServiceAccountUpdated = "service_account.updated";
    public const string ServiceAccountDeleted = "service_account.deleted";
    public const string ApiKeyCreated = "service_account.api_key.created";
    public const string ApiKeyDeleted = "service_account.api_key.deleted";

    public const string TenantCreated = "tenant.created";
    public const string TenantUpdated = "tenant.updated";
    public const string TenantDeleted = "tenant.deleted";
    public const string MemberInvited = "tenant.member.invited";
    public const string MemberRolesUpdated = "tenant.member.roles_updated";
    public const string MemberRemoved = "tenant.member.removed";
    public const string MemberLeft = "tenant.member.left";
    public const string InvitationAccepted = "tenant.invitation.accepted";

    public const string ChunkStoreCreated = "chunk_store.created";
    public const string ChunkStoreRebuildStarted = "chunk_store.rebuild.started";
    public const string ChunkStoreGcStarted = "chunk_store.gc.started";
    public const string ChunkStoreUpgradeStarted = "chunk_store.upgrade.started";

    public const string InstanceEmailConfigChanged = "instance.email_config.changed";
    public const string InstanceTenancyConfigChanged = "instance.tenancy_config.changed";
    public const string InstanceDomainConfigChanged = "instance.domain_config.changed";
    public const string InstanceStorageDefaultsChanged = "instance.storage_defaults.changed";
}
