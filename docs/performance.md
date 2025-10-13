# Performance & Storage Optimization

BinStash is designed for large-scale storage of release packages by aggressively optimizing data layout and deduplication.

---

## 🚀 Fast Chunking

- Uses **FastCDC** for content-defined chunking
- Chunk sizes are configurable: min, avg, max
- Avoids re-chunking when small diffs happen

---

## 📦 Chunk Pack Files

Chunks are stored in `.pack` files, grouped by BLAKE3 prefix:
- Each prefix maps to a set of rotating 4 GiB files
- Each pack file is compressed per chunk using **Zstd**
- Chunks include a 21-byte header:
    - Magic: `BSCK`
    - Version: 1
    - Uncompressed + compressed size
    - xxHash3 checksum

Index files (.idx) map chunk hashes to `(fileNo, offset, length)`.

---

## 🧠 Smart Deduplication

- BinStash builds a **chunk map** for each file
- If a chunk list appears in multiple files, it's stored once and **referenced by ID**
- Inline vs referenced encoding is chosen based on size tradeoff

---

## 🧮 Storage Stats

Stats are calculated per store:

- Total chunks
- Total compressed & uncompressed size
- Avg chunk size
- Compression ratio
- Per-prefix chunk distribution

Example:

```
TotalChunks: 3,842,201
AvgChunkSize: 11.2 KiB
CompressionRatio: 2.97
SpaceSaved: 66.3%
```

---

## 🪛 Memory & I/O Efficiency

- **Memory-mapped indexes** for fast lookups
- **Buffered streaming** reads for large chunks
- **Pack file rotation** max 4GB files
- **Parallel hashing + chunking + I/O**

