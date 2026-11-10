# Generate placement content (external agent prompt)

A prompt to hand to an external AI agent with no repository access, so it can produce the item bank a
two-stage placement sitting draws from. It is the placement-shaped sibling of
`generate-exam-content.md`: same manifest, same vocabularies, but the counts, the band spread and the
stimulus structure are the placement blueprint's, not a mock paper's.

Read the two notes at the end before using it. The listening half has a constraint that no prompt can
remove.

---

```
You are producing PLACEMENT test content for a multi-certificate English exam practice product. You
have no access to the repository — this brief is complete and self-contained. Do not ask for code,
files, or clarification about the codebase.

Read the whole brief before writing. Then return the JSON documents it asks for, plus a short report.

================================================================================
1. WHAT PLACEMENT IS, AND WHY IT CHANGES WHAT YOU WRITE
================================================================================

A placement sitting routes a learner to a CEFR band in about 60 minutes. It is not a mock exam and it
is not a practice set. Two consequences govern every item you write:

  (a) The sitting is TWO STAGES. Stage 1 is a fixed blueprint that samples every band, so the score
      can route. Stage 2 is a boundary stage that measures the pair of bands Stage 1 routed to. An
      item is written for its band and its skill — you do not decide which stage uses it. The engine
      draws; you supply stock.

  (b) Items are PLACEMENT-ELIGIBLE, not "exam items". Every item you produce carries
      "placementEligible": true. Eligibility is a budget: placement may draw at most one third of a
      band's stock. Writing everything eligible does not help — it breaks the ratio the pool is
      designed around.

Stage 1 blueprint, per objective skill, fixed and not negotiable:

  A1: 1    A2: 1    B1: 2    B2: 2    C1: 1    C2: 1     (8 items)

The ends matter. A blueprint that starts at A2 cannot route a beginner, and one that stops at B2
cannot route an advanced candidate. Every band you are asked for below is asked for because of this
row of numbers.

================================================================================
2. THE FOUR DELIVERABLES, AND WHICH ONES YOU CAN ACTUALLY MAKE
================================================================================

  DELIVERABLE 1  Reading items            you can write all of these
  DELIVERABLE 2  Listening items          items and transcripts: yes.
                                          audio: NO. See section 8.
  DELIVERABLE 3  Speaking tasks           prompts and rubrics: yes.
  DELIVERABLE 4  Writing tasks            prompts and word budgets: yes.

The unit of delivery is one manifest per skill per band per form. Do not mix bands in one document.

================================================================================
3. THE FORMAT: ONE FORMAT, ALREADY DECIDED
================================================================================

Placement content uses the SAME ingest manifest as every other paper. There is no placement-specific
format. A manifest is:

{
  "version": "1.0",
  "exam": {
    "title":       string,      required. Name the skill, band and form.
    "purpose":     string,      required. "placement"
    "durationMinutes": integer, optional. Omit for placement: the engine owns the clock.
    "sourceName":  string,      optional. Where the content came from.
    "description": string,      optional. One sentence.
    "questionCount": integer    optional. Leave out; it is derived.
  },
  "sections": [
    {
      "sectionCode": string,    required. A section code that already exists on the certificate.
      "groups": [
        {
          "passage":       string,    required for a reading bundle; null for a standalone item.
          "transcript":    string,    required for a listening bundle.
          "audioFile":     string,    the media filename, or null. See section 8.
          "audioStartMs":  integer,   null unless audioFile is set.
          "audioEndMs":    integer,   null unless audioStartMs is set. Must exceed it.
          "imageFile":     string,    the media filename, or null.
          "imageAlt":      string,    required whenever imageFile is set.
          "cefrLevel":     string,    required. The STIMULUS level, not a child's.
          "domainContext": string,    one of: general, workplace, academic.
          "promptCopy":    string,    the learner-facing instruction, if the section needs one.
          "prepSeconds":   integer,   productive tasks only.
          "responseSeconds": integer, productive tasks only.
          "unitNumber":    integer,   default 1. The multistage unit.
          "displayOrder":  integer,   required. 1-based within the section.
          "questions": [ ... ]        required, at least one
        }
      ]
    }
  ]
}

**`sectionCode` is not free text.** It must be a section that already exists on the target certificate.
The importer refuses an unknown code by name and lists the ones that would work:

```
Unknown section 'reading' for this certificate. Known sections: P1, P2, P3, P4, P5, P6, P7, S1, ...
```

That refusal is not a formatting complaint — it is the importer telling you the paper you are writing has
no shelf to sit on. The codes below are what this product currently holds; **use one of them exactly**.
If the certificate you are asked for is not here, ask for its section rows rather than inventing a code.

| Certificate | Listening | Reading | Speaking | Writing |
|---|---|---|---|---|
| TOEIC | P1, P2, P3, P4 | P5, P6, P7 | S1, S2, S3, S4, S5 | W1, W2, W3 |
| TOEIC_CAT | P1_CAT, P2_CAT | P5_CAT, P7_CAT | — | — |
| TOEIC_SW | — | — | SW_S1, SW_S2 | SW_W1, SW_W3 |
| IELTS | REC | PAS | INT | T1, T2 |
| VSTEP | L3 | R4 | S3 | W1, W2 |

The section also decides things you must match, so read it as a constraint and not just a label:

- its **modality** fixes the skill, so a `P5` section can only hold reading items
- its **`OptionCount`** fixes how many options an `mcq` there takes (TOEIC `P2` is three)
- its **`ClusterSize`** fixes how many questions each group must hold when it is greater than one
- its **`HideOptionText`** means the options are spoken, not printed — do not write option text that the
  learner is expected to read

A question is:

{
  "stem":            string,      required. Never empty, never a placeholder.
  "cefrLevel":       string,      required. THIS item's band: A1, A2, B1, B2, C1, C2.
  "questionType":    string,      required. The WIDGET: mcq | mcq_3 | multi_select |
                                  inline_completion | dropdown | drag_drop | free_response
  "itemType":        string,      required. The SEMANTIC type. See section 5.
  "topicCode":       string,      required. One of the twenty. See section 6.
  "placementEligible": true,      required. Always true in this deliverable.
  "explanation":     string,      required. Why the key is the key.
  "hint":            string,      optional but write one for every objective item.
  "evidenceStart":   integer,     optional. Seconds for audio, characters for text.
  "evidenceEnd":     integer,     optional. Must exceed evidenceStart.
  "evidenceUnit":    string,      "sec" or "char". Required when a span is given.
  "evidenceQuote":   string,      optional. Must appear verbatim in the passage/transcript.
  "sampleAnswerOrImageDesc": string, required for a describe-a-picture task.
  "referenceKind":   string,      "image" or "sample". Says which the field above holds.
  "prepSeconds":     integer,     productive tasks.
  "responseSeconds": integer,     productive tasks.
  "minWords":        integer,     productive tasks.
  "maxWords":        integer,     productive tasks.
  "displayOrder":    integer,     required. 1-based within the group.
  "options": [                    required for objective types, absent for free_response
    { "label": "A", "text": string, "isCorrect": boolean, "rationale": string }
  ]
}

Unmapped members are REJECTED. Emit nothing outside this shape — no "id", no "difficulty", no
"answer_key", no "estimated_level". A stray field fails the whole document.

================================================================================
4. THE TWO AXES, KEPT APART
================================================================================

questionType is HOW IT IS DRAWN. itemType is WHAT IS ASKED. They are different axes and both are
required. A reading comprehension drawn as a dropdown is questionType "dropdown", itemType
"passage_comprehension".

  questionType    meaning
  ------------    ---------------------------------------------------------------
  mcq             four options, exactly one correct
  mcq_3           three options, exactly one correct
  multi_select    four options, two or more correct
  inline_completion   a gap the learner types into
  dropdown        a gap chosen from a list
  drag_drop       a gap filled by dragging
  free_response   no options at all; the learner writes or speaks

Hard pairings, checked by the database:

  - a productive item (speaking, writing) is ALWAYS free_response, and has NO options
  - an objective item is NEVER free_response
  - a listening item type never appears in a reading section, and the reverse

================================================================================
5. THE FOURTEEN SEMANTIC TYPES, BY SKILL
================================================================================

  listening   short_exchange, extended_dialogue, monologue_talk
  reading     sentence_cloze, passage_comprehension, text_insertion_cohesion
  speaking    descriptive_monologue, situational_negotiation, argumentative_monologue,
              visual_data_synthesis
  writing     transactional_correspondence, visual_data_synthesis, persuasive_essay,
              analytical_evaluation_essay

A type outside its skill's row is refused.

================================================================================
6. THE TWENTY TOPIC CODES — CLOSED SET
================================================================================

  listening   L-GIST  global gist, purpose, macro-topic
              L-DET   explicit factual detail and extraction
              L-INF   pragmatic implication and logical inference
              L-ATT   speaker stance, attitude, prosodic emotion
              L-FUNC  communicative function and speech acts
              L-ORG   discourse structure, signposting, synthesis
  reading     R-GIST  global thesis, purpose, paragraph main idea
              R-DET   local factual retrieval and paraphrase matching
              R-INF   bridging inference and logical deduction
              R-ATT   authorial stance, rhetorical purpose, tone
              R-COH   discourse cohesion, reference, text insertion
              R-GRAM  morphosyntactic structure and lexical precision
  speaking    S-DESC  informational description and personal framing
              S-INT   transactional interaction and option negotiation
              S-ARG   sustained monologic argumentation and justification
              S-SYN   integrated summary and visual/data interpretation
  writing     W-CORR  transactional correspondence and register control
              W-DATA  visual data, process and informational synthesis
              W-ARG   thesis-driven persuasive and opinion argumentation
              W-DISC  multi-perspective problem-solution and evaluation

There is no other code. An invented one is refused, not clamped. No topic may be left unused across
your whole delivery if you were asked for a full band: an empty topic cell is a defect.

================================================================================
7. THE SIX LEVELS
================================================================================

  A1, A2, B1, B2, C1, C2.    There is no pre-A1. There is no "A1+".

A level is AUTHORED, never derived. You may not compute it from word count, sentence length, a
difficulty score, or a certificate's own scale. If you cannot justify an item at its band, do not
write it — say so in your open questions.

What a band has to mean, briefly:

  A1  short notices, routine exchanges, present/past simple, high-frequency lexis
  A2  everyday transactions, simple narratives, basic conditionals, common collocations
  B1  workplace and study topics, perfect aspects, passive voice, first conditional, AWL core
  B2  argument and abstraction, complex clauses, hedging, AWL wide, implicit stance
  C1  dense prose, inversion, subjunctive, conceptual anaphora, irony and implicature
  C2  near-native compression, idiom, contested stance, domain-specific presupposition

================================================================================
8. LISTENING: WHAT YOU CAN AND CANNOT PRODUCE
================================================================================

You cannot produce audio. Audio is a recorded file that must be sourced or recorded separately, and
the manifest only names it.

So a listening delivery has two halves, and you must keep them apart:

  HALF A  You write: the transcript, the child items, the evidence spans, the explanations.
          The transcript is the SOURCE OF TRUTH for what the audio must contain. It is also what
          every item's evidence must be checked against.

  HALF B  Someone else supplies: the audio file. Until it exists, the bundle is not importable.
          In your manifest set "audioFile": null and mark the bundle BLOCKED-AUDIO in your report.

The rule that follows: write the transcript and items as if the audio will be recorded from your
transcript verbatim. Do not describe audio you have not specified. Do not write an item whose answer
depends on a tone or a stress pattern unless your transcript marks it (e.g. italics on the stressed
word) and your item quotes the phrase it turns on.

When you are given an EXISTING audio file instead — a licensed recording — you work in the other
direction: transcribe it, then write items against what is actually said. State the file, its
duration, and the timestamp of each item's evidence.

Listening bundle shape, by stimulus level. These are the envelope the content specification fixes:

  level   audio duration      words at that pace      child items per audio
  A1      20-35 s             40-65   @ 100-120 wpm     2
  A2      30-45 s             60-95   @ 115-130 wpm     2
  B1      45-75 s             110-185 @ 135-150 wpm     3
  B2      60-90 s             150-240 @ 150-165 wpm     3
  C1      90-120 s            240-340 @ 165-180 wpm     4
  C2      90-120 s            260-360 @ 175-195 wpm     3

Two hard rules inside a listening bundle:

  - child items follow the CHRONOLOGICAL order of their acoustic evidence, except a global
    L-GIST item which sits first
  - any two adjacent detail or inference items must be at least 8.0 seconds apart in the audio, so
    the candidate can process one before the cue for the next is spoken

And one prohibition: no stem may reference a numeric timestamp ("at 0:42, why…"), and no item may
require scrubbing. Audio plays linearly. If an item turns on a specific phrase, quote the phrase.

================================================================================
9. READING BUNDLE SHAPE, BY STIMULUS LEVEL
================================================================================

  level   passage length      FK grade        child items per passage
  A1      60-100 words        1.5-3.0         2  (+1 for a single bundle)
  A2      120-180 words       3.5-5.5         3
  B1      220-300 words       6.0-8.0         3
  B2      320-450 words       8.5-11.0        4  (3 for a two-bundle paper)
  C1      450-600 words       11.5-14.0       4  (3 for a two-bundle paper)
  C2      500-650 words       above 14.0      4  (3 for a one bundle)

An R-GRAM sentence-cloze item is standalone: its own 1-2 sentence passage of 15-45 words, one item
per stimulus. It does not share a passage.

Inside a shared passage:

  - child item levels may sit at Ls-1, Ls or Ls+1 for Ls of B1 and above, and must equal Ls at A1 and
    A2; at least two thirds of them are at Ls
  - no two child items may test overlapping character spans (intersection-over-union below 0.15),
    except a whole-passage R-GIST item
  - no stem may clue the answer to another item in the same bundle

================================================================================
10. PRODUCTIONAL TASKS: PLACEMENT ENVELOPES ARE NARROWER
================================================================================

A placement sitting is 60 minutes for four skills. Placement tasks are therefore scoped below the
practice envelope, and an item written for practice is NOT automatically valid for placement.

  speaking    cap the recording at 60 SECONDS at every band. Set responseSeconds: 60.
              prepSeconds: 30-45 for the first task, 45-60 for the second.
  writing     Task 1: 120-140 words. Task 2: 180-220 words.
              Set minWords and maxWords to those bounds exactly.

Do not write 250-350 word C1/C2 essays for placement even though the level would justify them: they
do not fit the clock, and the engine will refuse them.

A describe-a-picture task needs "sampleAnswerOrImageDesc" (what the picture shows, in enough detail
that an examiner could mark it) and "referenceKind": "image". The image file itself is media; if you
do not have it, describe it precisely enough that it can be produced, and mark it BLOCKED-MEDIA.

================================================================================
11. HOW MANY TO PRODUCE
================================================================================

You are producing stock for SIX NON-OVERLAPPING FORMS. Placement needs, per band per objective skill:

  7 placement-eligible items per form x 6 forms = 42 per band per skill

That is the demand. You will be told which subset to produce; produce exactly that, and say what you
produced. Do not silently produce more or fewer.

Eligibility is one third of a band's stock. If you are asked for a band's full stock, mark exactly one
third of it eligible and the rest not.

Productive skills are two tasks per sitting (one at the routed lower band, one at the higher), so a
productive band needs 6 tasks across six forms, not 42.

================================================================================
12. STIMULUS STRUCTURE: WHY BUNDLES AND PARTIAL DELIVERY BOTH MATTER
================================================================================

Stage 1 draws ONE item at a band. Stage 2 draws FIVE OR SIX at a band. A listening bundle carries two
to four children and a reading bundle three to four.

So the same bundle must serve a one-item draw and a five-item draw. That is the engine's job — it
serves k of M children — but it is your job to make it possible:

  - never write a bundle whose children cannot stand alone. Each child's stem must be
    self-contained; it may rely on the shared passage/audio, never on a sibling child's wording
  - write enough bundles per band that a five-item draw can be assembled. One three-child bundle
    cannot fill a five-item draw on its own
  - a bundle's children may span adjacent bands (Ls-1 to Ls+1), which is what lets a single audio
    serve a B1 draw and a B2 draw. Use that: it is how the stock stretches

================================================================================
13. PROHIBITIONS
================================================================================

Do not:

  - derive a level from anything. Author it or omit the item.
  - invent a topic code, an item type, a render format, or a CEFR level.
  - emit a field that is not in section 3. Unmapped members are rejected.
  - leave placeholder text in a stem ("Lorem", "TBD", "Question 1", "…"). A stem is a real question.
  - reference a timestamp in a stem, or require audio scrubbing.
  - write an item that depends on an image you have not described in imageAlt.
  - give an objective item fewer or more than four options, except mcq_3 which takes three.
  - mark a productive item with an objective render format, or the reverse.
  - reuse one passage across bands. A2 and C2 are not the same text at different lengths.
  - write two items in one bundle whose evidence overlaps.
  - produce fewer items than asked and describe it as "representative".

================================================================================
14. THE KEY
================================================================================

KeyLabel is a comma-separated ASCENDING label list.

  one correct answer      "A"
  multi_select            "A,C"     (ascending, no spaces, no duplicates)
  productive task         omit the key entirely

Exactly one option must have isCorrect true for mcq, mcq_3 and single-answer types. For multi_select,
two or more. Every option needs a rationale, including the wrong ones: a distractor without a
rationale is an unexplained trap.

Spread the key across A, B, C and D across a document. A band whose keys are all A can be beaten by
guessing, and the item statistics will show it.

================================================================================
15. SELF-CHECK BEFORE YOU ANSWER
================================================================================

Answer each as yes or no. A "no" means fix it before returning.

  1. Does every document parse as JSON with no trailing commas and no comments?
  2. Does every question carry stem, cefrLevel, questionType, itemType, topicCode,
     placementEligible, explanation and displayOrder?
  3. Is every topicCode one of the twenty, and does it match the skill?
  4. Is every itemType in its skill's row?
  5. Is every productive item free_response with no options?
  6. Does every objective item have four options, or three for mcq_3?
  7. Does every mcq / mcq_3 have exactly one isCorrect, and every multi_select at least two?
  8. Does every option have a rationale?
  9. Is every KeyLabel ascending, comma-separated, no spaces, no duplicates?
  10. Is every level authored and justified, with no derived value anywhere?
  11. Does every displayOrder start at 1 and run without gaps, within its group?
  12. Does every listening bundle's transcript fall inside its level's duration and word envelope?
  13. Are listening child items in chronological evidence order, with the gist item first?
  14. Are adjacent listening detail items at least 8 seconds apart?
  15. Does any stem reference a timestamp, or require scrubbing?
  16. Does every shared-passage bundle's child evidence avoid overlap below IoU 0.15?
  17. Is every productive task inside its placement envelope (60 s speaking, 120-140 / 180-220 words)?
  18. Does every image-bearing item have imageAlt, and every describe-a-picture task a
      sampleAnswerOrImageDesc?
  19. Does any band's key distribution concentrate on one label?
  20. Have you listed every bundle you could not finish, with the reason?

================================================================================
16. RETURN
================================================================================

  - one JSON manifest per skill per band per form, as specified
  - a report table: skill | band | form | items produced | placement-eligible count |
    bundles | blocked, and why
  - an openQuestions list: anything you could not justify — a level you were unsure of, a band whose
    stock you could not fill, a bundle waiting on audio or an image. Never invent to fill a gap.
```

---

## How to use this

1. **Decide the tier and the subset first.** Placement needs 42 eligible items per band per objective
   skill across six forms, and 6 tasks per band for each productive skill. That is more than one
   session should produce in one go. Ask for one skill × one band × one form, and run it repeatedly.
2. **Save each returned manifest** as `docs/samples/placement/<certificate>-<skill>-<band>-<form>.json`
   while it is being reviewed, then import through the Admin → Ingestion screen (or
   `POST /api/admin/imports`), which is the same door every other manifest uses.
3. **Read the refusal, do not work around it.** The importer names the field and the rule. A document
   that is refused is not "nearly right"; the pool gate and `GateOne.Review` run on the same content
   afterwards and will refuse it again.
4. **Audio is a separate job.** A listening bundle is not importable until its audio exists. Collect
   the BLOCKED-AUDIO list from the report, record or license the audio against the transcripts, then
   re-submit with `audioFile` filled in.

## What this prompt deliberately does not do

- **It does not pick the tier.** Placement demand is 42 eligible per band; the content specification
  fixes supply per tier (demo 180+180 objective items, MVR 480+480, full 900+900). Naming the tier is
  the operator's job, and D8 records that MVR and Demo cannot supply six non-overlapping forms.
- **It does not produce audio.** See section 8. This is the one hard limit: an AI agent can write the
  transcript and the items, and cannot produce a recording that matches it.
- **It does not author rubrics.** Speaking and writing are scored against `GradingCriteria` rows and
  their band descriptors, which are authored separately (D13). The prompt asks for the task and its
  word/time envelope, not the descriptors.
- **It does not decide which stage uses an item.** Stage assignment is the engine's, from the band.
- **It does not make placement serve this content yet.** As of `d763001` the two-stage draw services
  (`PlacementStageOneDrawService`, `PlacementStageTwoDrawService`) exist and are tested but are not
  registered in DI and are not called by `PlacementService`, which still runs static
  `Purpose='placement'` papers. Content marked `placementEligible` is therefore stored but not yet
  served. Tracked as #260. Producing the stock is still correct and necessary work: #260 cannot
  complete without it.
