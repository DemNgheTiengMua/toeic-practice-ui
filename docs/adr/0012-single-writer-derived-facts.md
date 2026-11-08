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

Nine facts are registered with a table and enforced:

| Fact | Owner | Table |
|---|---|---|
| Placement result | `AttemptEngine` | `PlacementTests` |
| Placement per-skill result | `AttemptEngine` | `PlacementSkillResults` |
| Attempt score and band | `AttemptEngine` | `ExamAttempts` |
| Step closure | `PathStepCompletion` | `LearnerPathSteps` |
| Learner per-skill state | `SkillStateWriter` | `LearnerSkillStates` |
| Stratum decision | `StratifiedAuditService` | `ReviewStrata` |
| Window synthesis | `PathWindowSynthesizer` | `LearnerPathWeeks` |
| Exam edition snapshot | `ExamEditionFreeze` | `ExamEditions` |
| Topic capability | `CapabilityWriter` | `UserCellMasteries` |

Topic capability was the last fact waiting on another ticket. #237 created `UserCellMasteries`, so the
registry's `awaiting_table` set is now empty and this table is asserted at **9 / 0 / 1**.

**Topic capability is a service-owned projection, and that is a deliberate exception.** Every other
enforced fact here is written by a service from rows that already hold the derived value. Per-cell
mastery is different: its value is a recency-weighted Beta-Binomial probability with a prior
(`p_m = (α0 + k·Σw·c) / (α0 + β0 + n)`, SRS PLC-67), which SQL Server cannot express as a computed
column because the weight is a power and the state is a function of *events*, not of the value. So
`MasteryService` is the writer — but the arithmetic it calls is a pure function in
`MasteryCellMachine`/`MasteryEstimate`, and **every read returns the stored row verbatim**. There is no
recomputation at read time; a reader that re-derived the state would be a second writer in disguise.

The `awaiting_table` set is empty as of #237. The registry still carries the column, and the CHECK that
makes "covered but names no table" unstorable still holds, so a later fact cannot be registered as
covered without a table to guard.

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
- `docs/schema-tests/domain27-single-writer.sql` asserts the registry, the four guards, the accepted
  cases and the refusals. `docs/schema-tests/prove-guard-single-writer.sh` removes each guard and
  checks the specific expected failure.
- Adding a fifth covered fact means: create the table, add the columns, write the trigger copying the
  shape here, register the fact with `Coverage='covered'`, and extend `domain27`.

## #211 added the fourth

`LearnerSkillStates` (D15) is one row per learner per skill, and unlike the other three its **whole row**
is the derived fact — there is no sitting-state or lifecycle column to carve out, so every INSERT
declares a writer. The `WrittenByService` column is nullable, matching the three tables that predate
per-write enforcement: the trigger refuses an undeclared write, the column does not.

`docs/schema-tests/domain34-skill-state.sql` owns this table's shape and guard, and
`prove-guard-skill-state.sh` proves all fifteen guards load-bearing. Writing that script found five
fixtures that a foreign key or the primary key was refusing before the CHECK under test ran — which is
why each shape refusal there uses its own learner.

## #212 added the fifth, and it is the placement's per-skill grain

`PlacementSkillResults` is registered as `placement_skill_result`, owned by the same `AttemptEngine`
that owns the certificate-level snapshot. It could have been argued to be "part of `placement_result`",
and that would have been wrong for a mechanical reason: `CoveredTable` names exactly one table, so
folding it in leaves this table guarded by nothing while the registry claims coverage of the placement.

Unlike the other four, **the whole row is the derived fact including its status**, so every insert
declares — a `grading` placeholder is still a derived statement ("a task is in the AI queue"). The row
also carries `CutScoreTableVersion`, resolved once by `PlacementResultWriter`, passed to the projection
and stored on the same row: a projection must be reproducible from the version it names (LVL-13), and
#222 had to resolve a version at read time precisely because `ExamAttempts` carries none.

Two further triggers express PLC-36's completion rule — `done` requires at least one `measured` or
`estimated_from_one_task` skill, or a recorded completion failure. A CHECK cannot see another table, so
this is a trigger by the shape of the rule. `domain37-placement-skill-result.sql` owns both, and the
five mutations that prove them load-bearing report `RED` rather than `NOT LOAD-BEARING`.
