# Data appendix — the current content model, measured

Every number in `placement-practice-rebuild-brief.md` comes from here. An agent with no repository access
can use these CSV files to check the claims without trusting the prose.

Captured 2026-11-03 from the seeded demo database (`docs/seed-demo.sql` applied to a fresh schema).

| File | What it holds |
|---|---|
| `sections-vs-questions.csv` | Every section, per certificate and skill, with the question count it *declares* (`ExpectedQuestionCount`) beside the count it *has* |
| `exams-difficulty-spread.csv` | Every exam, with declared vs actual question count and a histogram over `DifficultyLevel` 1–5 |
| `categories-vs-questions.csv` | Every topic category, per certificate, with how many questions point at it |
| `bands.csv` | Every certificate's band table: code, readable name, min/max scaled score, cumulative study hours (NULL where the certificate carries none), display order |

## The four findings these files support

**Questions cluster at the bottom of the difficulty scale.** 116 questions total: 4 at level 1, 62 at
level 2, 43 at level 3, 7 at level 4, **0 at level 5**. See the D1–D5 columns in
`exams-difficulty-spread.csv`.

**Placement cannot discriminate.** The recommended TOEIC placement (exam 2, 24 questions) has 4 level-1,
11 level-2 and 9 level-3 items, and nothing above level 3. It has no item that could separate a B2
response from a C1 one. The IELTS and VSTEP placement exams are 10 questions each.

**Several exams declare far more than they contain.** The IELTS mock (exam 5) declares 40 and has 2. Five
sections declare 30 or more questions and have single digits. Two TOEIC CAT sections (adaptive) declare
6 and 25 questions and have **zero**.

**Topic categories are almost all empty.** Three of fifteen categories have any question at all (2, 1 and
1 question). The weakness analysis reads accuracy per category, so on this data it can speak about three
topics out of fifteen.

## What these files do *not* contain

They describe shape, not pedagogy. They cannot tell you whether a level-3 TOEIC item is genuinely B1 —
nothing in the schema records a CEFR mapping, which is precisely the gap the brief asks you to close.
