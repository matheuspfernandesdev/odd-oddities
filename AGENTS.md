# AGENTS.md — Odd Oddities

## What this is

.NET 8 background worker that auto-generates Instagram posts (fact curiosities + AI images) and publishes via Meta Graph API. Runs on a VPS with Docker Compose.

## Architecture

Hexagonal (Ports & Adapters). Four projects:

| Project | Role |
|---|---|
| `OddOddities.Domain` | Entities, enums, value objects, port interfaces (zero dependencies) |
| `OddOddities.Application` | Use cases, pipeline steps, DI registration. Depends on Domain. |
| `OddOddities.Infrastructure` | Adapters (EF Core, MinIO, OpenRouter, Meta API, ImageSharp). Depends on Domain + Application. |
| `OddOddities.Worker` | Entry point (`Program.cs`), hosted service, health checks. Depends on all. |

Pipeline runs via `PipelineOrchestrator` → ordered `IPipelineStep` implementations (TextGeneration → ImageGeneration → Publication).

## Commands

```powershell
# Build
dotnet build OddOddities.slnx

# Test (single project)
dotnet test tests\OddOddities.UnitTests\OddOddities.UnitTests.csproj

# Format
dotnet format

# Run locally (requires Docker + PostgreSQL container + User Secrets)
dotnet run --project src\OddOddities.Worker

# Local PostgreSQL
docker compose -f docker-compose.dev.yml up -d
```

There is no lint or typecheck step beyond `dotnet build` (nullable enabled, warnings as errors not enforced globally).

## Configuration — critical gotcha

Config uses 3 layers with strict precedence (lowest to highest):

1. `appsettings.json` — defaults
2. `appsettings.Development.json` — dev overrides (non-sensitive only, safe for Git)
3. **User Secrets** — sensitive values (outside Git)
4. **Environment variables** — production (overrides everything)

**`ConnectionStrings:DefaultConnection` must be set at the ROOT level** (`ConnectionStrings:DefaultConnection`), not nested under `AppConfiguration`. The code calls `GetConnectionString()` which reads from root.

### Local dev secrets

All secrets via User Secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=odd_oddities;Username=odd_oddities;Password=odd_oddities_dev" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:MinIO:AccessKey" "<key>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:MinIO:SecretKey" "<secret>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:OpenRouter:ApiKey" "<key>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:Meta:AppId" "<id>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:Meta:AppSecret" "<secret>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:Meta:AccessToken" "<token>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:Meta:InstagramUserId" "<user-id>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:TokenEncryption:Key" "<32-char-key>" --project src\OddOddities.Worker
```

Verify: `dotnet user-secrets list --project src\OddOddities.Worker`

## EF Core migrations

Migrations live in `OddOddities.Infrastructure/Migrations/`. They are **auto-applied on startup** via `ApplyMigrationsHostedService`. No manual migration step needed.

When adding a new migration:

```powershell
dotnet ef migrations add <MigrationName> --project src\OddOddities.Infrastructure --startup-project src\OddOddities.Worker
```

## CI/CD

GitHub Actions workflow (`.github/workflows/deploy.yml`) — currently **manual trigger only** (`workflow_dispatch`). Pushes to `main` do NOT auto-deploy.

Deploy flow: SSH into VPS → `git clone`/`git pull` → write `.env` on the VPS (from GitHub Secrets/Variables) → `docker compose up -d --build`.

## Branching

Single branch (`main`). Direct commits. No PR flow.

## Testing

- **Stack:** xUnit + FluentAssertions + NSubstitute (in `OddOddities.UnitTests.csproj`)
- Unit tests cover model selection, text/image step fallback & budget, OpenRouter catalog parsing (see `tests/OddOddities.UnitTests/`)
- No integration tests or E2E
- Tests target Domain + Application + Infrastructure projects

## Project-specific conventions

- All config section names match `AppConfiguration` (see `AppConfiguration.SectionName`)
- OpenRouter preferred models: `AppConfiguration:OpenRouter:TextModelId` / `ImageModelId` (first in the fallback chain); fallback limits & budget: `AppConfiguration:ModelSelection` (defaults in code, optional in appsettings) — see `docs/adr/ADR-008-modelo-dinamico-fallback-custo.md`
- `MinIO.Endpoint` points to the VPS public URL (`https://s3.binaryten.com.br`) even in dev — MinIO is shared, not local
- Worker runs as a Kestrel web server (for `/health` endpoint) despite being a "worker"
- `SemaphoreSlim(1,1)` ensures single pipeline execution at a time
- Published content is always in English; internal code/logs in English
- No i18n/L10n

## Do NOT

- Never commit `.env`, User Secrets, or any credentials
- Never put secrets in `appsettings*.json` — use User Secrets locally, env vars in production
- Never change the `ConnectionStrings` nesting (root-level is required by `GetConnectionString()`)
- Never remove the `ApplyMigrationsHostedService` — migrations must auto-apply
