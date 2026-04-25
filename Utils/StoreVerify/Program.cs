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

using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Serialization;
using BinStash.Infrastructure.Storage;
using BinStash.Infrastructure.Storage.FileDefinition;
using Npgsql;

// ============================================================
//  BinStash.StoreVerify — end-to-end verification of the
//  StorageKey-based FileDef retrieval path.
//
//  For each of 10 random OpaqueBlobBacking artifacts in a real
//  .rdef file, this tool:
//    1. Looks up StorageKey in PostgreSQL by Checksum + ChunkStoreId
//    2. Reads the FileDefinitionRecord blob from the on-disk store
//    3. Deserializes and validates the record
//
//  Usage:
//    BinStash.StoreVerify <rdefPath> <storeRoot> <connectionString>
//
//  Example:
//    BinStash.StoreVerify \
//        "C:\...\30af24...ae9.rdef" \
//        "C:\Tmp\BinStash\SecondLocalStoreSetup" \
//        "Host=127.0.0.1;Port=5432;Database=binstash_test2;..."
// ============================================================

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: BinStash.StoreVerify <rdefPath> <storeRoot> <connectionString>");
    return 1;
}

var rdefPath   = args[0];
var storeRoot  = args[1];
var connString = args[2];

Console.WriteLine("=== BinStash FileDef Retrieval Verification ===");
Console.WriteLine($"rdef file  : {rdefPath}");
Console.WriteLine($"Store root : {storeRoot}");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 1: Deserialize the .rdef file and collect OpaqueBlobBacking artifacts
// -----------------------------------------------------------------------
Console.WriteLine("[1/4] Deserializing .rdef file...");

ReleasePackage releasePackage;
await using (var fs = File.OpenRead(rdefPath))
{
    releasePackage = await ReleasePackageSerializer.DeserializeAsync(fs);
}

var opaqueArtifacts = releasePackage.OutputArtifacts
    .Where(a => a.Backing is OpaqueBlobBacking { ContentHash: not null })
    .Select(a => (a, Backing: (OpaqueBlobBacking)a.Backing))
    .ToList();

Console.WriteLine($"      Total artifacts      : {releasePackage.OutputArtifacts.Count}");
Console.WriteLine($"      OpaqueBlobBacking     : {opaqueArtifacts.Count}");

if (opaqueArtifacts.Count == 0)
{
    Console.Error.WriteLine("ERROR: No OpaqueBlobBacking artifacts with ContentHash found in the .rdef.");
    return 1;
}

// Pick up to 10 random entries
var rng = new Random(42);
var sample = opaqueArtifacts
    .OrderBy(_ => rng.Next())
    .Take(10)
    .ToList();

Console.WriteLine($"      Sample size           : {sample.Count}");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 2: Detect ChunkStoreId from the database
// -----------------------------------------------------------------------
Console.WriteLine("[2/4] Detecting ChunkStoreId from database...");
var chunkStoreId = await DetectChunkStoreIdAsync(connString, storeRoot);
Console.WriteLine($"      ChunkStoreId: {chunkStoreId}");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 3: Query StorageKey for each sampled artifact
// -----------------------------------------------------------------------
Console.WriteLine("[3/4] Querying StorageKey from PostgreSQL...");

var contentHashes = sample.Select(s => s.Backing.ContentHash!.Value).ToList();
var storageKeyMap = await QueryStorageKeysAsync(connString, contentHashes, chunkStoreId);

Console.WriteLine($"      Resolved {storageKeyMap.Count}/{sample.Count} StorageKey(s).");
Console.WriteLine();

// -----------------------------------------------------------------------
// Step 4: Retrieve and validate each FileDefinitionRecord
// -----------------------------------------------------------------------
// -----------------------------------------------------------------------
// Step 3b: Diagnose — try lookup by raw content hash (old-key format)
// -----------------------------------------------------------------------
Console.WriteLine("[3b] DIAGNOSTIC: trying lookup by raw content hash (old-key format)...");
{
    using var diagStore = new ObjectStore(storeRoot);
    var diagHash = sample[0].Backing.ContentHash!.Value;
    try
    {
        var diagBlob = await diagStore.ReadFileDefinitionBlobAsync(diagHash);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"      OLD-KEY LOOKUP SUCCEEDED for {diagHash.ToHexString()[..16]}... " +
                          $"({diagBlob.Length} bytes) — store is using OLD file-hash keys!");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"      Old-key lookup also failed: {ex.GetType().Name}: {ex.Message[..Math.Min(80, ex.Message.Length)]}");
    }
}
Console.WriteLine();

Console.WriteLine("[4/4] Retrieving and validating FileDefinitionRecord blobs...");
Console.WriteLine();

using var objectStore = new ObjectStore(storeRoot);

var passed  = 0;
var failed  = 0;
var skipped = 0;

for (var i = 0; i < sample.Count; i++)
{
    var (artifact, backing) = sample[i];
    var contentHash = backing.ContentHash!.Value;
    var label = $"  [{i + 1:00}/{sample.Count:00}] {contentHash.ToHexString()[..16]}...";

    if (!storageKeyMap.TryGetValue(contentHash, out var storageKey))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"{label}  SKIP  — StorageKey not found in DB (migration not run or row missing)");
        Console.ResetColor();
        skipped++;
        continue;
    }

    Console.Write($"{label}  storageKey={storageKey.ToHexString()[..16]}...  ");

    try
    {
        var blob = await objectStore.ReadFileDefinitionBlobAsync(storageKey);

        if (blob is null || blob.Length == 0)
            throw new InvalidDataException("ReadFileDefinitionBlobAsync returned null or empty blob.");

        var record = FileDefinitionRecord.Deserialize(blob);

        // Validate: FileHash must match the content hash from the artifact
        if (record.FileHash != contentHash)
            throw new InvalidDataException(
                $"FileHash mismatch: expected {contentHash.ToHexString()[..16]}..., " +
                $"got {record.FileHash.ToHexString()[..16]}...");

        if (record.ChunkHashes.Count == 0)
            throw new InvalidDataException("ChunkHashes list is empty.");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"PASS  (fileLen={record.FileLength:N0}, chunks={record.ChunkHashes.Count})");
        Console.ResetColor();
        passed++;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"FAIL  — {ex.GetType().Name}: {ex.Message}");
        Console.ResetColor();
        failed++;
    }
}

Console.WriteLine();
Console.WriteLine("=== Results ===");
Console.WriteLine($"  Passed  : {passed}");
Console.WriteLine($"  Failed  : {failed}");
Console.WriteLine($"  Skipped : {skipped}  (StorageKey NULL → run BinStash.StoreMigration first)");
Console.WriteLine();

if (failed > 0)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"VERIFICATION FAILED — {failed} test(s) did not pass.");
    Console.ResetColor();
    return 1;
}

if (skipped == sample.Count)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("VERIFICATION INCONCLUSIVE — all entries were skipped (no StorageKeys in DB).");
    Console.ResetColor();
    return 2;
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("VERIFICATION PASSED");
Console.ResetColor();
return 0;

// ============================================================
// Local functions
// ============================================================

/// <summary>
/// Detects the ChunkStoreId that owns the given storeRoot path from the DB.
/// Falls back to the sole ChunkStore if path matching fails.
/// </summary>
static async Task<Guid> DetectChunkStoreIdAsync(string connString, string storeRoot)
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

    // Try to match by path in BackendSettings JSON
    var normalizedRoot = storeRoot.Replace('\\', '/');
    foreach (var (id, settings) in rows)
    {
        if (settings is not null &&
            settings.Contains(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return id;
    }

    if (rows.Count == 1)
    {
        Console.WriteLine($"      WARN: Could not match store root to ChunkStore by path; " +
                          $"using sole ChunkStore {rows[0].Id}.");
        return rows[0].Id;
    }

    throw new InvalidOperationException(
        $"Could not determine ChunkStoreId from path '{storeRoot}'. " +
        $"Found {rows.Count} ChunkStores. Please specify the ChunkStoreId manually.");
}

/// <summary>
/// Queries PostgreSQL for the <c>FileDefinition.StorageKey</c> for each content hash.
/// Returns only rows where StorageKey is non-null.
/// </summary>
static async Task<Dictionary<Hash32, Hash32>> QueryStorageKeysAsync(
    string connString,
    IReadOnlyList<Hash32> contentHashes,
    Guid chunkStoreId)
{
    var result = new Dictionary<Hash32, Hash32>(contentHashes.Count);
    if (contentHashes.Count == 0)
        return result;

    await using var conn = new NpgsqlConnection(connString);
    await conn.OpenAsync();

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT "Checksum", "StorageKey"
        FROM "FileDefinitions"
        WHERE "ChunkStoreId" = @chunkStoreId
          AND "Checksum" = ANY(@checksums)
          AND "StorageKey" IS NOT NULL
        """;
    cmd.Parameters.AddWithValue("chunkStoreId", chunkStoreId);

    var checksumArrays = contentHashes.Select(h => h.GetBytes()).ToArray();
    cmd.Parameters.AddWithValue("checksums",
        NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Bytea,
        checksumArrays);

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var checksumBytes    = (byte[])reader["Checksum"];
        var storageKeyBytes  = (byte[])reader["StorageKey"];
        result[new Hash32(checksumBytes)] = new Hash32(storageKeyBytes);
    }

    return result;
}
