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

using BinStash.Core.Auth.Repository;
using BinStash.Core.Compression;
using BinStash.Infrastructure.Data;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

/// <summary>
/// Read-only lookups that let a client work out what an upload <em>would</em> cost before
/// committing to it (<c>BinStash.Cli analyze</c>).
/// </summary>
/// <remarks>
/// The equivalent lookup already exists on the ingest path, but only inside a session. Reusing it
/// for analysis would mean opening a session purely to ask a question: it consumes the write
/// admission check, counts against the session rate limit, leaves a row behind, and — now that
/// storage quota is enforced at session creation — would fail with 402 for exactly the workspace
/// most in need of finding out how much a release would cost it.
///
/// <para>
/// Write permission is required even though nothing is written. Answering "does this store
/// already hold this chunk" is an oracle over content the store may hold on behalf of another
/// tenant, since deduplication is per chunk store rather than per tenant. That oracle is already
/// reachable by anyone who can ingest, and this deliberately does not widen it beyond them.
/// </para>
/// </remarks>
public static class RepositoryAnalysisEndpoints
{
    public static RouteGroupBuilder MapRepositoryAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants/{tenantId:guid}/repositories/{repoId:guid}/analysis")
            .WithTags("Analysis")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        group.MapPost("/chunks/missing", GetMissingChunksAsync)
            .WithDescription("Given a set of chunk checksums, returns those the repository's chunk store does not already hold. Writes nothing.")
            .WithSummary("Find Missing Chunks")
            .Produces<byte[]>(contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireRepoPermission(RepositoryPermission.Write);

        return group;
    }

    private static async Task<IResult> GetMissingChunksAsync(Guid repoId, HttpRequest request, BinStashDbContext db, CancellationToken ct)
    {
        try
        {
            var chunkChecksums = (await ChecksumCompressor.TransposeDecompressHashesAsync(request.Body)).ToArray();

            var repo = await db.Repositories.FindAsync([repoId], ct);
            if (repo == null)
                return Results.NotFound();

            var store = await db.ChunkStores.FindAsync([repo.ChunkStoreId], ct);
            if (store == null)
                return Results.NotFound();

            if (chunkChecksums.Length == 0)
                return Results.Bytes(ChecksumCompressor.TransposeCompress([]), "application/octet-stream");

            var knownChecksums = await db.Chunks
                .AsNoTracking()
                .Where(c => c.ChunkStoreId == store.Id && chunkChecksums.Contains(c.Checksum))
                .Select(c => c.Checksum)
                .ToListAsync(ct);

            var missingChecksums = chunkChecksums.Except(knownChecksums).ToList();

            return Results.Bytes(
                ChecksumCompressor.TransposeCompress(missingChecksums.Select(x => x.GetBytes()).ToList()),
                "application/octet-stream");
        }
        catch (Exception)
        {
            return Results.BadRequest("Invalid request body.");
        }
    }
}
