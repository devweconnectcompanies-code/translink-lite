using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TransLink.Lite.Web.Models;

namespace TransLink.Lite.Web.Services;

public sealed class RealtimeObserverClient(HttpClient httpClient) : IAsyncDisposable
{
    public const int ProtocolVersion = 3;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifetime;
    private Task? _receiveTask;

    public async Task ConnectAsync(Guid sessionId, string accessToken, bool speechEnabled, Func<RealtimeObserverEvent, Task> onEvent, CancellationToken cancellationToken)
    {
        await DisconnectAsync();
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _socket = new ClientWebSocket();
        _socket.Options.AddSubProtocol("translink.realtime.v3");
        _socket.Options.AddSubProtocol($"translink.bearer.{accessToken}");
        var api = httpClient.BaseAddress ?? throw new InvalidOperationException("API base URL is unavailable.");
        var uri = new UriBuilder(api) { Scheme = api.Scheme == "https" ? "wss" : "ws", Path = "/api/realtime/sessions/observe", Query = "" }.Uri;
        await _socket.ConnectAsync(uri, _lifetime.Token);
        await SendAsync(new { type = "observer.subscribe", protocolVersion = ProtocolVersion, sessionId, speechEnabled }, _lifetime.Token);
        _receiveTask = ReceiveAsync(onEvent, _lifetime.Token);
    }

    public Task SetSpeechEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
        SendAsync(new { type = "observer.speech", protocolVersion = ProtocolVersion, enabled }, cancellationToken);

    public async Task DisconnectAsync()
    {
        if (_lifetime is not null) await _lifetime.CancelAsync();
        if (_receiveTask is not null)
        {
            await _receiveTask;
            _receiveTask = null;
        }
        if (_socket?.State is WebSocketState.Open)
        {
            try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "observer-disconnect", CancellationToken.None); }
            catch (WebSocketException) { }
        }
        _socket?.Dispose(); _socket = null; _lifetime?.Dispose(); _lifetime = null;
    }

    private async Task ReceiveAsync(Func<RealtimeObserverEvent, Task> onEvent, CancellationToken cancellationToken)
    {
        var buffer = new byte[64_000];
        RealtimeObserverEvent? pendingSpeech = null;
        try
        {
            while (_socket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var messageBuffer = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.Count > 0) messageBuffer.Write(buffer, 0, result.Count);
                    if (messageBuffer.Length > 1_048_576)
                        throw new WebSocketException("observer-message-limit");
                } while (!result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    if (pendingSpeech is not null && pendingSpeech.AudioByteLength == messageBuffer.Length)
                    {
                        await onEvent(pendingSpeech with { AudioPayload = messageBuffer.ToArray() });
                        pendingSpeech = null;
                    }
                    continue;
                }
                if (result.MessageType != WebSocketMessageType.Text) continue;
                var message = JsonSerializer.Deserialize<RealtimeObserverEvent>(
                    messageBuffer.ToArray(), JsonSerializerOptions.Web);
                if (message is null || message.ProtocolVersion != ProtocolVersion) continue;
                if (message.Type == "speech.segment") pendingSpeech = message;
                else await onEvent(message);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private Task SendAsync(object message, CancellationToken cancellationToken) =>
        _socket!.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonSerializerOptions.Web)), WebSocketMessageType.Text, true, cancellationToken);

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
