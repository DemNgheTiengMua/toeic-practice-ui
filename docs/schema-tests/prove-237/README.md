# #237 mutation proofs

Two scripts that prove each mastery guard is load-bearing: the mutation is applied, the relevant tests
are run, red is recorded, and the file is restored **from the in-memory original** (never
`git checkout`, which would discard uncommitted work — AGENTS.md §8).

Run both from the repository root.

```sh
python docs/schema-tests/prove-237/prove-guard-237-machine.py count-half-of-mastered-gate
python docs/schema-tests/prove-237/prove-guard-237-schema.py  no-observation-minimum
```

Omit the argument to run the first mutation in the table.

## `prove-guard-237-machine.py`

Mutates `MasteryCellMachine.cs` / `MasteryService.cs`, runs
`dotnet test tests/Services.Tests --filter FullyQualifiedName~Mastery`.

| Mutation | What it removes | Tests that must go red |
|---|---|---|
| `count-half-of-mastered-gate` | the observation minimum from the mastered gate | `The_mastered_state_needs_the_probability_and_the_count_together`, `Threshold_reached_with_the_count_short_does_not_master_the_cell` |
| `counter-not-reset-by-correct` | the reset of `ConsecutiveWrong` on a correct answer | `Wrong_then_correct_then_wrong_is_not_a_lapse` (machine and service) |
| `mock-exception-is-any-mock` | the at-or-above-level and same-skill scoping | `A_below_level_mock_section_does_not_save_the_cell`, the another-skill case, the predating case |
| `decay-fires-on-never-mastered` | the `State != Mastered` guard in `InactivityTick` | `Decay_never_fires_on_a_cell_that_was_never_mastered` |
| `mastered-arm-consults-the-estimate` | the rule that a mastered cell holds unless it lapses | six lapse/review-window tests |
| `entering-mastered-from-untested-allowed` | the refusal to master an unmeasured cell | `Mastered_is_never_reached_from_untested_without_answers` |

## `prove-guard-237-recompute.py`

Restores the pre-#237 body of `LearningPathService.LoadCellMasteryAsync` — which counted answers and
applied the legacy 85%/10 thresholds — and confirms that

`DynamicPathGenerationTests.A_stored_mastered_cell_sizes_its_step_short_where_a_recomputed_percentage_sees_nothing`

goes red. That test is the TRN-13 identity-5 guard: the cell is stored mastered with **no**
`PracticeAnswers` rows, which is the shape after session pruning. The old reader sees nothing and sizes
the step `developing`; the stored fact sizes it `mastered`.

## `prove-guard-237-schema.py`

Mutates `docs/schema-changes-237.sql`, runs `domain45-mastery.sql` on a scratch catalog.

| Mutation | What it removes | Case that must abort |
|---|---|---|
| `no-state-transition-guard` | the transition table | `untested -> mastered` |
| `no-mastered-gate` | both halves of the mastered gate | eight answers at a clear estimate |
| `no-observation-minimum` | only the count half | **eleven** perfect answers (p = 0.867) |
| `no-single-writer-guard` | the ownership refusal | a write declaring `PracticeService` |

**The eleven, not the four.** Four perfect answers give `(2+4)/(4+4) = 0.75`, below the 0.80 cut, so
they are refused by the threshold and the count is never consulted. A fixture built on four stays green
with the count deleted — the first draft of `domain45` used four and the mutation proof caught it.
