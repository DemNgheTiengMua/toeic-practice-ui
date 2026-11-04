# Extra Efficiency — Consolidated Product SRS

**Version:** 1.0 · **Date:** 2026-11-04
**Consolidates and supersedes `docs/research/output/1.md`–`6.md` for implementation.** The originals remain
the record of what was researched; they are not edited. Where this document and an original disagree, this
document governs. Where this document is silent, the original governs.

**Substrate:** SQL Server 2019+, EF Core database-first. Where an original specified PostgreSQL, the mechanism
is restated in SQL Server terms (see PRN-01). The schema of record is a single applied SQL file, not a
migration framework.

---

## 1. Normalization Principles

These are the rules every other chapter obeys. They are not prefatory decoration; a requirement that
contradicts one of them is a defect in the requirement.

| ID | Principle | Why it is load-bearing |
|---|---|---|
| **PRN-01** | **SQL Server, not PostgreSQL.** Every mechanism the originals expressed in PostgreSQL terms is restated in SQL Server terms. `JSONB` → `NVARCHAR(MAX)` validated as JSON. `INTERVAL` → `DATEADD`/`DATEDIFF`. `FOR UPDATE` → `UPDLOCK, ROWLOCK`. `RAISE EXCEPTION` → `THROW`. `pg_dump` → backup/restore. **One exception is not portable:** `SET LOCAL app.current_writer_service` has no transaction-local equivalent — `sp_set_session_context` survives commit and rollback, so the single-writer assertion uses a parameter passed into each write procedure plus a trigger that reads it, and this difference is stated wherever the mechanism appears. | The product runs on one engine. A specification written for another engine ships broken. |
| **PRN-02** | **One writer per derived fact.** A snapshot derived from a source is written by exactly one place. Two writers disagreeing about one row is a confirmed production defect in this product, not a hypothetical. | It has already happened once, with placement completion. |
| **PRN-03** | **A band is not a CEFR level.** A certificate band is a row in that certificate's own band table. A CEFR level is a position on the Council of Europe six-level scale. They are different quantities measured by different instruments. No silent conversion between them exists anywhere in this document. | The product has been wrong about this mapping before. TOEIC 785–940 is labelled "B2" internally and is not CEFR B2. |
| **PRN-04** | **"Cannot judge" is a valid answer.** Where a certificate's bands carry no study-hour evidence, or a skill could not be measured, the system says so. It does not substitute the nearest available number. | A fabricated level is worse than an absent one. |
| **PRN-05** | **Content is certificate-agnostic.** An item belongs to a skill, a CEFR level, and a topic. It does not belong to a certificate. A certificate declares which skills it examines and draws from those skills' pools. | One pool serves five certificates; two of them examine only two skills. |
| **PRN-06** | **Free is a data fact, not a policy.** Placement, practice, the learning path, weakness analysis, and lessons never write a credit ledger row. They do not have a paywall check that happens to pass; the code path does not exist. | A free flow that consults a balance will eventually consult a wrong one. |
| **PRN-07** | **Deterministic code decides; AI grades and narrates.** Item selection, stage routing, score aggregation, band resolution, mastery computation, and schedule synthesis are code. An AI model scores free responses against a written rubric and writes prose about stored facts. It never chooses what a learner studies, never assigns a score, never invents a level. | An AI that picks items cannot be audited; an AI that narrates stored numbers can be checked against them. |
| **PRN-08** | **No crosswalk without a defended source.** A per-skill cut score taken from a published standard-setting study is a crosswalk. A total-score-to-CEFR lookup is not; it is an invention. | The class of table that produced the current defect. |
| **PRN-09** | **Lossless against the originals.** This document carries every design decision, threshold, and refusal from the six originals. It resolves contradictions and converts platform syntax; it does not summarize away reasoning or drop options that were rejected. Where an original's reasoning matters to why a rule exists, the reasoning is kept. | Six independent documents disagree; the disagreements are the most valuable part. |

---

## 2. Scope

### 2.1 What is rebuilt

Five subsystems are replaced rather than extended. Each carries design faults that a patch would preserve.

| Area | What changes | Chapter |
|---|---|---|
| **Content warehousing** | Items gain CEFR levels; one shared pool; rubrics gain band descriptors | 5 |
| **Placement** | Two-stage multistage adaptive, four skills, per-skill CEFR | 6 |
| **Learning path** | Sliding four-week window, skill-weighted steps, mastery-driven sizing | 7 |
| **Learner interface** | Every screen that shows a band, a plan, a session, or a score | 8 |
| **Staff interface** | Authoring that enforces the specification; rubric authoring; publish gate | 9 |

### 2.2 What survives

Nine subsystems are not rebuilt. Every one of them reads a band or a level, whose meaning changes underneath
it, so every one is adapted — none is left untouched, and none is replaced.

| Subsystem | Disposition |
|---|---|
| Mock exam scoring and result display | Adapt scoring projection and result presentation |
| Exam review | Adapt item level and version binding |
| AI diagnostic narrative (insight) | Adapt prompt schema and output guardrails |
| Weakness analysis | Adapt to orthogonal topic × level |
| Goal and feasibility | Adapt starting coordinate from band to CEFR |
| Calibration | Adapt to level-conditioned accuracy |
| Credit ledger | Preserve financial structure exactly; enforce append-only and free-action rules |
| Mentor booking | Adapt learner level summary payload |
| Saved questions | Adapt to `(item_id, version)` and CEFR |

Chapter 10 governs how the two meanings coexist during the transition.

### 2.3 Out of scope

Three areas are excluded by owner decision. Hooks for the third exist inside the chapters that own the affected
screens and are not collected here.

- **Pricing and credit economics.** Packages, prices, order reconciliation, payouts. Excluded entirely.
- **Mobile.** Desktop-first at 1440×900 is a recorded trade-off, not an oversight. Chapter 8 §8.7 states
  exactly which mechanisms break on a narrow viewport.
- **Accessibility for v1.** Not certification-compliant by decision. Chapter 8 §8.10 lists four non-breaking
  v2 hooks that must be present in the v1 data model so enabling them later is not a rewrite.
- **Server-side device storage.** Device identifiers never reach the server, are never stored, and are never
  synchronised across machines or browsers (LRN-42). The server records only the gate verdict and the
  recoveries.
- **Audio processing.** No noise suppression, gain normalisation, or echo cancellation beyond what the browser
  applies by default.
- **Operating-system mixer inspection.** The product cannot tell whether the operating system has muted a
  device, and does not try.
- **Proctoring.** No camera, no screen capture, no tab-switch detection.
- **Changing the rubric or scoring.** No rubric criterion, band table, scale, aggregation rule, or overall
  computation changes because of §8.9. "Not measured" already exists in the result model; this chapter only
  arranges for it to be earned honestly rather than by silence.

---

## 3. Vocabulary

This chapter exists because six documents use six dialects. Where this document deliberately uses a word
differently from `CONTEXT.md`, the divergence is stated with its reason — a reader coming from the repo
glossary must not silently misread a requirement.

### 3.1 Canonical terms

| Term | Definition |
|---|---|
| **Certificate** | An English qualification the product prepares for. Defined entirely by data: which skills it examines, its band table, its scale, its aggregation rule. Five configurations exist. |
| **Skill** | One modality within a certificate: listening, reading, speaking, or writing. A certificate examines two or four. |
| **Section** | A part of a skill with its own declared item count, timing, and question-type constraints. |
| **Topic** | A communicative micro-skill within a skill — 20 canonical topics, 6 listening, 6 reading, 4 speaking, 4 writing. A topic belongs to the language, not to a certificate. |
| **Cell** | The intersection `(topic, cefr_level)` — 120 cells per skill-matrix. The unit of content coverage and of mastery. |
| **Item** | One objective assessment unit: a stem, four options, one key, an explanation, an evidence span. Belongs to one skill, one CEFR level, one topic. Never to a certificate, never to an exam at authoring time. |
| **Stimulus** | A shared passage or audio track serving 2–4 child items. Carries its own level. |
| **Task** | A speaking or writing prompt scored by rubric rather than by key. |
| **Exam** | An assembled set of items for one certificate, with a purpose: mock, placement, speaking practice, or writing practice. Assembly is a query over the pool, not an ownership relation. |
| **Attempt** | One learner's sitting of an exam, with a server-owned deadline. |
| **Practice session** | A drill over one `(skill, topic, cefr_level)` coordinate. Free. Graded immediately for objective skills, asynchronously for productive ones. |
| **Step** | One scheduled activity on a learning path, pointing at a `StepCoordinate`. |
| **Lesson** | A leveled instructional unit for one cell, with a primary and a remedial sub-module. |
| **CEFR level** | One of A1, A2, B1, B2, C1, C2 — or Pre-A1 as a reportable floor. A position on the Council of Europe scale. |
| **Band** | A row in a certificate's own band table (e.g. TOEIC `B2` = 785–940). A different quantity from a CEFR level. |
| **Raw score → Scaled score → Band** | The certificate's own two-step conversion. Applies to certificate exams only. |
| **Free response** | A learner's spoken or written answer, graded by AI against a rubric. |
| **Grading** | AI scoring of one free response against written rubric criteria at a fixed temperature. |
| **Insight** | AI narrative over stored facts. Never a number, never a decision. |
| **Weakness analysis** | Per-topic and per-section accuracy computed by SQL views. Deterministic; the numbers an insight reads. |
| **Credit / Ledger** | In-app payment unit; append-only record of movements. Balance is always a sum, never a column. |

### 3.2 Deliberate divergences from the repo glossary
| Word | Repo meaning (`CONTEXT.md`) | This document's meaning | Why it differs |
|---|---|---|---|
| **Practice session** | A free drill over one section | A drill over one `(skill, topic, cefr_level)` coordinate, whether launched from the path calendar or freely | The rebuild removes the section as the unit of drilling; the topic cell replaces it. A path step and a free drill are the same execution, differing only in origin. |
| **Band** | A certificate's band | Reserved **exclusively** for a certificate's band | The rebuild introduces CEFR levels. If "band" also meant CEFR level, PRN-03 would be unenforceable in prose. |
| **Question** | The item entity | Avoided except when quoting the repo | "Item" is the unit the warehouse stores; "question" now implies an exam-owned thing, which by PRN-05 no longer exists at authoring time. |
| **Level** | (unused) | A CEFR level, always | One word, one meaning. |
| **Section** | A part of a skill | Unchanged | — |

## 4. The Level Model

### 4.1 A CEFR level is a falsifiable claim

**LVL-01 —** Every item, task, stimulus, and lesson carries exactly one CEFR level from A1 to C2. Pre-A1 exists
only as a reportable floor, never as an authored level. There is no separate 1–5 difficulty scale; the rebuild
removes it rather than mapping it (PRN-08).

**LVL-02 —** "This item is B1" is a claim that the item satisfies three simultaneous, measurable constraints.
An author and a reviewer apply these; an automated gate checks them.

| Band | Lexical profile | Syntactic complexity | Cognitive operation required | Distractor construction |
|---|---|---|---|---|
| **A1** | ≥95% tokens within A1 core list (top 800 families); no abstract idioms | MLTU 5.0–7.5 words; simple declarative/interrogative; coordination limited to *and, but, or* | **Lexical recognition & verbatim match** — locate an isolated word, number, time, or set phrase in one clause | Distractors in the same semantic category (days, prices, places) but absent from the stimulus or explicitly negated |
| **A2** | ≥92% within A1–A2 (top 1,800); concrete everyday and workplace nouns | MLTU 7.5–10.5; basic subordination (*because, when, so, if* Type 0/1); ≤1.3 clauses/T-unit | **Local paraphrase & routine transaction** — connect a stem by direct synonym to one sentence in a predictable notice, email, or short dialogue | One verbatim keyword trap paired with a wrong action or subject; two plausible routine alternatives |
| **B1** | ≥90% within A1–B1 (top 3,000); ≤3% B2 tokens; transparent phrasal verbs | MLTU 10.5–13.5; defining relatives, indirect speech, modal possibility, passive; 1.3–1.6 clauses/T-unit | **Cross-sentence synthesis & straightforward pragmatics** — combine 2–3 adjacent sentences to identify a main point, intent, or direct cause | All three distractors reuse concepts from the stimulus; at least two require ruling out temporal reversal or speaker misattribution |
| **B2** | ≥88% within A1–B2 (top 5,000 + AWL 1–5) | MLTU 13.5–17.0; non-defining relatives, participles, mixed conditionals, nominalization; 1.6–2.0 clauses/T-unit | **Bridging inference & discourse stance** — infer unstated premises across paragraphs, evaluate pros and cons, detect hedging or disagreement | Partial truths (accurate for one detail, too narrow or broad for the claim), causal inversions, unsupported assumptions |
| **C1** | ≥85% within A1–C1 (AWL 6–10, idiomatic expressions, domain collocations) | MLTU 17.0–21.0; dense hypotaxis, clefts, concessive subordination; >2.0 clauses/T-unit | **Pragmatic implication & rhetorical evaluation** — decode implied meaning, irony, tone shifts, counter-argument structure | All four options are sophisticated paraphrases; distractors hinge on degree of modality (*inevitable* vs *probable*) or rhetorical scope |
| **C2** | Unrestricted; low-frequency academic, legal, technical, figurative lexis; nuanced register | MLTU >20.0; embedded nominal groups, negative inversion, ellipsis, appositive chains | **Abstract critical synthesis & micro-nuance** — reconstruct competing implicit frameworks, identify unstated assumptions, distinguish fine stylistic irony | Near-miss analytical interpretations failing only on an implicit condition or secondary entailment |

### 4.2 Level is modality-specific

**LVL-03 —** An A2 Listening item is not defined by the same textual parameters as an A2 Reading item. Spoken
input is linear, transient, and paced by the speaker; written input is static and re-readable. At every band,
listening stimuli carry a lower syntactic subordination index (≈15–20% shorter MLTU) and a higher
concentration of high-frequency tokens than reading stimuli at the identical band.

| Band | Listening envelope | Reading envelope | Speaking output demand | Writing output demand |
|---|---|---|---|---|
| **A1** | 100–120 wpm; clear articulation; pauses ≥0.8 s between clauses; 40–65 words (20–35 s) | 60–100 words; FK grade 1.5–3.0; notices, signs, 3-sentence messages | 15–30 s; isolated phrases and basic SVO on personal detail | 30–50 words; form completion or 3–5 basic clauses |
| **A2** | 115–130 wpm; minimal elision; 60–95 words (30–45 s) | 120–180 words; FK 3.5–5.5; routine emails, schedules, short memos | 30–45 s; linear clauses linked by *and, then, because* | 60–90 words; short transactional message |
| **B1** | 135–150 wpm; standard contractions; 110–185 words (45–75 s) | 220–300 words; FK 6.0–8.0; multi-paragraph correspondence or informational article | 45–60 s; connected narrative or opinion with reasons | 110–150 words; structured email or short opinion text |
| **B2** | 150–165 wpm; mild reduction; 150–240 words (60–90 s) | 320–450 words; FK 8.5–11.0; complex reports, editorials, multi-viewpoint policy | 60–90 s; systematic evaluation of options, defending a view | 180–250 words; organised essay or formal report |
| **C1** | 165–180 wpm; varied regional accents; implicit prosodic irony; 240–340 words (90–120 s) | 450–600 words; FK 11.5–14.0; dense academic prose, policy critique | 90–120 s in practice; **60 s in placement** (§6.7) | 250–320 words; analytical or persuasive essay |
| **C2** | 175–195 wpm; unscripted debate; dense ellipsis; 260–360 words (90–120 s) | 500–650 words; FK >14.0; abstract theoretical, legal, or philosophical text | 90–120 s in practice; **60 s in placement** | 280–350 words; incisive critique or synthesis |

**LVL-04 —** The C1 and C2 speaking rows carry two recording figures. The higher figure applies to practice;
placement is capped at 60 seconds (PLC-31). This is a deliberate scope difference, not an inconsistency: a
placement task must be bounded so conditions are equal across candidates, while a practice task may be as long
as the learner needs.

### 4.3 Shared stimuli carry two levels

**LVL-05 —** A shared passage or audio track carries `stimulus_level` (L_s). Each child item carries its own
`item_level` (L_i). All routing, scoring, and mastery computation operate on L_i, never on L_s.

**LVL-06 — the ±1 envelope.** When L_s ∈ {A1, A2}, every child item must have L_i = L_s. When L_s ≥ B1, L_i ∈
{L_s − 1, L_s, L_s + 1}. At least two-thirds (≥66%) of children on any stimulus must equal L_s exactly.

**LVL-07 —** The envelope exists because a stimulus imposes a floor and a ceiling on what can be asked of it. A
130-word A2 notice cannot support a valid C1 inference item; a 550-word C1 text imposes too heavy a decoding
burden for an A2 learner even when the stem asks for a stated detail.

### 4.4 The topic axis is orthogonal to the level axis

**LVL-08 —** Twenty canonical topics cross six CEFR levels, yielding 120 cells. "Verb tense at B1" is not a
different topic from "verb tense at B2"; it is topic `R-GRAM` at `cefr_level = B1`. This lets the system
diagnose that a learner has mastered `R-INF` at B1 while remaining weak in `R-INF` at B2, without creating six
disconnected topic silos per topic.

**LVL-09 —** `topic_id` is shared across all certificates (PRN-05). Domain flavour — workplace, academic,
general — is non-structural metadata carried per stimulus, balanced across cells by §5.9, and never changes
what the item measures.

### 4.5 Certificate scale alignment

**LVL-10 —** Internal measurement never touches a certificate's scale. Items, placement, mastery, and the path
operate exclusively on CEFR levels and the per-skill position inside a level. Certificate scales appear only
when a result is projected for a learner's own certificate.

**LVL-11 —** Projection is per-skill, from the continuous CEFR coordinate, using that certificate's official
aggregation rule: sum the two scaled section estimates for a TOEIC Listening & Reading total; average four
skills and round to the nearest 0.5 for IELTS and VSTEP; report separate skill scores with no combined total
for TOEIC Speaking & Writing.

**LVL-12 — the crosswalk is per-skill cut scores, never a total-score lookup.** The cut scores in §4.6 are
taken from published standard-setting studies, not from arithmetic on a total. The current product labels a
TOEIC Listening & Reading total of 785–940 as "B2"; that is invalid and is removed. The 785 figure is the sum
of two independent per-section minimums (Listening 400 + Reading 385), and a candidate scoring 450 Listening
and 335 Reading reaches 785 while remaining below B2 in Reading.

**LVL-13 —** The crosswalk table lives in the database and is read-only at the presentation layer. It is a
lookup, not a computation, and it is versioned so a recorded projection can be reproduced.

**LVL-14 —** TOEIC Listening, Reading, Speaking, and Writing do not measure C2; their scales top out at C1.
VSTEP certifies B1 through C1 and awards nothing below 4.0. IELTS alone spans C1 to C2. Where a certificate
cannot express a level, the presentation says so (PRN-04); it never displays a C2 band for a TOEIC total.

### 4.6 Per-skill cut scores and projection table

| CEFR | TOEIC Listening (5–495) | TOEIC Reading (5–495) | TOEIC Speaking (0–200) | TOEIC Writing (0–200) | IELTS (0–9) | VSTEP (0–10) |
|---|---|---|---|---|---|---|
| **Pre-A1** | 5–55 | 5–55 | 0–40 | 0–20 | 0.0–1.5 *(uncertified)* | 0.0–1.5 *(uncertified)* |
| **A1** | 60–105 (cut ≥60) | 60–110 (cut ≥60) | 50–80 (cut ≥50) | 30–60 (cut ≥30) | 2.0–2.5 | 2.0–2.5 *(Level 1)* |
| **A2** | 110–270 (cut ≥110) | 115–270 (cut ≥115) | 90–110 (cut ≥90) | 70–110 (cut ≥70) | 3.0–3.5 | 3.0–3.5 *(Level 2)* |
| **B1** | 275–395 (cut ≥275) | 275–380 (cut ≥275) | 120–150 (cut ≥120) | 120–140 (cut ≥120) | 4.0–5.0 | 4.0–5.5 *(Level 3)* |
| **B2** | 400–485 (cut ≥400) | 385–450 (cut ≥385) | 160–170 (cut ≥160) | 150–170 (cut ≥150) | 5.5–6.5 | 6.0–8.0 *(Level 4)* |
| **C1** | 490–495 (cut ≥490) | 455–495 (cut ≥455) | 180–200 (cut ≥180) | 180–200 (cut ≥180) | 7.0–8.0 | 8.5–10.0 *(Level 5)* |
| **C2** | *not measured; display caps at 495* | *not measured; display caps at 495* | *not measured; display caps at 200* | *not measured; display caps at 200* | 8.5–9.0 | *not measured; display caps at 10.0* |

**LVL-15 —** The C2 row is a display ceiling, not a level. It records the highest value the certificate's scale
can show when a learner's underlying CEFR coordinate is C2. No certificate except IELTS emits a C2 **level**.

**LVL-16 —** For the two certificates with no CEFR-named band (`CAT_B1`/`CAT_B2`, `SW_B1`/`SW_B2`), the
per-skill target coordinate needed for feasibility is null by default, and the feasibility verdict is
`cannot_judge` (PRN-04). Populating it requires an owner decision, not a lookup.

---

## 5. The Content Pool

### 5.1 One pool, three coordinates

**CNT-01 —** Every item, task, stimulus, and lesson is authored against `(skill, cefr_level, topic_id)`, plus
an orthogonal `domain_context`. No item is authored "for TOEIC". Any certificate whose skills include a given
skill draws from that skill's pool.

**CNT-02 —** The pool is populated from four JSON files, one per skill. Bulk load is the only practical way to
fill hundreds of items; JSON represents shared passages and multi-item groups without duplication, which CSV
cannot do. Per-line validation errors are mandatory: a file of 300 items where item 247 is wrong must not lose
the 246 before it.

**CNT-03 —** The upload schema is versioned (`UnifiedSkillContentPool` 2.0.0). Each record carries a
`lifecycle_status`, a `content_tier` (production or demo), a `batch_tag`, and an ID prefixed
`stim_`/`item_`/`task_`/`les_` followed by `prod_`/`demo_`. The prefix is machine-checked: a production exam
may not bind a demo item.

### 5.2 The twenty canonical topics

| Skill | Topic | Construct measured |
|---|---|---|
| **Listening** | `L-GIST` | Global gist, purpose, macro-topic |
| | `L-DET` | Explicit factual detail and extraction |
| | `L-INF` | Pragmatic implication and logical inference |
| | `L-ATT` | Speaker stance, attitude, prosodic emotion |
| | `L-FUNC` | Communicative function and speech acts |
| | `L-ORG` | Discourse structure, signposting, synthesis |
| **Reading** | `R-GIST` | Global thesis, purpose, paragraph main idea |
| | `R-DET` | Local factual retrieval and paraphrase matching |
| | `R-INF` | Bridging inference and logical deduction |
| | `R-ATT` | Authorial stance, rhetorical purpose, tone |
| | `R-COH` | Discourse cohesion, reference, text insertion |
| | `R-GRAM` | Morphosyntactic structure and lexical precision |
| **Speaking** | `S-DESC` | Informational description and personal framing |
| | `S-INT` | Transactional interaction and option negotiation |
| | `S-ARG` | Sustained monologic argumentation and justification |
| | `S-SYN` | Integrated summary and visual/data interpretation |
| **Writing** | `W-CORR` | Transactional correspondence and register control |
| | `W-DATA` | Visual data, process, and informational synthesis |
| | `W-ARG` | Thesis-driven persuasive and opinion argumentation |
| | `W-DISC` | Multi-perspective problem-solution and evaluation |

**CNT-04 —** The grain is 4–6 topics per skill deliberately. Fewer conflates distinct cognitive failures (a
lexical decoding failure with a pragmatic inference failure); more fragments observations across so many
buckets that statistical mastery never converges.

**CNT-05 —** The 15 legacy categories are replaced. They were arbitrary surface labels: twelve of the fifteen
held no items at all, so "your weakest topic" was computed over three populated buckets.

### 5.3 Item format

**CNT-06 —** Every objective item in the shared pool is a **four-option single-best-answer multiple-choice
item** with a 25% guessing floor. Certificate-specific variants that would change that floor — IELTS
True/False/Not Given, TOEIC Part 2 three-option question-response — are authored as four-option cognitive
equivalents: statement verification under `R-DET`, conversational speech-act matching under `L-FUNC`.

**CNT-07 —** The 25% floor is a load-bearing constant. Stage 2 band sizing (§6.4), the mastery threshold
(§7.5), and the monotonicity rule (§6.5) are all derived from it. Introducing a second option count would
require rederiving all three.

**CNT-08 —** Question types by skill, with stimulus and timing constraints:

| Skill | Type | Topics | Stimulus | Stem / option limits | Time |
|---|---|---|---|---|---|
| Listening | `short_exchange` | L-FUNC, L-DET, L-ATT | 20–45 s, 2 speakers, 2–4 turns | stem ≤18 words; options ≤12 | ~45 s |
| Listening | `extended_dialogue` | L-GIST, L-DET, L-INF, L-ATT, L-ORG | 45–120 s, 2–3 speakers, 2–4 children | stem ≤22; options ≤15 | ~50–55 s |
| Listening | `monologue_talk` | L-GIST, L-DET, L-INF, L-ATT, L-ORG | 45–120 s, 1 speaker, 2–4 children | stem ≤22; options ≤15 | ~50–55 s |
| Reading | `sentence_cloze` | R-GRAM | standalone 15–45 words, one blank, 1 item | options 1–5 words | 30–40 s |
| Reading | `passage_comprehension` | R-GIST, R-DET, R-INF, R-ATT | 60–650 words, 2–4 children | stem ≤25; options ≤18 | ~60 s |
| Reading | `text_insertion_cohesion` | R-COH | 120–650 words, four numbered markers | four options = four positions | 55–65 s |
| Speaking | `descriptive_monologue` | S-DESC | prompt 30–60 words + optional image | 3 bulleted sub-prompts | prep 30–45 s; record 45–60 s |
| Speaking | `situational_negotiation` | S-INT | scenario 50–90 words + 3 options | justify one, argue against two | prep 45–60 s; record 60–90 s |
| Speaking | `argumentative_monologue` | S-ARG | proposition 40–70 words + 3 points | sustained argument + counter | prep 60 s; record 90–120 s |
| Speaking | `visual_data_synthesis` | S-SYN | table or chart + query 40–70 words | synthesise, resolve conflict, recommend | prep 45 s; record 60–90 s |
| Writing | `transactional_correspondence` | W-CORR | prompt 60–100 words, 3 required actions | 50–150 words by band | 8–15 min |
| Writing | `visual_data_synthesis` | W-DATA | chart/table/process + prompt 40–70 words | 60–180 words | 10–20 min |
| Writing | `persuasive_essay` | W-ARG | thesis or policy claim 40–80 words | 80–300 words by band | 14–30 min |
| Writing | `analytical_evaluation_essay` | W-DISC | multi-perspective prompt 50–90 words | 80–320 words by band | 14–30 min |

### 5.4 The two hard shapes

**CNT-09 — shared audio.** One audio asset serves 2–4 child items. No stem or option may reference a numeric
timestamp ("at 0:42, why does the speaker pause?") and no item may require scrubbing: audio plays linearly
from start to finish as it does in an authentic sitting. When an `L-ATT` or `L-FUNC` item tests the meaning of
a specific utterance, the stem quotes the phrase verbatim or anchors by speaker turn and topic.

**CNT-10 —** Child items follow the chronological order of their acoustic evidence, with `L-GIST` at
`sequence_index = 1` as the sole exception. Adjacent detail or inference items are separated by at least
**8.0 seconds** of audio, so the listener can process item k before the cue for item k+1 is spoken.

**CNT-11 —** Every child item stores an evidence span — `(start_sec, end_sec, verbatim_quote)` for audio,
`(char_start, char_end, verbatim_quote)` for text — so the review screen can jump to the relevant segment.
While the stem may not depend on a timestamp during testing, the explanation does.

**CNT-12 — shared passage.** One passage serves 2–4 children, except `R-GRAM` sentence-cloze items, which use
standalone stimuli of 15–45 words with one item each. No two children on the same passage may test overlapping
character spans: intersection-over-union must be < 0.15, except when one item is a whole-passage `R-GIST`
question. Local independence is a psychometric requirement, not tidiness — overlapping items measure one
proficiency twice and inflate the score.

**CNT-13 —** Local independence is a measurement requirement, not tidiness. Two items on one passage testing
the same proposition measure one proficiency twice and inflate the score for a learner who happened to
understand that one sentence. The 0.15 overlap ceiling is the operational form of the rule; a bundle that
violates it is rejected at ingestion rather than quietly counting twice.

**CNT-14 —** Partial stimulus delivery is permitted and required: the engine may serve 1–2 child items from a
3–4 item bundle during placement or a single-topic drill, holding siblings in reserve. Without this, a 5-item
maintenance check cannot be assembled from 3-item audio groups, and every step would inflate into a full
testlet of unrelated topics.

### 5.5 Cell depth

**CNT-15 —** The pool is sized by cell, not by exam.

| Band group | Objective items per cell | Productive tasks per cell |
|---|---|---|
| **Core (A2, B1, B2, C1)** | 30 | 8 |
| **Peripheral (A1, C2)** | 15 | 4 |

**CNT-16 — why 30.** A single mastery verification window requires up to 15 items (§7.5). A weak learner's
cycle is an initial 5-item diagnostic, a post-lesson 5-item drill, a spaced 5-item review, and a 10–15 item
verification window — 25 to 30 unseen items. Thirty per cell supports two full verification windows or six
5-item drills without repeating an item.

**CNT-17 — why 8 tasks.** Generalizability Theory requires 3–4 tasks for a dependable productive judgement
(§5.7). Eight per cell allows a practice round, AI feedback, a lesson, and re-verification with no prompt
repetition.

**CNT-18 —** No cell may ship empty. This is the rule that would have prevented twelve empty topics: coverage
scales by depth, never by breadth.

### 5.6 Volume

**CNT-19 — the full production pool is 2,120 assessment units plus 120 lessons.**

| Skill | A1 | A2 | B1 | B2 | C1 | C2 | Total |
|---|---|---|---|---|---|---|---|
| Listening items | 90 | 180 | 180 | 180 | 180 | 90 | **900** |
| Reading items | 90 | 180 | 180 | 180 | 180 | 90 | **900** |
| Speaking tasks | 16 | 32 | 32 | 32 | 32 | 16 | **160** |
| Writing tasks | 16 | 32 | 32 | 32 | 32 | 16 | **160** |
| **All skills** | **212** | **424** | **424** | **424** | **424** | **212** | **2,120** |

Stimulus counts: 330 shared listening audios, 232 shared reading passages, 150 standalone `R-GRAM` cloze
stimuli (382 reading stimuli total).

**CNT-20 —** Reading passage arithmetic, for audit: 5 comprehension topics × 15 items = 75 passage items per
peripheral band and × 30 = 150 per core band. At A1 that is 36 passages of 2 items plus 1 of 3 (37 passages,
75 items); at A2 and B1, 50 passages of 3 (150 items); at B2 and C1, 36 of 4 plus 2 of 3 (38 passages, 150
items); at C2, 18 of 4 plus 1 of 3 (19 passages, 75 items).

**CNT-21 — three release tiers share one shape.**

| Tier | Objective items | Productive tasks | Lessons | Units | Empty cells |
|---|---|---|---|---|---|
| Legacy (replaced) | ~116 total | included | 13, none leveled | **116** | 12 of 15 |
| **Tier 1 — seeded demo** | 180 L + 180 R (110 audios, 114 stimuli) | 48 S + 48 W | 120 | **456** | 0 |
| **Tier 2 — MVR** | 480 L + 480 R (176 audios, 204 stimuli) | 80 S + 80 W | 120 | **1,120** | 0 |
| **Tier 3 — full pool** | 900 L + 900 R (330 audios, 382 stimuli) | 160 S + 160 W | 120 | **2,120** | 0 |

**CNT-22 —** Tier 2 (1,120 units + 120 lessons) is the launch gate. Tier 3 is a later expansion. Neither tier
may ship a partially populated cell matrix: a demo learner must be able to sit a complete two-stage placement
and run a full 5-item drill in any of the 120 cells.

**CNT-23 —** The demo set is a smaller run of the same shape, generated by the real AI author and scored by the
real AI grader — not a separate mock code path. A fake-AI demo proves nothing about the part most likely to
break, and two code paths drift.

**CNT-24 —** Placement-eligible stock. Ten items per core cell and five per peripheral cell carry
`placement_eligible = true`, giving 60 placement-eligible items per band at A2–C1 and 30 at A1/C2; productive
cells reserve two and one respectively. A placement draw consumes at most 1–2 items from any cell, leaving
27–29 unseen practice items in every core cell and 13–14 in every peripheral cell.

**CNT-25 —** The equalized placement blueprint (§6.4) needs 42 placement-eligible items per band for six
non-overlapping forms, which is 12 more than the 30 available at A1 and C2. The gap is closed by re-tagging 12
existing practice items per peripheral band as placement-eligible — not by authoring new items, and not by
reducing the number of forms. After re-tagging, 13–14 unseen practice items still remain in each peripheral
cell, which is the level this chapter promised.

**CNT-26 — 120 lessons.** Every cell has exactly one lesson, indexed by `(skill, topic_id, cefr_level)`. Each
contains two sub-modules: `module_primary` (the rule, a level-calibrated strategy, two annotated worked
examples, 600–900 words) and `module_remedial` (distractor anatomy for objective topics, or a band k−1 → k
upgrade comparison for productive topics, plus two guided micro-checks, 400–600 words). The split exists so a
second attempt at the same cell does not re-read identical instruction.

### 5.7 Rubrics

**CNT-27 —** One unified four-criterion analytic rubric per skill, scored on integers 0–6, where the integer is
the CEFR band: 0 = Pre-A1/non-scorable, 1 = A1, … 6 = C2.

| Skill | Criteria |
|---|---|
| **Speaking** | `S_FC` fluency/coherence/discourse · `S_PR` pronunciation/intonation/intelligibility · `S_LR` lexical resource/pragmatic precision · `S_GR` grammatical range/real-time accuracy |
| **Writing** | `W_TR` task fulfilment/relevance/argument · `W_CC` coherence/cohesion/organisation · `W_LR` lexical resource/register · `W_GR` grammatical range/complexity/accuracy |

**CNT-28 —** Every criterion has a written descriptor at every one of the seven bands. A descriptor states
observable floor gates and thresholds — speech rate in wpm, pause duration, error-free T-unit percentage,
subordination variety, lexical repetition. The current product's criteria carry a name and a maximum score and
no descriptor, which is why the AI has been grading against a scale nobody wrote down. Writing those
descriptors is not documentation; it is what makes grading possible at all.

**CNT-29 —** The task composite is the unweighted mean of the four criteria: `M_t = ¼ Σ c_j ∈ [0, 6]`. It maps
to CEFR by uniform half-band cuts: `M < 0.75` Pre-A1; `[0.75, 1.50)` A1; `[1.50, 2.50)` A2; `[2.50, 3.50)` B1;
`[3.50, 4.50)` B2; `[4.50, 5.50)` C1; `≥5.50` C2.

**CNT-30 —** A two-task writing sitting weights the extended task at twice the short one:
`M_skill = (M_T1 + 2·M_T2) / 3`.

**CNT-31 — anti-compression.** Direct holistic prompting collapses scores into B1/B2 and rewards verbosity.
Five mechanisms are mandatory: (1) every band transition is a set of binary gate conditions, not a scalar
impression; (2) the four criteria are evaluated in four separate calls at temperature 0.20, each extracting
verbatim evidence before emitting an integer; (3) six acoustic telemetry metrics are injected into the speaking
prompt alongside the transcript (speech rate, articulation rate, silent pauses over 1500 ms per minute, mean
pause duration, ASR confidence mean, phoneme posterior score); (4) hard length and repetition ceilings cap
`W_TR`/`S_FC` at 2 when a response falls below 50% of the task minimum and cap `W_LR`/`W_CC` at 3 when
type-token ratio or n-gram repetition reveals formulaic padding; (5) three calibrated benchmark anchors at
bands 2, 3, and 4 are injected into every grading call.

**CNT-32 — dependability.** One AI-scored task is not a defensible measure of a skill. Rater agreement with
expert consensus is high (`QWK` ≈ 0.72–0.82 writing, 0.70–0.78 speaking), but score generalizability across
the skill domain is only `Φ(1) = 0.55`. Two tasks across distinct genres reach `Φ(2) = 0.71` and are the
minimum for placement. Three tasks reach `Φ(3) = 0.786` and four reach `Φ(4) = 0.83`, which is the minimum for
certifying topic mastery.

| Tasks | Φ | SEM (bands) | Permitted decision |
|---|---|---|---|
| 1 | 0.550 | ±0.67 | Formative feedback only; never sets a level |
| 2 | 0.710 | ±0.47 | **Placement level assignment** (two distinct genres) |
| 3 | 0.786 | ±0.39 | Topic mastery at peripheral bands |
| 4 | 0.830 | ±0.34 | Skill promotion and core topic mastery |
| 6 | 0.880 | ±0.27 | Certificate readiness benchmark |

**CNT-33 —** Where a task fails to grade, the skill is not zero and not A1. One graded task yields a result
marked as estimated from one task (`Φ = 0.55`); zero graded tasks yields "not measured" (PRN-04).

### 5.8 Authoring and acceptance

**CNT-34 — five stages, three model families.** `Author_LLM ≠ Verifier_LLM ≠ Grader_LLM`, strictly. The same
model family authoring an item and later validating its key shares its own blind spots; the same family
grading responses it authored inflates scores for its own phrasing. Both failures are systemic, not occasional.

| Stage | Actor | Output |
|---|---|---|
| 1. Cell blueprint dispatch | Orchestrator | A generation spec with exact word-count, FK grade, MLTU, and lexical bounds for one empty cell |
| 2. Synthesis | Author model + neural multi-speaker TTS | Stimulus, 2–4 children with distractor rationales and evidence quotes; multi-voice audio at target wpm |
| 3. Automated gates 1–4 | Deterministic profiler + two heterogeneous verifier models | Validated bundle or rejection with a reason |
| 4. Stratified 10% human audit | Assessment SMEs | Approved batch, or a quarantined stratum |
| 5. Ingestion | Ingest service | Active in pool |

**CNT-35 — gate 1, structural and item hygiene.** Four distinct options with pairwise Levenshtein distance > 2;
no meta-options ("all of the above", "neither a nor b"); the key's word count within ±25% of the distractor
mean; part-of-speech parallelism across options; key balance between 20% and 30% per letter within each cell;
no unique content lemma appearing in both stem and key but absent from the stimulus and distractors; non-empty
explanation and per-option distractor rationale.

**CNT-36 — gate 2, linguistic and acoustic.** Word count, Flesch-Kincaid grade, MLTU, clauses per T-unit, and
target-band lexical coverage all inside §4.1–§4.2. For audio, an independent ASR pass (Whisper-Large-v3)
against the written script must yield word error rate 0.0% — a single mispronounced or clipped word breaks
every learner who reads the transcript in review.

**CNT-37 — gate 3, blind dual-model key verification.** The answer key is the highest-risk artefact in the
system: a wrong key mis-places every learner who meets that item and nobody finds out. Two heterogeneous
verifier models, both distinct from the author, each solving with the key and explanation withheld, must both
select the exact key with confidence ≥ 0.85 and assign < 0.15 to every distractor. A stimulus-blind ablation
follows: if a solver identifies the key with probability ≥ 0.50 without the passage or transcript, the item is
rejected for testing world knowledge rather than comprehension.

**CNT-38 — gate 4, level consistency and grader calibration.** An independent classifier must predict
`L̂_i = L_i`. Sibling evidence spans must satisfy the IoU < 0.15 rule. For productive tasks, a seven-response
synthetic ladder — six benchmark responses from A1 to C2 plus an adversarial "verbose B1 trap" of 280 words of
repetitive simple clauses — must produce strict monotonic composites with `ΔM ≥ 0.65` between adjacent bands
and a trap composite ≤ 3.25.

**CNT-39 — gate 4 (discrimination).** A rubric that compresses A2–C1 into a narrow band is not a rubric. The
separation floor and the verbose-trap ceiling are the two checks that make discrimination testable rather than
hoped for.

**CNT-40 — the 10% human audit.** The sample is stratified across every `(skill, cefr_level, topic_id)` cell,
not drawn at random, because random sampling can miss C1/C2 entirely. Reviewers solve blind before seeing the
AI key. A **zero-tolerance key rule** applies: one discovered key error in the sample quarantines 100% of that
stratum for full human verification or regeneration. Non-key defects (level drift over ±0.5 bands, unnatural
distractors, TTS artefacts) quarantine the stratum above a 5% failure rate.

**CNT-41 —** An approved stratum promotes both the 10% reviewed and the 90% unsampled buffer together. The 90%
is never individually reviewed; that is the accepted risk of skipping a pilot study, and it is why the sample
is stratified rather than random.

### 5.9 Lifecycle

**CNT-42 — six states.** `draft → validated → active`, plus `quarantined`, `retired`, `retired_defective`.
Selection queries filter on `active` and `content_tier`. Review queries join on `(item_id, version)` with no
lifecycle filter, so a past attempt always renders exactly what the learner sat.

**CNT-43 — no hard delete.** An item that has ever left draft, or that has at least one learner reference,
cannot be deleted. Retirement removes it from future assembly and preserves history. A copy-on-write version
bump is required for any change to a stimulus, audio file, stem, option text, key, or level; cosmetic typo
fixes in explanations may be edited in place.

**CNT-44 — telemetry quarantine.** Every active objective item is evaluated when it crosses 30, 50, 100, and
250 administrations. Four conditions flip it to `quarantined`, immediately removing it from selection:

1. Point-biserial discrimination below 0.10 at N ≥ 50, or below 0.00 at N ≥ 30 (higher-ability learners prefer a distractor over the key).
2. Target-band facility outside `[0.30, 0.92]` at N ≥ 50.
3. One distractor chosen by more than 45% of target-band learners while the key falls below 35%.
4. A productive task's mean AI score deviating more than 0.75 bands from the learners' established level at N ≥ 30.

**CNT-45 — monotonic learner protection.** When a quarantined item is confirmed defective, every historical
attempt referencing it is recalculated. If the recalculation lifts a band or promotes a topic from
non-mastered to mastered, the learner is upgraded automatically. **No learner is ever downgraded because of a
content defect.** A system error is not evidence about a learner.

**CNT-46 —** The demo tier decommissions by a single bulk state change, removing 100% of demo content from
selection without foreign-key violations, and audit logs of demo attempts survive.

## 6. Placement

### 6.1 What placement is for

**PLC-01 —** Placement establishes one CEFR level per examined skill plus one overall, and hands that result to
the learning path. It is a measurement instrument, not a test of speed.

**PLC-02 — placement is always free.** No credit row, ever. The code path does not consult a balance.

**PLC-03 — the result is per-skill plus an overall, and the overall is the weakest measured skill, never an
average.** Averaging conceals the exact profile a learner needs to see: a candidate who reads at B2 and speaks
at A2 has an overall of A2, and is told so, with Speaking named as the limiting skill. An average would report
B1 and imply both skills are adequate.

**PLC-04 —** Each skill's result is a level plus an integer 0–100 position inside that level. The pair is
inseparable: a level alone hides progress within it, and a position alone is meaningless after a band crossing
resets it. A learner who moves from B2·78 to C1·12 has improved; the number alone reads as collapse.

**PLC-05 —** Only skills the active certificate examines are measured. A result for a skill the certificate does
not examine is retained for a later certificate switch but never shown and never enters the overall.

### 6.2 Reuse and cooldown

**PLC-06 —** A per-skill result may be reused for 90 days. The screen shows each reused skill's level, position,
and original measurement date, and offers a choice: reuse valid skills and measure only the missing ones, or
re-measure everything. Re-measuring all four because the certificate changed is wasteful; eight-month-old
results say nothing about today.

**PLC-07 —** Retake cooldown is 24 hours, with no lifetime cap. Random pools plus instant retakes invites
retrying until a high band appears; a cooldown still permits recovery from a genuine mishap.

**PLC-08 — the cooldown has one exception.** If the previous attempt ended because every examined skill failed
to grade due to an internal outage, the cooldown is zero. The learner is not charged for the system's failure.

**PLC-09 —** A reused skill's result carries the mark `reused_from_prior` and its original measurement date is
preserved, not overwritten. The age of a measurement is part of the measurement.

### 6.3 Sitting mechanics

**PLC-10 - placement reuses the existing attempt engine.** It inherits the state machine, expiry handling,
draft synchronisation, and navigation locks. It does not get a second sitting engine: a
parallel implementation drifts, and the drift is invisible until a learner sees two different answers.

**PLC-11 —** The placement snapshot — the result row — is **derived from the attempt by exactly one writer**
(PRN-02). This is not a design preference. A placement carrying free responses could not be completed because
two writers disagreed about one row: one left the attempt `submitted` with no band, the other marked the
placement `done` with a null band, and a constraint correctly refused. Making free responses the normal case
means that defect would become the normal case.

**PLC-12 — there is no overall countdown.** Placement measures ability, not speed. A sitting is "in progress"
when the server says so, and no elapsed time is deducted while the browser tab is closed.

**PLC-13 —** Inactivity expiry is `min(last_activity + 48 h, started_at + 72 h)`. Forty-eight hours allows a
next-day completion; seventy-two prevents a half-sitting lingering indefinitely.

**PLC-14 — resume after exit.** When a learner returns after closing the tab, the screen shows the attempt start
date, which skills are done, the active skill and stage, and the count of synced responses, and offers either
resume at the exact item or explicit abandon.

**PLC-15 — resume after reload.** Reload restores the exact stage and item pointer without advancing. During a
listening track, playback resumes from the last checkpoint. During an active speaking recording, the partial
stream is discarded and **exactly one** retry is granted with the timer reinitialised, because a spliced
recording carries prosodic artefacts and would be graded as a worse performance than the learner gave. A second
reload during recording finalises the buffered audio if at least 15 seconds were captured, or replaces the task
with an unseen sibling from the same cell with no remaining retries — otherwise refreshing becomes a rehearsal.

**PLC-16 — resume during grading.** Reload during in-flight AI grading is a pure read. It never re-dispatches
the job and never resets the task status.

**PLC-17 — offline.** At the start of each stage the client pre-fetches that stage's stimuli and audio. If
connectivity drops, a persistent non-modal banner states that answers are being saved on the device and that
the learner may continue; the progress header shows the optimistic local count beside an explicit sync status.
At a hard server-dependent boundary — the stage transition, a speaking upload, or final submission — the flow
pauses and offers reconnection, rather than pretending to save.

**PLC-18 — partial submission.** A learner may submit with unanswered items only after confirming a modal that
names the unanswered count per section. Unanswered objective items are scored incorrect. Choosing *not* to
warn, or silently blocking, are both worse: one hides the cost, the other traps a learner who cannot finish.

**PLC-19 — abandonment versus salvage.** A sitting with zero measured skills becomes `abandoned` with no
result. A sitting where at least one skill was measured becomes `done` with a partial result, the unattempted
skills marked `not_measured` and excluded from the overall. A partial result beats no result.

**PLC-19a — hardware is a precondition, not a step inside the sitting.** A placement whose certificate examines
speaking does not begin until a microphone has been granted and measured as producing signal; one examining
listening does not begin until an output device has been confirmed audible. The gate is described in §8.9 and
applies identically to mock exams and to speaking practice. It is the only precondition in this chapter that can
refuse to start a sitting, and it refuses before any attempt row exists rather than after.

### 6.4 The paper: two stages

**PLC-20 — the sitting is 60 minutes across four skills.** Listening 15, Reading 15, Speaking 8, Writing 22.
A two-skill certificate sits 30 minutes. This is a hard cap, and both stages must fit inside it.

**PLC-21 — multistage, not per-item adaptive, not linear.** Linear forces a long paper and leaves each band
thinly sampled. Per-item adaptive requires calibrated item parameters from a large pilot sample this product
does not have. Multistage sits between: a fixed routing stage, then a targeted boundary stage.

**PLC-22 — stage 1 delivers 8 items per objective skill, sampling all six bands.**

| A1 | A2 | B1 | B2 | C1 | C2 |
|---|---|---|---|---|---|
| 1 | 1 | 2 | 2 | 1 | 1 |

**PLC-23 —** Sampling all six bands in stage 1 is required by the decision that stage 1 is a fixed blueprint
across bands. A blueprint omitting A1 and C2 cannot route a beginner or an advanced candidate, and would make
the two-stage design a two-stage design for the middle only.

**PLC-24 — stage 2 delivers 10–12 items, and every routed band ends with exactly 7 evaluated items.**

| Stage 1 score | Routed pair | Stage 2 delivery | Evaluated per band |
|---|---|---|---|
| 0–2 | A1–A2 | 6 A1 + 6 A2 | 7 / 7 |
| 3 | A2–B1 | 6 A2 + 5 B1 | 7 / 7 |
| 4–5 | B1–B2 | 5 B1 + 5 B2 | 7 / 7 |
| 6 | B2–C1 | 5 B2 + 6 C1 | 7 / 7 |
| 7–8 | C1–C2 | 6 C1 + 6 C2 | 7 / 7 |

**PLC-25 — why seven items per band.** Band L is awarded at ≥70% correct at L and at every lower band. With
discrete item counts, the passing threshold lands on an awkward fraction unless the count is chosen: at 5 items
the bar is 4/5 = 80%, giving 8.70% false promotion but **26.27% false demotion** of true masters; at 6 items the
bar is 5/6 = 83.3%, which spikes false demotion to **34.46%**; at 7 items the bar is 5/7 = **71.43%**, sitting
directly on the 70% cutoff, with false promotion 9.63%, false demotion 14.80%, and pure-guess false passes at
1.29%. Seven is not a preference; it is the count at which the stated rule is not systematically unfair to
competent candidates.

**PLC-26 — no substitution across bands.** If a cell is exhausted, the selector relaxes the topic constraint
first (same band, another topic), then draws from the practice reserve at the same band, then recycles the
oldest-seen item at that band and logs a warning. It never substitutes an adjacent band, because that would
change the denominator and invalidate the 70% rule.

### 6.5 Scoring

**PLC-27 — the 70% rule.** Band L is awarded only at ≥70% correct at L *and* at every evaluated lower band.
The threshold is published to the learner on the result screen. Publishing it is more honest than "the system
says B1".

**PLC-28 — non-monotonic results are flagged, never silently accepted.** Correct at B2 but wrong at A2 means
guessing or a bad item. The awarded level truncates to the highest band of the first contiguous passing run
from the bottom, and the anomaly is recorded for an administrator. A high rate of these means the pool is
broken, and without the flag nobody would find out.

**PLC-29 — the intra-band position.**

For objective skills, `P = round(100 × [0.70 × (clamp(p_L, 0.70, 1.00) − 0.70)/0.30 + 0.30 × clamp(p_{L+1}, 0, 0.70)/0.70])`: 70% weight on excess mastery above the threshold at the
awarded band, 30% on progress toward the threshold at the next band. At Pre-A1, `P = round(100 × p_A1 / 0.70)`.
At C2, only the first term applies.

**PLC-30 —** Worked behaviour, for verification: a candidate at 5/7 on B2 (0.714) and 1/7 on C1 (0.143) receives
B2·9. At 7/7 on B2 and 4/7 on C1 (0.571) they receive B2·94. One more C1 item correct (5/7) crosses to C1·3.
The number tracks the boundary continuously and never shows a bare percentage.

For productive skills, `P_prod = round(100 × (M_skill − B_low(L)) / (B_high(L) − B_low(L)))` where `B_low/B_high`
are the half-band cut intervals of §5.7.

**PLC-31 — the prompt ceiling.** A task scored on an A2 prompt cannot yield a C1 result however well it is
answered: `M_t` is bounded at `k_task + 1.00`. Otherwise a fluent learner answering a simple prompt receives a
fluent answer's score, which measures the prompt, not the learner.

**PLC-32 — speaking is capped at 60 seconds.** Speech must be bounded and conditions equal across candidates.
The A1–C2 envelopes in §4.2 list longer recording windows; those apply to practice, where no cross-candidate
comparison is made.

### 6.6 The result

**PLC-33 —** A result row exists per skill and carries: level, intra-band position, continuous CEFR coordinate,
projected certificate band and score (read-only lookup, §4.5), whether it was estimated from a single task,
whether it was truncated as non-monotonic, the original measurement timestamp, its expiry, and — where
applicable — that it was reused from a prior sitting.

**PLC-34 —** Results appear progressively. Objective skills are scored and visible immediately; productive skills
show a grading state and fill in as each task returns. A learner watching a result assemble is better served
than one watching a spinner, and if grading fails the objective half still has value.

**PLC-35 — a failed task does not void the skill.** One graded task yields a level marked as estimated from one
task. Zero graded tasks yields `not_measured` with a reason and a retry. Treating an AI failure as A1 is a
fabricated result; refusing to complete the sitting because one task failed punishes the learner for the
system's fault.

**PLC-36 — completion requires one measured skill.** A sitting where nothing could be measured is `abandoned`,
not `done`. A registration where `status = done` but the overall is null is replaced by `done ⇒ overall is
present, or a completion failure reason is recorded`.

### 6.7 What the path reads

**PLC-37 —** The path initialises each skill from that skill's own level and position, never from the overall.
Initialising all four skills at the overall re-creates the exact pathology the overall exists to prevent: a B2
reader would receive A2 reading lessons.

**PLC-38 —** Feasibility and weekly budgeting consume the continuous coordinate across all examined skills, plus
the overall as a headline readiness figure.

**PLC-39 —** Topic signals from the placement — 1–3 items seen per topic — are sequencing hints for the first
week. They are **never** certified mastery: three items cannot support a mastery judgement, which requires
twelve (§7.5).

---

## 7. The Learning Path

### 7.1 Shape

**PLC-40 — the path is a sliding four-week window, not a fixed block.** Exactly four weeks are live at any
moment. When week 1 closes — all its steps done, or the calendar crossing day 8 — week 2 becomes week 1, and a
new week N+4 is synthesised from current capability. Completed weeks move to history and are immutable.

**PLC-41 —** The window exists because a single 52-week block is wrong by week six. A learner who is faster or
slower than predicted either drags an obsolete plan or has it rewritten wholesale, and rewriting from scratch
reads as having never started.

**PLC-42 — one timeline, steps assigned to skills, skewed toward the weakest.** Not four independent strands.
Four strands turn the path into a scheduling problem: one lagging strand either runs past the exam date or must
be dropped, and both need rules nobody will maintain.

**PLC-43 — progress is reported two ways, never one.** *This week's work* (steps done, minutes done, whether
free drilling is unlocked) and *distance to target* (per-skill level and position against target, remaining
guided hours, feasibility verdict). "Week 3, 2 of 4 steps" does not answer "will I make my exam date"; "34% of
the way to B2" does not answer "what do I do today".

**PLC-44 — a step closes by being done, not by improving.** Mastery decides the step's size, never whether it
closes. Closing on improvement traps a learner who is genuinely weak and they quit; closing on completion alone
makes the path a checklist, which is why mastery must still shrink the next step.

### 7.2 The step

**PLC-45 — a step points at a coordinate, never at a certificate or an unlevelled section.**

`StepCoordinate = (skill, topic_id, target_cefr, step_type, sizing_tier)`

**PLC-46 —** Three bindings exist, chosen by `step_type`:

| Step type | Binds to | Executes through |
|---|---|---|
| `diagnostic_drill`, `focus_practice`, `review_spaced` | `(skill, topic, level)` + a count | The unified item selector (§7.4) |
| `concept_lesson`, and `expanded_remedial` with an embedded lesson | A leveled `lesson_id`, using `module_primary`, plus `module_remedial` when remedial | The lesson reader |
| `mock_section` | `(skill, level)` + a section | The existing attempt engine |

**PLC-47 — level follows the skill, never the overall.** `target_cefr` for a step on skill s is that skill's own
current level. A high overall never pulls a lagging skill up; a low overall never depresses a strong one.

**PLC-48 — progression to the next level is gated.** A step advances to `level + 1` only when three conditions
hold together: the topic is mastered at the current level, the skill's position is ≥75, and the certificate's
target band lies above the current level. Otherwise the step stays on-level, with periodic 5-item maintenance
checks so a mastered topic does not decay unnoticed.

**PLC-49 — lifecycle.** `locked → available → in_progress → closed`, sequentially by order within the active
week. A step partially finished is resumable to the exact unsubmitted item. If the week expires mid-step,
answers already given still count toward capability; unattempted items return to the pool.

**PLC-50 — sizing follows mastery.** Mastered → 5 items or 1 short task (~6–8 min). Developing or unmeasured →
8–10 items or 1 full task or lesson + 4 items (~12–15 min). Low → lesson (primary + remedial) + 12–15 items or
2 tasks (~20–25 min). A mastered topic's steps shorten; they are never removed. Removing them lets a learner
skip the plan through free drilling and makes the path decorative.

### 7.3 The schedule

**PLC-51 — weekly volume is derived, not asserted.** `H_required = Σ H_s(current → target) / max(1, days
remaining / 7)`, and `T_weekly = 60 × min(H_weekly_target, max(1.5, H_required))` minutes. Steps are packed to
that budget ±10 minutes.

**PLC-52 —** The legacy rule of 3–5 steps of 25–45 minutes was chosen by a ticket, not by evidence, and is
removed. A learner at A2 targeting B2 in three weeks and a learner casually practising for next year must not
receive the same session.

**PLC-53 — allocation across skills is skewed, with a floor.** Weight `W_s = d_s^1.5 / Σ d_j^1.5` where
`d_s = max(0.15, θ_target − θ_current)`. The exponent concentrates weekly minutes on the limiting skill without
starving the others; the 0.15 floor guarantees every examined skill at least one step per week.

**PLC-54 —** Worked allocation, for verification: a learner at Listening B2·71, Reading B2·42, Writing B1·50,
Speaking A2·64 targeting B2 with 165 minutes per week receives 52% (85 min, 3 steps at A2) to Speaking, 24%
(40 min, 1 expanded step at B1) to Writing, 15% (25 min, 1 step) to Reading and 9% (15 min, 1 maintenance check)
to Listening.

**PLC-55 — ordering is pedagogical, not cosmetic.** Within a skill and topic: lesson → blocked practice →
interleaved review. Across the timeline, consecutive steps alternate skills. Blocked practice first builds the
form-meaning mapping; interleaving afterwards builds discriminative control. Reversing the order imposes
undesirable difficulty on a beginner before declarative knowledge exists.

**PLC-56 — surplus and deficit hours.** With more hours than the pool can supply, up to 30% of surplus minutes
move to productive work and cross-topic review, the substitution cascade applies, and the week is capped with a
visible notice rather than silently repeating items. With too few hours to reach the target, the plan packs
strictly within the hours available, compresses mastered topics to 5-item checks, and surfaces the feasibility
verdict with three concrete options: extend the date, raise the hours, or lower the target.

### 7.4 Practice and free drilling

**PLC-57 — one selector for both.** A path step and a free drill differ only in origin. Same selection, same
logger, same capability writer. The legacy system ran two selectors that disagreed: standalone practice honoured
a cooldown, path practice re-served the item just missed, in the same hour.

**PLC-58 — selection order.** Filter to `(cert, skill, topic, level, placement_eligible = false)`, exclude items
inside cooldown and comprehension items seen within 14 days, then order by `times_seen ASC, last_seen_at ASC,
item_id ASC` so unseen items are served first. Deterministic, so a bug is reproducible.

**PLC-59 — cooldowns.** A wrong objective answer sets a 24-hour cooldown on that item. A correct answer sets 72
hours for discrete grammar items and 14 days for passage or audio comprehension. Repeating an identical
comprehension stimulus measures recall of the passage and the remembered key, not comprehension.

**PLC-60 — a path step is never blocked by a cooldown.** The step stays executable; the selector routes around
cooling items within the topic, and if the whole cell is cooling, substitutes a sibling topic and says so. The
plan says "practise P5 today"; refusing because P5 is cooling would be the plan arguing with itself.

**PLC-61 — free drilling is unrestricted but recommended.** Any examined skill, topic, and level may be
drilled. The launch view highlights the top three coordinates the path currently prioritises. Restricting
practice to unlocked topics stops a learner revising for their exam; no guidance at all leaves them lost.

**PLC-62 — free drilling unlocks after the week's steps are done.** It is a reward for following the plan, not
a substitute for it. Because a step closes only by being done, free drilling cannot skip the plan — it can only
make the following week's steps shorter through mastery.

**PLC-63 — free drilling is a fixed 10 objective items or 1 productive task.** A learner needs to know how long
free practice will take. A path step's size follows its purpose instead (§7.2).

**PLC-64 — daily ceiling.** One session per `(topic, level)` cell per day, and 50–60 objective items or 4
productive tasks per day overall. A learner drilling 200 items a day is exhausting a 30-item cell in one
afternoon, not learning faster. On reaching the ceiling the skill's drills lock until the next day with a notice
explaining that overnight spacing is required, and offering review or lessons instead.

**PLC-65 — practice is free.** No credit, no paid grading, no limit imposed to save money. Any design that needs
one contradicts PRN-06.

### 7.5 Mastery

**PLC-66 — mastery is estimated over a cell, not over an item.** Repeating one item teaches that item.

**PLC-67 — recency-weighted Beta-Binomial.**

`p_m = (α₀ + Σ wᵢcᵢ) / (α₀ + β₀ + Σ wᵢnᵢ)`, with `wᵢ = 0.85^(K−i)` and `α₀ = β₀ = 2.0` when unmeasured.

**PLC-68 — four bands.** `unmeasured`, `low` (`p_m < 0.733`), `developing`, `mastered` (`p_m ≥ 0.80` **and**
`observations ≥ 12` objective items or 3 tasks). Recency weighting prevents early pre-lesson errors from
permanently penalising a learner who masters the topic after instruction.

**PLC-69 — the 0.733 and 0.80 thresholds are not the legacy 75%.** The legacy rule was a single pass mark with
no sample-size requirement. These are the two proportions at which a four-option item bank distinguishes a
non-master from a master (0.50 vs 0.80 expected observed proportion at a 0.25 guessing floor), and the
observation minimum exists so a decision is not made on four answers.

**PLC-70 — a mastered state changes only on an event, never by recomputation.** A single wrong answer does not
drag a mastered topic back to developing; it sets a 24-hour review window. Two consecutive wrong answers while
mastered lapse it to `reviewing`. Recomputing from a percentage after every answer is what makes the "two
consecutive wrong" rule unreachable.

**PLC-71 — inactivity drift.** A mastered cell not practised for 28 days, with no mock section at or above that
level in the meantime, decays `0.02` per week toward the prior. Below 0.80 it reverts to developing and resumes
standard sizing until a maintenance check re-verifies it.

**PLC-72 — no third repetition.** A step that closes with no improvement is repeated exactly once with remedial
support. If the repeat also fails to improve, the topic is flagged as a weak spot and the path re-plans rather
than repeating forever. Infinite repetition is the fastest way to make a learner quit.

### 7.6 Re-planning

**PLC-73 — four triggers, and only four.** Two consecutive weeks with zero completed steps; any examined skill's
level changing (from a mock section, a placement retake, or calibration); the learner editing hours, date, or
target; a weak-spot escalation.

**PLC-74 —** Weekly automatic re-planning is prohibited. A plan that changes after every session stops being
trusted, and a plan that never changes drifts uncorrected.

**PLC-75 — what changes.** History is immutable: completed weeks, closed steps, and in-progress steps with saved
answers. Unstarted steps in the active week and all buffered steps are superseded and regenerated from current
capability and the refreshed feasibility calculation.

**PLC-76 — the learner is told.** Every re-plan writes an audit row and shows a dismissal-capable summary card
naming the trigger, the before-and-after step counts and sizes, and the refreshed verdict. A silent change of
plan is the fastest way to lose trust.

**PLC-77 — weak-spot escalation has a shape.** Direct drills on the topic pause for one week; a foundational
bridge step one level below is inserted; a diagnostic check is scheduled into week 2.

### 7.7 End states

**PLC-78 — four terminal states.** Target reached and exam date arrived are the primary flows with full detail.
Stagnated and abandoned are a badge plus one sentence, without a separate flow.

**PLC-79 — the readiness verdict reuses the feasibility arithmetic.** Four outcomes: `READY`; `BORDERLINE`
(remaining ≤15 hours and every skill within 0.25 level); `NOT_YET_READY`; `CANNOT_JUDGE` where a certificate's
bands carry no study-hour evidence.

**PLC-80 — not-ready produces a concrete continuation**, not encouragement: the per-skill deficit, the hours the
limiting skill still requires, and the minimum feasible new exam date computed from the weekly hours available.

**PLC-81 — stagnation has a definition.** Two consecutive four-week cycles, or three weak-spot escalations, with
level movement ≤0.05 across lagging skills despite at least 50% step completion. The learner is shown a plateau
notice with two options: consolidate one sub-level lower, or retake placement.

**PLC-82 — abandonment is four weeks of inactivity**, resumable from saved capability at any time.

---

## 8. The Learner Interface

### 8.1 The system is a constraint

**LRN-01 — the visual language is fixed.** Flat painted fields, 2px ink keylines, 1px soft interior rules, zero
border radius, zero shadow, zero gradient, zero blur. Active state inverts to a solid ink block; it never tints.
Depth is carried by keyline weight, never by a shadow. Every numeral a learner might compare is set in a
monospace face with tabular figures.

**LRN-02 —** The palette: Sign Blue for the learner's own act — every primary button, the current question's
ring; Sign Gold for attention — a flagged item, a warning, an unmeasured panel; Sign Red for refusal and
danger; Sign Green for an open condition and correctness. Ink is both lettering and field. Colour is always the
second layer of information, paired with a word or a number.

**LRN-03 — the three type voices.** A reading face for sustained prose; a condensed signage face for uppercase
placards and labels; a monospace for every figure. A number set in the reading face is a defect. The family is
IBM Plex, self-hosted with Latin and Vietnamese subsets, so diacritics render from the same family rather than
falling through to a fallback.

**LRN-04 — no emoji and no raw glyphs.** Icons come from a drawn set, sit beside a word, and never replace it. A
sign still says the word.

**LRN-05 — desktop-first at 1440×900.** Density is high and deliberate: the answer grid, the passage, the clock,
and the save status share one screen because a 200-item exam under a clock demands it.

**LRN-06 —** A requirement that genuinely cannot be met inside these rules is stated as such and argued. Using a
card grid with soft shadows, a rounded control, or an icon-only button is not a workaround; it is answering a
different product.

### 8.2 Screens

**LRN-07 — nineteen learner-facing screens replace roughly sixty fragmented views.**

| ID | Screen | Purpose |
|---|---|---|
| `SCR-SH-01` | Global shell and topbar | Six sidebar doors, active certificate placard, exam-date indicator, offline banner |
| `SCR-DB-01` | Dashboard | One next action, weekly execution, per-skill standing |
| `SCR-PL-01` | Placement preflight and reuse gate | What will be measured, what may be reused, and the hardware gate of §8.9 |
| `SCR-PL-02` | Placement sitting — objective | Two-stage listening and reading |
| `SCR-PL-03` | Inter-half checkpoint and productive sitting | Objective result, then speaking and writing tasks |
| `SCR-PL-04` | Placement result | Four levels plus overall, weakest-skill floor, progressive grading |
| `SCR-LP-01` | Path timeline | The four-week window, dual progress, re-plan card |
| `SCR-LP-02` | Goal setup and feasibility | Target, date, weekly hours, verdict |
| `SCR-LP-03` | Terminal readiness verdict | Ready, borderline, not-ready with continuation |
| `SCR-LS-01` | Leveled lesson | Primary and remedial modules, micro-checks |
| `SCR-PRAC-01` | Objective practice hub | Topic matrix, mastery, next recommended drill |
| `SCR-PRAC-02` | Writing practice hub | Task launch, prior submissions |
| `SCR-PRAC-03` | Speaking practice hub | Task launch, prior recordings |
| `SCR-PRAC-04` | Objective practice session | Item, answer, post-answer review |
| `SCR-PRAC-05` | Productive practice session | Prompt, record or compose, AI feedback |
| `SCR-EX-01` | Mock exam catalogue | Available mocks per certificate |
| `SCR-EX-02` | Mock exam sitting | Timed paper, navigator, autosave |
| `SCR-EX-03` | Mock exam result | Certificate score, projected level, per-section split |
| `SCR-PR-01` | Profile and certificate switcher | The only place a certificate changes |

**LRN-08 — six sidebar doors.** Path, Exams, Practice, Writing, Speaking, Home. Writing and Speaking are split
into their own doors because learners could not find productive practice when it was buried inside a generic
"practice" entry; merging them back repeats that fault. Path is a new concept and needs a door to exist in a
learner's head.

**LRN-09 — certificate switching lives in profile settings; the topbar placard is not clickable.** Switching
changes the path, the placement, and the practice pool. A one-click switch invites an accidental change that
reads as data loss.

**LRN-10 — a two-skill certificate keeps six doors and shows the missing ones as a non-clickable, explained
state.** Removing them renumbers the navigation and breaks muscle memory; a dead control with no explanation is
worse than either.

### 8.3 The dashboard

**LRN-11 — exactly three blocks.**

1. **Next action and weekly execution** — one primary call to action, a discrete step tally, minutes done against
   allocated.
2. **Goal standing and feasibility** — four tabular cells, no progress bars: current level per skill against
   target, remaining guided hours, pacing comparison, verdict.
3. **Per-skill standing** — one row per examined skill, sized by certificate configuration: four columns for a
   four-skill certificate, two for a two-skill certificate. Never a four-column grid with two empty cells.

**LRN-12 — no fourth block.** The dashboard was cut from eleven blocks to three because eleven hid the one action
that mattered. A rebuild that grows it back is a regression regardless of what the blocks contain.

**LRN-13 — with no placement, the dashboard is one block with one action** and a sentence describing what
placement is and how long it takes. No ghost blocks, no dashes. A new learner seeing a grid of empty figures
reads a broken product, and this is the only moment they have no reason to trust it.

**LRN-14 — the next action is chosen by a fixed cascade**, not by an opinion: resume an in-progress placement;
retry an unmeasured skill; show the week's next available step; show the recommended free drill; show the day's
ceiling state; show completion. The cascade is data, so the choice is reproducible.

**LRN-15 — the exam date shows as a date normally and becomes a gold countdown inside 14 days.** A perpetual
countdown is noise on a twelve-month goal and pressure with no use; inside two weeks the pressure is real.

### 8.4 The placement result

**LRN-16 — the result is one unified signboard, not four disconnected numbers.** An overall floor placard,
tethered to the limiting skill, above a six-band horizon table in which every skill is a row on one shared
ordinate. Rendering four separate cards invites the learner to average them mentally, which is exactly what the
overall exists to prevent.

**LRN-17 — the overall is labelled as the floor, with its reason in words**, naming the ceiling skill and the
floor skill. A number whose meaning the learner must infer is a defect.

**LRN-18 — an unmeasured skill is a gold-bordered panel** reading "not measured", with a short reason and a retry
control. Never a low band, and never grey. Grey reads as the learner's fault; showing the bottom band is a
fabricated number; gold is attention, not refusal.

**LRN-19 — the 0–100 position is shown as a position, never a percentage.** Crossing a band resets it downward,
so the presentation carries the crossing explicitly: "B2 (78/100) → C1 (12/100)" in Sign Green, framed as
advancement. A bare "12%" after "78%" reads as collapse.

**LRN-20 — the result carries a provenance strip**: the published 70% rule in plain language, an "AI-estimated"
label beside productive scores, each skill's measurement date, and a reuse badge where a result was carried over.

**LRN-21 — exactly one primary call to action** at the end of placement. Several equal actions is no action.

### 8.5 The path and practice

**LRN-22 — a plan with no end is shown as a window.** A five-tab week selector with an expandable history drawer
for completed weeks and three visible future weeks. No "9 of 20"; the position is a window, and the UI says so
in words rather than implying a total.

**LRN-23 — a step shows its size before it starts.** Four metadata columns: sequence and skill, the canonical
topic code and name, the level badge, and the exact item count with estimated minutes. A learner must not
discover that a step is twenty items after beginning it.

**LRN-24 — a repeated step carries a label and the numbers**: what was scored last time and what is needed. A
bare "repeat" reads as punishment.

**LRN-25 — a resting topic is shown, dimmed, with the local time it reopens.** Hiding it makes it look deleted;
revealing it only on click is a dead end. The time is the learner's local time, never UTC.

**LRN-26 — an item's CEFR level is not shown while the learner is answering.** Showing it invites avoiding hard
items, which corrupts the measurement: a learner who skips B2 items is measured as A2. It may be revealed after
the answer is locked, when it cannot affect behaviour.

**LRN-27 — the three practice modalities are three different interfaces**, not one wrapper. Listening shows one
stimulus group with its linked children at a time, because a shared audio track cannot be split across screens.
Reading shows a split pane: passage left, items right. Writing shows a split pane: checklist left, editor right
with a live word counter. Speaking shows one task at a time with a preparation timer.

**LRN-28 — free drilling locked state shows why**, naming the incomplete steps that unlock it, and how many
minutes the remainder should take. A lock with no explanation is the most hostile state in the product.

**LRN-29 — a stale result is marked.** A placement result reused up to 90 days carries its measurement date; from
60 days it gains a gold age badge and an action to re-measure that skill.

### 8.6 Copy

**LRN-30 — no praise.** No "well done", no congratulation. This is a signboard, not a coach.

**LRN-31 — no promises.** Never "you will reach B2 in three months". Every trajectory statement must be a
conditional derived from the feasibility arithmetic, with its inputs visible.

**LRN-32 — every state says what happened and what to do next**, never how to feel about it.

**LRN-33 — every number carries its unit and its meaning.** "B2 · 71/100 in band, 14 of 20 correct (70%),
~315 guided hours remaining, written 184 of 150–220 words". A number whose unit the learner must guess is a
defect.

**LRN-34 — no raw database key ever reaches a DOM node.** A band printed as `below_A1` is a known defect in this
product. Every internal code passes through a display registry with an exhaustive mapping; an unmapped key
renders a safe fallback and logs an error.

### 8.7 Deliberate suppression

**LRN-35 — the interface says less than the system knows, on purpose.** Seven facts are suppressed:

| Suppressed | Why |
|---|---|
| Item CEFR level during answering | Invites avoidance, corrupts measurement |
| Stage-2 routing band | Reveals whether the candidate routed high or low; induces anxiety |
| Non-monotonic anomaly flag | Accusing a learner of guessing undermines trust in the instrument |
| Grading failure internals (stack traces, phoneme posteriors, temperature) | The learner needs to know it failed and whether to retry, not why the model failed |
| Results for skills the certificate does not examine | An unused number invites confusion |
| Mastery probability as a decimal | Precision the learner cannot act on |
| Raw target coordinates as coordinates | Meaningless without the band vocabulary |

### 8.8 What breaks on a narrow viewport

**LRN-36 —** Five mechanisms break below the recorded reference width and are not designed for mobile: the
two-column split for reading and writing; three to four simultaneous listening items under one audio track; the
four-column skill matrix and six-band horizon track; the 120-cell free-drilling topic matrix; and the on-screen
keyboard consuming roughly 45% of vertical space during a writing sitting.

### 8.9 Audio equipment and sitting settings

This section governs hardware. It exists because a listening exam with a dead speaker is discovered within
seconds, while a speaking exam with a dead microphone is discovered after sixty seconds of silence, when the AI
grader returns a failure and the learner loses either a skill in their placement result or a credit in practice.
The two failures are not symmetric, so they do not get the same treatment.

**Permission**

**LRN-37 — permission is a hard gate, scoped to the skills actually examined.** A sitting may not begin unless the
equipment its certificate requires has been verified. A certificate examining speaking requires a granted
microphone permission. A certificate examining listening requires a working output device. A certificate examining
only reading and writing is gated on nothing: requesting a microphone for a reading paper asks for a permission the
sitting will never use, which both annoys the learner and devalues the next genuine prompt.

**LRN-38 — permission is obtained before the sitting, never during it.** The prompt is raised on the profile
settings surface and on the pre-sitting screen, both of which the learner reaches with intent. It is never raised
inside a timed sitting: a system permission dialog appearing over a running clock is an interruption the learner did
not ask for and cannot control.

**LRN-39 — device names require permission, and that is a browser rule rather than a defect.** A browser returns
empty labels for every device until the page has been granted microphone access. A settings surface therefore offers
an explicit control labelled to say what it does ("Enable the microphone to see device names"); the listing
refreshes once permission is granted. An implementation that enumerates devices without ever requesting permission
will show "Microphone 1" and "Speaker 1" forever and must be treated as unfinished.

**LRN-40 — permission state is read, not inferred.** The settings surface queries the browser's permission state
directly and distinguishes three cases with different copy: granted, denied, and unasked. "Denied" is not shown as
"no devices found", because the remedies differ — one is a browser setting the learner must change, the other is a
device they must plug in.

**LRN-41 — a denied permission is explained in place.** The surface names the browser and the exact control path to
re-enable it, and offers a retry. A generic "check your browser permissions" line is a dead end, and it is the state
this product currently ships.

**Device selection**

**LRN-42 — the learner chooses input and output devices, and the choice persists in the browser only.** Device
identifiers are salted per origin and per browser profile, so storing them on the server would produce broken
routing on any other machine or browser. The preference lives in browser storage; the server never sees it.

**LRN-43 — list membership is live.** Devices appearing and disappearing — a Bluetooth headset connecting, a USB
interface unplugged — update the listing without a page reload. The device in use is marked, and a device that has
vanished is not silently replaced by another.

**LRN-44 — a browser without output-device routing degrades explicitly.** Where the platform cannot route audio to
a chosen output, the surface says so plainly and states that system default will be used, rather than presenting a
selector whose choice has no effect.

**LRN-45 — the chosen output device applies to every audio player in the sitting**, including review playback after
submission. A preference that only affects the first player is a trap.

**Verification**

**LRN-46 — a microphone test measures signal, not permission.** The test captures a short window and reports one of
three outcomes: signal detected, no signal detected, or measurement unavailable. Only the middle outcome is evidence
about the learner's hardware; the third is evidence about the browser and must not be treated as a failure.

**LRN-47 — no signal blocks by default, with a stated-cost override.** Where the test measures silence during a
speaking-gated sitting, the sitting does not start, and the learner is told why. A confirmation path exists for a
learner who asserts their hardware works, and taking it states the consequence in advance: the speaking skill will
very likely be reported as not measured, and the overall will be computed from the remaining skills. A hard refusal
without an override converts a broken headset into a permanently unmeasurable account.

**LRN-48 — an output test is a real sound, not a visual pass.** The test plays an audible sample through the chosen
device at the learner's chosen volume and requires the learner to confirm they heard it. A "test passed" badge with
no audible sample proves nothing about a muted or misrouted device.

**The sitting settings control**

**LRN-49 — one settings control, present on every sitting surface, in the topbar.** It carries the font-size and
contrast controls and the audio-device controls together, because they share one property: a learner changes them
rarely, and when they do they need all of them. It is opened by a labelled control, never an icon alone.

**LRN-50 — the audio portion is hidden on sections that use no audio.** Within a multi-skill paper, device
selection is suppressed on a reading or writing section and returns on the next listening or speaking section. A
control that appears and disappears between items is worse than either state; a control offering device selection
during a reading passage is noise. Font size and contrast remain available throughout, because they apply to every
section.

**LRN-51 — the settings control appears on placement, practice, and exam sittings alike.** Placement is the longest
reading surface in the product and has the same need for type size; practice has the same need for device
switching. Restricting the control to mock exams would be an accident of which screen was built first.

**LRN-52 — continuous volume is not duplicated.** Volume belongs to the audio player itself, where it sits beside
the audio it controls and is reachable without opening a dialog. The sitting topbar carries no second volume
control: two sliders for one value is a defect, not redundancy.

**Failure during a sitting**

**LRN-53 — an audio device lost mid-sitting pauses and offers recovery.** Where the output device disappears while
audio is playing, playback stops rather than silently rerouting to system speakers — in a scored listening section,
audio escaping into the room is a worse outcome than a pause. The settings control opens, the learner selects a
replacement, and playback resumes shortly before the interruption so the content just missed is recovered.

**LRN-54 — recovery is unlimited and the clock does not stop.** The risk is accepted and named rather than denied: a
learner can repeatedly interrupt playback to re-hear segments. Three properties bound the damage without pretending
it is zero — the settings control is reachable at any moment, so an interruption is not needed to open it; the clock
is server-authoritative and never pauses for a device event; and every recovery is recorded against the attempt for
review. A capped number of recoveries was considered and rejected: it punishes the learner whose headset genuinely
fails twice.

**LRN-54a — the gate verdict is a client attestation.** Only the browser can observe its own permission state, so
the server records the verdict without being able to verify it. A learner determined to enter without working
hardware can. That is the forced entry PLC-19a names as their own risk; the alternative — a server that pretends
to observe a browser permission — would be a lie written into the schema.

**LRN-54b — naming and availability are different failures and are reported differently.** A learner whose
permission is granted and whose list is empty is told to connect a device; a learner whose labels are withheld is
told to enable the microphone. Collapsing both into "no devices found" is the state the product ships today and is
the reason the two remedies were indistinguishable.

### 8.10 v2 accessibility hooks

**LRN-55 -** Four non-breaking hooks are present in the v1 data model so enabling them later is not a rewrite: an
examined-skills vector stored per path rather than four hardcoded cards; a per-skill `exempt_accommodated` status
so a listening skill can be excluded from the overall without failing the sitting; a `timer_multiplier` of 1.0 /
1.5 / 2.0 on timed components; and a spelling-exemption flag decoupling orthographic error from the grammatical
and lexical criteria.

---

## 9. The Staff Interface

### 9.1 Permissions

**ADM-01 — five composable permission namespaces, not job titles.** `pool.content.author`, `pool.content.review`,
`assessment.blueprint.design`, `operations.support.triage`, `system.governance.admin`. A small team may assign
several to one account; the two invariants below cannot be overridden by any combination.

**ADM-02 — no self-approval.** An account holding both author and review cannot claim, adjudicate, or clear a
review sample for any batch it uploaded or last modified.

**ADM-03 — triage is read-only.** Support may inspect items, versions, evidence spans, and learner attempts, and
may escalate a defect into the review queue. It may not edit item text, keys, rubrics, or exam structure, and may
not override a learner's placement result.

**ADM-04 — the two invariants are enforced at the database transaction layer**, not only in controllers. A check
that exists only in a controller is bypassed by the next endpoint someone writes.

### 9.2 Screens

**ADM-05 — ten staff screens replace ten controllers and thirty-one loosely coupled views.**

| ID | Screen |
|---|---|
| `S-01` | Warehouse coverage and blueprint readiness matrix (120 cells, quotas, deficits) |
| `S-02` | Bulk JSON ingestion and staging workbench |
| `S-03` | 10% stratified audit and quarantine queue |
| `S-04` | Author remediation and rejection workbench |
| `S-05` | Master-detail stimulus and child item studio |
| `S-06` | Acoustic waveform and alignment dock |
| `S-07` | Productive task and visual stimulus studio |
| `S-08` | Analytical rubric and grader calibration studio |
| `S-09` | Certificate blueprint and exam assembly workbench |
| `S-10` | Consequence impact, telemetry, and defect triage console |

### 9.3 Authoring

**ADM-06 — nine controls per child item, down from eighteen.** Skill, question type, time limit, and timing are
inherited from the parent stimulus; exam binding is gone entirely because items no longer belong to an exam
(PRN-05); status is governed by the lifecycle pipeline rather than a dropdown. The nine remaining controls are
the ones that carry a decision: topic, level, placement eligibility, stem, four options with key badges, and the
explanation bound to an evidence span.

**ADM-07 — the two hard shapes are authored in one split canvas.** Parent stimulus pinned left; two to four
children stacked right. Selecting a stimulus level immediately locks the child count and the envelope bounds, and
pre-creates the slots. Binding a child to a text span highlights it in the child's own colour and computes
intersection-over-union live, turning the overlap red at 0.15. For audio, highlighting a sentence in the
time-aligned transcript binds the child's timestamps, enforces chronological order, and shows a live
inter-item gap badge.

**ADM-08 — the level control is a constraint, not a dropdown.** A six-value dropdown is effortless to render and
guaranteed to produce miscalibrated items, because an author selects the level they intend rather than what the
text demands. The control shows four live gauges — word count or duration, lexical coverage, MLTU and grade
level, acoustic rate — and displays which band the measured text actually fits when it falls outside the selected
one.

**ADM-09 — the rubric studio is the highest-leverage screen in the product.** Twenty-eight cells (four criteria ×
seven bands), each with a qualitative descriptor of at least forty characters, deterministic binary gates, and a
mandatory quoted-evidence directive. Three calibrated anchor responses at bands 2, 3, and 4 attach to every task.
The diagnostic drawer runs the seven-response ladder and plots separation between adjacent bands, naming the
criterion responsible when a segment flatlines.

**ADM-10 — rubric discrimination is testable, not hoped for.** A rubric passes when the six benchmark composites
are strictly monotonic, every adjacent separation is at least 0.65, and the verbose trap scores at most 3.25. A
compressed rubric is rejected at authoring time rather than discovered in learner results.

### 9.4 Publishing

**ADM-11 — publishing is one door.** A single action after an audit, never a status field. Three bypasses have
already shipped unreviewed content through status dropdowns; the status field is removed from every request
shape.

**ADM-12 — six invariants, evaluated live while editing.**

| Invariant | Rule |
|---|---|
| **INV-1** | Every enabled skill has at least one section; every section declares at least one item and has exactly that many active items bound |
| **INV-2** | Every shared stimulus bound to an exam carries its complete child group, satisfying the envelope, the span-independence rule, and the audio gap rule |
| **INV-3** | Every bound item is active, has verifier agreement, and has cleared its stratum's human audit |
| **INV-4** | Every enabled productive skill has a calibrated, locked rubric and every bound task carries three anchors |
| **INV-5** | Section timers sum to the declared exam duration, and the cut-score projection is monotonic and non-overlapping |
| **INV-6** | A production exam binds no demo-prefixed item |

**ADM-13 — the gate guides rather than blocks.** The rail evaluates on a short debounce during editing and shows
each unmet invariant with a one-click route to the exact deficit. Turning a wall into continuous guidance is the
goal; making it optional is not.

**ADM-14 — an exam with finished attempts locks its structure.** No adding, removing, or reordering items, no key
edits, no question-type changes. Cosmetic fields remain editable because they cannot change a recorded score. A
structural repair requires forking to a new edition; learners mid-sitting finish the edition they started, and
every historical attempt stays pinned to the edition it sat.

### 9.5 Dangerous operations

**ADM-15 — consequence is shown before the button enables.** Every destructive action computes its blast radius
first: how many learners are on a path step using this item, how many are mid-sitting, how many historical
attempts exist and how many would be upgraded, how many published exams pin it, and what it does to the cell's
quota.

**ADM-16 — three confirmation tiers.** Structural unlink on a draft: single click with an undo window. Warehouse
retirement or a defective key: a reason code, a replacement decision for pinned mocks, and typing the exact item
identifier. Certificate deactivation or tier decommission: governance role, an explicit learner drain policy, and
a semantic phrase.

**ADM-17 — no hard deletion** of anything that has left draft or has a learner reference. Retirement is the
default state change because it removes an item from future assembly while keeping every historical attempt
renderable.

**ADM-18 — retiring a topic coordinate is prohibited.** Topics are the axes of the warehouse. Staff retire items
within a cell; they never delete the cell.

---

## 10. Transition

### 10.1 Two meanings coexist

**TRN-01 —** During the transition a **band** and a **CEFR level** both exist and mean different things. A
certificate band is a row in that certificate's own table; a CEFR level is a position on the Council of Europe
scale produced by placement. Every reader either works in both worlds or must not ship until the world changes.

**TRN-02 — the worst outcome is not a crash.** It is a learner seeing two different answers to "what is my
level" on the same day. Consistency, not uptime, is the hard constraint of the sequence.

**TRN-03 — a certificate band is never silently equated to a CEFR level.** A crosswalk requires a defended
per-skill standard-setting study. Two of five certificates have no CEFR-named band at all, so a design assuming
every certificate can produce a level is broken for 40% of the product.

### 10.2 The nine surviving readers

Each of these reads a band or a level and must be adapted, not replaced.

| Reader | Today | After |
|---|---|---|
| **Mock exam** | Scores one paper on the certificate's scale; stores and displays a band | Keeps the certificate score; **adds** a projected CEFR level as a separate, labelled quantity. The two are never merged into one figure |
| **Exam review** | Per-question review beside the attempt's band | Binds each item to its `(item_id, version)` and shows the CEFR level after submission |
| **AI insight** | Narrative quoting a single band string | Receives a structured payload: four per-skill coordinates, overall, limiting skill, target, and feasibility. Narrates only; never computes |
| **Weakness analysis** | Aggregates accuracy by section and by 15 legacy categories | Aggregates by topic and level, because flat aggregation conflates stimulus difficulty with cognitive mastery |
| **Goal and feasibility** | Subtracts one band's study hours from another | Uses a continuous CEFR hours function; returns `cannot_judge` where evidence is absent |
| **Calibration** | Compares an unstratified accuracy percentage against a band | Compares only accuracy **at the learner's own level**, and proposes — never applies — a change |
| **Credit ledger** | Append-only; charges for mock exams and AI grading | Unchanged in financial structure. Placement, path steps, free drills, and failed-AI retries remain free |
| **Mentor booking** | Sends a band string to the mentor | Sends a structured multi-skill standing payload, and says "does not examine this skill" where the certificate does not |
| **Saved questions** | Links to items in the legacy bank | Links to `(item_id, version)`, renders the pinned version even when retired, and flags a retired-defective item |

### 10.3 Data disposition

**TRN-04 — the rule that decides preservation.** *Preserve what is an independent fact; drop what is a derived
opinion.*

- **Preserve:** anything recording a real identity, entitlement, human commitment, completed sitting, or
  append-only financial movement.
- **Drop:** anything derived from an invalid level model, an unlevelled taxonomy, or a calculation the rebuild
  redefines.

| Data | Disposition | Reason |
|---|---|---|
| **Credit ledger** | **Preserve as-is** | Append-only financial and audit fact. Exported and imported with every historical reason string intact |
| **Users and entitlements** | **Preserve and augment** | Identity and entitlement are facts |
| **Certificate catalogue and band tables** | **Preserve and augment** | The native band table remains correct; it gains examined-skill vectors, per-skill cut scores, and per-skill target coordinates |
| **Completed mock attempts** | **Preserve**, tagged `legacy_v1` | A sat exam is a self-contained historical fact. Renders its native score and band as a historical snapshot; never converted to CEFR; never feeds learner state |
| **In-progress and placement attempts** | **Drop** | Their results are derived from the defect being replaced |
| **Placement results** | **Drop and start clean** | Derived from a two-skill form with no items above the third difficulty level |
| **Topic mastery** | **Drop** | Keyed to fifteen unlevelled buckets with no bijection to the 120 cells |
| **Learning paths, templates, modules, steps** | **Drop and regenerate** | A single-band four-week checklist cannot be migrated to a per-skill, sliding-window model |
| **Insights** | **Drop**, except those attached to preserved mock attempts | Narrative quoting single-band codes would contradict the new display |
| **Legacy items, topics, lessons, rubrics** | **Drop and replace** | No 1–5 to CEFR mapping is invented |
| **Saved questions** | **Drop legacy links**; start clean against the warehouse | The old items no longer exist to point at |
| **Mentor bookings** | **Preserve**; refresh the payload | An operational commitment to a human being |

**TRN-05 — preserved mock attempts carry an epoch tag and are read-only.** They never feed learner state, never
underwent a fake conversion, and never render alongside a placement level without a distinct header. The
learner's question "what is my level" is answered by placement and the path, not by a two-year-old mock.

### 10.4 Single writer

**TRN-06 — seven derived facts, seven owners.** Every derived snapshot has exactly one writer, and that writer is
identified explicitly so a second one is a detectable defect rather than a drifting number.

| Derived fact | Owner |
|---|---|
| Placement result | The placement snapshot writer |
| Learner per-skill state | The skill-state writer |
| Attempt score and band | The attempt scoring writer |
| Topic capability | The capability writer |
| Step closure | The step closure writer |
| Window synthesis | The path window synthesizer |
| Credit movement | The ledger writer |

**TRN-07 — three structural assertions make a second writer fail loudly.** A required writer identifier column;
a trigger that rejects a write whose declared writer does not match the fact's owner; and a monotonic projection
version that prevents an older callback overwriting a newer result. A two-writer bug must fail at write time, not
silently produce two answers at read time.

**TRN-08 — the confirmed defect is the template.** A placement carrying free responses could not complete because
one writer left the attempt submitted with a null band while another marked the placement done with a null band.
The class is: *a derived snapshot written beside its source.* The rule that prevents it is TRN-06.

### 10.5 Sequence

**TRN-09 — content is the long pole.** Code can be written in parallel with content authoring; validated items
cannot be authored in parallel with anything. The sequence is ordered by content availability first and
consistency second, never by what is easiest to build.

| Phase | Ships | Gate |
|---|---|---|
| **1. Warehouse and staff tooling** | Lifecycle tables, gates 1–4, screens S-01 to S-08 | **Zero items required.** Proves the pipeline can refuse a bad item before any content exists |
| **2. Content and rubric lock** | Authored pool, 10% audit, calibrated rubric versions | Demo tier (456 units + 120 lessons) and MVR tier (1,120 + 120) with no empty cells |
| **3. Dark engines and learner UI behind a flag** | Placement, path, adapted readers, screens | Demo-tier content; demo accounts only |
| **4. Cutover** | Schema applied fresh; preserved data imported; flag flipped | MVR content live; preserved ledger and mock attempts verified by checksum |
| **5. Exam re-publication and pool expansion** | Experiments bound and published; pool reaches full volume | Six invariants green per exam; telemetry at N = 30 |
| **6. Removal** | Legacy paths, the flag, and the shims deleted | Every exit criterion met |

**TRN-10 — the flag is account-level, not screen-level or user-by-user.** Two code paths for one behaviour drift
apart; a flag that can split an individual learner's experience produces exactly the two-answers failure TRN-02
forbids.

**TRN-11 — the cutover is a single window for the data, not for the code.** Applying a fresh schema, importing
preserved data, and flipping the flag happens once, inside a stated window, with the previous database instance
retained as the rollback. Code is deleted incrementally afterwards, as replacements verify — a two-path coexistence
measured in months is the failure mode, and a two-hour data cutover is not.

**TRN-12 — the last thing switched off is the legacy level model itself**, and it may only be deleted when no
reader produces a band-derived level, no attempt is mid-sitting on a legacy exam, and the consistency assertions
below pass across all accounts and all five certificates.

### 10.6 Consistency, defined so it can be checked

**TRN-13 — six identities must hold across every screen after every state change.**

1. Every surface answering "your level" reads the same per-skill state and returns identical level and position.
2. Wherever an overall appears, it equals the weakest measured skill.
3. Every distance figure derives from the same target coordinate.
4. A mock exam's score is always labelled a certificate score, never a level.
5. Every mastery figure comes from the capability record, not from a recomputed percentage.
6. Every balance equals the ledger sum.

**TRN-14 — a step is safe to ship on evidence beyond green tests**: a concurrency pass with simultaneous
submissions and out-of-order grading callbacks; a psychometric pass proving every active item passed blind
dual-model solving and stimulus ablation; a copy-sanitisation pass proving no raw database key reaches a DOM
node; and a financial pass reconciling every ledger balance against a pre-cutover snapshot.

**TRN-15 — two smoke journeys are mandatory**: a learner who has never taken a placement, and a learner mid-path.
Either failing means the step did not ship.

---

## 11. Non-Functional Invariants

**NFI-01 — one writer per fact.** Stated in PRN-02 and enforced by TRN-06 and TRN-07.

**NFI-02 — the ledger is append-only and the balance is a sum.** No stored balance column. No debit without its
source row owned by the same account. No movement may drive a balance negative. Historical reason codes remain
readable forever, including codes for features that no longer exist.

**NFI-03 — free flows never touch the ledger.** Placement, practice, path steps, free drills, lessons, weakness
analysis, and failed-AI retries produce no ledger row. This is enforced by the absence of the code path, not by a
policy flag.

**NFI-04 — block before you charge.** Every paid action checks every precondition before a debit exists, and every
blocking screen states that nothing was deducted and offers a free route forward.

**NFI-05 — aggregates are derived.** Balance, rating, payout totals, and accuracy are sums and averages over rows,
never stored counters that can drift.

**NFI-06 — certificates never leak.** Composite keys carry the certificate down through skills, sections, exams,
attempts, and paths. One certificate's content, scoring, or rubric cannot appear in another's, and that is a
schema property rather than a policy.

**NFI-07 — the server owns time, answers, and balance.** The interface displays them; it never decides them. A
duration is a server-decided absolute expiry; the client counts down for display only.

**NFI-08 — correctness answers are never sent to the client during a sitting.** Only review and practice show
them.

**NFI-09 — grading is asynchronous for productive skills.** The interface needs a real pending state between
submission and result, including the failure state and a free retry.

**NFI-10 — an AI never invents a number in prose.** A narrative that quotes a value outside the stored facts is a
failed generation, not a stylistic choice.

**NFI-11 - accessibility for v1 is out of scope by decision; the four v2 hooks in LRN-55 are not.** The data model
carries them so that enabling accommodation later is a feature flag, not a rewrite.

**NFI-12 — desktop-first at 1440×900 is a recorded trade-off.** The five mechanisms in LRN-36 break below it
knowingly.

---

## 12. Decision Log

This chapter records what was resolved, how, and — where the originals disagreed — which one governs and why.
It also carries the decisions taken here that no original contains.

### 12.1 Ownership rulings between documents

Made by the ownership table: content owns item counts, levels, rubrics, and formats; placement owns how items are
used and what a result contains; path owns how a schedule is produced and closed; UI owns what is shown and how it
is worded; admin owns roles, upload, review, and the publish gate; transition owns what a band means outside the
rebuild and the order of operations.

| # | Conflict | Ruling | Owner who wins |
|---|---|---|---|
| **D-01** | Placement paper: 16 items per objective skill, stage 1 = 6 (A2, B1×2, B2×2, C1) vs 18–20 items, stage 1 = 8 (one per band) | **18–20 / stage 1 = 8.** A stage 1 blueprint omitting A1 and C2 cannot route a beginner or an advanced candidate, and contradicts the settled decision that stage 1 spans all bands. The 16-item blueprint also lands A2 and C1 on a 6-item denominator, where the passing bar becomes 83.3% and false demotion of true masters reaches 34.46% | placement |
| **D-02** | Number of non-overlapping forms: 8.5–10 vs 6 | **6.** Six forms is what the equalized blueprint's arithmetic supports at the available depth; the higher figure does not recompute the per-band demand | placement |
| **D-03** | Task 1 anchor: "core B1" vs the routed lower band | **The routed lower band.** An A2 learner must not receive a B1 anchor chosen before their objective result exists; the anchor is derived from the objective half by `min(level_L, level_R)` | placement |
| **D-04** | Placement-eligible depth: 5 and 10 per cell vs 42 per band (7 per cell) | **42 per band is the demand; the pool supplies 30 at A1/C2.** The gap is closed by re-tagging 12 existing practice items per peripheral band, not by a second volume table (CNT-25) | placement demand, content supply |
| **D-05** | Whether TOEIC and VSTEP can display C2 | **No level; a display ceiling only.** TOEIC scales cap at C1 and VSTEP has no C2 band. The C2 row in each table records the highest value the scale can show for a C2 coordinate, never an emitted level (LVL-14, LVL-15) | transition |
| **D-06** | Where the crosswalk lives: presentation layer only vs a versioned database table | **Both, resolved.** The table lives in the database and is read-only at the presentation layer. These were never in conflict once the two halves were separated (LVL-13) | transition |
| **D-07** | Recording window for C1–C2 speaking: 90–120 s vs 60 s | **60 s for every placement-eligible task.** Placement requires bounded, equal conditions. The longer windows remain for practice, where no cross-candidate comparison occurs (PLC-32, LVL-04) | placement |
| **D-08** | Whether a placement truncates mastery records or path rows | **Neither.** Placement's migration scope covers placement artefacts only. Path and mastery disposition is decided by the transition document's preservation rule (TRN-04) | transition |
| **D-09** | Whether the transition adds a second mutation guard to the ledger | **No.** The ledger is preserved as-is with its existing append-only and no-rewrite guarantees. Adding a second mechanism to the one table declared frozen is a second writer of one fact | transition |
| **D-10** | Whether the transition re-derives content volume | **No.** It cites the content volume table and does not recompute a second one. Where it needed placement-eligible counts it names them as a demand, not a supply | content |
| **D-11** | Item partition: by certificate vs one shared pool | **One shared pool.** A certificate declares which skills it examines and draws from those pools. This is PRN-05 and it is not negotiable | content |
| **D-12** | Whether `p₀ = 0.40` or `0.50` | **Both are correct; they measure different things.** 0.50 is the expected observed proportion for a non-master on the mastery scale (latent 0.333 + guessing); 0.40 is the expected observed proportion at ballot-level latent ability 0.20 used for the classification error table. No resolution required | — |
| **D-13** | Stage 1 reading blueprint with one B1, two B2 and one C1 child on a B2 passage | **Corrected to three B2 children.** Four children with two at B2 is 50%, below the 66% rule the same document sets. The corrected bundle is one B1, three B2, or two B2 with the remainder at B2 level | content |
| **D-14** | Daily practice ceiling: 40 items per skill vs 50–60 items overall with one session per cell | **50–60 overall and one session per cell.** The cell cap is what protects the pool; a per-skill cap still permits exhausting one cell in an afternoon | path |
| **D-15** | Open question counts stated in prose vs the table that follows | **The table governs.** Where a document says "eight questions" above seven rows, the rows are the record | — |

### 12.2 Decisions taken during consolidation

These resolve questions the originals left open, or state a choice the originals implied but never fixed.

| # | Decision | Basis |
|---|---|---|
| **D-16** | **Re-tag 12 practice items per peripheral band as placement-eligible** rather than authoring new items or reducing the form count | Zero authoring cost; the remaining 13–14 unseen practice items per peripheral cell still satisfy the practice floor (CNT-25) |
| **D-17** | **The pool is decoupled from the exam.** An item is authored against `(skill, level, topic)` and never against an exam; an exam is a query over the pool | PRN-05. Any design where an item requires an exam to exist cannot serve five certificates from one pool |
| **D-18** | **The topic axis replaces per-certificate categories.** One shared topic vocabulary, crossed with level, replaces fifteen certificate-scoped categories | A cross-certificate pool cannot be sliced by a certificate-scoped label; mastery must be comparable across certificates for a learner who switches |
| **D-19** | **Topic mastery is keyed to the cell**, so a learner's record cannot straddle two certificates | The rebuild introduces a shared pool; mastery must follow the topic, not the certificate's copy of it |
| **D-20** | **Feasibility returns `cannot_judge` for the two certificates with no CEFR-named band** rather than a prorated estimate | PRN-04. Prorating a four-skill hours figure to two skills is an unsourced arithmetic claim about a quantity nobody measured |
| **D-21** | **A single AI-scored practice task never sets a level.** It produces feedback and a rubric record marked as estimated from one task | `Φ(1) = 0.55` cannot distinguish adjacent levels; using it to set one would be measurement theatre |
| **D-22** | **Free drilling is gated on completing the week, and the gate is explained with its remaining cost** | The gate is a design choice made by the product owner; an unexplained gate is the fault this document fixes |
| **D-23** | **A repeated step states the numbers**, not just the word "repeat" | A repeat without a target reads as punishment |
| **D-24** | **Rubric descriptors are mandatory at all seven bands** before a rubric can be locked or a productive exam published | Grading against unwritten criteria is the current defect; the publish gate is the only place it can be caught |
| **D-25** | **Reading and listening evidence spans must be stored per child item**, including audio timestamps | Without them the review screen cannot jump to the evidence, and the evidence is what makes an explanation checkable |
| **D-26** | **The mock exam keeps its own result shape** and gains a separately labelled projected level rather than being merged with the placement result | A mock is a different measurement from a placement. Merging the two screens leaves a learner unable to tell which number is their level |
| **D-27** | **Certificate switching moves into profile settings**; the topbar placard stops being clickable | Switching changes path, placement, and practice pool; a one-click switch invites an accidental change that reads as data loss |
| **D-28** | **Placement is four screens, not one**: preflight and reuse, objective sitting, inter-half checkpoint and productive sitting, result | Progressive results need a natural point to show the objective half; one long page has none, and a page per section makes a 60-minute sitting feel longer |
| **D-29** | **The dashboard is three blocks and cannot become four** | Stated as a constraint rather than an open question, because the previous eleven-block version hid the one action that mattered |
| **D-30** | **Practice is three interfaces, not one wrapper** | Typed multiple choice, recorded speech, and typed composition have different interaction models; one wrapper produces a worse version of all three |

### 12.3 Resolved open questions

Each original carried open questions. Where consolidation resolved one, the resolution is recorded here with its
reason. Where an original's recommendation was overridden, the override and its reason are both stated.

| Original question | Resolution |
|---|---|
| **Content §8.1 — how deep should a cell be?** Options A (1,120), B (2,120), C (2,480) | **B (2,120) as the target; A (1,120) as the launch gate.** Ship A to unblock placement and the path, then reach B. C waits for telemetry showing more than 5% of learners exhaust a B1 or B2 cell |
| **Content §8.2 — fixed mock forms or dynamic assembly?** | **Dynamic unseen assembly at launch; pinned forms later if an institutional client requires cohort comparability.** Zero extra items; a fixed reserve of two forms would cost 416 additional units |
| **Content §8.3 — domain context split** | **Band-stratified ecological split: 50/50/0 at A1–A2, 40/35/25 at B1–B2, 45/45/10 at C1–C2.** An academic lecture at A1 violates the descriptors the level system is built on |
| **Content §8.4 — accent proportions** | **45% North American, 35% British and Irish, 20% Australasian**, stratified across every band. A TOEIC-heavy split under-prepares IELTS and VSTEP candidates |
| **Content — leaving a cell empty to ship faster** | **Never.** Coverage scales by depth, not breadth. This is the rule whose absence produced twelve empty topics |
| **Placement OQ-1 — the A1/C2 deficit** | **Re-tag existing items** (D-16), not new authoring and not a reduced form count |
| **Placement OQ-2 — inactivity expiry** | **48 hours from last activity, capped at 72 hours from start.** Fifteen minutes contradicts the no-timer decision; 24 hours is too short for a 60-minute four-skill sitting interrupted by a working day |
| **Placement OQ-3 — Task 1 anchor for a two-skill speaking and writing certificate with no objective result** | **Anchor Task 1 at B1 and route Task 2 from Task 1's grade**, falling back to the learner's declared starting band if grading exceeds three seconds. Asking the learner a receptive question would measure a skill the certificate does not examine |
| **Placement OQ-4 — study hours for the two-skill certificates** | **`cannot_judge`** (D-20). Prorating to 50% is an unsourced claim |
| **Placement OQ-5 — productive deficit at A1/C2** | **Re-tag two existing tasks per peripheral band** as placement-eligible, raising four to six per band, which is what six forms require when Task 1 and Task 2 sit at different levels |
| **Path OQ-01 — mastery thresholds and observation minimum** | **Beta-Binomial with `n_min = 12` objective items or 3 tasks, `τ_high = 0.80`, `τ_low = 0.733`.** Fixed step lengths avoid mid-step progress jumps; recency weighting prevents early errors from permanently penalising a learner who masters after instruction |
| **Path OQ-02 — step sizing and free-drill length** | **Fixed 10 items for free drills, as the settled decision requires**, with a flagged follow-up: ten long reading passages takes twenty minutes against five for grammar, so a passage-length cap may follow UX validation |
| **Path OQ-03 — daily ceiling** | **One session per cell per day, 50–60 items per day overall** (D-14) |
| **Path OQ-04 — weakest-skill skew exponent** | **γ = 1.5 with a 0.15 floor.** Concentrates on the limiting skill without starving the others over a four-week window; to be tuned against post-launch band gain |
| **Path OQ-05 — partially completed week at the boundary** | **Archive the week on schedule and re-queue unmastered topics into week N+4.** Carrying steps forward snowballs: missing three steps in week one produces a nine-step week two and the learner quits |
| **Path OQ-06 — v2 accessibility** | **All three hooks retained**: live-region announcements, modality exemption, extended time. Data model accommodates them in v1 |
| **Admin Q-01 — a defective item pinned inside a locked exam** | **Automated edition fork with a same-cell isomorph swap.** Structural immutability on the live edition is preserved, and no learner meets the defective item |
| **Admin Q-02 — answer key visible during the audit** | **Mandatory blind-first solve.** The 10% sample governs promotion of the 90%, so anchoring bias would propagate to the whole stratum |
| **Admin Q-03 — concurrent authoring** | **Optimistic version check plus a fifteen-minute lease.** Prevents silent overwrite of another author's trim or edits without real-time co-editing infrastructure |
| **Admin Q-04 — mock section assembly** | **Dual mode: dynamic unseen by default, pinned editions available.** Zero extra items, cohort forms possible later |
| **Admin Q-05 — pool depth target** | **Phased: MVR then full pool** |
| **Admin Q-06 — domain context** | **Band-stratified ecological split** |
| **Admin Q-07 — accent proportions** | **45/35/20 balanced international** |
| **Transition OQ-TR-01 — does a standalone mock exam affect learner state?** | **No.** A mock reports its certificate score and offers a one-click sync if the paper's projected level differs from the learner's state. Overwriting state automatically would let one rushed section damage a four-week plan |
| **Transition OQ-TR-02 — feasibility on the two non-CEFR certificates** | **`cannot_judge` by default** (D-20). The alternative — prorating four-skill hours to two — states a number nobody measured |
| **Transition OQ-TR-03 — what happens when calibration fires** | **A proposal card with two explicit actions.** Accepting adjusts the learner's level and re-plans unstarted steps; declining snoozes calibration for that skill for fourteen days and thirty items. Never silent, never accusatory |
| **Transition OQ-TR-04 — cutover timing** | **One cutover across all five certificates at MVR depth.** Two-stage activation is the only fallback, and objective-only placement with productive skills marked unmeasured is rejected outright: a partial result that looks complete is worse than a stated delay |
| **Transition OQ-TR-05 — full four-skill MVR or narrow launch** | **Full four-skill MVR is the primary plan**; two-stage activation at 960 listening and reading units is the permitted fallback |
| **UI §7 — anything the design system cannot express** | **Nothing was found that requires an exception.** The five viewport breakages in LRN-36 are recorded trade-offs, not design-system failures |

---

## 13. Provenance

This document consolidates six independently produced specifications. Where a reader needs the reasoning behind a
rule — the literature it rests on, the options that were rejected, the argument for a particular number — the
originals remain the record:

| Original | Subject |
|---|---|
| `docs/research/output/1.md` | Content blueprint, level model, topics, formats, rubrics, volume arithmetic |
| `docs/research/output/2.md` | Placement: multistage design, routing, scoring, result contract |
| `docs/research/output/3.md` | Learning path: window, step lifecycle, mastery, scheduling, re-planning |
| `docs/research/output/4.md` | Learner interface: screens, states, design system, copy rules |
| `docs/research/output/5.md` | Staff interface: permissions, authoring, rubric calibration, publish gate |
| `docs/research/output/6.md` | Transition: band and level semantics, surviving readers, disposition, sequence |

Three things are true of the originals and deliberately not carried forward:

1. **Their platform was not this product's.** Mechanisms expressed in PostgreSQL terms are restated for SQL
   Server (PRN-01). Where SQL Server has no exact equivalent — transaction-local session settings — the
   difference is stated rather than papered over.
2. **They were written without access to the implementation.** They name entities that do not exist in this
   codebase and do not name the ones that do. This document keeps their vocabulary because it describes what
   should exist; mapping it onto the current schema is the next step, not this one.
3. **Their citations are unverified.** Several sources cannot be checked, including two future-dated references
   and a works-cited entry asserting the exact claim one document spends a section refuting. They are preserved
   as written; nothing in this document depends on any of them.



