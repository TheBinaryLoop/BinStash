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

using BinStash.Core.Auditing;
using BinStash.Core.Entities;
using BinStash.Server.Billing;

namespace BinStash.Server.Services.Usage;

/// <summary>
/// Decides whether a tenant may ingest, store more, or download, and records the refusal.
/// </summary>
/// <remarks>
/// The billing boundary defines four limits. Before this existed only <c>IsIngestAllowed</c> had
/// an enforcement site, so a tenant over plan could still download without bound and still grow
/// its stored footprint — the other three were computed, surfaced on the usage page, and then
/// ignored. This is the single place that turns them into an answer.
///
/// <para>
/// Nothing about <em>what</em> a plan permits lives here. That is the billing provider's job, and
/// under the AGPL default it permits everything. This only asks the question and enforces the
/// answer, which is what keeps the open-source path fully functional while leaving a commercial
/// plugin somewhere to say no.
/// </para>
/// </remarks>
public sealed class TenantQuotaGuard(BillingLimitsCache limitsCache, TenantUsageService usage, IAuditLogWriter audit)
{
    /// <summary>Whether the tenant may open a new ingest session.</summary>
    public async Task<QuotaDecision> CheckIngestAsync(Guid tenantId, CancellationToken ct = default)
    {
        var limits = await limitsCache.GetCachedLimitsAsync(tenantId, ct);

        if (!limits.IsIngestAllowed)
            return await DenyAsync(tenantId, "ingest", "Your plan does not allow further ingest.", ct);

        if (!limits.IsStorageAllowed)
            return await DenyAsync(tenantId, "storage", "Your plan does not allow further storage.", ct);

        // Checked here as well as at finalize so a client learns it is out of room before it
        // spends an upload finding out. This reading is necessarily stale by the time the release
        // lands, which is exactly why finalize checks again against the real size.
        if (IsLimited(limits.MaxStorageBytes))
        {
            var used = await usage.GetBillableBytesAsync(tenantId, ct);
            if (used >= limits.MaxStorageBytes)
            {
                return await DenyAsync(tenantId, "storage", $"Storage quota reached ({used:N0} of {limits.MaxStorageBytes:N0} bytes used).", ct);
            }
        }

        return QuotaDecision.Allowed;
    }

    /// <summary>
    /// Whether a release of <paramref name="additionalBytes"/> logical bytes may be committed.
    /// </summary>
    /// <remarks>
    /// The authoritative storage check: finalize is the first moment the release's real size is
    /// known. Refusing here does waste an upload the client has already paid for in bandwidth —
    /// but the alternative is letting the quota be exceeded by the size of any single release,
    /// which makes it not a quota. Content already written is left for garbage collection.
    /// </remarks>
    public async Task<QuotaDecision> CheckStorageCommitAsync(Guid tenantId, long additionalBytes, CancellationToken ct = default)
    {
        var limits = await limitsCache.GetCachedLimitsAsync(tenantId, ct);

        if (!limits.IsStorageAllowed)
            return await DenyAsync(tenantId, "storage", "Your plan does not allow further storage.", ct);

        if (!IsLimited(limits.MaxStorageBytes))
            return QuotaDecision.Allowed;

        var used = await usage.GetBillableBytesAsync(tenantId, ct);
        if (used + additionalBytes <= limits.MaxStorageBytes)
            return QuotaDecision.Allowed;

        return await DenyAsync(tenantId, "storage", $"This release would put the tenant over its storage quota ({used:N0} bytes used plus {additionalBytes:N0} new exceeds {limits.MaxStorageBytes:N0}).", ct);
    }

    /// <summary>Whether the tenant may be served a download.</summary>
    public async Task<QuotaDecision> CheckEgressAsync(Guid tenantId, CancellationToken ct = default)
    {
        var limits = await limitsCache.GetCachedLimitsAsync(tenantId, ct);

        return limits.IsEgressAllowed
            ? QuotaDecision.Allowed
            : await DenyAsync(tenantId, "egress", "Your plan does not allow further downloads.", ct);
    }

    /// <summary>
    /// The NoOp provider reports <see cref="long.MaxValue"/>, which means "no plan limit" rather
    /// than a ceiling that happens to be very high. Zero is treated the same way: a limit of zero
    /// would make the tenant unusable, so it reads as unset.
    /// </summary>
    private static bool IsLimited(long maxStorageBytes) => maxStorageBytes is > 0 and < long.MaxValue;

    private async Task<QuotaDecision> DenyAsync(Guid tenantId, string resource, string detail, CancellationToken ct)
    {
        await audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.QuotaExceeded,
            TenantId = tenantId,
            Outcome = AuditOutcome.Denied,
            TargetType = "Quota",
            TargetName = resource,
            Metadata = new Dictionary<string, object?>
            {
                ["resource"] = resource,
                ["detail"] = detail
            }
        }, ct);

        return new QuotaDecision(false, detail);
    }
}

/// <summary>The outcome of a quota check. <c>402 Payment Required</c> when refused.</summary>
public sealed record QuotaDecision(bool IsAllowed, string? Detail)
{
    public static readonly QuotaDecision Allowed = new(true, null);

    /// <summary>The refusal as a problem response. Only valid when <see cref="IsAllowed"/> is false.</summary>
    public IResult ToProblem() => Results.Problem(statusCode: StatusCodes.Status402PaymentRequired, title: "Quota exceeded", detail: Detail);
}
