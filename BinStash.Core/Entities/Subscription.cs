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

public class Subscription
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public BillingMode BillingMode { get; set; } = BillingMode.UsageBased;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public double MinimumMonthlyFee { get; set; }
    public string Currency { get; set; } = null!;
    
    public Tenant Tenant { get; set; } = null!;
}

public enum BillingMode
{
    UsageBased = 1,
    Contract = 2
}

public enum SubscriptionStatus
{
    Trialing = 1,
    Active = 2,
    Suspended = 3,
    Expired = 4,
    Canceled = 5
}