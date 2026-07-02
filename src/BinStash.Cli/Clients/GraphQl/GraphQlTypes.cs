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

namespace BinStash.Cli.Clients.GraphQl;

// ---------------------------------------------------------------------------
// GraphQL request envelopes (one per distinct variable shape for AOT safety)
// ---------------------------------------------------------------------------

/// <summary>Request with no variables (e.g. list queries).</summary>
public class GqlRequest
{
    public required string Query { get; set; }
}

/// <summary>Request with a single <c>id</c> variable.</summary>
public class GqlRequestById
{
    public required string Query { get; set; }
    public required GqlIdVariables Variables { get; set; }
}

public class GqlIdVariables
{
    public required Guid Id { get; set; }
}

/// <summary>Request with pagination variables (no other variables).</summary>
public class GqlPagedRequest
{
    public required string Query { get; set; }
    public required GqlPageVariables Variables { get; set; }
}

public class GqlPageVariables
{
    public int First { get; set; }
    public string? After { get; set; }
}

/// <summary>Request with an <c>id</c> plus pagination variables (for nested connections).</summary>
public class GqlRequestByIdPaged
{
    public required string Query { get; set; }
    public required GqlIdPageVariables Variables { get; set; }
}

public class GqlIdPageVariables
{
    public required Guid Id { get; set; }
    public int First { get; set; }
    public string? After { get; set; }
}

/// <summary>Request for <c>createRepository</c> mutation.</summary>
public class GqlCreateRepositoryRequest
{
    public required string Query { get; set; }
    public required GqlCreateRepositoryVariables Variables { get; set; }
}

public class GqlCreateRepositoryVariables
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? StorageClassName { get; set; }
}

/// <summary>Request for <c>createChunkStore</c> mutation.</summary>
public class GqlCreateChunkStoreRequest
{
    public required string Query { get; set; }
    public required GqlCreateChunkStoreVariables Variables { get; set; }
}

public class GqlCreateChunkStoreVariables
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? LocalPath { get; set; }
    public GqlCreateChunkStoreChunkerVariables? Chunker { get; set; }
}

public class GqlCreateChunkStoreChunkerVariables
{
    public string? Type { get; set; }
    public int? MinChunkSize { get; set; }
    public int? AvgChunkSize { get; set; }
    public int? MaxChunkSize { get; set; }
}

// ---------------------------------------------------------------------------
// Generic GraphQL response envelope
// ---------------------------------------------------------------------------

public class GqlResponse<T>
{
    public T? Data { get; set; }
    public List<GqlError>? Errors { get; set; }
}

public class GqlError
{
    public string? Message { get; set; }
}

// ---------------------------------------------------------------------------
// Cursor-paged connection helper
// ---------------------------------------------------------------------------

public class GqlPageInfo
{
    public bool HasNextPage { get; set; }
    public string? EndCursor { get; set; }
}

public class GqlConnection<T>
{
    public List<T>? Nodes { get; set; }
    public int TotalCount { get; set; }
    public GqlPageInfo? PageInfo { get; set; }
}

// ---------------------------------------------------------------------------
// Query / mutation data bags (one class per top-level GQL field)
// ---------------------------------------------------------------------------

public class GqlRepositoriesData
{
    public GqlConnection<GqlRepository>? Repositories { get; set; }
}

public class GqlRepositoryData
{
    public GqlRepository? Repository { get; set; }
}

public class GqlCreateRepositoryData
{
    public GqlRepository? CreateRepository { get; set; }
}

public class GqlRepositoryWithReleasesData
{
    public GqlRepositoryWithReleases? Repository { get; set; }
}

public class GqlChunkStoresData
{
    public GqlConnection<GqlChunkStore>? ChunkStores { get; set; }
}

public class GqlTenantsData
{
    public GqlConnection<GqlTenant>? Tenants { get; set; }
}

public class GqlChunkStoreData
{
    public GqlChunkStore? ChunkStore { get; set; }
}

public class GqlCreateChunkStoreData
{
    public GqlChunkStore? CreateChunkStore { get; set; }
}

// ---------------------------------------------------------------------------
// GQL entity types (CLI-side mirror of server DTOs.cs)
// ---------------------------------------------------------------------------

public class GqlRepository
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string StorageClass { get; set; }
    public GqlChunker? Chunker { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class GqlRepositoryWithReleases
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public GqlConnection<GqlRelease>? Releases { get; set; }
}

public class GqlChunker
{
    public string? Type { get; set; }
    public int? MinChunkSize { get; set; }
    public int? AvgChunkSize { get; set; }
    public int? MaxChunkSize { get; set; }
}

public class GqlRelease
{
    public Guid Id { get; set; }
    public required string Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Notes { get; set; }
    public Guid RepoId { get; set; }
}

public class GqlChunkStore
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public GqlChunkStoreBackendSettings? BackendSettings { get; set; }
}

public class GqlTenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
}

public class GqlChunkStoreBackendSettings
{
    public required string BackendType { get; set; }
    public string? LocalPath { get; set; }
}
