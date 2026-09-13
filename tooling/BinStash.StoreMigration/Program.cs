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
using BinStash.Core.Compression;
using BinStash.Infrastructure.Storage.FileDefinition;
using BinStash.Infrastructure.Storage.Indexing;
using BinStash.Infrastructure.Storage.Packing;
using BinStash.StoreMigration;
using Npgsql;

// ============================================================
//  BinStash.StoreMigration — one-shot FileDef store migration
//
//  Converts a pre-LSM FileDef store to the current on-disk format.
//
//  The legacy store (written before the BINST-99 rewrite) keeps, per bucket:
//    - a flat varint index  index{prefix}.idx  keyed by the FILE HASH, and
//    - pack entries whose payload is a bare TransposeCompress(chunkHashes)
//      blob with no self-describing header.
//
//  The current store expects, per bucket:
//    - an LSM segment fileDefs{prefix}.seg-NNN.idx (+ .bloom), and
//    - pack entries holding a FileDefinitionRecord ("BSFD" magic) that embeds
//      the FileHash, so the index can be rebuilt from the packs alone.
//
//  Because the legacy index is already keyed by the file hash, every entry
//  stays in the bucket it is in: this is a pure in-bucket payload rewrite.
//  The file length is the only field the legacy payload does not carry; it is
//  read from FileDefinitions.Length in PostgreSQL.
//
//  The database is only ever READ. Nothing here writes to PostgreSQL.
//
//  Usage:
//    BinStash.StoreMigration <storeRoot> <connectionString> [--apply]
//
//  Without --apply the tool performs a read-only survey and reports what it
//  would do. Take a copy of <storeRoot>/FileDefs before running with --apply.
// ============================================================

var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
var positional = args.Where(static a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

if (positional.Length < 2)
{
    Console.Error.WriteLine("Usage: BinStash.StoreMigration <storeRoot> <connectionString> [--apply]");
    return 1;
}

var storeRoot    = positional[0];
var connString   = positional[1];
var fileDefsRoot = Path.Combine(storeRoot, "FileDefs");

if (!Directory.Exists(fileDefsRoot))
{
    Console.Error.WriteLine($"ERROR: FileDefs directory not found: {fileDefsRoot}");
    return 1;
}

const long maxPackSize = 4L * 1024 * 1024 * 1024; // must match ObjectStore.MaxPackSize

Console.WriteLine("=== BinStash FileDef Store Migration ===");
Console.WriteLine($"Store root : {storeRoot}");
Console.WriteLine($"FileDefs   : {fileDefsRoot}");
Console.WriteLine($"Mode       : {(apply ? "APPLY (the store will be rewritten)" : "DRY RUN (read-only survey)")}");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 1: Survey every bucket and classify it
// -----------------------------------------------------------------------
Console.WriteLine("[1/4] Surveying all 4096 FileDef prefix buckets...");

var sw = Stopwatch.StartNew();
var legacyBuckets = new List<LegacyBucket>();
var alreadyMigrated = 0;
var empty = 0;

for (var i = 0; i < 4096; i++)
{
    var prefix    = i.ToString("x3");
    var bucketDir = Path.Combine(fileDefsRoot, prefix[..2]);

    if (!Directory.Exists(bucketDir))
    {
        empty++;
        continue;
    }

    var packFiles = Directory
        .EnumerateFiles(bucketDir, $"fileDefs{prefix}-*.pack")
        .ToArray();

    if (packFiles.Length == 0)
    {
        empty++;
        continue;
    }

    var legacyIdxPath = Path.Combine(bucketDir, $"index{prefix}.idx");
    if (!File.Exists(legacyIdxPath))
    {
        // No legacy index: either already converted, or a bucket written by a
        // current-format server. Either way there is nothing to migrate.
        alreadyMigrated++;
        continue;
    }

    var oldIndex = OldFlatIndexReader.ReadAll(legacyIdxPath);
    if (oldIndex.Count == 0)
    {
        empty++;
        continue;
    }

    legacyBuckets.Add(new LegacyBucket(prefix, bucketDir, legacyIdxPath, oldIndex));
}

sw.Stop();

var totalEntries = legacyBuckets.Sum(static b => b.Index.Count);
Console.WriteLine($"      Legacy buckets needing migration : {legacyBuckets.Count}");
Console.WriteLine($"      Buckets already in current format: {alreadyMigrated}");
Console.WriteLine($"      Empty buckets                    : {empty}");
Console.WriteLine($"      File definitions to convert      : {totalEntries}");
Console.WriteLine($"      Survey duration                  : {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine();

if (legacyBuckets.Count == 0)
{
    Console.WriteLine("Nothing to do — the FileDef store is already in the current format.");
    return 0;
}

// -----------------------------------------------------------------------
// Step 2: Resolve file lengths from PostgreSQL (read-only)
// -----------------------------------------------------------------------
Console.WriteLine("[2/4] Reading file lengths from PostgreSQL (read-only)...");
sw.Restart();

var chunkStoreId = await DetectChunkStoreIdAsync(connString, fileDefsRoot);
Console.WriteLine($"      ChunkStoreId: {chunkStoreId}");

var allHashes = legacyBuckets.SelectMany(static b => b.Index.Keys).ToList();
var lengthMap = await QueryFileLengthsAsync(connString, allHashes, chunkStoreId);

sw.Stop();
var missingLengths = totalEntries - lengthMap.Count;
Console.WriteLine($"      Lengths resolved : {lengthMap.Count}/{totalEntries}");
Console.WriteLine($"      Missing lengths  : {missingLengths}" +
                  (missingLengths > 0 ? "  (these records get FileLength 0; no entry is dropped)" : ""));
Console.WriteLine($"      Query duration   : {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine();

if (!apply)
{
    Console.WriteLine("DRY RUN — no files were modified.");
    Console.WriteLine($"Re-run with --apply to convert {totalEntries} file definitions " +
                      $"across {legacyBuckets.Count} buckets.");
    return 0;
}

// -----------------------------------------------------------------------
// Step 3: Rewrite each bucket's pack files in the current record format
// -----------------------------------------------------------------------
Console.WriteLine("[3/4] Rewriting pack files as FileDefinitionRecord blobs...");
sw.Restart();

using var throttler = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount - 1));
var convertTasks = legacyBuckets.Select(b => ConvertBucketAsync(b, lengthMap, throttler)).ToList();
var convertResults = await Task.WhenAll(convertTasks);

sw.Stop();

var converted = convertResults.Count(static r => r.Success);
var failed    = convertResults.Where(static r => !r.Success).ToList();
var written   = convertResults.Sum(static r => r.EntriesWritten);

Console.WriteLine($"      Buckets converted: {converted}/{legacyBuckets.Count}");
Console.WriteLine($"      Entries written  : {written}");
Console.WriteLine($"      Duration         : {sw.Elapsed.TotalSeconds:F1}s");

foreach (var f in failed)
    Console.Error.WriteLine($"      FAILED {f.Prefix}: {f.Error}");

if (failed.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"ERROR: {failed.Count} bucket(s) failed to convert. Their original pack " +
                            "files and legacy indexes were left untouched. Resolve the errors above " +
                            "and re-run; buckets that already converted will be skipped.");
    return 1;
}

Console.WriteLine();

// -----------------------------------------------------------------------
// Step 4: Rebuild the LSM index for every converted bucket
// -----------------------------------------------------------------------
Console.WriteLine("[4/4] Rebuilding LSM index segments...");
sw.Restart();

var rebuildTasks = legacyBuckets.Select(b => RebuildBucketAsync(b, throttler)).ToList();
var rebuildResults = await Task.WhenAll(rebuildTasks);

sw.Stop();

var rebuilt = rebuildResults.Count(static r => r.Success);
var rebuildFailures = rebuildResults.Where(static r => !r.Success).ToList();

Console.WriteLine($"      Buckets rebuilt: {rebuilt}/{legacyBuckets.Count}");
Console.WriteLine($"      Duration       : {sw.Elapsed.TotalSeconds:F1}s");

foreach (var f in rebuildFailures)
    Console.Error.WriteLine($"      FAILED {f.Prefix}: {f.Error}");

if (rebuildFailures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"ERROR: {rebuildFailures.Count} bucket(s) failed to rebuild.");
    return 1;
}

Console.WriteLine();
Console.WriteLine("=== Migration complete. ===");
return 0;

// -----------------------------------------------------------------------
// Bucket conversion
// -----------------------------------------------------------------------

/// <summary>
/// Rewrites a single bucket's pack files, replacing each legacy
/// TransposeCompress payload with a self-describing
/// <see cref="FileDefinitionRecord"/> keyed by the same file hash.
///
/// <para>
/// The new pack is written alongside the old one and fully verified before it
/// replaces it, so a failure at any point leaves the bucket untouched.
/// </para>
/// </summary>
async Task<BucketResult> ConvertBucketAsync(
    LegacyBucket bucket,
    IReadOnlyDictionary<Hash32, long> lengths,
    SemaphoreSlim gate)
{
    await gate.WaitAsync();
    try
    {
        var newPackPath = Path.Combine(bucket.Directory, $"fileDefs{bucket.Prefix}-0.pack.new");
        var targetPath  = Path.Combine(bucket.Directory, $"fileDefs{bucket.Prefix}-0.pack");

        // The legacy index records which pack file each entry lives in. Open a
        // handle per file number so entries can be read wherever they sit; all
        // of them are consolidated into a single new pack.
        var handles = new Dictionary<int, Microsoft.Win32.SafeHandles.SafeFileHandle>();
        var expected = new HashSet<Hash32>();

        try
        {
            foreach (var fileNo in bucket.Index.Values.Select(static v => v.FileNo).Distinct())
            {
                var path = Path.Combine(bucket.Directory, $"fileDefs{bucket.Prefix}-{fileNo}.pack");
                if (!File.Exists(path))
                    return BucketResult.Fail(bucket.Prefix, $"legacy index references missing pack file '{path}'.");

                handles[fileNo] = File.OpenHandle(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    FileOptions.Asynchronous | FileOptions.RandomAccess);
            }

            await using (var newPack = new FileStream(
                             newPackPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             bufferSize: 65536, options: FileOptions.Asynchronous))
            {
                foreach (var (fileHash, (fileNo, offset, _)) in bucket.Index)
                {
                    var payload = await PackFileEntry.ReadAtAsync(handles[fileNo], offset);
                    if (payload is null)
                        return BucketResult.Fail(bucket.Prefix, $"pack entry at offset {offset} read as null.");

                    // Already converted? Then this bucket was interrupted mid-run;
                    // carry the record across verbatim rather than re-wrapping it.
                    byte[] blob;
                    if (IsFileDefinitionRecord(payload))
                    {
                        blob = payload;
                    }
                    else
                    {
                        var chunkHashes = ChecksumCompressor.TransposeDecompressHashes(payload);

                        // FileLength is informational — it is written but never read
                        // back by the server — so a missing DB row must not cost us
                        // the entry itself.
                        lengths.TryGetValue(fileHash, out var fileLength);

                        blob = new FileDefinitionRecord
                        {
                            FileHash    = fileHash,
                            FileLength  = fileLength,
                            ChunkHashes = chunkHashes
                        }.Serialize();
                    }

                    await PackFileEntry.WriteAsync(newPack, blob);
                    expected.Add(fileHash);
                }

                await newPack.FlushAsync();
            }
        }
        finally
        {
            foreach (var h in handles.Values)
                h.Dispose();
        }

        // --- Verify the new pack before it replaces anything ---
        var seen = new HashSet<Hash32>();
        await using (var verify = new FileStream(
                         newPackPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         bufferSize: 65536, options: FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await foreach (var entry in PackFileEntry.ReadAllEntriesAsync(verify))
            {
                Hash32 hash;
                try
                {
                    hash = FileDefinitionRecord.Deserialize(entry.Data).FileHash;
                }
                catch (Exception ex)
                {
                    return BucketResult.Fail(bucket.Prefix, $"verification failed to parse a rewritten record: {ex.Message}");
                }

                seen.Add(hash);
            }
        }

        if (!seen.SetEquals(expected))
        {
            File.Delete(newPackPath);
            return BucketResult.Fail(
                bucket.Prefix,
                $"verification mismatch: expected {expected.Count} file hashes, found {seen.Count}.");
        }

        // --- Commit: swap the pack in, then drop the now-stale legacy index ---
        FileAtomicHelper.ReplaceAtomic(newPackPath, targetPath);

        // Entries from higher-numbered packs were consolidated into -0.
        foreach (var fileNo in bucket.Index.Values.Select(static v => v.FileNo).Distinct().Where(static n => n != 0))
            File.Delete(Path.Combine(bucket.Directory, $"fileDefs{bucket.Prefix}-{fileNo}.pack"));

        File.Delete(bucket.LegacyIndexPath);

        return BucketResult.Ok(bucket.Prefix, expected.Count);
    }
    catch (Exception ex)
    {
        return BucketResult.Fail(bucket.Prefix, ex.Message);
    }
    finally
    {
        gate.Release();
    }
}

/// <summary>
/// Rebuilds the LSM segment + bloom filter for a bucket from its rewritten
/// pack, using the same code path the server uses so the result is identical
/// to a natively written store.
/// </summary>
async Task<BucketResult> RebuildBucketAsync(LegacyBucket bucket, SemaphoreSlim gate)
{
    await gate.WaitAsync();
    try
    {
        using var handler = new IndexedPackFileHandler(
            bucket.Directory,
            "fileDefs",
            bucket.Prefix,
            maxPackSize,
            static data => FileDefinitionRecord.Deserialize(data).FileHash);

        return await handler.RebuildIndexFile()
            ? BucketResult.Ok(bucket.Prefix, 0)
            : BucketResult.Fail(bucket.Prefix, "RebuildIndexFile returned false.");
    }
    catch (Exception ex)
    {
        return BucketResult.Fail(bucket.Prefix, ex.Message);
    }
    finally
    {
        gate.Release();
    }
}

/// <summary>
/// True when a pack payload already carries the "BSFD" FileDefinitionRecord
/// header, i.e. the entry has been converted by an earlier run.
/// </summary>
static bool IsFileDefinitionRecord(ReadOnlySpan<byte> payload)
    => payload.Length >= 5 &&
       payload[0] == 0x42 && payload[1] == 0x53 && payload[2] == 0x46 && payload[3] == 0x44;

// -----------------------------------------------------------------------
// PostgreSQL (read-only)
// -----------------------------------------------------------------------

/// <summary>
/// Queries PostgreSQL for the <c>FileDefinition.Length</c> of each supplied file hash
/// within the given chunk store.
/// </summary>
static async Task<Dictionary<Hash32, long>> QueryFileLengthsAsync(
    string connString,
    IReadOnlyList<Hash32> fileHashes,
    Guid chunkStoreId)
{
    var result = new Dictionary<Hash32, long>(fileHashes.Count);
    if (fileHashes.Count == 0)
        return result;

    await using var conn = new NpgsqlConnection(connString);
    await conn.OpenAsync();

    // Chunked so the bytea[] parameter stays a reasonable size on large stores.
    const int batchSize = 10000;

    for (var i = 0; i < fileHashes.Count; i += batchSize)
    {
        var batch = fileHashes.Skip(i).Take(batchSize).Select(static h => h.GetBytes()).ToArray();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Checksum", "Length"
            FROM "FileDefinitions"
            WHERE "ChunkStoreId" = @chunkStoreId
              AND "Checksum" = ANY(@checksums)
            """;
        cmd.Parameters.AddWithValue("chunkStoreId", chunkStoreId);
        cmd.Parameters.AddWithValue("checksums", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Bytea, batch);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var checksumBytes = (byte[])reader["Checksum"];
            var fileLength    = reader.GetInt64(reader.GetOrdinal("Length"));
            result[new Hash32(checksumBytes)] = fileLength;
        }
    }

    return result;
}

/// <summary>
/// Detects the ChunkStoreId from the database by finding which chunk store's
/// local path matches the store root we're migrating.
/// Falls back to the first (and only expected) chunk store if only one exists.
/// </summary>
static async Task<Guid> DetectChunkStoreIdAsync(string connString, string fileDefsRoot)
{
    await using var conn = new NpgsqlConnection(connString);
    await conn.OpenAsync();

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """SELECT "Id", "BackendSettings" FROM "ChunkStores" """;

    var rows = new List<(Guid Id, string? Settings)>();
    await using (var reader = await cmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var id       = reader.GetGuid(0);
            var settings = reader.IsDBNull(1) ? null : reader.GetString(1);
            rows.Add((id, settings));
        }
    }

    if (rows.Count == 0)
        throw new InvalidOperationException("No ChunkStores found in database.");

    var storeParent = Directory.GetParent(fileDefsRoot)?.FullName ?? fileDefsRoot;
    foreach (var (id, settings) in rows)
    {
        if (settings is not null &&
            settings.Contains(storeParent.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return id;
    }

    if (rows.Count == 1)
    {
        Console.WriteLine($"      WARN: Could not match store root to ChunkStore by path; " +
                          $"using sole ChunkStore {rows[0].Id}.");
        return rows[0].Id;
    }

    throw new InvalidOperationException(
        $"Could not determine ChunkStoreId from path '{storeParent}'. " +
        $"Found {rows.Count} ChunkStores. Please specify the ChunkStoreId manually.");
}

// -----------------------------------------------------------------------
// Types
// -----------------------------------------------------------------------

internal sealed record LegacyBucket(
    string Prefix,
    string Directory,
    string LegacyIndexPath,
    Dictionary<Hash32, (int FileNo, long Offset, int Length)> Index);

internal readonly record struct BucketResult(string Prefix, bool Success, int EntriesWritten, string? Error)
{
    public static BucketResult Ok(string prefix, int entries) => new(prefix, true, entries, null);
    public static BucketResult Fail(string prefix, string error) => new(prefix, false, 0, error);
}
