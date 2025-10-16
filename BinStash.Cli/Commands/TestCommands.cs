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
using BinStash.Core.Serialization.Utils;
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

[Command("test release-pack-patch", Description = "Tests the generation and application of release package patches.")]
public class TestReleasePackagePatchCommand : ICommand
{
    [CommandOption("parent", 'p', Description = "The parent.", IsRequired = true)]
    public string ParentFile { get; set; } = string.Empty;
    
    [CommandOption("child", 'c', Description = "The child.", IsRequired = true)]
    public string ChildFile { get; set; } = string.Empty;
    
    public async ValueTask ExecuteAsync(IConsole console)
    {
        if (!File.Exists(ParentFile))
            throw new FileNotFoundException($"The specified parent file '{ParentFile}' does not exist.");
        
        if (!File.Exists(ChildFile))
            throw new FileNotFoundException($"The specified child file '{ChildFile}' does not exist.");

        var parentFileBytes = await File.ReadAllBytesAsync(ParentFile);
        ReleasePackage parent;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            parent = await ReleasePackageSerializer.DeserializeAsync(parentFileBytes);
            await console.Output.WriteLineAsync($"Deserialization of parent successful. Time taken: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            await console.Output.WriteLineAsync($"Deserialization of parent failed: {e.Message}");
            await console.Output.WriteLineAsync(e.StackTrace);
            return;
        }
        
        var childFileBytes = await File.ReadAllBytesAsync(ChildFile);
        ReleasePackage child;
        sw.Restart();
        try
        {
            child = await ReleasePackageSerializer.DeserializeAsync(childFileBytes);
            await console.Output.WriteLineAsync($"Deserialization of child successful. Time taken: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            await console.Output.WriteLineAsync($"Deserialization of child failed: {e.Message}");
            await console.Output.WriteLineAsync(e.StackTrace);
            return;
        }
        
        var patch = ReleasePackagePatcher.CreatePatch(parent, child, 1, logMessage => console.Output.WriteLine(logMessage));
        
        await console.Output.WriteLineAsync($"Patch creation successful.");
        
        await File.WriteAllBytesAsync(@"C:\tmp\patch.rpackpatch", await ReleasePackagePatchSerializer.SerializeAsync(patch, new ReleasePackageSerializerOptions { EnableCompression = true }));
        patch = await ReleasePackagePatchSerializer.DeserializeAsync(await File.ReadAllBytesAsync(@"C:\tmp\patch.rpackpatch"));
        var reconstructedChild = ReleasePackagePatcher.ApplyPatch(parent, patch);
        
        
        // === Structural equivalence check (instead of byte comparison) ===
        if (ReleasePackagesEqual(child, reconstructedChild, out var diff))
        {
            await console.Output.WriteLineAsync("✅ Success: reconstructed child matches the original child (structurally).");
        }
        else
        {
            await console.Output.WriteLineAsync("❌ Mismatch: reconstructed child differs from the original.");
            await console.Output.WriteLineAsync($"First difference: {diff}");
        }
    }
    
    private static bool ReleasePackagesEqual(ReleasePackage a, ReleasePackage b, out string diff)
    {
        diff = string.Empty;

        if (a.Version != b.Version) { diff = $"Version differs: {a.Version} vs {b.Version}"; return false; }
        if (a.ReleaseId != b.ReleaseId) { diff = $"ReleaseId differs"; return false; }
        if (a.RepoId != b.RepoId) { diff = $"RepoId differs"; return false; }
        if (a.Notes != b.Notes) { diff = $"Notes differ"; return false; }
        if (a.CreatedAt != b.CreatedAt) { diff = $"CreatedAt differs"; return false; }

        if (a.CustomProperties.Count != b.CustomProperties.Count) { diff = $"CustomProperties count differs"; return false; }
        foreach (var kv in a.CustomProperties)
        {
            if (!b.CustomProperties.TryGetValue(kv.Key, out var val) || val != kv.Value)
            { diff = $"CustomProperties mismatch at key {kv.Key}"; return false; }
        }

        if (a.Chunks.Count != b.Chunks.Count) { diff = $"Chunks count differs"; return false; }
        for (int i = 0; i < a.Chunks.Count; i++)
        {
            if (!a.Chunks[i].Checksum.SequenceEqual(b.Chunks[i].Checksum))
            { diff = $"Chunk checksum mismatch at index {i}"; return false; }
        }

        if (a.StringTable.Count != b.StringTable.Count) { diff = $"StringTable count differs. A: {a.StringTable.Count}; B: {b.StringTable.Count}"; return false; }
        for (int i = 0; i < a.StringTable.Count; i++)
        {
            if (a.StringTable[i] != b.StringTable[i])
            { diff = $"StringTable entry mismatch at {i}"; return false; }
        }

        if (a.Components.Count != b.Components.Count) { diff = $"Components count differs"; return false; }
        for (int ci = 0; ci < a.Components.Count; ci++)
        {
            var ca = a.Components[ci];
            var cb = b.Components[ci];
            if (ca.Name != cb.Name) { diff = $"Component name differs at index {ci}"; return false; }
            if (ca.Files.Count != cb.Files.Count) { diff = $"File count differs in component {ca.Name}"; return false; }

            for (int fi = 0; fi < ca.Files.Count; fi++)
            {
                var fa = ca.Files[fi];
                var fb = cb.Files[fi];
                if (fa.Name != fb.Name) { diff = $"File name differs in {ca.Name} at index {fi}"; return false; }
                if (fa.Hash != fb.Hash) { diff = $"File hash differs in {fa.Name}"; return false; }
                if (fa.Chunks.Count != fb.Chunks.Count) { diff = $"Chunk count differs in {fa.Name}"; return false; }

                for (int chi = 0; chi < fa.Chunks.Count; chi++)
                {
                    var cha = fa.Chunks[chi];
                    var chb = fb.Chunks[chi];
                    if (cha.DeltaIndex != chb.DeltaIndex || cha.Offset != chb.Offset || cha.Length != chb.Length)
                    { diff = $"Chunk mismatch in {fa.Name} at index {chi}"; return false; }
                }
            }
        }

        if (a.Stats.ComponentCount != b.Stats.ComponentCount) { diff = "Stats.ComponentCount differs"; return false; }
        if (a.Stats.FileCount != b.Stats.FileCount) { diff = "Stats.FileCount differs"; return false; }
        if (a.Stats.ChunkCount != b.Stats.ChunkCount) { diff = "Stats.ChunkCount differs"; return false; }
        if (a.Stats.RawSize != b.Stats.RawSize) { diff = "Stats.RawSize differs"; return false; }

        return true;
    }
}