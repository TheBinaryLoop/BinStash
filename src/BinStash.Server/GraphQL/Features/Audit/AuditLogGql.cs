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

namespace BinStash.Server.GraphQL.Features.Audit;

/// <summary>Projection of <see cref="AuditLogEntry"/> exposed to clients.</summary>
public sealed class AuditLogEntryGql
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public AuditActorType ActorType { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorDisplay { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? TargetName { get; set; }
    public AuditOutcome Outcome { get; set; }
    public string? IpAddress { get; set; }

    /// <summary>
    /// Raw JSON detail. Deliberately a string rather than a structured type: the shape is
    /// per-action and the UI renders it as key/value pairs.
    /// </summary>
    public string? Metadata { get; set; }
}
