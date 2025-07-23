# FAQ

### ❓ What platforms does BinStash support?
Anything that supports .NET 9, including Linux, Windows, macOS.

### ❓ What kind of storage can I use?
Currently, chunk stores can only be on the local filesystem. Support for other storage types like S3 is planned for future releases.

### ❓ How is deduplication achieved?
Each file is chunked using FastCDC, hashed, and deduplicated by content checksum (BLAKE3).

### ❓ What happens if some chunks are already in the store?
Only missing chunks are uploaded. BinStash automatically checks and uploads only what’s needed.

### ❓ Can I define components manually?
Yes, via a `--component-map` file like:

```
bin/:Core
plugins/:Extensions
```
