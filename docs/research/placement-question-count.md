# How many questions a placement test asks

**Fixed at 16, then variable between 26 and 28, for TOEIC (Listening + Reading).**

Two stages, two different answers. The total is not one number because stage two routes.

---

## Stage one — always 16

Eight items per **objective skill**, spread over all six bands:

| Band | A1 | A2 | B1 | B2 | C1 | C2 |
|---|---|---|---|---|---|---|
| Items | 1 | 1 | 2 | 2 | 1 | 1 |

TOEIC examines two objective skills (Listening, Reading), so `8 × 2 = 16`.

The ends carry one item each because stage one is a **routing instrument, not a measurement**. A
blueprint starting at A2 cannot route a beginner; one stopping at B2 cannot route an advanced learner.

**Speaking and Writing are never drawn by band.** They are free responses graded by rubric. If a
placement paper binds them, they are *added to* the paper, not counted in the 16.

## Stage two — 10, 11, or 12 per skill depending on the route

The stage-one score (0–8) picks a band pair, and each band of that pair is topped up to **7 evaluated
items**:

| Stage-one score | Routes to | Stage 2 per band | Stage 2 total |
|---|---|---|---|
| 0–2 | A1 + A2 | 6 + 6 | **12** |
| 3 | A2 + B1 | 6 + 5 | **11** |
| 4–5 | B1 + B2 | 5 + 5 | **10** |
| 6 | B2 + C1 | 5 + 6 | **11** |
| 7–8 | C1 + C2 | 6 + 6 | **12** |

## The total

| Route | Stage 1 | Stage 2 | **Total per skill** |
|---|---|---|---|
| A1+A2 (weakest) | 8 | 12 | **20** |
| A2+B1 | 8 | 11 | **19** |
| B1+B2 (typical) | 8 | 10 | **18** |
| B2+C1 | 8 | 11 | **19** |
| C1+C2 (strongest) | 8 | 12 | **20** |

Across both objective skills:

| | Low total | High total |
|---|---|---|
| Weak route (20 × 2) | 40 | |
| Typical route (18 × 2) | 36 | |
| Strong route (20 × 2) | 40 | |

**So: 36 to 40 questions, decided by how the learner scored on the first 16.**

A learner who does terribly and a learner who does brilliantly both sit more questions than a middling
one. That is deliberate: the extremes are the boundaries the test most needs to pin down, and the middle
is where the pair B1+B2 already overlaps.

## Why 7 per band

The pass rule is "≥70% correct at band L". With a finite number of items, the actual bar drifts:

| n | Real bar | False demotion of a true master |
|---|---|---|
| 5 | 4/5 = **80%** | 26.3% |
| 6 | 5/6 = **83.3%** | 34.5% |
| 7 | 5/7 = **71.4%** | — |

Seven is the only value that puts the effective threshold next to the stated one. That is why the
delivery is "7 minus what stage one already asked", not "the same as stage one asked".

## What is fixed and what is not

| | Fixed | Variable |
|---|---|---|
| Items at stage one, per skill | **8** | |
| Bands covered at stage one | **all six** | |
| Items evaluated at a routed band | **7** | |
| Which pair stage two routes to | | **by stage-one score** |
| Stage-two delivery per band | | **5 or 6, derived from the pair** |
| Total questions | | **36–40 per skill for TOEIC** |
| Deadline | | **48 h inactive, 72 h absolute** |

## On the shipped seed

The current seed draws nothing: it binds 29 items to a static paper (10 Listening, 14 Reading, 3
Speaking, 2 Writing) and the draw needs **5–6 items at every band per skill** while the pool holds 2.
With too little stock the service falls back to the static paper rather than refusing, so a learner
still sits a placement — it is just the same 29 questions for everyone.

Until the pool carries roughly 30+ placement-eligible items per objective skill, the numbers above
describe the design rather than the observed sitting.

## One correction to a common assumption

The three Speaking tasks and two Writing tasks bound to the seeded paper are **not part of the band
draw**. They are graded by rubric, and a learner's placement result reports them separately. So a paper
advertising "29 questions" is 24 band-drawn items plus 5 free responses, and the 36–40 figure above
counts only the band-drawn ones.
