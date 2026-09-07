# EXT-001G — AWS Polly Realtime TTS Foundation

## Objective and MVP relationship

Deliver translated final segments as audible, ephemeral speech to an authorized Blazor WebAssembly observer. This advances the MVP promise of listening to browser-tab content in another language. It does not add history, storage, rooms, calls, exports, full language coverage, distributed delivery, or production SLA claims.

## Architecture and flow

Application defines generic speech synthesis contracts, an explicit voice catalog, bounded per-session coordination, demand leases, correlation, and safe metrics. Infrastructure implements the provider with the official AWS SDK for .NET. API connects translated finals to the coordinator and sends observer metadata plus binary audio. Web performs bounded sequential playback through a small browser-audio interoperability boundary and has no backend project reference.

```text
translation.final -> demand-aware session queue -> AWS Polly SynthesizeSpeech
                  -> speech.segment metadata -> binary MP3 frame
                  -> authorized observer -> bounded Web playback
```

Only non-empty, deduplicated `translation.final` events are eligible. `sourceResultId`, `eventSequence`, and a session-local `speechSequence` preserve correlation without exposing AWS identifiers.

## AWS configuration and voice mapping

Polly uses the existing AWS region and standard SDK credential chain. Development uses `AWS_PROFILE=translink-dev`; deployed workloads should use managed IAM credentials. `AwsPolly` configures queue capacity, provider timeout, maximum text characters, and maximum audio bytes. It contains no credentials.

The intentionally small catalog maps target `es` to locale `es-ES`, voice `Lucia`, neural engine, MP3, and 24 kHz. Lucia neural is supported in `us-east-1` and provides a pragmatic natural-quality/latency balance for the first English-to-Spanish validation. Unsupported targets retain text delivery and do not start TTS.

## Audio and protocol decisions

MP3 was selected over raw PCM and OGG. It is directly playable in major browsers, has substantially smaller payloads than PCM, avoids server transcoding, and remains usable by future mobile/desktop clients. PCM would simplify decoding but increase bandwidth and queue pressure; OGG variants have less uniform downstream compatibility. `SynthesizeSpeech` supplies an audio response stream, but this milestone performs segment-level synthesis after each final translation and does not claim token-level simultaneous speech.

Protocol v3 remains valid. Producer and observer endpoints have distinct directional roles, existing observers ignore unknown binary frames, and each observer audio payload is preceded by explicit `speech.segment` metadata containing format, length, sequence, correlation, and synthesis duration. Audio is a complete binary WebSocket message, never Base64 JSON. No producer PCM framing is reinterpreted.

## Demand, fan-out, backpressure, and cancellation

TTS defaults off. `observer.subscribe` may request speech, and `observer.speech` explicitly toggles it. With no active authorized speech consumer, no synthesis occurs. Multiple consumers share one per-session synthesis result and registry fan-out, avoiding duplicate Polly calls.

Server work is sequential and bounded to four pending segments. On overflow, the oldest not-yet-synthesized segment is discarded as a whole so playback stays near live content; bytes are never cut, reordered, or overlapped. Observer delivery retains its existing bounded queue and slow-observer isolation. Web playback is sequential and bounded to three pending segments with the same oldest-stale policy.

The final consumer disconnecting or disabling TTS cancels active synthesis and clears pending work. Producer Stop, session completion, API shutdown, Web disconnect, TTS Off, and Web session selection changes clear relevant ephemeral work/playback. No `Task.Run`, unbounded queue, database write, file, S3 object, browser storage, or audio cache is used.

## Failure isolation, security, and telemetry

Polly failure publishes only sanitized `speech.error` categories. Transcribe, Translate, producer capture, text events, Extension output, and Web text remain healthy. Translated text, synthesized bytes, JWTs, AWS credentials, request IDs, and provider exception details are not logged or persisted.

Safe in-memory counters cover requests, submitted characters, completed segments, failures, audio bytes, dropped segments, cumulative synthesis time, and active consumers. `speech.segment.synthesisDurationMilliseconds` measures translation-final-to-audio-ready provider work for development diagnostics. Web can expose queue depth; browser playback timing remains development evidence and is not an SLA.

## Testing

Automated tests use fakes and never call AWS. Coverage includes eligibility, empty/duplicate suppression, voice mapping, unsupported language, demand gating, multi-consumer single synthesis, ordering/correlation, cancellation, failure isolation, bounded queues, binary delivery, playback cleanup, and existing producer/observer regressions.

## IAM requirement

The development permission set requires only:

```text
polly:SynthesizeSpeech
Resource: *
```

Do not grant `AmazonPollyFullAccess`. IAM changes and permission-set reprovisioning are owner-controlled.

## Manual E2E procedure

1. Refresh and verify `translink-dev` IAM Identity Center authentication.
2. Confirm the role is allowed to call `polly:SynthesizeSpeech` without exposing credentials.
3. Start one API with `AWS_PROFILE=translink-dev` and `AwsTranscribe__Region=us-east-1`.
4. Start one Blazor Web client and load the Extension build.
5. Use the same temporary development JWT in Extension and Web, then start real `en-US` to `es` capture.
6. Discover and observe the session, explicitly turn translated audio On, and confirm ordered non-overlapping Spanish speech.
7. Record development-only synthesis/playback latency observations without content.
8. Verify Off clears speech, re-enable resumes future speech, Web disconnect leaves the producer healthy, and producer Stop clears playback.
9. Remove temporary tokens and stop both services.

## Acceptance and known limitations

Acceptance requires real Transcribe, Translate, Polly, text continuity, audible ordered Spanish segments, bounded near-live behavior, explicit demand, clean cancellation, and failure isolation. Limitations are segment-level speech, MP3 whole-segment buffering, one Spanish voice mapping, process-local affinity, no replay, no distributed backplane, and no production latency/load/SLA claim.
