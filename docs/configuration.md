# Local configuration

TransLink Lite does not store database credentials or JWT signing keys in tracked configuration files.

Run the commands in this document from the repository root unless a command specifies otherwise.

## .NET SDK and build

The backend targets .NET 10. The repository-level `global.json` selects SDK `10.0.300` and permits compatible patch servicing only within the `10.0.3xx` feature band. Preview SDKs are not accepted.

Install a compatible .NET 10 SDK, then verify resolution from the current Git root:

```bash
dotnet --version
dotnet --info
```

Restore and build the backend with:

```bash
dotnet restore TransLink.Lite.slnx
dotnet build TransLink.Lite.slnx --no-restore --configuration Release
```

## Required sensitive configuration

The API requires these values at startup:

| Purpose | Environment variable | .NET User Secrets key |
|---|---|---|
| PostgreSQL connection string | `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` |
| JWT signing key | `Jwt__SecretKey` | `Jwt:SecretKey` |

The JWT signing key must contain at least 32 bytes. Never reuse the example placeholders as real values.

## Configure development with .NET User Secrets

The API project has a `UserSecretsId`, so each developer can store local values outside the repository:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<YOUR_LOCAL_CONNECTION_STRING>" --project API/TransLink.Lite.API
dotnet user-secrets set "Jwt:SecretKey" "<YOUR_LOCAL_JWT_SECRET>" --project API/TransLink.Lite.API
```

List the configured keys without copying their values into tickets, logs, or documentation:

```bash
dotnet user-secrets list --project API/TransLink.Lite.API
```

Remove a local value when it is no longer needed:

```bash
dotnet user-secrets remove "ConnectionStrings:DefaultConnection" --project API/TransLink.Lite.API
dotnet user-secrets remove "Jwt:SecretKey" --project API/TransLink.Lite.API
```

## Configure with environment variables

CI and deployed environments should provide:

```text
ConnectionStrings__DefaultConnection=<YOUR_ENVIRONMENT_CONNECTION_STRING>
Jwt__SecretKey=<YOUR_ENVIRONMENT_JWT_SECRET>
```

Environment-specific values must come from the environment's secret-management facility. Do not commit `.env` files or local `appsettings` files.

## Non-sensitive JWT settings

The tracked `appsettings.json` file contains the JWT issuer, audience, and token lifetime. Environment variables may override them when an environment needs different non-sensitive values:

```text
Jwt__Issuer
Jwt__Audience
Jwt__ExpirationMinutes
```

## Authentication rate limiting

Public registration and login requests share a fixed-window limit per client IP. The default policy allows 10 requests per 60 seconds and does not queue excess requests. Rejected requests receive HTTP `429 Too Many Requests`.

The non-sensitive settings can be tuned through configuration or environment variables:

```text
RateLimiting__Authentication__PermitLimit
RateLimiting__Authentication__WindowSeconds
RateLimiting__Authentication__QueueLimit
```

This limiter stores counters in the API process. It is suitable for the current single-instance baseline, but it does not provide a global limit across multiple backend instances. A trusted proxy configuration and a distributed rate-limiting strategy must be introduced before horizontal deployment.

## Health endpoints

The API exposes anonymous, minimal health probes:

| Endpoint | Purpose | Dependencies |
|---|---|---|
| `/health` | Temporary compatibility alias for liveness | Process only |
| `/health/live` | Confirms that the API process can respond | Process only |
| `/health/ready` | Confirms that the instance can receive application traffic | PostgreSQL |

Healthy checks return HTTP `200`. Readiness returns HTTP `503` for both `Degraded` and `Unhealthy` states because PostgreSQL is currently a critical dependency. Responses contain only the aggregate health status and never expose connection or exception details.

In a future AWS deployment, container/process liveness should use `/health/live`, while load balancer target health and traffic readiness should use `/health/ready`.

## Realtime audio transport

Safe transport defaults are tracked under `RealtimeAudio`:

| Key | Default | Purpose |
|---|---:|---|
| `ProtocolVersion` | `2` | Required control/binary protocol version |
| `MaxBinaryFrameBytes` | `65560` | Maximum complete binary WebSocket message |
| `MaxControlMessageBytes` | `4096` | Maximum complete JSON control message |
| `ChunkDurationMs` | `150` | Required client PCM chunk duration |
| `HandshakeTimeoutSeconds` | `10` | Time allowed for `session.start` |
| `IdleTimeoutSeconds` | `15` | Maximum interval without a client message |
| `MaxProtocolViolations` | `1` | Violations allowed before closing |
| `AllowedOrigins` | empty in Development | Exact browser-origin allowlist; required outside Development |

Numeric settings are startup-validated. Non-development streaming requires HTTPS/WSS and at least one exact allowed origin. Development accepts an empty list because unpacked extension IDs vary, but an exact local ID can be configured without tracking it:

```bash
dotnet user-secrets set "RealtimeAudio:AllowedOrigins:0" "chrome-extension://<UNPACKED_EXTENSION_ID>" --project API/TransLink.Lite.API
```

Every connection still requires JWT, and native clients may omit `Origin`. Production deployments must supply their approved browser origins through deployment configuration; no developer extension ID belongs in tracked defaults.

For local Extension testing, obtain a short-lived token through the existing register/login endpoint and set it only in unpacked-extension local storage:

```javascript
chrome.storage.local.set({ developmentAccessToken: "<DEVELOPMENT_JWT>" })
chrome.storage.local.remove("realtimeEndpoint") // uses ws://localhost:5221/api/realtime/audio
```

Start that matching API profile with `dotnet run --project API/TransLink.Lite.API --launch-profile http`. Custom and production endpoints can be set with `realtimeEndpoint`, but credentials, query strings, and fragments are rejected and production must use `wss://`.

Remove it after testing:

```javascript
chrome.storage.local.remove("developmentAccessToken")
```

Never commit, log, or paste a real token into documentation. Full client login, production token storage, and refresh are deferred.

## AWS Transcribe Streaming

`AwsTranscribe` contains only non-secret operational configuration: explicit `Region`, partial-result stabilization, bounded audio/transcript capacities, write/start/completion timeouts, and maximum session duration. Environment overrides use keys such as `AwsTranscribe__Region`. Startup rejects missing or unsafe values.

Credentials are resolved only through the standard AWS SDK credential provider chain. Developers may select a local AWS CLI profile or use temporary environment credentials; deployed workloads should use a managed IAM role. Never place AWS credentials in tracked configuration, client storage, test fixtures, or documentation. See `SPEC/EXT-001D-AWS-Transcribe-Streaming-Foundation.md` for least-privilege IAM and manual validation.

## Startup behavior

The API fails fast with a safe configuration error when:

- `ConnectionStrings:DefaultConnection` is missing;
- `Jwt:Issuer` is missing;
- `Jwt:Audience` is missing;
- `Jwt:SecretKey` is missing or shorter than 32 bytes;
- `Jwt:ExpirationMinutes` is not greater than zero.
- `RateLimiting:Authentication:PermitLimit` is not greater than zero;
- `RateLimiting:Authentication:WindowSeconds` is not greater than zero;
- `RateLimiting:Authentication:QueueLimit` is negative.
- realtime protocol version is not `2`;
- realtime frame/control limits, chunk duration, timeouts, or violation limit fall outside their documented ranges.
- `RealtimeAudio:AllowedOrigins` is empty outside Development or IntegrationTesting.
- `AwsTranscribe` region, bounded capacities, stabilization, timeouts, or maximum session duration are invalid.

Validation errors identify the missing configuration key but never include its value.

## Credential rotation record

The PostgreSQL password and JWT signing key exposed before the baseline were rotated during `BASELINE-001B`. They are no longer present in tracked current-source configuration. The historical exposure remains in Git history; removing it from existing objects and clones requires a separately approved history-rewrite procedure.

Future rotation must occur in the owning database and deployment systems first, followed by User Secrets or the environment's secret manager. Rotating the JWT signing key invalidates tokens signed with the previous key.
