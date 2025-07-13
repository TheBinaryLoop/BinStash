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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repos;
using BinStash.Core.Compression;
using BinStash.Core.Entities;
using BinStash.Core.Serialization;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage;
using BinStash.Server.Helpers;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using ZstdNet;

namespace BinStash.Server.Endpoints;

public static class ReleaseEndpoints
{
    public static RouteGroupBuilder MapReleaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/releases")
            .WithTags("Releases");

        group.MapPost("/", CreateReleaseAsync)
            .WithDescription("Create a new release for a repository.")
            .WithSummary("Create Release")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        
        group.MapGet("/{id:guid}", GetReleaseByIdAsync)
            .WithDescription("Get a release by ID.")
            .WithSummary("Get Release")
            .Produces<ReleaseSummaryDto>()
            .Produces(StatusCodes.Status404NotFound);
            
        group.MapGet("/{id:guid}/download", GetReleaseDownloadAsync)
            .WithDescription("Download a release package.")
            .WithSummary("Download Release")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        
        // TODO: DELETE /api/releases/{id:guid}

        return group;
    }
    
    private static async Task<IResult> CreateReleaseAsync(HttpRequest request, BinStashDbContext db)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest("Content-Type must be multipart/form-data.");
        
        var form = await request.ReadFormAsync();
        
        var repoIdStr = form["repositoryId"].FirstOrDefault();
        if (!Guid.TryParse(repoIdStr, out var repoId))
            return Results.BadRequest("Invalid or missing repository ID.");
        
        var file = form.Files.GetFile("releaseDefinition");
        if (file == null || file.Length == 0)
            return Results.BadRequest("Missing or empty release definition file.");
        
        var contentType = file.ContentType;
        if (contentType is not "application/x-bs-rdef" )
            return Results.BadRequest("Unsupported Content-Type.");
        
        var repo = await db.Repositories.FindAsync(repoId);
        if (repo == null)
            return Results.NotFound();

        var store = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
        if (store == null)
            return Results.NotFound("Chunk store not found.");

        var releaseId = Guid.CreateVersion7();
        await using var stream = file.OpenReadStream();

        var releasePackage = await ReleasePackageSerializer.DeserializeAsync(stream);
        
        if (db.Releases.Any(r => r.RepoId == repo.Id && r.Version == releasePackage.Version))
            return Results.Conflict($"A release with version '{releasePackage.Version}' already exists for this repository.");

        releasePackage.ReleaseId = releaseId.ToString();
        releasePackage.RepoId = repo.Id.ToString();

        await using var releasePackageStream = new MemoryStream();
        await ReleasePackageSerializer.SerializeAsync(releasePackageStream, releasePackage);
        var releasePackageData = releasePackageStream.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(releasePackageData));

        await db.Releases.AddAsync(new Release
        {
            Id = releaseId,
            Version = releasePackage.Version,
            CreatedAt = DateTimeOffset.UtcNow,
            Notes = releasePackage.Notes,
            RepoId = repo.Id,
            Repository = repo,
            ReleaseDefinitionChecksum = hash
        });

        var chunkStore = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));
        await chunkStore.StoreReleasePackageAsync(releasePackageData);

        await db.SaveChangesAsync();

        return Results.Created($"/api/releases/{releaseId}", null);
    }
    
    private static async Task<IResult> GetReleaseByIdAsync(Guid id, BinStashDbContext db)
    {
        var release = await db.Releases.AsNoTracking().Include(r => r.Repository).FirstOrDefaultAsync(r => r.Id == id);
        if (release == null)
            return Results.NotFound();

        return Results.Ok(new ReleaseSummaryDto
        {
            Id = release.Id,
            Version = release.Version,
            CreatedAt = release.CreatedAt,
            Notes = release.Notes,
            Repository = new RepositorySummaryDto
            {
                Id = release.Repository.Id,
                Name = release.Repository.Name,
                Description = release.Repository.Description,
                ChunkStoreId = release.Repository.ChunkStoreId
            }
        });
    }
    
    private static async Task<IResult> GetReleaseDownloadAsync(Guid id, string? component, string? file, Guid? diffReleaseId, HttpResponse response, BinStashDbContext db)
    {
        if (!string.IsNullOrEmpty(file) && string.IsNullOrEmpty(component))
                return Results.BadRequest("Component must be specified when requesting a specific file.");
            
        var release = await db.Releases.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (release == null)
            return Results.NotFound();
        
        if (diffReleaseId is not null && diffReleaseId == release.Id)
            return Results.BadRequest("Cannot create a diff release against itself.");

        var diffRelease = await db.Releases.AsNoTracking().FirstOrDefaultAsync(r => r.Id == diffReleaseId);
        if (diffReleaseId is not null && diffRelease is null)
            return Results.NotFound("Diff release not found.");
        
        var repo = await db.Repositories.Include(r => r.ChunkStore).FirstOrDefaultAsync(r => r.Id == release.RepoId);
        if (repo == null)
            return Results.NotFound("Repository not found for the release.");
        
        var store = new ChunkStore(repo.ChunkStore.Name, repo.ChunkStore.Type, repo.ChunkStore.LocalPath, new LocalFolderObjectStorage(repo.ChunkStore.LocalPath));
        var packageData = await store.RetrieveReleasePackageAsync(release.ReleaseDefinitionChecksum);
        if (packageData == null)
            return Results.NotFound("Release package not found in the chunk store.");

        var releasePackage = await ReleasePackageSerializer.DeserializeAsync(packageData);
        
        var chunksByIndex = releasePackage.Chunks.Select((c, i) => new { Index = i, c.Checksum }).ToDictionary(x => x.Index, x => x.Checksum);
        var files = (component == null
            ? releasePackage.Components.SelectMany(c => c.Files.Select(f => (Component: c.Name, File: f)))
            : releasePackage.Components
                .Where(c => c.Name.Equals(component, StringComparison.OrdinalIgnoreCase))
                .SelectMany(c => c.Files.Select(f => (Component: c.Name, File: f)))).ToList();
        if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(component))
        {
            // Filter files by the requested file name and component
            files = files
                .Where(f => f.Component.Equals(component, StringComparison.OrdinalIgnoreCase) && f.File.Name.Equals(file, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (files.Count == 0)
                return Results.NotFound($"File '{file}' not found in the component '{component}'.");
        }
        
        if (files.Count == 0)
            return Results.NotFound("No files found for the requested component.");
        
        response.ContentType = "application/zstd";
        var fileName = $"{(component != null ? $"component-{component}-" : "release-")}{release.Id}{(diffRelease != null ? ".diff" : "")}.tar.zst";
        
        response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition
        {
            FileName = fileName,
            Inline = false
        }.ToString();
        

        await using var compressor = new CompressionStream(response.Body);
        await using var tarWriter = new TarWriter(compressor);
        
        if (diffRelease is not null)
        {
            // Generate a diff release package
            var diffPackageData = await store.RetrieveReleasePackageAsync(diffRelease.ReleaseDefinitionChecksum);
            if (diffPackageData == null)
                return Results.NotFound("Diff release package not found in the chunk store.");
            
            var diffReleasePackage = await ReleasePackageSerializer.DeserializeAsync(diffPackageData);
            
            releasePackage.ReleaseId = release.Id.ToString();
            diffReleasePackage.ReleaseId = diffRelease.Id.ToString();
            
            var (deltaManifest, newChunkChecksums) = DeltaCalculator.ComputeChunkDeltaManifest(diffReleasePackage, releasePackage, component);
            
            var deltaManifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(deltaManifest));
            // Write the delta manifest
            await tarWriter.WriteFileAsync("delta-manifest.json", deltaManifestBytes);
            
            foreach (var newChunks in newChunkChecksums)
            {
                var chunkData = await store.RetrieveChunkAsync(newChunks);
                if (chunkData == null)
                    return Results.NotFound($"Chunk {newChunks} not found in the chunk store.");
                
                await tarWriter.WriteFileAsync($"chunks/{newChunks}", chunkData);
            }
        }
        else
        {
            foreach (var (componentName, componentFile) in files)
            {
                var totalSize = componentFile.Chunks.Sum(c => (long)c.Length);
                // TODO: Remove component name from the path if component is set
                var relativePath = componentFile.Name.Replace('\\', '/');
                
                await tarWriter.WriteFileAsync(relativePath, async (outputStream) =>
                {
                    var chunkRefs = ChunkRefHelper.ConvertDeltaToChunkRefs(componentFile.Chunks).ToList();
                    var loadedChunks = new byte[chunkRefs.Count][];
                    var throttler = new SemaphoreSlim(64); // Limit parallel reads

                    await Task.WhenAll(chunkRefs.Select(async (chunkRef, i) =>
                    {
                        await throttler.WaitAsync();
                        try
                        {
                            var checksum = chunksByIndex[chunkRef.Index];
                            var chunkData = await store.RetrieveChunkAsync(Convert.ToHexStringLower(checksum));
                            loadedChunks[i] = chunkData ?? throw new FileNotFoundException($"Chunk {checksum} not found in the chunk store.");
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }));

                    foreach (var chunk in loadedChunks)
                        await outputStream.WriteAsync(chunk, 0, chunk.Length);
                }, totalSize, release.CreatedAt.UtcDateTime);
                
                await tarWriter.WriteFileAsync($"{relativePath}.xxhash3", componentFile.Hash);
            }
        }

        return Results.Empty;
    }
    
}