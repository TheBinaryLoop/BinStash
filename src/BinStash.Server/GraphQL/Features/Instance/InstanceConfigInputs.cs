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
