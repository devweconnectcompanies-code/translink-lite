# TransLink Lite — Backend Architecture

## Overview

TransLink Lite is a .NET 10 Web API organized in four projects under `API/`. The API exposes user management and JWT-based authentication against a PostgreSQL database via Entity Framework Core.

## Solution structure

```
API/
├── TransLink.Lite.API/              # HTTP host, controllers, composition root
├── TransLink.Lite.Application/      # DTOs, auth contracts, JWT settings
├── TransLink.Lite.Domain/           # Domain entities
└── TransLink.Lite.Infrastructure/   # EF Core, migrations, auth implementations
```

## Project references

| Project | References |
|---------|------------|
| **API** | Application, Infrastructure |
| **Application** | Domain |
| **Infrastructure** | Application, Domain |
| **Domain** | *(none)* |

Dependency flow (inward):

```
API ──────────────┬──► Application ──► Domain
                  └──► Infrastructure ──► Application
                                    └──► Domain
```

The API does **not** reference Domain directly. Controllers currently use `AppDbContext` from Infrastructure for data access (pragmatic setup; application services can be introduced later).

All four projects have `<Nullable>enable</Nullable>`.

## Layers

### Domain (`TransLink.Lite.Domain`)

- **`User`** — `Id`, `FirstName`, `LastName`, `Email`, `PasswordHash`, `PreferredLanguage`, `CreatedAt`

### Application (`TransLink.Lite.Application`)

**User DTOs** (`Users/DTOs/`):

| Type | Purpose |
|------|---------|
| `CreateUserRequest` | Protected user creation (`POST /api/Users`) |
| `UserResponse` | User API responses (no password fields) |

**Auth DTOs** (`Auth/DTOs/`):

| Type | Purpose |
|------|---------|
| `RegisterRequest` | Public registration |
| `LoginRequest` | Public login |
| `AuthResponse` | Auth responses including JWT |

**Contracts** (`Auth/Interfaces/`):

| Interface | Methods |
|-----------|---------|
| `IPasswordHasher` | `HashPassword`, `VerifyPassword` |
| `IJwtTokenService` | `GenerateAccessToken(User)` |

**Configuration:**

- `JwtSettings` — bound from `Jwt` section in configuration (`Issuer`, `Audience`, `SecretKey`, `ExpirationMinutes`)

### Infrastructure (`TransLink.Lite.Infrastructure`)

| Component | Role |
|-----------|------|
| `AppDbContext` | EF Core `DbSet<User>` |
| `BCryptPasswordHasher` | `IPasswordHasher` via BCrypt.Net-Next |
| `JwtTokenService` | HS256 JWT generation |
| `Migrations/` | PostgreSQL schema history |

### API (`TransLink.Lite.API`)

**Controllers:**

| Controller | Route | Auth | Actions |
|------------|-------|------|---------|
| `AuthController` | `api/auth` | Public | `POST register`, `POST login` |
| `UsersController` | `api/Users` | `[Authorize]` | `GET`, `POST` |

**Minimal endpoint:**

| Route | Auth | Description |
|-------|------|-------------|
| `GET /health` | Public | Health check |

## Authentication

### Registration (`POST /api/auth/register`)

1. Reject duplicate email (`409 Conflict`)
2. Hash password with `IPasswordHasher`
3. Persist `User`
4. Return `AuthResponse` with JWT

### Login (`POST /api/auth/login`)

1. Load user by email
2. Verify password with `IPasswordHasher`
3. On failure → `401 Unauthorized`
4. Return `AuthResponse` with JWT

### Protected routes

`UsersController` requires a valid Bearer JWT. Swagger is configured with a Bearer security definition (OpenAPI 2 / Swashbuckle 10).

### JWT configuration

Loaded from `appsettings.Development.json` under `Jwt`:

```json
{
  "Jwt": {
    "Issuer": "TransLink.Lite",
    "Audience": "TransLink.Lite.Client",
    "SecretKey": "...",
    "ExpirationMinutes": 60
  }
}
```

`Program.cs` binds `JwtSettings`, validates at startup, and configures `JwtBearer` with the same issuer, audience, and signing key.

**Note:** JWT settings are defined in Development configuration only. Running outside Development requires the same `Jwt` section in the active configuration or startup will fail.

### Password hashing

`BCryptPasswordHasher` delegates to `BCrypt.Net.BCrypt.HashPassword` and `Verify`. Used by `AuthController` and `UsersController` when creating users.

## Dependency injection (`Program.cs`)

| Service | Lifetime | Implementation |
|---------|----------|----------------|
| `AppDbContext` | Scoped | EF Core + Npgsql |
| `JwtSettings` | Options | `Configure<JwtSettings>` from `Jwt` section |
| `IPasswordHasher` | Singleton | `BCryptPasswordHasher` |
| `IJwtTokenService` | Singleton | `JwtTokenService` |
| Authentication | — | JWT Bearer |
| Authorization | — | ASP.NET Core default |

Middleware order: `UseAuthentication` → `UseAuthorization` → `MapControllers`.

## API endpoints summary

| Method | Path | Auth | Request | Response |
|--------|------|------|---------|----------|
| `GET` | `/health` | — | — | `{ status, service, timestamp }` |
| `POST` | `/api/auth/register` | — | `RegisterRequest` | `AuthResponse` |
| `POST` | `/api/auth/login` | — | `LoginRequest` | `AuthResponse` |
| `GET` | `/api/Users` | Bearer | — | `List<UserResponse>` |
| `POST` | `/api/Users` | Bearer | `CreateUserRequest` | `UserResponse` |

## Controller conventions

All controller actions are **async** (`Task<ActionResult<...>>`) and use EF Core async APIs (`ToListAsync`, `SaveChangesAsync`, `AnyAsync`, `SingleOrDefaultAsync`).

## Database

- **Provider:** PostgreSQL (Npgsql)
- **Connection string:** `ConnectionStrings:DefaultConnection` in `appsettings.Development.json`
- **Migrations:** `API/TransLink.Lite.Infrastructure/Migrations/`

## NuGet packages (high level)

| Project | Notable packages |
|---------|------------------|
| API | `JwtBearer`, `Swashbuckle`, `EF Core Design`, `Npgsql` |
| Infrastructure | `BCrypt.Net-Next`, `System.IdentityModel.Tokens.Jwt`, `EF Core`, `Npgsql` |
| Application | *(project references only)* |
| Domain | *(none)* |

## Local development

```bash
cd API/TransLink.Lite.API
dotnet run
```

- HTTP: `http://localhost:5221` (see `Properties/launchSettings.json`)
- Swagger UI: enabled in Development
- Sample HTTP requests: `TransLink.Lite.API.http` (`GET /health`)

## Cleanup notes (maintenance)

Removed template artifacts:

- Empty `Class1.cs` placeholder classes (Application, Domain, Infrastructure)
- `TransLink.Lite.API.http` sample `weatherforecast` request (no WeatherForecast controller exists)

No `WeatherForecast` types remain in the solution. All Application DTOs are referenced by controllers.
