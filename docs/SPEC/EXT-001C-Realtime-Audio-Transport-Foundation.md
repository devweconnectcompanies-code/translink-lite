# EXT-001C — Realtime Audio Transport Foundation

## Status

Implemented locally. Automated validation is complete; manual end-to-end Chrome validation is pending.

## Scope

EXT-001C proves an authenticated, continuous binary-audio path from the existing Chrome tab capture pipeline to the ASP.NET Core backend. The server validates and counts chunks in memory and never stores audio.

Excluded: AWS Transcribe, AWS Translate, subtitles, TTS, durable translation orchestration, production authentication UX, automatic reconnection, distributed session coordination, and production-scale load claims.

## One backend, multiple clients

The endpoint and protocol describe an authenticated realtime audio client, not Chrome concepts. Browser Extension, Web, Mobile, and Desktop clients can implement the same logical contract. Tab IDs, `tabCapture`, popup state, service-worker details, and stream IDs never cross the network.

The existing persisted `TranslationSession` is durable product metadata. The realtime transport session introduced here is ephemeral, server-generated, connection-scoped, and not written to PostgreSQL. A future orchestration phase may associate the two using an authorized server-side use case; EXT-001C deliberately does not force that coupling.

## Architecture

```text
Chrome tab MediaStream
├── direct local playback
├── AnalyserNode (activity meter)
└── AudioWorklet (mono chunking)
      -> PCM signed 16-bit little-endian
      -> versioned binary WebSocket frame
      -> authenticated WSS /api/realtime/audio
      -> protocol validation
      -> direct bounded consumption
      -> ephemeral validation metrics sink
```

The offscreen document owns the AudioWorklet, WebSocket, counters, and transport lifecycle. It already owns the long-lived MediaStream and Web Audio graph and is not tied to popup lifetime. The popup remains an observer/controller, while the service worker coordinates capture startup and supplies transient local development configuration. A service worker alone is not a reliable owner for a long-running socket because Manifest V3 may terminate it when idle.

## Authentication

The endpoint requires the existing JWT Bearer authentication. The API derives the user from the validated `NameIdentifier` claim and never accepts a client-supplied owner ID.

Browser WebSocket APIs cannot add an `Authorization` header. The extension therefore requests `translink.realtime.v1` plus a `translink.bearer.<JWT>` handshake subprotocol. `JwtBearerEvents` extracts the credential only for `/api/realtime/audio`; the server negotiates only the public protocol identifier, so the credential is never echoed or placed in a URL. Tokens are never logged or committed. Non-browser clients may continue using a normal Bearer header.

The extension has no full authentication UI yet. Manual development uses a token from the existing login/register endpoint stored locally under `developmentAccessToken` in `chrome.storage.local`. This is a deliberate development bridge, not the production credential-storage design. Remove it after testing.

Non-development WebSocket traffic must use HTTPS/WSS and startup requires at least one exact `RealtimeAudio:AllowedOrigins` value. Development permits an empty list so an unpacked extension with a generated ID can connect; developers may still configure its exact `chrome-extension://<extension-id>` origin through User Secrets. Native clients may omit `Origin`, so JWT validation remains the authorization boundary.

## Endpoint

```text
GET /api/realtime/audio
Upgrade: websocket
Authorization: required
```

Non-WebSocket requests return `400`. Missing or invalid authentication is rejected before the upgrade. The current validation endpoint handles one connection in one API process for that connection's lifetime.

## Protocol version 1

Control messages are compact JSON. Audio uses binary messages; audio samples are never Base64 or JSON arrays.

Client start:

```json
{"type":"session.start","protocolVersion":1,"audio":{"encoding":"pcm_s16le","sampleRateHz":48000,"channelCount":1,"chunkDurationMs":150}}
```

Client graceful stop:

```json
{"type":"session.stop","protocolVersion":1}
```

Server controls are `session.accepted`, `session.rejected`, `session.stopped`, and `transport.error`. Accepted sessions include a server-generated session ID. Clients must not treat that ID as durable ownership or route affinity.

## Binary audio frame

All multi-byte integers use little-endian byte order.

| Offset | Size | Field |
|---:|---:|---|
| 0 | 2 | ASCII magic `TL` |
| 2 | 1 | Protocol version (`1`) |
| 3 | 1 | Flags (`0`, reserved) |
| 4 | 8 | Monotonically increasing unsigned sequence, starting at `0` |
| 12 | 8 | Monotonic elapsed milliseconds from capture start |
| 20 | 4 | Payload byte length |
| 24 | N | Mono PCM signed 16-bit little-endian payload |

The session is attributable through its authenticated WebSocket connection. Repeating a UUID in every 150 ms frame would add metadata without improving routing correctness.

## Audio format and chunking

The AudioWorklet downmixes the captured stream to mono and frames approximately 150 ms of samples. Float Web Audio samples are clipped and converted to signed PCM16 little-endian. The transport `AudioContext` requests 48 kHz, allowing Chrome's native media pipeline to handle any source conversion rather than introducing a naïve custom resampler. The server contract accepts 16–48 kHz for future clients.

PCM16LE is compatible with the future AWS Transcribe direction. Measuring whether 16 kHz resampling improves bandwidth or cost without harming recognition is deferred. Chunk duration is configured by `RealtimeAudio:ChunkDurationMs`, constrained to 100–200 ms, with a tracked default of 150 ms.

## Bounded memory and backpressure

The API rents one bounded receive buffer per connection. It does not use `MemoryStream`, an unbounded queue, or a thread per connection. Each validated chunk is consumed before the next `ReceiveAsync`, so downstream delay naturally applies socket-level backpressure.

The current sink consumes synchronously and retains only counters. On the extension, `WebSocket.bufferedAmount` may not exceed four chunk payloads. Crossing that boundary stops capture with a backpressure error instead of buffering indefinitely. Server limits reject oversized controls and binary messages before parsing, and buffers return to `ArrayPool<byte>` on every exit path.

## Lifecycle

```text
authenticated -> WebSocket accepted -> awaiting session.start
-> session.accepted -> receiving ordered binary chunks
-> session.stop / disconnect / timeout / violation / cancellation
-> metrics summary -> cleanup
```

Handshake and idle timeouts propagate cancellation. Sequence must start at zero and remain contiguous; elapsed timestamps cannot decrease. Payload length, alignment, format, flags, and protocol version are validated. One configured violation closes the session. Client disconnect completes the close handshake where possible. Explicit user Stop never reconnects.

## Reconnect and errors

EXT-001C intentionally performs no automatic retries. A network close, rejection, protocol error, or backpressure event becomes a visible transport/capture error and cleans up the graph. The user may explicitly retry. A bounded backoff policy remains deferred until session-resume and duplicate-audio semantics are defined.

## Validation sink and observability

`IRealtimeAudioSessionFactory` creates an ephemeral sink per accepted connection. Infrastructure counts chunks, bytes, and first/last sequence only. It writes no audio, transcript, file, or database row.

Structured logs contain server session ID, authenticated user ID, format, counts, duration, and safe protocol codes. They never contain JWTs, audio payloads, tab metadata, stream IDs, email addresses, or URLs.

## Horizontal scale boundary

One WebSocket stays on one API instance for its lifetime, compatible with a load balancer that supports upgrades. The protocol exposes no instance affinity and durable correctness does not depend on in-process state. Future provider/shared processing can replace the sink abstraction without rewriting clients. Redis, Kafka, Kubernetes, and distributed coordination are intentionally absent.

## Configuration

Safe `RealtimeAudio` defaults cover protocol version, binary/control limits, chunk duration, handshake/idle timeouts, violation limit, and exact origin allowlisting. Invalid numeric limits fail startup, and non-development startup fails if its origin allowlist is empty. No setting contains credentials.

The unpacked client defaults to `ws://localhost:5221/api/realtime/audio`, matching `dotnet run` and the API's `http` launch profile. Earlier code defaulted to port `7194` over WSS while the documented owner test ran only port `5221` over HTTP; the browser therefore failed before an HTTP upgrade reached ASP.NET Core. Extension tests now pin the default URL, and integration coverage sends the same `chrome-extension://` Origin shape and verifies exact allowlist acceptance plus public subprotocol negotiation.

For localhost only, the offscreen transport emits structured lifecycle diagnostics and mirrors them to the longer-lived service-worker console. Logged endpoint data is restricted to scheme, host, port, and path. JWT-bearing protocols, query strings, audio, URLs, tab titles, and stream IDs are excluded. ASP.NET Core similarly logs whether an upgrade, Origin, public protocol, and authenticated identity were present without logging header values.

## Tests

Backend unit tests cover control parsing, malformed input, version/format validation, binary layout, declared length, and PCM alignment. WebSocket integration tests cover unauthorized rejection, authenticated start, ordered frames, malformed frames, sequence gaps, oversized frames, graceful stop, and disconnect cleanup. Extension tests cover start serialization, PCM16 conversion, binary framing, and server-control validation. Chrome media capture remains manual.

## Manual development validation

1. Start PostgreSQL and configure User Secrets per `docs/configuration.md`.
2. Run `dotnet run --project API/TransLink.Lite.API --launch-profile http`.
3. Confirm `http://localhost:5221/health/live` and `/health/ready` are healthy.
4. Register or login through the existing API and copy only the returned access token.
5. Run `npm run build` from `APP/Extension` and reload `APP/Extension/dist`.
6. In the extension service-worker DevTools console run:

   ```javascript
   chrome.storage.local.set({ developmentAccessToken: "<DEVELOPMENT_JWT>" })
   ```

7. Remove any stale endpoint override so the fixed Development default is used: `chrome.storage.local.remove("realtimeEndpoint")`. To test another deployment, configure its exact `wss://` endpoint explicitly.
8. Open audible YouTube, select the tab, and start capture.
9. Confirm stable local audio, responsive level, Streaming UI, and increasing chunks.
10. Confirm API lifecycle logs and increasing final chunk/byte totals without content.
11. Close/reopen the popup and confirm streaming continues.
12. Stop and confirm acknowledged WebSocket shutdown and idle UI.
13. Repeat and close the source tab; confirm deterministic cleanup.
14. Stop the API during streaming; confirm a clear error and no infinite retry.
15. Restart the API and retry explicitly.
16. Inspect logs for errors or privacy leaks.
17. Remove the token with `chrome.storage.local.remove("developmentAccessToken")`.

Manual success must not be claimed until the owner completes this procedure.

## Deferred work

- AWS Transcribe Streaming, resampling decisions, AWS Translate, subtitles, and Polly/TTS;
- durable product-session orchestration;
- full authentication UX and production token storage/refresh;
- bounded automatic reconnect/session resume;
- production origin/endpoint delivery;
- distributed coordination or backplane if justified;
- autoscaling infrastructure and production-scale load tests.

## References

- [Chrome offscreen API](https://developer.chrome.com/docs/extensions/reference/api/offscreen)
- [Chrome extension network requests](https://developer.chrome.com/docs/extensions/develop/concepts/network-requests)
- [ASP.NET Core WebSockets](https://learn.microsoft.com/aspnet/core/fundamentals/websockets)
- [Amazon Transcribe streaming](https://docs.aws.amazon.com/transcribe/latest/dg/streaming.html)
