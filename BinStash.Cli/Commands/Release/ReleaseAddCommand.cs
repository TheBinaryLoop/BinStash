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

using BinStash.Cli.Converters;
using BinStash.Cli.Infrastructure;
using BinStash.Cli.Services.Releases;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using Spectre.Console;

namespace BinStash.Cli.Commands.Release;

[Command("release add", Description = "Add a new release")]
public partial class ReleaseAddCommand : TenantCommandBase
{
    [CommandOption("version", 'v', Description = "The version/name of the release")]
    public required string Version { get; set; } = string.Empty;
    
    [CommandOption("notes", 'n', Description = "Release notes or description")]
    public string Note { get; set; } = string.Empty;
    
    [CommandOption("notes-file", Description = "File containing release notes or description")]
    public string NoteFile { get; set; } = string.Empty;

    [CommandOption("repository", 'r', Description = "Repository for the release")]
    public required string RepositoryName { get; set; } = string.Empty;
    
    [CommandOption("folder", 'f', Description = "Folder containing the release files")]
    public required string RootFolder { get; set; } = string.Empty;

    [CommandOption("component-map", 'c', Description = "The path to the component map file.")]
    public string ComponentMapFile { get; set; } = string.Empty;
    
    [CommandOption("custom-property", 'p', Description = "Custom property to add to the release. Can be specified multiple times.", Converter = typeof(DictionaryConverter))]
    public Dictionary<string, string> CustomProperties { get; set; } = new();

    private readonly ReleaseAddOrchestrator _orchestrator;

    public ReleaseAddCommand(ReleaseAddOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }
    
    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        ValidateInputs();

        string? noteContent = string.IsNullOrWhiteSpace(Note) ? null : Note;
        if (!string.IsNullOrWhiteSpace(NoteFile))
        {
            if (!File.Exists(NoteFile))
                throw new CommandException($"The specified note file '{NoteFile}' does not exist or is not a file.");

            noteContent = await File.ReadAllTextAsync(NoteFile);
        }

        var ansiConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            ColorSystem = ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(console.Output)
        });

        var request = new ReleaseAddOrchestrationRequest(
            Version: Version,
            Notes: noteContent,
            RepositoryName: RepositoryName,
            RootFolder: RootFolder,
            ComponentMapFile: string.IsNullOrWhiteSpace(ComponentMapFile) ? null : ComponentMapFile,
            CustomProperties: CustomProperties);

        var restClient = new BinStashApiClient(GetUrl(), AuthTokenFactory);
        var grpcClient = new BinStashGrpcClient(GetUrl(), AuthTokenFactory);

        await _orchestrator.RunAsync(ansiConsole, restClient, grpcClient, request, message => ansiConsole.MarkupLine($"[grey]LOG:[/] {message}[grey]...[/]"), CancellationToken.None);
    }

    private void ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(Version))
            throw new CommandException("A release version must be specified.");

        if (string.IsNullOrWhiteSpace(RepositoryName))
            throw new CommandException("A repository name must be specified.");

        if (string.IsNullOrWhiteSpace(RootFolder))
            throw new CommandException("A root folder must be specified.");

        if (!Directory.Exists(RootFolder))
            throw new CommandException($"The specified folder '{RootFolder}' does not exist or is not a directory.");

        if (!string.IsNullOrWhiteSpace(ComponentMapFile) && !File.Exists(ComponentMapFile))
            throw new CommandException($"The specified component map file '{ComponentMapFile}' does not exist or is not a file.");
    }
}