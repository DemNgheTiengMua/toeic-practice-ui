# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

**Prototype (current):** static HTML5 + CSS3 (custom properties, flexbox, grid) +
vanilla JS (ES2020). No build step, no framework, no npm, no CDN. Shared
`tokens.css` / `base.css` / `components.css`; display state switches via
`?state=` query param handled by `state-switch.js`.

**Target (binding — this is what gets assessed):** ASP.NET Core Web API, RESTful,
JWT bearer auth, three modules in one project (`Identity`, `Content`, `Taking`)
separated by folder + DI, not by process. Front end is ASP.NET Core MVC + Razor
views calling the API via jQuery Ajax. SQL Server. JWT lives in an **HttpOnly
cookie** on the MVC server, never in `localStorage`. Schema is already written:
`docs/database-schema.sql`.

The prototype is step 1 of a graded course project; the .NET application is the
deliverable that follows. The prototype's job is to lock the business flow and
every screen state before real code exists.

## Users

**Primary — individual self-study learners** preparing for TOEIC Listening &
Reading. Each buys their own credit package and manages their own wallet. They
are studying for a score they need (graduation, job application, certification),
not for a class.

**Secondary — Admin.** Reviews the KYC queue, authors exams and questions,
manages credit packages and prices, reconciles orders, maintains score-conversion
tables, and sees platform stats.

Only these two roles exist. There is no teacher, class, or cohort concept.

## Product Purpose

A learner practises by part for free, sees exactly which parts and question types
are weak, verifies their identity, buys credits, and sits full 200-question TOEIC
L&R mock exams under real conditions — then gets a score converted to a CEFR band
and can review every answer with an explanation.

Success means a learner can answer two questions at any point: *where am I weak,
and what do I do about it?* and *what is my score worth in a standard I can
quote?* (raw → scaled → CEFR).

## Positioning

The diagnostic loop is free and stays free — practice by part and weakness
analysis carry no paywall, no purchase prompt, and no credit cost. Weakness
analysis draws on **both** free practice sessions and graded paid attempts, so it
is already useful to a learner who has never paid. Exactly one thing costs money:
one credit = one full exam.

The paid exam is additionally gated by CCCD (Vietnamese national ID) verification,
which is itself free and never consumes a credit. A competitor could copy the
funnel; the identity gate reflects a local regulatory-style requirement for
high-stakes testing, and the ordering rule (verify before charging) is a product
commitment, not an implementation detail.

## Operating Context

**Learner path:** register → practise free → read weakness analysis → verify CCCD
→ buy credits through a redirect gateway (VNPay / MoMo) → sit a full exam
(200 questions, 120 minutes) → result with CEFR band → review answers → history.

**Exam-day realities that are normal, not edge cases:** a 120-minute session will
be reloaded mid-attempt; the clock will run out while the submit dialog is open;
a learner will cancel partway; the network will drop while an answer is saving.
Each has its own screen and its own message.

**Admin path:** KYC queue (compare ID photos against declared data, reject with a
mandatory written reason) → exam authoring (multi-step wizard, per-part question
editor, audio and image upload) → packages and prices → orders → score-conversion
tables → user list.

**Review ritual:** the prototype is reviewed in a browser from `index.html`, a
catalog linking every screen and every one of its states. The convention
throughout is `?state=<name>`.

## Capabilities and Constraints

**Exam format.** TOEIC Listening & Reading only, 100% multiple choice,
auto-graded. 200 questions, 120 minutes, Parts 1–7. Listening and Reading each
5–495; total 10–990. **Part 2 has three options; all other parts have four** — the
option count must be dynamic, never hardcoded.

**Free:** practice by part with immediate per-question feedback; weakness analysis
(per-part accuracy ordered weakest-first, question-type breakdown, 4-week trend).

**Score conversion, two steps with different ownership.** `raw → scaled` varies
per exam because difficulty varies, so **admin edits it per exam**. `scaled → CEFR`
is fixed standard data, so **admin cannot edit it** — the admin screen shows it
read-only with no input fields. Letting it be edited would put two learners with
the same score in different bands.

**CEFR bands (total 10–990):** `<A1` 10–119, `A1` 120–224, `A2` 225–549,
`B1` 550–784, `B2` 785–944, `C1` 945–990. The first band is *below A1*, not A1 —
off by one misreports a learner's level.

**Credits.** A wallet balance is the **SUM over a ledger**, never a stored counter
column. UI must never present the balance as an autonomous number.

**KYC.** Three photos (ID front, ID back, portrait) plus a declaration that the ID
is the learner's own. One CCCD maps to one account; one account has at most one
`pending` application. Rejecting **requires** a written reason, which the learner
sees verbatim.

**Payments.** Redirect gateway (VNPay / MoMo, mocked in the prototype). Order
states `pending` → `paid` | `failed` | `expired`. `expired` (past the 15-minute
reconciliation window) is deliberately separate from `failed` — one invites a
retry, the other a new order. **One `pending` order per account.**

**Target-system constraints that shape the UI:**
1. The timer is **server-authoritative** — the server returns an absolute
   `expiresAt`; the client counts down for display only. The UI needs a
   server-decided expired state.
2. Each answer saves immediately (~400 ms debounce) — the UI needs
   saving / saved / save-failed indication.
3. **Correct answers and explanations are never sent to the client during an
   exam.** Only Review and practice show them.
4. Credit balance is a ledger sum, so no screen may treat it as authoritative
   state of its own.

**Security constraints already decided:** never store raw passwords; hash the
national ID; keep CCCD images out of the database.

**Explicitly out of scope:** TOEIC Speaking & Writing (needs human or AI
grading); vocabulary flashcards; scheduled exam slots with capacity (rejected in
favour of the credit model); refunds.

**Deliberate constraint:** desktop-first, reference viewport 1440×900. Full mobile
breakpoint coverage is intentionally not done — sitting TOEIC on a phone is not
the primary flow.

**Undecided — do not treat as settled:**
- The product name. "TOEIC Practice" is a working name in page titles, not a
  confirmed brand.
- The ingest format for the real question content (see Evidence on Hand).

## Brand Commitments

- **UI copy and exam content are both English.** The prototype shipped its first
  pass in Vietnamese; it was translated to English in full (the UI chrome, the
  generated state-switcher labels, and the inline scripts included) so the
  screens can be reviewed without a translator in the room.
- **The product is still Vietnamese**, and the type system is built to survive
  the copy moving back without a rewrite: IBM Plex is self-hosted in the latin
  *and* vietnamese subsets, so diacritics render from the same family rather than
  falling through to a fallback. Prices stay in ₫, ID verification stays CCCD,
  the gateway stays VNPay/MoMo.
- Personal names in sample data are Vietnamese and stay Vietnamese. The
  convention is to localise the language, not the users.
- Working name: "TOEIC Practice" — unconfirmed, see Undecided above.
- No logo, identity assets, or legal/footer copy exist yet.

## Evidence on Hand

- **34 static screens** + `index.html` catalog, committed on `feat/toeic-ui-prototype`.
  Every screen's states are reachable and verified (26 states, 143 state renders
  checked in-browser).
- **`docs/database-schema.sql`** — SQL Server schema derived from the prototype,
  verified by live execution: 18 tables, 2 views, 15 filtered indexes, 32 check
  constraints. The CEFR lookup was checked at all twelve band boundaries.
- **`docs/toeic-user-flow.md`** — the flow told by actor, each step citing its
  screen file.
- **`docs/superpowers/specs/2026-09-20-toeic-ui-design.md`** — screens and states.
- **`docs/superpowers/plans/2026-09-20-toeic-ui-prototype.md`** — 20 tasks.

**Content is placeholder only.** Exam names like "ETS 2024 — Test 5" are
invented labels. There is no real ETS question content and no real audio —
`assets/img/part1-sample.svg` is a hand-drawn placeholder. **Real question sets
and audio will be supplied later**; the application must be able to ingest them,
but the ingest format is not yet decided.

**Absences future work must not fabricate:** no real ETS content, no real user
data, no testimonials, no customer logos, no benchmarks, no pricing research, no
deployment or compliance claims.

## Product Principles

1. **The diagnostic loop is free, forever.** Practice and weakness analysis never
   carry a paywall or a purchase prompt. They are the reason a learner buys an
   exam, so anything that monetises them defeats the funnel.
2. **Block before you charge.** Every precondition — identity verification, credit
   balance — is checked *before* a credit is deducted, and every blocking screen
   states plainly that nothing was deducted and offers a free way forward.
3. **States are business truth, not decoration.** Timeout, cancellation, reload,
   and save failure are ordinary branches of a 120-minute exam. Each gets a real
   screen and a message that says what it cost.
4. **The server owns time, answers, and balance.** The UI displays these; it never
   decides them. A screen that treats any of the three as its own state is wrong.
5. **Every score must be actionable.** Raw → scaled → CEFR, shown as a position on
   a band with the distance to the next one — because a bare number does not tell
   a learner where they stand.

## Accessibility & Inclusion

Confirmed requirements already applied across the prototype:

- Every input has a `<label>`; every image has `alt`.
- Answer groups use `role="radiogroup"`; selection state is exposed via
  `aria-checked`, not colour alone.
- Focus must always be visible.
- The CEFR band bar is `role="img"` with an `aria-label` naming the current band
  and score; the numeric scale ticks are `aria-hidden` as visual detail.
- Progress and severity are never communicated by colour alone — colour is the
  second layer, always paired with a label or number.
- Tables are used only for real tabular data (score conversions, order lists),
  never for layout.

No formal conformance level (e.g. WCAG 2.1 AA) has been set — undecided.
