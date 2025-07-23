# Release File Format (.rdef)

BinStash release packages use a highly efficient binary format optimized for deduplication, delta encoding, and minimal storage overhead.

## 🧱 File Structure

Each `.rdef` file contains multiple sections, optionally compressed with Zstd:

| Section ID | Purpose                                            |
|------------|----------------------------------------------------|
| `0x01`     | Release metadata (version, notes)                  |
| `0x02`     | Chunk table (BLAKE3, transpose-compressed)         |
| `0x03`     | String table (component/file names)                |
| `0x04`     | Deduplicated content ID mappings (chunk sequences) |
| `0x05`     | Component & file definitions with chunk refs       |
| `0x06`     | Statistics (counts, sizes)                         |

## 🔗 Chunk Reference Format

Each file consists of a sequence of `DeltaChunkRef` items:
- `deltaIndex`: delta to the previous chunk index (VarInt)
- `offset`: byte offset within chunk (VarInt)
- `length`: number of bytes (VarInt)

If a chunk list is reused across multiple files, it is stored once and referenced by index (content ID).

## 🪜 Compression & Encoding

- **Chunk table** uses transpose compression for space-efficient BLAKE3 hashes.
- **Chunk references** are bit-packed using calculated widths.
- **String table** uses tokenization and reuse to reduce redundancy.
- All sections are optionally **Zstd-compressed**.

## 📌 File Header

Every `.rdef` file starts with:

| Field     | Size    | Description                   |
|-----------|---------|-------------------------------|
| Magic     | 4 bytes | BPKG (BinStash Package)       |
| Version   | 1 byte  | 
| Flags     | 1 byte  | e.g. compression enabled flag |

See source: `ReleasePackageSerializer.cs`
