# Backlog

Scope is frozen against this list. A new idea goes here first; nothing is built mid-batch.
Each batch: schema + tests → screens → audit row in `docs/schema-ui-audit.md` → commit.

## Batch A — learner feedback (in progress)

- [ ] **Writing practice** (brief approved 2026-10-09): `writing-instructions`, `exam-writing`,
  `writing-review`; mock exam writing rows; path-detail writing task.
  Schema: `writing_practice` purpose (writing sections only, `ai_grading` only, no band),
  `Questions.MinWords/MaxWords`, draft state on `FreeResponses`, group `ResponseSeconds`,
  `ai_task` path item may carry `RefExamId`.
- [ ] **Practice hints and recommendations**: hint on demand in `practice-take`,
  recommendations on `practice-summary`.
  Schema: `Questions.Hint`, hint-used flag on `PracticeAnswers`, `Recommendations.PracticeSessionId`.

## Screen-only (no schema change; does not block backend planning)

- [x] Learner complaint form — `support-report` + `support-history` (2026-10-10, UI-48/49)
- [x] Saved-questions bank screen — `saved-questions` (2026-10-10, UI-47)
- [x] Certificates explainer page — `certificates.html` (2026-10-10, UI-50)
- [x] Email-verification nudge on dashboard (2026-10-10, UI-51)
- [ ] Admin batch import screen (schema has `ImportBatches` / `ImportLog`)
- [ ] Question editor: inputs for question-type label and per-option rationale

## Decisions needed before backend planning

- [ ] AI provider for transcription and grading
- [ ] Ingest format for real question content

## Out of scope by decision

Re-recording history, time-on-task tracking, video watch progress, stored admin alerts
(see `docs/schema-ui-audit.md` "Left open on purpose").
