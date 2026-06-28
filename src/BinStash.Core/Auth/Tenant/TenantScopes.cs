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

namespace BinStash.Core.Auth.Tenant;

/// <summary>
/// Scope strings carried by a machine (service-account) API key, and the mapping from those
/// scopes to <see cref="TenantPermission"/>. Machine subjects are authorized purely by their
/// key's scopes (least privilege), unlike users which are authorized by tenant membership/roles.
/// </summary>
public static class TenantScopes
{
    /// <summary>Read/use tenant resources: list repositories, run ingest sessions, publish releases.</summary>
    public const string Member = "tenant:member";

    /// <summary>Administer the tenant: create repositories, manage tenant configuration.</summary>
    public const string Admin = "tenant:admin";

    /// <summary>Billing administration for the tenant.</summary>
    public const string Billing = "tenant:billing";

    /// <summary>All scopes a caller may grant when minting a service-account key.</summary>
    public static readonly string[] All = [Member, Admin, Billing];

    /// <summary>
    /// Whether the given key scopes satisfy the required tenant permission. <see cref="Admin"/>
    /// implies <see cref="Member"/>.
    /// </summary>
    public static bool Satisfies(ICollection<string> scopes, TenantPermission permission) =>
        permission switch
        {
            TenantPermission.Member => scopes.Contains(Member) || scopes.Contains(Admin),
            TenantPermission.Admin => scopes.Contains(Admin),
            TenantPermission.BillingAdmin => scopes.Contains(Billing) || scopes.Contains(Admin),
            _ => false
        };
}
