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

using Amazon.S3;
using Amazon.S3.Model;
using BinStash.Infrastructure.Storage.Packing;

namespace BinStash.Infrastructure.Storage.S3;

/// <summary>
/// Buffers chunk data into a local temporary pack file and flushes it to S3 as a single
/// object, using multipart upload for files larger than 5 MiB and a simple PutObject otherwise.
/// </summary>
/// <remarks>
/// The caller is responsible for serializing write access. <see cref="S3ChunkStoreStorage"/>
/// uses a <see cref="SemaphoreSlim"/> to ensure only one thread writes at a time.
/// </remarks>
internal sealed class S3PackWriter : IAsyncDisposable
{
    /// <summary>AWS S3 minimum part size for all parts except the last (5 MiB).</summary>
    private const long AwsMinPartSize = 5L * 1024 * 1024;

    private readonly string _tempFilePath;
    private readonly FileStream _tempFileStream;
    private readonly Dictionary<string, (long Offset, int Length)> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>S3 object key that this pack will be stored at after flush.</summary>
    public string PackKey { get; }

    /// <summary>Current byte size of the local temp pack file.</summary>
    public long CurrentSize => _tempFileStream.Position;

    /// <summary>Number of chunk entries written to this pack.</summary>
    public int EntryCount => _entries.Count;

    /// <summary>
    /// Creates a new pack writer, allocating a temporary file in <paramref name="localCachePath"/>.
    /// </summary>
    /// <param name="localCachePath">Directory for the temp file.</param>
    /// <param name="prefix">S3 key prefix (e.g. <c>"binstash/prod/"</c>).</param>
    public S3PackWriter(string localCachePath, string prefix)
    {
        var guid = Guid.NewGuid().ToString("N");
        PackKey = $"{prefix}chunks/{guid}.pack";
        _tempFilePath = Path.Combine(localCachePath, $"{guid}.pack.tmp");
        _tempFileStream = new FileStream(
            _tempFilePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 65536,
            FileOptions.Asynchronous);
    }

    /// <summary>
    /// Appends a chunk to the pack file using the <see cref="PackFileEntry"/> format (BSPK header + Zstd).
    /// </summary>
    /// <returns>Number of bytes written (header + compressed payload).</returns>
    public async Task<int> WriteEntryAsync(string hexHash, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var (offset, length) = await PackFileEntry.WriteAsync(_tempFileStream, data, ct).ConfigureAwait(false);
        _entries[hexHash] = (offset, length);
        return length;
    }

    /// <summary>Returns true if the given hash is already buffered in this writer.</summary>
    public bool HasEntry(string hexHash) => _entries.ContainsKey(hexHash);

    /// <summary>
    /// Tries to retrieve the pack-file location of a buffered chunk.
    /// </summary>
    public bool TryGetEntry(string hexHash, out (long Offset, int Length) location)
        => _entries.TryGetValue(hexHash, out location);

    /// <summary>
    /// Returns a snapshot of all entries recorded in this writer.
    /// </summary>
    public IReadOnlyDictionary<string, (long Offset, int Length)> GetEntries() => _entries;

    /// <summary>
    /// Reads a single pack entry from the local temp file at the given offset.
    /// </summary>
    public Task<byte[]?> ReadEntryAtAsync(long offset, CancellationToken ct = default)
        => PackFileEntry.ReadAtAsync(_tempFileStream.SafeFileHandle, offset, ct: ct);

    /// <summary>
    /// Flushes the temp file to disk and uploads it to S3 as <see cref="PackKey"/>.
    /// Uses multipart upload for files ≥ <paramref name="partSizeBytes"/> (adjusted to the
    /// AWS minimum of 5 MiB), and a single PutObject for smaller files.
    /// </summary>
    public async Task FlushAndUploadAsync(IAmazonS3 client, string bucket, long partSizeBytes, CancellationToken ct = default)
    {
        await _tempFileStream.FlushAsync(ct).ConfigureAwait(false);
        _tempFileStream.Position = 0;

        var fileSize = _tempFileStream.Length;
        if (fileSize == 0)
            return;

        // Clamp part size to AWS limits (min 5 MiB, max 2 GiB per ReadAsync buffer allocation).
        var effectivePartSize = Math.Clamp(partSizeBytes, AwsMinPartSize, 2L * 1024 * 1024 * 1024);

        if (fileSize < AwsMinPartSize)
        {
            // Small pack: use a single PutObject.
            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = PackKey,
                InputStream = _tempFileStream,
            };
            await client.PutObjectAsync(request, ct).ConfigureAwait(false);
        }
        else
        {
            await UploadMultipartAsync(client, bucket, effectivePartSize, ct).ConfigureAwait(false);
        }
    }

    private async Task UploadMultipartAsync(IAmazonS3 client, string bucket, long partSizeBytes, CancellationToken ct)
    {
        var initiateResponse = await client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = bucket, Key = PackKey }, ct)
            .ConfigureAwait(false);

        var uploadId = initiateResponse.UploadId;
        var completedParts = new List<PartETag>();

        try
        {
            _tempFileStream.Position = 0;
            var fileSize = _tempFileStream.Length;
            var bytesRemaining = fileSize;
            var partNumber = 1;
            var buffer = new byte[(int)partSizeBytes];

            while (bytesRemaining > 0)
            {
                var readSize = (int)Math.Min(partSizeBytes, bytesRemaining);
                await _tempFileStream.ReadExactlyAsync(buffer.AsMemory(0, readSize), ct).ConfigureAwait(false);

                var partResponse = await client.UploadPartAsync(new UploadPartRequest
                {
                    BucketName = bucket,
                    Key = PackKey,
                    UploadId = uploadId,
                    PartNumber = partNumber,
                    InputStream = new MemoryStream(buffer, 0, readSize, writable: false),
                    PartSize = readSize,
                }, ct).ConfigureAwait(false);

                completedParts.Add(new PartETag { PartNumber = partNumber, ETag = partResponse.ETag });
                bytesRemaining -= readSize;
                partNumber++;
            }

            await client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
            {
                BucketName = bucket,
                Key = PackKey,
                UploadId = uploadId,
                PartETags = completedParts,
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort abort to avoid leaving orphaned multipart uploads (incurs storage charges).
            try
            {
                await client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
                {
                    BucketName = bucket,
                    Key = PackKey,
                    UploadId = uploadId,
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Swallow abort failure — the original exception is the important one.
            }

            throw;
        }
    }

    /// <summary>
    /// Deletes the local temporary pack file. Called after a successful upload.
    /// </summary>
    public void DeleteTempFile()
    {
        try
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
        }
        catch
        {
            // Best-effort; orphaned temp files are cleaned on next startup.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _tempFileStream.DisposeAsync().ConfigureAwait(false);
        DeleteTempFile();
    }
}
