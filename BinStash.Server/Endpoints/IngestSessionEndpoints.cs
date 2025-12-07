// Copyright (C) 2025  Lukas Eßmann
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

using BinStash.Contracts.Ingest;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;

namespace BinStash.Server.Endpoints;

public static class IngestSessionEndpoints
{
    public static RouteGroupBuilder MapIngestSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingest")
            .WithTags("Ingest Sessions")
            .RequireAuthorization();
        
        group.MapPost("/sessions", CreateIngestSessionAsync)
            .WithName("CreateIngestSession")
            .Produces<CreateIngestSessionResponse>(201)
            .Produces(400)
            .Produces(401)
            .Produces(404);
        
        /*group.MapPost("/start", IngestSessionHandlers.StartIngestSession);
        group.MapPost("/{sessionId}/finish", IngestSessionHandlers.FinishIngestSession);
        group.MapPost("/{sessionId}/abort", IngestSessionHandlers.AbortIngestSession);
        group.MapGet("/{sessionId}", IngestSessionHandlers.GetIngestSessionInfo);
        group.MapGet("/{sessionId}/chunks/{chunkChecksum}", IngestSessionHandlers.CheckChunkExists);
        group.MapPut("/{sessionId}/chunks/{chunkChecksum}", IngestSessionHandlers.UploadChunk);
        group.MapPost("/{sessionId}/release", IngestSessionHandlers.UploadReleaseDefinition);*/
        return group;
    }

    public static async Task<IResult> CreateIngestSessionAsync(CreateIngestSessionRequest req, BinStashDbContext db)
    {
        // If we have authentication, we can link the session to a user. We could also enforce per-user limits.
        // For now, we just create a session with a random ID and a 30-minute expiry.

        var repo = await db.Repositories.FindAsync(req.RepoId);
        if (repo is null)
            return Results.Json(new { error = "No repo found" }, statusCode: 404);
        
        var session = new IngestSession
        {
            Id = Guid.NewGuid(),
            RepoId = repo.Id,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            State = IngestSessionState.Created
        };
        
        db.IngestSessions.Add(session);
        await db.SaveChangesAsync();
        
        // return new session ID and expiry time (30 minutes from now)
        return Results.Json(new CreateIngestSessionResponse(session.Id, session.ExpiresAt), statusCode: 201);
    }
}