# #146 — Score scale is a skill property, `GetFallbackScale` deleted

**Status:** done, pushed. **Verified:** 1351 tests pass; schema tests no worse than baseline; browser walk on a live seed.

Two commits:

| Commit | What |
|---|---|
| `d1d222a` | The ticket: `Skills.MinScaledScore/MaxScaledScore/ScoreStep`, both `GetFallbackScale` copies deleted, one `ScoreScale` behaviour. |
| `ac27350` | A defect found during the browser walk: the preview endpoint answered 500 instead of 400 for an unconfigured skill. |

## What the ticket asked for, and what happened

**1. Scale as data.** Three NULLable columns on `Skills`, with CHECKs (`Min < Max`, `Step > 0`, non-negative). NULLable on purpose — see "the decision I made" below.

Backfilled from the existing `ScoreScales` rows, never invented. `MIN(ScaledScore)`, `MAX(ScaledScore)`, and the step is the largest of 10/5/2/1 that divides both the minimum and every consecutive gap:

```
IELTS     LIST/READ   20–90   step 10
TOEIC     LIST/READ    5–495  step 5
TOEIC_CAT LIST       200–495  step 5
TOEIC_CAT READ       200–850  step 5
TOEIC_SW  SPEA/WRIT   50–200  step 10
VSTEP     LIST/READ   20–90   step 5
```

Dividing the minimum matters: a seeded row of 100 snapped off a step-300 grid would have silently changed published scores.

**2. Both `GetFallbackScale` copies gone.** `AttemptEngine.Scoring.cs` and `ScoreConversionService.Preview.cs`. No `_ =>` arm returning a range survives.

**3. One `ScoreScale` behaviour.** `ProjectOntoScale` interpolates between the two bracketing rows and rounds onto the skill's own grid. The int overload's "closest row" behaviour is gone — that's the one that returned 0 for every interpolated case.

## Proven in the browser, on a live seed

Same 50/100 raw score, three certificates:

| Cert | Own scale | Result |
|---|---|---|
| TOEIC | 5–495 step 5 | **250** |
| IELTS | 20–90 step 10 | **50** |
| TOEIC_SW | 50–200 step 10 | **200** |
| unconfigured skill | — | **400** `SCORE_CONVERSION_INVALID`, naming the skill |

Before this ticket all three would have been scored on the TOEIC range, because the certificate's name decided the scale.

## The defect found while walking it

`ac27350`. The service correctly threw `ScoreConversionException` for an unconfigured skill, but the preview endpoint had no `catch`, so the admin authoring a certificate got an opaque 500 that named neither the skill nor the missing columns. Now a 400 carrying the service's own message. Two tests drive the real endpoint group, not a reimplementation of its catch, and I verified both fail with the catch removed.

## The decision I made that the handoff did not cover

**Columns are NULLable.** Making them NOT NULL would break 12 schema-test files and 16 `INSERT INTO Skills` statements in `docs/seed-demo.sql` in one commit. The "missing config is an error" rule is enforced at *call time* instead: a skill with NULL parameters refuses to score and names itself. Enforcing NOT NULL belongs in a follow-up once authoring writes real values.

I also verified the subagent's claim that six schema tests already fail on `main`, rather than taking it. Checked out the parent commit in a separate worktree and ran each domain file individually:

```
baseline (37fc74e): 6 failures — domain04, 05, 06, 07, 08, 15
after    (d1d222a): 6 failures — identical set
```

No regression. The new assertions in `domain15` were moved to the top so they execute ahead of the pre-existing failure, and all four print their expected accept/reject lines.

## Known gap, deliberately left

Two browser-side calculators still hardcode TOEIC's `495`: `wwwroot/js/admin/certificate-wizard.js:603` and `wwwroot/js/admin/score-conversion.js:18`. They are outside `src/Services`, so the ticket's grep does not cover them, and both files are rewritten or deleted by #147. Until then an admin's client-side preview will disagree with the server for any non-TOEIC certificate.

---

**Skipped:** did not run a full placement sitting end-to-end in the browser (the band rows and the preview path are verified directly instead); did not fix the six pre-existing schema-test failures — they predate this work and one of them looks like a trigger rejecting a legitimate row, which needs its own ticket. **Risk:** a certificate authored with the three columns left NULL now refuses to score instead of guessing, which is the intent, but operators must set them.
