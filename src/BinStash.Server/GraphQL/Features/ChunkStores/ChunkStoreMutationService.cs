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

using System.Text.Json;
using System.Threading.Channels;
using BinStash.Core.Auth.Instance;
using BinStash.Core.Entities;
using BinStash.Core.Serialization;
using BinStash.Infrastructure.Data;
using BinStash.Server.Configuration;
using BinStash.Server.GraphQL.Auth;
using BinStash.Server.HostedServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace BinStash.Server.GraphQL.Features.ChunkStores;

public sealed class ChunkStoreMutationService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;
    private readonly IOptions<StorageSettings> _storageOptions;
    private readonly RebuildJobChannel _rebuildJobChannel;
    private readonly Channel<Guid> _upgradeJobChannel;

    public ChunkStoreMutationService(
        BinStashDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService authorizationService,
        IOptions<StorageSettings> storageOptions,
        RebuildJobChannel rebuildJobChannel,
        Channel<Guid> upgradeJobChannel)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
        _storageOptions = storageOptions;
        _rebuildJobChannel = rebuildJobChannel;
        _upgradeJobChannel = upgradeJobChannel;
    }

    public async Task<ChunkStoreGql> CreateChunkStoreAsync(CreateChunkStoreInput input, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);

        if (string.IsNullOrWhiteSpace(input.Name))
            throw new GraphQLException("Chunk store name is required.");

        if (await _db.ChunkStores.AnyAsync(x => x.Name == input.Name, ct))
            throw new GraphQLException($"A chunk store with the name '{input.Name}' already exists.");

        if (!Enum.TryParse<BinStash.Core.Entities.ChunkStoreType>(input.Type, true, out var chunkStoreType))
            throw new GraphQLException($"Invalid chunk store type '{input.Type}'.");

        ChunkStoreBackendSettings backendSettings;
        switch (chunkStoreType)
        {
            case BinStash.Core.Entities.ChunkStoreType.Local:
            {
                if (string.IsNullOrWhiteSpace(input.LocalPath))
                    throw new GraphQLException("LocalPath is required for local chunk store type.");

                var localPath = input.LocalPath.Trim();

                if (localPath.StartsWith(@"\\", StringComparison.Ordinal) ||
                    localPath.StartsWith("//", StringComparison.Ordinal))
                    throw new GraphQLException("UNC paths are not permitted for local chunk store paths.");

                if (!Path.IsPathRooted(localPath))
                    throw new GraphQLException("LocalPath must be an absolute path.");

                string canonicalPath;
                try
                {
                    canonicalPath = Path.GetFullPath(localPath);
                }
                catch (Exception e)
                {
                    throw new GraphQLException($"LocalPath is not a valid filesystem path: {e.Message}");
                }

                var allowedRoot = _storageOptions.Value.AllowedRootPath;
                if (!string.IsNullOrWhiteSpace(allowedRoot))
                {
                    string canonicalRoot;
                    try
                    {
                        canonicalRoot = Path.GetFullPath(allowedRoot);
                    }
                    catch (Exception e)
                    {
                        throw new GraphQLException($"Server configuration error: Storage:AllowedRootPath is invalid: {e.Message}");
                    }

                    var rootWithSep = canonicalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                      + Path.DirectorySeparatorChar;
                    var pathWithSep = canonicalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                      + Path.DirectorySeparatorChar;

                    if (!pathWithSep.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
                        throw new GraphQLException(
                            $"LocalPath must reside within the configured allowed root '{canonicalRoot}'.");
                }

                if (!Directory.Exists(canonicalPath))
                {
                    try
                    {
                        Directory.CreateDirectory(canonicalPath);
                    }
                    catch (Exception e)
                    {
                        throw new GraphQLException($"Failed to create local path: {e.Message}");
                    }
                }

                backendSettings = new LocalFolderBackendSettings { Path = canonicalPath };
                break;
            }
            default:
                throw new GraphQLException($"Chunk store type '{input.Type}' is not yet supported.");
        }

        var chunkerOptions = input.Chunker == null
            ? ChunkerOptions.Default(ChunkerType.FastCdc)
            : new ChunkerOptions
            {
                Type = Enum.TryParse<ChunkerType>(input.Chunker.Type ?? "FastCdc", true, out var ct2) ? ct2 : ChunkerType.FastCdc,
                MinChunkSize = input.Chunker.MinChunkSize ?? 2048,
                AvgChunkSize = input.Chunker.AvgChunkSize ?? 8192,
                MaxChunkSize = input.Chunker.MaxChunkSize ?? 65536,
            };

        var chunkerErrors = chunkerOptions.Validate();
        if (chunkerErrors.Count > 0)
            throw new GraphQLException($"Invalid chunker options: {string.Join("; ", chunkerErrors)}");

        var chunkStore = new ChunkStore(input.Name, chunkStoreType, backendSettings)
        {
            ChunkerOptions = chunkerOptions
        };

        _db.ChunkStores.Add(chunkStore);
        await _db.SaveChangesAsync(ct);

        return new ChunkStoreGql
        {
            Id = chunkStore.Id,
            Name = chunkStore.Name,
            Type = chunkStore.Type.ToString(),
            BackendSettings = new ChunkStoreBackendSettingsGql
            {
                BackendType = "LocalFolder",
                LocalPath = (backendSettings as LocalFolderBackendSettings)?.Path
            }
        };
    }

    public async Task<BackgroundJobGql> RebuildChunkStoreAsync(Guid chunkStoreId, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);

        var store = await _db.ChunkStores.FindAsync(new object[] { chunkStoreId }, ct);
        if (store is null)
            throw new GraphQLException($"Chunk store '{chunkStoreId}' not found.");

        var hasActiveJob = _db.BackgroundJobs.Where(j =>
            j.JobType == BackgroundJobTypes.ChunkStoreRebuild
            && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.Running)
            && j.JobData != null).AsEnumerable().Any(j => j.JobData!.Contains(chunkStoreId.ToString()));

        if (hasActiveJob)
            throw new GraphQLException("A rebuild job is already running or pending for this chunk store.");

        var jobData = new ChunkStoreRebuildJobData { ChunkStoreId = chunkStoreId };

        var job = new BackgroundJob
        {
            Id = Guid.NewGuid(),
            JobType = BackgroundJobTypes.ChunkStoreRebuild,
            Status = BackgroundJobStatus.Pending,
            JobData = JsonSerializer.Serialize(jobData),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.BackgroundJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        await _rebuildJobChannel.Channel.Writer.WriteAsync(job.Id, ct);

        return MapJobToGql(job, chunkStoreId);
    }

    public async Task<BackgroundJobGql> UpgradeChunkStoreAsync(Guid chunkStoreId, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);

        var store = await _db.ChunkStores.FindAsync(new object[] { chunkStoreId }, ct);
        if (store is null)
            throw new GraphQLException($"Chunk store '{chunkStoreId}' not found.");

        if (store.Type != BinStash.Core.Entities.ChunkStoreType.Local)
            throw new GraphQLException("Release upgrade is currently only supported for local chunk stores.");

        var hasActiveJob = _db.BackgroundJobs.Where(j =>
            j.JobType == BackgroundJobTypes.ReleaseUpgrade
            && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.Running)
            && j.JobData != null).AsEnumerable().Any(j => j.JobData!.Contains(chunkStoreId.ToString()));

        if (hasActiveJob)
            throw new GraphQLException("An upgrade job is already running or pending for this chunk store.");

        var jobData = new ReleaseUpgradeJobData
        {
            ChunkStoreId = chunkStoreId,
            TargetSerializerVersion = ReleasePackageSerializer.Version
        };

        var job = new BackgroundJob
        {
            Id = Guid.NewGuid(),
            JobType = BackgroundJobTypes.ReleaseUpgrade,
            Status = BackgroundJobStatus.Pending,
            JobData = JsonSerializer.Serialize(jobData),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.BackgroundJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        await _upgradeJobChannel.Writer.WriteAsync(job.Id, ct);

        return MapJobToGql(job, chunkStoreId);
    }

    private static BackgroundJobGql MapJobToGql(BackgroundJob job, Guid chunkStoreId) => new()
    {
        Id = job.Id,
        JobType = job.JobType,
        Status = job.Status.ToString(),
        ChunkStoreId = chunkStoreId,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        ErrorDetails = job.ErrorDetails
    };
}
