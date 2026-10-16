# Backlog

Scope is frozen against this list. A new idea goes here first; nothing is built mid-batch.
Each batch: schema + tests → screens → audit row in `docs/schema-ui-audit.md` → commit.

## Batch A — learner feedback (done; verified 2026-10-12: schema, screens, and domain tests all pass)

- [x] **Writing practice** (brief approved 2026-10-09): `writing-instructions`, `exam-writing`,
  `writing-review`; mock exam writing rows; path-detail writing task.
  Schema: `writing_practice` purpose (writing sections only, `ai_grading` only, no band),
  `Questions.MinWords/MaxWords`, draft state on `FreeResponses`, group `ResponseSeconds`,
  `ai_task` path item may carry `RefExamId`.
- [x] **Practice hints and recommendations**: hint on demand in `practice-take`,
  recommendations on `practice-summary`.
  Schema: `Questions.Hint`, hint-used flag on `PracticeAnswers`, `Recommendations.PracticeSessionId`.

## Screen-only (no schema change; does not block backend planning)

- [x] Learner complaint form — `support-report` + `support-history` (2026-10-10, UI-48/49)
- [x] Saved-questions bank screen — `saved-questions` (2026-10-10, UI-47)
- [x] Certificates explainer page — `certificates.html` (2026-10-10, UI-50)
- [x] Email-verification nudge on dashboard (2026-10-10, UI-51)
- [x] Admin batch import screen — `/admin/imports` (2026-10-16, ADR-0007)
- [ ] Question editor: inputs for question-type label and per-option rationale

## Batch B — backend groundwork (schema done 2026-10-12; screens pending)

Schema + tests (`domain14-ai-insights-payments.sql`): `Users.TimeZoneId`, `Orders.PaymentNo` +
`SEQ_PaymentNo`, gateways `payos`/`mock`, `PaymentEvents`, `AiInsights`, `Recommendations.InsightId`.

- [ ] Profile: time-zone picker (UI-52); every date/time renders in the user's zone
- [ ] Checkout / gateway-mock / payment-result / wallet / admin orders: VNPay/MoMo → payOS
  (VietQR transfer); drop the gateway choice at checkout (ADR-0003)
- [ ] AI insight panels on dashboard, practice-summary, exam-result, weakness-analysis,
  path-detail, each with loading / failed / stale states (ADR-0004)

## Decisions needed before backend planning

- [x] AI provider: Azure AI Speech for speaking, Claude via HH-tech proxy for grading and
  insights (ADR-0005)
- [x] Ingest format for real question content (ADR-0007)

## Later (decided against for now)

- Mentor role and self-service mentor area (admin manages mentors today)
- Real-time spoken conversation with an AI mentor (AI mentor = speaking practice attempt today)
- AI-authored learning-path steps (insights may only flag priorities today)
- Real VNPay / MoMo gateways
- Speaking practice enterprise improvements: pre-flight mic check, codec fallback, VAD, S3 storage (`docs/speaking-practice-improvements.md`)

## Out of scope by decision

Re-recording history, time-on-task tracking, video watch progress, stored admin alerts
(see `docs/schema-ui-audit.md` "Left open on purpose").
