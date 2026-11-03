# Confirmed defect: a placement carrying free responses cannot be completed

Found by a code-reading review, then **confirmed at runtime** by the two tests in
`tests/Services.Tests/PlacementFreeResponseProbeTests.cs`. Relevant to the placement rebuild because
it is a live example of the rule "placement must produce a band" being asserted in one place and
bypassed in another.

## The chain

1. Exam 13 (`Diagnostic Benchmark 45M`, purpose `placement`) carries **two `free_response` questions**
   in sections W2 and W3. Verified against the database.
2. On submit, a free response moves to `pending_ai`, so `AttemptEngine` sets the attempt to
   `submitted` rather than `graded`, and leaves `BandCode` null. That part is correct.
3. `AttemptEngine` also guards the placement snapshot: the `PlacementTests` write only happens
   `if (!hasPendingMockResponses)`.
4. **`PlacementService.SubmitPlacementInternalAsync` writes that same row again, unconditionally:**
   `Status = 'done'`, `TotalScore = grading.TotalScore`, `ResultBandCode = grading.BandCode` — where
   `grading.BandCode` is null for a pending attempt.
5. `CK_Placement_DoneHasResult` requires `ResultBandCode IS NOT NULL` when `Status = 'done'`.

So the second writer produces a row the constraint must reject. The constraint does reject it — the
test proves that — which means the failure surfaces as an exception during submit rather than as a
silent bad row. Either way, **the learner cannot finish that placement.**

## Why this matters for the rebuild

The rebuild makes placement cover four skills, which means free responses become the *normal* case
rather than an accident of one seeded exam. If the new design keeps "one write path for the attempt,
a second for the placement snapshot", it will hit this immediately and at scale.

The rule the rebuild should carry: **a placement snapshot is derived from the attempt, never written
beside it.** One writer. If the attempt is not yet graded, the placement is not yet done — and the UI
says "still grading" rather than forcing the row.

## Not yet done

The defect is not fixed. It is recorded here because fixing it belongs to the placement rebuild
(where the scoring and result model changes anyway), and because a fix now would be thrown away.
If the rebuild slips, this needs its own ticket: it is a live crash on a seeded exam.
