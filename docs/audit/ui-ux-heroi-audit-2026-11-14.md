# UI/UX & HeroUI v3 coverage audit — 2026-11-14

Scope: `src/web` (136 TS/TSX files, 38 Vitest files / 180 tests green, `tsc --noEmit` clean).
Method: static read of every page + `heroui-react` MCP (`list_components`, `get_component_docs`) against
`DESIGN.md`. No browser walk was taken, so every claim below is a code claim, not a screenshot claim.

Headline: the app uses HeroUI **correctly** (compound components, semantic tokens, no v2 flat props), but
uses only **32 of 73** components. The gaps cluster in three places: *feedback/loading*, *form primitives in
admin*, and *data visualisation* — where HeroUI v3 deliberately ships no chart component, so the work is
inline SVG, not a new dependency.

---

## 1. HeroUI coverage

| | Count |
|---|---|
| Components available (v3.2.7) | 73 |
| Imported anywhere | 32 |
| Never imported | 41 |

**Used:** Accordion, Alert, AlertDialog, Breadcrumbs, Button, Card, Checkbox, CheckboxGroup, Chip,
Description, FieldError, Form, Input, Label, ListBox, Meter, Modal, NumberField, Pagination, Popover,
ProgressBar, RadioGroup, Select, Slider, Spinner, Switch, Table, Tabs, TextArea, TextField, Toast,
ToggleButton.

**Never used (41):** Autocomplete, Avatar, AvatarGroup, Badge, ButtonGroup, Calendar, CloseButton,
ColorArea, ColorField, ColorPicker, ColorSlider, ColorSwatch, ColorSwatchPicker, ComboBox,
DateField, DatePicker, DateRangePicker, Disclosure, DisclosureGroup, Drawer, Dropdown, ErrorMessage,
Fieldset, InputGroup, InputOTP, Kbd, Link, ProgressCircle, RangeCalendar, ScrollShadow, SearchField,
Separator, Sheet, Skeleton, Surface, TagGroup, TimeField, Toolbar, Tooltip, Typography, ToggleButtonGroup.

Not all 41 matter. The ones that map onto an existing pain point are listed in §2.

---

## 2. Missing components that fix a real defect (ranked)

### P1 — `Toast` mounted, never fired; 36 ad-hoc success banners instead
`main.tsx:23` mounts `<Toast.Provider />`, but `toast()` is called **zero** times in the whole app.

Meanwhile four pages keep a `successMessage`/`submitSuccess` string in state and render an `Alert` that
never expires:

- `WeaknessAnalysisPage.tsx:38,151-160` — "Recommendation marked as resolved." stays forever.
- `LearningPathPage.tsx:282-291`, `OnboardingPage.tsx`, `SupportPage.tsx:180-190`.

Cost: a duplicate banner on every repeat action, plus a full `Alert` block pushing layout down for a
message that is transient by nature. `toast('…')` with `Toast.Description` is the platform answer and
needs no new state.

### P1 — `Skeleton`: zero usage; every page swaps to a centred `Spinner`
47 files use `Spinner`; four learner pages replace the whole viewport with
`min-h-[400px]` + spinner (`DashboardPage.tsx:61-68`, `LearningPathPage.tsx:136-143`,
`WeaknessAnalysisPage.tsx:88-95`, `LessonReaderPage.tsx:124-131`).

That is a layout jump on every navigation and the slowest-perceived state available. `Skeleton` exists,
is unmounted nowhere, and the card geometry it needs is already fixed by `Card`. Dashboard is the clearest
candidate: 4 KPI cells + skill grid, all known shapes.

### P1 — 26 native `<select>`: no typeahead, no dark-mode parity, inconsistent focus
`<select` appears 26 times in 11 files, each with hand-written
`className="h-9 px-3 rounded-md border border-border bg-background …"`:

- admin ops: `OrdersTab.tsx:148,168`, `RefundsTab.tsx:295`, `ComplaintsTab.tsx:208`, `MentorsTab.tsx:782,805,1004`
- admin studio: `ExamsStudioPage.tsx:408,418,622,947,965,1100`, `QuestionBankStudioPage.tsx:598,612,628,724`, `CertificatesStudioPage.tsx:414,478`
- learner: `ProfilePage.tsx:342`, `SupportPage.tsx:383,424,443,462`, `MockExamReviewPage.tsx:259,279`

Against 7 correct `<Select>` usages (AppShell, PracticeBuilder, SavedQuestions, Onboarding…). The native
ones lose keyboard typeahead, popover positioning, and the `--field-*` token set. This is the single
largest source of "looks different in dark mode" risk, and `DESIGN.md:141` explicitly requires the field
components here.

### P1 — `Tooltip`: zero usage, and there are real things to explain
Abbreviations and icon-only affordances carry no explanation anywhere:

- CEFR/band codes rendered alone (`DashboardPage.tsx:461-467`), skill codes `LIST/READ/SPEA/`WRIT``,
  `OVERALL B1 · Reading` (`:329-332`).
- The flag dots on the sitting navigator (`ExamSittingShell.tsx:1206-1212`) — a coloured dot with
  `aria-hidden`, so a sighted mouse user cannot learn what gold means.
- Stale-measurement chip "Needs re-measuring" (`DashboardPage.tsx:450-454`) has no date, though
  `skill.measuredAt` is available and unused.

### P1 — `Drawer`/`Sheet`: the mobile navigation is a horizontal scroll strip
`AppShell.tsx:357-388` — on `<lg` the sidebar becomes `flex … overflow-x-auto` holding **11** nav items.
The user scrolls sideways through a nav bar with no gradient, no scrollbar affordance (`ScrollShadow`
would give one), and no way to see the last items. Below `md` the top nav is `hidden` too (`:204`), so
mobile's only navigation is that strip plus the `select` certificate switcher.

`Drawer` (or `Sheet`) with the same `navItems` array is the intended pattern and already keyed by
`isActive`.

### P2 — `SearchField`: hand-rolled search in three places
`OrdersTab.tsx:131-144` (a `TextField` + absolutely-positioned `Search` icon + `Input`), plus
`ExamsStudioPage.tsx:400`, `AdminUsersPage.tsx:316`. `SearchField` + `SearchField.ClearButton` removes
the icon-token hack and gives a clear button the hand-rolled versions lack.

### P2 — `Pagination`: 1 correct usage, 3 hand-rolled prev/next
Correct: `SavedQuestionsPage.tsx:545-580`. Hand-rolled:
`OrdersTab.tsx:291-316`, `RefundsTab.tsx:427-450`, `ComplaintsTab.tsx:311-334` — no page numbers, no
summary, no ellipsis. Admin users then have no idea where they are in a 200-row list.

### P2 — `NumberField` / `DateField` / `TimeField` in admin + onboarding
Native inputs with `type="number" | "date" | "time"`:
`MentorsTab.tsx:501,513` (date), `:836,865` (number), `:1026,1039` (time); `RefundsTab.tsx:487,535,558,578,588`
(number), `:896,905` (date); `OnboardingPage.tsx:621` (`<Input type="date">`) and `:634`
(`<Input type="number" min max step>`).

`OnboardingPage` is the worst case: it is the goal-setting step (exam date + weekly hours,
"Feasible / Stretch / Unrealistic"), i.e. the one screen where an invalid value changes the whole plan,
and it validates with raw HTML `min/max` only. `NumberField`'s increment/decrement + `FieldError` and
`DateField`'s segment editing exist and are used nowhere.

### P2 — `Kbd`: shortcuts exist but are undiscoverable
`PracticeSessionPage.tsx:328-367` implements `1-4`, `S`, `H`, `B`, `N`, `P`. Nothing in the UI says so,
and there is no shortcut help. `Kbd` exists for exactly this. Meanwhile `ExamSittingShell` has **no**
keyboard shortcut handling at all (no `onKeyDown`), so the two sitting surfaces behave differently —
the mock exam, the more timer-pressured of the two, is the one without shortcuts.

### P2 — `Avatar` / `AvatarGroup`: mentor cards are text-only
`MentorsCatalogPage.tsx:132-196` renders name + headline + rating. `MentorSummaryDto` has no image field,
so this is initials-only, but a `Avatar` with the mentor's initials gives the catalog the visual anchor it
currently lacks — 3 cards per row of pure text.

### P3 — `Badge`, `Disclosure`, `ScrollShadow`, `Separator`, `Surface`, `Typography`
- `Badge`: 16 hand-written `px-2 py-0.5 rounded bg-…` counters; no notification badge on the `Support`
  nav item even though `needsAttention.openComplaintsCount` exists.
- `Disclosure`/`Accordion`: used once (`PricingPage.tsx:272-284`). The two longest learner pages stack
  everything (`WeaknessAnalysisPage` ~500 lines, `LearningPathPage` ~680) — the evidence/methodology
  paragraphs are collapsible content by nature.
- `ScrollShadow`: long `Table.ScrollContainer` and ledger lists have no scroll affordance.
- `Separator`/`Surface`/`Typography`: 86 `Card className="… border …"` overrides and dozens of
  hand-written `<strong className="text-sm font-bold">` in card headers where `Card.Title` exists
  (`WeaknessAnalysisPage.tsx:232,306,376,415,443`). Cosmetic debt, not a defect.

---

## 3. Where the app should get *more visual*, and whether the data exists

HeroUI v3 has **no chart component** (verified: the 73-component list contains none). So "dashboard needs
charts" is inline SVG work, not a library decision. Below, only the ones where the API already returns
the series — no backend change.

| Where | What to draw | Data already returned | Status |
|---|---|---|---|
| `MockExamHistoryPage` | score trend across attempts (line/stepped) + band track | `history.attempts[].totalScore`, `history.bandCode`, `history.cefrBands` | **`cefrBands` is fetched and never rendered** (0 refs); `averageScore` is shown only as one number (`:186`) |
| `WeaknessAnalysisPage` | horizontal bar list per topic, weakest first | `topics[].accuracyPercentage`, `.answeredCount` | currently a `<table>` of numbers (`:265-298`) plus hand-rolled bars for sections only |
| `WalletPage` | balance-over-time sparkline | `wallet.ledger[].delta` + `.createdAt` (full history, no slicing) | nothing visual; the ledger is a table |
| `AdminOverviewPage` | revenue/credit sparkline; today-vs-month delta | `creditsPerDayAverage`, `revenueTodayVnd`, `revenueThisMonthVnd` | 4 static KPI cards + one raw `<table>` (`:384`) |
| `DashboardPage` — skill standing | `ProgressCircle` with the level code in the centre | `skill.levelCode` + `skill.positionInBand` | plain text "Position: 62%" (`:464-468`) |
| `DashboardPage` — this week | `Meter` with `Label` + `Meter.Output` | `goal.weekCompletedMinutes` / `weekPlannedMinutes` | a prose sentence (`:300-305`) |
| `PracticeResultPage` | correct / incorrect / skipped as one stacked bar | `correctCount`, `incorrectCount`, `skippedCount`, `hintUsedCount` | three separate numbers |
| `PlacementResultPage` | already has a good band signboard | — | keep; this is the best visual in the app |

Two notes on `MockExamHistoryPage`: it is the only page with a real time series and it is also the only
page that ignores the band data it fetched. That is the highest-value single change on this list.

`Meter` already works (`WritingPracticePage.tsx:380-393`) — the word-count meter proves the pattern; it
just was not reused.

---

## 4. Defects found while auditing (not component gaps)

1. **`/weakness-analysis` bypasses the OOBE gate.** `router.tsx:438-449` — `/weakness` wraps
   `WeaknessAnalysisPage` in `OobeGate`, `/weakness-analysis` does not. A learner mid-OOBE can reach the
   analysis page through the alias.
2. **Route paths are ignored by their pages.** `/support`, `/support/report`, `/support/history` all render
   `SupportPage`, which hard-codes `useState('history')` (`SupportPage.tsx:44`) with no `useLocation`. Deep
   link `/support/report` lands on history. Same for `/placement/target-band` → `OnboardingPage`.
3. **Onboarding navigates to a param name the route does not declare.** `OnboardingPage.tsx:115,276` uses
   `to: '/placement/take/$placementId'` with `params: { placementId }`; the route declares
   `$attemptId` (`router.tsx:319`). `tsc --noEmit` passes and the tests mock `navigate`, so nothing catches
   it. Verify in a browser before trusting either path.
4. **74 `<Link><Button>` pairs.** `Button` renders `<button>`, so this is `<button>` inside `<a>`: invalid
   nesting, two tab stops, and Enter on the inner button does not follow the link. Affects navigation the
   user hits constantly — `MockExamResultPage.tsx:75-85`, `LandingPage.tsx:75-85`,
   `MentorsCatalogPage.tsx:181-192`, `WalletPage.tsx:74-86`, 22 files total.
5. **`text-warning` used as a text colour on light surfaces.** `#f2b705` on white is ≈1.6:1 — fails WCAG
   AA for text by a wide margin. 50 usages, incl. counts (`OrdersTab.tsx:100`), stars
   (`MentorsTab.tsx:436`), and figures (`ComplaintsTab.tsx:139`). `--warning-foreground` is the
   on-warning token; these need `text-foreground` + a warning *surface*, or a darker warning text token.
6. **No router error boundary.** `router.tsx` has no `errorComponent`/`defaultErrorComponent` (0 matches),
   and there is no `ErrorBoundary` anywhere in `src/web`. Any render throw = blank page, no way back.
7. **Mixed language.** `AppShell.tsx:333` and `OobeSoftLockEmptyState.tsx:17-20` are Vietnamese; every
   other string in the app is English. The Vietnamese copy is also the least polished text in the UI.
8. **Sitting accessibility.** The save-state placard (`ExamSittingShell.tsx:775-788`) has no `aria-live`, so
   "Saved" / "Offline — N queued" is invisible to screen readers. The question navigator
   (`:1182-1213`) is 20–200 raw `<button>`s in a 5-column grid with no `focus-visible` styling and no
   arrow-key behaviour: ~40 Tab presses to reach question 41. Mentor time slots
   (`MentorDetailPage.tsx:341-373`) are the same pattern.
9. **Legacy button classes.** `PlacementResultPage.tsx:180,511-520` and `PlacementCatalogPage.tsx:175,273`
   use `className="button button--sm button--primary"` on a `<Link>`. These classes do exist in
   `@heroui/styles` (so they render correctly), but they bypass the React component: no `isPending`, no
   disabled semantics, and they are the only place a "button" is not a `Button`.
10. **Dead code in the shell.** `router.tsx:74-123` `PlaceholderView` + `stubRoute` — 0 call sites.
    `features/wallet/CheckoutPage.tsx` is a 43-byte re-export shim of `features/checkout/CheckoutPage`
    with no importers (`features/wallet/index.ts` does not exist).

---

## 5. Suggested order (cheapest first, highest ratio first)

1. `MockExamHistoryPage` — render the `cefrBands` already fetched + an inline SVG score trend. One page,
   no new data, biggest "dashboard chart" payoff.
2. Replace the 4 full-page spinners with `Skeleton` grids.
3. `toast()` for the 36 transient success banners; delete the state.
4. Swap the 26 native `<select>` for `Select` (start with admin ops, where the filters are daily tools).
5. Add an `errorComponent` to the router (one file, prevents blank screens).
6. Mobile nav → `Drawer`; sitting navigator → `focus-visible` + arrow keys.
7. `WeaknessAnalysisPage` topic table → `Meter` bar list; `DashboardPage` skill card → `ProgressCircle`.
8. Fix `text-warning`-as-text (token change, no logic), then the route/param defects in §4.1–4.3.

Items 1–5 are independent and each is a single-file change. Items 6–8 touch shared surfaces and need the
full Vitest + crawl-env re-walk.
