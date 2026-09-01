# TransLink-Lite

TransLink-Lite is the active foundation for a realtime multilingual communication platform. The repository contains a hardened .NET 10 backend, a Chrome Extension with tab discovery and a local tab-audio capture foundation, automated PostgreSQL integration tests, and CI. Audio transport and AWS translation remain planned.

## Repository

```text
API/       .NET backend projects
APP/       Browser Extension and Web clients
Tests/     Unit and integration test projects
docs/      Product, architecture, configuration, ADR, and specification documents
.github/   GitHub Actions workflows
```

The root solution is `TransLink.Lite.slnx`. The required SDK is resolved through `global.json` (`10.0.300`, compatible patch servicing within its feature band).

## Backend quick start

Prerequisites:

- compatible .NET 10 SDK;
- PostgreSQL for local API runtime;
- Docker for integration tests.

Configure the PostgreSQL connection string and JWT signing key with .NET User Secrets as described in [`docs/configuration.md`](docs/configuration.md). Never commit local credentials.

From the repository root:

```bash
dotnet restore TransLink.Lite.slnx
dotnet build TransLink.Lite.slnx --no-restore --configuration Release
dotnet test TransLink.Lite.slnx --no-build --configuration Release
dotnet run --project API/TransLink.Lite.API
```

Swagger is available only in Development. Health probes are exposed at `/health`, `/health/live`, and `/health/ready`.

## Documentation

- [`docs/TRANSLINK-MASTER-v0.4.md`](docs/TRANSLINK-MASTER-v0.4.md): current product and architecture source of truth.
- [`docs/backend-architecture.md`](docs/backend-architecture.md): implemented backend architecture.
- [`docs/configuration.md`](docs/configuration.md): secure local configuration and runtime settings.
- [`docs/BASELINE-001-Repository-Backend-Hardening.md`](docs/BASELINE-001-Repository-Backend-Hardening.md): completed baseline specification and record.

`EXT-001A` is complete. `EXT-001B` implements the local tab-audio capture foundation and awaits manual Chrome validation. Transport-ready chunking, realtime transport, and AWS services remain later scoped work.
