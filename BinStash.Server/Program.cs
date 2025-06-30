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

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BinStash.Contracts.ChunkStore;
using BinStash.Contracts.Delta;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repos;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ZstdNet;

namespace BinStash.Server;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddAuthorization();

        builder.Services.AddDbContext<BinStashDbContext>((_, optionsBuilder) => optionsBuilder.UseNpgsql(builder.Configuration.GetConnectionString("BinStashDb"))/*.EnableSensitiveDataLogging()*/);

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();
        
        var chunkStoreGroup = app.MapGroup("/api/chunkstores");
        chunkStoreGroup.MapPost("/", async (CreateChunkStoreDto dto, BinStashDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return Results.BadRequest("Chunk store name is required.");
            
            if (db.ChunkStores.Any(x => x.Name == dto.Name))
                return Results.Conflict($"A chunk store with the name '{dto.Name}' already exists.");

            // Check if the type is valid, otherwise return non-success
            var isValidChunkStoreType = Enum.TryParse<ChunkStoreType>(dto.Type, true, out var chunkStoreType);
            if (!isValidChunkStoreType)
                return Results.BadRequest($"Invalid chunk store type '{dto.Type}'.");
            
            // Check if the local path is valid for local chunk store type
            if (chunkStoreType == ChunkStoreType.Local)
            {
                if (string.IsNullOrWhiteSpace(dto.LocalPath))
                    return Results.BadRequest("Local path is required for local chunk store type.");
            }
            
            // Validate chunker options or set defaults
            var chunkerOptions = dto.Chunker == null ? ChunkerOptions.Default(ChunkerType.FastCdc) : new ChunkerOptions
            {
                Type = Enum.TryParse<ChunkerType>(dto.Chunker.Type, true, out var chunkerType) ? chunkerType : ChunkerType.FastCdc,
                MinChunkSize = dto.Chunker.MinChunkSize ?? 2048,
                AvgChunkSize = dto.Chunker.AvgChunkSize ?? 8192,
                MaxChunkSize = dto.Chunker.MaxChunkSize ?? 65536
            };
            
            if (chunkerOptions.MinChunkSize <= 0 || chunkerOptions.AvgChunkSize <= 0 || chunkerOptions.MaxChunkSize <= 0)
                return Results.BadRequest("Chunk sizes must be greater than zero.");
            
            var chunkStore = new ChunkStore(dto.Name, chunkStoreType, dto.LocalPath, new LocalFolderObjectStorage(dto.LocalPath))
            {
                ChunkerOptions = chunkerOptions
            };

            db.ChunkStores.Add(chunkStore);
            await db.SaveChangesAsync();

            return Results.Created($"/api/chunkstores/{chunkStore.Id}", new ChunkStoreSummaryDto
            {
                Id = chunkStore.Id,
                Name = chunkStore.Name
            });
        });
        chunkStoreGroup.MapGet("/", async (BinStashDbContext db) =>
        {
            return (await db.ChunkStores.ToListAsync()).Select(x => new ChunkStoreSummaryDto
            {
                Id = x.Id,
                Name = x.Name
            });
        });
        chunkStoreGroup.MapGet("/{id:guid}", async (Guid id, BinStashDbContext db) =>
        {
            var store = await db.ChunkStores.FindAsync(id);
            if (store == null)
                return Results.NotFound();

            return Results.Ok(new ChunkStoreDetailDto
            {
                Id = store.Id,
                Name = store.Name,
                Type = store.Type.ToString(),
                Chunker = new ChunkStoreChunkerDto
                {
                    Type = store.ChunkerOptions.Type.ToString(),
                    MinChunkSize = store.ChunkerOptions.MinChunkSize,
                    AvgChunkSize = store.ChunkerOptions.AvgChunkSize,
                    MaxChunkSize = store.ChunkerOptions.MaxChunkSize,
                },
                Stats = new Dictionary<string, object>() // await new LocalFolderChunkStorage(store.LocalPath).GetStorageStatsAsync()
            });
        });
        chunkStoreGroup.MapDelete("/{id:guid}", async (Guid id, BinStashDbContext db) =>
        {
            var store = await db.ChunkStores.FindAsync(id);
            if (store == null)
                return Results.NotFound();
            
            // TODO: Check if the store is in use by any repository before deleting
            // TODO: Delete the physical store if it's a local store

            return Results.Conflict();
        });
        chunkStoreGroup.MapPost("/{id:guid}/chunks/missing", async (Guid id, ChunkStoreMissingChunkSyncInfoDto dto, BinStashDbContext db) =>
        {
            var store = await db.ChunkStores.FindAsync(id);
            if (store == null)
                return Results.NotFound();
            
            // Return a list of all chunks that are in the dto but not in the database with the store id
            if (!dto.ChunkChecksums.Any())
                return Results.BadRequest("No chunk checksums provided.");
            
            var knownChecksums = await db.Chunks
                .Where(c => c.ChunkStoreId == id && dto.ChunkChecksums.Contains(c.Checksum))
                .Select(c => c.Checksum)
                .ToListAsync();
            
            var missingChecksums = dto.ChunkChecksums.Except(knownChecksums).ToList();
            
            if (!missingChecksums.Any())
                return Results.Ok(new ChunkStoreMissingChunkSyncInfoDto
                {
                    ChunkChecksums = []
                });
            
            return Results.Ok(new ChunkStoreMissingChunkSyncInfoDto
            {
                ChunkChecksums = missingChecksums
            });
        });
        chunkStoreGroup.MapPost("/{id:guid}/chunks/{chunkChecksum:length(64)}", async (Guid id, string chunkChecksum,  BinStashDbContext db, Stream chunkStream) =>
        {
            var store = await db.ChunkStores.FindAsync(id);
            if (store == null)
                return Results.NotFound();
            
            store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));
            
            using var ms = new MemoryStream();
            await chunkStream.CopyToAsync(ms);
            ms.Position = 0;

            if (db.Chunks.Any(c => c.Checksum == chunkChecksum && c.ChunkStoreId == id)) return Results.Ok();
            if (!await store.StoreChunkAsync(chunkChecksum, ms.ToArray())) return Results.Problem();
            db.Chunks.Add(new Chunk
            {
                Checksum = chunkChecksum,
                ChunkStoreId = id,
                Length = ms.Length
            });
            await db.SaveChangesAsync();
            return Results.Ok();
        });
        chunkStoreGroup.MapPost("/{id:guid}/chunks/batch", async (Guid id, List<ChunkUploadDto> chunks, BinStashDbContext db) =>
        {
            var store = await db.ChunkStores.FindAsync(id);
            if (store == null)
                return Results.NotFound();

            store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));

            if (!chunks.Any())
                return Results.BadRequest("No chunks provided.");

            // Deduplicate the chunks based on checksum
            var uniqueChunks = chunks
                .GroupBy(c => c.Checksum)
                .Select(g => g.First()) // Take the first occurrence of each checksum
                .ToList();
            

            var checksums = uniqueChunks.Select(c => c.Checksum).ToList();

            var knownChecksums = await db.Chunks
                .Where(c => c.ChunkStoreId == id && checksums.Contains(c.Checksum))
                .Select(c => c.Checksum)
                .ToListAsync();

            var missingChecksums = checksums.Except(knownChecksums).ToList();

            var tasks = new List<Task<bool>>();
            foreach (var chunk in uniqueChunks.Where(x => missingChecksums.Contains(x.Checksum)))
            {
                tasks.Add(Task.Run(async () =>
                {
                    var hash = Convert.ToHexString(SHA256.HashData(chunk.Data));
                    if (!hash.Equals(chunk.Checksum, StringComparison.OrdinalIgnoreCase))
                        return false;

                    // Store to filesystem asynchronously
                    if (!await store.StoreChunkAsync(chunk.Checksum, chunk.Data))
                        throw new Exception($"Failed to store chunk {chunk.Checksum} in store {store.Name}");

                    return true;
                }));
            }

            // Run all tasks
            await Task.WhenAll(tasks);

            // Add unique chunks to DbContext ensuring no duplicates
            var chunksToAdd = uniqueChunks
                .Where(x => missingChecksums.Contains(x.Checksum))
                .Select(chunk => new Chunk
                {
                    Checksum = chunk.Checksum,
                    ChunkStoreId = id,
                    Length = chunk.Data.Length
                });

            db.Chunks.AddRange(chunksToAdd);
            await db.SaveChangesAsync();

            return Results.Ok();
        });
        
        
        var repositoryGroup = app.MapGroup("/api/repositories");
        repositoryGroup.MapGet("/", async (BinStashDbContext db) =>
        {
            return (await db.Repositories.Include(r => r.ChunkStore).ToListAsync()).Select(r => new RepositorySummaryDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                ChunkStoreId = r.ChunkStoreId
            });
        });
        repositoryGroup.MapPost("/", async (CreateRepositoryDto dto, BinStashDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return Results.BadRequest("Repository name is required.");
            
            if (db.Repositories.Any(x => x.Name == dto.Name))
                return Results.Conflict($"A repository with the name '{dto.Name}' already exists.");
            
            if (dto.ChunkStoreId == Guid.Empty)
                return Results.BadRequest("Chunk store ID is required.");
            
            var chunkStore = await db.ChunkStores.FindAsync(dto.ChunkStoreId);
            if (chunkStore == null)
                return Results.NotFound($"Chunk store with ID '{dto.ChunkStoreId}' not found.");

            var repo = new Repository
            {
                Name = dto.Name,
                ChunkStore = chunkStore,
                ChunkStoreId = chunkStore.Id,
                Description = dto.Description
            };
            
            await db.Repositories.AddAsync(repo);
            
            await db.SaveChangesAsync();
            
            return Results.Created($"/api/repositories/{repo.Id}", new
            {
                Id = repo.Id,
                Name = chunkStore.Name
            });
        });
        repositoryGroup.MapGet("/{id:guid}", async (Guid id, BinStashDbContext db) =>
        {
            var repo = await db.Repositories.FindAsync(id);
            if (repo == null)
                return Results.NotFound();

            return Results.Ok(new RepositorySummaryDto
            {
                Id = repo.Id,
                Name = repo.Name,
                Description = repo.Description,
                ChunkStoreId = repo.ChunkStoreId
            });
        });
        repositoryGroup.MapGet("/{id:guid}/config", async (Guid id, BinStashDbContext db) =>
        {
            var repo = await db.Repositories.Include(x => x.ChunkStore).FirstOrDefaultAsync(x => x.Id == id);
            if (repo == null)
                return Results.NotFound();

            return Results.Ok(new
            {
                DedupeConfig = new
                {
                    Type = repo.ChunkStore.ChunkerOptions.Type.ToString(),
                    repo.ChunkStore.ChunkerOptions.MinChunkSize,
                    repo.ChunkStore.ChunkerOptions.AvgChunkSize,
                    repo.ChunkStore.ChunkerOptions.MaxChunkSize,
                    repo.ChunkStore.ChunkerOptions.ShiftCount,
                    repo.ChunkStore.ChunkerOptions.BoundaryCheckBytes
                }
            });
        });
        repositoryGroup.MapGet("/{id:guid}/releases", async (Guid id, BinStashDbContext db) =>
        {
            var repo = await db.Repositories.Include(x => x.Releases).FirstOrDefaultAsync(x => x.Id == id);
            if (repo == null)
                return Results.NotFound();

            return Results.Ok(repo.Releases.Select(x => new ReleaseSummaryDto
            {
                Id = x.Id,
                Version = x.Version,
                CreatedAt = x.CreatedAt,
                Notes = x.Notes,
                Repository = new RepositorySummaryDto
                {
                    Id = repo.Id,
                    Name = repo.Name,
                    Description = repo.Description,
                    ChunkStoreId = repo.ChunkStoreId
                }
            }));
        });
        repositoryGroup.MapPost("/{id:guid}/releases", async (Guid id, HttpRequest request, BinStashDbContext db) =>
        {
            var contentType = request.ContentType;
            if (contentType is not "application/x-msgpack" and not "application/x-msgpack+gzip" and not "application/x-msgpack+zst")
                return Results.BadRequest("Unsupported Content-Type. Use application/x-msgpack, application/x-msgpack+gzip or application/x-msgpack+zst.");

            if (request.ContentLength is null or 0)
                return Results.BadRequest("Request body cannot be empty.");
            
            var repo = await db.Repositories.FindAsync(id);
            if (repo == null)
                return Results.NotFound();
            
            var store = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
            if (store == null)
                return Results.NotFound("Chunk store not found for the repository.");
            
            store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));

            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms);
            ms.Position = 0;

            var releaseId = Guid.CreateVersion7();
            
            var releasePackage = contentType switch
            {
                "application/x-msgpack+zst" => MessagePackSerializer.Deserialize<ReleasePackage>(new Decompressor().Unwrap(ms.ToArray())),
                "application/x-msgpack+gzip" => MessagePackSerializer.Deserialize<ReleasePackage>(new GZipStream(ms, CompressionMode.Decompress)),
                _ => throw new NotImplementedException()
            };
            
            releasePackage.ReleaseId = releaseId.ToString();
            releasePackage.RepoId = repo.Id.ToString();
            
            var zstData = new Compressor().Wrap(MessagePackSerializer.Serialize(releasePackage));
            var hash = Convert.ToHexString(SHA256.HashData(zstData));
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
            
            await store.StoreReleasePackageAsync(zstData);
            
            await db.SaveChangesAsync();
            return Results.Created();
        });
        
        var releaseGroup = app.MapGroup("/api/releases");
        releaseGroup.MapGet("/", async (BinStashDbContext db) =>
        {
            return (await db.Releases.Include(r => r.Repository).ToListAsync()).Select(r => new ReleaseSummaryDto
            {
                Id = r.Id,
                Version = r.Version,
                CreatedAt = r.CreatedAt,
                Notes = r.Notes,
                Repository = new RepositorySummaryDto
                {
                    Id = r.Repository.Id,
                    Name = r.Repository.Name,
                    Description = r.Repository.Description,
                    ChunkStoreId = r.Repository.ChunkStoreId
                }
            });
        });
        releaseGroup.MapGet("/{id:guid}", async (Guid id, BinStashDbContext db) =>
        {
            var release = await db.Releases.Include(r => r.Repository).FirstOrDefaultAsync(r => r.Id == id);
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
        });
        releaseGroup.MapGet("/{id:guid}/download", async (Guid id, string? component, string? file, Guid? diffReleaseId, HttpResponse response, BinStashDbContext db) =>
        {
            if (!string.IsNullOrEmpty(file) && string.IsNullOrEmpty(component))
                return Results.BadRequest("Component must be specified when requesting a specific file.");
            
            var release = await db.Releases.FirstOrDefaultAsync(r => r.Id == id);
            if (release == null)
                return Results.NotFound();
            
            if (diffReleaseId is not null && diffReleaseId == release.Id)
                return Results.BadRequest("Cannot create a diff release against itself.");

            var diffRelease = await db.Releases.FirstOrDefaultAsync(r => r.Id == diffReleaseId);
            if (diffReleaseId is not null && diffRelease is null)
                return Results.NotFound("Diff release not found.");
            
            var repo = await db.Repositories.Include(r => r.ChunkStore).FirstOrDefaultAsync(r => r.Id == release.RepoId);
            if (repo == null)
                return Results.NotFound("Repository not found for the release.");
            
            var store = new ChunkStore(repo.ChunkStore.Name, repo.ChunkStore.Type, repo.ChunkStore.LocalPath, new LocalFolderObjectStorage(repo.ChunkStore.LocalPath));
            var packageData = await store.RetrieveReleasePackageAsync(release.ReleaseDefinitionChecksum);
            if (packageData == null)
                return Results.NotFound("Release package not found in the chunk store.");

            var decompressedData = new Decompressor().Unwrap(packageData);
            var releasePackage = MessagePackSerializer.Deserialize<ReleasePackage>(decompressedData);
            
            var chunksByIndex = releasePackage.Chunks.ToDictionary(c => c.Index, c => c.Checksum);
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

            if (diffRelease is not null)
            {
                // Generate a diff release package
                var diffPackageData = await store.RetrieveReleasePackageAsync(diffRelease.ReleaseDefinitionChecksum);
                if (diffPackageData == null)
                    return Results.NotFound("Diff release package not found in the chunk store.");
                
                var diffDecompressedData = new Decompressor().Unwrap(diffPackageData);
                var diffReleasePackage = MessagePackSerializer.Deserialize<ReleasePackage>(diffDecompressedData);
                
                releasePackage.ReleaseId = release.Id.ToString();
                diffReleasePackage.ReleaseId = diffRelease.Id.ToString();
                
                var (deltaManifest, newChunkChecksums) = ComputeChunkDeltaManifest(diffReleasePackage, releasePackage, component);
                
                var deltaManifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(deltaManifest));
                // Write the delta manifest as a tar header
                var manifestHeader = CreateTarHeader("delta-manifest.json", deltaManifestBytes.Length, DateTime.UtcNow);
                await compressor.WriteAsync(manifestHeader);
                await compressor.WriteAsync(deltaManifestBytes);
                
                // Pad to 512-byte blocks
                var manifestPadding = 512 - deltaManifestBytes.Length % 512;
                if (manifestPadding != 512)
                    await compressor.WriteAsync(new byte[manifestPadding]);
                
                foreach (var newChunks in newChunkChecksums)
                {
                    var chunkData = await store.RetrieveChunkAsync(newChunks);
                    if (chunkData == null)
                        return Results.NotFound($"Chunk {newChunks} not found in the chunk store.");
                    
                    var chunkHeader = CreateTarHeader($"chunks/{newChunks}", chunkData.Length, DateTime.UtcNow);
                    await compressor.WriteAsync(chunkHeader);
                    await compressor.WriteAsync(chunkData);
                    
                    // Pad to 512-byte blocks
                    var padding = 512 - chunkData.Length % 512;
                    if (padding != 512)
                        await compressor.WriteAsync(new byte[padding]);
                }
            }
            else
            {
                foreach (var (componentName, componentFile) in files)
                {
                    var totalSize = componentFile.Chunks.Sum(c => (long)c.Length);
                    var relativePath = Path.Combine(componentName, componentFile.Name).Replace('\\', '/');
                    var header = CreateTarHeader(relativePath, totalSize, release.CreatedAt.UtcDateTime);
                    await compressor.WriteAsync(header);

                    foreach (var chunkRef in componentFile.Chunks)
                    {
                        var checksum = chunksByIndex[chunkRef.Index];
                        var chunkData = await store.RetrieveChunkAsync(Convert.ToHexStringLower(checksum));
                        await compressor.WriteAsync(chunkData, 0, chunkData.Length);
                    }
                    
                    // Pad to 512-byte blocks
                    var padding = 512 - totalSize % 512;
                    if (padding != 512)
                        await compressor.WriteAsync(new byte[padding]);
                }
            }
            
            
            // Write 2 empty 512-byte blocks to mark end of archive
            await compressor.WriteAsync(new byte[1024]);

            return Results.Empty;
        });
        // TODO: DELETE /api/releases/{id:guid}

        app.Run();
    }
    
    private static byte[] CreateTarHeader(string name, long size, DateTime lastModifiedUtc)
    {
        var buffer = new byte[512];
        
        void WriteOctal(long value, int offset, int length)
        {
            var str = Convert.ToString(value, 8).PadLeft(length - 1, '0') + '\0';
            Encoding.ASCII.GetBytes(str).CopyTo(buffer, offset);
        }

        void WriteString(string value, int offset, int length)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            Array.Copy(bytes, 0, buffer, offset, Math.Min(bytes.Length, length));
        }
        
        WriteString(name, 0, 100);
        WriteOctal(0, 100, 8); // mode
        WriteOctal(0, 108, 8); // uid
        WriteOctal(0, 116, 8); // gid
        WriteOctal(size, 124, 12); // size
        WriteOctal((long)(lastModifiedUtc - new DateTime(1970, 1, 1)).TotalSeconds, 136, 12); // mtime
        for (int i = 148; i < 156; i++) buffer[i] = (byte)' '; // checksum placeholder
        buffer[156] = (byte)'0'; // typeflag = normal file

        // Compute checksum
        int checksum = buffer.Sum(b => b);
        WriteOctal(checksum, 148, 8);

        return buffer;
    }
    
    private static (DeltaManifest manifest, List<string> newChunkChecksums) ComputeChunkDeltaManifest(ReleasePackage oldRelease, ReleasePackage newRelease, string? singleComponent)
    {
        // Map: index → checksum
        var oldChunkMap = oldRelease.Chunks.ToDictionary(c => c.Index, c => Convert.ToHexStringLower(c.Checksum));
        var newChunkMap = newRelease.Chunks.ToDictionary(c => c.Index, c => Convert.ToHexStringLower(c.Checksum));

        var oldChecksumSet = oldChunkMap.Values.ToHashSet();
        var newChunks = new HashSet<string>();
        var files = new List<DeltaFile>();
        
        if (singleComponent != null)
        {
            // Filter components if single component is specified
            newRelease.Components = newRelease.Components
                .Where(c => c.Name.Equals(singleComponent, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var component in newRelease.Components)
        {
            foreach (var file in component.Files)
            {
                var chunkRefs = new List<DeltaChunkRef>();
                long totalSize = 0;

                foreach (var chunk in file.Chunks)
                {
                    var checksum = newChunkMap[chunk.Index];
                    var source = oldChecksumSet.Contains(checksum) ? "existing" : "new";

                    chunkRefs.Add(new DeltaChunkRef(
                        chunk.Index,
                        chunk.Offset,
                        checksum,
                        chunk.Length,
                        source
                    ));

                    totalSize += chunk.Length;

                    if (source == "new")
                        newChunks.Add(checksum);
                }
                
                // Get the old file hash from the old release if it exists
                var oldHash = oldRelease.Components.FirstOrDefault(x => x.Name == component.Name)?.Files.FirstOrDefault(x => x.Name == file.Name)?.Hash;

                files.Add(new DeltaFile(
                    (singleComponent != null ? file.Name : Path.Combine(component.Name, file.Name)).Replace('\\', '/'),
                    totalSize,
                    oldHash != null ? Convert.ToHexStringLower(oldHash) : string.Empty,
                    Convert.ToHexStringLower(file.Hash),
                    chunkRefs
                ));
            }
        }

        return (
            new DeltaManifest(oldRelease.ReleaseId, newRelease.ReleaseId, files.Where(x => x.Chunks.Any(x => x.Source == "new")).ToList()),
            newChunks.ToList()
        );
    }
}