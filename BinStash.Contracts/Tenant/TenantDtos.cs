// Copyright (C) 2025  Lukas Eßmann
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

namespace BinStash.Contracts.Tenant;

public record CreateTenantDto(string Name, string Slug);
public record UpdateTenantDto(string Name, string Slug);
public record TenantInfoDto(Guid TenantId, string Name, string Slug, DateTimeOffset JoinedAt, string Role);
public record UpdateTenantMemberRolesDto(List<string> Roles);
public record TenantMemberDto(Guid TenantId, Guid UserId, List<string> Roles);
public record InviteTenantMemberDto(string Email, List<string> Roles);