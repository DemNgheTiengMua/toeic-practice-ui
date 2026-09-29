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
JWT bearer auth, modules in one project separated by folder + DI, not by
process. Front end is ASP.NET Core MVC + Razor views calling the API via jQuery
Ajax. SQL Server. JWT lives in an **HttpOnly cookie** on the MVC server, never
in `localStorage`. Schema is already written: `docs/database-schema.sql` — the
**multi-certificate companion** schema (10 domains), which supersedes the
TOEIC-only schema the current static prototype was built against (see
Architecture Pivot below).

The prototype is step 1 of a graded course project; the .NET application is the
deliverable that follows. The prototype's job was to lock the business flow and
every screen state before real code exists; the schema has since pivoted wider
than the prototype (see Architecture Pivot).

## Architecture Pivot (2026-09-24)

An advisor reviewed the database design and raised two structural objections:
features must live in the schema (not hidden in backend logic), and the product
must broaden beyond a single TOEIC L&R exam. The accepted redesign —
`docs/superpowers/specs/2026-09-24-multi-cert-companion-design.md` — turns the
product into a **multi-certificate practice-and-mock-exam companion**:

- **Removed:** the real/high-stakes exam ("thi thật") and the entire KYC/CCCD
  identity gate. Only **practice** (free) and **mock exams** (paid) exist;
  nothing pretends to be an official sitting, so no identity verification is
  needed.
- **Added:** multiple certificates (TOEIC, IELTS, VSTEP — data-driven, each
  with its own skills and sections); skills beyond listening/reading
  (speaking, writing); a placement test that generates a level-matched
  **learning path**; **AI grading** of speaking/writing against a rubric; and
  **mentor booking** (human via Google Meet, or AI) with reviews, complaints,
  and payouts.
- **Monetisation widened:** one credit was "one real exam"; now credits pay
  for a mock exam start, AI grading, a mentor booking, or a premium learning
  path.

The static prototype under `prototype/` still implements the old TOEIC-only,
KYC-gated flow and **has not been re-screened** for the pivot. The schema and
this document are the source of truth; the prototype is historical evidence of
state coverage, not of current scope.

## Users

**Primary — individual self-study learners** preparing for an English
certificate (TOEIC, IELTS, VSTEP, …). Each buys their own credit package and
manages their own wallet. They are studying for a score they need (graduation,
job application, certification), not for a class. A learner may pursue
**several certificates in parallel** — everything learner-scoped is keyed by
`(user, certificate)` and persists independently; switching the active
certificate never destroys another certificate's data (only an explicit reset
does).

**Secondary — Admin.** Authors and imports exams and questions, maintains
per-certificate score scales and bands, manages credit packages and prices,
reconciles orders, handles complaints, and sees platform stats.

**Tertiary — Mentors.** Humans who sell bookable slots and meet learners on
Google Meet (the app collects payment and stores the link; it does not host
sessions), plus AI mentors that book without a slot.

There is no teacher, class, or cohort concept.

## Product Purpose

A learner picks a certificate, takes a **free placement test**, gets a band
and a level-matched **learning path**, practises sections for free, and spends
credits only on the things that move them toward their target band: full
**mock exams**, **AI grading** of their speaking/writing with concrete
recommendations, and **mentor sessions**. After purchase, the product stays
with them — the companion follows the path, unlocks steps upward, and aims
every correction at the target band.

Success means a learner can answer two questions at any point: *where am I
weak, and what do I do about it?* and *what is my score worth in a standard I
can quote?* (raw → scaled → band).

## Positioning

The diagnostic loop is free and stays free — practice by section, weakness
analysis, and the placement test carry no paywall, no purchase prompt, and no
credit cost. **Free is a data fact, not a policy:** those flows never write a
ledger row. Weakness analysis draws on **both** free practice sessions and
graded paid attempts, so it is already useful to a learner who has never paid.

Exactly four things cost credits, each a distinct ledger reason: starting a
mock exam (`mock_exam_start`), AI-grading an attempt's free responses
(`ai_grading`, one charge per attempt, not per answer), booking a mentor
(`mentor_booking`), and upgrading to a premium learning path (`premium_path`).

The old identity gate is gone. With only mock testing, gating payment behind
CCCD verification bought nothing — so verification, and the whole KYC queue,
were removed rather than softened.

## Operating Context

**Learner path:** register → pick a certificate (placement selects the
certificate **first**, so the test matches the skills that certificate
actually has) → free placement test → band + target band → generated learning
path → practise free → buy credits through a redirect gateway (VNPay / MoMo) →
spend them on mock exams / AI grading / mentor bookings / premium path →
review answers with explanations → history per certificate.

**Exam-session realities that are normal, not edge cases:** a long mock exam
will be reloaded mid-attempt; the clock will run out while the submit dialog
is open; a learner will cancel partway; the network will drop while an answer
is saving. Each has its own screen and its own message.

**Companion realities:** an attempt with speaking/writing answers **cannot
finish grading on submit** — it waits for the AI grading pass; a learner
switches between two certificates and expects both histories intact; a
resolved mentor complaint may come back as a credit make-good.

**Admin path:** exam authoring and **batch import** (import is a first-class,
queryable feature with per-row logs — an advisor requirement) → per-exam
raw→scaled tables → packages and prices → orders → complaint resolution → user
list.

**Review ritual:** the prototype is reviewed in a browser from
`prototype/index.html`, a catalog linking every screen and every one of its
states. The convention throughout is `?state=<name>`.

## Capabilities and Constraints

**Catalog is data-driven.** Certificates → Skills → Sections are tables, not
code. `Skills.Modality` (listening/reading/speaking/writing) drives the answer
branch downstream: listening/reading → MCQ, auto-graded; speaking/writing →
free response, AI-graded. `Sections.OptionCount` generalises the old "Part 2
has 3 options" rule — per-section, data-driven, NULL for non-MCQ sections,
never hardcoded.

**Certificate isolation is a schema fact.** A skill belongs to one
certificate, a section to one skill, an exam to one certificate — and
composite foreign keys carry `CertId` down so a TOEIC exam *cannot* contain an
IELTS section, a rubric score *cannot* name another skill's criterion, and a
path item *cannot* reference another certificate's content. This is enforced
by keys, not by backend policy.

**Exam format varies by certificate.** Question counts, durations, skill
counts (TOEIC 2, IELTS 4), and scales all come from catalog rows. Per-skill
scores are rows (`AttemptSkillScores`), not fixed columns; the total must
always equal the SUM of scaled skill scores (trigger-enforced).

**Free:** practice by section with immediate per-question feedback (MCQ only —
immediate grading structurally requires a correct option, so speaking/writing
practice routes through paid AI grading); weakness analysis; the placement
test and the basic generated path.

**Score conversion, two steps with different ownership.** `raw → scaled`
varies per exam because difficulty varies, so **admin edits it per exam**
(`ScoreScales`). `scaled → band` is fixed standard data per certificate
(TOEIC→CEFR, IELTS→0–9, VSTEP→bậc), so **admin cannot edit it** — the admin
screen shows it read-only with no input fields, and band ranges may not
overlap within a certificate. Band codes are **snapshotted** into results at
grading time so history survives later catalog edits.

**Credits.** A wallet balance is the **SUM over an append-only ledger**, never
a stored counter column. Ledger rows cannot be deleted or rewritten; every
debit names its provenance (order / attempt / booking / path); the charged
wallet must own the source row; no transaction may drive a balance negative;
an order that has been credited cannot leave `paid`. UI must never present the
balance as an autonomous number.

**Payments.** Redirect gateway (VNPay / MoMo, mocked in the prototype). Order
states `pending` → `paid` | `failed` | `expired`. `expired` (past the
15-minute reconciliation window) is deliberately separate from `failed` — one
invites a retry, the other a new order. **One `pending` order per account.**

**Mentors.** The app collects the booking credit and stores a Meet link; it
never hosts sessions. Human mentors need slots (one live booking per slot);
AI mentors book without one. Reviews and complaints attach only to bookings;
average rating is a **view**, never stored. A resolved complaint may issue an
`admin_grant` credit make-good, auditable through the same ledger. Mentor
payouts batch settled bookings; payout totals are a **view**, and the actual
outbound transfer happens outside the app.

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
5. Grading is **asynchronous** for speaking/writing — the UI needs a
   pending-grading state between submit and result.

**Security constraints already decided:** never store raw passwords (hash);
password-reset tokens are stored hashed. (CCCD hashing and image handling are
gone with KYC.)

**Explicitly out of scope:** real/high-stakes exam sittings and everything
they required (KYC/CCCD, identity gates); cash refunds (a resolved complaint
may issue a *credit* make-good, but money never returns to a card/bank);
scheduled exam slots with capacity; the outbound money transfer to mentors;
the concrete question-import file format; OCR/face matching.

**Deliberate constraint:** desktop-first, reference viewport 1440×900. Full
mobile breakpoint coverage is intentionally not done.

**Undecided — do not treat as settled:**
- The ingest format for the real question content (see Evidence on Hand).
- Which AI model/provider performs grading (`AiGradings.Model` records which
  one did, after the fact).

## Brand Commitments

- **Product name: "extra efficient"** — confirmed brand for the multi-certificate
  companion.
- **UI copy and exam content are both English.** The prototype shipped its
  first pass in Vietnamese; it was translated to English in full (the UI
  chrome, the generated state-switcher labels, and the inline scripts
  included) so the screens can be reviewed without a translator in the room.
- **The product is still Vietnamese**, and the type system is built to survive
  the copy moving back without a rewrite: IBM Plex is self-hosted in the latin
  *and* vietnamese subsets, so diacritics render from the same family rather
  than falling through to a fallback. Prices stay in ₫, the gateway stays
  VNPay/MoMo.
- Personal names in sample data are Vietnamese and stay Vietnamese. The
  convention is to localise the language, not the users.
- No logo, identity assets, or legal/footer copy exist yet.

## Evidence on Hand

- **48 static HTML screens** spanning student (33), admin (10), auth (2), and
  payment (3) sections + `prototype/index.html` catalog, committed on
  `feat/toeic-ui-prototype`. Every screen's states are reachable and verified
  (26 states, 143 state renders checked in-browser). **These screens describe
  the pre-pivot TOEIC-only product**; KYC screens and the "thi thật" framing
  no longer reflect scope.
- **Layout system consolidation (2026-09-28):** 173 inline spacing overrides
  eliminated and converted to utility classes; definition-list grids, progress
  bars, centered content, and button groups now use consistent token-based
  layout helpers (`stack--tight`, `row--center`, `container--narrow`,
  `no-margin`). Skeleton loading animations refined for `prefers-reduced-motion`
  accessibility.
- **`docs/database-schema.sql`** — the multi-certificate companion schema,
  verified by live execution on LocalDB: 41 tables, 5 views, 16 triggers, 9
  filtered unique indexes, ~50 check constraints, plus 10 domain test scripts
  (`docs/schema-tests/`) that all pass (`run-all.sh`).
- **`docs/superpowers/specs/2026-09-24-multi-cert-companion-design.md`** — the
  pivot design (10 domains), the authority for the schema.
- **`docs/superpowers/plans/2026-09-24-multi-cert-companion-schema.md`** — the
  11-task schema build plan (executed; this doc update is its closing step).
- **`docs/toeic-user-flow.md`** — the user flow told by actor, updated to the
  post-pivot flow; screen-file citations still point at the pre-pivot
  prototype.
- **`docs/superpowers/specs/2026-09-20-toeic-ui-design.md`** and
  **`docs/superpowers/plans/2026-09-20-toeic-ui-prototype.md`** — the
  pre-pivot screen/state spec and build plan (historical).

**Content is placeholder only.** Exam names like "ETS 2024 — Test 5" are
invented labels. There is no real question content and no real audio —
`prototype/assets/img/part1-sample.svg` is a hand-drawn placeholder. **Real
question sets and audio will be supplied later**; the application must be able
to ingest them (`ImportBatches` / `ImportLog` make ingest a first-class
feature), but the ingest format is not yet decided.

**Absences future work must not fabricate:** no real question content, no real
user data, no testimonials, no customer logos, no benchmarks, no pricing
research, no deployment or compliance claims, no chosen AI grading provider.

## Product Principles

1. **The diagnostic loop is free, forever.** Practice, weakness analysis, and
   placement never carry a paywall or a purchase prompt — and never write a
   ledger row, so free is a data fact. They are the reason a learner buys
   anything, so anything that monetises them defeats the funnel.
2. **Block before you charge.** Every precondition is checked *before* a
   credit is deducted, and every blocking screen states plainly that nothing
   was deducted and offers a free way forward.
3. **States are business truth, not decoration.** Timeout, cancellation,
   reload, save failure, and pending AI grading are ordinary branches of a
   long exam. Each gets a real screen and a message that says what it cost.
4. **The server owns time, answers, and balance.** The UI displays these; it
   never decides them. A screen that treats any of the three as its own state
   is wrong.
5. **Every score must be actionable.** Raw → scaled → band, shown as a
   position on a band with the distance to the next one — because a bare
   number does not tell a learner where they stand.
6. **Certificates never leak into each other.** Isolation is enforced by keys
   in the schema, so no feature built on top can mix one certificate's content,
   scoring, or rubric into another's.
7. **Aggregates are always derived.** Wallet balance, mentor rating, payout
   totals — SUM/AVG over rows, never stored counters that can drift.

## Accessibility & Inclusion

**Target conformance: WCAG 2.1 AA** — confirmed standard for this product.

Requirements already applied across the prototype:

- Every input has a `<label>`; every image has `alt`.
- Answer groups use `role="radiogroup"`; selection state is exposed via
  `aria-checked`, not colour alone.
- Focus must always be visible.
- The score band bar is `role="img"` with an `aria-label` naming the current
  band and score; the numeric scale ticks are `aria-hidden` as visual detail.
- Progress and severity are never communicated by colour alone — colour is the
  second layer, always paired with a label or number.
- Tables are used only for real tabular data (score conversions, order lists),
  never for layout.
- Skeleton animations respect `prefers-reduced-motion` by replacing motion with
  fade-in transitions.
