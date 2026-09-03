# TransLink Lite backend architecture

This document describes the implemented backend baseline. Product direction and planned capabilities are documented in `TRANSLINK-MASTER-v0.4.md`.

## Repository layout

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
│   ├── TransLink.Lite.UnitTests/
│   └── TransLink.Lite.IntegrationTests/
├── docs/
├── .github/workflows/backend-ci.yml
├── global.json
└── TransLink.Lite.slnx
```

The repository is a modular monolith. It now contains an authenticated realtime audio-ingestion boundary and ephemeral validation sink; it does not contain AWS translation services.

## Projects and dependencies

| Project | Responsibility | Direct project references |
|---|---|---|
| `TransLink.Lite.Domain` | Domain entities | None |
| `TransLink.Lite.Application` | Use-case orchestration, DTOs, validation, and persistence/authentication abstractions | Domain |
| `TransLink.Lite.Infrastructure` | EF Core/PostgreSQL, repositories, JWT creation, and BCrypt hashing | Application, Domain |
| `TransLink.Lite.API` | HTTP transport, composition root, middleware, authentication, rate limiting, and health probes | Application, Infrastructure |

```text
API ──────────────┬──> Application ──> Domain
                  └──> Infrastructure ──> Application
                                      └──> Domain
```

Domain has no dependency on ASP.NET Core, EF Core, Infrastructure, or API. Controllers do not access `AppDbContext`; they call application-service interfaces. Repository interfaces are defined in Application and implemented in Infrastructure.

## Domain model

`User` persists `Id`, profile names, `Email`, `NormalizedEmail`, `PasswordHash`, `PreferredLanguage`, `CreatedAt`, and its owned sessions. `NormalizedEmail`, rather than display `Email`, is used for identity lookup and uniqueness.

`TranslationSession` persists `Id`, `UserId`, `Title`, source and target languages, `Status`, timestamps, and a required `User` navigation. It currently represents session metadata, not an audio stream, transcript, or translation pipeline.

## Application layer

| Service | Implemented responsibilities |
|---|---|
| `AuthService` | Register, normalize email, enforce uniqueness, hash passwords, authenticate, and issue tokens |
| `UserProfileService` | Read and update the authenticated user's profile |
| `TranslationSessionService` | Create and retrieve sessions owned by the authenticated user |
| `RealtimeAudioProtocol` | Parse and validate versioned generic control and binary-audio frames |

Persistence abstractions are specific: `IUserRepository` and `ITranslationSessionRepository`. No generic repository abstraction is used. Request and response DTOs prevent binding domain entities directly and do not expose `UserId`, `PasswordHash`, or `NormalizedEmail` as user-controlled fields.

### Validation and normalization

The baseline uses native data annotations, custom attributes, and application-level checks. It includes centralized trim/invariant-lowercase email normalization, syntactic email validation, password length from 12 through 128 characters, bounded profile names and session titles, supported-language validation, and rejection of equal source and target languages.

The provisional `SupportedLanguageCatalog` contains `en` and `es`. It is intentionally small and structured for later replacement by a capability-aware catalog; it is not the final AWS language matrix.

## Infrastructure and PostgreSQL

`AppDbContext` applies mappings from the Infrastructure assembly through `ApplyConfigurationsFromAssembly`.

`UserConfiguration` enforces these maximum lengths: `FirstName` 100, `LastName` 100, `Email` 254, `NormalizedEmail` 254, `PasswordHash` 255, and `PreferredLanguage` 35. It creates the unique index `IX_Users_NormalizedEmail`.

`TranslationSessionConfiguration` enforces: `Title` 200, `SourceLanguage` 35, `TargetLanguage` 35, and `Status` 32. It creates an index on `UserId` and configures the required relationship `TranslationSession.UserId -> User.Id` with `DeleteBehavior.Restrict`.

`UserRepository` and `TranslationSessionRepository` use EF Core asynchronous APIs and cancellation tokens. Session queries include the authenticated user's ID, enforcing ownership at the data-access boundary.

Five migrations are tracked. The latest is `StrengthenUserAndTranslationSessionIntegrity`, which introduced normalized-email persistence, bounded fields, the unique email index, the session index, and the required foreign key. Migrations are not applied automatically at API startup.

## HTTP API

Controllers are thin adapters: they obtain the authenticated user identifier, invoke an application service, and translate the result to HTTP.

| Method | Route | Access | Purpose |
|---|---|---|---|
| `POST` | `/api/auth/register` | Anonymous, rate limited | Register and return a JWT |
| `POST` | `/api/auth/login` | Anonymous, rate limited | Authenticate and return a JWT |
| `GET` | `/api/Users/me` | Bearer JWT | Get the authenticated profile |
| `PUT` | `/api/Users/me` | Bearer JWT | Update the authenticated profile |
| `POST` | `/api/TranslationSessions` | Bearer JWT | Create an owned session |
| `GET` | `/api/TranslationSessions` | Bearer JWT | List owned sessions |
| `GET` | `/api/TranslationSessions/{id}` | Bearer JWT | Get one owned session |
| `GET` | `/health` | Anonymous | Temporary liveness compatibility alias |
| `GET` | `/health/live` | Anonymous | Process liveness |
| `GET` | `/health/ready` | Anonymous | PostgreSQL readiness |
| `GET` | `/api/realtime/audio` | Bearer JWT + WebSocket upgrade | Ephemeral realtime PCM audio ingestion |

The former collection-level `GET /api/Users` and `POST /api/Users` endpoints have been removed.

## Security behavior

- Passwords are hashed and verified with BCrypt.
- JWT validation checks issuer, audience, lifetime, and the symmetric signing key.
- JWT settings and required configuration are validated at startup.
- Profile and session endpoints require authentication.
- The server derives the user from JWT claims; clients cannot assign ownership.
- Session reads return `404` for missing and non-owned resources.
- Register/login share a fixed-window, per-client-IP rate limit.

The in-process limiter is suitable for the current single-instance baseline. Horizontal deployment requires trusted-proxy handling and distributed enforcement.

## Errors and health

ASP.NET Core `ProblemDetails` is the public error contract. The global handler maps validation to `400`, authentication failure to `401`, missing/non-owned resources to `404`, normalized-email conflicts to `409`, and unexpected failures to a generic `500`. Responses include `traceId` without SQL details, stack traces, secrets, or internal exception messages.

Liveness checks only the API process. Readiness checks PostgreSQL and returns `503` when it is unavailable while liveness remains healthy. Responses expose only aggregate status.

## Realtime audio transport

`/api/realtime/audio` is a generic multi-client WebSocket contract. Authentication binds a server-generated ephemeral transport session to the JWT user. The protocol contains no Chrome tab, popup, or service-worker concepts and remains implementable by future Web, Mobile, and Desktop clients.

The local Extension default targets `ws://localhost:5221/api/realtime/audio`, matching the Development HTTP launch profile. Non-development instances require WSS and an explicitly configured exact browser-origin allowlist. Unpacked-extension IDs are not embedded in production configuration.

Control messages are versioned JSON; audio uses a compact binary header plus mono signed PCM16 little-endian payload. The API validates handshake order, format, version, message size, payload length, sequence continuity, monotonic elapsed time, and lifecycle transitions.

The receive loop rents one bounded buffer and awaits direct sink consumption before receiving another frame. `IRealtimeAudioSessionFactory` is an Application abstraction; Infrastructure implements a non-persistent validation sink that retains only counters and emits a safe completion summary. No audio reaches PostgreSQL or disk.

One connection currently remains on one API instance. The client contract exposes no process affinity, and future AWS/shared processing can replace the sink without rewriting clients. Distributed coordination is intentionally deferred.

## Configuration and secrets

Tracked configuration contains only non-sensitive defaults. Local database credentials and the JWT signing key use .NET User Secrets; deployed environments must use their secret-management facility. See `configuration.md`.

## Automated verification

The approved baseline contained 30 unit tests and 26 integration tests. EXT-001C adds protocol unit tests and authenticated WebSocket integration coverage for lifecycle, ordering, malformed/oversized input, and cleanup. Integration tests continue using `WebApplicationFactory`, Testcontainers, PostgreSQL 17 Alpine, an ephemeral database, and real migrations. Guards prevent targeting `translink_lite_dev`.

GitHub Actions runs on `ubuntu-latest` for pushes and pull requests to `main`, and manual dispatch. It resolves the SDK from `global.json`, restores, audits NuGet packages, builds Release, and runs the complete test suite. The first published baseline run succeeded.

## Current boundaries

Not yet implemented: AWS Transcribe/Translate/Polly, translated result delivery, durable realtime orchestration, production authentication UX, automatic reconnect/session resume, distributed infrastructure, production cloud deployment, external observability, calls, billing, organizations, or enterprise modules.
