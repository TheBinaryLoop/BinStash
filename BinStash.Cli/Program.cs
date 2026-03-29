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

using BinStash.Cli.Services.Releases;
using BinStash.Core.Ingestion.Abstractions;
using BinStash.Core.Ingestion.Execution;
using BinStash.Core.Ingestion.Formats.Plain;
using BinStash.Core.Ingestion.Formats.Zip;
using CliFx;
using Microsoft.Extensions.DependencyInjection;

namespace BinStash.Cli;

public static class Program
{
    public static async Task<int> Main() =>
        await new CliApplicationBuilder()
            .AddCommandsFromThisAssembly()
            .UseTypeActivator(commandTypes =>
            {
                var services = new ServiceCollection();
                
                // Register services
                
                // Core ingestion/execution services
                services.AddSingleton<IInputDiscoveryService, InputDiscoveryService>();
                services.AddSingleton<IContentProcessor, ContentProcessor>();

                services.AddSingleton<IInputFormatDetector, DefaultInputFormatDetector>();
                services.AddSingleton<IIngestionPlanner, DefaultIngestionPlanner>();

                services.AddSingleton<ZipArchiveInspector>();
                services.AddSingleton<ZipEntryStreamFactory>();
                services.AddSingleton<ZipMemberSelectionPolicy>();
                services.AddSingleton<ZipReconstructionPlanner>();
                services.AddSingleton<ZipRecipeBuilder>();
                
                services.AddSingleton<IInputFormatHandler, PlainFileFormatHandler>();
                services.AddSingleton<IInputFormatHandler, ZipFormatHandler>();
                services.AddSingleton<ReleasePackageBuilder>();
                services.AddSingleton<IReleaseIngestionEngine,  ReleaseIngestionEngine>();
                
                // CLI release services
                services.AddSingleton<ComponentMapLoader>();
                services.AddSingleton<ServerUploadPlanner>();
                services.AddSingleton<ReleaseAddOrchestrator>();
                
                // Register commands
                foreach (var commandType in commandTypes)
                    services.AddTransient(commandType);

                return services.BuildServiceProvider();

            })
#if DEBUG
            .AllowDebugMode()
#endif
            .Build()
            .RunAsync();
        
}