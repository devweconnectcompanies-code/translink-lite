using System.Buffers;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TransLink.Lite.API.Configuration;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.API.RealtimeAudio;

public sealed class RealtimeAudioConnectionHandler
{
    private readonly RealtimeAudioOptions _options;
    private readonly IRealtimeAudioSessionFactory _sessionFactory;
    private readonly ILogger<RealtimeAudioConnectionHandler> _logger;
    private readonly IWebHostEnvironment _environment;

    public RealtimeAudioConnectionHandler(
        IOptions<RealtimeAudioOptions> options,
        IRealtimeAudioSessionFactory sessionFactory,
        ILogger<RealtimeAudioConnectionHandler> logger,
        IWebHostEnvironment environment)
    {
        _options = options.Value;
        _sessionFactory = sessionFactory;
        _logger = logger;
        _environment = environment;
    }

    public async Task HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Realtime audio handler reached. OriginPresent: {OriginPresent}",
            context.Request.Headers.Origin.Count > 0);

        if (!context.WebSockets.IsWebSocketRequest)
        {
            _logger.LogWarning("Realtime audio request rejected: WebSocket upgrade required.");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!context.WebSockets.WebSocketRequestedProtocols.Contains(
                RealtimeAudioProtocol.WebSocketSubprotocol,
                StringComparer.Ordinal))
        {
            _logger.LogWarning("Realtime audio request rejected: public protocol missing.");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!_environment.IsDevelopment() &&
            !_environment.IsEnvironment("IntegrationTesting") &&
            !context.Request.IsHttps)
        {
            _logger.LogWarning("Realtime audio request rejected: secure transport required.");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (_options.AllowedOrigins.Length > 0 &&
            context.Request.Headers.Origin is { Count: > 0 } origin &&
            !_options.AllowedOrigins.Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Realtime audio request rejected: origin not allowed.");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!Guid.TryParse(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var authenticatedUserId))
        {
            _logger.LogWarning("Realtime audio request rejected: authenticated user unavailable.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync(
            RealtimeAudioProtocol.WebSocketSubprotocol);
        var sessionId = Guid.NewGuid();
        var bufferSize = Math.Max(
            _options.MaxBinaryFrameBytes,
            _options.MaxControlMessageBytes) + 1;
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        IRealtimeAudioSession? session = null;
        var violations = 0;

        _logger.LogInformation(
            "Realtime audio connection accepted. SessionId: {SessionId}, UserId: {UserId}",
            sessionId,
            authenticatedUserId);

        try
        {
            var handshake = await ReceiveMessageAsync(
                socket,
                rentedBuffer,
                _options.MaxControlMessageBytes,
                TimeSpan.FromSeconds(_options.HandshakeTimeoutSeconds),
                cancellationToken);

            if (handshake.Status != ReceiveStatus.Complete ||
                handshake.MessageType != WebSocketMessageType.Text)
            {
                await RejectAsync(socket, sessionId, "invalid-handshake", cancellationToken);
                return;
            }

            var control = RealtimeAudioProtocol.ParseControl(
                rentedBuffer.AsSpan(0, handshake.Count));
            if (!TryValidateStart(control, out var format, out var startError))
            {
                await RejectAsync(socket, sessionId, startError, cancellationToken);
                return;
            }

            session = _sessionFactory.Create(sessionId, authenticatedUserId, format!);
            await SendControlAsync(socket, new
            {
                type = "session.accepted",
                protocolVersion = _options.ProtocolVersion,
                sessionId,
            }, cancellationToken);

            _logger.LogInformation(
                "Realtime audio session started. SessionId: {SessionId}, UserId: {UserId}, SampleRateHz: {SampleRateHz}, ChunkDurationMs: {ChunkDurationMs}",
                sessionId,
                authenticatedUserId,
                format!.SampleRateHz,
                format.ChunkDurationMs);

            ulong expectedSequence = 0;
            ulong previousElapsedMilliseconds = 0;

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var received = await ReceiveMessageAsync(
                    socket,
                    rentedBuffer,
                    _options.MaxBinaryFrameBytes,
                    TimeSpan.FromSeconds(_options.IdleTimeoutSeconds),
                    cancellationToken);

                if (received.Status == ReceiveStatus.Closed)
                {
                    await CloseSafelyAsync(
                        socket,
                        socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        "client-disconnect",
                        cancellationToken);
                    break;
                }
                if (received.Status == ReceiveStatus.TimedOut)
                {
                    await CloseSafelyAsync(
                        socket,
                        WebSocketCloseStatus.PolicyViolation,
                        "idle-timeout",
                        cancellationToken);
                    break;
                }

                if (received.Status == ReceiveStatus.TooLarge)
                {
                    await CloseSafelyAsync(
                        socket,
                        WebSocketCloseStatus.MessageTooBig,
                        "frame-too-large",
                        cancellationToken);
                    break;
                }

                if (received.Status == ReceiveStatus.Invalid)
                {
                    violations++;
                    if (await HandleViolationAsync(
                            socket,
                            sessionId,
                            "invalid-fragmentation",
                            violations,
                            cancellationToken)) break;
                    continue;
                }

                if (received.MessageType == WebSocketMessageType.Text)
                {
                    if (received.Count > _options.MaxControlMessageBytes)
                    {
                        await CloseSafelyAsync(
                            socket,
                            WebSocketCloseStatus.MessageTooBig,
                            "control-too-large",
                            cancellationToken);
                        break;
                    }

                    var message = RealtimeAudioProtocol.ParseControl(
                        rentedBuffer.AsSpan(0, received.Count));
                    if (IsValidStop(message))
                    {
                        await SendControlAsync(socket, new
                        {
                            type = "session.stopped",
                            protocolVersion = _options.ProtocolVersion,
                            sessionId,
                        }, cancellationToken);
                        await CloseSafelyAsync(
                            socket,
                            WebSocketCloseStatus.NormalClosure,
                            "session-stopped",
                            cancellationToken);
                        break;
                    }

                    violations++;
                    if (await HandleViolationAsync(
                            socket,
                            sessionId,
                            message.ErrorCode ?? "invalid-control",
                            violations,
                            cancellationToken)) break;
                    continue;
                }

                if (received.MessageType != WebSocketMessageType.Binary)
                {
                    violations++;
                    if (await HandleViolationAsync(
                            socket,
                            sessionId,
                            "unsupported-message-type",
                            violations,
                            cancellationToken)) break;
                    continue;
                }

                var parsedFrame = RealtimeAudioProtocol.ParseAudioFrame(
                    rentedBuffer.AsSpan(0, received.Count));
                var frameError = ValidateAudioFrame(
                    parsedFrame,
                    format,
                    expectedSequence,
                    previousElapsedMilliseconds);
                if (frameError is not null)
                {
                    violations++;
                    if (await HandleViolationAsync(
                            socket,
                            sessionId,
                            frameError,
                            violations,
                            cancellationToken)) break;
                    continue;
                }

                var header = parsedFrame.Header;
                await session.ConsumeAsync(
                    new RealtimeAudioChunk(
                        header,
                        rentedBuffer.AsMemory(
                            RealtimeAudioProtocol.BinaryHeaderLength,
                            header.PayloadLength)),
                    cancellationToken);
                expectedSequence++;
                previousElapsedMilliseconds = header.ElapsedMilliseconds;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await CloseSafelyAsync(
                socket,
                WebSocketCloseStatus.PolicyViolation,
                "timeout",
                CancellationToken.None);
        }
        catch (WebSocketException exception)
        {
            _logger.LogWarning(
                "Realtime audio connection ended unexpectedly. SessionId: {SessionId}, Category: {Category}",
                sessionId,
                exception.GetType().Name);
        }
        finally
        {
            if (session is not null)
            {
                session.Complete(violations);
                await session.DisposeAsync();
            }

            ArrayPool<byte>.Shared.Return(rentedBuffer, clearArray: false);
        }
    }

    private bool TryValidateStart(
        ControlParseResult result,
        out RealtimeAudioFormat? format,
        out string errorCode)
    {
        format = result.Message?.Audio;
        if (!result.IsValid || result.Message is null)
        {
            errorCode = result.ErrorCode ?? "invalid-control";
            return false;
        }

        var validationError = RealtimeAudioProtocol.ValidateSessionStart(
            result.Message,
            _options.ProtocolVersion,
            _options.ChunkDurationMs);
        if (validationError is not null)
        {
            errorCode = validationError;
            return false;
        }

        errorCode = string.Empty;
        return true;
    }

    private bool IsValidStop(ControlParseResult result) =>
        result.IsValid &&
        result.Message?.Type == "session.stop" &&
        result.Message.ProtocolVersion == _options.ProtocolVersion;

    private string? ValidateAudioFrame(
        AudioFrameParseResult result,
        RealtimeAudioFormat format,
        ulong expectedSequence,
        ulong previousElapsedMilliseconds)
    {
        if (!result.IsValid) return result.ErrorCode;
        if (result.Header.ProtocolVersion != _options.ProtocolVersion)
            return "unsupported-protocol-version";
        if (result.Header.Flags != 0) return "unsupported-frame-flags";
        if (result.Header.Sequence != expectedSequence) return "invalid-sequence";
        if (result.Header.ElapsedMilliseconds < previousElapsedMilliseconds)
            return "invalid-timestamp";

        var expectedPayloadBytes = checked(
            (format.SampleRateHz * format.ChunkDurationMs + 500) / 1_000 * sizeof(short));
        return result.Header.PayloadLength == expectedPayloadBytes
            ? null
            : "invalid-chunk-duration";
    }

    private async Task<bool> HandleViolationAsync(
        WebSocket socket,
        Guid sessionId,
        string errorCode,
        int violations,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Realtime audio protocol violation. SessionId: {SessionId}, ErrorCode: {ErrorCode}, Count: {Count}",
            sessionId,
            errorCode,
            violations);

        await SendControlAsync(socket, new
        {
            type = "transport.error",
            protocolVersion = _options.ProtocolVersion,
            code = errorCode,
        }, cancellationToken);

        if (violations < _options.MaxProtocolViolations) return false;

        await CloseSafelyAsync(
            socket,
            WebSocketCloseStatus.InvalidPayloadData,
            errorCode,
            cancellationToken);
        return true;
    }

    private async Task RejectAsync(
        WebSocket socket,
        Guid sessionId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await SendControlAsync(socket, new
        {
            type = "session.rejected",
            protocolVersion = _options.ProtocolVersion,
            code = errorCode,
        }, cancellationToken);

        _logger.LogWarning(
            "Realtime audio session rejected. SessionId: {SessionId}, ErrorCode: {ErrorCode}",
            sessionId,
            errorCode);
        await CloseSafelyAsync(
            socket,
            WebSocketCloseStatus.PolicyViolation,
            errorCode,
            cancellationToken);
    }

    private static async Task SendControlAsync(
        WebSocket socket,
        object message,
        CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open) return;
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(
            payload,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    private static async Task CloseSafelyAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string description,
        CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(status, description, cancellationToken);
            }
            catch (WebSocketException)
            {
                // The peer may already be gone; finally still releases the session.
            }
        }
    }

    private static async Task<ReceivedMessage> ReceiveMessageAsync(
        WebSocket socket,
        byte[] buffer,
        int maximumBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var count = 0;
        WebSocketMessageType? messageType = null;

        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(
                    buffer.AsMemory(count, Math.Min(buffer.Length - count, maximumBytes + 1 - count)),
                    timeoutSource.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                    return new(ReceiveStatus.Closed, result.MessageType, count);

                messageType ??= result.MessageType;
                if (messageType != result.MessageType)
                    return new(ReceiveStatus.Invalid, result.MessageType, count);

                count += result.Count;
                if (count > maximumBytes)
                    return new(ReceiveStatus.TooLarge, result.MessageType, count);

                if (result.EndOfMessage)
                    return new(ReceiveStatus.Complete, result.MessageType, count);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(ReceiveStatus.TimedOut, messageType ?? WebSocketMessageType.Text, count);
        }
    }

    private enum ReceiveStatus
    {
        Complete,
        Closed,
        TooLarge,
        TimedOut,
        Invalid,
    }

    private readonly record struct ReceivedMessage(
        ReceiveStatus Status,
        WebSocketMessageType MessageType,
        int Count);
}
