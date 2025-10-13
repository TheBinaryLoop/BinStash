# Release File Format (.rdef)

BinStash release packages use a highly efficient binary format optimized for deduplication, delta encoding, and minimal storage overhead.

## 🧱 File Structure

Each `.rdef` file contains multiple sections, optionally compressed with Zstd:

| Section ID | Purpose                                        |
|------------|------------------------------------------------|
| `0x01`     | Release metadata (version, notes)              |
| `0x02`     | File hash table (BLAKE3, transpose-compressed) |
| `0x03`     | String table (component/file names)            |
| `0x04`     | Component & file definitions with file refs    |
| `0x05`     |  Statistics (counts, sizes)                    |

## 🪜 Compression & Encoding

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
