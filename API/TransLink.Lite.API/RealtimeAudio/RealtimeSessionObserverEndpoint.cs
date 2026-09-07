using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.API.RealtimeAudio;

public static class RealtimeSessionObserverEndpoint
{
    public const string ObserverPath = "/api/realtime/sessions/observe";
    public const string ActiveSessionsPath = "/api/realtime/sessions/active";

    public static IEndpointRouteBuilder MapRealtimeSessionObservers(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ActiveSessionsPath, (
                ClaimsPrincipal user,
                IRealtimeSessionRegistry registry) =>
            TryGetUserId(user, out var userId)
                ? Results.Ok(registry.GetActiveSessions(userId))
                : Results.Unauthorized())
            .RequireAuthorization();

        endpoints.Map(ObserverPath, HandleObserverAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task HandleObserverAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest ||
            !context.WebSockets.WebSocketRequestedProtocols.Contains(
                RealtimeAudioProtocol.WebSocketSubprotocol,
                StringComparer.Ordinal) ||
            !TryGetUserId(context.User, out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var options = context.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<
                Configuration.RealtimeAudioOptions>>().Value;
        var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (!environment.IsDevelopment() &&
            !environment.IsEnvironment("IntegrationTesting") &&
            !context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (options.AllowedOrigins.Length > 0 &&
            context.Request.Headers.Origin is { Count: > 0 } origin &&
            !options.AllowedOrigins.Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync(
            RealtimeAudioProtocol.WebSocketSubprotocol);
        var buffer = new byte[options.MaxControlMessageBytes + 1];
        var received = await socket.ReceiveAsync(buffer, context.RequestAborted);
        if (received.MessageType != WebSocketMessageType.Text ||
            !received.EndOfMessage || received.Count > options.MaxControlMessageBytes ||
            !TryParseSubscription(buffer.AsSpan(0, received.Count), out var sessionId, out var speechEnabled))
        {
            await RejectAsync(socket, "invalid-observer-handshake", context.RequestAborted);
            return;
        }

        var registry = context.RequestServices.GetRequiredService<IRealtimeSessionRegistry>();
        await using var subscription = registry.Subscribe(sessionId, userId);
        if (subscription is null)
        {
            await RejectAsync(socket, "session-not-found", context.RequestAborted);
            return;
        }

        var speechCoordinator = context.RequestServices
            .GetRequiredService<IRealtimeSpeechSynthesisCoordinator>();
        var initialSpeechLease = speechEnabled
            ? speechCoordinator.AcquireConsumer(sessionId)
            : null;
        await SendAsync(socket, new
        {
            type = "observer.accepted",
            protocolVersion = RealtimeAudioProtocol.CurrentVersion,
            sessionId,
        }, context.RequestAborted);

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var pump = PumpEventsAsync(socket, subscription, lifetime.Token);
        var monitor = MonitorClientAsync(
            socket, sessionId, initialSpeechLease, speechCoordinator,
            options.MaxControlMessageBytes, lifetime.Token);
        await Task.WhenAny(pump, monitor);
        await lifetime.CancelAsync();
        try { await Task.WhenAll(pump, monitor); }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (IOException) { }
        catch (ChannelClosedException)
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.PolicyViolation,
                        "observer-backpressure",
                        CancellationToken.None);
                }
                catch (WebSocketException) { }
            }
        }
    }

    private static bool TryParseSubscription(
        ReadOnlySpan<byte> json,
        out Guid sessionId,
        out bool speechEnabled)
    {
        sessionId = default;
        speechEnabled = false;
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            var valid = root.TryGetProperty("type", out var type) &&
                type.GetString() == "observer.subscribe" &&
                root.TryGetProperty("protocolVersion", out var version) &&
                version.GetInt32() == RealtimeAudioProtocol.CurrentVersion &&
                root.TryGetProperty("sessionId", out var id) &&
                Guid.TryParse(id.GetString(), out sessionId);
            if (valid && root.TryGetProperty("speechEnabled", out var speech) &&
                speech.ValueKind is JsonValueKind.True or JsonValueKind.False)
                speechEnabled = speech.GetBoolean();
            return valid;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task PumpEventsAsync(
        WebSocket socket,
        IRealtimeSessionSubscription subscription,
        CancellationToken cancellationToken)
    {
        await foreach (var message in subscription.Events.ReadAllAsync(cancellationToken))
        {
            await SendAsync(socket, message, cancellationToken);
            if (message.Type == "speech.segment" && message.AudioPayload is { Length: > 0 } audio)
                await socket.SendAsync(audio, WebSocketMessageType.Binary, true, cancellationToken);
            if (message.Type == "session.closed")
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "session-ended",
                    cancellationToken);
                return;
            }
        }
    }

    private static async Task MonitorClientAsync(
        WebSocket socket,
        Guid sessionId,
        IRealtimeSpeechConsumerLease? initialSpeechLease,
        IRealtimeSpeechSynthesisCoordinator speechCoordinator,
        int maximumControlBytes,
        CancellationToken cancellationToken)
    {
        IRealtimeSpeechConsumerLease? speechLease = initialSpeechLease;
        var buffer = new byte[maximumControlBytes + 1];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "observer-disconnect",
                        cancellationToken);
                    return;
                }
                if (result.MessageType != WebSocketMessageType.Text ||
                    !result.EndOfMessage || result.Count > maximumControlBytes ||
                    !TryParseSpeechControl(buffer.AsSpan(0, result.Count), out var enabled))
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.PolicyViolation,
                        "observer-read-only",
                        cancellationToken);
                    return;
                }
                if (enabled && speechLease is null)
                    speechLease = speechCoordinator.AcquireConsumer(sessionId);
                else if (!enabled && speechLease is not null)
                {
                    await speechLease.DisposeAsync();
                    speechLease = null;
                }
                await SendAsync(socket, new
                {
                    type = "speech.state",
                    protocolVersion = RealtimeAudioProtocol.CurrentVersion,
                    sessionId,
                    enabled = speechLease is not null,
                }, cancellationToken);
            }
        }
        finally
        {
            if (speechLease is not null) await speechLease.DisposeAsync();
        }
    }

    private static bool TryParseSpeechControl(ReadOnlySpan<byte> json, out bool enabled)
    {
        enabled = false;
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) ||
                type.GetString() != "observer.speech" ||
                !root.TryGetProperty("protocolVersion", out var version) ||
                version.GetInt32() != RealtimeAudioProtocol.CurrentVersion ||
                !root.TryGetProperty("enabled", out var value) ||
                value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            enabled = value.GetBoolean();
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static Task RejectAsync(
        WebSocket socket,
        string code,
        CancellationToken cancellationToken) =>
        SendThenCloseAsync(socket, code, cancellationToken);

    private static async Task SendThenCloseAsync(
        WebSocket socket,
        string code,
        CancellationToken cancellationToken)
    {
        await SendAsync(socket, new
        {
            type = "observer.rejected",
            protocolVersion = RealtimeAudioProtocol.CurrentVersion,
            code,
        }, cancellationToken);
        await socket.CloseAsync(
            WebSocketCloseStatus.PolicyViolation, code, cancellationToken);
    }

    private static Task SendAsync(
        WebSocket socket,
        object message,
        CancellationToken cancellationToken) =>
        socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web),
            WebSocketMessageType.Text,
            true,
            cancellationToken);

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
