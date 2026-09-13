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

using BinStash.Contracts.ChunkStore;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repo;
using BinStash.Contracts.Tenant;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Auth.Tenant;
using BinStash.Server.Extensions;
using BinStash.Server.GraphQL;
using BinStash.Server.GraphQL.Features.ChunkStores;
using BinStash.Server.GraphQL.Features.Repositories;
using BinStash.Server.GraphQL.Features.Tenants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

/// <summary>
/// REST surface for the CLI.
/// </summary>
/// <remarks>
/// The CLI is NativeAOT-published and needs six flat operations, not a graph. Driving them
/// over GraphQL forced a hand-written request envelope per variable shape plus a third copy
/// of every DTO, for no benefit — the wire contract it actually wants already exists in
/// BinStash.Contracts. The web UI keeps using GraphQL, where paging/filtering/projection
/// genuinely pay off.
///
/// These handlers deliberately delegate to the SAME services the GraphQL resolvers use, so
/// there is one implementation and one authorization path behind two transports rather than
/// two parallel implementations that can drift. Authorization is enforced inside those
/// services (they must, since GraphQL depends on it); the permission each route requires is
/// noted on the route so it stays readable from here.
/// </remarks>
public static class CliApiEndpoints
{
    public static void MapCliApiEndpoints(this IEndpointRouteBuilder app)
    {
        var tenants = app.MapGroup("/api/tenants")
            .WithTags("CLI")
            .RequireAuthorization();

        // Lists what the caller can see; handles user and service-account (machine) subjects.
        tenants.MapGet("/", ListTenantsAsync)
            .WithName("CliListTenants")
            .WithDescription("Lists the tenants the caller belongs to.");

        var repositories = tenants.MapGroup("/{tenantId:guid}/repositories");

        // Requires tenant Member.
        repositories.MapGet("/", ListRepositoriesAsync)
            .WithName("CliListRepositories")
            .WithDescription("Lists the repositories in a tenant.");

        // Requires tenant Admin.
        repositories.MapPost("/", CreateRepositoryAsync)
            .WithName("CliCreateRepository")
            .WithDescription("Creates a repository.");

        // Requires repository Read.
        repositories.MapGet("/{repoId:guid}", GetRepositoryAsync)
            .WithName("CliGetRepository")
            .WithDescription("Gets a repository by id.");

        // Requires repository Read.
        repositories.MapGet("/{repoId:guid}/releases", ListReleasesAsync)
            .WithName("CliListReleases")
            .WithDescription("Lists the releases of a repository.")
            .RequireRepoPermission(RepositoryPermission.Read);

        // Chunk stores are instance-wide; the service enforces instance Admin.
        var chunkStores = app.MapGroup("/api/chunk-stores")
            .WithTags("CLI")
            .RequireAuthorization();

        chunkStores.MapGet("/", ListChunkStoresAsync)
            .WithName("CliListChunkStores")
            .WithDescription("Lists the chunk stores on this instance.");

        chunkStores.MapGet("/{id:guid}", GetChunkStoreAsync)
            .WithName("CliGetChunkStore")
            .WithDescription("Gets a chunk store by id.");

        chunkStores.MapPost("/", CreateChunkStoreAsync)
            .WithName("CliCreateChunkStore")
            .WithDescription("Creates a chunk store.");
    }

    private static async Task<IResult> ListTenantsAsync(TenantQueryService service)
    {
        var tenants = await MaterializeAsync((await service.GetTenantsAsync()).OrderBy(t => t.Name), default);

        return Results.Ok(tenants
            .Select(t => new TenantInfoDto(
                t.Id,
                t.Name,
                t.Slug,
                t.JoinedAt ?? default,
                // Role is per-membership and not part of the tenant list projection; the CLI
                // only uses it for display and treats empty as "unknown".
                string.Empty))
            .ToList());
    }

    private static async Task<IResult> ListRepositoriesAsync(RepositoryQueryService service)
    {
        var repositories = await MaterializeAsync((await service.GetRepositoriesAsync()).OrderBy(r => r.Name), default);

        return Results.Ok(repositories.Select(ToSummary).ToList());
    }

    private static async Task<IResult> GetRepositoryAsync(Guid repoId, RepositoryQueryService service, CancellationToken ct)
    {
        var repository = await service.GetRepositoryByIdAsync(repoId, ct);
        return repository is null ? Results.NotFound() : Results.Ok(ToSummary(repository));
    }

    private static async Task<IResult> CreateRepositoryAsync(
        [FromBody] CreateRepositoryDto dto,
        RepositoryMutationService service,
        CancellationToken ct)
    {
        var created = await service.CreateRepositoryAsync(new CreateRepositoryInput
        {
            Name = dto.Name,
            Description = dto.Description,
            StorageClassName = dto.StorageClassName
        }, ct);

        return Results.Ok(ToSummary(created));
    }

    private static async Task<IResult> ListReleasesAsync(
        Guid repoId,
        RepositoryQueryService repositories,
        CancellationToken ct)
    {
        var repository = await repositories.GetRepositoryByIdAsync(repoId, ct);
        if (repository is null)
            return Results.NotFound();

        var releases = await MaterializeAsync(
            repositories.GetReleasesForRepository(repoId).OrderByDescending(r => r.CreatedAt), ct);

        var summary = ToSummary(repository);

        return Results.Ok(releases
            .Select(r => new ReleaseSummaryDto
            {
                Id = r.Id,
                Version = r.Version,
                CreatedAt = r.CreatedAt,
                Notes = r.Notes,
                Repository = summary
            })
            .ToList());
    }

    private static async Task<IResult> ListChunkStoresAsync(ChunkStoreQueryService service, CancellationToken ct)
    {
        var stores = await MaterializeAsync((await service.GetChunkStoresAsync(ct)).OrderBy(s => s.Name), ct);

        return Results.Ok(stores
            .Select(s => new ChunkStoreSummaryDto { Id = s.Id, Name = s.Name })
            .ToList());
    }

    private static async Task<IResult> GetChunkStoreAsync(Guid id, ChunkStoreQueryService service, CancellationToken ct)
    {
        var store = await service.GetChunkStoreByIdAsync(id, ct);
        if (store is null)
            return Results.NotFound();

        var stats = await service.GetChunkStoreStatsAsync(id, ct);
        return Results.Ok(ToDetail(store, stats?.TotalChunks));
    }

    private static async Task<IResult> CreateChunkStoreAsync(
        [FromBody] CreateChunkStoreDto dto,
        ChunkStoreMutationService service,
        CancellationToken ct)
    {
        var created = await service.CreateChunkStoreAsync(new CreateChunkStoreInput
        {
            Name = dto.Name,
            Type = dto.Type,
            LocalPath = dto.LocalPath,
            Chunker = dto.Chunker is null
                ? null
                : new ChunkStoreChunkerInput
                {
                    Type = dto.Chunker.Type,
                    MinChunkSize = dto.Chunker.MinChunkSize,
                    AvgChunkSize = dto.Chunker.AvgChunkSize,
                    MaxChunkSize = dto.Chunker.MaxChunkSize
                }
        }, ct);

        return Results.Ok(ToDetail(created, totalChunks: null));
    }

    /// <summary>
    /// Materialises a queryable returned by one of the shared services.
    /// </summary>
    /// <remarks>
    /// Not every one of them is EF-backed: ChunkStoreQueryService deliberately loads its
    /// rows first because BackendSettings is a JSON-converted column that cannot be
    /// projected in SQL, and hands back an in-memory <c>AsQueryable()</c>. Calling
    /// <c>ToListAsync</c> on that throws "The source 'IQueryable' doesn't implement
    /// 'IAsyncEnumerable'", so dispatch on what the source actually supports.
    /// </remarks>
    private static Task<List<T>> MaterializeAsync<T>(IQueryable<T> source, CancellationToken ct)
        => source is IAsyncEnumerable<T>
            ? source.ToListAsync(ct)
            : Task.FromResult(source.ToList());

    private static RepositorySummaryDto ToSummary(RepositoryGql repository) => new()
    {
        Id = repository.Id,
        Name = repository.Name,
        Description = repository.Description,
        StorageClass = repository.StorageClass,
        Chunker = repository.Chunker is null
            ? null
            : new ChunkStoreChunkerDto
            {
                Type = repository.Chunker.Type,
                MinChunkSize = repository.Chunker.MinChunkSize,
                AvgChunkSize = repository.Chunker.AvgChunkSize,
                MaxChunkSize = repository.Chunker.MaxChunkSize
            }
    };

    private static ChunkStoreDetailDto ToDetail(ChunkStoreGql store, int? totalChunks) => new()
    {
        Id = store.Id,
        Name = store.Name,
        Type = store.Type,
        Chunker = new ChunkStoreChunkerDto
        {
            Type = store.Chunker?.Type ?? string.Empty,
            MinChunkSize = store.Chunker?.MinChunkSize,
            AvgChunkSize = store.Chunker?.AvgChunkSize,
            MaxChunkSize = store.Chunker?.MaxChunkSize
        },
        BackendSettings = new ChunkStoreBackendSettingsDto
        {
            Type = store.BackendSettings?.BackendType ?? string.Empty,
            LocalPath = store.BackendSettings?.LocalPath
        },
        Stats = totalChunks is null
            ? []
            : new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["totalChunks"] = System.Text.Json.JsonSerializer.SerializeToElement(totalChunks.Value)
            }
    };
}
