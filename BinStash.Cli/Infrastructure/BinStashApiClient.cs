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
using BinStash.Contracts.ChunkStore;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repos;
using BinStash.Core.Chunking;
using BinStash.Core.Compression;
using BinStash.Core.Serialization;
using BinStash.Core.Types;
using RestSharp;

namespace BinStash.Cli.Infrastructure;

public class BinStashApiClient
{
    private readonly string _rootUrl;
    private readonly RestClientOptions _restClientOptions;

    public BinStashApiClient(string rootUrl)
    {
        _rootUrl = rootUrl;
        _restClientOptions = new RestClientOptions
        {
            BaseUrl = new Uri(_rootUrl)
        };
    }
    
    #region ChunkStore
    
    public async Task<List<ChunkStoreSummaryDto>?> GetChunkStoresAsync()
    {
        using var client = new RestClient(_restClientOptions);
        return await client.GetAsync<List<ChunkStoreSummaryDto>>("api/chunkstores");
    }
    
    public async Task<ChunkStoreDetailDto?> GetChunkStoreAsync(Guid id)
    {
        using var client = new RestClient(_restClientOptions);
        return await client.GetAsync<ChunkStoreDetailDto>($"api/chunkstores/{id}");
    }
    
    public async Task<ChunkStoreDetailDto?> CreateChunkStoreAsync(CreateChunkStoreDto dto)
    {
        using var client = new RestClient(_restClientOptions);
        var request = new RestRequest("api/chunkstores", Method.Post);
        request.AddJsonBody(dto);
        
        var response = await client.ExecuteAsync(request);
        
        if (!response.IsSuccessful || response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Failed to create chunk store: {response.StatusCode} {response.ErrorMessage}");
        }
        
        // Parse Location header
        var locationHeader = response.Headers?
            .FirstOrDefault(h => h.Name.Equals("Location", StringComparison.OrdinalIgnoreCase))
            ?.Value.ToString();

        if (string.IsNullOrWhiteSpace(locationHeader))
        {
            throw new InvalidOperationException("Missing Location header in response.");
        }
        
        // Make follow-up GET request // TODO: Maybe use the existing GetChunkStoreAsync method instead?
        var detailRequest = new RestRequest(locationHeader);
        return await client.GetAsync<ChunkStoreDetailDto>(detailRequest);
    }
    
    public async Task DeleteChunkStoreAsync(Guid id)
    {
        using var client = new RestClient(_restClientOptions);
        var request = new RestRequest($"api/chunkstores/{id}", Method.Delete);
        
        var response = await client.ExecuteAsync(request);
        
        if (!response.IsSuccessful)
        {
            throw new InvalidOperationException($"Failed to delete chunk store: {response.StatusCode} {response.ErrorMessage}");
        }
    }
    
    public async Task<List<string>> GetMissingChunkChecksumAsync(Guid id,  List<Hash32> chunkChecksums)
    public async Task<List<Hash32>> GetMissingChunkChecksumAsync(Guid id,  List<Hash32> chunkChecksums)
    {
        using var client = new HttpClient();
        client.BaseAddress = new Uri(_rootUrl);
        var resp = await client.PostAsync($"api/chunkstores/{id}/chunks/missing", new ByteArrayContent(ChecksumCompressor.TransposeCompress(chunkChecksums.Select(x => x.GetBytes()).ToList())));
        resp.EnsureSuccessStatusCode();
        await File.WriteAllBytesAsync(@"C:\Tmp\missing-checksums.client.bin", await resp.Content.ReadAsByteArrayAsync());
        var respStream = await resp.Content.ReadAsStreamAsync();
        var decompressedChecksums = await ChecksumCompressor.TransposeDecompressAsync(respStream);
        return decompressedChecksums.Select(x => new Hash32(x)).ToList();
    }
    
    public async Task UploadChunkStoreFileAsync(Guid id, string chunkChecksum, byte[] chunkData)
    {
        using var client = new RestClient(_restClientOptions);
        var request = new RestRequest($"api/chunkstores/{id}/chunks/{chunkChecksum}", Method.Post);
        
        // Add file to the request
        request.AddBody(chunkData, "application/octet-stream");
        
        var response = await client.ExecuteAsync(request);
        
        if (!response.IsSuccessful)
        {
            throw new InvalidOperationException($"Failed to upload file to chunk store: {response.StatusCode} {response.ErrorMessage}{Environment.NewLine}{response.Content}");
        }
    }
    
    // TODO: Implement gRPC-based upload method to support smoother chunk uploads
    public async Task UploadChunksAsync(IChunker chunker, Guid chunkStoreId, IEnumerable<ChunkMapEntry> chunksToUpload, int batchSize = 100, Func<int, int, Task>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        using var client = new RestClient(_restClientOptions);
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

            var request = new RestRequest($"api/chunkstores/{chunkStoreId}/chunks/batch", Method.Post)
                .AddJsonBody(uploadDtos);

            var response = await client.ExecuteAsync(request, cancellationToken);
            if (!response.IsSuccessful)
            {
                throw new InvalidOperationException($"Upload failed: {response.StatusCode} {response.Content}");
            }

            uploaded += batch.Length;
            if (progressCallback != null)
                await progressCallback(uploaded, total);
        }
    }
    
    #endregion
    
    #region Repository
    
    public async Task<List<RepositorySummaryDto>?> GetRepositoriesAsync()
    {
        using var client = new RestClient(_restClientOptions);
        return await client.GetAsync<List<RepositorySummaryDto>>("api/repositories");
    }
    
    public async Task<RepositorySummaryDto?> CreateRepositoryAsync(CreateRepositoryDto createDto)
    {
        using var client = new RestClient(_restClientOptions);
        var request = new RestRequest("api/repositories", Method.Post);
        request.AddJsonBody(createDto);
        
        var response = await client.ExecuteAsync(request);
        
        if (!response.IsSuccessful || response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Failed to create repository: {response.StatusCode} {response.ErrorMessage}");
        }
        
        // Parse Location header
        var locationHeader = response.Headers?
            .FirstOrDefault(h => h.Name.Equals("Location", StringComparison.OrdinalIgnoreCase))
            ?.Value.ToString();
        
        if (string.IsNullOrWhiteSpace(locationHeader))
        {
            throw new InvalidOperationException("Missing Location header in response.");
        }
        
        // Make follow-up GET request
        var detailRequest = new RestRequest(locationHeader);
        return await client.GetAsync<RepositorySummaryDto>(detailRequest);
    }
    
    #endregion
    
    #region Release
    
    public async Task<List<ReleaseSummaryDto>?> GetReleasesAsync()
    {
        using var client = new RestClient(_restClientOptions);
        return await client.GetAsync<List<ReleaseSummaryDto>>("api/releases");
    }
    
    public async Task<List<ReleaseSummaryDto>?> GetReleasesForRepoAsync(Guid repositoryId)
    {
        using var client = new RestClient(_restClientOptions);
        var request = new RestRequest($"api/repositories/{repositoryId}/releases");
        return await client.GetAsync<List<ReleaseSummaryDto>>(request);
    }
    
    public async Task CreateReleaseAsync(string repositoryId, ReleasePackage release)
    {
        using var client = new HttpClient(); // ideally use IHttpClientFactory
        using var uploadStream = new MemoryStream();
        await ReleasePackageSerializer.SerializeAsync(uploadStream, release);
        
        uploadStream.Position = 0;

        // Create multipart form data
        using var form = new MultipartFormDataContent();

        // Add repositoryId as form field
        form.Add(new StringContent(repositoryId), "repositoryId");

        // Add the releaseDefinition file
        var fileContent = new StreamContent(uploadStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bs-rdef");

        // This name MUST match what the server expects: "releaseDefinition"
        form.Add(fileContent, "releaseDefinition", "release.rdef");

        // Updated URL — now posts to /api/releases (not /api/repositories/{id}/releases)
        var url = new Uri(new Uri(_rootUrl), "api/releases");
        var response = await client.PostAsync(url, form);

        if (!response.IsSuccessStatusCode || response.StatusCode != HttpStatusCode.Created)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to create release: {response.StatusCode} - {body}");
        }
    }

    
    public async Task<bool> DownloadReleaseAsync(Guid releaseId, string downloadPath, string? component = null)
    {
        using var client = new RestClient(_restClientOptions);
        var request = new RestRequest($"api/releases/{releaseId}/download");
        if (!string.IsNullOrWhiteSpace(component))
            request.AddQueryParameter("component", component);

        var response = await client.DownloadStreamAsync(request);
        
        if (response == null) 
            return false;

        await using var fsOut = File.OpenWrite(downloadPath);
        await response.CopyToAsync(fsOut);
        await fsOut.FlushAsync();
        
        return true;
    }
    
    #endregion


    
}