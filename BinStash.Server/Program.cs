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

using BinStash.Infrastructure.Data;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

namespace BinStash.Server;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSystemd();
        builder.Services.AddWindowsService();
        
        // Add services to the container.
        builder.Services.AddResponseCompression();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthorization();

        builder.Services.AddDbContext<BinStashDbContext>((_, optionsBuilder) => optionsBuilder.UseNpgsql(builder.Configuration.GetConnectionString("BinStashDb"))/*.EnableSensitiveDataLogging()*/);

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();
        
        // Configure the ef core migration process
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
            db.Database.Migrate(); // applies any pending migrations
        });

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.UseHttpsRedirection();
        app.UseResponseCompression();
        app.UseAuthorization();
        app.MapAllEndpoints();
        
        app.Run();
    }
}