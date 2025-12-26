// Copyright (C) 2025  Lukas Eßmann
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

using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Configuration.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinStash.Server.HostedServices;

public class SingleTenantBootstrapper(IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        
        var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
        var opt = scope.ServiceProvider.GetRequiredService<IOptions<TenancyOptions>>().Value;
        
        if (opt.Mode != TenancyMode.Single)
            return;
        
        var t = opt.SingleTenant;
        
        var exists = await db.Tenants.AnyAsync(x => x.Id == t.TenantId, cancellationToken);
        if (!exists)
        {
            db.Tenants.Add(new Tenant
            {
                Id = t.TenantId,
                Name = t.Name,
                Slug = t.Slug
            });
            
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}