# Answers to the five questions — and the two corrections they exposed

Good questions. Two of them found genuine contradictions in my prompt; the answers below are checked
against the importer's code rather than from memory, and the prompt file has been corrected to match.

---

## 1. Certificate, batch name, sections

**TOEIC, batch `r1`, sections `P5` + `P6` + `P7`.**

Filename: `toeic_placement_pool_r1.json`
Title: `TOEIC Placement Pool — Reading r1`

Reading only. See answer 2 for why.

## 2. Assets — take the text-only route

**Reading-only: `P5`, `P6`, `P7`.** No audio, no images.

You are right that Gate 3 requires `audioFile` on a Listening group and `imageFile` on `P1`, and right
that Section 2 forbids placeholder paths. Those two together mean a batch without real assets cannot
honestly fill `P1`–`P4`. Any agent that "solves" this by inventing `media/sample_audio.mp3` produces a
file that either fails the gate or, worse, imports against a path that resolves to nothing at runtime.

Reading sections need neither. That is the whole reason this batch is text-only.

## 3. Volumes, and the group-count tension

**Target 3 items per band per section — 9 per band, 54 total.**

Not the 5-per-band minimum. The draw needs **at least 5 items at every band of every objective skill**
before it works at all, and that floor is per *skill*, not per section. Reading-only means one skill, so
5 per band is the absolute floor:

| | Per band | Bands | Total |
|---|---|---|---|
| `P5` | 2 | 6 | 12 |
| `P6` | 1 | 6 | 6 |
| `P7` | 2 | 6 | 12 |
| **Floor** | **5** | | **30** |

Write **9 per band (54 total)** instead: 3 `P5`, 2 `P6`, 4 `P7`. At the floor, a learner sits 18 Reading
items and the draw has no room to avoid repeating an item across two sittings. At 9 per band it can.

### The group tension — my prompt was wrong, not you

**Use the full group. `P6` = 4 questions per passage, `P7` = 4 questions per passage.**

My Section 5 table counted "items per band" and my Section 1 said `P6`/`P7` groups carry 4–5 questions.
Those are compatible only if you understand which one constrains the importer — and I did not say.

Here is the actual rule, from `QuestionImportService.BlueprintValidation.cs:78`:

> A section's `ClusterSize` is enforced **only when it is greater than 1**. `P3` and `P4` are 3, so a
> conversation group must hold exactly 3 questions. **`P5`, `P6` and `P7` are all `ClusterSize = 1`, so
> no group size is enforced at all.**

So a `P6` group with 4 questions imports fine. The shipped seed does exactly that: `P6` and `P7` each
carry **4 questions per unit**, verified in the database.

**Corrected volume table** (9 items per band):

| Section | Items per band | Group size | Groups per band | Item type |
|---|---|---|---|---|
| `P5` | 3 | 1 | 3 | `sentence_cloze` |
| `P6` | 2 | 4 | 1 group of 4 → take 2? **No** | `sentence_cloze` |
| `P7` | 4 | 4 | 1 group of 4 | `passage_comprehension` |

That `P6` row is the remaining awkwardness, and the honest answer is: **do not try to hit an item count
in `P6` by splitting a passage.** Write whole `P6` groups of 4. If you write 2 groups you get 8 `P6`
items, not 2 — and that is correct, not a violation.

So the practical target becomes:

| Section | Groups per band | Items per band |
|---|---|---|
| `P5` | 3 groups × 1 | 3 |
| `P6` | 1 group × 4 | 4 |
| `P7` | 1 group × 4 | 4 |
| **Total** | | **11 per band = 66** |

Write 11 per band, 66 for the batch. Every band then comfortably clears the per-skill floor of 5.

## 4. `sourceName`

```
"Original AI-Authored — Internal Placement Stock (r1)"
```

Use that verbatim. It is honest: nobody has verified this content against a publisher, and a reviewer who
sees the string should read it as "unvetted internal material" rather than as a citation. Do not name a
publisher, a test series, or a year.

## 5. `P6` `itemType` — `sentence_cloze`

**Use `sentence_cloze` for `P6`, and `passage_comprehension` for `P7`.**

You caught a real ambiguity. The prose rule ("a passage means `passage_comprehension`") is about how the
importer *derives* a type when the field is absent; it is not an instruction to override the field you do
supply. For `P6` the semantic type is a cloze with context, which is `sentence_cloze`.

The shipped seed agrees: `P5` and `P6` are both `sentence_cloze`, and `P7` is `passage_comprehension`.

Watch the topic fallback this triggers. `DefaultTopicFor("reading", "sentence_cloze")` resolves to
`R-GRAM`, and `DefaultTopicFor("reading", "passage_comprehension")` resolves to `R-DET`. So:

- If a `P6` blank tests **grammar or lexical form**, omit `topicCode` and `R-GRAM` is right.
- If a `P6` blank tests **cohesion across sentences** (a connector, a pronoun referent), say
  `topicCode: "R-COH"` explicitly — that is the code for text insertion and reference.
- Never leave a `P7` item to default: `R-DET` is detail retrieval, and a main-idea question is
  `R-GIST`. Say which.

---

## One thing neither of us asked, and should have

`P2` expects **3 options** (`OptionCount = 3`), and every other objective section expects 4. The importer
enforces this (`QuestionValidation.cs:195`). You are not writing `P2`, so it does not bite here — but if a
later batch adds Listening, that is the first refusal it will hit.

## Summary — write this

```
file:     toeic_placement_pool_r1.json
title:    TOEIC Placement Pool — Reading r1
sections: P5, P6, P7
per band: P5 ×3 (single questions), P6 ×1 group of 4, P7 ×1 group of 4  → 11 items
total:    66 questions across A1, A2, B1, B2, C1, C2
every question: placementEligible: true, cefrLevel set explicitly
itemType: P5 sentence_cloze, P6 sentence_cloze, P7 passage_comprehension
topicCode: P5 R-GRAM, P6 R-GRAM or R-COH (say which), P7 R-DET or R-GIST (say which)
sourceName: "Original AI-Authored — Internal Placement Stock (r1)"
```
