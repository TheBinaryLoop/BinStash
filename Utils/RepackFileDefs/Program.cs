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

using System.Diagnostics;
using BinStash.Contracts.Hashing;
using BinStash.Infrastructure.Storage;
using BinStash.Infrastructure.Storage.Indexing;
using BinStash.Infrastructure.Storage.Packing;
using Blake3;

// ============================================================
//  BinStash.RepackFileDefs — cross-bucket FileDef repack
//
//  Fixes the bucket-mismatch bug introduced by BinStash.StoreMigration:
//  each FileDef pack entry was written to the prefix bucket corresponding
//  to the OLD file-hash prefix, but at read-time ObjectStore routes by
//  storageKey = BLAKE3(blob) whose first 3 hex digits identify the bucket.
//
//  This tool:
//    1. Scans all 4096 FileDefs prefix buckets and reads every pack entry.
//    2. For each entry, computes storageKey = BLAKE3(blob) and derives the
//       correct 3-hex prefix.
//    3. Groups entries by correct prefix and writes new *.pack files to the
//       correct bucket directories.
//    4. Deletes all stale index files (*.seg-*.idx, *.seg-*.bloom, index.log).
//    5. Calls ObjectStore.RebuildStorageAsync() to regenerate all seg files
//       from the correctly-routed pack files.
//
//  Usage:
//    BinStash.RepackFileDefs <storeRoot>
//
//  Example:
//    BinStash.RepackFileDefs "C:\Tmp\BinStash\SecondLocalStoreSetup"
// ============================================================

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: BinStash.RepackFileDefs <storeRoot>");
    return 1;
}

var storeRoot    = args[0];
var fileDefsRoot = Path.Combine(storeRoot, "FileDefs");

if (!Directory.Exists(fileDefsRoot))
{
    Console.Error.WriteLine($"ERROR: FileDefs directory not found: {fileDefsRoot}");
    return 1;
}

Console.WriteLine("=== BinStash FileDef Cross-Bucket Repack ===");
Console.WriteLine($"Store root : {storeRoot}");
Console.WriteLine($"FileDefs   : {fileDefsRoot}");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 1: Scan all pack files and group blobs by correct target prefix
// -----------------------------------------------------------------------
Console.WriteLine("[1/4] Scanning all FileDef pack files...");
var sw = Stopwatch.StartNew();

// correctPrefix -> list of (storageKey, decompressedBlob)
var byTargetPrefix = new Dictionary<string, List<(Hash32 StorageKey, byte[] Blob)>>(4096);

long totalEntries   = 0;
long totalBytes     = 0;
int  scannedPacks   = 0;
int  skippedEntries = 0;

for (var i = 0; i < 4096; i++)
{
    var prefix    = i.ToString("x3");
    var bucketDir = Path.Combine(fileDefsRoot, prefix[..2]);

    if (!Directory.Exists(bucketDir))
        continue;

    // Each bucket dir may have one or more pack files named fileDefs{prefix}-N.pack
    var packFiles = Directory.GetFiles(bucketDir, $"fileDefs{prefix}-*.pack");
    foreach (var packPath in packFiles)
    {
        scannedPacks++;
        try
        {
            await using var fs = new FileStream(
                packPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 65536,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            await foreach (var (_, length, blob) in PackFileEntry.ReadAllEntriesAsync(fs))
            {
                // Compute the correct storage key = BLAKE3(decompressed blob)
                var storageKey    = new Hash32(Hasher.Hash(blob).AsSpan());
                var correctPrefix = storageKey.ToHexString()[..3];

                if (!byTargetPrefix.TryGetValue(correctPrefix, out var list))
                {
                    list = [];
                    byTargetPrefix[correctPrefix] = list;
                }

                list.Add((storageKey, blob));
                totalEntries++;
                totalBytes += blob.Length;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  WARN: Skipping {packPath}: {ex.GetType().Name}: {ex.Message}");
            skippedEntries++;
        }
    }
}

sw.Stop();
Console.WriteLine($"      Scanned {scannedPacks} pack files, {totalEntries} entries ({totalBytes / 1024.0 / 1024.0:F1} MiB uncompressed).");
Console.WriteLine($"      Distinct target prefixes: {byTargetPrefix.Count}");
if (skippedEntries > 0)
    Console.WriteLine($"      Skipped (corrupt/unreadable): {skippedEntries}");
Console.WriteLine($"      Scan duration: {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 2: Write new pack files routed to the correct prefix buckets
// -----------------------------------------------------------------------
Console.WriteLine("[2/4] Writing correctly-routed pack files...");
sw.Restart();

var writtenEntries = 0;
var writtenPacks   = 0;

// Build the complete set of all 4096 prefixes (some may receive 0 entries).
// We write new files as -0.pack.new, then atomically rename.
foreach (var (targetPrefix, entries) in byTargetPrefix)
{
    if (entries.Count == 0)
        continue;

    var bucketDir = Path.Combine(fileDefsRoot, targetPrefix[..2]);
    Directory.CreateDirectory(bucketDir);

    var finalPackPath = Path.Combine(bucketDir, $"fileDefs{targetPrefix}-0.pack");
    var tempPackPath  = finalPackPath + ".new";

    // Deduplicate by storageKey — keep first occurrence
    var seen = new HashSet<Hash32>(entries.Count);

    await using var packStream = new FileStream(
        tempPackPath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 65536,
        options: FileOptions.Asynchronous);

    foreach (var (storageKey, blob) in entries)
    {
        if (!seen.Add(storageKey))
            continue; // duplicate — skip

        await PackFileEntry.WriteAsync(packStream, blob.AsMemory());
        writtenEntries++;
    }

    await packStream.FlushAsync();
    // Close the stream before the atomic rename (Windows requires no open handles)
    await packStream.DisposeAsync();

    FileAtomicHelper.ReplaceAtomic(tempPackPath, finalPackPath);
    writtenPacks++;
}

sw.Stop();
Console.WriteLine($"      Wrote {writtenEntries} entries into {writtenPacks} pack files in {sw.Elapsed.TotalSeconds:F1}s.");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 3: Delete all stale index/bloom/log files so RebuildStorage starts clean
// -----------------------------------------------------------------------
Console.WriteLine("[3/4] Deleting stale index files...");
sw.Restart();
var deletedFiles = 0;

for (var i = 0; i < 4096; i++)
{
    var prefix    = i.ToString("x3");
    var bucketDir = Path.Combine(fileDefsRoot, prefix[..2]);

    if (!Directory.Exists(bucketDir))
        continue;

    // Delete all seg files for this prefix
    foreach (var segFile in Directory.GetFiles(bucketDir, $"fileDefs{prefix}.seg-*.idx"))
    {
        File.Delete(segFile);
        deletedFiles++;
    }

    // Delete all bloom files
    foreach (var bloomFile in Directory.GetFiles(bucketDir, $"fileDefs{prefix}.seg-*.bloom"))
    {
        File.Delete(bloomFile);
        deletedFiles++;
    }

    // Delete the append log if present
    var logFile = Path.Combine(bucketDir, $"fileDefs{prefix}.index.log");
    if (File.Exists(logFile)) { File.Delete(logFile); deletedFiles++; }

    // Legacy seg/bloom without prefix
    var legacySeg   = Path.Combine(bucketDir, "seg-000.idx");
    var legacyBloom = Path.Combine(bucketDir, "seg-000.bloom");
    if (File.Exists(legacySeg))   { File.Delete(legacySeg);   deletedFiles++; }
    if (File.Exists(legacyBloom)) { File.Delete(legacyBloom); deletedFiles++; }
}

sw.Stop();
Console.WriteLine($"      Deleted {deletedFiles} stale index/bloom/log files in {sw.Elapsed.TotalSeconds:F1}s.");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 4: Rebuild all index files from the correctly-routed pack files
// -----------------------------------------------------------------------
Console.WriteLine("[4/4] Rebuilding FileDef index files via ObjectStore.RebuildStorageAsync()...");
sw.Restart();

using var objectStore = new ObjectStore(storeRoot);
var rebuildOk = await objectStore.RebuildStorageAsync();

sw.Stop();
Console.WriteLine($"      RebuildStorageAsync returned: {(rebuildOk ? "OK" : "PARTIAL FAILURE")} in {sw.Elapsed.TotalSeconds:F1}s.");
Console.WriteLine();

if (!rebuildOk)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("WARNING: One or more prefix buckets failed to rebuild their index. " +
                      "Check the output above for errors. The pack files are correct; re-running this " +
                      "tool will retry the index rebuild.");
    Console.ResetColor();
    Console.WriteLine();
}

Console.WriteLine("=== Repack complete. ===");
Console.WriteLine($"  Total entries repacked : {writtenEntries}");
Console.WriteLine($"  Pack files written     : {writtenPacks}");
Console.WriteLine($"  Stale files deleted    : {deletedFiles}");
Console.WriteLine($"  Index rebuild          : {(rebuildOk ? "PASSED" : "PARTIAL FAILURE")}");
Console.WriteLine();
Console.WriteLine("Run BinStash.StoreVerify to confirm end-to-end retrieval.");

return rebuildOk ? 0 : 1;
