# Database-first EF Core; the SQL script is the schema

`docs/database-schema.sql` is the single source of truth for the database. EF Core entities and the
`DbContext` are scaffolded from it, never the other way round, and there are no EF migrations. The
schema carries most of its invariants in SQL Server itself — 27 triggers, 9 views, 13 filtered unique
indexes, composite foreign keys for certificate isolation — and all of it is verified by
`docs/schema-tests/`. Code-first would have meant re-expressing that as raw SQL inside migrations and
losing the tested script; Dapper would have meant hand-writing mapping for 47 tables.

## Consequences

- A schema change is made in the SQL script first, tested with `run-all.sh`, then re-scaffolded.
- Every table with a trigger must be declared with `HasTrigger(...)` in the model, or EF's
  `OUTPUT`-based saves fail on SQL Server.
- Views are mapped as keyless, read-only entities. Derived aggregates (wallet balance, mentor rating,
  payout totals) are only ever read from them, never computed or stored by the app.
- Services check every precondition themselves before writing ("block before you charge"); a trigger
  or constraint error reaching the app is a bug, logged and shown as a generic failure.
- Tests run against real SQL Server (LocalDB), because in-memory and SQLite providers cannot run the
  triggers.
