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

using BinStash.Contracts.StorageClass;
using BinStash.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class StorageClassEndpoints
{
    public static RouteGroupBuilder MapStorageClassEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/storage-classes")
            .WithSummary("Endpoints for managing storage classes.")
            .WithTags("Storage Classes")
            .RequireAuthorization("Permission:Instance:Admin"); // TODO: Rewrite to better readable code

        group.MapGet("/", GetStorageClassesAsync)
            .WithName("GetStorageClasses")
            .WithDescription("Retrieves a list of all available storage classes.")
            .Produces<List<StorageClassDetailsDto>>();
        group.MapGet("/default-mappings", GetStorageClassDefaultMappingsAsync)
            .WithName("GetStorageClassDefaultMappings")
            .WithDescription("Retrieves the default mappings of storage classes to chunk stores.")
            .Produces<List<StorageClassDefaultMappingDto>>();

        return group;
    }

    private static async Task<IResult> GetStorageClassesAsync(BinStashDbContext db)
    {
        var storageClasses = await db.StorageClasses
            .Select(sc => new StorageClassDetailsDto
            {
                Name = sc.Name,
                DisplayName = sc.DisplayName,
                Description = sc.Description,
                IsDeprecated = sc.IsDeprecated
            })
            .ToListAsync();

        return Results.Ok(storageClasses);
    }
    
    private static async Task<IResult> GetStorageClassDefaultMappingsAsync(BinStashDbContext db)
    {
        var mappings = await db.StorageClassDefaultMappings
            .Select(m => new StorageClassDefaultMappingDto
            {
                StorageClassName =  m.StorageClassName,
                ChunkStoreId = m.ChunkStoreId,
                IsDefault = m.IsDefault,
                IsEnabled = m.IsEnabled
            })
            .ToListAsync();

        return Results.Ok(mappings);
    }
}