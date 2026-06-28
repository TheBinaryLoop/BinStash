# Domain Glossary

## Purpose
- Define recurring BinStash domain terms needed to interpret code, tickets, and requests.
- Preserve vocabulary that would otherwise be repeatedly re-inferred.

## What does not belong here
- generic software terms
- speculative definitions not backed by source material
- detailed process documentation

## Terms

### Chunk
- Meaning: a variable-length byte range produced by content-defined chunking, addressed by its **BLAKE3** hash (`Hash32`). The unit of deduplication and storage.
- Source: `BinStash.Core/Chunking`, `Core/Hashing`.

### Content-Defined Chunking (CDC) / FastCDC
- Meaning: splitting a file at boundaries determined by content (a rolling gear-hash), not fixed offsets, so small edits shift few chunks. BinStash uses a **custom FastCDC implementation** (`FastCdcChunker`) with a seeded gear table and normalized-chunking masks; configured by `ChunkerOptions` (min/avg/max).
- Source: `Core/Chunking/FastCdcChunker.cs`.

### Chunk Store
- Meaning: a physical storage location for chunks, file definitions, and release packages. Deduplication scope is **per chunk store**. Backend is pluggable via polymorphic `BackendSettings`; only `LocalFolder` exists today. A `Repository` points at one chunk store.
- Source: `Core/Entities/ChunkStore.cs`, `Infrastructure/Storage`.

### Repository
- Meaning: a logical grouping of releases (belongs to a `Tenant`, references a `ChunkStore`, has a `StorageClass`). Roughly "one product/artifact line".
- Source: `Core/Entities/Repository.cs`.

### Release
- Meaning: an immutable, versioned snapshot of a build output recorded against a repository; its metadata is serialized as a `.rdef` and identified by a `ReleaseDefinitionChecksum`.
- Source: `Core/Entities/Release.cs`.

### `.rdef` (Release Definition)
- Meaning: the custom binary release-metadata file (magic `BPKG`, current write version **V6**). Holds the artifact tree, content-hash table, path-segment token table, backings, and container recipes.
- Source: `Core/Serialization/ReleasePackageSerializer.cs`; see `dataModelNotes.md`.

### File Definition / `FileDefinitionRecord`
- Meaning: the per-file record mapping a file to its ordered chunk hashes; stored in the chunk store's `FileDefs/` area, magic `BSFD`, keyed by the file's content hash (`FileHash`).
- Source: `Core/Serialization`, `Infrastructure/Storage`.

### Output Artifact / Backing
- Meaning: an entry in a release's artifact tree. Its **backing** is how its bytes are recovered: an **Opaque Blob** (stored as one content-hashed blob) or a **Reconstructed Container** (e.g. a ZIP deterministically rebuilt from member files + a recipe). Container **members** and **recipe payloads** describe the reconstruction.
- Source: `Core/Serialization`, `Core/Ingestion/Formats/Zip`.

### Component
- Meaning: the top-level grouping of a release's artifacts; in V6 derived from the first path segment of an artifact (not stored as a separate field on the wire).
- Source: `Core/Serialization/ReleasePackageSerializer.cs`.

### Ingest Session
- Meaning: a server-side session tracking one release upload (chunks + file definitions) until finalize; carries `IntendedRelease` and gates ingest against billing limits.
- Source: `Core/Entities/IngestSession.cs`, `Server/Endpoints/IngestSessionEndpoints.cs`.

### Tenant / Service Account / API Key
- Meaning: a **Tenant** is an isolation boundary (Single or Multi mode). A **Service Account** is a machine principal within a tenant that owns **API Keys** (`{guid}.{secret}`, scoped, hash-stored) used for non-interactive auth.
- Source: `Core/Entities`, `Server/Auth`.

### Storage Class
- Meaning: a named storage policy (string PK, e.g. `default`) with constraints such as `MaxChunkBytes`; mapped to repositories.
- Source: `Core/Entities/StorageClass.cs`.

### Pack file / Segment / Bloom (`.pack` / `.idx` / `.bloom` / `.log`)
- Meaning: on-disk storage primitives — chunk/file-def payloads live in rotating `.pack` files (magic `BSPK`); an LSM index made of an append `.log`, sorted memory-mapped `IDX2` segments, and per-segment bloom filters provides lookups.
- Source: `Infrastructure/Storage/Packing`, `…/Indexing`; see `dataModelNotes.md`.
