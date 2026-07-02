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

using BinStash.Server.Endpoints;

namespace BinStash.Server.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static void MapAllEndpoints(this IEndpointRouteBuilder app)
    {
        //app.MapIdentityApi<BinStashUser>();
        app.MapIdentityEndpoints();
        app.MapIngestSessionEndpoints();
        app.MapInstanceEndpoints();
        app.MapReleaseEndpoints();
        app.MapSetupEndpoints();
        // The following REST surfaces were migrated to GraphQL and removed:
        //  - Tenant list + members/invitations/roles/leave/delete (query tenants / tenantMembers / tenantInvitationPreview, mutation invite/updateRoles/remove/leave/accept/deleteTenant).
        //    The tenants query handles service-account (machine) tokens, so the CLI uses it too.
        //  - Service-account API keys (mutation createServiceAccountApiKey / deleteServiceAccountApiKey, query serviceAccountApiKeys)
        //  - Storage classes + default mappings (query storageClasses / storageClassDefaultMappings, mutation setStorageClassDefaultMappings)
        //  - Instance stats + email/tenancy/domain config, tenant members/invitations, repo config/access,
        //    chunk-store list/detail/stats/types/create, background-job rebuild/upgrade, and repositories
        //    list/detail/create (query repositories/repository, mutation createRepository).
        //    ChunkStoreEndpoints.ListChunkStoresAsync/CreateChunkStoreAsync are kept as plain methods,
        //    reused directly by the setup wizard (SetupEndpoints) which runs before GraphQL auth exists.
        // REST is retained only where it is not GraphQL-shaped: auth (cookies), setup (bootstrap),
        // ingest (binary/gRPC), release download (binary), health, and the CLI tenant list.
    }
}