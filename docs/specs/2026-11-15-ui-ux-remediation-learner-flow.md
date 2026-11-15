# Spec: UI/UX Remediation — Make the Learner Flow Run Without Lying

**Status:** ready-for-agent
**Date:** 2026-11-15
**ADRs touched:** ADR-0001 (database-first EF Core), ADR-0015 (React + HeroUI v3 SPA), ADR-0017 (streamlined direct ownership)
**Evidence:** `docs/audit/ui-ux-live-audit-2026-11-15.md`, `docs/audit/live/` (32 browser screenshots), `docs/audit/remediation-plan-2026-11-15.md`

---

## Problem Statement

A learner with a demo account cannot complete the guided setup. Saving a study goal at Step 3 of the OOBE stores the goal on the server, then throws while rendering, and the page collapses into a raw error frame. The learner is left on a broken screen that has no route back, and on reload they are pushed forward to a paid step they never agreed to.

Behind that crash sit two more defects on the same screen: the Weekly Study Hours field accepts 1–80 while the server refuses anything above 25, so a large part of the input range is a promised value the server will reject; and the form's error handling is the browser's native validation, so the app's own error message never renders and a junk exam date (a year 70315) is accepted without complaint.

The rest of the product tells the same kind of untruth in quieter ways. The Weakness Analysis page renders a table whose Skill column reads "Skill", whose Accuracy column reads "%", and whose section list shows seven rows with no name and a red "Weakest" badge on every one — because the page reads field names the API does not send. The exam history and exam confirmation tables render as unstyled HTML because they are missing the component root, so headers collide into `QuestionsDuration`. The mock exam result screen prints a "Section & skill breakdown" heading above an empty space.

Dark mode makes text unreadable: brand tokens are fixed hex values applied in both themes, so a link on a dark surface measures 1.96:1 against the 4.5:1 the design contract requires. The Vietnamese đồng sign is not in any of the self-hosted font subsets, so prices render in a fallback font with a broken baseline.

And the shell leaks: the certificate selector overflows the header on every viewport below desktop, the mobile navigation is an eleven-item strip 1553px wide inside a 390px viewport with no affordance that more exists, and the mock exam sitting — the surface where a keyboard would help most — ignores every keystroke while the free practice session implements six shortcuts nobody can discover.

Nobody wants a redesign. They want the flow to run, and the screens to stop stating things that are not true.

## Solution

Fix the flow first, at its narrowest points, and make every number on screen match a number the server actually computed.

The goal setup saves without crashing, accepts exactly the range the server accepts, tells the learner the pace it recommends instead of leaving them to guess, and refuses a past exam date at the field rather than after a round trip. The placement sitting stops presenting its 48-hour inactivity window as an exam countdown. The Weakness Analysis page reads the fields the API sends, and only names a topic "weakest" once that topic has enough answers to deserve the word. The two tables get their component root and their padding back. The result screen says plainly when an attempt recorded no per-skill rows, and the demo seed records some so the breakdown is demonstrable. Dark mode gets text that can be read. Prices keep their own font. The header stops overflowing and the mobile navigation becomes a drawer.

Where a decision was already made and shipped — the goal feasibility ceiling — the plan does not reverse it. The client is brought into line with the server rather than the server being widened to match a bug.

## User Stories

### Goal setup and pacing

1. As a learner finishing the guided setup, I want saving my study goal to keep working, so that I am not dropped onto an error screen at the last step before my learning path.
2. As a learner who has just saved a goal, I want to see the learning path step that follows, so that I know the setup advanced.
3. As a learner entering weekly study hours, I want the field to accept exactly what the server accepts, so that a value I can type is a value that will be stored.
4. As a learner who typed an impossible pace, I want the error to appear next to the field in the app's own voice, so that I do not have to interpret a browser popup.
5. As a learner who typed an impossible pace, I want to be told what pace would work, so that I can decide rather than guess.
6. As a learner whose goal needs more weekly hours than anyone can keep up, I want to be told no sustainable pace exists and what the earliest feasible date is, so that I have two concrete escape routes.
7. As a learner setting an exam date, I want a past date refused as I type it, so that I do not build a plan around a date that already passed.
8. As a learner, I want the default weekly pace to be a number the product can justify, so that the starting point is not an arbitrary figure.
9. As a learner, I want the pacing verdict, the required hours, the available hours and the earliest feasible date shown together, so that the verdict is checkable rather than asserted.

### Placement sitting

10. As a learner sitting a placement test, I want the remaining time presented as a session expiry rather than an exam countdown, so that I do not believe I am being timed per question.
11. As a learner sitting a placement test, I want the expiry shown quietly until it actually matters, so that a 48-hour window does not read as pressure.
12. As a learner who steps away mid-sitting, I want the sitting to still be there when I return, so that leaving the tab does not end my work.

### Weakness analysis

13. As a learner opening weakness analysis, I want each row to name its section, so that I know which part of the exam the number refers to.
14. As a learner reading the topic table, I want the Skill column to say Listening or Reading, so that I can act on the row.
15. As a learner reading the topic table, I want the Accuracy column to show a percentage, so that the row carries the number it claims to.
16. As a learner reading the topic table, I want the Evidence column to show how many answers support the percentage, so that I can judge how much to trust it.
17. As a learner with a small number of answers on a topic, I want the page to say the evidence is too thin to rank, so that it does not call a topic my weakness from two questions.
18. As a learner whose section has never been attempted, I want that row to say so and carry no "Weakest" badge, so that a label does not imply evidence that does not exist.
19. As a learner, I want the overall accuracy figure and the per-topic figures on one screen to agree, so that I am not asked to believe two different numbers.
20. As a learner, I want the frequently-missed list to show names, levels, percentages and answer counts, so that it is readable rather than a row of placeholders.

### Tables and result screens

21. As a learner viewing exam structure before starting, I want each column to have its own header cell with spacing, so that I can tell Questions from Duration.
22. As a learner viewing exam structure, I want the skill name and section code separated, so that I do not read `ListeningREC` as one word.
23. As a learner viewing my exam history, I want each row to fit the page, so that the exam name is not truncated to make room for empty space.
24. As a learner whose attempt recorded no per-skill scores, I want the result screen to say so explicitly, so that I do not read an empty area as a broken page.
25. As a learner using the demo seed, I want a graded mock attempt to carry per-skill scores, so that the breakdown can be demonstrated.
26. As a learner, I want the exam history column that shows my skill scores to show them or say why not, so that the column is never silently empty.

### Colour, contrast and type

27. As a learner using dark mode, I want links and labels on dark surfaces to be readable, so that I can navigate at night without strain.
28. As a learner, I want warning text to meet the same contrast standard as body text, so that "not measured yet" is legible rather than decorative.
29. As a learner, I want currency to sit on the same baseline as the number beside it, so that prices read as one figure.
30. As a user of any theme, I want the same semantic role to keep its meaning in both themes, so that "accent" is still recognisably the accent.

### Shell, navigation and accessibility

31. As a learner on a phone, I want the certificate selector to show one line, so that it does not spill out of the header and cover the page.
32. As a learner on a phone, I want navigation in a drawer rather than a sideways strip, so that I can see what exists without guessing that it scrolls.
33. As a learner in an exam sitting, I want focus to be visibly indicated on the question navigator, so that I can tell where I am.
34. As a learner navigating a long exam by keyboard, I want to move between questions without tabbing through every cell, so that reaching question 40 is not forty presses.
35. As a learner, I want to know when my answer has been saved without having to look, so that a screen reader also says so.
36. As a learner in an exam sitting, I want the same keyboard shortcuts the free practice session offers, so that the surface with a clock is not the one without them.
37. As a learner, I want to discover those shortcuts, so that they exist to be used.

### Actions and navigation

38. As a learner, I want a link styled as a button to be one focusable control, so that I do not tab twice and press Enter into silence.
39. As a learner deep-linking to the support report form, I want the form to open, so that the URL means what it says.
40. As a learner who has not finished the guided setup, I want every gated study route to be gated, so that the alias route does not become a side door.
41. As a learner whose page throws, I want a screen with a retry and a way home, so that I am not left on a raw stack message.
42. As a staff member opening the question bank, I want no failing request on load, so that the console is clean and the page is not paying for a feature that no longer exists.
43. As a learner new to my learning path, I want my first weeks not labelled "off track" before I have started, so that the first impression is not a reprimand.
44. As a learner whose practice catalogue is entirely locked, I want one clear route to unlock it, so that I am not given four identical dead ends and a recommendation pointing at a locked card.
45. As a learner setting a target band, I want one control for that value, so that two controls writing the same state do not make me wonder which one counts.
46. As a learner reading a lesson, I want emphasis to render as emphasis, so that I do not read markup syntax in study material.
47. As a learner, I want the interface to speak one language consistently, so that an English shell and Vietnamese study content do not read as two products.

### Success feedback

48. As a learner who marks a recommendation resolved, I want the confirmation to be transient, so that it does not sit on the page forever.
49. As a learner loading a page, I want a placeholder shaped like the content that is coming, so that the layout does not jump.

## Implementation Decisions

### Modules modified

- **Goal setup page** — the save-success handler stops writing one payload shape into a cache slot typed as another and instead invalidates the query. The weekly-hours bound, the default pace and the suggested pace are rendered from one place. The date field gains a lower bound.
- **Goal service** — unchanged in behaviour for this spec. The existing refusal of a pace above the ceiling stays. Widening it is explicitly out of scope (see below).
- **Placement sitting shell** — the countdown label and its quiet styling become conditional on whether the sitting runs an exam clock.
- **Weakness analysis page and its wire types** — field names align with the contract the API actually sends; the observation floor gates the "weakest" label.
- **Exam structure and exam history pages** — gain the table component root; the history table gains a title cell that can wrap.
- **Exam result page** — an explicit empty state for an attempt with no per-skill rows.
- **Demo seed** — the graded mock attempt gains per-skill rows.
- **Design tokens** — the four brand hues gain dark-mode overrides that raise lightness while holding hue; a text-safe warning token is added; the đồng code point joins every font subset range.
- **Shell** — the certificate selector truncates and shows the code only; the mobile navigation becomes a drawer; the save-state placard becomes an announcement region.
- **Router** — a product error component replaces the framework default; the gated alias route is gated or redirected.
- **Wire contract** — four audited DTOs are pinned to generated types; six retired DTOs and one call to a retired endpoint are deleted.

### Key decisions

1. **The client bound follows the server, not the reverse.** The ceiling of 25 weekly hours is a shipped product rule expressed as a named constant in the feasibility calculator, as a database constraint, and in the product master spec. The client's 1–80 was the defect. Reversing the rule to let the database store an unplannable pace would require a schema migration and would leave the ceiling enforced in two places that must not drift.
2. **The default pace is 7 hours a week.** It is the only figure with a source in the product's own research note; the previous 5 was arbitrary.
3. **No hard floor above zero.** A learner who genuinely has two hours a week must be able to record that goal and see it judged. The research note's "3 to 25" is the domain of a slider widget, not a validity rule, and no specification promotes it to one.
4. **The suggested pace is rendered, and substituted when it exceeds the ceiling.** The calculator deliberately does not clamp the suggestion — tests pin a suggested pace of 60 and of 30 while the verdict is unrealistic. Rendering that number verbatim would instruct a learner to study thirty hours a week. When the suggestion exceeds the ceiling the surface says no sustainable pace exists and names the earliest feasible date, which the calculator already computes.
5. **A topic is named weakest only when it reaches its observation floor.** The wire contract already carries the floor per cell (12 answers for listening and reading, 3 tasks for speaking and writing). Below it the surface says the evidence is too thin. This is a change in what the product claims, so the vocabulary is recorded in the glossary.
6. **The placement deadline is an expiry, not a countdown.** The sitting is explicitly clock-free; the only deadline is the shorter of a 72-hour lifetime and a 48-hour inactivity window, stamped by a write. Presenting that as "time remaining" in the same style as an exam clock converts an anti-anxiety design into exam pressure.
7. **A missing per-skill breakdown is stated, and the seed is fixed rather than only the screen.** The audit established that the table is empty because the seeded attempt has no rows — not because the projection drops them. Fixing only the screen leaves the demo permanently unable to show a breakdown; fixing only the seed leaves the screen printing a heading over nothing.
8. **Contract drift is ratcheted, not solved.** The React wire types are hand-written and will drift again. This spec pins the four audited DTOs to types generated from the API's OpenAPI document, which the API already publishes and which an existing test already parses. Pinning four types is a ratchet: it stops these four from drifting and establishes the mechanism. Wholesale replacement of every interface is refused because it cannot be reviewed alongside behavioural fixes.
9. **Retired surface is deleted, not hidden.** Six calibration and audit DTOs serve the complexity the current rebuild eradicated, and one client call targets an endpoint that returns 404 on every mount. Dead declarations that look live are the documented trap in this repository.
10. **Defensive styling already owned elsewhere is folded into that work.** The project's validation-hardening specification already claims the shell, header, card and table text-overflow work. The certificate-selector overflow and the history table width belong to it; a second ticket changing the same lines would conflict and both would be "correct".
11. **Grouping follows the files touched, not the severity.** The crash and the table defect are both two-file changes; splitting tickets by severity would put them in different branches touching the same lines.

### Wire and schema

- No schema change. The weekly-hours ceiling stays a constraint as well as a verdict.
- `POST /api/learner/goal` behaviour is unchanged: it refuses a pace at or below zero and above the ceiling.
- The four pinned DTOs are the section accuracy, topic accuracy, weakness analysis and attempt skill score shapes.
- Six calibration/audit DTOs and one ingestion-batch call are removed from the client.

### Interactions

- The save-goal success handler invalidates the target-band query instead of writing into it.
- Error text for the weekly-hours field is produced by the app and rendered at the field, not by the browser's native validation bubble.
- The "weakest" badge is suppressed below the observation floor and replaced by an evidence note carrying the required count.
- The placement expiry label appears plainly, and only escalates when under an hour.
- Table headers and cells get their component padding back by restoring the component root, not by re-adding utility classes to each cell.

## Testing Decisions

### What makes a good test here

A test must observe the artefact the user receives, not the mechanism that produced it. For the goal flow, that means the persisted row and the rendered page, not the cache call that wrote them. For the weakness page, that means the rendered row text, not the field mapping that fed it. For contrast, that means a measured ratio from computed styles, because "a token changed" passes while the text stays unreadable.

A test that only asserts "the endpoint returned 200" would pass on the crashing page — the crash happened after a successful save. A test that only asserts "the table rendered" would pass on the unstyled table. Both weaker forms are named here so a future contributor recognises them.

### Seams

| Seam | Used for | Prior art |
|---|---|---|
| Goal service, through its public interface | pace validation, verdict, stored row, path-generation refusal | `GoalFeasibilityServiceTests` — same fixtures, same inaccessible-database-per-test isolation |
| Goal endpoint over HTTP | request validation and problem-details shape | `GoalStatusMappingTests`, `BadRequestBindingTests` |
| The goal setup page, rendered with a router at a route | the save path does not throw; field bounds; suggested pace text | `learner-hub.test.tsx` "allows selecting target band and submitting study goal" |
| The weakness page and the exam pages, rendered | row text, badge suppression, table structure, empty state | `learner-hub.test.tsx` weakness block, `MockExamReviewAndHistory.test.tsx` |
| Generated wire types against the published OpenAPI document | contract pinning | `OpenApiDocumentationTests` already parses the document |
| Computed-style measurement in a real browser | contrast, font subset coverage | the audit's own probe, kept as a script |

### Tests to add

- A goal saved from the page does not throw, and the following step renders. (Regression for the crash; the existing page test mocks a successful save and still does not catch it because it never re-renders with the mutated cache.)
- A pace above the ceiling is refused by the service with the ceiling named, and nothing is stored. (Already exists — keep, do not weaken.)
- The page refuses a pace above the ceiling before calling the API, and the message names the ceiling.
- The suggested pace above the ceiling renders as "no sustainable pace" with the earliest feasible date, and the raw number is not presented as advice.
- A date before today is refused at the field.
- A weakness row renders its section name, its skill word and a percentage — asserted on text, not on props.
- A cell below its observation floor carries no "weakest" badge and shows the evidence note.
- An attempt with no per-skill rows renders the explicit empty state; an attempt with rows renders a progress element per skill.
- Pinned DTO types match the generated OpenAPI types (build-time, not runtime).
- Contrast probe: on eight routes across both themes, no text node falls below its computed requirement.

### Tests not to add

- Tests asserting a particular CSS class string. They pass on unstyled markup and break on refactors.
- Tests asserting the number of `setQueryData` calls.
- A test that re-implements the server's availability formula in the browser.

## Out of Scope

- **Widening the weekly-hours ceiling so an unrealisable pace can be stored.** This was considered and declined for this specification: it needs a schema migration, a schema-test change and a re-run of the schema harness, and it delivers nothing the demo flow needs. It remains a coherent future change, recorded with its trade-off in the remediation plan.
- Replacing every hand-written wire interface with generated types.
- Rebuilding the pages the current platform rebuild owns — the guided setup flow, the shell banner, the question bank tabs, the practice unlock rule. This work repairs their defects; it does not redesign them.
- The missing side-by-side model-rewrite card and interactive inline highlighting in the productive feedback surface. Both are required by the rebuild spec, but no speaking or writing set is published on the current seed, so the surface cannot be verified. It needs its own ticket once content exists.
- Any mobile client work.
- Any behaviour of the retired calibration, audit and ingestion features beyond deleting their dead client declarations.

## Further Notes

- The audit's finding that the client permitted 1–80 while the server accepted up to 25 has a second half: the app's own error branch was unreachable because native browser validation blocked submission first. Fixing the bound without fixing the error path would leave the app still unable to speak for itself.
- `docs/audit/remaining.md` currently reports the platform rebuild tickets as open; the tracker reports them closed. Every later session reads that file through the preflight script, so it is corrected as part of this work rather than left to mislead.
- Two vocabulary entries are added to the glossary by this work: the observation floor, and the distinction between a rankable and an unranked weakness cell.
- Money formatting depends on two things at once — the number formatter and the font subset. Fixing the font alone leaves the unit adjacent to the figure by a breaking space; fixing the formatter alone changes nothing on screen.
