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

using BinStash.Core.Chunking;
using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;

namespace BinStash.Cli.Commands;

[Command("analyze", Description = "Provides tools to tune chunkers and deduplication settings for repositories and chunk stores.")]
public class AnalyzeBaseCommand : ICommand
{
    public ValueTask ExecuteAsync(IConsole console)
    {
        // TODO: Add subcommands for analyzing chunk stores and repositories
        throw new CommandException("Please specify a subcommand for 'analyze'. Available subcommands: chunker.", showHelp: true);
    }
}

[Command("analyze chunker", Description = "Recommends chunker settings based on the target folder's content.")]
public class AnalyzeChunkerCommand : ICommand
{
    [CommandOption("target", 't', Description = "The target folder to analyze for chunker settings.", IsRequired = true)]
    public string TargetFolder { get; set; } = string.Empty;
    
    public async ValueTask ExecuteAsync(IConsole console)
    {
        if (!Directory.Exists(TargetFolder))
            throw new DirectoryNotFoundException($"The specified target folder '{TargetFolder}' does not exist.");

        var chunker = new FastCdcChunker(0, 0, 0);
        var recommendation = await chunker.RecommendChunkerSettingsForTargetAsync(TargetFolder, ChunkAnalysisTarget.Dedupe, (logMessage) => console.WriteLine(logMessage), console.RegisterCancellationHandler());
        
        await console.Output.WriteLineAsync("Recommended Chunker Settings:");
        await console.Output.WriteLineAsync(recommendation.Summary);
    }
}