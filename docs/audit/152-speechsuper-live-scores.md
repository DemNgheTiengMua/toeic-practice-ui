# Live SpeechSuper verification — real audio, real scores

Date: 2026-11-02. Credentials renewed by the project owner.

## The run

`scripts/seed-manual-test.sh fresh --real-ai`, learner `learner@demo.local`, active certificate switched to IELTS via `PUT /api/learner/active-certificate`.

Started speaking practice exam 8 (`Cambridge IELTS 18 — Speaking Practice Test 1`), attempt 4, question 25:

> "Part 1: Do you work or are you a student? What do you enjoy most about your studies or job?"

Uploaded `speaking-sample-1e.mp3` (783 KB, a real spoken answer to exactly that question) as `audioFile`, submitted.

## Provider

```
Sending HTTP request POST https://api.speechsuper.com/speak.eval.pro
SpeechSuper HTTP status OK, body length 10485
```

## Result

| GradingId | Status | OverallScaled | BandCode |
|---|---|---|---|
| 1 | done | **13.40** | **B1** |

Per criterion:

| Criterion | Score | Max | Comment |
|---|---|---|---|
| FC | 3.20 | 9.00 | SpeechSuper measured 36/100 (36%). |
| LR | 3.40 | 9.00 | SpeechSuper measured 38/100 (38%). |
| GRA | 3.40 | 9.00 | SpeechSuper measured 38/100 (38%). |
| PRON | 3.40 | 9.00 | SpeechSuper measured 38/100 (38%). |

## What this closes

- **The whole speaking pipeline works end to end against the live provider**, on a real recording, on a non-TOEIC certificate.
- SpeechSuper reports percentages (36, 38, 38, 38 out of 100) and the grader normalises them onto the rubric's 9-point criteria: 36% → 3.20, 38% → 3.40. The `?? 6.0` default is gone — these are the provider's own numbers.
- The result lands in IELTS's own band table: 13.40/36 ≈ 37% → **B1**, not a TOEIC band.
- The percentage-vs-band-scale defect the review found (dividing percentages by 9.0) is fixed and this run proves the corrected path.

## Earlier run, for contrast

A silent 2-second clip on TOEIC (attempt 3, question 16, Read Aloud) produced `Status=done`, `OverallScaled=0.00`, `BandCode NULL`, criteria all `0.00` with comment "SpeechSuper measured 0/9 (0%)". A zero stays a zero and earns no band — the failure mode is honest at both ends.

## Remaining gap

None for Speaking grading itself. `SPEECHSUPER_CORE_TYPE` stays `speak.eval.pro` (the unscripted endpoint) per decision D18 — scripted Read Aloud questions are therefore also scored by the unscripted endpoint. Recorded as a deliberate deferral with the reason in `docs/STATE.md`.
