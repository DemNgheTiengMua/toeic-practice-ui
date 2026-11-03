# Audit: is the learning-path brief still complete and consistent?

> **Answered 2026-11-03.** This audit asked whether the path half was ready and found one real
> conflict. The owner chose to resolve it by specifying **both branches**, so the deliverable is now
> path-rebuild-brief.md - which asks the agent to state which branch it assumed rather than waiting
> for the choice. This file is kept as the evidence behind that brief.

Checked against `docs/research/placement-practice-rebuild-brief.md` (written 2026-11-03) and the
decisions taken since, before writing the placement brief. This is the check that the two do not
contradict each other.

**Verdict: the path content of the old brief is still correct, but three things will break when
placement changes, and one assumption in the old brief is now wrong.**

---

## 1. What is still true

The old brief's description of how a path is generated is accurate and can be kept:

- Steps come from the learner's weakest **sections**, ranked by `vw_UserSectionAccuracyAll`, plus the
  goal's `WeeklyStudyHours`. Two learners with the same band pair get different content.
- Four weeks, 3–5 steps per week, 25–45 minutes per step, with the minute ceiling documented.
- `mock_exam` and `lesson` steps close through one writer, `PathStepCompletion`.
- Feasibility is computed from band cumulative hours, never from an LLM.
- One session is ten questions with at most four previously seen.
- The AI writes narrative only; it never chooses a step, a question or a score.

None of that changes because placement changes.

## 2. What breaks, in order of severity

### 2.1 The path reads a single band. The new placement produces four.

`LearningPathService.Generation.cs:55` takes `latestPlacement.ResultBandCode` — one scalar — as the
starting band for the whole path. The rebuild returns **four per-skill CEFR values plus one overall**.
Nothing in the path understands a per-skill band; it greps two `SkillId` references for module content
and none for capability.

Consequence: unless the placement brief says otherwise, the path will keep working by reading only the
overall value — and a learner who is Reading B2 / Speaking A2 gets a path pitched at A2 for everything,
or B2 for everything. Either way the strongest signal the product has just became unusable.

**This is the one real conflict between the two briefs and must be settled in the placement brief.**

### 2.2 Selection is by section, but the new levels are per question.

The path picks a step by *section*. Placement and the new pool work at *question level* with a CEFR
label. A step that says "practise P5" has no level of its own, so it cannot be matched to "this learner
needs B1 grammar". The old brief already flagged that topic-level selection is unimplemented; the
placement rebuild makes the gap sharper, because now there *is* a per-question level to use and still
no seam that consumes it.

### 2.3 Session size is fixed at ten, regardless of how far a learner is from their target.

Ten questions is the right default. It is wrong as a constant when the learner is A2 targeting B2 with
an exam in three weeks — the path cannot vary intensity because the session size cannot vary.

## 3. What is now wrong in the old brief

**The "measured state" section is stale.** It reports 116 questions, 0 at difficulty 5, three of
fifteen topic categories populated, five sections with no questions, and lessons with no level column.
Those numbers were true on 2026-11-03 and will stop being true the moment the new pool lands, because
the placement rebuild replaces the level model entirely (`DifficultyLevel` 1–5 becomes CEFR).

The brief should not be edited to chase the numbers; it should stop presenting them as the current
state and point at the placement brief as the source of truth about content. Otherwise a reader will
soon be auditing against numbers that describe the old system.

## 4. What the old brief got right and should be repeated in the placement brief

Three rules earned their place and apply equally to placement:

1. **Do not invent numbers.** The old brief's strongest section is the one that refuses to pick item
   counts without arithmetic. The placement brief must refuse the same way.
2. **A band is not a CEFR level.** The old brief warns that TOEIC 785–940 is called "B2" inside the
   product but that this is an internal label. The new placement now makes CEFR the actual unit, which
   turns this warning into a design constraint: when the product says B2 from placement, it must be
   CEFR B2, not a band renamed.
3. **Cross-certificate comparison must be justified or avoided.** The new design avoids it by scoring
   on the CEFR axis rather than each certificate's own scale — which is the resolution, not a dodge.

---

## 5. Decisions to carry forward into the path work

Recorded here so the placement brief does not need to restate them:

| Decision | Status |
|---|---|
| Path starting point comes from placement | Still true, but **now ambiguous** — see §2.1 |
| Two learners with the same band pair differ by weakest sections | Still true |
| Path practice selects by section, not topic | Still true, and now visibly a gap |
| Session size ten, at most four seen | Still true |
| AI never chooses content | Still true, and must be restated for placement |

---

## 6. Recommendation

The placement brief is written first, because it decides what a band *is* — and the path cannot be
specified until that is settled. When it is, the path needs one follow-up decision that this audit
cannot make: **does a path follow the overall CEFR, or does it become per-skill?**

That question belongs in the placement brief as an explicit open item, because whichever way it goes
changes the path schema, not just the path logic.
