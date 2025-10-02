using BinStash.Server.Endpoints;
using Microsoft.AspNetCore.Identity;

namespace BinStash.Server.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static void MapAllEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/auth")
            .WithTags("Authentication")
            .MapIdentityApi<IdentityUser<Guid>>();
        app.MapChunkStoreEndpoints();
        app.MapIngestSessionEndpoints();
        app.MapRepositoryEndpoints();
        app.MapReleaseEndpoints();
    }
}