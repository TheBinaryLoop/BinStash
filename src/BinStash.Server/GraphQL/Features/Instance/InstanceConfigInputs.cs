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

namespace BinStash.Server.GraphQL.Features.Instance;

// Config inputs mirror the config output shapes. Any field left null is not written.
// A sensitive field (Brevo.ApiKey / Smtp.Password) whose value equals the mask "****"
// is treated as "keep the existing value".

public sealed class SetEmailConfigInput
{
    public string? Provider { get; init; }
    public EmailSharedConfigInput? Shared { get; init; }
    public EmailBrevoConfigInput? Brevo { get; init; }
    public EmailSmtpConfigInput? Smtp { get; init; }
}

public sealed class EmailSharedConfigInput
{
    public string? FromEmail { get; init; }
    public string? SupportEmail { get; init; }
}

public sealed class EmailBrevoConfigInput
{
    public string? ApiKey { get; init; }
}

public sealed class EmailSmtpConfigInput
{
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Security { get; init; }
}

public sealed class SetTenancyConfigInput
{
    public string? Mode { get; init; }
    public string? DefaultTenantId { get; init; }
}

public sealed class SetDomainConfigInput
{
    public string? BaseUrl { get; init; }
}

/// <summary>
/// Changes to unattended chunk-store collection. Every field is optional; omitting one leaves it
/// as configured.
/// </summary>
/// <remarks>
/// The window is a pair: clearing one half means "no window". It is expressed in whole UTC hours
/// rather than the viewer's local time because the instance may serve admins in several zones,
/// and a schedule that means different things to two admins is worse than one they both have to
/// convert.
/// </remarks>
public sealed class SetGcConfigInput
{
    public bool? Enabled { get; init; }
    public double? IntervalHours { get; init; }
    public int? WindowStartHourUtc { get; init; }
    public int? WindowEndHourUtc { get; init; }

    /// <summary>Pass true to clear the window entirely, since null means "leave unchanged".</summary>
    public bool? ClearWindow { get; init; }

    public bool? DryRun { get; init; }
    public bool? SkipReclaim { get; init; }

    /// <summary>
    /// How long quarantined content stays recoverable, in hours. Applies to every run, scheduled
    /// or manual, that does not override it explicitly.
    /// </summary>
    public double? RetentionHours { get; init; }
}
