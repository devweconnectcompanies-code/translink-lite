# EXT-001E — AWS Translate Realtime Foundation

## Objective and non-goals

Translate each eligible `transcript.final` through Amazon Translate and emit a client-agnostic `translation.final`. The first validation path is English (`en-US`) audio to Spanish (`es`). Translations remain ephemeral. This milestone does not translate partials and adds no Polly/TTS, persistence, history, queues, microservices, distributed coordination, or database changes.

## Architecture and realtime flow

```text
Client audio -> authenticated realtime v3 endpoint -> AWS Transcribe
             -> transcript.partial -> client
             -> transcript.final -> Application translation orchestration
             -> Infrastructure Amazon Translate adapter -> translation.final -> client
```

Application owns provider-neutral contracts, explicit language mapping, no-op policy, and orchestration. Infrastructure alone owns AWS SDK types. API owns WebSocket validation and event transport. Clients never receive AWS-specific details.

## Protocol

`session.start` requires `targetLanguage`. Because published v2 clients do not send this required field, the public subprotocol advances to `translink.realtime.v3` instead of claiming backward compatibility. The binary audio layout is unchanged apart from its version byte.

```json
{
  "type": "session.start",
  "protocolVersion": 3,
  "targetLanguage": "es",
  "audio": {
    "encoding": "pcm_s16le",
    "sampleRateHz": 48000,
    "channelCount": 1,
    "chunkDurationMs": 150,
    "sourceLanguage": "en-US"
  }
}
```

`translation.final` carries `sessionId`, `eventSequence`, `sourceResultId`, source/target languages, text, and operation duration. Correlation uses the generic transcript result ID and sequence. `translation.error` carries only the source result identifier and a stable sanitized category.

## Language mapping and cost controls

The centralized Application catalog explicitly maps supported transcription locales to Translate codes (`en-US` to `en`, supported Spanish locales to `es`, and `fr-FR` to `fr`) and validates a deliberately small target catalog. Unsupported values reject session start. Empty, whitespace, and partial transcripts are never submitted. Same-language translation is a deterministic pass-through. Each eligible final produces at most one application call. AWS SDK built-in retry behavior is used without a second retry layer.

## Ordering, cancellation, failure, and backpressure

The existing transcription dispatch awaits each callback. Translation therefore executes one final at a time per connection, preserves result order, and uses the existing bounded transcript channel for backpressure. There are no fire-and-forget tasks, global locks, or global session registries. Stop, disconnect, shutdown, and session cancellation propagate to the active request; Infrastructure bounds each provider request to ten seconds.

A segment-level failure emits a sanitized `translation.error` and leaves healthy transcription open. Provider messages, stack traces, request IDs, credentials, and signing details never cross the API boundary.

## AWS configuration and credentials

Translate reuses `AwsTranscribe:Region` for the shared AWS realtime boundary. The standard AWS SDK credential chain is used. Local development selects IAM Identity Center credentials with `AWS_PROFILE=translink-dev`; production uses workload identity. Required least privilege is `translate:TranslateText`. No profile name or credential is stored in application configuration.

## Privacy, observability, cost, and scalability

Audio, transcript text, and translated text are neither logged nor persisted. Safe telemetry contains session correlation, languages, duration, counts, and sanitized categories. The extension holds only the latest translation and counters in ephemeral capture state and clears them on Stop/reset. Readiness performs no paid AWS call.

The design is asynchronous and connection-scoped, performs no per-chunk or per-result database writes, and supports later API scale-out with connection affinity. No production scale or latency SLA is claimed without measurement.

## Testing strategy

Unit tests cover language mapping, unsupported values, partial/empty skipping, same-language pass-through, provider success/failure, and cancellation. Integration tests use deterministic fakes for target negotiation, correlated translation output, safe failure, and continued transcription. Extension tests cover target propagation and translation event validation. CI needs no AWS credentials and makes no billable calls.

## Manual validation

1. Authenticate the IAM Identity Center profile and confirm STS succeeds.
2. Build the solution and extension; load `APP/Extension/dist` in Edge/Chrome.
3. Configure a fresh short-lived development JWT only for this test.
4. Start exactly one API process:

   ```sh
   AWS_PROFILE=translink-dev \
   AwsTranscribe__Region=us-east-1 \
   dotnet run --project API/TransLink.Lite.API --launch-profile http --no-build
   ```

5. Confirm `/health/live` and `/health/ready` return 200.
6. Select real English HTTPS tab audio and capture with source `en-US`, target `es`.
7. Confirm partial/final transcripts, no translation of partials, multiple ordered `translation.final` events, an increasing counter, and ephemeral Spanish text corresponding to the source.
8. Record only safe translation durations; do not record speech content or claim an SLA.
9. Stop and confirm transcription/translation cease, latest text clears, and the UI is reusable.
10. Verify nothing sensitive or content-bearing was persisted, remove the JWT, and stop the API.

### Validation result

Manual end-to-end validation passed with real spoken-English audio from an HTTPS YouTube tab, source language `en-US`, target language `es`, and protocol `translink.realtime.v3`. The existing IAM Identity Center profile and standard AWS SDK credential chain were used; no long-lived AWS credential was introduced.

AWS Transcribe Streaming produced continuous partial events and final transcripts. Partial events did not trigger translation, while final transcript events produced real Amazon Translate `TranslateText` calls and correlated `translation.final` events. The popup reached at least four final transcripts and four final translations, and rendered coherent Spanish text ephemerally under Latest Translation without recording the source or translated content here.

Stop Capture ended capture, transcription, and translation cleanly, cleared the active translation and latest-translation UI state, and returned the extension to its reusable idle state. The milestone introduced no audio, transcript, or translation persistence.

## Acceptance, limitations, and rollback

Acceptance requires the full real-browser, real-Transcribe, real-Translate flow, multiple finals, correct English-to-Spanish meaning, no partial translation, safe errors, clean Stop, and no audio/protocol/security regression. Automated tests alone do not establish real AWS success.

The initial catalog is intentionally small, work is sequential per session, translated text is proof UI rather than history, and production latency/load remain unmeasured. Rollback removes Translate registration/orchestration and additive client fields while retaining EXT-001D; no database rollback is required.
