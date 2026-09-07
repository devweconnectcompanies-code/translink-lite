# EXT-001F — Web Realtime Translation Delivery Foundation

## Objective and non-goals

An authenticated Web client can discover and observe an active realtime producer session owned by the same user, receiving future `transcript.final`, `translation.final`, sanitized translation errors, and completion. This adds no history, replay, sharing, calls, TTS, persistence, migration, or distributed infrastructure.

## Architecture and identity

The Extension is an audio producer and the standalone Blazor WebAssembly Web app is a read-only observer. Both use the same ASP.NET Core API and generic contracts. Application defines delivery abstractions; Infrastructure supplies a replaceable in-process registry; API owns authenticated HTTP/WebSocket transport. AWS providers know nothing about observers. Web targets .NET 10, is implemented in C#, and has no project reference to the backend or dependency on AWS packages.

- `APP/Extension`: React, TypeScript, and Vite.
- `APP/Web`: Blazor WebAssembly, .NET 10, and C#.
- Backend: ASP.NET Core, .NET 10, and C#.

The persisted `TranslationSession` is not reused because its durable lifecycle does not match an ephemeral connection. Session IDs are random server-generated GUIDs. Possession is not authorization: lookup also requires the JWT-derived owner ID.

## Discovery and protocol

`GET /api/realtime/sessions/active` returns only the authenticated owner's active sessions. `WS /api/realtime/sessions/observe` uses the existing bearer subprotocol and an explicit `observer.subscribe` v3 handshake. V3 remains valid because this is a separate additive observer endpoint; producer framing is unchanged. Unknown and foreign sessions return the same safe rejection. Observers cannot send audio or producer controls.

## Fan-out, lifecycle, and backpressure

Each observer has an ordered bounded channel of 32 events. Publishing is non-blocking; overflow removes only the slow observer and cannot stall Transcribe, Translate, the producer, or other observers. There are no static global registries, replay buffers, raw-audio copies, fire-and-forget translations, or unbounded queues.

Observers receive future finals only. Web disconnect removes its subscription without affecting production. Producer Stop publishes `session.closed`, completes observers, and removes the session. Web reconnect is explicit and user-triggered, avoiding retry storms.

## Security and privacy

- Random IDs plus owner checks mitigate guessing and cross-user access.
- JWT travels in Authorization or WebSocket subprotocol, never URL/query, and Web holds it only in memory.
- Existing exact origin, secure transport, and control-size rules protect observer connections.
- Observer input after subscription causes a sanitized policy close.
- Bounded queues and a 20-line Web window limit memory.
- Deterministic producer cleanup prevents stale registry entries.

No audio, transcript, translation, JWT, authorization header, credential, or signed AWS request is logged or persisted.

## Scale-out and observability

The process-local registry is replaceable. Current observation requires producer/observer affinity to one API instance; distributed multi-node delivery is deferred without changing public contracts. Safe telemetry may include session correlation, observer lifecycle/counts, published counts, overflow category, and duration, never content. No numeric scaling claim is made.

## Testing

Unit tests cover ownership, unknown/foreign access, ordering, bounded overflow isolation, completion, and cleanup. Integration tests cover authenticated discovery/subscription, fake final delivery, correlation, authorization non-disclosure, Stop completion, and read-only enforcement. Web tests cover protocol validation and bounded rendering. CI uses fake AWS providers.

## Manual validation

1. Start one API with `AWS_PROFILE=translink-dev` and `AwsTranscribe__Region=us-east-1`.
2. Run `dotnet run --project APP/Web/TransLink.Lite.Web --launch-profile http` and open `http://localhost:5268`.
3. Use the same fresh short-lived development JWT in Extension and Web; never put it in a URL.
4. Start real English (`en-US`) tab capture targeting Spanish (`es`).
5. In Web, discover and observe the active session.
6. Verify Extension continues while Web receives ordered original and coherent Spanish finals.
7. Disconnect/reload Web and confirm the producer continues; reconnect manually.
8. Stop the producer and verify Web reports session end and clears subtitles.
9. Confirm no persistence/leakage, remove temporary JWTs, and stop both servers.

## Acceptance and limitations

Acceptance requires real AWS delivery to both clients, owner authorization, no observer audio, multiple ordered finals, isolated Web disconnect, clean producer Stop, and no persistence, leakage, playback, or protocol regression. Limitations are future-events-only delivery, single-instance affinity, manual reconnect, owner-only access, bounded UI lines, and temporary development token entry pending production auth UX.

## Manual E2E validation result

**Result: PASS — 2026-09-07.**

Manual validation used real English speech from a YouTube browser tab with an `en-US` source and `es` target. The complete flow passed through the Extension producer, authenticated `translink.realtime.v3` WebSocket transport, ASP.NET Core API, real AWS Transcribe Streaming, real AWS Translate, process-local realtime fan-out, and the Blazor WebAssembly observer. The Web client discovered the owned active session and received ordered future `transcript.final` and `translation.final` events while the Extension continued receiving its own output.

Explicit Web disconnect cleared its ephemeral subtitles without interrupting the producer, Transcribe, Translate, or Extension output. Manual reconnect to the still-active producer resumed delivery of new future events without replay. Stopping the producer propagated session completion, moved Web to the ended state, and cleared subtitle state.

An expired local IAM Identity Center session initially prevented AWS Transcribe startup. Refreshing the `translink-dev` SSO session resolved the environment condition; investigation confirmed that authentication and protocol v3 negotiation were working and that no product-code correction was required.
