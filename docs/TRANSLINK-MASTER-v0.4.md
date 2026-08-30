# TransLink product and architecture master v0.4

**Status:** Current product and architecture source of truth<br>
**Active product:** TransLink-Lite<br>
**Technical baseline:** .NET 10, ASP.NET Core, PostgreSQL, React, TypeScript, Vite, Manifest V3, and planned AWS services<br>
**Principle:** Design for scale; deploy for measured demand.

This version supersedes `TRANSLINK-MASTER-v0.3.md`. The v0.3 document remains unchanged as a historical record. Implemented code remains the source of truth for actual behavior.

## 1. Product vision

TransLink is a realtime multilingual communication platform intended to remove language barriers from meetings, calls, multimedia content, and direct communication. It is not limited to text translation.

The planned ecosystem includes TransLink-Lite, TransLink-Pro, Enterprise capabilities, Web, Browser Extension, Desktop and Mobile clients, a public API, and third-party SDKs.

## 2. Immediate TransLink-Lite outcome

The first end-to-end Alpha must demonstrate:

```text
Browser tab audio
  -> TransLink Extension capture
  -> authenticated realtime backend transport
  -> AWS Transcribe Streaming
  -> AWS Translate
  -> realtime result delivered to TransLink Web
```

The engineering metric is end-to-end translation latency. The working target is approximately 0.8–1.0 seconds when conditions permit; it must be measured and must not be presented as achieved before evidence exists.

## 3. Architecture principles

- Modular monolith first; extract services only with operational evidence.
- One logical backend serves Extension and Web.
- Domain and Application remain independent of transport and external providers.
- Stateless services, asynchronous I/O, cancellation, bounded buffers, and backpressure where applicable.
- Server-side authentication, authorization, ownership, and input validation.
- Versionable contracts, failure isolation, observability, and least-privilege access.
- No client connects directly to PostgreSQL or AWS using privileged credentials.
- Audio is processed and discarded by default unless a consented feature requires storage.

Target logical flow:

```text
Extension / Web
      -> HTTPS / WSS
      -> TransLink Lite backend
      -> PostgreSQL and realtime pipeline
      -> AWS Transcribe -> AWS Translate -> AWS Polly (later TTS phase)
```

## 4. Verified implementation baseline

`BASELINE-001` is complete, published on `main`, and verified by a successful GitHub Actions execution.

### Repository and backend

```text
TransLink-Lite/
├── API/
│   ├── TransLink.Lite.API/
│   ├── TransLink.Lite.Application/
│   ├── TransLink.Lite.Domain/
│   └── TransLink.Lite.Infrastructure/
├── APP/
│   ├── Extension/
│   └── Web/
├── Tests/
├── docs/
├── .github/
├── global.json
└── TransLink.Lite.slnx
```

All .NET projects target `net10.0`. `global.json` selects SDK `10.0.300` and compatible patch servicing within its feature band.

The implemented dependency direction is Domain <- Application <- Infrastructure, with API composing Application and Infrastructure. Controllers are thin HTTP adapters. Application services orchestrate use cases. Application owns repository interfaces; Infrastructure owns EF Core, concrete repositories, JWT generation, and BCrypt hashing.

### Implemented HTTP surface

Anonymous:

- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /health`
- `GET /health/live`
- `GET /health/ready`

Authenticated and ownership-scoped:

- `GET /api/Users/me`
- `PUT /api/Users/me`
- `POST /api/TranslationSessions`
- `GET /api/TranslationSessions`
- `GET /api/TranslationSessions/{id}`

`GET /api/Users` and `POST /api/Users` were removed during authorization hardening.

### Implemented security and validation

- JWT Bearer authentication and BCrypt password hashing.
- Server-derived ownership; request DTOs do not accept `UserId` mass assignment.
- Centralized email normalization and unique normalized-email identity.
- Email, name, password, language, and session input validation.
- Provisional supported languages: `en` and `es`.
- Fixed-window rate limiting for public registration and login.
- Secrets absent from tracked current-source configuration; User Secrets for local development.
- Fail-fast configuration validation.
- Global safe `ProblemDetails` responses with `traceId`.

The current supported-language catalog is deliberately provisional. Future work must model provider-specific capabilities across Transcribe, Translate, and Polly rather than accepting arbitrary strings.

### Implemented persistence

PostgreSQL is the transactional datastore. EF Core mappings use `IEntityTypeConfiguration<T>`. The model includes persisted `NormalizedEmail`, unique `IX_Users_NormalizedEmail`, bounded text columns, a required `TranslationSession.UserId -> User.Id` foreign key, an index on `UserId`, and restricted user deletion. Five migrations are tracked; the latest is `StrengthenUserAndTranslationSessionIntegrity`.

The API does not apply migrations automatically at startup.

### Implemented operations and quality

- `/health/live` is process-only liveness.
- `/health/ready` checks PostgreSQL and returns `503` when unavailable.
- `/health` temporarily aliases liveness.
- 30 unit tests and 26 PostgreSQL/Testcontainers integration tests (56 total).
- Integration tests use PostgreSQL 17 Alpine, ephemeral databases, and real migrations.
- CI restores, audits NuGet dependencies, builds Release, and runs all tests on `ubuntu-latest`.
- The known Microsoft.OpenApi advisory found during the baseline was remediated; the current approved audit found no known vulnerable NuGet packages.

Detailed implementation documentation is in `backend-architecture.md`; local configuration is in `configuration.md`.

## 5. Planned, not implemented

The following are product direction, not current functionality:

- real browser-tab discovery and audio capture;
- WebSocket transport and connection lifecycle;
- realtime audio ingestion, bounded buffers, and backpressure;
- AWS Transcribe Streaming, Translate, and Polly;
- translated result delivery to Web;
- distributed rate limiting, Redis, or queues;
- production AWS infrastructure and external observability;
- internal translated calls, billing, organizations, and enterprise administration;
- Desktop, Mobile, public API, and third-party SDKs.

No documentation or UI should imply that these capabilities already exist.

## 6. Realtime and audio direction

Audio must be streamed rather than uploaded as a completed file. The initial conceptual pipeline is:

```text
Audio chunks
  -> ingestion
  -> bounded processing buffer
  -> AWS Transcribe partial/final transcript
  -> AWS Translate
  -> realtime gateway
  -> Web client
```

Initial chunk duration may be approximately 100–200 ms, but remains a configurable hypothesis to benchmark. The system must define message limits, timeouts, cancellation, heartbeats, reconnection, controlled overload behavior, and saturation metrics before production use.

## 7. Security direction

- HTTPS/WSS in deployed environments; restrictive CORS and browser security headers.
- Least-privilege AWS IAM and no AWS credentials in clients.
- Never log passwords, full JWTs, signing keys, raw audio, or unnecessary personal data.
- Authenticate realtime connections and authorize every session.
- Treat client-side controls only as UX; enforcement remains server-side.
- Introduce tenant/organization authorization only when that model is designed.
- Evaluate PostgreSQL RLS only as defense in depth, never as an authorization replacement.
- Use environment secret management in deployment and rotate credentials through owning systems.
- Continue dependency audits and controlled upgrades.

## 8. Observability direction

Future realtime work must introduce structured logs, metrics, and traces around active sessions, connections, reconnects, request rate, failures, buffer saturation, transcription latency, translation latency, end-to-end latency, and estimated provider cost.

Correlation should include safe forms of `TraceId`, `UserId`, `TranslationSessionId`, and `ConnectionId` without leaking tokens or personal content.

## 9. Client boundaries

The Browser Extension is a thin capture/control client: authenticate, discover/select tabs, choose a supported target language, capture and transmit audio, expose state, reconnect, stop, and open Web. It does not own billing, complete history, business rules, exports, or direct AWS access.

Web is the principal experience for realtime results and later history, notes, TTS, exports, session management, settings, and calls.

## 10. Roadmap

### Completed: BASELINE-001

Repository hygiene, secret rotation/configuration, reproducible SDK, dependency patching, authorization, validation, relational integrity, application separation, global errors, health checks, automated tests, CI, repository organization, publication, and documentation reconciliation.

### Next: EXT-001 — Browser Extension foundation

`EXT-001` begins the first client milestone. Its first approved execution unit must be:

#### EXT-001A — Chrome Integration Foundation

- inspect the existing Extension implementation before changes;
- establish/verify Manifest V3 structure;
- enumerate real browser tabs with required permissions;
- present explicit tab selection;
- persist safe local selection/preferences with `chrome.storage`;
- integrate the provisional supported-language choice without inventing the final language subsystem;
- add focused validation/testing appropriate to Extension tooling;
- do not capture or stream audio yet.

#### Later EXT-001 work

- real `tabCapture` audio lifecycle;
- audio activity detection and cleanup;
- capture-format and processing decisions based on browser constraints.

### Subsequent milestones

- `RT-001`: authenticated realtime transport, binary audio, lifecycle, reconnection, and backpressure.
- `AWS-STT-001`: AWS Transcribe Streaming.
- `AWS-TR-001`: AWS Translate.
- `WEB-RT-001`: realtime Web rendering and measured end-to-end latency.
- Later: Polly/TTS, calls, richer history, commercial and enterprise capabilities.

## 11. Alpha definition of done

The Alpha is complete only when a reproducible flow authenticates a user, selects and captures a real tab, creates/authorizes a translation session, streams audio to the backend, transcribes and translates it through AWS, delivers realtime results to Web, handles stop/cleanup and basic reconnection, and measures end-to-end latency.

## 12. Architecture decisions to formalize

The following remain the intended ADR set: one logical backend; ASP.NET Core/C#; PostgreSQL transactional storage; thin Extension; Web as primary UI; streaming audio; non-persistent audio by default; modular monolith first; horizontal scalability; AWS as initial cloud provider; no direct client database access; and security-first baseline before realtime.

Create individual ADRs only when a decision is actively reviewed. `docs/ADR/README.md` is the current placeholder.

## 13. Delivery workflow

```text
Specification -> plan -> scoped implementation -> build/test -> review -> commit -> documentation -> next task
```

For each task: inspect the current code, preserve English technical identifiers, avoid unrelated dependencies and scope, protect secrets, validate the relevant build/tests, and report contradictions rather than silently redefining requirements.
