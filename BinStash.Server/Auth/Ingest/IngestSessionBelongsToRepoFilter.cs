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

using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Auth.Ingest;

public class IngestSessionBelongsToRepoFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!http.Request.RouteValues.TryGetGuidValue("repoId", out var repoId) ||
            !http.Request.RouteValues.TryGetGuidValue("sessionId", out var sessionId))
            return Results.BadRequest();

        var db = http.RequestServices.GetRequiredService<BinStashDbContext>();

        // Validate session belongs to repo and is in a valid state (repo/tenant permissions get checked elsewhere)
        var ok = await db.IngestSessions // whatever your entity is called
            .AnyAsync(s =>
                s.Id == sessionId && s.RepoId == repoId &&
                (s.State == IngestSessionState.Created || s.State == IngestSessionState.InProgress) &&
                s.ExpiresAt > DateTimeOffset.UtcNow);

        if (!ok)
            return Results.BadRequest("Invalid ingest session."); // Check if we want to give this information to the user

        return await next(context);
    }
}