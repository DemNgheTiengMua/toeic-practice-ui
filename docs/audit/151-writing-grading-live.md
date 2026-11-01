# Live Claude verification — the writing prompt and what the model returns

Date: 2026-11-02. Model `claude-sonnet-3.7` via `https://hhtechapi.net`, key from `.env`.

This is the live evidence for **#151**. Unlike #152's SpeechSuper run, the subject here is the prompt
itself: the ticket was about what the grader is *told* and what it is *allowed to return*, so a fake
grader would prove nothing. The harness is `.scratch/prompt-probe/Program.cs` — it builds a real
`ClaudeAiGrader` over the real `ClaudeClient`, calls `GradeAsync`, and prints both the prompt and the
raw response. Full transcript: `docs/audit/151-live-prompt-and-response.txt`.

## What the prompt now carries

Taken verbatim from the captured transcript:

```
[CHECKLIST - already computed from the text. Do not recompute it, and do not award a score that contradicts it.]
- wordCount: 201
- meetsLength: false (minimum 250, maximum 350)
- usesAllKeywords: true
- addressesPrompt: answer this one yourself in the JSON below.

[CRITERIA TO EVALUATE]
- ID: 1, Code: TR, Name: Task Response, MaxScore: 9, Weight: 1
  Rubric: 9: fully addresses all parts, fully developed position. 7: addresses all parts, clear
  position. 5: addresses the task only partially. 3: does not adequately address the task. 0: does
  not attempt the task.
- ID: 2, Code: CC, ...   (same band descriptors for all four IELTS criteria)
```

Before #151 this section was one line per criterion — `ID: 1, Code: TR, Name: Task Response,
MaxScore: 9, Weight: 1` — with no rubric text anywhere in the request.

## The real answer to a 201-word essay (failing the 250-word minimum)

Criterion marks `6.0 / 7.0 / 7.0 / 7.0` out of 9, each with a note that cites the essay. The model
produced **five** located errors with corrections, a priority fix and a rewrite. Two of them:

| Category | Original | Correction |
|---|---|---|
| grammar | `Some people think that universities should prepare students for work, while others believe academic knowledge matters more.` | `... while others believe that academic knowledge matters more.` |
| grammar | `because that is the order in which both are learned` | `because that is the order in which both can best be learned` |

`priorityFix`: *"Expand the essay to at least 250 words by developing each body paragraph with a
concrete example, so the clear position and structure already present are fully supported."*

`modelRewrite`: a rewritten version of the sentence the model judged weakest.

## The acceptance case: three nonsense words

Same prompt, `ResponseText = "dog run fast"`. The model returned:

```json
{ "checklist": { "addressesPrompt": false }, "scores": [], ... }
```

`GradeAsync` reports `success=False`, reason *"The response does not answer the task, so it cannot be
scored. Write an answer that addresses the prompt and grade it again."*, and **zero scores**. Nothing
is stored, so the review page has no number to print and the pending page shows the retry control.

### The finding that changed the code

On an earlier run of the same three words the model answered `addressesPrompt: false` **and still
returned four criterion marks**. That is the defect in a different coat: a fluent-looking number for
an answer to a question nobody asked. The checklist therefore decides before the marks are read —
`addressesPrompt: false` fails the grading whatever the `scores` array contains. Pinned by
`A_refusal_to_score_an_off_task_answer_is_reported_not_scored`, which supplies four marks alongside
`addressesPrompt: false` and asserts none survive.

## What this does not prove

- The band. `AiGradings.BandCode` is written by the worker from the score this rubric produces
  (#148's path, unchanged here), and the live harness calls the grader directly, not through the
  worker. The band coverage is `AiGradingProcessorTests.ProcessPendingGradings_writes_the_certificates_own_band_for_a_writing_practice`
  and `The_same_rubric_score_finds_its_band_in_each_certificates_own_table`, against the real schema.
- The browser walk. See the screenshots listed in the PR.
