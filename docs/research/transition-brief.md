# Research brief — Transition: what survives, what it must become, and the order it happens in

**Audience:** one external deep-research agent. **No repository access** — self-contained.

**What you deliver:** one SRS covering two halves of the same problem — the subsystems that are **not**
being rebuilt but read a band that is about to change meaning, and the **sequence** in which the whole
rebuild ships without the product contradicting itself.

**Scope:** the seam and the order. The design of placement, the path, the learner interface, the admin
tools and the content are five sibling briefs.

**Why one document:** "what reads a band" and "what order do we change things" are the same question
asked twice. A list of surviving readers is useless without knowing which of them change first, and a
sequence is fiction without knowing which readers exist. Two documents produced two migration plans that
disagreed.

---

## 1. The product, and the shape of the change

A multi-certificate English exam-practice application. Learners take a placement test, follow a learning
path, drill, and sit mock exams.

Five certificates, and **they do not all have four skills:**

| Certificate | Skills examined | Band codes |
|---|---|---|
| TOEIC Listening & Reading | Listening, Reading, Speaking, Writing | `below_A1` … `C1` (10–990) |
| IELTS Academic & General | Listening, Reading, Writing, Speaking | `B1` … `C2` (40–90) |
| VSTEP (Vietnam national test) | Listening, Reading, Writing, Speaking | `A2` … `C1` (40–100) |
| TOEIC Multi-Stage Adaptive (CAT) | Listening, Reading | `CAT_B1`, `CAT_B2` — **not CEFR** |
| TOEIC Speaking & Writing | Speaking, Writing | `SW_B1`, `SW_B2` — **not CEFR** |

**Domain vocabulary:** Certificate, **Skill**, **Section**, **Topic**, **Exam**, **Item**, **Lesson**,
**Attempt**, **Practice session**, **Step**, **Free response**, **Raw score → Scaled score → Band**,
**CEFR level**, **Insight**, **Credit**, **Ledger**.

**What is being rebuilt, and by whom:**

| Area | What changes | Sibling brief |
|---|---|---|
| Content | Items gain CEFR levels; shared pool; rubric descriptors | content |
| Placement | Two-stage adaptive, four skills, per-skill CEFR | placement |
| Path | Sliding four-week window, skill-weighted steps | path |
| Learner UI | Every screen showing a band, a plan, a session, a score | ui |
| Admin | Authoring that enforces the specification, rubric authoring | admin |

**This brief covers everything else.**

---

## 2. What is changing underneath everything

### 2.1 A band is about to mean two different things

Today a **Band** is a row in a certificate's own band table: TOEIC `below_A1` 10–119 up to `C1` 945–990;
IELTS 40–50 up to 85–90; VSTEP 40–45 up to 80–100.

The placement rebuild produces **CEFR** — A1 to C2 — per skill. That is a different quantity. A TOEIC
score of 860 is called "B2" inside this product and is **not** CEFR B2.

So there is a period during which both meanings exist. Every reader either works in both worlds or must
not ship until the world changes — and the worst outcome is not a crash, it is a learner seeing two
different answers to "what is my level" on the same day.

### 2.2 The surviving readers

These are **not** being rebuilt, and every one of them touches a band or a level:

| Subsystem | How it reads a band or level |
|---|---|
| **Mock exam** | Scores one paper on the certificate's scale, stores the attempt's band, renders a result page |
| **Exam review** | Per-question review alongside the attempt's band |
| **AI insight** | Narrative quoting the learner's band and target, generated per session, per exam, per path, per dashboard |
| **Weakness analysis** | Aggregates accuracy per topic and section — no band, but it feeds the path |
| **Goal and feasibility** | Compares a starting band to a target band using cumulative study hours per band |
| **Calibration** | Detects when practice accuracy contradicts the placement band and proposes a correction |
| **Credit ledger** | Charges for mock exams and AI gradings; unaffected by bands but affected by what counts as a paid action |
| **Mentor booking** | Describes the learner's band to a mentor |
| **Saved questions** | Attaches items to a learner |

### 2.3 Two of five certificates cannot produce a CEFR band

TOEIC Multi-Stage Adaptive and TOEIC Speaking & Writing have **no CEFR-named band**. If the product now
speaks CEFR, what do these two say? A rule that quietly maps `CAT_B2` to "B2" is a claim nobody made.

### 2.4 The content does not exist yet

The new pool must be authored, validated, reviewed and loaded before placement or path can use it. The
current bank is 116 items, **zero at the hardest level**, and **twelve of fifteen topics empty**. A
four-skill placement cannot launch against that.

### 2.5 The learner's history is load-bearing

Attempts exist with bands recorded under the old model. Mastery records are keyed to today's topics.
Paths exist with steps. Insights exist quoting numbers that will no longer mean the same thing.

Deleting them is possible — the product is not in production — but it is not the only option, and the
choice must be deliberate rather than a cascade.

### 2.6 There are no migrations

The schema is a single SQL file applied to a fresh database. No migration framework. That makes "start
clean" cheap and "preserve history" expensive. Say which you would choose, and for what.

### 2.7 A confirmed defect already shows how this fails

A placement containing free responses cannot be completed: the attempt stays `submitted` with no band,
and a second writer marks the placement `done` with a null band, which a constraint correctly refuses.
**Two writers disagreeing about one row.** Any transition that has two writers for one fact reproduces
this class of failure.

---

## 3. Decisions already taken

| # | Decision | Reason |
|---|---|---|
| T1 | **CEFR is the scoring axis for placement**; certificate scales stay for certificate exams. | Two certificates have no CEFR-named band, so a single axis cannot be "the certificate's". |
| T2 | **A band is not a CEFR level.** No silent conversion. | TOEIC 785–940 is labelled "B2" internally and is not CEFR B2. A conversion is a claim to be argued. |
| T3 | **Where a certificate has no CEFR-named band, say so** rather than inventing one. | "Cannot judge" is honest; a fabricated level is not. |
| T4 | **One writer per fact.** A derived snapshot is written by exactly one place. | Two writers disagreeing is a confirmed defect. |
| T5 | **The ledger is append-only.** | Historical rows carry reasons that must stay readable. |
| T6 | **Delete-and-rebuild, not incremental extension.** | The old placement and path carry design faults a patch preserves. |
| T7 | **Old code paths are deleted once their replacement is green**, not left behind a flag forever. | Two paths for one behaviour drift apart. |
| T8 | **The product is not in production**, so downtime is acceptable and "start clean" is available. | The cheapest moment this migration will ever have. |

---

## 4. What the SRS must answer

### 4.1 Reconciling the two band meanings

1. **What does a mock exam result show** when placement returns CEFR? The mock scores one paper on the
   certificate's scale — a different measurement. State what each means and how a learner is prevented
   from confusing them.
2. **What does the goal and feasibility calculation do** when the starting point is a CEFR level and the
   target is a certificate band? The existing arithmetic uses cumulative study hours per band. Does a
   CEFR starting point have hours? If not, what replaces it?
3. **What do the two non-CEFR certificates show** for a learner who has taken a placement? Design the
   honest answer, including when there is none.
4. **What does calibration compare** when placement says CEFR and practice accuracy is a percentage? Is
   the comparison still meaningful, and how is the proposal worded so it does not accuse anyone?

### 4.2 The AI insight subsystem

5. **What does an insight prompt contain** when the learner's standing is four CEFR values plus an
   overall?
6. **What is forbidden in an insight?** The rule is that the AI never chooses content, a score or a
   schedule — it narrates. State what that means concretely now the numbers changed shape.

### 4.3 The untouched exam surfaces

7. **Does the mock exam need to change at all**, beyond band wording on its result page?
8. **What happens to an attempt recorded under the old level model** — does it still render, and what
   does its band mean?
9. **Does weakness analysis change** when items carry CEFR rather than 1–5? It aggregates by topic and
   section — say whether that is still enough.
10. **What breaks for a saved question** whose level model changed?

### 4.4 The sequence

11. **What order do the six areas change in**, and what does the product look like at each step? A step
    must leave the product usable or explicitly take it offline for a stated window.
12. **Which steps can ship behind a flag, and which cannot?** A flag is only useful if both paths
    coexist without disagreeing.
13. **What is the first thing to ship**, and what does it prove?
14. **What is the last thing switched off**, and what must be true before the old path is deleted?

### 4.5 Content before code

15. **How much content must exist before placement can launch**, and before the path can launch? The
    content brief derives the volume; you decide the threshold.
16. **What does the product do while content is thin** — partial rollout to one certificate, restricted
    level range, or nothing until the pool is full? A partial launch is possible and may be right.
17. **How is content versioned** so a bad batch can be withdrawn without losing learner history that
    references it?

### 4.6 Old data

18. **What happens to existing attempts** whose bands were recorded under the old model — rendered as-is,
    converted, hidden, or deleted?
19. **What happens to existing mastery records** keyed to the old topic taxonomy?
20. **What happens to existing learners' paths** — regenerated, frozen, or dropped?
21. **What happens to stored AI insights** that quote band values which will mean something else?
22. **What is the rule** that decides preserve versus drop for each of the above? State it once rather
    than per table.

### 4.7 Coexistence and the two-writer problem

23. **During the transition, how is it guaranteed that only one writer produces a derived snapshot** —
    placement result, attempt band, path step state?
24. **What invariant can be asserted at write time** so a two-writer bug fails loudly rather than silently
    at read time? The existing constraints caught one instance; say whether that generalises.
25. **Where else does a derived snapshot get written beside its source?** The placement defect is one
    instance. Identify the others and state the rule that prevents them.
26. **What is the rollback story** for each step, given there are no migrations?

### 4.8 Verification

27. **How do you know a step is safe to ship?** The product has a full test suite and a schema harness;
    say what evidence is required beyond "tests pass".
28. **What smoke check** proves the product works after a step, for a learner mid-path and for one who
    has never taken a placement?
29. **How is "the product is consistent" defined**, so it can be checked rather than assumed? For
    example: no screen shows two different levels for the same skill on the same day.

---

## 5. Deliverable format

One SRS with:

1. Scope and assumptions, listing explicitly which subsystems are untouched.
2. **A band-and-level map** — every place a band or level is read, what it means now, and what it must
   mean after. A table is the right shape.
3. **Numbered, testable functional requirements** per surviving subsystem.
4. **Business rules** for reconciliation: what a learner is told when two numbers disagree, what is shown
   when a certificate cannot produce a level, what happens to pre-rebuild records.
5. **The step sequence** — each step with what changes, what the product looks like after, what is
   flagged, how it is verified, and how it rolls back.
6. **A data disposition table** — every table holding a band, a level or a derived value, and
   preserve/convert/drop for each, with the rule that decided it.
7. **The single-writer guarantee** — how it is enforced during and after the transition.
8. **Content readiness gates** — how much content, for which certificates, before which step.
9. **Open questions** with options and your recommendation.
10. **References** to real sources on score reporting, crosswalk caution, expand–contract migration, and
    feature-flag sequencing.

---

## 6. Known gaps, deliberately not solved here

- **The design of any rebuilt subsystem.** Five sibling briefs own those.
- **Pricing and credit economics.** Out of scope.
- **Accessibility and mobile.** Out of scope for v1 by the owner's decision.

---

## 7. What to be careful about

- **A band is not a CEFR level.** This is the spine of the brief. Do not reconcile the two with a lookup
  table unless the table is defended with sources; "TOEIC 785 means B2" is a published claim, not an
  obvious one, and this product has been wrong about it before.
- **Two of five certificates cannot produce a CEFR band.** Any design assuming every certificate can is
  broken for 40% of the product.
- **"Cannot judge" is a valid answer.** The product already refuses to invent study hours where bands
  carry none. Extend that honesty to levels.
- **Do not add a second writer.** A snapshot derived from an attempt is written by one place, or it
  drifts.
- **The ledger is append-only.** Do not propose deleting historical transaction reasons to tidy a
  vocabulary.
- **Old records still exist.** An attempt sat under the old level model must still render, or you are
  proposing to delete learner history.
- **"Start clean" is available but not free.** It costs every learner's history. Say what you would
  preserve and why, rather than defaulting to either extreme.
- **A flag is not a plan.** Two code paths coexisting is a temporary state with a defined end.
- **Do not sequence by what is easiest to build.** Sequence by what keeps the product consistent.
- **Content is the long pole.** Code can be written in parallel; validated items cannot. If your sequence
  assumes content will be ready, say so as an assumption.
