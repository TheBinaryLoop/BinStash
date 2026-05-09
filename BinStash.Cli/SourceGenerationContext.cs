// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Text.Json;
using System.Text.Json.Serialization;
using BinStash.Cli.Auth;
using BinStash.Cli.Infrastructure.GraphQl;
using BinStash.Cli.Infrastructure.Svn;
using BinStash.Contracts.ChunkStore;
using BinStash.Contracts.Ingest;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repo;
using BinStash.Contracts.Tenant;

namespace BinStash.Cli;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowOutOfOrderMetadataProperties = true)]
// Auth
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(TokenInfo))]
[JsonSerializable(typeof(AuthFile))]
[JsonSerializable(typeof(Dictionary<string, TokenInfo>))]
// Tenant
[JsonSerializable(typeof(List<TenantInfoDto>))]
// ChunkStore
[JsonSerializable(typeof(List<ChunkStoreSummaryDto>))]
[JsonSerializable(typeof(ChunkStoreDetailDto))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(CreateChunkStoreDto))]
[JsonSerializable(typeof(ChunkStoreChunkerDto))]
[JsonSerializable(typeof(ChunkStoreBackendSettingsDto))]
[JsonSerializable(typeof(List<ChunkUploadDto>))]
[JsonSerializable(typeof(ChunkUploadDto))]
// Repository
[JsonSerializable(typeof(List<RepositorySummaryDto>))]
[JsonSerializable(typeof(RepositorySummaryDto))]
[JsonSerializable(typeof(CreateRepositoryDto))]
// Release
[JsonSerializable(typeof(List<ReleaseSummaryDto>))]
[JsonSerializable(typeof(ReleaseSummaryDto))]
// Ingest
[JsonSerializable(typeof(CreateIngestSessionRequest))]
[JsonSerializable(typeof(CreateIngestSessionResponse))]
// SVN cache models
[JsonSerializable(typeof(CachedChunkMap))]
[JsonSerializable(typeof(List<ChunkMapCacheEntry>))]
[JsonSerializable(typeof(ChunkMapCacheEntry))]
// GraphQL — request bodies
[JsonSerializable(typeof(GqlRequest), TypeInfoPropertyName = "GqlRequestBody")]
[JsonSerializable(typeof(GqlRequestById), TypeInfoPropertyName = "GqlRequestByIdBody")]
[JsonSerializable(typeof(GqlPagedRequest), TypeInfoPropertyName = "GqlPagedRequestBody")]
[JsonSerializable(typeof(GqlRequestByIdPaged), TypeInfoPropertyName = "GqlRequestByIdPagedBody")]
[JsonSerializable(typeof(GqlCreateRepositoryRequest), TypeInfoPropertyName = "GqlCreateRepositoryRequestBody")]
[JsonSerializable(typeof(GqlCreateChunkStoreRequest), TypeInfoPropertyName = "GqlCreateChunkStoreRequestBody")]
// GraphQL — response envelopes
[JsonSerializable(typeof(GqlResponse<GqlRepositoriesData>))]
[JsonSerializable(typeof(GqlResponse<GqlRepositoryData>))]
[JsonSerializable(typeof(GqlResponse<GqlCreateRepositoryData>))]
[JsonSerializable(typeof(GqlResponse<GqlRepositoryWithReleasesData>))]
[JsonSerializable(typeof(GqlResponse<GqlChunkStoresData>))]
[JsonSerializable(typeof(GqlResponse<GqlChunkStoreData>))]
[JsonSerializable(typeof(GqlResponse<GqlCreateChunkStoreData>))]
internal partial class SourceGenerationContext : JsonSerializerContext { }

