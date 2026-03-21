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

namespace BinStash.Server.GraphQL;

public sealed class RepositoryGql
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string StorageClass { get; init; }
    public ChunkStoreChunkerGql? Chunker { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class ChunkStoreChunkerGql
{
    public required string Type { get; init; }
    public int? MinChunkSize { get; init; }
    public int? AvgChunkSize { get; init; }
    public int? MaxChunkSize { get; init; }
}

public sealed class ReleaseGql
{
    public required Guid Id { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Notes { get; init; }
    public Guid RepoId { get; init; }
    public object? CustomProperties { get; init; }
}

public sealed class ServiceAccountGql
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class UserGql
{
    public required Guid Id { get; init; }
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    // TODO: List of all roles for this user (instance and tenant)
    public required bool IsEmailVerified { get; init; }
    public required bool IsOnboardingCompleted { get; init; }
}

public sealed class TenantGql
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; set; }
    public required  DateTimeOffset CreatedAt { get; init; }
    
    public DateTimeOffset? JoinedAt { get; init; }
}

public sealed class ChunkStoreGql
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
}