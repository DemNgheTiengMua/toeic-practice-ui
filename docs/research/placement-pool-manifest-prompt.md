# Prompt: generate a placement pool manifest

You are generating a **question pool manifest** for an English-certification platform. The file you
produce is imported by an admin screen and stored as items. Nothing you write is displayed to a learner
directly; it becomes the stock a placement test draws from.

Read this whole document before writing. The acceptance criteria at the end are what will be checked.

---

## 1. What the file is

One JSON file. Contact sheet:

```json
{
  "schema": "https://raw.githubusercontent.com/extra-efficiency/schemas/main/manifest.schema.json",
  "version": "1.0",
  "exam": {
    "title": "TOEIC Placement Pool — <your batch name>",
    "deliveryMode": "practice",
    "durationMinutes": 60,
    "sourceName": "<where the content comes from>",
    "description": "<one sentence a learner could read>",
    "questionCount": <exact number of questions you wrote>
  },
  "sections": [
    {
      "sectionCode": "P1",
      "groups": [ ... ]
    }
  ]
}
```

`sectionCode` must be one the certificate already defines. For TOEIC:

| Section | Skill | What it holds |
|---|---|---|
| `P1` | Listening | Photographs — one image, one question |
| `P2` | Listening | Question-Response — short audio exchange, one question |
| `P3` | Listening | Conversations — shared audio, 3 questions |
| `P4` | Listening | Talks — shared audio, 3 questions |
| `P5` | Reading | Incomplete sentences — one question each, no passage |
| `P6` | Reading | Text completion — shared passage, 4 questions |
| `P7` | Reading | Reading comprehension — shared passage, 4–5 questions |

Do **not** write sections for Speaking (`S1`–`S5`) or Writing (`W1`–`W3`). Placement draws objective
skills only; a productive item is graded by rubric, never by the band draw. A productive item in this
file is refused at import.

---

## 2. The envelope: `group`

A **group** is one stimulus plus the questions asked about it. A P5 sentence gets a group with one
question and no passage. A P7 passage gets a group with four questions and the passage text.

```json
{
  "unitNumber": 1,
  "passage": "The full passage text, or null.",
  "audioFile": "media/….mp3",
  "audioStartMs": 12000,
  "audioEndMs": 48000,
  "imageFile": "media/….jpg",
  "imageAlt": "A photo of a woman presenting at a whiteboard.",
  "transcript": "The audio script, for review only.",
  "prepSeconds": null,
  "responseSeconds": null,
  "displayOrder": 1,
  "cefrLevel": "B1",
  "domainContext": "workplace",
  "promptCopy": null,
  "questions": [ ... ]
}
```

Rules:

- **`cefrLevel` on the group is the stimulus's own level**, independent of its children. Leave it out and
  the first child's level is adopted — which is usually wrong for a passage whose questions span two
  bands. State it.
- **`domainContext`** is `workplace`, `academic`, or `general`. Register only; it never changes what is
  measured.
- **`audioFile` and `imageFile` are paths**, not uploads. If you cannot supply a real asset, omit the
  field rather than writing a path that does not exist — an import that points at a missing file is
  refused, and a placeholder path in a delivered batch is worse than an empty one.
- **`audioStartMs`/`audioEndMs`** slice a shared track. Omit both for a whole-file clip.
- **`displayOrder`** is 1-based and contiguous within the section.

---

## 3. The question

```json
{
  "stem": "According to the passage, what did the company announce?",
  "questionType": "mcq",
  "cefrLevel": "B1",
  "itemType": "passage_comprehension",
  "topicCode": "R-DET",
  "placementEligible": true,
  "explanation": "Why the key is the key.",
  "hint": "One nudge, not the answer.",
  "displayOrder": 1,
  "options": [
    { "label": "A", "text": "…", "isCorrect": true,  "rationale": "Stated in paragraph 2." },
    { "label": "B", "text": "…", "isCorrect": false, "rationale": "Contradicts paragraph 3." },
    { "label": "C", "text": "…", "isCorrect": false, "rationale": "Not mentioned." },
    { "label": "D", "text": "…", "isCorrect": false, "rationale": "True but not asked." }
  ]
}
```

### `cefrLevel` is mandatory and is not a difficulty

This is the single most important field. It is the CEFR band the item was authored at — A1, A2, B1, B2,
C1, C2. Not "easy/medium/hard", not a 1–5 scale, not derived from one. A file with no level is refused;
there is no default, because a silent A1 is how a previous batch put 212 items at the wrong band.

`pre_a1` is a reportable floor for results, never an authored level.

### `itemType` must match the modality

Fourteen types, constrained per skill. Use exactly these strings:

| Skill | Allowed `itemType` |
|---|---|
| Listening | `short_exchange`, `extended_dialogue`, `monologue_talk` |
| Reading | `sentence_cloze`, `passage_comprehension`, `text_insertion_cohesion` |
| Speaking | `descriptive_monologue`, `situational_negotiation`, `argumentative_monologue`, `visual_data_synthesis` |
| Writing | `transactional_correspondence`, `visual_data_synthesis`, `persuasive_essay`, `analytical_evaluation_essay` |

A reading item typed `short_exchange` is refused. Derive it from the shape: **a passage with questions about
its meaning is `passage_comprehension`; a blank to fill is `sentence_cloze`**, whether or not it sits in a
passage. A `P6` clozes text, so it is `sentence_cloze`; a `P7` asks about the passage, so it is
`passage_comprehension`. Only use `text_insertion_cohesion` for a genuine "where does this sentence
belong" task.

### `questionType` is the widget

| `questionType` | Use for |
|---|---|
| `mcq` | one correct option out of 3–4 |
| `mcq_3` | exactly three options |
| `multi_select` | two or more correct; also set `maxSelections` |
| `inline_completion` | a blank to fill; also set `acceptedAnswers` |
| `dropdown` | pick one from a list embedded in a sentence |
| `drag_drop` | match labels to targets |

### `placementEligible` — the field this whole file exists for

Set `true` on **every question**. Written explicitly, not omitted.

The importer reads `source.PlacementEligible ?? false`, so a missing field becomes `false` and the item is
**never drawn by a placement test**. It still works as practice and exam stock, so nothing fails and
nothing complains — the batch simply enriches a pool that placement cannot use. This is the single
easiest way to deliver a large, valid, useless file.

Placement-eligible stock is about a third of a healthy pool.

Placement-eligible stock is about a third of a healthy pool.

### `topicCode` — a closed set of twenty

**An invented code is refused, not clamped**, and the refusal lists every valid alternative. Use only
these:

| Skill | Codes |
|---|---|
| Listening | `L-ATT` speaker stance, attitude, prosody · `L-DET` factual detail · `L-FUNC` communicative function · `L-GIST` global gist · `L-INF` pragmatic inference · `L-ORG` discourse structure |
| Reading | `R-ATT` authorial stance, tone · `R-COH` cohesion, reference, text insertion · `R-DET` factual retrieval · `R-GIST` global thesis, main idea · `R-GRAM` morphosyntax, lexical precision · `R-INF` bridging inference |

Speaking and Writing have their own four each (`S-ARG`, `S-DESC`, `S-INT`, `S-SYN`; `W-ARG`,
`W-CORR`, `W-DATA`, `W-DISC`), but this file carries no productive sections.

If you cannot place an item confidently in one of the six, **omit the field** — the item type's own
topic is used instead. That is a correct file. Guessing a code is a refused file.

### `explanation`, `hint`, `rationale`

- `explanation` says why the key is correct. It is shown after the attempt.
- `hint` is one nudge. Never the answer.
- `rationale` on each option says why that option is right or wrong. A distractor with no rationale is a
  distractor nobody can defend in review.

---

## 4. What makes a batch acceptable

The importer runs four gates. A batch that fails them is refused with the reasons listed, so an
almost-right file costs a round trip.

1. **Structure** — the section code exists on the certificate, the item type matches the modality,
   `questionType` matches the item shape, display orders are 1-based and contiguous.
2. **Content** — every question has a stem, a level, and either options with exactly one key
   (`mcq`) or the fields its `questionType` requires.
3. **Stimulus** — a group that requires a passage has one; a group that requires audio names a file;
   a child's level is not two bands away from its stimulus's level.
4. **Difficulty spread** — a batch covering one band only is refused. A pool stratified on one level
   cannot route a learner to the other five.

---

## 5. Volumes — write to this shape

Placement draws a **fixed** 8 items per objective skill at stage one, then routes to a band pair and
draws 5–6 more per band. It needs **at least 5 items at every band of every objective skill**, or it
falls back to a static paper. That floor is per **skill**, not per section.

### Group size is only enforced where it is greater than 1

`P3` and `P4` have `ClusterSize = 3`, so a conversation group must hold exactly 3 questions.
**`P5`, `P6` and `P7` have `ClusterSize = 1`, so no group size is enforced.** A `P6` passage group may
hold 4 questions and a `P7` passage group may hold 4 — which is what the shipped seed does.

Do not split a passage to hit an item count. Write whole groups.

### For a Reading-only batch (`P5`, `P6`, `P7`)

| Section | Groups per band | Items per band | itemType |
|---|---|---|---|
| `P5` | 3 groups × 1 question | 3 | `sentence_cloze` |
| `P6` | 1 group × 4 questions | 4 | `sentence_cloze` |
| `P7` | 1 group × 4 questions | 4 | `passage_comprehension` |
| **Total** | | **11 per band** | |

Six bands → **66 questions**. Any band supplying fewer than 5 items makes the draw fall back to a static
paper for that band.

### For a Listening batch (`P1`–`P4`)

Needs real audio (and a real image for `P1`). If you do not have assets, do not write these sections —
see the assets rule above.

| Section | Groups per band | Items per band |
|---|---|---|
| `P1` | 1 group × 1 | 1 |
| `P2` | 2 groups × 1 | 2 |
| `P3` | 1 group × 3 | 3 |
| `P4` | 1 group × 3 | 3 |

`P2` expects **3 options**, not 4; every other objective section expects 4. The importer enforces it.

Aim higher than the floor. **30+ placement-eligible items per objective skill** gives the draw room to
avoid repeating an item across two sittings.

Aim for an even spread. Six items at A1 and six at C2 is better than twelve at B1, because the draw
routes a beginner to A1+A2 and an advanced learner to C1+C2.

---

## 6. Answer these before you write

1. Which certificate is this batch for, and which sections does it fill?
2. How many items per band per section, and does the total meet the table above?
3. Do you have real audio and image assets, or are you writing text-only sections (P5–P7)?
4. What is the source of the content, so `sourceName` is honest?

If any answer is unknown, ask rather than invent. A batch with a fabricated source name or a path to an
audio file that does not exist is refused at import and wastes the round trip.

---

## 7. Deliverable

One file: `<cert_code>_placement_pool_<batch>.json`.

Do not include:

- Speaking or Writing sections.
- Items with no `cefrLevel`.
- Placeholder audio paths if you have no audio.
- A `questionCount` that disagrees with the number of questions actually in the file.

Then state, in your reply:

- the file path
- the question count per section per band, as a table
- which of the four gates you expect to be tightest, and why
