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

## Startup behavior

The API fails fast with a safe configuration error when:

- `ConnectionStrings:DefaultConnection` is missing;
- `Jwt:Issuer` is missing;
- `Jwt:Audience` is missing;
- `Jwt:SecretKey` is missing or shorter than 32 bytes;
- `Jwt:ExpirationMinutes` is not greater than zero.

Validation errors identify the missing configuration key but never include its value.

## Credential rotation

The PostgreSQL password and JWT signing key that were previously committed must be treated as compromised. Their owner must rotate them manually. Rotating the JWT signing key invalidates tokens signed with the previous key.
