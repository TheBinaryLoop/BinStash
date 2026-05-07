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

using BinStash.Cli.Infrastructure;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repo;
using BinStash.Core.Chunking;
using BinStash.Core.Entities;
using BinStash.Core.Ingestion.Abstractions;
using BinStash.Core.Ingestion.Execution;
using BinStash.Core.Ingestion.Models;
using Blake3;
using Spectre.Console;

namespace BinStash.Cli.Services.Releases;

public sealed class ReleaseAddOrchestrator
{
    private readonly ComponentMapLoader _componentMapLoader;
    private readonly IInputDiscoveryService _inputDiscoveryService;
    private readonly IReleaseIngestionEngine _releaseIngestionEngine;
    private readonly ReleasePackageBuilder  _releasePackageBuilder;
    private readonly IContentProcessor _contentProcessor;
    private readonly ServerUploadPlanner _serverUploadPlanner;

    public ReleaseAddOrchestrator(ComponentMapLoader componentMapLoader, IReleaseIngestionEngine releaseIngestionEngine, ReleasePackageBuilder releasePackageBuilder, IInputDiscoveryService inputDiscoveryService, IContentProcessor contentProcessor, ServerUploadPlanner serverUploadPlanner)
    {
        _componentMapLoader = componentMapLoader;
        _releaseIngestionEngine = releaseIngestionEngine;
        _inputDiscoveryService = inputDiscoveryService;
        _releasePackageBuilder = releasePackageBuilder;
        _contentProcessor = contentProcessor;
        _serverUploadPlanner = serverUploadPlanner;
    }

    public async Task RunAsync(IAnsiConsole console, BinStashApiClient restClient, BinStashGrpcClient grpcClient, ReleaseAddOrchestrationRequest request, Action<string>? log = null, CancellationToken ct = default)
    {
        await console.Status()
            .AutoRefresh(true)
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Fetching repos...", async ctx =>
            {
                var repositories = await restClient.GetRepositoriesAsync();
                if (repositories == null || repositories.Count == 0)
                    throw new InvalidOperationException("No repositories found. Please create a repository first.");

                log?.Invoke($"Found {repositories.Count} repositories");

                ctx.Status("Checking repository name...");
                var repository = repositories.FirstOrDefault(r =>
                    r.Name.Equals(request.RepositoryName, StringComparison.OrdinalIgnoreCase));

                if (repository == null)
                    throw new InvalidOperationException($"Repository '{request.RepositoryName}' not found.");

                ctx.Status("Checking release name duplicate...");
                var releases = await restClient.GetReleasesForRepoAsync(repository.Id);
                if (releases != null && releases.Any(r =>
                        r.Version.Equals(request.Version, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"Release with version '{request.Version}' already exists in repository '{repository.Name}'.");
                }

                ctx.Status($"Fetching chunker settings for repository '{repository.Name}'...");
                repository = await restClient.GetRepositoryAsync(repository.Id)
                             ?? throw new InvalidOperationException("Repository disappeared while fetching details.");

                if (repository.Chunker == null)
                    throw new InvalidOperationException($"Repository '{repository.Name}' does not have a chunker configured.");

                var chunker = CreateChunker(repository);

                log?.Invoke($"Using chunker: {chunker.GetType().Name}");

                ctx.Status("Creating component map...");
                var componentMap = _componentMapLoader.Load(
                    request.ComponentMapFile,
                    request.RootFolder,
                    log);

                if (componentMap.Count == 0)
                    throw new InvalidOperationException("No components found in the specified folder.");

                log?.Invoke($"Component map contains {componentMap.Count} components");

                ctx.Status("Discovering files...");
                var inputs = _inputDiscoveryService.DiscoverFiles(request.RootFolder, componentMap);

                log?.Invoke($"Discovered {inputs.Count} files");

                ctx.Status("Building ingestion graph...");
                var ingestionResult = await _releaseIngestionEngine.IngestAsync(inputs, componentMap, ct);

                log?.Invoke($"Ingestion produced {ingestionResult.OutputArtifacts.Count} output artifacts");
                log?.Invoke($"Ingestion produced {ingestionResult.StorageWorkItems.Count} storage work items");
                log?.Invoke($"Ingestion produced {ingestionResult.LogicalArtifacts.Count} logical artifacts");

                var outputArtifactBreakdown = ingestionResult.OutputArtifacts
                    .GroupBy(x => x.Backing.GetType().Name)
                    .Select(x => $"{x.Key}={x.Count()}")
                    .ToArray();

                log?.Invoke($"Output artifact backing breakdown: {string.Join(", ", outputArtifactBreakdown)}");

                ctx.Status("Requesting ingest session...");
                var ingestSessionId = await restClient.CreateIngestSessionAsync(repository.Id, request.Version);

                log?.Invoke($"Received ingest session ID: {ingestSessionId}");

                ctx.Status($"Hashing {ingestionResult.StorageWorkItems.Count} storage work items...");
                StorageHashingResult hashingResult;
                try
                {
                    hashingResult = _contentProcessor.HashStorageWorkItems(ingestionResult);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Hashing storage work items failed.", ex);
                }

                var bindContext = new IngestionExecutionContext(ingestionResult);
                foreach (var item in hashingResult.ItemResults)
                {
                    var workItem = hashingResult.WorkItems.First(x =>
                        string.Equals(x.Identity, item.WorkItemIdentity, StringComparison.OrdinalIgnoreCase));

                    bindContext.BindStoredContent(workItem, item.Hash, item.Length);
                }

                log?.Invoke($"Computed {hashingResult.ContentHashes.Count} unique stored content hashes");

                ctx.Status("Requesting missing file definitions...");
                var missingFileChecksums = await restClient.GetMissingFileChecksumsAsync(
                    repository.Id,
                    ingestSessionId,
                    hashingResult.ContentHashes.Keys.ToList());

                log?.Invoke($"Found {missingFileChecksums.Count} missing stored contents");

                ctx.Status("Generating chunk maps for missing stored contents...");
                var chunkMapResult = _contentProcessor.GenerateChunkMaps(
                    hashingResult,
                    ingestionResult,
                    chunker,
                    new HashSet<Hash32>(missingFileChecksums));

                log?.Invoke($"Generated chunk maps for {chunkMapResult.FileChunkMaps.Count} stored contents");

                ctx.Status("Planning upload...");
                var uploadPlan = await _serverUploadPlanner.CreateAsync(
                    restClient,
                    repository.Id,
                    ingestSessionId,
                    hashingResult,
                    chunkMapResult,
                    ct);

                log?.Invoke($"Found {uploadPlan.MissingChunkChecksums.Count} missing chunks");
                log?.Invoke($"Selected {uploadPlan.MissingChunkEntries.Count} chunk entries for upload");

                if (uploadPlan.MissingChunkEntries.Count > 0)
                {
                    ctx.Status("Uploading missing chunks...");
                    await grpcClient.UploadChunksAsync(
                        repository.Id,
                        ingestSessionId,
                        chunker,
                        uploadPlan.MissingChunkEntries.ToList(),
                        progressCallback: (uploaded, total) =>
                        {
                            ctx.Status($"Uploading missing chunks ({uploaded}/{total}, {(double)uploaded / total:P2})...");
                            return Task.CompletedTask;
                        },
                        cancellationToken: ct);
                }

                if (uploadPlan.FileDefinitions.Count > 0)
                {
                    ctx.Status("Uploading file definitions...");
                    await grpcClient.UploadFileDefinitionsAsync(
                        repository.Id,
                        ingestSessionId,
                        uploadPlan.FileDefinitions.ToDictionary(x => x.Key, x => x.Value),
                        progressCallback: (uploaded, total) =>
                        {
                            ctx.Status($"Uploading file definitions ({uploaded}/{total}, {(double)uploaded / total:P2})...");
                            return Task.CompletedTask;
                        },
                        cancellationToken: ct);
                }

                var releasePackage = _releasePackageBuilder.Build(
                    request,
                    ingestionResult,
                    repository.Id);

                ctx.Status("Creating release...");
                await restClient.CreateReleaseAsync(ingestSessionId, repository.Id.ToString(), releasePackage);

                console.MarkupLine("[green]Release created successfully![/]");
            });
    }

    private static IChunker CreateChunker(RepositorySummaryDto repository)
    {
        var chunkerType = Enum.TryParse<ChunkerType>(repository.Chunker!.Type, true, out var parsed)
            ? parsed
            : ChunkerType.FastCdc;

        return chunkerType switch
        {
            ChunkerType.FastCdc => new FastCdcChunker(
                repository.Chunker.MinChunkSize!.Value,
                repository.Chunker.AvgChunkSize!.Value,
                repository.Chunker.MaxChunkSize!.Value),
            _ => throw new NotSupportedException($"Unsupported chunker type: {repository.Chunker.Type}")
        };
    }
}

public sealed record ReleaseAddOrchestrationRequest(
    string Version,
    string? Notes,
    string RepositoryName,
    string RootFolder,
    string? ComponentMapFile,
    Dictionary<string, string> CustomProperties);