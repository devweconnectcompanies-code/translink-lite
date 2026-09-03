using Microsoft.AspNetCore.Authorization;

namespace TransLink.Lite.API.RealtimeAudio;

public static class RealtimeAudioEndpoint
{
    public const string Path = "/api/realtime/audio";

    public static IEndpointRouteBuilder MapRealtimeAudio(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, async (
                HttpContext context,
                RealtimeAudioConnectionHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(context, cancellationToken);
            })
            .RequireAuthorization(new AuthorizeAttribute());

        return endpoints;
    }
}
