# Research brief — What survives the rebuild, and what must change with it

**Audience:** one external deep-research agent. **No repository access** — self-contained.

**What you deliver:** an SRS for the parts of the product that are **not** being rebuilt, but which
currently read a band or a level and will therefore break when the rebuild changes what a band means.

**Scope:** the surviving subsystems and the seam where they meet the new ones. Not placement, not the
path, not the learner interface, not the admin tools — those have their own briefs.

**Why this exists:** four sibling briefs rebuild placement, path, UI and admin. Everything else stays —
and a surprising amount of it reads a band. A rebuild that changes the meaning of a band without
changing its readers produces a product that is internally inconsistent on day one.

---

## 1. The product

A multi-certificate English exam-practice application. Five certificates:

| Certificate | Skills examined |
|---|---|
| TOEIC Listening & Reading | Listening, Reading, Speaking, Writing |
| IELTS Academic & General | Listening, Reading, Writing, Speaking |
| VSTEP (Vietnam national test) | Listening, Reading, Writing, Speaking |
| TOEIC Multi-Stage Adaptive (CAT) | Listening, Reading |
| TOEIC Speaking & Writing | Speaking, Writing |

**Domain vocabulary:** Certificate, **Skill**, **Section**, **Topic**, **Exam**, **Item**, **Lesson**,
**Attempt** (a sitting of an exam), **Practice session**, **Step**, **Free response**, **Raw score →
Scaled score → Band**, **CEFR level**, **Insight** (AI narrative over a learner's history), **Credits**
and **Ledger**.

---

## 2. What is changing, and why it breaks things

### 2.1 A band is about to mean two different things

Today a **Band** comes from a certificate's own band table: TOEIC `below_A1` 10–119 up to `C1` 945–990;
IELTS 40–50 up to 85–90; VSTEP 40–45 up to 80–100. Two certificates have bands that are *not*
CEFR-named at all — TOEIC Multi-Stage Adaptive uses `CAT_B1`/`CAT_B2`, TOEIC Speaking & Writing uses
`SW_B1`/`SW_B2`.

The placement rebuild is about to produce **CEFR** levels — A1 to C2 — per skill. Those are a different
quantity. A TOEIC score of 860 is called "B2" inside this product and is not CEFR B2.

Everything that reads a band today therefore has one of three problems: it will read a CEFR value and
treat it as a certificate band; it will keep reading certificate bands and now disagree with placement;
or it sits at a boundary where both arrive and nothing reconciles them.

### 2.2 The surviving readers

These subsystems are **not** being rebuilt and all touch a band or a level:

| Subsystem | How it reads a band / level |
|---|---|
| **Mock exam** | Scores the paper on the certificate's scale, stores `BandCode` on the attempt, shows a result page |
| **Exam review** | Shows a per-question review with the attempt's band |
| **AI insight** | Generates narrative referencing the learner's band and target |
| **Weakness analysis** | Aggregates accuracy per topic and section — no band, but it feeds the path |
| **Goal and feasibility** | Compares a starting band to a target band using cumulative study hours per band |
| **Calibration** | Detects when practice accuracy contradicts the placement band, and proposes a correction |
| **Credit ledger** | Charges for mock exams and AI gradings; unaffected by bands but affected by what counts as a paid action |
| **Mentor booking** | Reads a learner's band to describe them to a mentor |
| **Saved questions** | Attaches items to a learner; unaffected directly |
| **Placement reuse** | A result can be reused for 90 days; the date and its meaning must be visible |

### 2.3 Two of five certificates cannot produce a CEFR band

TOEIC Multi-Stage Adaptive and TOEIC Speaking & Writing have no CEFR-named band. If the product now
speaks CEFR, what do these two say? A rule that quietly maps `CAT_B2` to "B2" is a claim nobody made.

### 2.4 The confirmed defect that must not recur

A placement containing free responses cannot be completed: the free response goes to `pending_ai`, the
attempt stays `submitted` with no band, and the placement snapshot is written as `done` with a null band
— which a constraint correctly refuses. **Two writers disagree about one row.**

The same shape exists elsewhere: any subsystem that writes a snapshot beside the attempt it derives from
can drift. This brief's job includes finding them.

---

## 3. Decisions already taken

| # | Decision | Reason |
|---|---|---|
| S1 | **CEFR is the scoring axis for placement**; certificate scales stay as they are for certificate exams. | Two certificates have no CEFR-named band, so a single axis cannot be "the certificate's". |
| S2 | **A band is not a CEFR level.** No silent conversion between them. | TOEIC 785–940 is labelled "B2" internally and is not CEFR B2. A conversion is a claim that must be argued, not assumed. |
| S3 | **Where a certificate has no CEFR-named band, the product must say so** rather than inventing one. | "Cannot judge" is honest; a fabricated level is not. |
| S4 | **One writer per fact.** A derived snapshot is written by exactly one place. | Two writers disagreeing is a confirmed defect, not a theoretical risk. |
| S5 | **The ledger is append-only.** Nothing rewrites or deletes a credit transaction. | Historical rows carry reasons that must remain readable. |

---

## 4. What the SRS must answer

### 4.1 Reconciling the two band meanings

1. **What does a mock exam result show now** that placement returns CEFR? The mock exam scores one paper
   on the certificate's scale. It is a different measurement from a placement. State what each means and
   how a learner is prevented from confusing them.
2. **What does the goal and feasibility calculation do** when the starting point is a CEFR level and the
   target is a certificate band? The existing arithmetic uses cumulative study hours per band. Does a CEFR
   starting point have hours? If not, what replaces the calculation?
3. **What do the two non-CEFR certificates show** for a learner who has taken a placement? Design the
   honest answer, including the case where there is no answer.
4. **What does calibration compare** when placement says CEFR and practice accuracy is just a percentage?
   Is the comparison still meaningful, and how is a proposal worded so it does not accuse anyone?

### 4.2 The AI insight subsystem

5. **What does the insight prompt contain** when the learner's standing is four CEFR values plus an
   overall? Insights are generated per practice session, per exam, per path and per dashboard.
6. **What is forbidden in an insight?** The product's rule is that the AI never chooses content, a score,
   or a schedule — it narrates. State what that means concretely when the numbers it is given just
   changed shape.

### 4.3 The mock exam

7. **Does the mock exam need to change at all**, beyond the band wording on its result page? It reads
   level through the certificate's scale, which is unchanged.
8. **What happens to an attempt recorded under the old level model** — an attempt with `DifficultyLevel`
   1–5 era content? Does it still render, and what does its band mean?

### 4.4 Weakness analysis and saved questions

9. **Does weakness analysis change** when items carry CEFR rather than 1–5? It aggregates by topic and
   section, not by level — say whether that is still enough.
10. **What breaks for a saved question** whose level model changed?

### 4.5 Migration and coexistence

11. **What is the order of operations** for the whole rebuild? Placement, path, UI, admin and these
    surviving parts cannot all change in one deploy. State a sequence that leaves the product working at
    every step, and say which steps are behind a feature flag.
12. **What happens to existing learner data** — attempts, mastery records, paths, placement results,
    insights — when the level model changes. The product has no migrations and is not in production, so
    "start clean" is available; say what you would preserve and why.
13. **What must be true before the old code paths can be deleted**, and what is the last thing switched
    off?

### 4.6 Finding the other two-writer defects

14. **Where else does a derived snapshot get written beside its source?** The placement defect is one
    instance. Identify the others by inspection of the described subsystems and state the rule that
    prevents them.

---

## 5. Deliverable format

An SRS with:

1. Scope and non-goals, listing explicitly which subsystems are untouched.
2. **A band-and-level map** — every place a band or a level is read, what it means there now, and what it
   must mean after. A table is the right shape.
3. **Numbered, testable functional requirements** per surviving subsystem.
4. **Business rules** for the reconciliation: what a learner is told when two numbers disagree, what is
   shown when a certificate cannot produce a level, what happens to pre-rebuild records.
5. **A migration and rollout sequence** with the state of the product at each step.
6. **The two-writer audit** — every derived snapshot, its single owner, and how a second writer is
   prevented.
7. **Open questions** with options and your recommendation.
8. **References** to real sources on score reporting, crosswalk caution, and data migration sequencing.

---

## 6. Known gaps, deliberately not solved here

- **Placement, path, learner UI, admin tools.** Four sibling briefs own those.
- **Pricing.** Out of scope.
- **Accessibility and mobile.** Out of scope for v1 by the owner's decision.

---

## 7. What to be careful about

- **A band is not a CEFR level.** This is the whole brief. Do not reconcile the two with a lookup table
  unless the table is defended with sources; "TOEIC 785 means B2" is a published claim, not an obvious
  one, and the product has been wrong about it before.
- **Two of five certificates cannot produce a CEFR band.** Any design that assumes every certificate can
  is broken for 40% of the product.
- **"Cannot judge" is a valid answer.** The product already refuses to invent study hours where a
  certificate's bands carry none. Extend that honesty to levels.
- **Do not add a second writer.** A snapshot derived from an attempt is written by one place, or it
  drifts.
- **The ledger is append-only.** Do not propose deleting historical transaction reasons to tidy a
  vocabulary.
- **Old records still exist.** An attempt sat under the old level model must still render, or you are
  proposing to delete learner history. Say which you chose.
