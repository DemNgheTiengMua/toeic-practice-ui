# The pre-campaign pass

Run before taking an audit campaign, so the images describe a build that is known good rather than a
build that happened to draw.

```sh
# 1. Schema harness — every constraint, trigger and view, one fresh database per file.
"C:\Program Files\Git\bin\bash.exe" -c "bash docs/schema-tests/run-all.sh"

# 2. Unit and integration suites.
dotnet test tests/WebMVC.Tests  --nologo      # ~1040 tests, ~20 s
dotnet test tests/WebAPI.Tests  --nologo      # ~125 tests, ~10 s
dotnet test tests/Services.Tests --nologo     # ~1985 tests, ~25 min (one fresh database per test)

# 3. The apps, on a freshly seeded database.
"C:\Program Files\Git\bin\bash.exe" -c "bash scripts/seed-manual-test.sh fresh"

# 4. The campaign. Both servers must be up; the run overwrites the images rather than adding to them.
E2E_BASE_URL=http://localhost:5000 \
  dotnet test tests/WebMVC.E2ETests --filter "FullyQualifiedName~FullAuditCampaignTests" --nologo
```

## Why the order matters

`Services.Tests` builds its own database from `database-schema.sql` for every test, so it is the only
suite that proves the schema file is executable — a table that is not in it does not exist (ADR-0001).
Running it after a schema change and before the campaign means a green campaign is not standing on a
schema the product does not actually ship.

`seed-manual-test.sh fresh` drops and reloads the everyday `ExtraEfficiency` database. The campaign
against a stale database photographs the previous build's data.

## Two traps

- **A `.cshtml` edit needs `-t:Rebuild`.** Otherwise the stale compiled Razor passes assertions the
  source would fail.
- **A run reporting fewer tests than the previous one is usually a stale `testhost`, not a real
  result.** Kill it, `dotnet build-server shutdown`, rebuild.
