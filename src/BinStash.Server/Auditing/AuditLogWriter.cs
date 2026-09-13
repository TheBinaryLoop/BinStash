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

using System.Security.Claims;
using System.Text.Json;
using BinStash.Core.Auditing;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Context;

namespace BinStash.Server.Auditing;

/// <summary>
/// Persists audit entries, deriving actor/tenant/client details from the ambient request so call
/// sites only describe <em>what</em> happened.
/// </summary>
/// <remarks>
/// Writes are best-effort by design: an audit failure is logged but never propagated, because
/// failing the caller's mutation after it already succeeded would be strictly worse than a gap in
/// the trail.
///
/// The entry is written through a DbContext resolved from a fresh DI scope rather than the
/// request-scoped one. Sharing the caller's context would mean this SaveChangesAsync also commits
/// whatever the caller has staged but not yet saved, and would tie the audit record to the
/// caller's transaction. The trail has to be independent of the thing it records.
/// </remarks>
public sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AuditLogWriter> _logger;

    public AuditLogWriter(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenantContext,
        ILogger<AuditLogWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task WriteAsync(AuditEntryDraft draft, CancellationToken ct = default)
    {
        try
        {
            var http = _httpContextAccessor.HttpContext;
            var user = http?.User;

            var (actorType, actorId, actorDisplay) = draft.SystemActor
                ? (AuditActorType.System, (Guid?)null, draft.SystemActorDisplay ?? "system")
                : ResolveActor(user);

            var entry = new AuditLogEntry
            {
                Id = Guid.CreateVersion7(),
                TenantId = draft.InstanceScoped
                    ? null
                    : draft.TenantId ?? (_tenantContext.IsResolved ? _tenantContext.TenantId : null),
                OccurredAt = DateTimeOffset.UtcNow,
                Action = draft.Action,
                ActorType = actorType,
                ActorId = actorId,
                ActorDisplay = actorDisplay,
                TargetType = draft.TargetType,
                TargetId = draft.TargetId,
                TargetName = Truncate(draft.TargetName, 512),
                Outcome = draft.Outcome,
                IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Truncate(http?.Request.Headers.UserAgent.ToString(), 512),
                Metadata = draft.Metadata is null or { Count: 0 }
                    ? null
                    : JsonSerializer.Serialize(draft.Metadata)
            };

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();

            db.AuditLogEntries.Add(entry);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit entry for action {Action}.", draft.Action);
        }
    }

    private static (AuditActorType Type, Guid? Id, string? Display) ResolveActor(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return (AuditActorType.Anonymous, null, null);

        Guid.TryParse(
            user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            out var subjectId);

        var id = subjectId == Guid.Empty ? (Guid?)null : subjectId;

        if (user.FindFirstValue("auth_type") == "machine")
        {
            var name = user.FindFirstValue("client_name")
                       ?? user.FindFirstValue(ClaimTypes.Name)
                       ?? "service account";
            return (AuditActorType.ServiceAccount, id, name);
        }

        var display = user.FindFirstValue(ClaimTypes.Email)
                      ?? user.FindFirstValue("email")
                      ?? user.FindFirstValue(ClaimTypes.Name);

        return (AuditActorType.User, id, display);
    }

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
}
