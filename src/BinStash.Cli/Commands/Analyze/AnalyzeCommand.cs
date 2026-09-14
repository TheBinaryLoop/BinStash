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

using BinStash.Cli.Clients;
using BinStash.Cli.Services.Analysis;
using BinStash.Contracts.Repo;
using BinStash.Core.Chunking;
using BinStash.Core.Entities;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using Spectre.Console;

namespace BinStash.Cli.Commands.Analyze;

/// <summary>
/// Predicts what publishing a build would cost — how much of it the store already holds, how much
/// would cross the wire, and how much space it would occupy once packed.
/// </summary>
/// <remarks>
/// Doubles as the root of the <c>analyze</c> command group: with a path it analyzes, without one
/// it prints the group's help. That is why the path is optional and why the credential pre-check
/// is deferred — asking for a server URL before telling the user what the command does would be
/// backwards.
/// </remarks>
[Command("analyze", Description = "Estimate what uploading a file or folder would cost against a repository's chunk store.")]
public partial class AnalyzeCommand : TenantCommandBase
{
    // Nullable, and deliberately not `required`: that is how CliFx 3 infers an optional
    // parameter, and it is what lets bare `analyze` fall through to the group's help.
    [CommandParameter(0, Name = "path", Description = "The file or folder to analyze.")]
    public string? TargetPath { get; set; }

    [CommandOption("repository", 'r', Description = "Repository whose chunk store the target is compared against.")]
    public string RepositoryName { get; set; } = string.Empty;

    [CommandOption("sample", 's', Description = "How many new chunks to compress when estimating the packed size. 0 compresses every one of them — exact, and about as expensive as the upload itself.")]
    public int CompressionSampleChunks { get; set; } = 512;

    private readonly DedupAnalysisService _analysisService;

    public AnalyzeCommand(DedupAnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    protected override ValueTask<bool> PreCheckAsync(IConsole console)
    {
        // No path means this invocation is the group's help, which needs neither a server nor a
        // credential. Checking for them first would reject `analyze` with "pre-checks failed"
        // instead of explaining what analyze is.
        if (string.IsNullOrWhiteSpace(TargetPath))
            throw new CommandException("Specify a file or folder to analyze, or a subcommand. Available subcommands: chunker.", showHelp: true);

        return base.PreCheckAsync(console);
    }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        // Non-null past the pre-check, which turns an absent path into the group's help.
        var targetPath = TargetPath!;

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            throw new CommandException($"'{targetPath}' does not exist.");

        if (string.IsNullOrWhiteSpace(RepositoryName))
            throw new CommandException("A repository must be specified with --repository, so the target can be compared against its chunk store.");

        var ansiConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            ColorSystem = ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(console.Output)
        });

        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory, authScheme: AuthScheme);
        var tenantId = GetTenantId();

        var repository = await ResolveRepositoryAsync(client, tenantId);
        var chunker = CreateChunker(repository);

        var displayName = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var targetSize = Directory.Exists(targetPath) ? DirectorySize(targetPath) : new FileInfo(targetPath).Length;

        ansiConsole.MarkupLineInterpolated($"Scanning [bold]{displayName}[/] ({FormatBytes(targetSize)})...");

        DedupAnalysisResult result = null!;
        await ansiConsole.Status()
            .AutoRefresh(true)
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Chunking...", async ctx =>
            {
                var progress = new Progress<string>(message => ctx.Status($"{message}..."));
                var request = new DedupAnalysisRequest(client, tenantId, repository.Id, targetPath, chunker, CompressionSampleChunks);
                result = await _analysisService.AnalyzeAsync(request, progress, console.RegisterCancellationHandler());
            });

        Render(ansiConsole, result, repository.Name);
    }

    private static void Render(IAnsiConsole console, DedupAnalysisResult result, string repositoryName)
    {
        console.MarkupLineInterpolated($"Found [bold]{result.TotalChunks:N0}[/] chunks across {result.FileCount:N0} file(s)");
        console.MarkupLineInterpolated($"Compared against repository [bold]{repositoryName}[/]");
        console.WriteLine();

        var table = new Table().Border(TableBorder.None).HideHeaders();
        table.AddColumn(new TableColumn("Label"));
        table.AddColumn(new TableColumn("Value").RightAligned());

        table.AddRow("Total Size", FormatBytes(result.TotalLogicalBytes));
        table.AddRow("Stored Chunks", $"[green]{result.StoredChunks:N0}[/] ({result.StoredFraction:P1})");
        table.AddRow("New Chunks", $"[yellow]{result.NewChunks:N0}[/] ({result.NewFraction:P1})");
        table.AddRow("Upload Size", FormatBytes(result.UploadBytes));
        table.AddRow(result.CompressionSampled ? "Est. Pack Size" : "Pack Size", $"{FormatBytes(result.EstimatedPackBytes)} [grey](with Zstd)[/]");

        console.Write(table);
        console.WriteLine();

        if (result.TotalLogicalBytes > 0)
            console.MarkupLineInterpolated($"[grey]Deduplication avoids uploading {result.DeduplicationSaving:P1} of the input.[/]");

        if (result.CompressionSampled)
            console.MarkupLine("[grey]Packed size is extrapolated from a sample of the new chunks; pass --sample 0 to compress all of them.[/]");
    }

    private async Task<RepositorySummaryDto> ResolveRepositoryAsync(BinStashApiClient client, Guid tenantId)
    {
        var repositories = await client.GetRepositoriesAsync(tenantId);
        if (repositories == null || repositories.Count == 0)
            throw new CommandException("No repositories found. Create a repository first.");

        var repository = repositories.FirstOrDefault(r => r.Name.Equals(RepositoryName, StringComparison.OrdinalIgnoreCase));
        if (repository == null)
            throw new CommandException($"Repository '{RepositoryName}' not found. Available: {string.Join(", ", repositories.Select(r => r.Name))}");

        if (repository.Chunker == null)
            throw new CommandException($"Repository '{repository.Name}' does not have a chunker configured.");

        return repository;
    }

    /// <summary>
    /// The repository's own chunker settings. Chunk boundaries depend on them, and therefore so
    /// does which chunks the store already holds — analyzing with anything else would produce a
    /// figure the real upload could not reproduce.
    /// </summary>
    private static IChunker CreateChunker(RepositorySummaryDto repository)
    {
        var chunkerType = Enum.TryParse<ChunkerType>(repository.Chunker!.Type, true, out var parsed) ? parsed : ChunkerType.FastCdc;

        return chunkerType switch
        {
            ChunkerType.FastCdc => new FastCdcChunker(repository.Chunker.MinChunkSize!.Value, repository.Chunker.AvgChunkSize!.Value, repository.Chunker.MaxChunkSize!.Value),
            _ => throw new NotSupportedException($"Unsupported chunker type: {repository.Chunker.Type}")
        };
    }

    private static long DirectorySize(string path)
        => new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes:N0} {units[unit]}" : $"{value:N2} {units[unit]}";
    }
}
