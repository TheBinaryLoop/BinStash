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

using BinStash.Core.Entities;

namespace BinStash.Core.Auditing;

/// <summary>
/// Records audit entries. Implementations must never throw into the calling request: a failed
/// audit write must not roll back the action it describes, it must be logged and swallowed.
/// </summary>
public interface IAuditLogWriter
{
    Task WriteAsync(AuditEntryDraft entry, CancellationToken ct = default);
}

/// <summary>
/// What a call site supplies. Actor, tenant, IP and user agent are filled in by the
/// implementation from the ambient request context so call sites cannot get them wrong.
/// </summary>
public sealed record AuditEntryDraft
{
    public required string Action { get; init; }

    public string? TargetType { get; init; }

    public string? TargetId { get; init; }

    public string? TargetName { get; init; }

    public AuditOutcome Outcome { get; init; } = AuditOutcome.Success;

    /// <summary>Overrides the ambient tenant. Use for instance-scoped events (pass <see cref="Guid.Empty"/> is NOT valid; leave null).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>When true the entry is recorded without a tenant, even if one is resolved.</summary>
    public bool InstanceScoped { get; init; }

    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}
