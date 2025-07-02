using BinStash.Server.Endpoints;

namespace BinStash.Server.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static void MapAllEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapChunkStoreEndpoints();
        app.MapRepositoryEndpoints();
        app.MapReleaseEndpoints();
    }
}