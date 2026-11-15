# Remediation plan — UI/UX audit 2026-11-15

Source of findings: `docs/audit/ui-ux-live-audit-2026-11-15.md` (29 findings, browser-verified) and
`docs/audit/ui-ux-heroi-audit-2026-11-14.md` (HeroUI coverage). Evidence: `docs/audit/live/` (32 shots).

Status: plan only. No code changed, no tickets filed yet.

---

## 0. Decisions locked during grilling

| # | Decision |
|---|---|
| D1 | Fix all 29 findings. Each is classified fix / superseded-by-rebuild / fix-temporarily — **none is dropped**. Verified: no audited page is deleted by #309–#314 (the only deletions the spec names are `AdminAuditBatchPage.tsx`, already gone, and two tabs inside `CertificatesStudioPage.tsx`) |
| D2 | Evidence tier per finding type. **Behaviour** → failing test first. **Display** → before/after screenshot in `docs/audit/live/`. **All** → `dotnet test` + `npm test` twice at the end |
| D3 | Deliverable = plan + ticket list. No code this session |
| D4 | A **weakest topic/section** may only be named when the cell reaches its `observationFloor` (12 items listening/reading, 3 tasks speaking/writing). Below it the UI says "not enough evidence". Turns `observationFloor` from an unread field into the rule |
| D5 | Placement deadline is labelled **"Session expires"**, styled quietly, and only shown when under 1 hour. Auto-submit at zero is retained |
| D6 | Mock exam with no per-skill rows gets an explicit empty state **and** the seed is extended so the breakdown is demonstrable |
| D7 | English in repo docs; Vietnamese summary in chat |
| D8 | `GoalFeasibilityCalculator.MaxWeeklyHours` (25) becomes a **verdict** input, not an input rejection. `POST /api/learner/goal` accepts the pace; `POST /learning-path/generate` returns 422 for `Unrealistic` (it already does) |
| D9 | The client learns the bound from a DTO, never from a literal. Default pace becomes **7 h/week** (the only sourced default: `Language Exam App Architecture Research.md:278`); no hard floor above 0, because a learner who truly has 2 h/week must still be able to record a goal and see it judged |
| D10 | `suggestedWeeklyHours` gets rendered. When it exceeds 25 it is replaced by "no sustainable pace — earliest feasible date is X" (that date is already computed) |
| D11 | Contract drift is fixed by generating TS from OpenAPI (`ADR-0015:16` already mandates `openapi-typescript`; `/openapi/v1.json` is already parsed by `tests/WebAPI.Tests/OpenApiDocumentationTests.cs:44`), but only the 4 audited DTOs are pinned now. `api.ts` is not wholesale replaced |
| D12 | Dead surface from the eradicate wave is deleted now: 6 calibration/audit DTOs in `api.ts` + `studioApi.getIngestionBatches()` (calls a 404) |
| D13 | Findings already covered by #324–#328's "shell/headers/cards/tables lack defensive styling" are folded into that work, not duplicated |
| D14 | `docs/audit/remaining.md` is corrected in the first PR (it claims #309–#314 are open; GitHub says closed) |
| D15 | Group by **the files touched**, not by severity — P0 and P1 touch the same two files |

---

## 1. Findings → ticket map

| Ticket | Findings | Files (primary) | Evidence tier |
|---|---|---|---|
| **EPIC** | — | — | — |
| **T1** Broken goal & pacing flow | 1, 2, 3, 4, 25 | `OnboardingPage.tsx`, `PlacementService.cs`, `GoalFeasibility.cs`, `SetGoalRequest.cs`/`GoalStatusDto.cs`, `database-schema.sql`, `ExamSittingShell.tsx` | red test → green |
| **T2** DTO contract drift + missing Table root | 5, 6, 7, 23 | `lib/api.ts`, `WeaknessAnalysisPage.tsx`, `MockExamConfirmPage.tsx`, `MockExamHistoryPage.tsx`, `docs/seed-demo.sql` | compile-time pin + screenshot |
| **T3** Token & accessibility | 8, 9, 12, 26, 28, 29 | `globals.css`, `AppShell.tsx`, `ExamSittingShell.tsx`, `security.ts` | contrast measurement before/after |
| **T4** Unused HeroUI components | 10, 11, 13, 14 | `main.tsx`+4 pages (Toast), 4 pages (Skeleton), 3 admin pages (SearchField), 3 admin pages (Pagination), `AppShell.tsx` (Drawer), `ExamSittingShell.tsx` (Kbd), 74 `<Link><Button>` sites, 26 `<select>` | screenshot per component |
| **T5** Navigation, dead surface & copy | 15, 16, 17, 18, 19, 20, 21, 22, 24, 27 | `router.tsx`, `SupportPage.tsx`, `OnboardingPage.tsx`, `studioApi.ts`, `api.ts`, `SafeMarkdown.tsx`, `OobeSoftLockEmptyState.tsx`, `LearningPathPage.tsx`, `PracticeBuilderPage.tsx` | red test → green (15–19) / screenshot (20–27) |

Findings **9** and **23**'s layout half are handed to #324–#328 per D13; their audit rows stay open until
that ticket closes.

---

## 2. Execution order

Ordered by dependency, not severity.

```
PR0  docs: remaining.md state fix + GLOSSARY terms            (T5, 3 lines + 2 terms)
PR1  T1-part-1  P0 crash + pacing rule + schema CHECK          (findings 1,2,3,4)
PR2  T1-part-2  placement deadline label                       (finding 25)
PR3  T2        contract pin + Table root + skillScores seed     (5,6,7,23)
PR4  T3        tokens + contrast + aria-live + focus + touch    (8,9,12,26,28,29)
PR5  T5        gate/deep-link/param/error-boundary/404 + copy    (15,16,17,18,19,20,21,22,24,27)
PR6  T4a       Toast + Skeleton                                  (10 partly)
PR7  T4b       SearchField + Pagination + Drawer                  (10,14)
PR8  T4c       Kbd + exam shortcuts + Link>Button sweep           (11,13)
```

Why T1 before T2: T1 adds a DTO field (`GoalLimitsDto` or equivalent). Doing the OpenAPI pin first would
force regenerating types twice.

Why T3 before T4: PR4 changes token values; any screenshot taken for PR5–PR8 must come after, or it
captures the old contrast.

PR1 and PR0 may merge if the review is small enough.

---

## 3. Ticket bodies

### T1 — Broken goal & pacing flow

**Findings.** 1 (goal save crashes the page), 2 (client 1–80 vs server ≤25), 3 (native validation is
dead-end), 4 (date input accepts junk), 25 (placement deadline mislabelled).

**Root causes, verified.**

1. `OnboardingPage.tsx:126-129` writes a `SetGoalResultDto` into the cache slot typed `TargetBandViewDto`;
   `:487` then reads `view.activeCert.code`. `SetGoalResultDto` has no `activeCert`.
2. `GoalFeasibility.cs:39` `MaxWeeklyHours = 25m`; `PlacementService.cs:915` rejects `≤0 || >25` at
   `/goal`; `OnboardingPage.tsx:634` permits `1..80`; `:178` re-checks `1..80` in dead code.
3. `database-schema.sql:1923` hard-codes the literal `25` in `CK_TargetBands_WeeklyHours` — it cannot
   reference the C# constant, so a constant change silently desynchronises the database.
4. `suggestedWeeklyHours` is computed (`GoalFeasibility.cs:80`), typed (`api.ts:295`), tested
   (`GoalFeasibilityCalculatorTests.cs:69,106` — pins 60 and 30), and rendered **nowhere**.

**Work.**

- Replace `setQueryData` with `invalidateQueries({queryKey:['learner','target-band']})`.
- D8: drop the `/goal` rejection of a pace above the ceiling. Keep `≤0` rejection (a zero pace divides
  the availability formula).
- D9: add `GoalLimitsDto { MinWeeklyHours, MaxWeeklyHours, DefaultWeeklyHours }` to `TargetBandViewDto`;
  client renders `min`/`max`/`default` from it. Delete `:178`'s literal and `:634`'s `min={1} max={80}`.
- D10: render `suggestedWeeklyHours`; when `> MaxWeeklyHours`, render "no sustainable pace — earliest
  feasible date {earliestFeasibleDate}".
- Replace `Input type=date` with `DateField` (HeroUI is already a dependency) or, if that is too large
  for this ticket, add `min={today}` and reject the parsed value when it precedes today.
- D5: label the placement countdown "Session expires".

**Blocker — must be decided before code (see §5):** the DB `CHECK (... <= 25)` rejects the very row D8
wants to store. Storing a 26 h/week goal fails at the constraint. D8 is therefore **not implementable
without a schema change**.

**Acceptance.**

- Red test in `tests/Services.Tests` (pattern: `GoalFeasibilityServiceTests.cs:190-204`):
  `POST /goal` with `weeklyStudyHours: 26` persists the row, returns `Unrealistic`, and
  `POST /learning-path/generate` then returns 422.
- Red test in `src/web/src/features/learner/__tests__`: rendering `OnboardingPage` with a cached
  `SetGoalResultDto` does not throw (this is the crash in finding 1).
- Both pass. `npm test` + `dotnet test` twice.
- Browser re-walk: set goal → no crash → Step 4 visible → generate path refused with the feasibility
  message → raise the date → path generated.

---

### T2 — DTO contract drift + missing Table root

**Findings.** 5 (8 field-name mismatches on `sections[]`, 6 on `topics[]`), 6 (two pages render an
unstyled `<table>`), 7 (`skillScores` empty), 23 (history table 475px wide, empty column).

**Verified mismatches.**

| Response (`GET /api/learner/weakness-analysis`) | `api.ts` today |
|---|---|
| `code`, `name`, `answered`, `correct` | `sectionCode`, `sectionName`, `answeredCount`, `correctCount` |
| `topicId`, `modality`, `answered`, `correct`, `accuracyPercent`, `observationFloor` | `categoryId`, `skillCode`+`skillName`, `answeredCount`, `correctCount`, `accuracyPercentage`, `meetsFloor` |

`AttemptSkillScores` has **no row** for the mock attempt (`AttemptId=2`), only for the placement
(`AttemptId=3`) — so finding 7 is a seed gap plus a missing empty state, not a projection bug.

**Work.**

- D11: add `openapi-typescript` as a devDependency, script `npm run gen:api`, generate
  `src/web/src/lib/api-types.gen.ts` from `/openapi/v1.json`, and make the 4 audited DTOs
  (`SectionAccuracyDto`, `TopicAccuracyDto`, `WeaknessAnalysisDto`, `AttemptSkillScoreDto`) alias the
  generated types. Leave the other 100 interfaces alone.
- Fix `WeaknessAnalysisPage` to the corrected field names.
- D4: gate the "Weakest" badge on `observationFloor`; below the floor render "not enough evidence
  (12 needed)" — the field carries the number.
- Add `<Table>` root to `MockExamConfirmPage.tsx:370-372` and `MockExamHistoryPage.tsx:219-221`.
- D6: add `AttemptSkillScores` rows for the seeded mock attempt in `docs/seed-demo.sql`; add an empty
  state for `skillScores.length === 0` in both exam pages.

**Acceptance.** `npm run gen:api` produces no diff against the committed file (drift becomes a build
failure). Screenshot before/after of `/weakness` (table shows a section name and a real %) and of
`/exams/5` (header no longer reads `QuestionsDuration`).

---

### T3 — Token & accessibility

**Findings.** 8 (contrast), 9 (Select overflows the header), 12 (navigator has no HeroUI focus state),
26 (no `aria-live`), 28 (`₫` missing from the font subset), 29 (touch targets, tab state in URL).

**Measured, not estimated.**

| Element | Ratio | Needed |
|---|---|---|
| `Not measured yet` (`#f2b705` on white) | **1.82:1** | 4.5 |
| `Cannot judge` / `4 SKILLS` (muted on surface-secondary) | 4.21:1 | 4.5 |
| dark `+ Top up` (`#0b4ea2` on 12% black) | **1.96:1** | 4.5 |
| dark `Measure Writing →` | **2.22:1** | 4.5 |
| dark `-5` (danger) | **2.54:1** | 4.5 |

`document.fonts.check('16px "IBM Plex Mono"', '₫')` = **false**; `'€'` = **true** — `U+20AB` is absent
from every `unicode-range` in `globals.css:10,19`, so the glyph falls back to a system font (measured:
identical width to `sans-serif`, 51.27px).

**Work.**

- Add `--warning-text` (a darker gold) and use it for text; keep `--warning` for fills.
- Override the four brand hues inside `.dark` only, raising lightness while holding the hue — `DESIGN.md:159`
  means "one token per role, CSS resolves the mode", not "one hex for both modes".
- `U+20AB` into all six `unicode-range` lines.
- `Select.Trigger` in `AppShell.tsx:241` gets truncation and shows only `cert.code`.
- Navigator buttons: React Aria focus state + roving tabindex so 200 questions are not 200 tab stops.
- `aria-live="polite"` on the save placard; `aria-current` on the current navigator cell.
- Mobile touch targets ≥44px; wallet tab state into the URL.

**Acceptance.** Re-run the same contrast probe (illuminated DOM walk, alpha-composited) on 8 routes ×
2 themes and record the new counts: dark `/practice` 16 → 0, `/dashboard` light 4 → 0. `₫` assertion:
`document.fonts.check(...) === true`.

---

### T4 — Unused HeroUI components

**Findings.** 10 (32/73 used), 11 (no shortcuts in the exam sitting while practice has six), 13 (74
`<Link><Button>` pairs), 14 (26 native `<select>`).

**Work, in three PRs.**

- **T4a**: `toast()` for the 36 transient success banners and delete their state; `Skeleton` for the four
  `min-h-[400px]` spinners.
- **T4b**: `SearchField` (3 admin pages), `Pagination` (3 admin pages that hand-roll prev/next),
  `Drawer` for the mobile nav (measured: `scrollWidth 1553 / clientWidth 390`, 11 items, top nav
  `display:none`).
- **T4c**: `Kbd` legend + shortcuts in `ExamSittingShell` mirroring `PracticeSessionPage.tsx:328-367`;
  `<Button render={<Link/>}>` across 74 sites.

**Acceptance.** DOM probe on the 12 representative routes: nested `a > button` count 0 (was 12 at 12
sites), `<select>` count 0 in admin/learner (was 5 on the probed admin pages), pressing `2`/`n` in a
sitting selects an option / advances, mobile nav opens a `Drawer` and the strip is gone.

---

### T5 — Navigation, dead surface & copy

**Findings.** 15 (`/weakness-analysis` bypasses the OOBE gate — proven with a fresh account), 16
(`/support/report` opens the history tab), 17 (`$placementId` vs `$attemptId`), 18 (router has no
`errorComponent`), 19 (`GET /api/admin/ingestion` → 404 on every `/admin/question-bank` mount), 20
(Vietnamese OOBE banner vs English shell), 21 (all four weeks "Off track" before starting), 22 (four
locked practice cards + a recommended-focus banner pointing at a locked one), 24 (two controls for the
same target band), 27 (14 literal `*` in a rendered lesson).

**Work.**

- `router.tsx:445-448` → wrap in `OobeGate`; delete the duplicate route or redirect it.
- `SupportPage` reads the path segment instead of a hard-coded `useState('history')`.
- Fix the param name; add a browser check either way (finding 17 is unconfirmed at runtime).
- Add `errorComponent` with a retry and a route home.
- D12: delete `studioApi.getIngestionBatches()` and its call site; delete the 6 calibration/audit DTOs
  from `api.ts`.
- Decide one language for the shell (D7 concerns doc language; the UI mix is finding 20 and needs its
  own call — recommendation: English shell, Vietnamese content, which is what the lesson actually is).
- Suppress "Off track" for weeks with zero elapsed planned time.
- Practice: one unlock CTA at the top when everything is locked; make the recommended-focus banner
  point at an unlocked section or say why it cannot.
- Remove the duplicate band control (keep the chip row, drop the `Select`, or the reverse — one state).
- `SafeMarkdown`: italic.

**Acceptance.** Red tests for 15, 16, 19; screenshot for 20–27. `/admin/question-bank` network log
shows zero 4xx.

---

## 4. Cross-cutting risks

1. **The DB constraint blocks D8.** `CK_TargetBands_WeeklyHours` (`database-schema.sql:1923`) rejects
   `> 25`, so accepting a 26 h/week goal needs the constraint widened or dropped. This is a schema
   change and per ADR-0001 it starts in `docs/database-schema.sql` and must pass
   `docs/schema-tests/run-all.sh` before any service change. **Open — see §5.**
2. **Two sources of truth for 25.** Even unchanged, the C# constant and the SQL literal can drift.
   Add a schema test asserting `GoalFeasibilityCalculator.MaxWeeklyHours` equals the constraint's bound,
   or drop the constraint and make 25 verdict-only. The current schema test
   (`docs/schema-tests/domain16-goal-feasibility.sql:30-37`) asserts 26 is *rejected*, which is exactly
   what D8 reverses.
3. **`suggestedWeeklyHours` is intentionally unclamped.** `GoalFeasibilityCalculatorTests.cs:97-108`
   pins `SuggestedWeeklyHours == 30` while the status is `Unrealistic`. Rendering it verbatim would tell
   a learner to study 30 h/week. D10's substitution is therefore mandatory, not cosmetic.
4. **`remaining.md` is the file `preflight.sh` prints every session.** It is wrong today. Leaving it
   wrong means the next session avoids pages that are already safe to change.
5. **`api.ts` is 100+ hand-written interfaces.** Pinning 4 of them does not stop the other 96 from
   drifting. The pin is a ratchet, not a cure — say so in the ticket so nobody believes drift is solved.
6. **Dark-mode token change touches every screen.** It must land before the T4 screenshots or they
   document the old contrast.

---

## 5. Open decision (blocks T1)

`CK_TargetBands_WeeklyHours` must change if a pace above 25 is to be storable. Three shapes:

- **(a) Drop the CHECK.** 25 becomes a verdict-only rule, matching `MASTER-SPEC.md:65`
  ("`>25 giờ/tuần → Unrealistic → chặn tạo lộ trình`"). Risk: nothing stops a 100 h/week row; the verdict
  is the only guard.
- **(b) Raise to the mathematical maximum (`<= 168`)** — a week has 168 hours. Keeps "absurd" out of the
  table, lets 26–25-style goals in. Risk: a bound that means nothing invites someone to enforce it.
- **(c) Keep `<= 25` and keep the 400.** Then D8 is reversed and `Unrealistic` stays unreachable from the
  UI; the audit's finding 2 is "fixed" by changing the client to 1–25 and by amending `MASTER-SPEC`.

Recommended: **(a)**, plus a schema test pinning the C# constant to the *verdict* rather than to a
constraint, and a `CHECK (> 0)` left in place for the divide-by-zero case that
`domain16-goal-feasibility.sql:39-46` already covers. This is ADR-shaped: it reverses shipped behaviour,
a future reader will ask why the API stores a pace it refuses to plan with, and there is a real
trade-off between failing fast and showing the verdict.

---

## 6. What is deliberately not in this plan

- Replacing all of `api.ts` with generated types (D11): a 100-interface diff cannot be reviewed next to
  29 fixes.
- Fixing `ProductiveAiFeedback`'s missing side-by-side rewrite card and inline highlighting (spec
  `:74` asks for both). Unverifiable on this seed — no speaking/writing set is published. Separate ticket
  once content exists.
- Any mobile (`D:\Study\PRM_Android`) work.
- Rebuilding the pages #309–#314 owns (`OnboardingPage`, `AppShell` banner, `QuestionBankStudioPage`
  tabs, `PracticeBuilderPage` unlock rule). This plan fixes their **defects**, not their design.
