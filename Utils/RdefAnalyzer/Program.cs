// Analyse the sample rdef, measure compression options, simulate format variants.
// Run: dotnet run --project Utils/RdefAnalyzer [path-to.rdef]

using System.Text;
using BinStash.Contracts.Release;
using BinStash.Core.Compression;
using BinStash.Core.Serialization;
using BinStash.Core.Serialization.Utils;

string rdefPath = (args.Length > 0)
    ? args[0]
    : FindEmbeddedRdef();

byte[] originalRaw = await File.ReadAllBytesAsync(rdefPath);
Console.WriteLine("Original (V2) file size: " + originalRaw.Length.ToString("N0") + " B");

// Deserialise the V2 file
ReleasePackage package = await ReleasePackageSerializer.DeserializeAsync(originalRaw);

// Patch missing lengths (V2 did not store lengths)
foreach (var a in package.OutputArtifacts)
    if (a.Backing is OpaqueBlobBacking ob && ob.Length == null)
        ob.Length = 0;

// ---- Metadata inspection ------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Metadata ===");
Console.WriteLine("Version:    " + (package.Version ?? "<null>"));
Console.WriteLine("ReleaseId:  " + (package.ReleaseId ?? "<null>"));
Console.WriteLine("RepoId:     " + (package.RepoId ?? "<null>"));
Console.WriteLine("Notes:      " + (string.IsNullOrEmpty(package.Notes) ? "<empty>" : $"{package.Notes.Length} chars: \"{Truncate(package.Notes, 120)}\""));
Console.WriteLine("CreatedAt:  " + package.CreatedAt);

// ---- Custom properties inspection ---------------------------------------
Console.WriteLine();
Console.WriteLine("=== Custom Properties (" + package.CustomProperties.Count + " entries) ===");
foreach (var kvp in package.CustomProperties.Take(20))
{
    Console.WriteLine($"  [{Encoding.UTF8.GetByteCount(kvp.Key)} B] \"{Truncate(kvp.Key, 60)}\"");
    Console.WriteLine($"    → [{Encoding.UTF8.GetByteCount(kvp.Value)} B] \"{Truncate(kvp.Value, 80)}\"");
}

// ---- Path segment analysis ----------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Path Statistics ===");
Console.WriteLine("OutputArtifacts: " + package.OutputArtifacts.Count.ToString("N0"));
Console.WriteLine("Unique paths:    " + package.OutputArtifacts.Select(a => a.Path).Distinct().Count().ToString("N0"));
Console.WriteLine("Components:      " + package.OutputArtifacts.Select(a => a.ComponentName).Distinct().Count().ToString("N0"));

var allPaths = package.OutputArtifacts.Select(a => a.Path ?? "").ToList();
var allSegments = allPaths.SelectMany(p => p.Split('/')).Distinct().ToList();

// Build the byte-sorted token table (same as V4 serializer)
var sortedTokens = allSegments
    .Select(x => (Text: x, Bytes: Encoding.UTF8.GetBytes(x)))
    .OrderBy(x => x.Bytes, LexicographicByteComparer.Instance)
    .Select(x => x.Text)
    .ToList();
var sortedIndex = sortedTokens.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i, StringComparer.Ordinal);

// ---- Existing V4 sizes --------------------------------------------------
Console.WriteLine();
var optCompressed   = new ReleasePackageSerializerOptions { EnableCompression = true,  CompressionLevel = 9 };
var optUncompressed = new ReleasePackageSerializerOptions { EnableCompression = false };

byte[] v4Compressed   = await ReleasePackageSerializer.SerializeAsync(package, optCompressed);
byte[] v4Uncompressed = await ReleasePackageSerializer.SerializeAsync(package, optUncompressed);

Console.WriteLine("V4 compressed size:   " + v4Compressed.Length.ToString("N0") + " B");
Console.WriteLine("V4 uncompressed size: " + v4Uncompressed.Length.ToString("N0") + " B");
Console.WriteLine($"V2 {originalRaw.Length:N0} B  →  V4 compressed {v4Compressed.Length:N0} B  ({DiffPct(originalRaw.Length, v4Compressed.Length)})");

Console.WriteLine();
Console.WriteLine("=== V4 Compressed section layout ===");
PrintSections(v4Compressed);

// ========================================================================
// EXPERIMENT 1: Artifact sort order
// Does sorting artifacts by path before writing §0x05 improve compression?
// Idea: lexicographic sort ensures adjacent artifacts share path prefix tokens,
// giving Zstd better context correlation.
// ========================================================================
Console.WriteLine();
Console.WriteLine("=== EXP 1: Artifact Sort Order in §0x05 ===");
{
    using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));

    byte[] SimArtifacts(IList<string> paths, bool sorted)
    {
        var list = sorted
            ? paths.OrderBy(p => p, StringComparer.Ordinal).ToList()
            : (List<string>)paths;

        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)list.Count);
        foreach (var path in list)
        {
            var segs = path.Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs)
                WriteVarInt(w, (ulong)sortedIndex[seg]);
            w.Write((byte)0);  // Kind=File
            w.Write((byte)1);  // RequiresBytePerfect=true
            w.Write((byte)0);  // BackingType=OpaqueBlob
        }
        return ms.ToArray();
    }

    byte[] unsortedRaw = SimArtifacts(allPaths, sorted: false);
    byte[] sortedRaw   = SimArtifacts(allPaths, sorted: true);
    int unsortedC = zstd.Wrap(unsortedRaw).Length;
    int sortedC   = zstd.Wrap(sortedRaw).Length;

    Console.WriteLine($"  Unsorted (current): {unsortedC:N0} B compressed ({unsortedRaw.Length:N0} raw)");
    Console.WriteLine($"  Sorted by path:     {sortedC:N0} B compressed ({sortedRaw.Length:N0} raw)");
    Console.WriteLine($"  Saving: {unsortedC - sortedC:N0} B  ({DiffPct(unsortedC, sortedC)})");
}

// ========================================================================
// EXPERIMENT 2: §0x02 outer Zstd passthrough
// TransposeCompress already Zstd-compresses each column internally.
// The outer WriteSectionAsync Zstd re-compresses this payload.
// Measure: skip outer Zstd for §0x02 (write already-compressed bytes raw).
// ========================================================================
Console.WriteLine();
Console.WriteLine("=== EXP 2: §0x02 Outer Zstd Passthrough ===");
{
    var hashes = package.OutputArtifacts
        .Select(a => ((OpaqueBlobBacking)a.Backing).ContentHash!.Value)
        .OrderBy(x => x)
        .Select(x => x.GetBytes())
        .ToList();

    byte[] transposePayload   = ChecksumCompressor.TransposeCompress(hashes);
    using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));
    byte[] doubleCompressed   = zstd.Wrap(transposePayload);

    Console.WriteLine($"  TransposeCompress output (inner Zstd):    {transposePayload.Length:N0} B");
    Console.WriteLine($"  After outer Zstd re-compression:          {doubleCompressed.Length:N0} B");
    Console.WriteLine($"  Overhead (outer vs inner only):           {doubleCompressed.Length - transposePayload.Length:+#;-#;0} B");
    Console.WriteLine($"  Conclusion: write §0x02 as raw section (no outer Zstd) saves {doubleCompressed.Length - transposePayload.Length:+#;-#;0} B");
    Console.WriteLine($"  Note: a saving means outer Zstd is hurting; a gain means it is helping.");
}

// ========================================================================
// EXPERIMENT 3: Inline contentHashIndex into §0x05 (eliminate §0x06)
// Instead of:  §0x05: [segCount][segs][kind][bytePerfect][backingType]
//              §0x06: [hashIndex][length]
// Write:       §0x05: [segCount][segs][kind][bytePerfect][hashIndex][length]
// This merges both sections so Zstd can cross-correlate path and hash data.
// ========================================================================
Console.WriteLine();
Console.WriteLine("=== EXP 3: Inline HashIndex+Length into §0x05 (merge §0x06) ===");
{
    using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));

    // Current §0x05 (no hashIndex, no length)
    byte[] SimCurrent05()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)allPaths.Count);
        foreach (var path in allPaths)
        {
            var segs = path.Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs)
                WriteVarInt(w, (ulong)sortedIndex[seg]);
            w.Write((byte)0);  // Kind
            w.Write((byte)1);  // BytePerfect
            w.Write((byte)0);  // BackingType
        }
        return ms.ToArray();
    }

    // Current §0x06 (hashIndex + length, for each opaque artifact in order)
    byte[] SimCurrent06()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);

        var opaques = package.OutputArtifacts
            .Select(a => (OpaqueBlobBacking)a.Backing)
            .ToList();

        // Build hash→index table (same as serializer: sorted unique hashes)
        var hashTable = opaques
            .Select(b => b.ContentHash!.Value)
            .Distinct()
            .OrderBy(h => h)
            .ToList();
        var hashIdx = hashTable.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);

        WriteVarInt(w, (ulong)opaques.Count);
        foreach (var b in opaques)
        {
            WriteVarInt(w, (ulong)hashIdx[b.ContentHash!.Value]);
            WriteVarInt(w, (ulong)(b.Length ?? 0));
        }
        return ms.ToArray();
    }

    // Merged §0x05 with inline hash+length (no §0x06 needed)
    byte[] SimMerged05()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);

        var opaques = package.OutputArtifacts
            .Select(a => (OpaqueBlobBacking)a.Backing)
            .ToList();
        var hashTable = opaques
            .Select(b => b.ContentHash!.Value)
            .Distinct()
            .OrderBy(h => h)
            .ToList();
        var hashIdx = hashTable.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);

        WriteVarInt(w, (ulong)allPaths.Count);
        for (var ai = 0; ai < allPaths.Count; ai++)
        {
            var segs = allPaths[ai].Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs)
                WriteVarInt(w, (ulong)sortedIndex[seg]);
            w.Write((byte)0);  // Kind
            w.Write((byte)1);  // BytePerfect
            // No BackingType byte (all opaque in this sample; could omit with a flag)
            // Inline hash index + length
            WriteVarInt(w, (ulong)hashIdx[opaques[ai].ContentHash!.Value]);
            WriteVarInt(w, (ulong)(opaques[ai].Length ?? 0));
        }
        return ms.ToArray();
    }

    byte[] cur05 = SimCurrent05();
    byte[] cur06 = SimCurrent06();
    byte[] merged = SimMerged05();

    int cur05C    = zstd.Wrap(cur05).Length;
    int cur06C    = zstd.Wrap(cur06).Length;
    int mergedC   = zstd.Wrap(merged).Length;
    int currentTotal = cur05C + cur06C;

    Console.WriteLine($"  Current §0x05 alone:    {cur05C:N0} B compressed ({cur05.Length:N0} raw)");
    Console.WriteLine($"  Current §0x06 alone:    {cur06C:N0} B compressed ({cur06.Length:N0} raw)");
    Console.WriteLine($"  Current §0x05+§0x06:    {currentTotal:N0} B combined");
    Console.WriteLine($"  Merged §0x05 (inline):  {mergedC:N0} B compressed ({merged.Length:N0} raw)");
    Console.WriteLine($"  Saving from merge:      {currentTotal - mergedC:N0} B  ({DiffPct(currentTotal, mergedC)})");
}

// ========================================================================
// EXPERIMENT 4: Combined sort + merge
// Sort artifacts by path AND inline hash+length into §0x05.
// ========================================================================
Console.WriteLine();
Console.WriteLine("=== EXP 4: Sorted artifacts + inline HashIndex+Length ===");
{
    using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));

    var artifacts = package.OutputArtifacts
        .Select(a => (Path: a.Path ?? "", Backing: (OpaqueBlobBacking)a.Backing))
        .ToList();

    var hashTable = artifacts
        .Select(a => a.Backing.ContentHash!.Value)
        .Distinct()
        .OrderBy(h => h)
        .ToList();
    var hashIdx = hashTable.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);

    byte[] SimSortedMerged()
    {
        var sorted = artifacts.OrderBy(a => a.Path, StringComparer.Ordinal).ToList();
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)sorted.Count);
        foreach (var (path, backing) in sorted)
        {
            var segs = path.Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs)
                WriteVarInt(w, (ulong)sortedIndex[seg]);
            w.Write((byte)0);  // Kind
            w.Write((byte)1);  // BytePerfect
            WriteVarInt(w, (ulong)hashIdx[backing.ContentHash!.Value]);
            WriteVarInt(w, (ulong)(backing.Length ?? 0));
        }
        return ms.ToArray();
    }

    byte[] sortedMerged = SimSortedMerged();
    int sortedMergedC = zstd.Wrap(sortedMerged).Length;

    // Reference: current §0x05 + §0x06 combined
    byte[] SimCurrent05()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)allPaths.Count);
        foreach (var path in allPaths)
        {
            var segs = path.Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs)
                WriteVarInt(w, (ulong)sortedIndex[seg]);
            w.Write((byte)0);  w.Write((byte)1);  w.Write((byte)0);
        }
        return ms.ToArray();
    }
    byte[] SimCurrent06()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)artifacts.Count);
        foreach (var (_, b) in artifacts)
        {
            WriteVarInt(w, (ulong)hashIdx[b.ContentHash!.Value]);
            WriteVarInt(w, (ulong)(b.Length ?? 0));
        }
        return ms.ToArray();
    }

    int cur05C = zstd.Wrap(SimCurrent05()).Length;
    int cur06C = zstd.Wrap(SimCurrent06()).Length;
    int currentTotal = cur05C + cur06C;

    Console.WriteLine($"  Current §0x05+§0x06:              {currentTotal:N0} B");
    Console.WriteLine($"  Sorted + merged:                  {sortedMergedC:N0} B");
    Console.WriteLine($"  Saving vs current:                {currentTotal - sortedMergedC:N0} B  ({DiffPct(currentTotal, sortedMergedC)})");
}

// ========================================================================
// EXPERIMENT 5: Delta-encode contentHashIndex in §0x06
// If artifacts are sorted by path and hashes are sorted, consecutive
// artifacts in the same component may reference nearby hash indices.
// Delta-encode the hash index (zigzag varint).
// ========================================================================
Console.WriteLine();
Console.WriteLine("=== EXP 5: Delta-encode hashIndex in §0x06 ===");
{
    using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));

    var opaques = package.OutputArtifacts
        .Select(a => (OpaqueBlobBacking)a.Backing)
        .ToList();
    var hashTable = opaques
        .Select(b => b.ContentHash!.Value)
        .Distinct()
        .OrderBy(h => h)
        .ToList();
    var hashIdx = hashTable.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);

    // Also try sorted-by-path order for delta encoding
    var sortedOpaques = package.OutputArtifacts
        .OrderBy(a => a.Path, StringComparer.Ordinal)
        .Select(a => (OpaqueBlobBacking)a.Backing)
        .ToList();

    byte[] SimAbsolute(List<OpaqueBlobBacking> list)
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)list.Count);
        foreach (var b in list)
        {
            WriteVarInt(w, (ulong)hashIdx[b.ContentHash!.Value]);
            WriteVarInt(w, (ulong)(b.Length ?? 0));
        }
        return ms.ToArray();
    }

    byte[] SimDelta(List<OpaqueBlobBacking> list)
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)list.Count);
        int prev = 0;
        foreach (var b in list)
        {
            int idx = hashIdx[b.ContentHash!.Value];
            int delta = idx - prev;
            prev = idx;
            // Zigzag encode signed delta
            ulong zigzag = (ulong)((delta << 1) ^ (delta >> 31));
            WriteVarInt(w, zigzag);
            WriteVarInt(w, (ulong)(b.Length ?? 0));
        }
        return ms.ToArray();
    }

    byte[] absUnsorted   = SimAbsolute(opaques);
    byte[] absSorted     = SimAbsolute(sortedOpaques);
    byte[] deltaUnsorted = SimDelta(opaques);
    byte[] deltaSorted   = SimDelta(sortedOpaques);

    int absUnsortedC   = zstd.Wrap(absUnsorted).Length;
    int absSortedC     = zstd.Wrap(absSorted).Length;
    int deltaUnsortedC = zstd.Wrap(deltaUnsorted).Length;
    int deltaSortedC   = zstd.Wrap(deltaSorted).Length;

    Console.WriteLine($"  Absolute (unsorted, current): {absUnsortedC:N0} B ({absUnsorted.Length:N0} raw)");
    Console.WriteLine($"  Absolute (sorted by path):    {absSortedC:N0} B ({absSorted.Length:N0} raw)");
    Console.WriteLine($"  Delta (unsorted):             {deltaUnsortedC:N0} B ({deltaUnsorted.Length:N0} raw)");
    Console.WriteLine($"  Delta (sorted by path):       {deltaSortedC:N0} B ({deltaSorted.Length:N0} raw)");
    Console.WriteLine($"  Best saving vs current:       {absUnsortedC - Math.Min(Math.Min(absSortedC, deltaUnsortedC), deltaSortedC):N0} B");
}

// ========================================================================
// EXPERIMENT 6: Cross-section compression §0x05+§0x06 concatenated
// Compress the raw bytes of both sections together as one Zstd stream.
// ========================================================================
Console.WriteLine();
Console.WriteLine("=== EXP 6: Cross-section Zstd (§0x05 + §0x06 as one stream) ===");
{
    using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));

    var opaques = package.OutputArtifacts
        .Select(a => (OpaqueBlobBacking)a.Backing)
        .ToList();
    var hashTable = opaques
        .Select(b => b.ContentHash!.Value)
        .Distinct()
        .OrderBy(h => h)
        .ToList();
    var hashIdx = hashTable.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);

    byte[] raw05, raw06;
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)allPaths.Count);
        foreach (var path in allPaths)
        {
            var segs = path.Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs) WriteVarInt(w, (ulong)sortedIndex[seg]);
            w.Write((byte)0); w.Write((byte)1); w.Write((byte)0);
        }
        raw05 = ms.ToArray();
    }
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)opaques.Count);
        foreach (var b in opaques)
        {
            WriteVarInt(w, (ulong)hashIdx[b.ContentHash!.Value]);
            WriteVarInt(w, (ulong)(b.Length ?? 0));
        }
        raw06 = ms.ToArray();
    }

    int c05 = zstd.Wrap(raw05).Length;
    int c06 = zstd.Wrap(raw06).Length;

    // Concatenate and compress together
    byte[] concat = raw05.Concat(raw06).ToArray();
    int concatC = zstd.Wrap(concat).Length;

    Console.WriteLine($"  §0x05 separate: {c05:N0} B");
    Console.WriteLine($"  §0x06 separate: {c06:N0} B");
    Console.WriteLine($"  Sum separate:   {c05 + c06:N0} B");
    Console.WriteLine($"  Concat §0x05+§0x06: {concatC:N0} B");
    Console.WriteLine($"  Saving from concat: {(c05 + c06) - concatC:N0} B  ({DiffPct(c05 + c06, concatC)})");
}

// ========================================================================
// EXPERIMENT 7: Omit BackingType byte when all artifacts are same type
// Use a header flag instead of per-artifact byte.
// In the all-opaque sample: saves 11,049 bytes uncompressed, but Zstd
// compresses constant-byte streams to ~1 byte — measure the actual saving.
// ========================================================================
Console.WriteLine();
Console.WriteLine("=== EXP 7: Omit BackingType byte (uniform-type flag) ===");
{
    using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));

    byte[] WithBacking()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)allPaths.Count);
        foreach (var path in allPaths)
        {
            var segs = path.Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs) WriteVarInt(w, (ulong)sortedIndex[seg]);
            w.Write((byte)0); w.Write((byte)1); w.Write((byte)0); // Kind, BytePerfect, BackingType
        }
        return ms.ToArray();
    }

    byte[] WithoutBacking()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)allPaths.Count);
        foreach (var path in allPaths)
        {
            var segs = path.Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs) WriteVarInt(w, (ulong)sortedIndex[seg]);
            w.Write((byte)0); w.Write((byte)1); // Kind, BytePerfect (no BackingType)
        }
        return ms.ToArray();
    }

    byte[] withB = WithBacking();
    byte[] noB   = WithoutBacking();
    int withBC = zstd.Wrap(withB).Length;
    int noBC   = zstd.Wrap(noB).Length;

    Console.WriteLine($"  With BackingType byte:    {withBC:N0} B compressed ({withB.Length:N0} raw)");
    Console.WriteLine($"  Without BackingType byte: {noBC:N0} B compressed ({noB.Length:N0} raw)");
    Console.WriteLine($"  Saving:                   {withBC - noBC:N0} B  ({DiffPct(withBC, noBC)})");
    Console.WriteLine($"  Note: if mixed types exist, BackingType must be kept");
}

// ========================================================================
// EXPERIMENT 8: Omit Kind+BytePerfect bytes (constant in this sample)
// Both are always the same value. Measure if omitting them saves anything.
// ========================================================================
Console.WriteLine();
Console.WriteLine("=== EXP 8: Omit Kind + BytePerfect bytes (constant in sample) ===");
{
    using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));

    byte[] WithKindBP()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)allPaths.Count);
        foreach (var path in allPaths)
        {
            var segs = path.Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs) WriteVarInt(w, (ulong)sortedIndex[seg]);
            w.Write((byte)0); w.Write((byte)1); w.Write((byte)0);
        }
        return ms.ToArray();
    }

    byte[] WithoutKindBP()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)allPaths.Count);
        foreach (var path in allPaths)
        {
            var segs = path.Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs) WriteVarInt(w, (ulong)sortedIndex[seg]);
            // no Kind, no BytePerfect, no BackingType
        }
        return ms.ToArray();
    }

    byte[] withKBP = WithKindBP();
    byte[] noKBP   = WithoutKindBP();
    int withKBPC = zstd.Wrap(withKBP).Length;
    int noKBPC   = zstd.Wrap(noKBP).Length;

    Console.WriteLine($"  With Kind+BytePerfect+BackingType:    {withKBPC:N0} B compressed ({withKBP.Length:N0} raw)");
    Console.WriteLine($"  Without (path indices only):          {noKBPC:N0} B compressed ({noKBP.Length:N0} raw)");
    Console.WriteLine($"  Saving (upper bound if constant):     {withKBPC - noKBPC:N0} B  ({DiffPct(withKBPC, noKBPC)})");
}

// ========================================================================
// EXPERIMENT 9: Combined best-case — sort artifacts + merge §0x05/§0x06 +
//               skip outer Zstd for §0x02 passthrough
// Estimate total V5 file size vs current V4.
// ========================================================================
Console.WriteLine();
Console.WriteLine("=== EXP 9: Projected V5 total size estimate ===");
{
    using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));

    // §0x02: no outer Zstd (TransposeCompress already compressed)
    var hashes = package.OutputArtifacts
        .Select(a => ((OpaqueBlobBacking)a.Backing).ContentHash!.Value)
        .OrderBy(x => x)
        .Select(x => x.GetBytes())
        .ToList();
    byte[] transposePayload = ChecksumCompressor.TransposeCompress(hashes);

    // §0x05 merged: sorted + inline hash+length
    var artifacts = package.OutputArtifacts
        .OrderBy(a => a.Path, StringComparer.Ordinal)
        .Select(a => (Path: a.Path ?? "", Backing: (OpaqueBlobBacking)a.Backing))
        .ToList();
    var hashTable = artifacts
        .Select(a => a.Backing.ContentHash!.Value)
        .Distinct()
        .OrderBy(h => h)
        .ToList();
    var hashIdxMap = hashTable.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);

    byte[] mergedSection;
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)artifacts.Count);
        foreach (var (path, backing) in artifacts)
        {
            var segs = path.Split('/');
            WriteVarInt(w, (ulong)segs.Length);
            foreach (var seg in segs) WriteVarInt(w, (ulong)sortedIndex[seg]);
            w.Write((byte)0);  // Kind
            w.Write((byte)1);  // BytePerfect
            w.Write((byte)0);  // BackingType (keep for mixed-type support)
            WriteVarInt(w, (ulong)hashIdxMap[backing.ContentHash!.Value]);
            WriteVarInt(w, (ulong)(backing.Length ?? 0));
        }
        mergedSection = ms.ToArray();
    }

    // §0x03: token table (unchanged)
    byte[] tokenTableRaw;
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)sortedTokens.Count);
        var encoded = sortedTokens.Select(Encoding.UTF8.GetBytes).ToList();
        foreach (var b in encoded) WriteVarInt(w, (ulong)b.Length);
        foreach (var b in encoded) w.Write(b);
        tokenTableRaw = ms.ToArray();
    }

    // Sizes
    int sec02Size = transposePayload.Length; // raw, no outer Zstd
    int sec03Size = zstd.Wrap(tokenTableRaw).Length;
    int sec05Size = zstd.Wrap(mergedSection).Length; // merged §0x05+§0x06
    // §0x01, §0x04, §0x07..§0x0A are tiny and unchanged
    // Grab them from the existing V4 for the estimate
    int otherSections = GetSectionSizes(v4Compressed, excludeIds: [0x02, 0x03, 0x05, 0x06]);

    int projectedBest = sec02Size + sec03Size + sec05Size + otherSections
        + 6   // header (magic 4 + version 1 + flags 1)
        + 6   // §0x02 section header (id 1 + flags 1 + varint size ~4)
        + 6   // §0x03 section header
        + 6;  // §0x05 section header (merged)

    Console.WriteLine($"  Current V4:              {v4Compressed.Length:N0} B");
    Console.WriteLine($"  §0x02 (no outer Zstd):   {sec02Size:N0} B  (was: {GetSectionSize(v4Compressed, 0x02):N0} B)");
    Console.WriteLine($"  §0x03 (unchanged):        {sec03Size:N0} B  (was: {GetSectionSize(v4Compressed, 0x03):N0} B)");
    Console.WriteLine($"  §0x05 merged+sorted:      {sec05Size:N0} B  (was: {GetSectionSize(v4Compressed, 0x05) + GetSectionSize(v4Compressed, 0x06):N0} B)");
    Console.WriteLine($"  Other sections:           {otherSections:N0} B");
    Console.WriteLine($"  Projected best-case (~):  {projectedBest:N0} B");
    Console.WriteLine($"  Saving vs V4:             {v4Compressed.Length - projectedBest:N0} B  ({DiffPct(v4Compressed.Length, projectedBest)})");
    Console.WriteLine($"  Saving vs V2 original:    {originalRaw.Length - projectedBest:N0} B  ({DiffPct(originalRaw.Length, projectedBest)})");
}

// ========================================================================
// EXPERIMENT 10: Hash Section Deep Dive — §0x02 compression strategies
// The hash section is 67% of the V4 file. Explore ways to shrink it.
// Current approach: sort hashes → transpose into 32 columns → Zstd each
// column at level 9 → outer streaming Zstd wraps the whole thing.
// ========================================================================
Console.WriteLine();
Console.WriteLine("=== EXP 10: §0x02 Hash Compression Deep Dive ===");
{
    // Collect unique sorted hashes (same as V4 serializer does)
    var hashesRaw = package.OutputArtifacts
        .Select(a => ((OpaqueBlobBacking)a.Backing).ContentHash!.Value)
        .OrderBy(x => x)
        .Select(x => x.GetBytes())
        .ToList();

    int hashCount = hashesRaw.Count;
    const int hashSize = 32;
    Console.WriteLine($"  Hash count: {hashCount:N0}  (unique, sorted)");
    Console.WriteLine($"  Raw size:   {hashCount * hashSize:N0} B");
    Console.WriteLine();

    // ---- Baseline: current TransposeCompress (inner Zstd level 9) ----
    byte[] baselineTranspose = ChecksumCompressor.TransposeCompress(hashesRaw);
    Console.WriteLine($"  [BASELINE] TransposeCompress (inner Zstd L9):       {baselineTranspose.Length:N0} B");

    // Measure with outer streaming Zstd (matching WriteSectionAsync behavior)
    byte[] StreamingZstd(byte[] input, int level)
    {
        using var ms = new MemoryStream();
        using (var z = new ZstdNet.CompressionStream(ms, new ZstdNet.CompressionOptions(level)))
            z.Write(input);
        return ms.ToArray();
    }

    byte[] baselineWithOuterZstd = StreamingZstd(baselineTranspose, 9);
    Console.WriteLine($"  [BASELINE] + outer streaming Zstd L9:               {baselineWithOuterZstd.Length:N0} B  (this is V4 §0x02)");
    Console.WriteLine();

    // ---- Helper: transpose into columns ----
    byte[][] Transpose(List<byte[]> hashes)
    {
        var cols = new byte[hashSize][];
        for (int c = 0; c < hashSize; c++)
            cols[c] = new byte[hashes.Count];
        for (int r = 0; r < hashes.Count; r++)
            for (int c = 0; c < hashSize; c++)
                cols[c][r] = hashes[r][c];
        return cols;
    }

    // ---- Helper: compress columns with given level, return total inner payload size ----
    int CompressColumns(byte[][] columns, int level)
    {
        using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(level));
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)columns[0].Length); // hash count
        for (int i = 0; i < columns.Length; i++)
        {
            var compressed = zstd.Wrap(columns[i]);
            WriteVarInt(w, (ulong)compressed.Length);
            w.Write(compressed);
        }
        return (int)ms.Length;
    }

    // ---- Helper: compress columns with given level, return the actual bytes ----
    byte[] CompressColumnsBytes(byte[][] columns, int level)
    {
        using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(level));
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)columns[0].Length); // hash count
        for (int i = 0; i < columns.Length; i++)
        {
            var compressed = zstd.Wrap(columns[i]);
            WriteVarInt(w, (ulong)compressed.Length);
            w.Write(compressed);
        }
        return ms.ToArray();
    }

    byte[][] cols = Transpose(hashesRaw);

    // ---- EXP 10a: Higher inner Zstd levels ----
    Console.WriteLine("  --- 10a: Inner Zstd compression level sweep ---");
    foreach (int level in new[] { 1, 3, 5, 9, 12, 15, 19 })
    {
        int innerSize = CompressColumns(cols, level);
        byte[] innerBytes = CompressColumnsBytes(cols, level);
        byte[] withOuter = StreamingZstd(innerBytes, 9);
        Console.WriteLine($"    Inner L{level,2}: {innerSize,10:N0} B inner  |  {withOuter.Length,10:N0} B with outer L9");
    }
    Console.WriteLine();

    // ---- EXP 10b: Higher outer Zstd levels ----
    Console.WriteLine("  --- 10b: Outer Zstd compression level sweep (inner L9 fixed) ---");
    foreach (int level in new[] { 1, 3, 5, 9, 12, 15, 19 })
    {
        byte[] withOuter = StreamingZstd(baselineTranspose, level);
        Console.WriteLine($"    Outer L{level,2}: {withOuter.Length,10:N0} B");
    }
    Console.WriteLine();

    // ---- EXP 10c: XOR delta encoding per column ----
    Console.WriteLine("  --- 10c: XOR delta encoding per column (before Zstd) ---");
    {
        byte[][] xorCols = new byte[hashSize][];
        for (int c = 0; c < hashSize; c++)
        {
            xorCols[c] = new byte[hashCount];
            xorCols[c][0] = cols[c][0]; // first row is absolute
            for (int r = 1; r < hashCount; r++)
                xorCols[c][r] = (byte)(cols[c][r] ^ cols[c][r - 1]);
        }

        int xorInner = CompressColumns(xorCols, 9);
        byte[] xorInnerBytes = CompressColumnsBytes(xorCols, 9);
        byte[] xorWithOuter = StreamingZstd(xorInnerBytes, 9);
        Console.WriteLine($"    XOR delta inner L9:           {xorInner,10:N0} B");
        Console.WriteLine($"    XOR delta + outer L9:         {xorWithOuter.Length,10:N0} B");
        Console.WriteLine($"    vs baseline inner:            {xorInner - baselineTranspose.Length:+#;-#;0} B");
        Console.WriteLine($"    vs baseline + outer:          {xorWithOuter.Length - baselineWithOuterZstd.Length:+#;-#;0} B");
    }
    Console.WriteLine();

    // ---- EXP 10d: Sub delta (arithmetic, not XOR) per column ----
    Console.WriteLine("  --- 10d: Arithmetic delta encoding per column ---");
    {
        byte[][] deltaCols = new byte[hashSize][];
        for (int c = 0; c < hashSize; c++)
        {
            deltaCols[c] = new byte[hashCount];
            deltaCols[c][0] = cols[c][0];
            for (int r = 1; r < hashCount; r++)
                deltaCols[c][r] = (byte)(cols[c][r] - cols[c][r - 1]);
        }

        int deltaInner = CompressColumns(deltaCols, 9);
        byte[] deltaInnerBytes = CompressColumnsBytes(deltaCols, 9);
        byte[] deltaWithOuter = StreamingZstd(deltaInnerBytes, 9);
        Console.WriteLine($"    Arith delta inner L9:         {deltaInner,10:N0} B");
        Console.WriteLine($"    Arith delta + outer L9:       {deltaWithOuter.Length,10:N0} B");
        Console.WriteLine($"    vs baseline inner:            {deltaInner - baselineTranspose.Length:+#;-#;0} B");
        Console.WriteLine($"    vs baseline + outer:          {deltaWithOuter.Length - baselineWithOuterZstd.Length:+#;-#;0} B");
    }
    Console.WriteLine();

    // ---- EXP 10e: Single Zstd stream for all columns (no per-column framing) ----
    Console.WriteLine("  --- 10e: Single Zstd stream for all 32 columns concatenated ---");
    {
        // Concatenate all columns into one byte array, then compress as one stream
        byte[] allColumnsConcat = new byte[hashCount * hashSize];
        for (int c = 0; c < hashSize; c++)
            Buffer.BlockCopy(cols[c], 0, allColumnsConcat, c * hashCount, hashCount);

        using var zstd9 = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));
        byte[] singleStream = zstd9.Wrap(allColumnsConcat);

        // Also try with varint header
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)hashCount);
        w.Write(singleStream);
        byte[] singleStreamWithHeader = ms.ToArray();

        // With outer streaming Zstd
        byte[] singleWithOuter = StreamingZstd(singleStreamWithHeader, 9);

        Console.WriteLine($"    Single Zstd L9 (no framing):  {singleStream.Length,10:N0} B");
        Console.WriteLine($"    Single + header:              {singleStreamWithHeader.Length,10:N0} B");
        Console.WriteLine($"    Single + outer streaming L9:  {singleWithOuter.Length,10:N0} B");
        Console.WriteLine($"    vs baseline inner:            {singleStreamWithHeader.Length - baselineTranspose.Length:+#;-#;0} B");
        Console.WriteLine($"    vs baseline + outer:          {singleWithOuter.Length - baselineWithOuterZstd.Length:+#;-#;0} B");
    }
    Console.WriteLine();

    // ---- EXP 10f: Column group widths (2, 4, 8 bytes per group) ----
    Console.WriteLine("  --- 10f: Column group widths (2, 4, 8 bytes interleaved per group) ---");
    foreach (int groupWidth in new[] { 1, 2, 4, 8, 16, 32 })
    {
        int groupCount = hashSize / groupWidth;
        byte[][] groups = new byte[groupCount][];
        for (int g = 0; g < groupCount; g++)
        {
            groups[g] = new byte[hashCount * groupWidth];
            for (int r = 0; r < hashCount; r++)
                for (int b = 0; b < groupWidth; b++)
                    groups[g][r * groupWidth + b] = hashesRaw[r][g * groupWidth + b];
        }

        using var zstd9 = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteVarInt(w, (ulong)hashCount);
        for (int g = 0; g < groupCount; g++)
        {
            var compressed = zstd9.Wrap(groups[g]);
            WriteVarInt(w, (ulong)compressed.Length);
            w.Write(compressed);
        }
        byte[] groupInner = ms.ToArray();
        byte[] groupWithOuter = StreamingZstd(groupInner, 9);

        string label = groupWidth == 1 ? "(current)" : groupWidth == 32 ? "(no transpose)" : "";
        Console.WriteLine($"    Width {groupWidth,2}: {groupCount,2} groups  inner {groupInner.Length,10:N0} B  |  +outer {groupWithOuter.Length,10:N0} B  {label}");
    }
    Console.WriteLine();

    // ---- EXP 10g: XOR delta + higher inner levels ----
    Console.WriteLine("  --- 10g: XOR delta + inner level sweep ---");
    {
        byte[][] xorCols = new byte[hashSize][];
        for (int c = 0; c < hashSize; c++)
        {
            xorCols[c] = new byte[hashCount];
            xorCols[c][0] = cols[c][0];
            for (int r = 1; r < hashCount; r++)
                xorCols[c][r] = (byte)(cols[c][r] ^ cols[c][r - 1]);
        }

        foreach (int level in new[] { 9, 12, 15, 19 })
        {
            byte[] innerBytes = CompressColumnsBytes(xorCols, level);
            byte[] withOuter = StreamingZstd(innerBytes, 9);
            Console.WriteLine($"    XOR + inner L{level,2} + outer L9:  inner {innerBytes.Length,10:N0} B  |  +outer {withOuter.Length,10:N0} B");
        }
    }
    Console.WriteLine();

    // ---- EXP 10h: Hash sort order impact ----
    Console.WriteLine("  --- 10h: Hash sort order impact ---");
    {
        // Unsorted (original artifact order)
        var hashesUnsorted = package.OutputArtifacts
            .Select(a => ((OpaqueBlobBacking)a.Backing).ContentHash!.Value)
            .Select(x => x.GetBytes())
            .ToList();

        // Sorted by hash value (current V4 approach)
        var hashesSorted = hashesUnsorted
            .OrderBy(x => x, LexicographicByteComparer.Instance)
            .ToList();

        // Sorted by artifact path (correlated with §0x05 order)
        var hashesByPath = package.OutputArtifacts
            .OrderBy(a => a.Path, StringComparer.Ordinal)
            .Select(a => ((OpaqueBlobBacking)a.Backing).ContentHash!.Value)
            .Select(x => x.GetBytes())
            .ToList();

        byte[] unsortedInner = ChecksumCompressor.TransposeCompress(hashesUnsorted);
        byte[] sortedInner = ChecksumCompressor.TransposeCompress(hashesSorted);
        byte[] byPathInner = ChecksumCompressor.TransposeCompress(hashesByPath);

        byte[] unsortedOuter = StreamingZstd(unsortedInner, 9);
        byte[] sortedOuter = StreamingZstd(sortedInner, 9);
        byte[] byPathOuter = StreamingZstd(byPathInner, 9);

        Console.WriteLine($"    Unsorted (original):   inner {unsortedInner.Length,10:N0} B  |  +outer {unsortedOuter.Length,10:N0} B");
        Console.WriteLine($"    Sorted by hash:        inner {sortedInner.Length,10:N0} B  |  +outer {sortedOuter.Length,10:N0} B  (current V4)");
        Console.WriteLine($"    Sorted by path:        inner {byPathInner.Length,10:N0} B  |  +outer {byPathOuter.Length,10:N0} B");
    }
    Console.WriteLine();

    // ---- EXP 10i: Deduplicated hashes (unique only, current) vs all with dupes ----
    Console.WriteLine("  --- 10i: Unique vs all hashes (with duplicates) ---");
    {
        var allHashes = package.OutputArtifacts
            .Select(a => ((OpaqueBlobBacking)a.Backing).ContentHash!.Value)
            .Select(x => x.GetBytes())
            .ToList();
        var uniqueHashes = allHashes
            .Distinct(new ByteArrayEqualityComparer())
            .OrderBy(x => x, LexicographicByteComparer.Instance)
            .ToList();

        Console.WriteLine($"    Total hashes:  {allHashes.Count:N0}");
        Console.WriteLine($"    Unique hashes: {uniqueHashes.Count:N0}  (dedup ratio: {100.0 * (allHashes.Count - uniqueHashes.Count) / allHashes.Count:F1}%)");

        byte[] allInner = ChecksumCompressor.TransposeCompress(allHashes);
        byte[] uniqueInner = ChecksumCompressor.TransposeCompress(uniqueHashes);
        byte[] allOuter = StreamingZstd(allInner, 9);
        byte[] uniqueOuter = StreamingZstd(uniqueInner, 9);

        Console.WriteLine($"    All hashes:    inner {allInner.Length,10:N0} B  |  +outer {allOuter.Length,10:N0} B");
        Console.WriteLine($"    Unique only:   inner {uniqueInner.Length,10:N0} B  |  +outer {uniqueOuter.Length,10:N0} B  (current V4)");
    }
    Console.WriteLine();

    // ---- EXP 10j: Per-column byte distribution analysis (UNIQUE hashes) ----
    Console.WriteLine("  --- 10j: Per-column entropy and compression (UNIQUE hashes) ---");
    {
        var uniqueHashesForJ = package.OutputArtifacts
            .Select(a => ((OpaqueBlobBacking)a.Backing).ContentHash!.Value)
            .Distinct()
            .OrderBy(x => x)
            .Select(x => x.GetBytes())
            .ToList();
        byte[][] uCols = Transpose(uniqueHashesForJ);
        int uCount = uniqueHashesForJ.Count;

        using var zstd9 = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));
        Console.WriteLine($"    {"Col",4} {"Unique",7} {"Entropy",8} {"Raw",8} {"Zstd",8} {"Ratio",7}");
        for (int c = 0; c < hashSize; c++)
        {
            int unique = uCols[c].Distinct().Count();
            var freq = new int[256];
            foreach (byte b in uCols[c]) freq[b]++;
            double entropy = 0;
            for (int i = 0; i < 256; i++)
            {
                if (freq[i] == 0) continue;
                double p = (double)freq[i] / uCount;
                entropy -= p * Math.Log2(p);
            }
            int rawLen = uCols[c].Length;
            int zstdLen = zstd9.Wrap(uCols[c]).Length;
            double ratio = (double)zstdLen / rawLen * 100;
            Console.WriteLine($"    {c,4} {unique,7} {entropy,8:F3} {rawLen,8:N0} {zstdLen,8:N0} {ratio,6:F1}%");
        }
    }
    Console.WriteLine();

    // ---- EXP 10k: Key experiments re-run on UNIQUE hashes (correct V4 baseline) ----
    Console.WriteLine("  --- 10k: Strategies applied to UNIQUE sorted hashes (V4 baseline) ---");
    {
        var uniqueHashes = package.OutputArtifacts
            .Select(a => ((OpaqueBlobBacking)a.Backing).ContentHash!.Value)
            .Distinct()
            .OrderBy(x => x)
            .Select(x => x.GetBytes())
            .ToList();
        int uCount = uniqueHashes.Count;
        byte[][] uCols = Transpose(uniqueHashes);

        byte[] uBaseline = ChecksumCompressor.TransposeCompress(uniqueHashes);
        byte[] uBaselineOuter = StreamingZstd(uBaseline, 9);
        Console.WriteLine($"    BASELINE (unique, inner L9 + outer L9): {uBaselineOuter.Length:N0} B");
        Console.WriteLine();

        // 10k-a: Higher inner levels
        Console.WriteLine("    Higher inner Zstd levels:");
        foreach (int level in new[] { 9, 12, 15, 19 })
        {
            byte[] inner = CompressColumnsBytes(uCols, level);
            byte[] withOuter = StreamingZstd(inner, 9);
            Console.WriteLine($"      Inner L{level,2} + outer L9:  {withOuter.Length,10:N0} B  ({withOuter.Length - uBaselineOuter.Length:+#;-#;0} vs baseline)");
        }
        Console.WriteLine();

        // 10k-b: Higher outer levels
        Console.WriteLine("    Higher outer Zstd levels (inner L9):");
        foreach (int level in new[] { 9, 12, 15, 19 })
        {
            byte[] withOuter = StreamingZstd(uBaseline, level);
            Console.WriteLine($"      Inner L9 + outer L{level,2}:  {withOuter.Length,10:N0} B  ({withOuter.Length - uBaselineOuter.Length:+#;-#;0} vs baseline)");
        }
        Console.WriteLine();

        // 10k-c: XOR delta on unique hashes
        Console.WriteLine("    XOR delta encoding:");
        {
            byte[][] xorCols = new byte[hashSize][];
            for (int c = 0; c < hashSize; c++)
            {
                xorCols[c] = new byte[uCount];
                xorCols[c][0] = uCols[c][0];
                for (int r = 1; r < uCount; r++)
                    xorCols[c][r] = (byte)(uCols[c][r] ^ uCols[c][r - 1]);
            }
            foreach (int level in new[] { 9, 12, 15, 19 })
            {
                byte[] inner = CompressColumnsBytes(xorCols, level);
                byte[] withOuter = StreamingZstd(inner, 9);
                Console.WriteLine($"      XOR + inner L{level,2} + outer L9:  {withOuter.Length,10:N0} B  ({withOuter.Length - uBaselineOuter.Length:+#;-#;0})");
            }
        }
        Console.WriteLine();

        // 10k-d: Arithmetic delta on unique hashes
        Console.WriteLine("    Arithmetic delta encoding:");
        {
            byte[][] deltaCols = new byte[hashSize][];
            for (int c = 0; c < hashSize; c++)
            {
                deltaCols[c] = new byte[uCount];
                deltaCols[c][0] = uCols[c][0];
                for (int r = 1; r < uCount; r++)
                    deltaCols[c][r] = (byte)(uCols[c][r] - uCols[c][r - 1]);
            }
            foreach (int level in new[] { 9, 12, 15, 19 })
            {
                byte[] inner = CompressColumnsBytes(deltaCols, level);
                byte[] withOuter = StreamingZstd(inner, 9);
                Console.WriteLine($"      Delta + inner L{level,2} + outer L9:  {withOuter.Length,10:N0} B  ({withOuter.Length - uBaselineOuter.Length:+#;-#;0})");
            }
        }
        Console.WriteLine();

        // 10k-e: Column group widths on unique hashes
        Console.WriteLine("    Column group widths:");
        foreach (int groupWidth in new[] { 1, 2, 4, 8, 16, 32 })
        {
            int groupCount = hashSize / groupWidth;
            byte[][] groups = new byte[groupCount][];
            for (int g = 0; g < groupCount; g++)
            {
                groups[g] = new byte[uCount * groupWidth];
                for (int r = 0; r < uCount; r++)
                    for (int b = 0; b < groupWidth; b++)
                        groups[g][r * groupWidth + b] = uniqueHashes[r][g * groupWidth + b];
            }

            using var zstd9 = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(9));
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            WriteVarInt(w, (ulong)uCount);
            for (int g = 0; g < groupCount; g++)
            {
                var compressed = zstd9.Wrap(groups[g]);
                WriteVarInt(w, (ulong)compressed.Length);
                w.Write(compressed);
            }
            byte[] groupInner = ms.ToArray();
            byte[] groupWithOuter = StreamingZstd(groupInner, 9);

            string label = groupWidth == 1 ? "(current)" : groupWidth == 32 ? "(no transpose)" : "";
            Console.WriteLine($"      Width {groupWidth,2}: {groupCount,2} groups  |  +outer {groupWithOuter.Length,10:N0} B  ({groupWithOuter.Length - uBaselineOuter.Length:+#;-#;0})  {label}");
        }
        Console.WriteLine();

        // 10k-f: Single stream (no per-column framing) on unique hashes
        Console.WriteLine("    Single Zstd stream (all columns concatenated):");
        {
            byte[] allConcat = new byte[uCount * hashSize];
            for (int c = 0; c < hashSize; c++)
                Buffer.BlockCopy(uCols[c], 0, allConcat, c * uCount, uCount);

            foreach (int level in new[] { 9, 12, 15, 19 })
            {
                using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(level));
                byte[] compressed = zstd.Wrap(allConcat);
                using var ms = new MemoryStream();
                using var w = new BinaryWriter(ms);
                WriteVarInt(w, (ulong)uCount);
                w.Write(compressed);
                byte[] total = ms.ToArray();
                byte[] withOuter = StreamingZstd(total, 9);
                Console.WriteLine($"      Single L{level,2} + outer L9:  {withOuter.Length,10:N0} B  ({withOuter.Length - uBaselineOuter.Length:+#;-#;0})");
            }
        }
        Console.WriteLine();

        // 10k-g: No transpose at all — just raw sorted hashes in one Zstd stream
        Console.WriteLine("    No transpose (raw sorted hashes, single Zstd):");
        {
            byte[] rawConcat = new byte[uCount * hashSize];
            for (int r = 0; r < uCount; r++)
                Buffer.BlockCopy(uniqueHashes[r], 0, rawConcat, r * hashSize, hashSize);

            foreach (int level in new[] { 9, 12, 15, 19 })
            {
                using var zstd = new ZstdNet.Compressor(new ZstdNet.CompressionOptions(level));
                byte[] compressed = zstd.Wrap(rawConcat);
                using var ms = new MemoryStream();
                using var w = new BinaryWriter(ms);
                WriteVarInt(w, (ulong)uCount);
                w.Write(compressed);
                byte[] total = ms.ToArray();
                byte[] withOuter = StreamingZstd(total, 9);
                Console.WriteLine($"      Raw L{level,2} + outer L9:  {withOuter.Length,10:N0} B  ({withOuter.Length - uBaselineOuter.Length:+#;-#;0})");
            }
        }
        Console.WriteLine();

        // 10k-h: XOR delta on unique hashes + column group widths combined
        Console.WriteLine("    XOR delta + column group widths (inner L19):");
        foreach (int groupWidth in new[] { 1, 2, 4, 8, 16, 32 })
        {
            int groupCount = hashSize / groupWidth;
            // First apply XOR delta row-wise on raw hashes, then group
            byte[][] xorHashes = new byte[uCount][];
            xorHashes[0] = (byte[])uniqueHashes[0].Clone();
            for (int r = 1; r < uCount; r++)
            {
                xorHashes[r] = new byte[hashSize];
                for (int b = 0; b < hashSize; b++)
                    xorHashes[r][b] = (byte)(uniqueHashes[r][b] ^ uniqueHashes[r - 1][b]);
            }

            byte[][] groups = new byte[groupCount][];
            for (int g = 0; g < groupCount; g++)
            {
                groups[g] = new byte[uCount * groupWidth];
                for (int r = 0; r < uCount; r++)
                    for (int b = 0; b < groupWidth; b++)
                        groups[g][r * groupWidth + b] = xorHashes[r][g * groupWidth + b];
            }

            byte[] inner = CompressColumnsBytes(groups, 19);
            byte[] withOuter = StreamingZstd(inner, 9);
            string label = groupWidth == 1 ? "(current cols)" : groupWidth == 32 ? "(no transpose)" : "";
            Console.WriteLine($"      XOR + Width {groupWidth,2}: {groupCount,2} groups  |  +outer {withOuter.Length,10:N0} B  ({withOuter.Length - uBaselineOuter.Length:+#;-#;0})  {label}");
        }
    }
}

// ========================================================================
// Helper methods
// ========================================================================
static void WriteVarInt(BinaryWriter w, ulong value)
{
    var v = value;
    do
    {
        var b = (byte)(v & 0x7F);
        v >>= 7;
        if (v != 0) b |= 0x80;
        w.Write(b);
    } while (v != 0);
}

static string DiffPct(long baseline, long value)
{
    var pct = 100.0 * (value - baseline) / baseline;
    return pct <= 0
        ? $"-{Math.Abs(pct):F1}%"
        : $"+{pct:F1}%";
}

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

static void PrintSections(byte[] data)
{
    var sectionNames = new Dictionary<byte, string>
    {
        [0x01] = "metadata",
        [0x02] = "content-hashes",
        [0x03] = "string-table",
        [0x04] = "custom-props",
        [0x05] = "output-artifacts",
        [0x06] = "opaque-backings",
        [0x07] = "reconstructed-backings",
        [0x08] = "container-members",
        [0x09] = "recipe-payloads",
        [0x0A] = "stats",
    };

    using var ms = new MemoryStream(data);
    using var rdr = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

    _ = rdr.ReadBytes(4); // magic
    byte version = rdr.ReadByte();
    byte flags = rdr.ReadByte();
    Console.WriteLine("Version: " + version + "  Flags: 0x" + flags.ToString("X2"));

    while (ms.Position < ms.Length)
    {
        byte sectionId = rdr.ReadByte();
        _ = rdr.ReadByte();
        uint sz = VarIntUtils.ReadVarInt<uint>(rdr);
        string name = sectionNames.TryGetValue(sectionId, out string? n) ? n : "unknown";
        Console.WriteLine("  0x" + sectionId.ToString("X2") + "  " + name.PadRight(28) + sz.ToString("N0").PadLeft(10) + " B");
        ms.Seek(sz, SeekOrigin.Current);
    }
}

static int GetSectionSize(byte[] data, byte targetId)
{
    using var ms = new MemoryStream(data);
    using var rdr = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
    _ = rdr.ReadBytes(4); _ = rdr.ReadByte(); _ = rdr.ReadByte(); // magic + version + flags
    while (ms.Position < ms.Length)
    {
        byte sectionId = rdr.ReadByte();
        _ = rdr.ReadByte();
        uint sz = VarIntUtils.ReadVarInt<uint>(rdr);
        if (sectionId == targetId) return (int)sz;
        ms.Seek(sz, SeekOrigin.Current);
    }
    return 0;
}

static int GetSectionSizes(byte[] data, byte[] excludeIds)
{
    var exclude = new HashSet<byte>(excludeIds);
    int total = 0;
    using var ms = new MemoryStream(data);
    using var rdr = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
    _ = rdr.ReadBytes(4); _ = rdr.ReadByte(); _ = rdr.ReadByte();
    while (ms.Position < ms.Length)
    {
        byte sectionId = rdr.ReadByte();
        _ = rdr.ReadByte();
        uint sz = VarIntUtils.ReadVarInt<uint>(rdr);
        // Include section header bytes too (id 1 + flags 1 + varint ~2)
        if (!exclude.Contains(sectionId)) total += (int)sz + 4; // header approx
        ms.Seek(sz, SeekOrigin.Current);
    }
    return total;
}

static string FindEmbeddedRdef()
{
    string? dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        string[] found = Directory.GetFiles(dir, "*.rdef", SearchOption.AllDirectories);
        if (found.Length > 0) return found[0];
        dir = Directory.GetParent(dir)?.FullName;
    }
    throw new FileNotFoundException("No .rdef file found.");
}

sealed class LexicographicByteComparer : IComparer<byte[]>
{
    public static readonly LexicographicByteComparer Instance = new();
    public int Compare(byte[]? x, byte[]? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        var len = Math.Min(x.Length, y.Length);
        for (var i = 0; i < len; i++)
        {
            var c = x[i].CompareTo(y[i]);
            if (c != 0) return c;
        }
        return x.Length.CompareTo(y.Length);
    }
}

sealed class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
{
    public bool Equals(byte[]? x, byte[]? y)
    {
        if (x is null && y is null) return true;
        if (x is null || y is null) return false;
        return x.AsSpan().SequenceEqual(y);
    }
    public int GetHashCode(byte[] obj)
    {
        // Use first 8 bytes for hash
        if (obj.Length >= 8)
            return BitConverter.ToInt32(obj, 0) ^ BitConverter.ToInt32(obj, 4);
        var hash = 17;
        foreach (var b in obj) hash = hash * 31 + b;
        return hash;
    }
}
