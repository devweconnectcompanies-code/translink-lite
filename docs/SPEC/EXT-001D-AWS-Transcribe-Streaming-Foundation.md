# EXT-001D — AWS Transcribe Streaming Foundation

## Scope and architecture

EXT-001D replaces the validation-only audio sink with a provider-neutral Application contract and an AWS Transcribe Streaming adapter in Infrastructure. The authenticated API remains the only cloud boundary:

```text
Any authenticated client -> realtime WebSocket -> API handler
  -> IRealtimeSpeechTranscriptionSession -> AWS adapter -> AWS Transcribe
  -> generic partial/final transcript events -> originating WebSocket
```

The Extension remains a thin producer/control surface. It displays transcription state and partial/final counters, but never displays transcript text. Translate, target languages, Polly, persistence, history, billing, and the Web transcript UI are deferred.

## Provider boundary and lifecycle

`IRealtimeSpeechTranscriptionSessionFactory` creates isolated mutable state per connection. A session starts the provider stream asynchronously, accepts PCM chunks, emits provider-neutral events, completes gracefully, propagates cancellation, and releases provider resources. AWS SDK types are confined to Infrastructure. The shared AWS client is stateless/thread-safe; no transcription stream is singleton.

The server-generated realtime session ID, durable `TranslationSession`, and provider session identity are distinct. Client input never establishes ownership. The current return path uses the producer socket, but the event contract is not Chrome-specific. A later authorized routing layer can associate an authenticated producer and authenticated Web/Mobile/Desktop subscribers with a server-owned session without changing the provider adapter or transcript shape.

## Audio and language

The existing mono PCM signed 16-bit little-endian stream remains at 48 kHz in 150 ms frames. AWS Transcribe accepts PCM at 8–48 kHz, so no resampler or binary-frame change is necessary.

Protocol v2 adds `audio.sourceLanguage` to `session.start`. The initial transcription catalog accepts canonical `en-US` and `es-US` and normalizes casing. Unsupported values are rejected before provider allocation. This deliberately small catalog can later become the centralized capability model for Transcribe, Translate, and Polly.

## Generic transcript events

The server emits `transcript.partial` and `transcript.final` with protocol version, ephemeral realtime session ID, monotonic event sequence, provider result ID, text, finality, optional start/end milliseconds, and source language. Raw AWS objects and request IDs are not exposed.

Partial events are replaceable hypotheses, not durable lines. Consumers should replace the current hypothesis for its result and commit only a final event. Neither partial nor final text is persisted in this phase.

## Backpressure, concurrency, and cleanup

Each provider session owns bounded audio and transcript channels. Audio writes wait only up to the configured bound/timeout and then fail the session safely. Transcript overload also fails safely rather than growing memory. The WebSocket receive loop awaits each audio handoff, and server sends are serialized per socket.

All I/O is asynchronous. Stop, disconnect, API cancellation, bounded timeouts, and provider failure complete/cancel the stream. The default maximum provider session is 60 minutes. No global mutable session dictionary, audio file, transcript file, or database write is introduced. Horizontal routing/stickiness and a distributed event backplane remain deployment concerns for a later producer/subscriber phase.

## Configuration and credentials

Tracked `AwsTranscribe` settings contain non-secret region, stabilization, buffer, timeout, and session-limit values. Credentials use the standard AWS SDK credential chain: a local AWS profile or environment variables in Development, and a managed workload role in production. AWS credentials must never be stored in appsettings, User Secrets intended for application settings, browser storage, client code, tests, or documentation.

The minimum IAM action for this SDK HTTP/2 streaming path is:

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Action": "transcribe:StartStreamTranscription",
    "Resource": "*"
  }]
}
```

`Resource: "*"` is required because this streaming action does not support resource-level scoping. Do not grant `AdministratorAccess`. The configured region must offer Transcribe Streaming. Normal `/health` and `/health/ready` do not make a paid provider request or require local AWS credentials.

## Privacy, observability, and cost

Structured logs contain session/user correlation IDs, provider category, region, language, sample rate, duration, byte/chunk counts, result counts, and safe failure categories. They never contain audio, transcript text, JWTs, browser metadata, credentials, signed requests, or provider exception details. Audio and transcript text exist only in bounded memory and realtime frames.

Streaming is usage-billed. Bounded sessions and explicit stop limit accidental runtime, but production quotas, plan enforcement, rate controls, cost metrics, load tests, and billing are deferred.

## Automated validation

Unit tests cover language validation, partial/final mapping, and bounded option validation. Integration tests replace AWS with a deterministic fake and cover authenticated start with language, audio acceptance, partial/final return events, safe provider failure, stop, disconnect, and cleanup. CI makes no real AWS or billable call.

## Manual real-AWS validation

1. Configure a short-lived AWS CLI profile or environment credentials with only the IAM action above; never paste credentials into the repository or browser.
2. Set the profile/credential-chain selection and region for the API process (for example `AwsTranscribe__Region=us-east-1`).
3. Start local PostgreSQL and the API using the Development HTTP profile.
4. Login and obtain a short-lived Development JWT without logging or committing it.
5. From `APP/Extension`, run `npm install` and `npm run build`, then reload `APP/Extension/dist` as the unpacked extension.
6. In the extension service-worker console, configure only local temporary values:
   `chrome.storage.local.set({ developmentAccessToken: "<DEVELOPMENT_JWT>", sourceLanguage: "en-US" })`.
7. Open an audible English HTTPS video, select it, and start capture.
8. Verify `socket.open`, protocol `translink.realtime.v2`, `session.accepted`, transcription active, and increasing partial/final counters. Counter growth proves AWS returned recognized results without exposing speech text.
9. Confirm source audio remains stable; close/reopen the popup and confirm capture/counters continue.
10. Stop capture and verify the provider completion log and idle UI.
11. Repeat once and close the captured tab; repeat once and stop the API. Verify prompt safe termination and no reconnect loop or background stream.
12. Inspect safe logs and database/filesystem: no content, credential, audio, or transcript was persisted.
13. Remove the temporary JWT and language setting, stop the API, and unset temporary AWS environment variables/profile selection.

Real AWS success must not be claimed until the owner completes this procedure.

## Deferred work

AWS Translate, translated events, target-language orchestration, Web subscription/rendering, Mobile/Desktop clients, Polly/TTS, transcript persistence/history, notes, export, billing, quotas, distributed session coordination/backplane, reconnect/resume, autoscaling infrastructure, and production load/cost validation remain deferred.
