# Generate exam content (external agent prompt)

This is the prompt that was missing beside the six SRS briefs in `docs/research/README.md`: those ask for
specification documents, this one asks for **actual item content**. A human copies the fenced block below
verbatim into an external AI agent that has **no access to this repository**, names the certificate and the
release tier, and gets back one or more ADR-0007 manifest JSON documents, plus — for a paper with audio or
images — a media list carrying each file's path, duration, speakers and script. The agent cannot produce the
binary media, and the importer refuses a referenced file that is absent from the package, so the recordings
and pictures are generated from that list before the zip is assembled. `QuestionImportService.ImportAsync`
(`src/Services/QuestionImportService.cs`) accepts the manifest as a draft exam. The schema, the closed
vocabularies and every validation rule below are copied from `docs/adr/0007-question-ingest-format.md`, the
`ImportManifest*` types, `docs/database-schema.sql`, `PoolImport.cs`, `QuestionImportService.*.cs` and
`GateOne.cs`; the volume tables are quoted from the content and placement SRS
(`docs/research/output/1.md` §6.3/§6.5, `docs/research/output/2.md` §5.3) and
`docs/specs/2026-11-04-cefr-rebuild-spec.md` D7/D8.

````text
You are generating exam content for an English exam-practice product. Your output is imported by a
server that validates every field before it writes a row.

You have no access to the repository this product lives in. Do not ask for code, files, a schema, a
database, or clarification about the codebase. Everything you need is in this message. If something is
genuinely missing, put it under `openQuestions` at the end and leave the field out — never invent a
value.

## 1. What you return

Return one or more **manifest documents**, each a single JSON object in the format below (the product
calls this ADR-0007 version 1.0). One document is one exam.

- Put each manifest in its own fenced ```json block.
- If an exam references audio or images, also return a **media list**: a Markdown table, one row per
  file, with `path` (always `media/<name>`), `kind` (audio|image), `duration_sec` (audio only),
  `speakers` and `accent` (audio only), `script` (audio only — the exact words spoken), and `alt`
  (image only — what the picture shows). Do NOT put the media list inside `manifest.json`: the parser
  refuses unknown members, so any extra key is a hard error.
- After each manifest, return an **`openQuestions` list**: every decision you could not justify from
  this prompt, with the options you considered and what you chose instead. An empty list is a valid
  answer only if you had no such decision.
- Return no commentary before the manifests.

The request you were given names two things. If it does not, stop and ask for them rather than guessing:

1. **The certificate** (its name and its section codes). Section codes differ per certificate: TOEIC
   Listening & Reading uses `P1`–`P7`, TOEIC Speaking uses `S1`–`S5`, TOEIC Writing uses `W1`–`W3`,
   IELTS uses `REC`, `PAS`, `T1`, `T2`, `INT`. A section code that is not this certificate's is refused.
2. **The release tier**: `demo` (Tier 1), `mvr` (Tier 2, the launch gate) or `full` (Tier 3, the
   production pool). The tier fixes how many items you produce (§9).

## 2. The manifest schema

JSON comments (`//`) and trailing commas are tolerated by the parser, so this skeleton may be pasted and
edited as-is. `?` marks an optional member; everything else is required. Write member names in camelCase:
matching is case-insensitive, but a **mistyped or unknown** name is refused rather than ignored.

This is the only upload shape that becomes an exam. A second, unrelated format exists in the product's
specification for bulk pool load (`UnifiedSkillContentPool` 2.0.0, with `schema_version`, `stimulus_groups`
and `items[].correct_option`); it does not create a paper and it refuses productive tasks. Where the two
disagree on member names or shape, **this document wins**, because this is what the exam importer reads.
Where they disagree on standards — topic vocabulary, level definitions, volume, eligibility — the
specification wins, and this document quotes it.

```json
{
  "$schema": "https://raw.githubusercontent.com/extra-efficiency/schemas/main/manifest.schema.json", // string? editor hint only
  "version": "1.0",                                  // string? if present it must be exactly "1.0"
  "exam": {                                          // object, required
    "title": "…",                                    // string, required, <=160 chars, not already a live exam of this certificate
    "purpose": "mock",                               // string? mock | placement | speaking_practice | writing_practice (default mock)
    "deliveryMode": "strict_exam",                   // string? strict_exam | practice (default strict_exam)
    "durationMinutes": 45,                           // int, required, > 0
    "sourceName": "…",                               // string? <=120 chars
    "description": "…",                              // string? <=500 chars
    "questionCount": 4                               // int? > 0 when given; metadata only, never compared to the real count
  },
  "sections": [                                      // array, required, at least one
    {
      "sectionCode": "P5",                           // string, required, a section code of the chosen certificate
      "groups": [                                    // array, required, at least one
        {
          "unitNumber": 1,                           // int? > 0 (default 1)
          "passage": null,                           // string? the reading passage shared by this group's children
          "audioFile": null,                         // string? path under media/ (.mp3 or .wav); .zip upload only
          "audioStartMs": null,                      // int? with audioEndMs, or neither; >= 0
          "audioEndMs": null,                        // int? with audioStartMs, or neither; > audioStartMs
          "imageFile": null,                         // string? path under media/ (.jpg, .jpeg or .png)
          "imageAlt": null,                          // string? <=400 chars, required whenever imageFile is set
          "transcript": "…",                         // string? the audio script, shown after the sitting
          "prepSeconds": null,                       // int? >= 0
          "responseSeconds": null,                   // int? > 0
          "displayOrder": 1,                         // int? 1-based position of this group in its section
          "cefrLevel": "B1",                         // string? the STIMULUS's own level, A1-C2 (defaults to the first child's)
          "domainContext": "workplace",              // string? workplace | academic | general (default general)
          "promptCopy": null,                        // string? a productive task's instruction, where it is not the passage
          "questions": [                             // array, required, at least one
            {
              "stem": "…",                           // string, required; the question itself, never placeholder text
              "cefrLevel": "B1",                     // string, REQUIRED: A1 | A2 | B1 | B2 | C1 | C2
              "questionType": "mcq",                 // string, required: one of the 7 render formats (§4)
              "topicCode": "R-GRAM",                 // string, required by this prompt: one of the 20 topics (§3)
              "itemType": "sentence_cloze",          // string: one of the 14 item types (§5); required for listening, speaking, writing
              "placementEligible": false,            // bool, REQUIRED by this prompt (absent in the file means false)
              "categoryCode": null,                  // string? a legacy category of this certificate, if you were given one; else omit
              "explanation": "…",                    // string, required by this prompt; why the key is the key
              "hint": "…",                           // string? <=1000 chars; a nudge before answering, never the answer
              "displayOrder": 1,                     // int? 1-based position of this question in the file's order
              "evidenceStart": 332,                  // int? with evidenceEnd and evidenceUnit together, or none
              "evidenceEnd": 378,                    // int? with evidenceStart and evidenceUnit; > evidenceStart
              "evidenceUnit": "char",                // string? "char" for text, "sec" for audio
              "evidenceQuote": "…",                  // string? the words the evidence span covers, verbatim
              "sampleAnswerOrImageDesc": null,       // string? <=1000 chars; a read-aloud script or a picture description
              "referenceKind": null,                 // string? "script" | "image_desc"; must accompany the field above
              "prepSeconds": null,                   // int? productive only; >= 0
              "responseSeconds": null,               // int? productive only; > 0
              "minWords": null,                      // int? productive writing; > 0
              "maxWords": null,                      // int? productive writing; >= minWords
              "maxSelections": null,                 // int? multi_select only; must be >= 2
              "allowNumber": true,                   // bool? inline_completion only; may the blank hold a digit (default true)
              "acceptedAnswers": ["within"],         // string or array? inline_completion only; the accepted answer set
              "acceptedAnswersJson": null,           // string? the same set already encoded as a JSON array; use one or the other
              "requiredKeywords": null,              // array of string? free_response; words a passing answer must contain
              "options": [                           // array: exactly four for an objective item, absent for a productive one
                { "label": "A", "text": "…", "isCorrect": true,  "rationale": "…" }, // label A-D (<=4 chars), text required, rationale required by this prompt
                { "label": "B", "text": "…", "isCorrect": false, "rationale": "…" },
                { "label": "C", "text": "…", "isCorrect": false, "rationale": "…" },
                { "label": "D", "text": "…", "isCorrect": false, "rationale": "…" }
              ],
              "criteria": [ { "code": "GRAM" } ]     // array? free_response only; each code must already exist for this certificate's skill
            }
          ]
        }
      ]
    }
  ]
}
```

A **productive** (speaking/writing) question carries `prepSeconds`, `responseSeconds`, `minWords`,
`maxWords`, `criteria` and **no** `options`. An **objective** (listening/reading) question carries
`options` and **none** of the timing or word members.

## 3. Closed vocabulary: the twenty topics

Exactly twenty topic codes exist, six listening, six reading, four speaking, four writing. There is no
twenty-first. A code outside this list is refused, and the refusal prints the list. Use these spellings,
uppercase, exactly.

| Modality | Code | What it measures |
|---|---|---|
| listening | `L-GIST` | global gist, purpose, macro-topic |
| listening | `L-DET` | explicit factual detail and extraction |
| listening | `L-INF` | pragmatic implication and logical inference |
| listening | `L-ATT` | speaker stance, attitude, prosodic emotion |
| listening | `L-FUNC` | communicative function and speech acts |
| listening | `L-ORG` | discourse structure, signposting, synthesis |
| reading | `R-GIST` | global thesis, purpose, paragraph main idea |
| reading | `R-DET` | local factual retrieval and paraphrase matching |
| reading | `R-INF` | bridging inference and logical deduction |
| reading | `R-ATT` | authorial stance, rhetorical purpose, tone |
| reading | `R-COH` | discourse cohesion, reference, text insertion |
| reading | `R-GRAM` | morphosyntactic structure and lexical precision |
| speaking | `S-DESC` | informational description and personal framing |
| speaking | `S-INT` | transactional interaction and option negotiation |
| speaking | `S-ARG` | sustained monologic argumentation and justification |
| speaking | `S-SYN` | integrated summary and visual/data interpretation |
| writing | `W-CORR` | transactional correspondence and register control |
| writing | `W-DATA` | visual data, process and informational synthesis |
| writing | `W-ARG` | thesis-driven persuasive and opinion argumentation |
| writing | `W-DISC` | multi-perspective problem-solution and evaluation |

A topic belongs to exactly one modality. A listening item on a reading topic is refused.

## 4. Two axes, kept apart: item type and render format

`questionType` is the **widget** the sitting draws (7 values). `itemType` is the **semantic type**, what
the item measures (14 values across 4 skills). They are not interchangeable: two `passage_comprehension`
items can render as `mcq` and as `multi_select`, and `mcq_3` and `dropdown` can carry the same three
options and one key while drawing differently.

The seven render formats — the legible value of `questionType`:

| `questionType` | Plain meaning |
|---|---|
| `mcq` | four options A–D, exactly one correct, drawn as a radio group |
| `mcq_3` | three options, exactly one correct, drawn as a radio group |
| `multi_select` | several correct answers, drawn as checkboxes; `maxSelections` says how many may be picked |
| `inline_completion` | the learner types into a blank; the key is `acceptedAnswers`, not a label |
| `dropdown` | the options are a select list |
| `drag_drop` | the options are dragged onto a target |
| `free_response` | a text area, or a recording; the only legal widget for speaking and writing |

Hard pairing rules, checked by the database and by the importer:

- A **listening or reading** item uses one of `mcq`, `mcq_3`, `multi_select`, `inline_completion`,
  `dropdown`, `drag_drop`. It may **not** be `free_response`.
- A **speaking or writing** item is **always** `free_response` and nothing else. A writing task drawn as
  a radio group measures nothing.
- `inline_completion` must carry `acceptedAnswers` (or `acceptedAnswersJson`), and it is the only
  question type whose four option columns are empty while a key exists.
- Use `mcq` with four options unless the section blueprint you were given says otherwise. The shared pool
  holds four-option single-best-answer items; the 25% guessing floor every score is calibrated against
  depends on that count.

## 5. The fourteen item types, by modality

`itemType` must belong to the section's modality. A reading item cannot be a listening type.

| Modality | `itemType` | Topics it may carry | Stimulus |
|---|---|---|---|
| listening | `short_exchange` | L-FUNC, L-DET, L-ATT | shared audio 20–45 s, 2 speakers, 2–4 turns |
| listening | `extended_dialogue` | L-GIST, L-DET, L-INF, L-ATT, L-ORG | shared audio 45–120 s, 2–3 speakers |
| listening | `monologue_talk` | L-GIST, L-DET, L-INF, L-ATT, L-ORG | shared audio 45–120 s, 1 speaker |
| reading | `sentence_cloze` | R-GRAM | standalone 15–45-word text with one blank, one item |
| reading | `passage_comprehension` | R-GIST, R-DET, R-INF, R-ATT | shared passage 60–650 words |
| reading | `text_insertion_cohesion` | R-COH | shared passage 120–650 words with four numbered markers |
| speaking | `descriptive_monologue` | S-DESC | prompt 30–60 words, optional image |
| speaking | `situational_negotiation` | S-INT | scenario 50–90 words with three candidate options |
| speaking | `argumentative_monologue` | S-ARG | proposition 40–70 words with three points |
| speaking | `visual_data_synthesis` | S-SYN | table or chart plus a 40–70-word query |
| writing | `transactional_correspondence` | W-CORR | prompt 60–100 words with three required actions |
| writing | `visual_data_synthesis` | W-DATA | chart, table or process plus a 40–70-word prompt |
| writing | `persuasive_essay` | W-ARG | thesis or policy claim 40–80 words |
| writing | `analytical_evaluation_essay` | W-DISC | multi-perspective prompt 50–90 words |

A reading item's type may be derived from the group, so declaring it is optional: a reading group **with**
a passage is `passage_comprehension`, one **without** is `sentence_cloze`. For listening, speaking and
writing it is required: the text alone does not tell a short exchange from a monologue, or a description
from an argument.

## 6. CEFR levels

- Authored levels are exactly `A1`, `A2`, `B1`, `B2`, `C1`, `C2`.
- `pre_a1` is a **reportable floor**, never an authored level. A document containing it is refused.
- **A level is authored per item, at the item.** It is never derived from a score band, a certificate's
  own scale, a difficulty number or an exam total. A certificate band (TOEIC 785–940, IELTS 6.5) is a
  scored range, not a CEFR level; do not convert between them.
- A shared stimulus carries its own level: the group's `cefrLevel`. Every child item carries its own.
  When the stimulus is `A1` or `A2`, every child must be that same level. When the stimulus is `B1` or
  higher, a child may be one band below or above it, and at least two thirds of the children must equal
  it exactly.
- If you cannot justify a level for an item, do not pick one. Leave the item out and say so under
  `openQuestions`.

## 7. What "this item is B1" has to mean

A level is a falsifiable claim about the text and the question, not a vibe. Apply the band's constraints
simultaneously; a reviewer will check them.

| Band | Lexical profile | Syntax | Cognitive operation | Distractors |
|---|---|---|---|---|
| A1 | ≥95% of tokens in the A1 core list; no abstract idioms | MLTU 5.0–7.5 words; simple clauses; only *and, but, or* | locate a stated word, number, time or set phrase in one clause | same semantic category, but absent from the stimulus or explicitly negated |
| A2 | ≥92% within A1–A2; concrete everyday and workplace nouns | MLTU 7.5–10.5; basic subordination; ≤1.3 clauses/T-unit | local paraphrase: match a stem to one sentence by direct synonym | one verbatim keyword trap with a wrong action or subject; two routine alternatives |
| B1 | ≥90% within A1–B1; ≤3% B2 tokens | MLTU 10.5–13.5; relatives, indirect speech, modals, passive; 1.3–1.6 clauses/T-unit | cross-sentence synthesis: combine 2–3 adjacent sentences | all distractors reuse stimulus concepts; at least two need a temporal reversal or a speaker misattribution ruled out |
| B2 | ≥88% within A1–B2 plus academic word list 1–5 | MLTU 13.5–17.0; non-defining relatives, participles, mixed conditionals; 1.6–2.0 clauses/T-unit | bridging inference across paragraphs; detect hedging or disagreement | partial truths, causal inversions, unsupported assumptions |
| C1 | ≥85% within A1–C1; idioms and domain collocations | MLTU 17.0–21.0; dense hypotaxis, clefts, concessive subordination; >2.0 clauses/T-unit | pragmatic implication and rhetorical evaluation | all four options are sophisticated paraphrases; distractors hinge on modality or scope |
| C2 | unrestricted, low-frequency and figurative lexis | MLTU >20.0; embedded nominal groups, inversion, ellipsis | abstract critical synthesis and micro-nuance | near-miss interpretations failing on one implicit condition |

Level is modality-specific: at the same band, spoken input has a shorter mean clause length and a higher
share of high-frequency tokens than written input.

## 8. Validation rules the importer enforces

Every rule below is checked before a single row is written, and **all** problems are reported, not just
the first. Write to these rules; do not rely on the importer to tidy your file.

### File level
1. Upload a `.json` manifest, or a `.zip` whose **top level** holds `manifest.json` plus an optional
   `media/` folder. Any other extension is refused.
2. Keep the manifest under 5 MB, the upload under 100 MB, each media file under 60 MB, the archive under
   500 entries and under 200 MB unpacked.
3. A plain `.json` upload **cannot** reference media. Any `audioFile` or `imageFile` requires a `.zip`.
4. Use no member this document does not list. Unknown members are refused by name, not ignored — a typo
   such as `"exma"` is an error. `$schema` is the one accepted extra.
5. If `version` is present it must be `"1.0"`. There is no other version.
6. `exam` and `sections` are required; at least one section; every section at least one group; every group
   at least one question.

### Exam header
7. Give `title`, at most 160 characters, and not already the name of a live exam of this certificate.
8. Give `durationMinutes` greater than 0.
9. `purpose`, when given, is one of `mock`, `placement`, `speaking_practice`, `writing_practice`.
   `deliveryMode`, when given, is `strict_exam` or `practice`.
10. Keep `sourceName` at 120 characters or fewer and `description` at 500 or fewer.
11. A `speaking_practice` exam holds only speaking sections; a `writing_practice` exam only writing
    sections.

### Section
12. Give a `sectionCode` that exists for the chosen certificate. A code shared by two skills of one
    certificate cannot be imported by code and is refused.
13. Match the section's blueprint: its option count (an `mcq`'s option count must equal the section's
    declared count), its cluster size (the number of questions per group, when the section declares one),
    and its requirements for a passage, audio or image.
14. `mcq` questions go only in listening or reading sections; `free_response` questions go only in
    speaking or writing sections.

### Group (stimulus)
15. Give `audioFile` and `imageFile` paths that sit under `media/`, are relative, and exist in the
    package. A path that climbs out of `media/` is refused.
16. `audioFile` ends in `.mp3` or `.wav`; `imageFile` ends in `.jpg`, `.jpeg` or `.png`.
17. Give `imageAlt` (at most 400 characters) whenever `imageFile` is set.
18. Give `audioStartMs` and `audioEndMs` together, with `audioStartMs` at 0 or more and `audioEndMs`
    after it; or give neither.
19. `prepSeconds` is 0 or more; `responseSeconds` is greater than 0.
20. One group is one shared stimulus. Put a passage, an audio file or a prompt on the **group**, never
    repeated on each child. Give every group and every question an explicit 1-based `displayOrder` with no
    gaps: the paper renders in (group `displayOrder`, then question `displayOrder`) order.

### Question, all types
21. Give `stem`. Never leave placeholder text (`Lorem`, `TBD`, `[Question #1]`, `…`) in a stem.
22. Give `cefrLevel`, one of the six authored levels. An item with no level is refused, and the refusal
    names the level.
23. Give `questionType`, one of the seven render formats.
24. Give `topicCode` from the twenty, matching the section's modality.
25. Keep `hint` at 1000 characters or fewer.
26. Keep an evidence span consistent: `evidenceStart`, `evidenceEnd` and `evidenceUnit` come together or
    not at all; the unit is `char` for text and `sec` for audio; the end is after the start; the quote is
    the words that span covers.
27. Give `sampleAnswerOrImageDesc` and `referenceKind` together, or neither. `referenceKind` is `script`
    or `image_desc`.

### Per question type
28. `mcq`: at least 2 options (use exactly 4 unless the section says otherwise), **exactly one**
    `isCorrect`, and no `prepSeconds`, `responseSeconds`, `minWords`, `maxWords` or `criteria`.
29. `mcq_3`: exactly 3 options, exactly one correct.
30. `multi_select`: at least 2 options, at least one correct, `maxSelections` at least 2, `maxSelections`
    not greater than the option count, and never more correct options than `maxSelections`.
31. `inline_completion`: carry `acceptedAnswers` (or `acceptedAnswersJson`); `minWords` and `maxWords`,
    when given, are greater than 0 and `maxWords` is at least `minWords`; no prep or response seconds.
32. `dropdown` and `drag_drop`: at least 2 options and at least one correct.
33. `free_response`: **no options at all**; `prepSeconds` 0 or more; `responseSeconds` greater than 0;
    `minWords` greater than 0 when given; `maxWords` at least `minWords` when given; every `criteria`
    code must already exist for the section's skill.
34. Every option needs a label and non-blank text; labels are unique within the item and at most 4
    characters long.

### The key
35. Mark correctness with `isCorrect` on the options. The importer derives the stored key from them: a
    comma-separated ascending label set, no spaces — `A`, or `A,C` for a multi-select. You never write a
    key string yourself.
36. Leave at least one option correct. An item whose options are all wrong has no key and is refused.

### Explanation and rationale
37. Give every item a non-empty `explanation` saying why the key is the key. `explanation` is the single
    editable field after import, because it is what teaches after the answer.
38. Give **every option**, the key and each distractor, a `rationale` saying why it is right or wrong.
    The exam import writes an item with a missing rationale, and the item then carries no explanation of
    its distractors for the rest of its life; the pool ingestion seam (`POST /api/admin/imports/pool`)
    refuses it outright with `gate1.feedback.rationale`. Write it on every option so the item is whole
    whichever door it later passes through.

### What happens to an item the pool cannot hold
39. An objective item that is not four options with one key — two or three options, a multi-select, a
    typed blank — still imports **into the paper**, and a warning names it as not written to the shared
    pool. Prefer the four-option shape so the item becomes pool stock as well.

### Quality rules the pool seam applies to the same content later
These are not checked by the exam import. They are checked when the item is judged for the pool, and an
item that breaks one is refused there (`GateOne`, `GateFour`):
40. Every pair of options differs by more than 2 character edits (`gate1.options.distance`).
41. No meta-option — nothing answerable without reading the stem: "all of the above", "none of the above",
    "both A and B", "neither A nor C" (`gate1.options.meta`).
42. The key's word count is within 25% of the mean of the three distractors (`gate1.key.length`).
43. The options are grammatically parallel: do not mix a noun phrase with a prepositional phrase.
44. No content word appears in both the stem and the key while appearing nowhere in the stimulus or the
    distractors — that lets a learner answer by matching words instead of reading.
45. Key letters balance across each (topic, level) cell: each of A–D holds 20–30% of that cell's keys.
    Vary the key position; do not put the answer at A by default.
46. Every reading and listening item on a shared stimulus carries an evidence span. Two children of one
    passage may not cite overlapping spans: their intersection over union must stay under 0.15, except a
    whole-passage gist question.
47. Listening children are served in the order their evidence is heard, with a gist item allowed at
    position 1 as the only exception. Adjacent detail or inference items are at least 8.0 seconds apart.

## 9. How much to produce: the tier is a parameter, not your choice

Volume is owned by the content SRS (`docs/research/output/1.md` §6.3 and §6.5) and by the placement SRS
(`docs/research/output/2.md` §5.3), not by you. The request names one tier. Reproduce its counts, and
state at the top of your reply which tier you produced.

Every tier fills the same 20 topics × 6 bands = 120 cells. **No cell may be shipped empty.**

| Tier | Objective items per cell | Productive tasks per cell | Per-skill totals | All four skills |
|---|---|---|---|---|
| `demo` (Tier 1) | 5 at every band | 2 at every band | 180 listening items, 180 reading items, 48 speaking tasks, 48 writing tasks | 456 units |
| `mvr` (Tier 2, launch gate) | 15 at A2–C1, 10 at A1 and C2 | 4 at A2–C1, 2 at A1 and C2 | 480 listening items, 480 reading items, 80 speaking tasks, 80 writing tasks | 1,120 units |
| `full` (Tier 3) | 30 at A2–C1, 15 at A1 and C2 | 8 at A2–C1, 4 at A1 and C2 | 900 listening items, 900 reading items, 160 speaking tasks, 160 writing tasks | 2,120 units |

Stimulus packing (one stimulus, several children) is what keeps the audio and passage counts down. Demo
Tier 1 is 110 audio files for its 180 listening items and 114 stimuli for its 180 reading items; the full
pool is 330 audio files for 900 listening items and 382 stimuli for 900 reading items. Put 2–4 children on
one shared stimulus, except `sentence_cloze`, which is standalone at one item per stimulus.

Lessons are a separate deliverable and not part of a manifest: 120 core lessons, one per (topic, band),
are owned by the content SRS §6.4. Do not produce them here.

## 10. `placementEligible`: a budget, not a checkbox

Set `placementEligible` on **every** item, `true` or `false`, explicitly. Omitting it means `false`.

The placement engine may draw only items flagged `true`, and placement-eligible stock is **one third of a
cell's depth** (SRS CNT-24, and D8 in `docs/specs/2026-11-04-cefr-rebuild-spec.md`): 10 of 30 items in a
core cell (A2–C1) and 5 of 15 in a peripheral cell (A1, C2). Productive cells reserve 2 of 8 tasks at
A2–C1 and 1 of 4 at A1 and C2. That ratio is the rule: mark one third of each (topic, level) cell `true`,
never the whole cell, and never zero in a cell that has the depth for it. Where a cell is too shallow for a
third to be a whole number — a demo cell of 5 — distribute the flags across the band's six cells so the
**per-band** count lands on one third (6 cells × 5 items = 30 per band, 10 eligible per band: four cells
carry 2 and two carry 1, or any split that totals 10).

Why the budget matters: placement needs 7 items per band per objective skill per form, and six
non-overlapping forms, so 42 placement-eligible items per band. A demo cell of 5 yields 10 eligible per
band and an MVR cell of 15 yields 20 at A1/C2 and 30 at A2–C1 (the placement SRS §5.3 measures 10, 20 and
30). None of those reaches 42. That is a recorded shortfall in the tier, not something you fix by marking
more items true — state it under `openQuestions` if the tier you were asked for cannot meet it.

A productive task may be placement-eligible, but its placement envelope is **narrower than its practice
envelope**:

- Speaking, every band: recording capped at **60 seconds**. A 90–120-second C1/C2 practice task is not a
  valid placement item.
- Writing, placement: **120–140 words** for Task 1 and **180–220 words** for Task 2. Practice allows
  250–350 words at C1/C2, so an item authored for practice is not automatically valid for placement.

Declare the narrower envelope in `minWords`/`maxWords`/`responseSeconds` on the placement-eligible copy.

## 11. Prohibitions

Do not:

1. Derive a CEFR level from a band, a score, a difficulty number or an exam total. Leave the level out and
   report it instead.
2. Use a topic code that is not one of the twenty. An invented code is refused, not clamped.
3. Invent a volume, a cell depth, a form count or a lesson count. The tier in the request supplies them; if
   it does not, ask.
4. Put an item type from one modality in another: no `short_exchange` in a reading section, no
   `persuasive_essay` in a speaking one.
5. Give a speaking or writing item a radio-group render format, or any options at all.
6. Leave placeholder text in a stem, an option, an explanation or a hint. No `Lorem`, `TBD`, `[Question
   #1]`, "Detailed scoring rationale and rubric expectations".
7. Write a stem or option that depends on something in an image you did not describe in `imageAlt`.
8. Reference a numeric timestamp inside a listening stem ("at 0:42, why…"). Audio plays linearly; quote
   the phrase or anchor by speaker and topic instead, and put the time in the evidence span.
9. Let one child's stem or options clue another child of the same stimulus.
10. Repeat a passage or an audio file on several children instead of putting it once on the group.
11. Convert a certificate band to a CEFR level, or write a crosswalk.
12. Decline to state a level, a type or a topic by "using a sensible default". There is no default for a
    level, and an absent level is a refusal.

## 12. Worked example: one valid manifest

A short reading paper: section `P5` with two standalone cloze groups (one item each), then section `P7` with
one passage group (two items). Four items in all. This is the shape to copy; the prose is real content, not
filler.

Note: every item carries `placementEligible: false` because four items are documentation, not stock. In a
real request set one third of each cell's items to `true` (§10).

```json
{
  "version": "1.0",
  "exam": {
    "title": "Reading Set A — Cloze and Workplace Notice",
    "purpose": "mock",
    "deliveryMode": "strict_exam",
    "durationMinutes": 20,
    "sourceName": "Original content for the shared pool",
    "description": "Two reading sections: standalone sentence completion and a workplace notice."
  },
  "sections": [
    {
      "sectionCode": "P5",
      "groups": [
        {
          "unitNumber": 1,
          "passage": null,
          "audioFile": null,
          "audioStartMs": null,
          "audioEndMs": null,
          "imageFile": null,
          "imageAlt": null,
          "transcript": null,
          "prepSeconds": null,
          "responseSeconds": null,
          "displayOrder": 1,
          "cefrLevel": "B1",
          "domainContext": "workplace",
          "questions": [
            {
              "stem": "The branch will remain closed _____ the electrical inspection has been completed.",
              "cefrLevel": "B1",
              "questionType": "mcq",
              "itemType": "sentence_cloze",
              "topicCode": "R-GRAM",
              "placementEligible": false,
              "explanation": "'Until' marks the point in time at which the closure ends, so it is the only option that fits a state continuing up to a deadline.",
              "hint": "The clause after the blank describes the event that ends the closure.",
              "displayOrder": 1,
              "options": [
                { "label": "A", "text": "during", "isCorrect": false, "rationale": "'During' takes a period, not a completed event, so the clause does not fit." },
                { "label": "B", "text": "until", "isCorrect": true, "rationale": "Correct: it introduces the end point of a continuing state." },
                { "label": "C", "text": "beside", "isCorrect": false, "rationale": "'Beside' is a preposition of place and cannot join two clauses of time." },
                { "label": "D", "text": "beyond", "isCorrect": false, "rationale": "'Beyond' means further than a place or limit; it does not mark when something ends." }
              ]
            }
          ]
        },
        {
          "unitNumber": 1,
          "passage": null,
          "audioFile": null,
          "audioStartMs": null,
          "audioEndMs": null,
          "imageFile": null,
          "imageAlt": null,
          "transcript": null,
          "prepSeconds": null,
          "responseSeconds": null,
          "displayOrder": 2,
          "cefrLevel": "B1",
          "domainContext": "workplace",
          "questions": [
            {
              "stem": "The invoice must be paid _____ thirty days of receipt.",
              "cefrLevel": "B1",
              "questionType": "mcq",
              "itemType": "sentence_cloze",
              "topicCode": "R-GRAM",
              "placementEligible": false,
              "explanation": "'Within thirty days of receipt' is the standard commercial expression for a deadline counted from delivery.",
              "hint": "Which word expresses a limit measured from a starting event?",
              "displayOrder": 1,
              "options": [
                { "label": "A", "text": "until", "isCorrect": false, "rationale": "'Until thirty days of receipt' names an end point, not a window that begins at delivery." },
                { "label": "B", "text": "since", "isCorrect": false, "rationale": "'Since' counts forward from a past point and needs a perfect tense, which the sentence does not use." },
                { "label": "C", "text": "within", "isCorrect": true, "rationale": "Correct: it sets a deadline that falls inside the thirty days after receipt." },
                { "label": "D", "text": "despite", "isCorrect": false, "rationale": "'Despite' concedes a contrast; nothing is being conceded here." }
              ]
            }
          ]
        }
      ]
    },
    {
      "sectionCode": "P7",
      "groups": [
        {
          "unitNumber": 1,
          "passage": "NOTICE TO ALL STAFF\n\nFrom Monday, 3 March, the west entrance will be closed for repairs. The work is expected to finish on Friday, 14 March. Please use the north entrance beside the loading bay while the repairs are taking place, and allow a few minutes for the extra walking distance.\n\nStaff who park in the basement garage should allow an extra ten minutes to reach the office. The internal stairwell will also be closed, and the lift will stop only at the ground floor until the work is complete.\n\nSecurity will check badges at the north entrance. Visitors must be signed in by the staff member they are meeting. If you have any questions about the work, please contact the facilities office on extension 2200 before Friday, 28 February.",
          "audioFile": null,
          "audioStartMs": null,
          "audioEndMs": null,
          "imageFile": null,
          "imageAlt": null,
          "transcript": null,
          "prepSeconds": null,
          "responseSeconds": null,
          "displayOrder": 1,
          "cefrLevel": "A2",
          "domainContext": "workplace",
          "questions": [
            {
              "stem": "According to the notice, what should staff who park in the basement garage do?",
              "cefrLevel": "A2",
              "questionType": "mcq",
              "itemType": "passage_comprehension",
              "topicCode": "R-DET",
              "placementEligible": false,
              "explanation": "The second paragraph addresses exactly this group of staff and asks them to allow an extra ten minutes to reach the office.",
              "hint": "Read the paragraph that begins 'Staff who park'.",
              "displayOrder": 1,
              "evidenceStart": 332,
              "evidenceEnd": 378,
              "evidenceUnit": "char",
              "evidenceQuote": "allow an extra ten minutes to reach the office",
              "options": [
                { "label": "A", "text": "Allow an extra ten minutes to reach the office.", "isCorrect": true, "rationale": "Stated directly in the second paragraph." },
                { "label": "B", "text": "Move their cars to the north car park.", "isCorrect": false, "rationale": "No car park is mentioned; the notice is about entrances and the stairwell." },
                { "label": "C", "text": "Collect a new security badge at reception.", "isCorrect": false, "rationale": "Badges are checked at the north entrance, not reissued." },
                { "label": "D", "text": "Use a different entrance from next month.", "isCorrect": false, "rationale": "The closure runs from 3 to 14 March, not from next month." }
              ]
            },
            {
              "stem": "Why may staff find it harder to reach the basement garage during the repairs?",
              "cefrLevel": "A2",
              "questionType": "mcq",
              "itemType": "passage_comprehension",
              "topicCode": "R-INF",
              "placementEligible": false,
              "explanation": "If the lift stops only at the ground floor and the internal stairwell is closed, the garage cannot be reached from inside the building. The notice never says so outright, which is what makes this an inference.",
              "hint": "Combine what happens to the lift with what happens to the stairwell.",
              "displayOrder": 2,
              "evidenceStart": 428,
              "evidenceEnd": 471,
              "evidenceUnit": "char",
              "evidenceQuote": "the lift will stop only at the ground floor",
              "options": [
                { "label": "A", "text": "The repairs will take longer than three weeks.", "isCorrect": false, "rationale": "3 to 14 March is under two weeks." },
                { "label": "B", "text": "Staff will have to move to the north building.", "isCorrect": false, "rationale": "The north entrance is in the same building; no other building appears." },
                { "label": "C", "text": "The car park will close for the whole period.", "isCorrect": false, "rationale": "The notice never closes the car park." },
                { "label": "D", "text": "The lift will not serve the basement garage.", "isCorrect": true, "rationale": "Follows from the lift stopping only at the ground floor plus the closed stairwell." }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

## 13. Self-check before you answer

Answer every question. One "no" means the document is not ready.

**Structure**
1. Is `version` absent or exactly `"1.0"`?
2. Does the file use only the members in §2 — no extra keys, no `schema_version`, no `correct_option`, no
   `options[].key`?
3. Does every exam have a title (≤160 characters), a `durationMinutes` above 0, and a valid `purpose` and
   `deliveryMode`?
4. Does every section code exist for the certificate you were given?
5. Does every group declare a stimulus once, rather than repeating it on each child?
6. Is every `audioFile`/`imageFile` under `media/`, with an allowed extension, present in the package, and
   paired with `imageAlt` where it is an image?

**Levels and vocabulary**
7. Does every question carry a `cefrLevel` from A1–C2, chosen for that item and not derived from anything?
8. Is every `topicCode` one of the twenty, and does it belong to the section's modality?
9. Is every `itemType` one of the fourteen, and does it belong to that modality?
10. Where a group carries a stimulus, is its `cefrLevel` stated, and does every child sit inside the ±1
    envelope with at least two thirds exactly at the stimulus level?

**Answer shape**
11. Does every objective item have exactly four options (or the section's declared count), labelled A–D
    once each, all with text?
12. Does every objective item mark at least one option correct, and no more than `maxSelections` where
    that applies?
13. Does every productive item have **no** options, and a `responseSeconds` above 0?
14. Is `free_response` used for every speaking and writing item, and never for a listening or reading one?
15. Do `prepSeconds`, `responseSeconds`, `minWords`, `maxWords` and `criteria` appear only on productive
    items?
16. Do `evidenceStart`, `evidenceEnd` and `evidenceUnit` appear together or not at all, with `char` for
    text and `sec` for audio, and an end after the start?

**Teaching content**
17. Does every item have a non-empty `explanation` that would teach a learner who got it wrong?
18. Does every option — key and distractor — have a `rationale`?
19. Is there any placeholder text left anywhere?
20. Does any stem depend on a timestamp, on an image you did not describe, or on another item's answer?

**Pool hygiene**
21. Does every pair of options differ by more than 2 character edits, with no meta-options?
22. Is the key's word count within 25% of the distractors' mean, and are the options grammatically
    parallel?
23. Are the key letters spread across A–D rather than parked at A?
24. Do two children of one passage cite non-overlapping spans (intersection over union under 0.15)?
25. Are listening children in evidence order, with detail or inference spans at least 8.0 seconds apart?

**Budget and honesty**
26. Is `placementEligible` set explicitly on every item, and does each band's eligible count come to one
    third of that band's stock (§10)?
27. If an item is placement-eligible and productive, is it inside the narrower placement envelope (60 s
    speaking; 120–140 or 180–220 words writing)?
28. Does the document match the tier you were given, with no empty cell among the 120?
29. Is every value you could not justify listed under `openQuestions` rather than invented?

Return the manifests, then the media list, then `openQuestions`. Nothing else.
````

## How to use this

1. **Save the reply.** One manifest per exam, in a staging folder of your choosing; nothing in the repo
   reads it, and `docs/samples/` holds the reference files the import tests read, so a new file there is not
   picked up by them. If the paper references media, build a zip whose **top level** holds `manifest.json`
   and `media/` — not a folder containing them.
2. **Get the certificate's code list first.** The request must name the certificate; its section codes come
   from `GET /api/admin/imports/manifest-template?certId=<id>`, which returns a skeleton generated from that
   certificate's own `Sections` rows (`QuestionImportService.GenerateManifestTemplateAsync`). Paste the
   section codes into the prompt. Guessing them is the most common refusal. That endpoint does **not** export
   the certificate's `GradingCriteria` codes, and nothing in the repo derives them for a prompt; supply them
   from the certificate's rubric rows, or omit `criteria` entirely (it is optional) rather than guessing.
3. **Import.** Through the site: `POST /admin/exams/import` (multipart `certId` + `file`, anti-forgery
   checked, requires `pool.content.author`) on the MVC admin exam screen. Directly against the API:
   `POST /api/admin/imports` with the same multipart fields. Either way one upload creates one **draft**
   exam; publishing it is a separate admin step, where the publish checks apply.
4. **Read the refusal.** A failed batch returns `status: failed` and a batch id. `GET
   /api/admin/imports/{batchId}` lists one `ImportLog` row per problem, each naming the section, group and
   question, with `RowNo` = the question's 1-based position in the file (null for file- or group-level
   problems). Fix the file and re-upload; nothing was written, and media already stored is deleted.
5. **Do not hand-edit the validator.** Every refusal above is also a database constraint; a document that
   passes will never reach SQL Server as a constraint error (`ADR-0001`). If a refusal looks wrong, the
   finding belongs on the issue, not in a relaxed rule.
6. **A different door for pool stock.** `POST /api/admin/imports/pool?skill=reading` takes
   `UnifiedSkillContentPool` **2.0.0** (`schema_version`, `stimulus_groups`, `items[].correct_option`), not
   this format, and refuses productive tasks by design. If the two formats disagree, the shape and member
   names of **this** document come from ADR-0007, because ADR-0007 is what `QuestionImportService` reads;
   volume, topic, level and eligibility standards come from the content and placement SRS whenever they
   differ.

## What this prompt deliberately does not do

- **It does not invent volume.** Cell depths and tier totals are quoted verbatim from the content SRS
  (`docs/research/output/1.md` §6.3 "Reconciling Placement and Practice into One Unified Pool" and §6.5
  "Minimum Viable First Release (MVR) and Demo Set Volumes"): demo 180/180 items and 48/48 tasks, MVR
  480/480 and 80/80, full 900/900 and 160/160. Volume is therefore a **parameter of the request**, not a
  judgement the generating agent makes: it is told a tier and reproduces that tier's counts, or states
  which tier it produced. Producing a tier the SRS does not define, or a higher count "to be safe", is the
  failure this section prevents.
- **It does not choose the certificate's blueprint.** Section codes, option counts, cluster sizes, required
  passages/audio/images and required keyword counts are rows in `Sections`, owned by whoever holds
  `assessment.blueprint.design`; the manifest template endpoint is how their values reach the prompt.
- **It does not author rubrics.** Criteria and band descriptors are the content SRS §5.1–§5.2 and the rubric
  studio's calibration ladder (`RubricCalibrationLadder`, `docs/specs/2026-11-04-cefr-rebuild-spec.md`
  D13). A manifest may only *name* criteria that already exist; naming an unknown one is an error.
- **It does not decide how a placement paper is built.** Stage sizes, routing and the seven-item-per-band
  rule are D7 in the rebuild spec and §5.1–§5.2 of the placement SRS (`docs/research/output/2.md`). This
  prompt only supplies the `placementEligible` budget those documents require.
- **It does not run the four authoring gates.** Gate one (item hygiene), gate two (linguistic and acoustic
  profile), gate three (blind dual-model key verification) and gate four (level consistency, span
  independence) run server-side at pool ingestion, not at this import. Rules 40–47 above are quoted so an
  author clears them on the first pass, not so a manifest can claim to have passed them.
- **It does not produce lessons.** The 120 core lessons (content SRS §6.4) are a separate artifact with
  their own format (`docs/samples/lesson-template.json`, the lessons import endpoint).

**Known limitation, stated so nobody works around it.** `PlacementStageOneDrawService` and
`PlacementStageTwoDrawService` exist and are tested (`PlacementStageOneDrawTests`,
`PlacementStageTwoDrawTests`), and they are the code that would read `Items.PlacementEligible`. They are
**not registered in DI** (`src/WebAPI/Extensions/ServiceCollectionExtensions.cs` registers `IPlacementService`
only) and `PlacementService` never references them. Content marked placement-eligible therefore imports and
is stored correctly, but no live placement sitting currently draws from it. That is a wiring gap in the
product, not a defect in the content, and it is not something the generating agent should try to
compensate for.
