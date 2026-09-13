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

using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using System.Web;
using BinStash.Cli.Auth;
using BinStash.Cli.Versioning;
using BinStash.Contracts.ChunkStore;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Ingest;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repo;
using BinStash.Contracts.Tenant;
using BinStash.Core.Chunking;
using BinStash.Core.Compression;
using BinStash.Core.Serialization;
using BinStash.Core.Serialization.Utils;
using CliFx.Infrastructure;
using Microsoft.Extensions.Http;
using Polly;
using Polly.Extensions.Http;
using ZstdNet;

namespace BinStash.Cli.Clients;

public class BinStashApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy = HttpPolicyExtensions.HandleTransientHttpError().OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests).WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    private readonly IConsole? _console;
    
    public BinStashApiClient(string rootUrl, Func<Task<string>>? authTokenFactory = null!, IConsole? console = null, string authScheme = "Bearer")
    {
        authTokenFactory ??= () => Task.FromResult(string.Empty);
        // Wire it into the handler pipeline manually
        var sockets = new SocketsHttpHandler
        {
            // Optional, but good defaults:
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        var policyHandler = new PolicyHttpMessageHandler(_retryPolicy)
        {
            InnerHandler = sockets
        };
        
        var authHandler = new AuthHeaderHandler(authTokenFactory, authScheme)
        {
            InnerHandler = policyHandler
        };
        
        _httpClient = new HttpClient(authHandler)
        {
            BaseAddress = new Uri(rootUrl),
            DefaultRequestHeaders =
            {
                { "Accept", "application/json" },
                { "User-Agent", "BinStash.Cli/1.0" }
            }
        };
        
        _console = console;
    }

    #region Tenant Info

    public async Task<List<TenantInfoDto>?> GetTenantsAsync()
        => await GetAsync("tenants", SourceGenerationContext.Default.ListTenantInfoDto);

    #endregion
    
    #region ChunkStore
    
    public async Task<List<ChunkStoreSummaryDto>?> GetChunkStoresAsync()
        => await GetAsync("chunk-stores", SourceGenerationContext.Default.ListChunkStoreSummaryDto);

    public async Task<ChunkStoreDetailDto?> GetChunkStoreAsync(Guid id)
        => await GetAsync($"chunk-stores/{id}", SourceGenerationContext.Default.ChunkStoreDetailDto);

    public async Task<ChunkStoreDetailDto?> CreateChunkStoreAsync(CreateChunkStoreDto dto)
        => await PostAsJsonAsync("chunk-stores", dto,
            SourceGenerationContext.Default.CreateChunkStoreDto,
            SourceGenerationContext.Default.ChunkStoreDetailDto);

    public async Task DeleteChunkStoreAsync(Guid id)
    {
        await Task.Delay(0);
        throw new NotImplementedException("Chunk store deletion is not implemented yet.");
    }
    
    #endregion
    
    #region Repository
    
    public async Task<List<RepositorySummaryDto>?> GetRepositoriesAsync(Guid tenantId)
        => await GetAsync($"tenants/{tenantId}/repositories", SourceGenerationContext.Default.ListRepositorySummaryDto);

    public async Task<RepositorySummaryDto?> GetRepositoryAsync(Guid tenantId, Guid repositoryId)
        => await GetAsync($"tenants/{tenantId}/repositories/{repositoryId}", SourceGenerationContext.Default.RepositorySummaryDto);

    public async Task<RepositorySummaryDto?> CreateRepositoryAsync(Guid tenantId, CreateRepositoryDto createDto)
        => await PostAsJsonAsync($"tenants/{tenantId}/repositories", createDto,
            SourceGenerationContext.Default.CreateRepositoryDto,
            SourceGenerationContext.Default.RepositorySummaryDto);

    #endregion
    
    #region Release
    
    public async Task<List<ReleaseSummaryDto>?> GetReleasesAsync()
        => await Task.FromResult<List<ReleaseSummaryDto>?>(null);
    
    public async Task<List<ReleaseSummaryDto>?> GetReleasesForRepoAsync(Guid tenantId, Guid repositoryId)
        => await GetAsync($"tenants/{tenantId}/repositories/{repositoryId}/releases",
            SourceGenerationContext.Default.ListReleaseSummaryDto);

    public async Task<ReleaseDefinitionMetrics> CreateReleaseAsync(Guid tenantId, Guid ingestSessionId, string repositoryId, ReleasePackage release, ReleasePackageSerializerOptions? options = null)
    {
        // Serialize .rdef into a pipe: the write end is filled by the serializer and
        // the read end is consumed by HttpClient, avoiding a full in-memory buffer.
        var pipe = new Pipe();
        ReleaseDefinitionMetrics? serializerMetrics = null;

        var countingStream = new CountingStream(pipe.Writer.AsStream());
        var serializeTask = Task.Run(async () =>
        {
            try
            {
                serializerMetrics = await ReleasePackageSerializer.SerializeAsync(countingStream, release, options);
                // Patch TotalBytes: the underlying PipeWriter stream is not seekable so the
                // serializer always records 0. Use the byte count from CountingStream instead.
                serializerMetrics = new ReleaseDefinitionMetrics
                {
                    TotalBytes          = countingStream.BytesWritten,
                    FormatVersion       = serializerMetrics.FormatVersion,
                    UniqueFileCount     = serializerMetrics.UniqueFileCount,
                    ArtifactCount       = serializerMetrics.ArtifactCount,
                    TokenCount          = serializerMetrics.TokenCount,
                    CustomPropertyCount = serializerMetrics.CustomPropertyCount,
                    Elapsed             = serializerMetrics.Elapsed,
                };
            }
            finally
            {
                await pipe.Writer.CompleteAsync();
            }
        });

        // Create multipart form data
        using var form = new MultipartFormDataContent();
        form.Headers.Add("X-Ingest-Session-Id", ingestSessionId.ToString());

        // Add repositoryId as form field
        form.Add(new StringContent(repositoryId), "repositoryId");

        // Add the releaseDefinition file
        var fileContent = new StreamContent(pipe.Reader.AsStream());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bs-rdef");

        // This name MUST match what the server expects: "releaseDefinition"
        form.Add(fileContent, "releaseDefinition", "release.rdef");

        var response = await _httpClient.PostAsync($"tenants/{tenantId}/repositories/{repositoryId}/ingest/sessions/{ingestSessionId}/finalize", form);
        
        await serializeTask; // propagate any serializer exception
        
        response.EnsureSuccessStatusCode();
        
        if (response.StatusCode != HttpStatusCode.Created)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to create release: {response.StatusCode} - {body}");
        }

        return serializerMetrics!;
    }

    
    public async Task<bool> DownloadReleaseAsync(Guid tenantId, Guid repoId, Guid releaseId, string downloadPath, string? component = null)
    {
        var downloadUri = new Uri(_httpClient.BaseAddress!, $"tenants/{tenantId}/repositories/{repoId}/releases/{releaseId}/download");
        var uriBuilder = new UriBuilder(downloadUri);
        var query = HttpUtility.ParseQueryString(string.Empty);
        
        if (!string.IsNullOrWhiteSpace(component))
            query["component"] = component;

        var request = new HttpRequestMessage(HttpMethod.Get, uriBuilder.ToString());
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            _console?.WriteLine($"Bad request: {await response.Content.ReadAsStringAsync()}");
            return false;
        }
        
        response.EnsureSuccessStatusCode();
        
        await using var fsOut = File.OpenWrite(downloadPath);
        await response.Content.CopyToAsync(fsOut);
        await fsOut.FlushAsync();
        
        return true;
    }
    
    #endregion
    
    #region Ingestion Session
    
    public async Task<Guid> CreateIngestSessionAsync(Guid tenantId, Guid repoId, string intendedRelease)
    { 
        var response = await PostAsJsonAsync(
            $"tenants/{tenantId}/repositories/{repoId}/ingest/sessions",
            new CreateIngestSessionRequest($"BinStash.Cli/{Environment.Version}", intendedRelease),
            SourceGenerationContext.Default.CreateIngestSessionRequest,
            SourceGenerationContext.Default.CreateIngestSessionResponse);
        if (response == null)
            throw new InvalidOperationException("Failed to create ingest session: No response from server.");
        if (response.SessionId == Guid.Empty)
            throw new InvalidOperationException("Failed to create ingest session: Invalid session ID returned.");
        return response.SessionId;
    }
    
    public async Task<List<Hash32>> GetMissingChunkChecksumsAsync(Guid tenantId, Guid repoId, Guid ingestSessionId, List<Hash32> chunkChecksums)
        => await PostAsTransposedCompressedByteArrayAsync($"tenants/{tenantId}/repositories/{repoId}/ingest/sessions/{ingestSessionId}/chunks/missing", chunkChecksums);
    
    public async Task<List<Hash32>> GetMissingFileChecksumsAsync(Guid tenantId, Guid repoId, Guid ingestSessionId, List<Hash32> fileChecksums)
        => await PostAsTransposedCompressedByteArrayAsync($"tenants/{tenantId}/repositories/{repoId}/ingest/sessions/{ingestSessionId}/files/missing", fileChecksums);
    
    public async Task UploadChunksAsync(Guid tenantId, Guid repoId, Guid ingestSessionId, IChunker chunker, IEnumerable<ChunkMapEntry> chunksToUpload, int batchSize = 100, Func<int, int, Task>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        var allChunks = chunksToUpload.ToList();
        var total = allChunks.Count;
        var uploaded = 0;

        foreach (var batch in allChunks.Chunk(batchSize))
        {
            var uploadDtos = new List<ChunkUploadDto>();
            foreach (var entry in batch)
            {
                var data = (await chunker.LoadChunkDataAsync(entry, cancellationToken)).Data;
                uploadDtos.Add(new ChunkUploadDto { Checksum = entry.Checksum.ToHexString(), Data = data });
            }

            var request = new HttpRequestMessage(HttpMethod.Post, $"tenants/{tenantId}/repositories/{repoId}/ingest/sessions/{ingestSessionId}/chunks/batch");
            request.Headers.Add("X-Ingest-Session-Id", ingestSessionId.ToString());
            request.Content = JsonContent.Create(uploadDtos, SourceGenerationContext.Default.ListChunkUploadDto);
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            uploaded += batch.Length;
            if (progressCallback != null)
                await progressCallback(uploaded, total);
        }
    }
    
    public async Task UploadFileDefinitionsAsync(Guid tenantId, Guid repoId, Guid ingestSessionId, Dictionary<Hash32, (List<Hash32> Chunks, long Length)> fileDefinitionsToUpload, int batchSize = 1000, Func<int, int, Task>? progressCallback = null, CancellationToken cancellationToken = default)
    { 
        var allChunks = fileDefinitionsToUpload.Values.SelectMany(x => x.Chunks).Distinct().ToList();
        var total = allChunks.Count;
        var uploaded = 0;

        foreach (var batch in fileDefinitionsToUpload.Chunk(batchSize))
        {
            var hashesForBatch = batch.SelectMany(x => x.Value.Chunks).Distinct().ToList();
            using var ms = new MemoryStream();
            await using (var compressionStream = new CompressionStream(ms))
            {
                await compressionStream.WriteAsync(
                    ChecksumCompressor.TransposeCompress(hashesForBatch.Select(x => x.GetBytes()).ToList()),
                    cancellationToken);

                await VarIntUtils.WriteVarIntAsync(compressionStream, batch.Length, cancellationToken);

                foreach (var (fileHash, (chunkList, fileLength)) in batch)
                {
                    await compressionStream.WriteAsync(fileHash.GetBytes(), cancellationToken);
                    await VarIntUtils.WriteVarIntAsync(compressionStream, fileLength, cancellationToken);
                    await VarIntUtils.WriteVarIntAsync(compressionStream, chunkList.Count, cancellationToken);

                    var indexMap = new Dictionary<Hash32,int>(hashesForBatch.Count);
                    for (var idx = 0; idx < hashesForBatch.Count; idx++)
                        indexMap[hashesForBatch[idx]] = idx;

                    foreach (var chunkHash in chunkList)
                        await VarIntUtils.WriteVarIntAsync(compressionStream, indexMap[chunkHash], cancellationToken);
                }
            } // the scope ends here, so we're disposing the compression stream which ensures end-of-frame is written

            var compressedData = ms.ToArray();
        
            var request = new HttpRequestMessage(HttpMethod.Post, $"tenants/{tenantId}/repositories/{repoId}/ingest/sessions/{ingestSessionId}/files/batch");
            request.Headers.Add("X-Ingest-Session-Id", ingestSessionId.ToString());
            request.Content = new ByteArrayContent(compressedData);
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            uploaded += batch.Length;
            if (progressCallback != null)
                await progressCallback(uploaded, total);
        }
    }
    
    #endregion

    #region Helpers

    /// <summary>
    /// Checks for a <c>426 Upgrade Required</c> response from the version gate
    /// and throws <see cref="CliVersionIncompatibleException"/> with details
    /// from the problem-JSON body.  Otherwise delegates to
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>.
    /// </summary>
    private static async Task EnsureCompatibleAsync(HttpResponseMessage response)
    {
        if ((int)response.StatusCode == 426)
        {
            // Try to read the structured problem body so we can surface precise
            // version info to the user.
            var clientVersion = CliVersion.Value;
            var minimumVersion = "unknown";
            var serverVersion = "unknown";

            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync());
                var root = doc.RootElement;
                if (root.TryGetProperty("clientVersion", out var cv)) clientVersion = cv.GetString() ?? clientVersion;
                if (root.TryGetProperty("minimumVersion", out var mv)) minimumVersion = mv.GetString() ?? minimumVersion;
                if (root.TryGetProperty("serverVersion", out var sv)) serverVersion = sv.GetString() ?? serverVersion;
            }
            catch { /* fall back to defaults */ }

            throw new CliVersionIncompatibleException(clientVersion, minimumVersion, serverVersion);
        }

        response.EnsureSuccessStatusCode();
    }

    private async Task<T?> GetAsync<T>(string path, JsonTypeInfo<T> typeInfo)
    {
        var response = await _httpClient.GetAsync(path);
        await EnsureCompatibleAsync(response);
        var content = await response.Content.ReadFromJsonAsync(typeInfo);
        return content;
    }

    private async Task<TResponse?> PostAsJsonAsync<TRequest, TResponse>(string path, TRequest body, JsonTypeInfo<TRequest> requestTypeInfo, JsonTypeInfo<TResponse> responseTypeInfo)
    {
        var response = await _httpClient.PostAsJsonAsync(path, body, requestTypeInfo);
        await EnsureCompatibleAsync(response);
        var content = await response.Content.ReadFromJsonAsync(responseTypeInfo);
        return content;
    }
    
    private async Task<List<Hash32>> PostAsTransposedCompressedByteArrayAsync(string path, List<Hash32> checksums)
    {
        var compressedContent = ChecksumCompressor.TransposeCompress(checksums.Select(x => x.GetBytes()).ToList());
        var response = await _httpClient.PostAsync(path, new ByteArrayContent(compressedContent));
        await EnsureCompatibleAsync(response);
        var respStream = await response.Content.ReadAsStreamAsync();
        return await ChecksumCompressor.TransposeDecompressHashesAsync(respStream);
    }

    #endregion

    /// <summary>Write-only stream wrapper that counts bytes forwarded to the inner stream.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead  => false;
        public override bool CanSeek  => false;
        public override bool CanWrite => true;
        public override long Length   => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(buffer, offset, count, cancellationToken);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            BytesWritten += buffer.Length;
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}