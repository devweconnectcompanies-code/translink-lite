using System.Net.Http.Headers;
using System.Net.Http.Json;
using TransLink.Lite.Web.Models;

namespace TransLink.Lite.Web.Services;

public sealed class ActiveSessionClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<ActiveRealtimeSession>> GetActiveAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/realtime/sessions/active");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ActiveRealtimeSession[]>(cancellationToken: cancellationToken) ?? [];
    }
}
