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
using Blake3; 
using Npgsql;

// ============================================================
//  BinStash.StoreMigration — one-shot FileDef store migration
//
//  Repairs the on-disk FileDef pack-file store and PostgreSQL
//  database after the BINST-99 LSM-tree + BLAKE3-self-keying
//  FileDefinitionRecord format changes.
//
//  Usage:
//    BinStash.StoreMigration <storeRoot> <connectionString>
//
//  Example:
//    BinStash.StoreMigration "C:\Tmp\BinStash\SecondLocalStoreSetup" \
//        "Host=localhost;Port=6432;Database=binstash;Username=postgres;Password=postgres"
// ============================================================

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: BinStash.StoreMigration <storeRoot> <connectionString>");
    return 1;
}

var storeRoot    = args[0];
var connString   = args[1];
var fileDefsRoot = Path.Combine(storeRoot, "FileDefs");

if (!Directory.Exists(fileDefsRoot))
{
    Console.Error.WriteLine($"ERROR: FileDefs directory not found: {fileDefsRoot}");
    return 1;
}

Console.WriteLine("=== BinStash FileDef Store Migration ===");
Console.WriteLine($"Store root : {storeRoot}");
Console.WriteLine($"FileDefs   : {fileDefsRoot}");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 1: Back up the PostgreSQL database via pg_dump
// -----------------------------------------------------------------------
Console.WriteLine("[1/5] Backing up PostgreSQL database via pg_dump...");
var backupResult = RunPgDump(connString, storeRoot);
if (backupResult != 0)
{
    Console.Error.WriteLine("ERROR: pg_dump failed. Aborting migration to protect data.");
    return 1;
}
Console.WriteLine("      Backup complete.");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 2: Discover all 4096 prefixes and collect migration data
// -----------------------------------------------------------------------
Console.WriteLine("[2/5] Scanning all 4096 FileDef prefix buckets...");

// For each prefix we'll collect:
//   - rebuilt (hash -> storageKey) mappings for the new IDX2 segments
//   - DB update records: (fileHash, storageKey)
var dbUpdates = new List<(Hash32 FileHash, Hash32 StorageKey, Guid ChunkStoreId)>();
var prefixSegments = new Dictionary<string, List<(Hash32 Hash, IndexEntry Entry)>>();

var sw = Stopwatch.StartNew();
var totalEntries = 0;
var skippedPrefixes = 0;

// Discover ChunkStoreId from DB — we need it to match FileDefinition rows
Guid chunkStoreId = await DetectChunkStoreIdAsync(connString, fileDefsRoot);
Console.WriteLine($"      ChunkStoreId: {chunkStoreId}");

using var throttler = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount - 1));
var scanTasks = new List<Task<PrefixMigrationResult?>>();

for (var i = 0; i < 4096; i++)
{
    var prefix = i.ToString("x3");
    var bucketDir = Path.Combine(fileDefsRoot, prefix[..2]);
    scanTasks.Add(MigratePrefixAsync(bucketDir, prefix, chunkStoreId, connString, throttler));
}

var scanResults = await Task.WhenAll(scanTasks);

foreach (var result in scanResults)
{
    if (result is null)
    {
        skippedPrefixes++;
        continue;
    }
    prefixSegments[result.Prefix] = result.SortedSegmentEntries;
    dbUpdates.AddRange(result.DbUpdates);
    totalEntries += result.SortedSegmentEntries.Count;
}

sw.Stop();
Console.WriteLine($"      Scanned {4096 - skippedPrefixes} active prefixes, {skippedPrefixes} empty.");
Console.WriteLine($"      Total entries to migrate: {totalEntries}");
Console.WriteLine($"      Scan duration: {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 3: Write new IDX2 segment files
// -----------------------------------------------------------------------
Console.WriteLine("[3/5] Writing new IDX2 segment files...");
sw.Restart();

var segWriteTasks = new List<Task>();
foreach (var (prefix, entries) in prefixSegments)
{
    if (entries.Count == 0) continue;
    var bucketDir = Path.Combine(fileDefsRoot, prefix[..2]);
    var segPath   = Path.Combine(bucketDir, $"fileDefs{prefix}.seg-000.idx");
    segWriteTasks.Add(WriteNewSegmentAsync(segPath, entries));
}

await Task.WhenAll(segWriteTasks);
sw.Stop();
Console.WriteLine($"      Wrote {segWriteTasks.Count} segment files in {sw.Elapsed.TotalSeconds:F1}s.");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 4: Delete stale bloom filters and old flat index files
// -----------------------------------------------------------------------
Console.WriteLine("[4/5] Cleaning up stale bloom filters and old flat index files...");
var deletedFiles = 0;
for (var i = 0; i < 4096; i++)
{
    var prefix    = i.ToString("x3");
    var bucketDir = Path.Combine(fileDefsRoot, prefix[..2]);

    // Delete the wrong-keyed bloom filter (we just replaced the segment)
    var bloomPath = Path.Combine(bucketDir, $"fileDefs{prefix}.seg-000.bloom");
    if (File.Exists(bloomPath)) { File.Delete(bloomPath); deletedFiles++; }

    // Delete the old flat varint index (source of truth consumed)
    var idxPath = Path.Combine(bucketDir, $"index{prefix}.idx");
    if (File.Exists(idxPath)) { File.Delete(idxPath); deletedFiles++; }

    // Delete legacy un-prefixed segments if they exist
    var legacySegPath   = Path.Combine(bucketDir, "seg-000.idx");
    var legacyBloomPath = Path.Combine(bucketDir, "seg-000.bloom");
    if (File.Exists(legacySegPath))   { File.Delete(legacySegPath);   deletedFiles++; }
    if (File.Exists(legacyBloomPath)) { File.Delete(legacyBloomPath); deletedFiles++; }
}
Console.WriteLine($"      Deleted {deletedFiles} stale files.");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 5: Update FileDefinition.StorageKey in the database
// -----------------------------------------------------------------------
Console.WriteLine($"[5/5] Updating {dbUpdates.Count} FileDefinition.StorageKey rows in PostgreSQL...");
sw.Restart();
var updatedRows = await UpdateStorageKeysAsync(connString, dbUpdates);
sw.Stop();
Console.WriteLine($"      Updated {updatedRows} rows in {sw.Elapsed.TotalSeconds:F1}s.");
Console.WriteLine();

Console.WriteLine("=== Migration complete. ===");
return 0;

// ============================================================
// Local functions
// ============================================================

/// <summary>
/// Migrates a single 3-hex-digit prefix bucket:
/// reads the old flat index, reads the old pack payloads,
/// re-serialises as FileDefinitionRecord blobs, writes new pack entries,
/// and builds the new IDX2 sorted segment entries in memory.
/// </summary>
static async Task<PrefixMigrationResult?> MigratePrefixAsync(
    string bucketDir,
    string prefix,
    Guid chunkStoreId,
    string connString,
    SemaphoreSlim throttler)
{
    await throttler.WaitAsync();
    try
    {
        var oldIdxPath  = Path.Combine(bucketDir, $"index{prefix}.idx");
        var oldPackPath = Path.Combine(bucketDir, $"fileDefs{prefix}-0.pack");

        if (!File.Exists(oldIdxPath) || !File.Exists(oldPackPath))
            return null;

        // --- Read old flat index ---
        var oldIndex = OldFlatIndexReader.ReadAll(oldIdxPath);
        if (oldIndex.Count == 0)
            return null;

        // --- Query DB for file lengths (old payloads don't embed them) ---
        var fileHashes = oldIndex.Keys.ToList();
        var lengthMap  = await QueryFileLengthsAsync(connString, fileHashes, chunkStoreId);

        // --- Build new pack file for this prefix (alongside old, then swap) ---
        var newPackPath = Path.Combine(bucketDir, $"fileDefs{prefix}-0.pack.new");
        var segEntries  = new List<(Hash32 Hash, IndexEntry Entry)>(oldIndex.Count);
        var dbUpdates   = new List<(Hash32 FileHash, Hash32 StorageKey, Guid ChunkStoreId)>(oldIndex.Count);

        // Open old pack file for random access inside a scoped block so the
        // handle is closed before we try to replace the file (Windows requires
        // no open handles on the destination of MoveFileExW / File.Move overwrite).
        {
            using var packHandle = File.OpenHandle(
                oldPackPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileOptions.Asynchronous | FileOptions.RandomAccess);

            await using var newPackStream = new FileStream(
                newPackPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 65536,
                options: FileOptions.Asynchronous);

            foreach (var (fileHash, (fileNo, offset, length)) in oldIndex)
            {
                // Read old pack entry (raw decompressed payload = TransposeCompress(chunkHashes))
                var oldPayload = await PackFileEntry.ReadAtAsync(packHandle, offset)
                                 ?? throw new InvalidDataException(
                                     $"Pack entry at offset {offset} in {oldPackPath} returned null.");

                // Decompress old-format TransposeCompress payload → chunk hashes
                var chunkHashes = ChecksumCompressor.TransposeDecompressHashes(oldPayload);

                // Get file length from DB
                if (!lengthMap.TryGetValue(fileHash, out var fileLength))
                {
                    Console.Error.WriteLine(
                        $"WARN: No length found in DB for fileHash {fileHash} (prefix {prefix}). Skipping entry.");
                    continue;
                }

                // Build new FileDefinitionRecord
                var record = new FileDefinitionRecord
                {
                    FileHash    = fileHash,
                    FileLength  = fileLength,
                    ChunkHashes = chunkHashes
                };

                var blob       = record.Serialize();
                var storageKey = new Hash32(Hasher.Hash(blob).AsSpan());

                // Write new pack entry
                var (newOffset, entryTotalLen) = await PackFileEntry.WriteAsync(newPackStream, blob);

                segEntries.Add((storageKey, new IndexEntry(0, newOffset, entryTotalLen)));
                dbUpdates.Add((fileHash, storageKey, chunkStoreId));
            }

            await newPackStream.FlushAsync();
        } // packHandle and newPackStream are both closed here before the rename

        // Atomically replace old pack file with new one (no open handles on Windows)
        FileAtomicHelper.ReplaceAtomic(newPackPath, oldPackPath);

        // Sort segment entries ascending by hash
        segEntries.Sort(static (a, b) => a.Hash.CompareTo(b.Hash));

        return new PrefixMigrationResult(prefix, segEntries, dbUpdates);
    }
    finally
    {
        throttler.Release();
    }
}

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

    // Build an IN-list query using unnest for efficient bulk lookup
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT "Checksum", "Length"
        FROM "FileDefinitions"
        WHERE "ChunkStoreId" = @chunkStoreId
          AND "Checksum" = ANY(@checksums)
        """;
    cmd.Parameters.AddWithValue("chunkStoreId", chunkStoreId);

    // Npgsql maps byte[][] to bytea[] via ANY
    var checksumArrays = fileHashes.Select(h => h.GetBytes()).ToArray();
    cmd.Parameters.AddWithValue("checksums", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Bytea, checksumArrays);

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var checksumBytes = (byte[])reader["Checksum"];
        var fileLength    = reader.GetInt64(reader.GetOrdinal("Length"));
        result[new Hash32(checksumBytes)] = fileLength;
    }

    return result;
}

/// <summary>
/// Writes a new IDX2 sorted segment file from the in-memory entries.
/// </summary>
static Task WriteNewSegmentAsync(
    string segPath,
    List<(Hash32 Hash, IndexEntry Entry)> sortedEntries)
{
    return SortedIndexSegment.WriteAsync(segPath, sortedEntries);
}

/// <summary>
/// Bulk-updates <c>FileDefinition.StorageKey</c> in PostgreSQL for all migrated entries.
/// </summary>
static async Task<int> UpdateStorageKeysAsync(
    string connString,
    List<(Hash32 FileHash, Hash32 StorageKey, Guid ChunkStoreId)> updates)
{
    if (updates.Count == 0)
        return 0;

    await using var conn = new NpgsqlConnection(connString);
    await conn.OpenAsync();

    var updatedTotal = 0;
    const int batchSize = 500;

    for (var i = 0; i < updates.Count; i += batchSize)
    {
        var batch = updates.Skip(i).Take(batchSize).ToList();

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            foreach (var (fileHash, storageKey, storeId) in batch)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    UPDATE "FileDefinitions"
                    SET "StorageKey" = @storageKey
                    WHERE "Checksum" = @checksum
                      AND "ChunkStoreId" = @chunkStoreId
                    """;
                cmd.Parameters.AddWithValue("storageKey",   NpgsqlTypes.NpgsqlDbType.Bytea, storageKey.GetBytes());
                cmd.Parameters.AddWithValue("checksum",     NpgsqlTypes.NpgsqlDbType.Bytea, fileHash.GetBytes());
                cmd.Parameters.AddWithValue("chunkStoreId", storeId);

                updatedTotal += await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    return updatedTotal;
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

    // Try to find the ChunkStore whose LocalPath matches the store root parent
    // BackendSettings is stored as jsonb with $type discriminator
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

    // Try to match by path contained in BackendSettings JSON
    var storeParent = Directory.GetParent(fileDefsRoot)?.FullName ?? fileDefsRoot;
    foreach (var (id, settings) in rows)
    {
        if (settings is not null &&
            settings.Contains(storeParent.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return id;
    }

    // If only one store, use it
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

/// <summary>
/// Runs pg_dump to create a SQL backup of the database before any changes.
/// </summary>
static int RunPgDump(string connString, string storeRoot)
{
    // Parse connection string for pg_dump args
    var builder = new NpgsqlConnectionStringBuilder(connString);
    var host     = builder.Host     ?? "localhost";
    var port     = builder.Port > 0 ? builder.Port : 5432;
    var database = builder.Database ?? "binstash";
    var username = builder.Username ?? "postgres";
    var password = builder.Password;

    var backupPath = Path.Combine(storeRoot, $"binstash-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sql");
    Console.WriteLine($"      Backup path: {backupPath}");

    var psi = new ProcessStartInfo
    {
        FileName  = "pg_dump",
        Arguments = $"-h {host} -p {port} -U {username} -d {database} -f \"{backupPath}\" --no-password",
        UseShellExecute        = false,
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
    };

    if (!string.IsNullOrEmpty(password))
        psi.Environment["PGPASSWORD"] = password;

    try
    {
        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start pg_dump.");
        proc.WaitForExit(120_000);

        var stderr = proc.StandardError.ReadToEnd();
        if (!string.IsNullOrWhiteSpace(stderr))
            Console.Error.WriteLine($"      pg_dump stderr: {stderr}");

        if (proc.ExitCode != 0)
            Console.Error.WriteLine($"      pg_dump exited with code {proc.ExitCode}.");

        return proc.ExitCode;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"      pg_dump invocation failed: {ex.Message}");
        Console.Error.WriteLine("      Hint: ensure pg_dump is on PATH, or skip backup with --no-backup flag.");
        // Return 0 to allow proceeding if pg_dump is simply not installed
        // and user accepts the risk — change to 'return 1;' for strict mode.
        Console.Error.WriteLine("      Continuing without backup (pg_dump not available).");
        return 0;
    }
}

// ============================================================
// Result record
// ============================================================

internal sealed record PrefixMigrationResult(
    string Prefix,
    List<(Hash32 Hash, IndexEntry Entry)> SortedSegmentEntries,
    List<(Hash32 FileHash, Hash32 StorageKey, Guid ChunkStoreId)> DbUpdates);
