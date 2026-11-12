# Live dashboard change detection without a timestamp column

## Status

Accepted. Implements #286.

## Context

The admin dashboard must refresh itself when a paid order arrives, a complaint changes status, or a
refund is decided. The obvious mechanism is an `UpdatedAt` column on the mutable tables, or a
`rowversion`, and a poll on `MAX(UpdatedAt)`.

Neither exists, and adding one is expensive here. `Orders` and `Complaints` carry only insert-time and
transition-time stamps (`PaidAt`, `ResolvedAt`), and `Complaints.open -> reviewing` stamps nothing. There
is no `rowversion` column anywhere in the schema. An `UpdatedAt` would have to be maintained by a trigger
on the two hottest tables in the domain, which brings three codebase-specific costs: trigger nesting to
avoid self-recursion, a `HasTrigger(...)` declaration in the EF model mapping or saves fail on the
`OUTPUT` clause, and a column that must then be kept correct forever by every future writer.

`graphs`-level blast radius: `Orders` has 9 indexes, 8 constraints and one `AFTER UPDATE` trigger already.

## Decision

Change detection is a **revision** computed from counts and sums over already-indexed columns, not from a
timestamp. Every change the dashboard must reflect moves at least one of them: a paid order changes the
paid count, a complaint status change moves one bucket to another, a refund decision does the same.

No schema change, no trigger, no new column.

## Consequences

The revision's contract is a single equality test, so it is testable without HTTP and without a database
in the web-app tests.

The known limit is deliberate and must not be discovered as a bug: **an edit that changes neither a count
nor a sum is invisible to the revision.** This is correct for the dashboard, which shows only counts and
sums. It would be wrong for a surface showing free text, and the same mechanism must not be reused there
without revisiting this decision.

Because one dashboard alert is time-based rather than row-based — orders waiting for reconciliation over
30 minutes — the stream re-sends on a heartbeat even when the revision is unchanged. A revision that only
fires on row changes would leave that alert unable to appear on its own.
