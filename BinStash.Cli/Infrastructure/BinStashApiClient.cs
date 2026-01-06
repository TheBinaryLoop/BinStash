// Copyright (C) 2025  Lukas Eßmann
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

using System.Net;
using System.Net.Http.Json;
using System.Web;
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

namespace BinStash.Cli.Infrastructure;

public class BinStashApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy = HttpPolicyExtensions.HandleTransientHttpError().OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests).WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    private readonly IConsole? _console;
    
    public BinStashApiClient(string rootUrl, Func<string>? authTokenFactory = null!, IConsole? console = null)
    {
        authTokenFactory ??= () => string.Empty;
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
        
        _httpClient = new HttpClient(policyHandler)
        {
            BaseAddress = new Uri(rootUrl),
            DefaultRequestHeaders =
            {
                { "Accept", "application/json" },
                { "User-Agent", "BinStash.Cli/1.0" },
                { "Authorization", $"Bearer {authTokenFactory()}" }
            }
        };
        _console = console;
    }

    #region Tenant Info

    public async Task<List<TenantInfoDto>?> GetTenantsAsync()
        => await GetAsync<List<TenantInfoDto>>("tenants");

    #endregion
    
    #region ChunkStore
    
    public async Task<List<ChunkStoreSummaryDto>?> GetChunkStoresAsync()
        => await GetAsync<List<ChunkStoreSummaryDto>>("chunkstores");
    
    public async Task<ChunkStoreDetailDto?> GetChunkStoreAsync(Guid id)
        => await GetAsync<ChunkStoreDetailDto>($"chunkstores/{id}");
    
    public async Task<ChunkStoreDetailDto?> CreateChunkStoreAsync(CreateChunkStoreDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync("chunkstores", dto);
        response.EnsureSuccessStatusCode();
        
        if (response.StatusCode != HttpStatusCode.Created) return null;
        
        // Parse Location header
        var locationHeader = response.Headers
            .FirstOrDefault(h => h.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
            .Value.ToString();
            
        if (string.IsNullOrWhiteSpace(locationHeader))
            throw new InvalidOperationException("Missing Location header in response.");
            
        // Make follow-up GET request // TODO: Maybe use the existing GetChunkStoreAsync method instead?
        response = await _httpClient.GetAsync(locationHeader);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<ChunkStoreDetailDto>();
        return content;
    }
    
    public async Task DeleteChunkStoreAsync(Guid id)
    {
        await Task.Delay(0);
        /*using var client = new RestClient(_restClientOptions);
        var request = new RestRequest($"chunkstores/{id}", Method.Delete);
        
        var response = await client.ExecuteAsync(request);
        
        if (!response.IsSuccessful)
        {
            throw new InvalidOperationException($"Failed to delete chunk store: {response.StatusCode} {response.ErrorMessage}");
        }*/
        throw new NotImplementedException("Chunk store deletion is not implemented yet.");
    }
    
    public async Task<List<Hash32>> GetMissingChunkChecksumsAsync(Guid repoId, Guid ingestSessionId,  List<Hash32> chunkChecksums)
        => await PostAsTransposedCompressedByteArrayAsync($"repositories/{repoId}/ingest/sessions/{ingestSessionId}/chunks/missing", chunkChecksums);
    
    public async Task<List<Hash32>> GetMissingFileChecksumsAsync(Guid repoId, Guid ingestSessionId, List<Hash32> fileChecksums)
        => await PostAsTransposedCompressedByteArrayAsync($"repositories/{repoId}/ingest/sessions/{ingestSessionId}/files/missing", fileChecksums);
    
    // TODO: Implement gRPC-based upload method to support smoother chunk uploads
    public async Task UploadChunksAsync(Guid repoId, Guid ingestSessionId, IChunker chunker, IEnumerable<ChunkMapEntry> chunksToUpload, int batchSize = 100, Func<int, int, Task>? progressCallback = null, CancellationToken cancellationToken = default)
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

            var request = new HttpRequestMessage(HttpMethod.Post, $"repositories/{repoId}/ingest/sessions/{ingestSessionId}/chunks/batch");
            request.Headers.Add("X-Ingest-Session-Id", ingestSessionId.ToString());
            request.Content = JsonContent.Create(uploadDtos);
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            uploaded += batch.Length;
            if (progressCallback != null)
                await progressCallback(uploaded, total);
        }
    }
    
    public async Task UploadFileDefinitionsAsync(Guid repoId, Guid ingestSessionId, Dictionary<Hash32, (List<Hash32> Chunks, long Length)> fileDefinitionsToUpload, int batchSize = 1000, Func<int, int, Task>? progressCallback = null, CancellationToken cancellationToken = default)
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

                VarIntUtils.WriteVarInt(compressionStream, batch.Length);

                foreach (var (fileHash, (chunkList, fileLength)) in batch)
                {
                    await compressionStream.WriteAsync(fileHash.GetBytes(), cancellationToken);
                    VarIntUtils.WriteVarInt(compressionStream, fileLength);
                    VarIntUtils.WriteVarInt(compressionStream, chunkList.Count);

                    var indexMap = new Dictionary<Hash32,int>(hashesForBatch.Count);
                    for (var idx = 0; idx < hashesForBatch.Count; idx++)
                        indexMap[hashesForBatch[idx]] = idx;

                    foreach (var chunkHash in chunkList)
                        VarIntUtils.WriteVarInt(compressionStream, indexMap[chunkHash]);
                }
            } // the scope ends here, so we're disposing the compression stream which ensures end-of-frame is written

            var compressedData = ms.ToArray();
        
            var request = new HttpRequestMessage(HttpMethod.Post, $"repositories/{repoId}/ingest/sessions/{ingestSessionId}/files/batch");
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
    
    #region Repository
    
    public async Task<List<RepositorySummaryDto>?> GetRepositoriesAsync()
        => await GetAsync<List<RepositorySummaryDto>>("repositories");
    
    public async Task<RepositorySummaryDto?> GetRepositoryAsync(Guid repositoryId)
        => await GetAsync<RepositorySummaryDto>($"repositories/{repositoryId}");
    
    public async Task<RepositorySummaryDto?> CreateRepositoryAsync(CreateRepositoryDto createDto)
    {
        var response = await _httpClient.PostAsJsonAsync("repositories", createDto);
        response.EnsureSuccessStatusCode();
        
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Failed to create repository: {response.StatusCode} {response.ReasonPhrase}");
        }
        
        // Parse Location header
        var locationHeader = response.Headers
            .FirstOrDefault(h => h.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
            .Value.FirstOrDefault()?.ToString();
        
        if (string.IsNullOrWhiteSpace(locationHeader))
        {
            throw new InvalidOperationException("Missing Location header in response.");
        }
        
        // Make the follow-up GET request
        return await GetAsync<RepositorySummaryDto>(locationHeader);
    }
    
    #endregion
    
    #region Release
    
    public async Task<List<ReleaseSummaryDto>?> GetReleasesAsync()
        => await GetAsync<List<ReleaseSummaryDto>>("releases");
    
    public async Task<List<ReleaseSummaryDto>?> GetReleasesForRepoAsync(Guid repositoryId)
        => await GetAsync<List<ReleaseSummaryDto>>($"repositories/{repositoryId}/releases");
    
    public async Task CreateReleaseAsync(Guid ingestSessionId, string repositoryId, ReleasePackage release, ReleasePackageSerializerOptions? options = null)
    {
        using var uploadStream = new MemoryStream();
        await ReleasePackageSerializer.SerializeAsync(uploadStream, release, options);
        
        uploadStream.Position = 0;

        // Create multipart form data
        using var form = new MultipartFormDataContent();
        form.Headers.Add("X-Ingest-Session-Id", ingestSessionId.ToString());

        // Add repositoryId as form field
        form.Add(new StringContent(repositoryId), "repositoryId");

        // Add the releaseDefinition file
        var fileContent = new StreamContent(uploadStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bs-rdef");

        // This name MUST match what the server expects: "releaseDefinition"
        form.Add(fileContent, "releaseDefinition", "release.rdef");

        var response = await _httpClient.PostAsync($"repositories/{repositoryId}/ingest/sessions/{ingestSessionId}/finalize", form);
        response.EnsureSuccessStatusCode();
        
        if (response.StatusCode != HttpStatusCode.Created)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to create release: {response.StatusCode} - {body}");
        }
    }

    
    public async Task<bool> DownloadReleaseAsync(Guid repoId, Guid releaseId, string downloadPath, string? component = null)
    {
        var downloadUri = new Uri(_httpClient.BaseAddress!, $"repositories/{repoId}/releases/{releaseId}/download");
        var uriBuilder = new UriBuilder(downloadUri);
        var query = HttpUtility.ParseQueryString(string.Empty);
        
        if (!string.IsNullOrWhiteSpace(component))
            query["component"] = component;

        uriBuilder.Query = query.ToString();
        Console.WriteLine(uriBuilder.ToString());
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
    
    public async Task<Guid> CreateIngestSessionAsync(Guid repoId, string intendedRelease)
    { 
        var response = await PostAsJsonAsync<CreateIngestSessionResponse>($"repositories/{repoId}/ingest/sessions", new CreateIngestSessionRequest($"BinStash.Cli/{Environment.Version}", intendedRelease));
        if (response == null)
            throw new InvalidOperationException("Failed to create ingest session: No response from server.");
        if (response.SessionId == Guid.Empty)
            throw new InvalidOperationException("Failed to create ingest session: Invalid session ID returned.");
        return response.SessionId;
    }
    
    #endregion

    #region Helpers

    private async Task<T?> GetAsync<T>(string path)
    {
        _console?.WriteLine($"{_httpClient.BaseAddress}{path}");
        var response = await _httpClient.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<T>();
        return content;
    }

    private async Task<T?> PostAsJsonAsync<T>(string path, object? body)
    {
        _console?.WriteLine($"POST: {_httpClient.BaseAddress}{path}");
        var response = await _httpClient.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<T>();
        return content;
    }
    
    private async Task<List<Hash32>> PostAsTransposedCompressedByteArrayAsync(string path, List<Hash32> checksums)
    {
        _console?.WriteLine($"POST: {_httpClient.BaseAddress}{path}");
        var compressedContent = ChecksumCompressor.TransposeCompress(checksums.Select(x => x.GetBytes()).ToList());
        var response = await _httpClient.PostAsync(path, new ByteArrayContent(compressedContent));
        response.EnsureSuccessStatusCode();
        var respStream = await response.Content.ReadAsStreamAsync();
        var decompressedChecksums = await ChecksumCompressor.TransposeDecompressAsync(respStream);
        return decompressedChecksums.Select(x => new Hash32(x)).ToList();
    }

    #endregion
}