# AI narratives are insights, not gradings

Two different AI jobs write to the database and they get separate tables. `AiGradings` scores **one**
free response against a rubric — it is paid, per-response, and retryable. `AiInsights` reads a
learner's own accuracy history and says what it means ("stronger in reading, Part 5 slipped"), with an
optional `Trend`. Insights are generated for practice sessions, attempts, paths and the dashboard —
including the free diagnostic loop, because a canned template cannot tell a learner which answers were
luck and which were competence, and that judgement is the product. Folding either into the other table
would make most columns nullable and lose the per-response retry semantics.

Weakness percentages are **not** an AI job: `vw_UserSectionAccuracyAll` and `vw_UserCategoryAccuracy`
compute them in SQL, free and deterministically. The model reads those numbers; it does not derive them.

## Consequences

- One AI call per learner activity, never per page view: `UX_AiInsights_Fingerprint` makes an insight
  reusable while its inputs are unchanged, so opening the dashboard repeatedly costs nothing.
- `Recommendations` now has two possible producers — `GradingId` (rubric) or `InsightId` (aggregate) —
  and `CK_Reco_OneProducer` forbids both at once.
- Nothing calls the model mid-attempt. `practice-take` reminders use the stored `Hint` and rules:
  an in-flight call would be slow and could leak answers.
- The learning path's steps stay template-owned. An insight may flag what to prioritise, but does not
  insert or reorder steps; AI-authored steps are backlog.
- `Model` is config-driven and recorded per row, so the model can change without a code change.
