using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLink.Lite.Application.RealtimeAudio;
using TransLink.Lite.IntegrationTests.Infrastructure;
using TransLink.Lite.Infrastructure.Persistence;

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
        await SendStartAsync(socket, "EN-us");

        using var accepted = await ReceiveControlAsync(socket);
        Assert.Equal("session.accepted", accepted.RootElement.GetProperty("type").GetString());
        Assert.Equal(3, accepted.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("en-US", accepted.RootElement.GetProperty("sourceLanguage").GetString());

        await socket.SendAsync(CreateFrame(0, 0), WebSocketMessageType.Binary, true, default);
        using var partial = await ReceiveControlAsync(socket);
        using var final = await ReceiveControlAsync(socket);
        using var translation = await ReceiveControlAsync(socket);
        Assert.Equal("transcript.partial", partial.RootElement.GetProperty("type").GetString());
        Assert.False(partial.RootElement.GetProperty("isFinal").GetBoolean());
        Assert.Equal("transcript.final", final.RootElement.GetProperty("type").GetString());
        Assert.True(final.RootElement.GetProperty("isFinal").GetBoolean());
        Assert.Equal("translation.final", translation.RootElement.GetProperty("type").GetString());
        Assert.Equal("test-final", translation.RootElement.GetProperty("sourceResultId").GetString());
        Assert.Equal("es", translation.RootElement.GetProperty("targetLanguage").GetString());
        await socket.SendAsync(CreateFrame(1, 150), WebSocketMessageType.Binary, true, default);
        await socket.SendAsync(
            Encoding.UTF8.GetBytes("""{"type":"session.stop","protocolVersion":3}"""),
            WebSocketMessageType.Text,
            true,
            default);

        using var stopped = await ReceiveControlAsync(socket);
        Assert.Equal("session.stopped", stopped.RootElement.GetProperty("type").GetString());
        var close = await socket.ReceiveAsync(new byte[1], default);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.CloseStatus);
        socket.Dispose();
        await WaitUntilAsync(() =>
            _fixture.Factory.RealtimeTranscription.Completed == 1 &&
            _fixture.Factory.RealtimeTranscription.Disposed == 1);

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await dbContext.TranslationSessions.CountAsync());
    }

    [Fact]
    public async Task Start_WithUnsupportedSourceLanguage_IsRejectedBeforeProviderStarts()
    {
        await _fixture.ResetDatabaseAsync();
        using var socket = await ConnectAuthenticatedAsync();
        await SendStartAsync(socket, "fr-FR");

        using var rejected = await ReceiveControlAsync(socket);
        Assert.Equal("session.rejected", rejected.RootElement.GetProperty("type").GetString());
        Assert.Equal("unsupported-audio-format", rejected.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, _fixture.Factory.RealtimeTranscription.Created);
    }

    [Fact]
    public async Task Start_WithUnsupportedTargetLanguage_IsRejectedBeforeProvidersStart()
    {
        await _fixture.ResetDatabaseAsync();
        using var socket = await ConnectAuthenticatedAsync();
        await SendStartAsync(socket, targetLanguage: "unsupported");

        using var rejected = await ReceiveControlAsync(socket);
        Assert.Equal("session.rejected", rejected.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "unsupported-translation-language",
            rejected.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, _fixture.Factory.RealtimeTranscription.Created);
        Assert.Equal(0, _fixture.Factory.RealtimeTranslation.Requests);
    }

    [Fact]
    public async Task TranslationFailure_ReturnsSafeError_WithoutClosingTranscription()
    {
        await _fixture.ResetDatabaseAsync();
        _fixture.Factory.RealtimeTranslation.Fail = true;
        using var socket = await ConnectAuthenticatedAsync();
        await SendStartAsync(socket);
        using var accepted = await ReceiveControlAsync(socket);

        await socket.SendAsync(CreateFrame(0, 0), WebSocketMessageType.Binary, true, default);
        using var partial = await ReceiveControlAsync(socket);
        using var final = await ReceiveControlAsync(socket);
        using var error = await ReceiveControlAsync(socket);

        Assert.Equal("transcript.partial", partial.RootElement.GetProperty("type").GetString());
        Assert.Equal("transcript.final", final.RootElement.GetProperty("type").GetString());
        Assert.Equal("translation.error", error.RootElement.GetProperty("type").GetString());
        Assert.Equal("translation-connection", error.RootElement.GetProperty("code").GetString());
        Assert.Equal(WebSocketState.Open, socket.State);
        Assert.Equal(1, _fixture.Factory.RealtimeTranslation.Requests);
    }

    [Fact]
    public async Task Stream_WhenProviderFails_ReturnsSafeTranscriptionError()
    {
        await _fixture.ResetDatabaseAsync();
        _fixture.Factory.RealtimeTranscription.FailOnAudio = true;
        using var socket = await ConnectAuthenticatedAsync();
        await SendStartAsync(socket);
        using var accepted = await ReceiveControlAsync(socket);

        await socket.SendAsync(CreateFrame(0, 0), WebSocketMessageType.Binary, true, default);

        using var error = await ReceiveControlAsync(socket);
        Assert.Equal("transcription.error", error.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "transcription-provider-unavailable",
            error.RootElement.GetProperty("code").GetString());
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
        await WaitUntilAsync(() => _fixture.Factory.RealtimeTranscription.Disposed == 1);
    }

    [Fact]
    public async Task Observer_SameOwner_ReceivesOrderedFinalEvents_AndProducerStopEndsSession()
    {
        await _fixture.ResetDatabaseAsync();
        using var httpClient = _fixture.Factory.CreateClient();
        var auth = await ApiTestClient.RegisterAsync(httpClient);
        using var producer = await ConnectAuthenticatedAsync(auth.AccessToken);
        await SendStartAsync(producer);
        using var accepted = await ReceiveControlAsync(producer);
        var sessionId = accepted.RootElement.GetProperty("sessionId").GetGuid();

        using var activeRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/realtime/sessions/active");
        activeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", auth.AccessToken);
        using var activeResponse = await httpClient.SendAsync(activeRequest);
        Assert.Equal(System.Net.HttpStatusCode.OK, activeResponse.StatusCode);
        var activeJson = await activeResponse.Content.ReadAsStringAsync();
        Assert.Contains(sessionId.ToString(), activeJson, StringComparison.OrdinalIgnoreCase);

        using var observer = await ConnectObserverAsync(auth.AccessToken, sessionId);
        using var observerAccepted = await ReceiveControlAsync(observer);
        Assert.Equal("observer.accepted", observerAccepted.RootElement.GetProperty("type").GetString());

        await producer.SendAsync(CreateFrame(0, 0), WebSocketMessageType.Binary, true, default);
        using var producerPartial = await ReceiveControlAsync(producer);
        using var producerFinal = await ReceiveControlAsync(producer);
        using var producerTranslation = await ReceiveControlAsync(producer);
        using var observedTranscript = await ReceiveControlAsync(observer);
        using var observedTranslation = await ReceiveControlAsync(observer);
        Assert.Equal("transcript.final", observedTranscript.RootElement.GetProperty("type").GetString());
        Assert.Equal("translation.final", observedTranslation.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            observedTranscript.RootElement.GetProperty("eventSequence").GetInt64(),
            observedTranslation.RootElement.GetProperty("eventSequence").GetInt64());

        await producer.SendAsync(
            Encoding.UTF8.GetBytes("""{"type":"session.stop","protocolVersion":3}"""),
            WebSocketMessageType.Text, true, default);
        using var producerStopped = await ReceiveControlAsync(producer);
        var producerClose = await producer.ReceiveAsync(new byte[1], default);
        Assert.Equal(WebSocketMessageType.Close, producerClose.MessageType);
        await producer.CloseOutputAsync(
            WebSocketCloseStatus.NormalClosure, "session-stopped", default);
        using var observerClosed = await ReceiveControlAsync(observer);
        Assert.Equal("session.closed", observerClosed.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Observer_ForeignOwner_GetsSameSafeRejectionAsUnknownSession()
    {
        await _fixture.ResetDatabaseAsync();
        using var httpClient = _fixture.Factory.CreateClient();
        var owner = await ApiTestClient.RegisterAsync(httpClient);
        var foreign = await ApiTestClient.RegisterAsync(httpClient);
        using var producer = await ConnectAuthenticatedAsync(owner.AccessToken);
        await SendStartAsync(producer);
        using var accepted = await ReceiveControlAsync(producer);
        var sessionId = accepted.RootElement.GetProperty("sessionId").GetGuid();

        using var observer = await ConnectObserverAsync(foreign.AccessToken, sessionId);
        using var rejected = await ReceiveControlAsync(observer);
        using var unknownObserver = await ConnectObserverAsync(foreign.AccessToken, Guid.NewGuid());
        using var unknownRejected = await ReceiveControlAsync(unknownObserver);
        Assert.Equal("observer.rejected", rejected.RootElement.GetProperty("type").GetString());
        Assert.Equal("session-not-found", rejected.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            rejected.RootElement.GetProperty("code").GetString(),
            unknownRejected.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TwoObservers_ReceiveSameEvents_AndOneDisconnectIsIsolated()
    {
        await _fixture.ResetDatabaseAsync();
        using var httpClient = _fixture.Factory.CreateClient();
        var auth = await ApiTestClient.RegisterAsync(httpClient);
        using var producer = await ConnectAuthenticatedAsync(auth.AccessToken);
        await SendStartAsync(producer);
        using var accepted = await ReceiveControlAsync(producer);
        var sessionId = accepted.RootElement.GetProperty("sessionId").GetGuid();
        using var observerA = await ConnectObserverAsync(auth.AccessToken, sessionId);
        using var observerB = await ConnectObserverAsync(auth.AccessToken, sessionId);
        using var acceptedA = await ReceiveControlAsync(observerA);
        using var acceptedB = await ReceiveControlAsync(observerB);

        await observerA.CloseAsync(
            WebSocketCloseStatus.NormalClosure, "observer-disconnect", default);
        await producer.SendAsync(CreateFrame(0, 0), WebSocketMessageType.Binary, true, default);
        using var producerPartial = await ReceiveControlAsync(producer);
        using var producerFinal = await ReceiveControlAsync(producer);
        using var producerTranslation = await ReceiveControlAsync(producer);
        using var observedTranscript = await ReceiveControlAsync(observerB);
        using var observedTranslation = await ReceiveControlAsync(observerB);

        Assert.Equal("transcript.final", observedTranscript.RootElement.GetProperty("type").GetString());
        Assert.Equal("translation.final", observedTranslation.RootElement.GetProperty("type").GetString());
        Assert.Equal(WebSocketState.Open, producer.State);
        Assert.Equal(WebSocketState.Open, observerB.State);
    }

    [Fact]
    public async Task Observer_SendingBinaryAudio_IsClosedWithoutStoppingProducer()
    {
        await _fixture.ResetDatabaseAsync();
        using var httpClient = _fixture.Factory.CreateClient();
        var auth = await ApiTestClient.RegisterAsync(httpClient);
        using var producer = await ConnectAuthenticatedAsync(auth.AccessToken);
        await SendStartAsync(producer);
        using var accepted = await ReceiveControlAsync(producer);
        var sessionId = accepted.RootElement.GetProperty("sessionId").GetGuid();
        using var observer = await ConnectObserverAsync(auth.AccessToken, sessionId);
        using var observerAccepted = await ReceiveControlAsync(observer);

        await observer.SendAsync(new byte[] { 1 }, WebSocketMessageType.Binary, true, default);
        var close = await observer.ReceiveAsync(new byte[1], default);

        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, observer.CloseStatus);
        Assert.Equal(WebSocketState.Open, producer.State);
    }

    [Fact]
    public async Task SpeechEnabledObserver_ReceivesCorrelatedBinaryAudio()
    {
        await _fixture.ResetDatabaseAsync();
        using var httpClient = _fixture.Factory.CreateClient();
        var auth = await ApiTestClient.RegisterAsync(httpClient);
        using var producer = await ConnectAuthenticatedAsync(auth.AccessToken);
        await SendStartAsync(producer);
        using var accepted = await ReceiveControlAsync(producer);
        var sessionId = accepted.RootElement.GetProperty("sessionId").GetGuid();
        using var observer = await ConnectObserverAsync(auth.AccessToken, sessionId, speechEnabled: true);
        using var observerAccepted = await ReceiveControlAsync(observer);

        await producer.SendAsync(CreateFrame(0, 0), WebSocketMessageType.Binary, true, default);
        using var producerPartial = await ReceiveControlAsync(producer);
        using var producerFinal = await ReceiveControlAsync(producer);
        using var producerTranslation = await ReceiveControlAsync(producer);
        using var observedTranscript = await ReceiveControlAsync(observer);
        using var observedTranslation = await ReceiveControlAsync(observer);
        using var synthesizing = await ReceiveControlAsync(observer);
        using var speech = await ReceiveControlAsync(observer);
        var audioBuffer = new byte[32];
        var audio = await observer.ReceiveAsync(audioBuffer, default);

        Assert.Equal("speech.synthesizing", synthesizing.RootElement.GetProperty("type").GetString());
        Assert.Equal("speech.segment", speech.RootElement.GetProperty("type").GetString());
        Assert.Equal("test-final", speech.RootElement.GetProperty("sourceResultId").GetString());
        Assert.Equal("mp3", speech.RootElement.GetProperty("audioFormat").GetString());
        Assert.Equal(WebSocketMessageType.Binary, audio.MessageType);
        Assert.Equal(4, audio.Count);
        Assert.Equal(1, _fixture.Factory.RealtimeSpeech.Requests);
        Assert.Equal(WebSocketState.Open, producer.State);
    }

    [Fact]
    public async Task SpeechFailure_DoesNotStopTextOrProducer()
    {
        await _fixture.ResetDatabaseAsync();
        _fixture.Factory.RealtimeSpeech.Fail = true;
        using var httpClient = _fixture.Factory.CreateClient();
        var auth = await ApiTestClient.RegisterAsync(httpClient);
        using var producer = await ConnectAuthenticatedAsync(auth.AccessToken);
        await SendStartAsync(producer);
        using var accepted = await ReceiveControlAsync(producer);
        var sessionId = accepted.RootElement.GetProperty("sessionId").GetGuid();
        using var observer = await ConnectObserverAsync(auth.AccessToken, sessionId, speechEnabled: true);
        using var observerAccepted = await ReceiveControlAsync(observer);

        await producer.SendAsync(CreateFrame(0, 0), WebSocketMessageType.Binary, true, default);
        using var producerPartial = await ReceiveControlAsync(producer);
        using var producerFinal = await ReceiveControlAsync(producer);
        using var producerTranslation = await ReceiveControlAsync(producer);
        using var observedTranscript = await ReceiveControlAsync(observer);
        using var observedTranslation = await ReceiveControlAsync(observer);
        using var synthesizing = await ReceiveControlAsync(observer);
        using var speechError = await ReceiveControlAsync(observer);

        Assert.Equal("translation.final", observedTranslation.RootElement.GetProperty("type").GetString());
        Assert.Equal("speech.error", speechError.RootElement.GetProperty("type").GetString());
        Assert.Equal("speech-unavailable", speechError.RootElement.GetProperty("code").GetString());
        Assert.Equal(WebSocketState.Open, producer.State);
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

    private async Task<WebSocket> ConnectAuthenticatedAsync(string accessToken)
    {
        var webSocketClient = _fixture.Factory.Server.CreateWebSocketClient();
        webSocketClient.SubProtocols.Add(RealtimeAudioProtocol.WebSocketSubprotocol);
        webSocketClient.SubProtocols.Add(
            $"{RealtimeAudioProtocol.BearerSubprotocolPrefix}{accessToken}");
        return await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/api/realtime/audio"), CancellationToken.None);
    }

    private async Task<WebSocket> ConnectObserverAsync(string accessToken, Guid sessionId, bool speechEnabled = false)
    {
        var webSocketClient = _fixture.Factory.Server.CreateWebSocketClient();
        webSocketClient.SubProtocols.Add(RealtimeAudioProtocol.WebSocketSubprotocol);
        webSocketClient.SubProtocols.Add(
            $"{RealtimeAudioProtocol.BearerSubprotocolPrefix}{accessToken}");
        var socket = await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/api/realtime/sessions/observe"), CancellationToken.None);
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "observer.subscribe",
            protocolVersion = 3,
            sessionId,
            speechEnabled,
        }), WebSocketMessageType.Text, true, default);
        return socket;
    }

    private static Task SendStartAsync(
        WebSocket socket,
        string sourceLanguage = "en-US",
        string targetLanguage = "es") =>
        socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "session.start",
                protocolVersion = 3,
                targetLanguage,
                audio = new
                {
                    encoding = "pcm_s16le",
                    sampleRateHz = 48_000,
                    channelCount = 1,
                    chunkDurationMs = 150,
                    sourceLanguage,
                },
            }),
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition());
    }
}
