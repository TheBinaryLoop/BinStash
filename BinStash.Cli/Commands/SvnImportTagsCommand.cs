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
using BinStash.Cli.Infrastructure.Svn;
using BinStash.Cli.Services.Releases;
using BinStash.Core.Chunking;
using BinStash.Core.Entities;
using BinStash.Core.Ingestion.Abstractions;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;

namespace BinStash.Cli.Commands;

[Command("svn import-tags", Description = "Import SVN tags as BinStash releases")]
public sealed partial class SvnImportTagsCommand : TenantCommandBase
{
    private readonly IReleaseIngestionEngine _releaseIngestionEngine;
    private readonly IContentProcessor _contentProcessor;
    private readonly ServerUploadPlanner _serverUploadPlanner;
    private readonly ReleasePackageBuilder _releasePackageBuilder;

    public SvnImportTagsCommand(IReleaseIngestionEngine releaseIngestionEngine, IContentProcessor contentProcessor, ServerUploadPlanner serverUploadPlanner, ReleasePackageBuilder releasePackageBuilder)
    {
        _releaseIngestionEngine = releaseIngestionEngine;
        _contentProcessor = contentProcessor;
        _serverUploadPlanner = serverUploadPlanner;
        _releasePackageBuilder = releasePackageBuilder;
    }
    
    [CommandOption("repo", 'r', Description = "BinStash repository name")]
    public required string RepositoryName { get; set; } = string.Empty;

    [CommandOption("svn-root", 's', Description = "SVN tags root URL")]
    public required string SvnRoot { get; set; } = string.Empty;

    [CommandOption("state-file", Description = "SQLite file for resumable state")]
    public required string StateFile { get; set; } = string.Empty;

    [CommandOption("svn-user", Description = "SVN username")]
    public string? SvnUser { get; set; }

    [CommandOption("svn-password", Description = "SVN password")]
    public string? SvnPassword { get; set; }

    [CommandOption("resume", Description = "Resume unfinished import")]
    public bool Resume { get; set; }

    [CommandOption("limit", Description = "Maximum number of tags to import")]
    public int? Limit { get; set; }

    [CommandOption("dry-run", Description = "Scan and plan without uploading")]
    public bool DryRun { get; set; }

    [CommandOption("concurrency", Description = "Max concurrent SVN file reads")]
    public int Concurrency { get; set; } = 4;

    [CommandOption("component-map", Description = "Optional component map file in format <svnSubPath>:<componentName>")]
    public string? ComponentMapFile { get; set; }

    [CommandOption("exclude", Description = "Exclude glob pattern. Can be repeated.")]
    public string[] Excludes { get; set; } = [];

    [CommandOption("include", Description = "Include glob pattern. Can be repeated.")]
    public string[] Includes { get; set; } = [];

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var api = new BinStashApiClient(GetUrl(), AuthTokenFactory, console);
        var ingestClient = new BinStashGrpcClient(GetUrl(), AuthTokenFactory);

        var repositories = await api.GetRepositoriesAsync();
        var repository = repositories?.FirstOrDefault(r => r.Name.Equals(RepositoryName, StringComparison.OrdinalIgnoreCase));
        if (repository == null)
            throw new CommandException($"Repository '{RepositoryName}' not found.");

        repository = await api.GetRepositoryAsync(repository.Id);
        if (repository == null)
            throw new CommandException($"Repository '{RepositoryName}' could not be loaded.");

        if (repository.Chunker == null)
            throw new CommandException($"Repository '{repository.Name}' does not have a chunker configured.");

        var chunker = (Enum.TryParse<ChunkerType>(repository.Chunker.Type, true, out var chunkerType) ? chunkerType : ChunkerType.FastCdc) switch
        {
            ChunkerType.FastCdc => new FastCdcChunker(
                repository.Chunker.MinChunkSize!.Value,
                repository.Chunker.AvgChunkSize!.Value,
                repository.Chunker.MaxChunkSize!.Value),
            _ => throw new NotSupportedException($"Unsupported chunker type: {repository.Chunker.Type}")
        };

        var svn = new SvnCliClient(SvnUser, SvnPassword);
        await using var state = new ImportStateStore(StateFile);
        await state.InitializeAsync();

        var componentMapper = SvnComponentMapper.Load(ComponentMapFile);

        var engine = new SvnImportEngine(svn, api, ingestClient, state, repository, chunker, console, Concurrency, componentMapper, Includes, Excludes, _releaseIngestionEngine, _contentProcessor, _serverUploadPlanner, _releasePackageBuilder);

        await engine.RunAsync(svnRoot: SvnRoot, tenantSlug: TenantSlug!, dryRun: DryRun, resume: Resume, limit: Limit);
    }
}