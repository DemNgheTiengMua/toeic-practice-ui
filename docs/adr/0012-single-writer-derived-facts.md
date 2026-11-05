# One writer per derived fact, enforced in the database

## Status

Accepted. Implements #191 (D14, TRN-06, TRN-07).

## Context

A derived snapshot written beside its source is a confirmed production defect in this product, not a
hypothetical. `docs/research/placement-free-response-defect.md` records it: `AttemptEngine` wrote the
placement snapshot and `PlacementService` wrote the same row again unconditionally, so a placement
carrying a free response was marked `done` with a null band and the learner could not finish it. The
constraint refused the second row, which turned a two-writer bug into a live crash.

Agreeing "one writer" by convention is what allowed that. Three writers of the placement snapshot
existed at the time of this decision (`AttemptEngine`, `PlacementService.SubmitPlacementInternalAsync`,
`PlacementService.SyncPlacementAfterAttemptSubmissionAsync`) plus a fourth outside the services
(`AiGradingProcessor`, patching the snapshot after finalizing the attempt).

## Decision

Every derived-fact table carries a required writer column (`WrittenByService`) and a monotonic
`ProjectionVersion`, and a trigger refuses any write that declares a writer other than the fact's
registered owner. Owners live in a new `DerivedFactOwners` table, so the registry is queryable and a
test can assert its coverage rather than trusting prose.

**The writer identity is a column on the write, never `sp_set_session_context`.** PRN-01 states the
exception explicitly: SQL Server has no transaction-local setting, and session context survives commit
and rollback, so a name left in it would be read by the next transaction on that pooled connection and
would vouch for code that never declared itself.

**The declaration must be in the statement, not inherited from the row.** The trigger checks
`UPDATE(WrittenByService)` alongside the value. A second writer that changes only `TotalScore` leaves
the previous writer's name in place; a value-only check passes it. EF Core omits an unchanged column, so
an owner that saves the same row twice marks the property modified explicitly.

**The derived fact is a column set, not a row.** `ExamAttempts` is written constantly for sitting state;
only `TotalScore` and `BandCode` are derived. `LearnerPathSteps` is written when a step starts; only the
closure is derived. A guard that refused every write to these tables would be correct-looking and would
break the product.

## Coverage, stated honestly

Seven facts are registered. Three have a table today and are enforced:

| Fact | Owner | Table |
|---|---|---|
| Placement result | `AttemptEngine` | `PlacementTests` |
| Attempt score and band | `AttemptEngine` | `ExamAttempts` |
| Step closure | `PathStepCompletion` | `LearnerPathSteps` |

Three are waiting on a table another ticket creates, and the registry says `awaiting_table` so the gap
is data rather than a missing promise:

| Fact | Owner | Waiting on |
|---|---|---|
| Learner per-skill state | `SkillStateWriter` | #210, `LearnerSkillStates` |
| Topic capability | `CapabilityWriter` | #232, per-cell mastery |
| Window synthesis | `PathWindowSynthesizer` | #212, window rows |

One is exempt on purpose:

| Fact | Owner | Why exempt |
|---|---|---|
| Credit movement | `CreditLedger` | `CreditTransactions` is already append-only and immutable (`trg_CreditTransactions_AppendOnly`, `trg_CreditTransactions_NoRewrite`) |

SRS decision D-09 rejects adding a second mutation guard to the one table declared frozen. A second
mechanism on it would be a second writer of one fact — the exact defect this decision exists to
prevent. The two rules reconcile as: **a fact gets a guard if and only if it does not already have
one.** Credit movement has one; the registry records that rather than duplicating it.

## Consequences

- `PlacementService` no longer writes the snapshot at all. `AiGradingProcessor` no longer patches it.
  `FinalizeAiGradedAttemptAsync` closes it, so a placement carrying a free response reaches `done` when
  its grading settles rather than crashing at submit.
- Every fixture that wrote a derived column now declares a writer, including `docs/seed-demo.sql`. The
  seed stays idempotent because the guard is per row, not per table.
- `docs/schema-tests/domain27-single-writer.sql` asserts the registry, the three guards, the accepted
  cases and the refusals. `docs/schema-tests/prove-guard-single-writer.sh` removes each guard and
  checks the specific expected failure.
- Adding a fourth covered fact means: create the table, add the columns, write the trigger copying the
  shape here, register the fact with `Coverage='covered'`, and extend `domain27`.
