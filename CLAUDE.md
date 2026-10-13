# CLAUDE.md

Prototype UI for a multi-certificate TOEIC practice app. See `PRODUCT.md`
for product scope and `DESIGN.md` for the design system.

## Build, test, run

The backend is a .NET 10 solution (`ExtraEfficiency.slnx`): six layers —
`BusinessObjects` (domain shapes) ← `DataAccess` (EF Core, database-first from
`docs/database-schema.sql`) ← `Services` (use cases) ← `Contracts` (wire DTOs
plus the API client) — consumed by `WebAPI` (minimal API, port 5001) and
`WebMVC` (server-rendered site, port 5000, the API's only caller — ADR-0002).

```sh
dotnet build                                   # whole solution
dotnet test                                    # Services tests against LocalDB,
                                               # schema reloaded per run
sqlcmd -S "(localdb)\MSSQLLocalDB" -I -b -Q "IF DB_ID('ExtraEfficiency') IS NOT NULL DROP DATABASE [ExtraEfficiency]; CREATE DATABASE [ExtraEfficiency];"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -i docs/database-schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -i docs/seed-demo.sql
# First time only: the API refuses to boot without a JWT signing key.
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 32)" --project src/WebAPI
dotnet user-secrets set "Jwt:Issuer" "extra-efficient" --project src/WebAPI
dotnet user-secrets set "Jwt:Audience" "extra-efficient" --project src/WebAPI
dotnet run --project src/WebAPI                # http://localhost:5001
dotnet run --project src/WebMVC                # http://localhost:5000/certificates
```

Demo accounts from the seed: `learner@demo.local` / `Learner!1234` and
`admin@demo.local` / `Admin!1234`.

After a schema change: edit `docs/database-schema.sql`, run
`docs/schema-tests/run-all.sh`, then re-scaffold —
`dotnet ef dbcontext scaffold "<conn>" Microsoft.EntityFrameworkCore.SqlServer --project src/DataAccess --context CompanionDbContext --output-dir Scaffold --context-dir . --use-database-names --no-onconfiguring --data-annotations --force`
— and verify every trigger still has a `HasTrigger` in the generated context.

## Agent skills

### Issue tracker

Issues live as GitHub issues on `DemNgheTiengMua/toeic-practice-ui`, managed
with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage roles, each label named after its role. See
`docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` and `docs/adr/` at the repo root (created
lazily). See `docs/agents/domain.md`.

## Look it up before you write it

Before writing or changing anything where exact syntax, versions or
configuration keys matter, query the **context7 MCP** for that library's current
docs — do not write it from memory. Memory of a framework's API is often a
version or two stale, and a plausible-looking wrong option name costs more to
debug than the lookup costs to run.

This applies to EF Core (scaffolding, `HasTrigger`, keyless entities), ASP.NET
Core (JWT bearer, cookie auth, anti-forgery, `ProblemDetails`, background
services), the payOS API (create payment link, webhook signature, status
query), Azure AI Speech (pronunciation assessment), the Anthropic SDK
(structured outputs, model ids), `.csproj` / `appsettings` / CI config, and any
SQL Server feature we lean on. Same rule for a package version or a CLI flag:
check, then write.
