# CLAUDE.md

Prototype UI for a multi-certificate TOEIC practice app. See `PRODUCT.md`
for product scope and `DESIGN.md` for the design system.

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
