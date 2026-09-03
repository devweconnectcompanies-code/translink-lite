using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TransLink.Lite.Application.RealtimeAudio;
using TransLink.Lite.IntegrationTests.Infrastructure;

namespace TransLink.Lite.IntegrationTests.RealtimeAudio;

[Collection(IntegrationTestCollection.Name)]
public sealed class RealtimeAudioWebSocketTests
{
    private const string ChromeExtensionOrigin =
        "chrome-extension://bfaojhccmpbmgepomionfobjmclhkmod";
    private readonly PostgreSqlApiFixture _fixture;

    public RealtimeAudioWebSocketTests(PostgreSqlApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Connect_WithoutJwt_IsRejectedBeforeUpgrade()
    {
        var webSocketClient = _fixture.Factory.Server.CreateWebSocketClient();
        webSocketClient.SubProtocols.Add(RealtimeAudioProtocol.WebSocketSubprotocol);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            webSocketClient.ConnectAsync(
                new Uri("ws://localhost/api/realtime/audio"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Stream_WithValidStartAndSequentialFrames_StopsCleanly()
    {
        await _fixture.ResetDatabaseAsync();
        using var socket = await ConnectAuthenticatedAsync();
        await SendStartAsync(socket);

        using var accepted = await ReceiveControlAsync(socket);
        Assert.Equal("session.accepted", accepted.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, accepted.RootElement.GetProperty("protocolVersion").GetInt32());

        await socket.SendAsync(CreateFrame(0, 0), WebSocketMessageType.Binary, true, default);
        await socket.SendAsync(CreateFrame(1, 150), WebSocketMessageType.Binary, true, default);
        await socket.SendAsync(
            Encoding.UTF8.GetBytes("""{"type":"session.stop","protocolVersion":1}"""),
            WebSocketMessageType.Text,
            true,
            default);

        using var stopped = await ReceiveControlAsync(socket);
        Assert.Equal("session.stopped", stopped.RootElement.GetProperty("type").GetString());
        var close = await socket.ReceiveAsync(new byte[1], default);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.CloseStatus);
    }

    [Fact]
    public async Task Connect_FromChromeExtensionOrigin_WithExactAllowlist_IsAccepted()
    {
        await _fixture.ResetDatabaseAsync();
        await using var factory = _fixture.CreateFactory(
            realtimeAllowedOrigin: ChromeExtensionOrigin);
        using var httpClient = factory.CreateClient();
        var auth = await ApiTestClient.RegisterAsync(httpClient);
        var webSocketClient = factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
            request.Headers["Origin"] = ChromeExtensionOrigin;
        webSocketClient.SubProtocols.Add(RealtimeAudioProtocol.WebSocketSubprotocol);
        webSocketClient.SubProtocols.Add(
            $"{RealtimeAudioProtocol.BearerSubprotocolPrefix}{auth.AccessToken}");

        using var socket = await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/api/realtime/audio"),
            CancellationToken.None);
        await SendStartAsync(socket);
        using var accepted = await ReceiveControlAsync(socket);

        Assert.Equal("session.accepted", accepted.RootElement.GetProperty("type").GetString());
        Assert.Equal(RealtimeAudioProtocol.WebSocketSubprotocol, socket.SubProtocol);
    }

    [Fact]
    public async Task Connect_FromUnlistedChromeExtensionOrigin_IsRejectedBeforeUpgrade()
    {
        await _fixture.ResetDatabaseAsync();
        await using var factory = _fixture.CreateFactory(
            realtimeAllowedOrigin: ChromeExtensionOrigin);
        using var httpClient = factory.CreateClient();
        var auth = await ApiTestClient.RegisterAsync(httpClient);
        var webSocketClient = factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
            request.Headers["Origin"] = "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        webSocketClient.SubProtocols.Add(RealtimeAudioProtocol.WebSocketSubprotocol);
        webSocketClient.SubProtocols.Add(
            $"{RealtimeAudioProtocol.BearerSubprotocolPrefix}{auth.AccessToken}");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            webSocketClient.ConnectAsync(
                new Uri("ws://localhost/api/realtime/audio"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Stream_WithMalformedBinaryFrame_IsRejectedSafely()
    {
        await _fixture.ResetDatabaseAsync();
        using var socket = await ConnectAuthenticatedAsync();
        await SendStartAsync(socket);
        using var accepted = await ReceiveControlAsync(socket);

        await socket.SendAsync(
            new byte[RealtimeAudioProtocol.BinaryHeaderLength - 1],
            WebSocketMessageType.Binary,
            true,
            default);

        using var error = await ReceiveControlAsync(socket);
        Assert.Equal("transport.error", error.RootElement.GetProperty("type").GetString());
        Assert.Equal("frame-too-small", error.RootElement.GetProperty("code").GetString());
        var close = await socket.ReceiveAsync(new byte[1], default);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, socket.CloseStatus);
    }

    [Fact]
    public async Task Stream_WithSequenceGap_IsRejectedSafely()
    {
        await _fixture.ResetDatabaseAsync();
        using var socket = await ConnectAuthenticatedAsync();
        await SendStartAsync(socket);
        using var accepted = await ReceiveControlAsync(socket);

        await socket.SendAsync(CreateFrame(1, 150), WebSocketMessageType.Binary, true, default);

        using var error = await ReceiveControlAsync(socket);
        Assert.Equal("invalid-sequence", error.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Stream_WithOversizedFrame_ClosesWithMessageTooBig()
    {
        await _fixture.ResetDatabaseAsync();
        using var socket = await ConnectAuthenticatedAsync();
        await SendStartAsync(socket);
        using var accepted = await ReceiveControlAsync(socket);

        await socket.SendAsync(
            new byte[65_561],
            WebSocketMessageType.Binary,
            true,
            default);

        var close = await socket.ReceiveAsync(new byte[1], default);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, socket.CloseStatus);
    }

    [Fact]
    public async Task Disconnect_AfterAcceptedSession_ReleasesConnection()
    {
        await _fixture.ResetDatabaseAsync();
        using var socket = await ConnectAuthenticatedAsync();
        await SendStartAsync(socket);
        using var accepted = await ReceiveControlAsync(socket);

        await socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "client-disconnect",
            default);

        Assert.Equal(WebSocketState.Closed, socket.State);
    }

    private async Task<WebSocket> ConnectAuthenticatedAsync()
    {
        using var httpClient = _fixture.Factory.CreateClient();
        var auth = await ApiTestClient.RegisterAsync(httpClient);
        var webSocketClient = _fixture.Factory.Server.CreateWebSocketClient();
        webSocketClient.SubProtocols.Add(RealtimeAudioProtocol.WebSocketSubprotocol);
        webSocketClient.SubProtocols.Add(
            $"{RealtimeAudioProtocol.BearerSubprotocolPrefix}{auth.AccessToken}");
        return await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/api/realtime/audio"),
            CancellationToken.None);
    }

    private static Task SendStartAsync(WebSocket socket) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(
                """{"type":"session.start","protocolVersion":1,"audio":{"encoding":"pcm_s16le","sampleRateHz":48000,"channelCount":1,"chunkDurationMs":150}}"""),
            WebSocketMessageType.Text,
            true,
            default);

    private static async Task<JsonDocument> ReceiveControlAsync(WebSocket socket)
    {
        var buffer = new byte[4_096];
        var result = await socket.ReceiveAsync(buffer, default);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.True(result.EndOfMessage);
        return JsonDocument.Parse(buffer.AsMemory(0, result.Count));
    }

    private static byte[] CreateFrame(ulong sequence, ulong elapsedMilliseconds)
    {
        const int payloadLength = 48_000 * 150 / 1_000 * sizeof(short);
        var frame = new byte[RealtimeAudioProtocol.BinaryHeaderLength + payloadLength];
        frame[0] = RealtimeAudioProtocol.MagicFirstByte;
        frame[1] = RealtimeAudioProtocol.MagicSecondByte;
        frame[2] = RealtimeAudioProtocol.CurrentVersion;
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(4, 8), sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(12, 8), elapsedMilliseconds);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(20, 4), payloadLength);
        return frame;
    }
}
