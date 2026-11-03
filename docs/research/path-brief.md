# Research brief — Rebuild the Learning Path

**Audience:** one external deep-research agent. **No repository access** — this file is self-contained.

**What you deliver:** a complete SRS that becomes specs → tickets → build.

**Scope:** everything between "the learner knows where they are" and "the learner sits the exam" — how
a plan is produced, what its steps contain, how practice content is generated and selected, how a
learner drills outside the plan, how progress is measured, and how the plan ends. Pricing is out of
scope.

**This is a delete-and-rebuild.** The existing path, practice and lesson machinery is deleted, not
extended. What exists is evidence of what went wrong, not a constraint on your answer.

---

## 1. The product

A multi-certificate English exam-practice application. A learner picks a **certificate**, sets a
**goal** (starting band, target band, exam date, hours per week), takes a **placement test** to learn
where they are, then follows a **learning path** to the exam date.

Five certificates. **They do not all have four skills:**

| Certificate | Skills examined |
|---|---|
| TOEIC Listening & Reading | Listening, Reading, Speaking, Writing |
| IELTS Academic & General | Listening, Reading, Writing, Speaking |
| VSTEP (Vietnam national test) | Listening, Reading, Writing, Speaking |
| TOEIC Multi-Stage Adaptive (CAT) | Listening, Reading |
| TOEIC Speaking & Writing | Speaking, Writing |

**Domain vocabulary to adopt:** Certificate, **Skill** (one modality), **Section** (a part of a paper),
**Topic** (a sub-area within a skill, e.g. "verb tense", "inference"), **Exam**, **Practice session** (a
drill of items), **Lesson** (authored teaching material), **Attempt** (a sitting of an exam), **Step**
(one activity in a path), **Free response** (spoken or written answer graded by AI), **Band**, **CEFR
level** (A1–C2).

**Invariants that must survive:**

- Practice and placement are **always free**. No credit ledger row, ever.
- One certificate's content never leaks into another.
- An AI never chooses a step, an item, a score, or a schedule. It grades free responses and writes
  narrative.
- Derived values (accuracy, mastery, progress) are computed from stored facts, never stored twice.
- **One writer per fact.** A previous defect came from two writers disagreeing about one answer.

---

## 2. What exists now, and why it is being replaced

### 2.1 The path is four weeks long and then stops

The planner has a hard `WindowWeeks = 4`. Week 5 has no steps. When the last step closes the path
becomes `completed`, and **nothing creates a new one**. A learner with a twelve-month goal runs out of
plan after four weeks and finds an empty screen.

### 2.2 It reads one band for the whole learner

The generator takes a **single** starting band — placement result, else latest mock exam, else the
goal — and pitches the entire plan at it. A learner who reads B2 and speaks A2 gets one number.

The placement rebuild returns **one CEFR per skill plus an overall**. A single band cannot express that.

### 2.3 It selects material by section, not by level

A step says "practise section P5" and carries no level, so it cannot be matched to "this learner needs
B1 grammar". The pool being built for placement labels every item with a CEFR level, and no part of the
path can consume that label.

### 2.4 Lessons carry no level

The lesson table has **no band or level column at all**. Thirteen lessons exist; one is attached to a
topic. A path cannot say "study this because you are B1 and weak in verb tenses" — only "study this".

### 2.5 Two question selectors that disagree

- **Standalone practice** — topic mastery with a 24-hour cooldown after a wrong answer, ten items per
  session, at most four previously seen.
- **Path practice** — serve items from a section in `DisplayOrder`, ignoring mastery and ignoring the
  cooldown. It can re-serve the item the learner just got wrong, in the same hour.

### 2.6 Session size is a constant

Ten items. A learner at A2 targeting B2 in three weeks and a learner casually practising for next year
get the same session.

### 2.7 The middle of a path is unmeasured

A step closes when the activity finishes. Whether it moved the learner toward the target is not recorded
in any actionable way. A path that cannot say "you are on week 3, and no closer to B2" is a checklist.

### 2.8 Measured state

From the seeded demo database on 2026-11-03. These numbers describe the system **before** the rebuild;
treat them as evidence, not as a target.

| Measure | Value |
|---|---|
| Learner paths / steps | 1 / 6 |
| Path templates / modules / module items | 4 / 16 / 34 |
| Topic rows that **no code reads** | 5 |
| Topic categories | 15 |
| Topic categories with **any** item | **3** (2, 1 and 1 item) |
| Items in the bank, all certificates | 116 |
| Items at difficulty 1/2/3/4/5 | 4 / 62 / 43 / 7 / **0** |
| Sections declaring ≥ 30 items | 8 |
| Sections with **zero** items | 5 |
| Lessons / lessons with a level | 13 / **impossible — no such column** |
| Lessons attached to a topic | 1 of 13 |
| The practice-session model for Speaking/Writing | **does not exist** |

The consequence: a mastery model over topics is only as good as the topics. With three usable topics,
"your weakest topic" is nearly meaningless.

---

## 3. Decisions already taken

### 3.1 The plan itself

| # | Decision | Reason |
|---|---|---|
| P1 | **The path is a sliding four-week window**, not a fixed block. As week 1 completes, week 5 is generated. | A single 52-week block is wrong by week 6 (the learner is faster or slower than predicted) and fixing it means discarding the whole path. Restarting in monthly chunks makes the learner feel they never progress. |
| P2 | **Progress is shown two ways: this week's work, and distance to the target band.** Never one number. | "Week 3, 2/4 steps done" does not answer "will I make my exam date". "34% of the way to B2" does not answer "what do I do today". They are different units and must be separate. |
| P3 | **One timeline, with steps assigned to skills.** The proportion of steps skews toward the weakest skills. Not four independent strands. | Four strands turn the path into a scheduling problem: one lagging strand either extends past the exam date or must be dropped, and both need complex rules. One band for everything throws away the four values placement just produced. |
| P4 | **A step closes by doing it, not by improving.** Mastery decides the step's *size*, not whether it closes. | Closing on improvement traps a learner who is genuinely weak, and they quit. Closing on completion alone makes the path a checklist. So: the step must be done, but if the topic's mastery is already high the step is short. |
| P5 | **Re-plan only on a significant event**, not every week: two consecutive weeks missed, a skill's CEFR changes, or the learner says the plan does not fit. | Weekly re-planning makes the plan meaningless — a learner who sees it change constantly stops trusting it. Never re-planning lets it drift uncorrected. |
| P6 | **When content is missing, substitute and say so.** Never refuse to generate; never schedule a step that cannot be completed. | Refusing means the learner gets nothing while the pool is thin — for months. Scheduling a dead step is the current behaviour and it is the worst of the three. |
| P7 | **All four end states must work:** target reached, exam date arrives, learner abandons, learner stops progressing. Priority on the first two. | The exam date is when the learner most needs to know whether they are ready. The other two can be a state plus a sentence, without a separate flow. |
| P8 | **One path per certificate.** Switching certificate switches path; it does not reset the old one. | Matches the existing `(UserId, CertId)` uniqueness. Parallel paths are unmanageable for the learner and double every screen. |

### 3.2 Practice and free drilling

| # | Decision | Reason |
|---|---|---|
| L1 | **Free drilling differs from a path step only in where it came from** — a path step is on the calendar, free drilling the learner chose. | Any other distinction (enforced order, different purpose) makes the two overlap and the learner cannot tell which to use. |
| L2 | **Free drilling is unrestricted, but the UI recommends what the path suggests this week.** | Restricting it to unlocked topics stops a learner revising for their exam, which helps nobody. No guidance at all leaves them lost. |
| L3 | **A path step's size varies by what the step is** (a short check may be 5 items, a review 20). **Free drilling is a fixed 10.** | A learner needs to know how long free practice will take. A step's size should follow its purpose. |
| L4 | **Mastery is one input to re-planning**, alongside weekly progress, band change and calibration. It does not itself rewrite the plan. | Mastery as decoration gives the learner no reason to care. Mastery rewriting the plan on every change is exactly the thrashing P5 avoids. |
| L5 | **The path reads capability, not activity logs.** Everything the learner does counts toward mastery — including free drilling. | If a learner improved through free drilling and the path does not know, the path makes them redo what they already know. That is the worst experience available. |
| L6 | **Free drilling unlocks after the week's steps are done.** | *(Owner's choice.)* It is a reward for doing the plan, not a substitute for it. **Note the interaction with P4/L5:** because a step closes only by being done, free drilling cannot be used to skip the plan — it can only make the following week's steps shorter via mastery. |
| L7 | **Mastered is a label with a consequence**: a mastered topic's steps shorten. It does not remove the step. | Removing steps means the learner can skip the plan entirely through free drilling, and the path becomes decorative. |

### 3.3 How a path is measured and ended

| # | Decision | Reason |
|---|---|---|
| E1 | **A step records what was done and how it went**, so "no improvement" is detectable. | Without this, P5's "learner stops progressing" end state cannot be detected, and the path cannot tell whether it is working. |
| E2 | **A step that shows no improvement is repeated once, then marked as a weak spot** rather than repeated forever. | Infinite repetition is the fastest way to make a learner quit. Recording it gives the re-plan something to act on. |
| E3 | **"Target reached" and "exam date arrived" both produce a readiness verdict**, reusing the existing feasibility arithmetic. | The learner's real question at the end is "am I ready", not "did the plan finish". |

---

## 4. The engines: reuse, do not rebuild

The owner's instruction: reuse what exists where it is correct. Two pieces earn their place; everything
else is candidate for deletion.

### 4.1 Keep: the single writer that closes a step

Today one function closes a step and unlocks the next, and it is called from every place an activity
finishes — practice session, mock exam, lesson. This exists because **three separate writers drifted
apart once**. Whatever replaces the path needs the same property: one writer per fact.

### 4.2 Keep: the feasibility calculation

Band distance in cumulative study hours, checked against the learner's declared hours and the exam
date, returning feasible / stretch / unrealistic, and returning "cannot judge" when a certificate's
bands carry no hours. It is already correct. Do not reimplement it.

### 4.3 Keep the sitting engine, if a step needs one

A path step that is a mock exam or a placement uses the existing attempt engine — states, expiry,
locks, draft sync, one scoring function. Do not build a second one.

### 4.4 Do not keep

Path templates, modules, module items, topic rows with no reader, and the section-drill selector. The
demo database holds 1 path, 6 steps, 4 templates and 5 unread topic rows: there is almost nothing to
lose.

---

## 5. What the SRS must answer

### 5.1 The step

1. **What does a step point at?** A section, a lesson, a topic, a skill, a level band, or several.
   Whatever you choose must work for a certificate with two skills and one with four, and must never
   name a certificate.
2. **How does a step carry a level** when lessons have no level column today? Either lessons gain one,
   or the path uses a lesson without knowing its level, or lessons are replaced by something that has
   one. Pick one and justify.
3. **What makes a step complete** (see P4) — and what is recorded so E1 works.
4. **What if a learner half-finishes a step** and leaves? Is that resumable?

### 5.2 The schedule

5. **How much work per week**, and how is that derived from the goal's hours and the band distance
   rather than asserted? The current 3–5 steps of 25–45 minutes was chosen by a ticket, not by evidence.
6. **How is the week's composition decided** — how many steps per skill, in what order, and does the
   order matter pedagogically or only for display?
7. **What happens when the learner has more hours than the content available**, and the reverse.

### 5.3 Practice content

8. **What selects the next item**, given §2.5's two disagreeing selectors. Choose one model; say why the
   other loses.
9. **Is the Topic the right unit for mastery?** With 15 topics and 3 populated, the unit as designed
   cannot work. Is the right grain the same for every skill? Does a topic belong to a certificate or to
   the language — is "verb tense" one topic shared by TOEIC and IELTS learners, or two?
10. **What does a practice session mean for Speaking and Writing?** Typed multiple-choice does not
    apply. Is a Speaking session a set of recorded tasks? How many, at what length? Does AI-graded
    practice feed the same mastery as typed drills — and if not, how does a learner whose goal is
    Writing ever advance?
11. **How does intensity vary** (L3), and what caps it? A learner drilling 200 items a day is not
    learning better, they are exhausting the pool. Is there a daily ceiling, and what do they see?
12. **How does practice relate to the CEFR level** the placement rebuild provides — at the learner's
    level, just above, or spanning?

### 5.4 The relationship between plan and practice

13. **Who wins when a path step conflicts with the cooldown?** The plan says "practise P5 today"; every
    P5 topic is cooling down from a wrong answer an hour ago. Is a path step an override, or does the
    plan bend?
14. **Can free drilling contribute to closing a step?** (L5 says the path reads capability; P4 says a
    step closes by being done. State the rule that reconciles them.)

### 5.5 Levels and adaptation

15. **Does the path follow the overall CEFR or per-skill levels?** P3 chose one timeline with
    skill-weighted steps — say how a step's level is chosen when four skills have four levels.
16. **What triggers a re-plan** (P5) and what exactly changes when it fires — which steps are
    regenerated, and what happens to the steps already done?
17. **What does the learner see** when a re-plan happens? A silent change of plan is the fastest way to
    lose trust.

### 5.6 Consuming content, not specifying it

18. **What does the path require of the content?** Not how many items exist — that is fixed by the content
    brief, a sibling document owned by another session. State what the path needs *from* it: how many
    items a topic must have before mastery can be judged, what item format a step needs, what a
    productive-skill step requires. Name the minimum; do not invent a volume table.
19. **What happens when the content brief cannot supply that minimum** for a given topic or skill? The
    path must still generate (P6). Say how the substitution is chosen and what the learner is told.
20. **How does a path step express what content it needs** — by topic, by skill and level, by section —
    in a way an authoring system can answer? This is the interface between your design and the content
    brief.

**If your design needs more content than the content brief specifies, say so explicitly and name the
gap.** "This mastery model needs 12 items per topic and the pool specifies 6" is a finding worth having.
Producing your own volume table is not — two documents promising different pools is the failure this
arrangement exists to prevent.

### 5.7 Ending

21. **What are the four end states** (P7) and what does the learner see in each?
22. **What is the readiness verdict** at the exam date (E3), and what does it do if the learner is not
    ready?

---

## 6. Known gaps, deliberately not solved here

- **Accessibility.** Out of scope for v1 by the owner's decision. Note in open questions what a future
  version would need.
- **Pricing and credit economics.** Out of scope entirely.
- **The demo content set.** This brief says how a demo differs from real content, not what it holds.

---

## 7. Deliverable format

An SRS with:

1. Scope and non-goals, including what of the current system survives.
2. **Domain model** — entities, relationships, the level model, and the step shape. A schema sketch is
   welcome.
3. **Numbered, testable functional requirements**, grouped by: generating a path, a step's lifecycle,
   free drilling, mastery and spacing, the weekly schedule, re-planning, reporting progress, ending.
4. **Business rules** not obvious from the requirements: cooldown, lapse, decay, eligibility, what
   happens when the pool is short, what happens when AI grading fails, who wins between plan and
   cooldown.
5. **What the path requires of the content** - the minimum per topic, per skill, per step type - reconciled against the content brief rather than replacing it.
6. **Open questions** with options and your recommendation.
7. **References** to real sources on spaced repetition, interleaving, retrieval practice, adaptive
   sequencing and curriculum scheduling. Cite specific work; the implementing agent will check it.

---

## 8. What to be careful about

- **Do not design for one certificate.** One of five examines two skills. A rule naming a certificate
  is a red flag — that mistake caused a defect here before.
- **Do not invent numbers.** If you cannot justify a steps-per-week figure, a session size, or an
  accuracy threshold, say it is provisional. The current 10 items and 75% threshold have no evidence
  behind them; inheriting them quietly is how a rebuild reproduces the old system with new names.
- **Spaced-repetition research is about facts and vocabulary.** Grammar and reading comprehension may
  not behave the same way. If you lean on SRS literature, say where it transfers and where it does not.
- **Speaking and Writing are the hard half.** Typed drills are a bank plus a selector. Productive skills
  are task design, rubric design, AI cost and AI failure. Do not make them an afterthought.
- **Practice is free.** Any design needing a credit, a paid grading, or a money-saving limit
  contradicts an invariant.
- **The AI is not the teacher.** It grades and narrates. It does not choose what to practise.
- **The path is not a curriculum.** It is a schedule over material that exists. If your design needs
  content that does not exist, that is a content requirement — say so.
