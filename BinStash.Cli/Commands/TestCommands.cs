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

using BinStash.Contracts.Release;
using BinStash.Core.Serialization;
using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;

namespace BinStash.Cli.Commands;

[Command("test", Description = "Provides tools to test parts of the program.")]
public class TestBaseCommand : ICommand
{
    public ValueTask ExecuteAsync(IConsole console)
    {
        // TODO: Add subcommands for analyzing chunk stores and repositories
        throw new CommandException("Please specify a subcommand for 'test'. Available subcommands: N/A.", showHelp: true);
    }
}

[Command("test serialization", Description = "Tests the serialization and deserialization of a given file using RPack serialization.")]
public class TestSerializationCommand : ICommand
{
    [CommandOption("target", 't', Description = "The target file to test serialization with/against.", IsRequired = true)]
    public string TargetFile { get; set; } = string.Empty;
    
    public async ValueTask ExecuteAsync(IConsole console)
    {
        if (!File.Exists(TargetFile))
            throw new FileNotFoundException($"The specified target file '{TargetFile}' does not exist.");

        var fileBytes = await File.ReadAllBytesAsync(TargetFile);
        ReleasePackage deserialized;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            deserialized = await ReleasePackageSerializer.DeserializeAsync(fileBytes);
            await console.Output.WriteLineAsync($"Deserialization successful. Time taken: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            await console.Output.WriteLineAsync($"Deserialization failed: {e.Message}");
            await console.Output.WriteLineAsync(e.StackTrace);
            return;
        }
        byte[] reserialized;
        sw.Restart();
        try
        {
            reserialized = await ReleasePackageSerializer.SerializeAsync(deserialized);
            await console.Output.WriteLineAsync($"Serialization successful. Time taken: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            await console.Output.WriteLineAsync($"Serialization failed: {e.Message}");
            await console.Output.WriteLineAsync(e.StackTrace);
            return;
        }
        sw.Stop();
        
        // Compare original and reserialized bytes
        if (fileBytes.Length != reserialized.Length)
        {
            await console.Output.WriteLineAsync("Mismatch: Original and reserialized data lengths differ.");
            //return;
        }
        for (var i = 0; i < fileBytes.Length; i++)
        {
            if (fileBytes[i] != reserialized[i])
            {
                await console.Output.WriteLineAsync($"Mismatch: Data differs at byte index {i}.");
                await File.WriteAllBytesAsync($"{TargetFile}.out", reserialized);
                return;
            }
        }
        
        await console.Output.WriteLineAsync("Success: Original and reserialized data match exactly.");
    }
}