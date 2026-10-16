# Question content ingest format and validation rules

## Context

The solution requires a batch-ingestion pipeline (`ImportBatches`, `ImportLog`) for admins and content authors to import real exam content—including reading passages, listening conversations with audio clips, photographs with alt-text, multiple-choice questions with per-option explanations, and speaking/writing free responses with rubric criteria.

Prior to this decision, question data was seeded exclusively through hardcoded SQL statements in `docs/seed-demo.sql`. Issue #33 unblocks the question import slice by establishing a canonical, human- and machine-authorable package format with strict validation rules enforced prior to persistence.

## Decision

### 1. Ingest Package Format: Manifest JSON and Media ZIP

We adopt a dual-entry package model:
- **Media Ingest Package (`.zip`)**: A standard ZIP archive containing a top-level `manifest.json` file and an optional `media/` folder holding referenced audio files (`.mp3`, `.wav`) and images (`.jpg`, `.png`).
- **Text-Only Ingest (`.json`)**: When an exam or drill contains no binary media (e.g. Reading Part 5/6, or text-only writing prompts), authors can upload the standalone `.json` file directly without zipping.

### 2. Manifest Schema

The manifest mirrors the domain model (`Exams` → `Sections` → `QuestionGroups` → `Questions` → `QuestionOptions`) and uses the schema's own codes — no translation table sits between the file and the database:

```json
{
  "version": "1.0",
  "exam": {
    "title": "TOEIC ETS 2024 Practice Test 1",
    "purpose": "mock",
    "durationMinutes": 120,
    "sourceName": "ETS 2024",
    "description": null,
    "questionCount": null
  },
  "sections": [
    {
      "sectionCode": "P5",
      "groups": [
        {
          "passage": null,
          "audioFile": null, "audioStartMs": null, "audioEndMs": null,
          "imageFile": null, "imageAlt": null,
          "transcript": null,
          "prepSeconds": null, "responseSeconds": null,
          "displayOrder": 1,
          "questions": [
            {
              "stem": "Ms. Tanaka requested that all department managers submit their quarterly budgets by _____ Friday.",
              "difficultyLevel": 3,
              "questionType": "mcq",
              "categoryCode": "grammar_prep",
              "explanation": "The adjective 'next' correctly precedes a specific day of the week to denote future timing.",
              "hint": "Look for a temporal adjective that describes the upcoming day.",
              "displayOrder": 1,
              "options": [
                { "label": "A", "text": "next", "isCorrect": true, "rationale": "Correct temporal adjective modifying Friday." },
                { "label": "B", "text": "nearly", "isCorrect": false, "rationale": "Adverb meaning almost; cannot modify Friday directly." }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

- `exam.purpose` is a `CK_Exams_Purpose` code: `mock`, `placement`, `speaking_practice`, `writing_practice` (default `mock`). The exam is always created as a **draft**; the admin reviews it in the exam editor and publishes it there, where the publish checks (expected counts per part, listening audio) apply.
- `sectionCode` is a `Sections.Code` of the chosen certificate (TOEIC `P1`–`P7`, `S1`–`S5`, `W1`–`W3`; IELTS `REC`, `PAS`, `T1`, `T2`, `INT`; …). A code shared by two skills of one certificate cannot be imported by code and is refused.
- `categoryCode` is optional and must be a `QuestionCategories.Code` of the certificate.
- A **free response** question carries `prepSeconds`, `responseSeconds`, `minWords`, `maxWords` instead of `options`, and may list `criteria` by code:

```json
{
  "stem": "Describe a difficult problem you solved at your workplace.",
  "difficultyLevel": 4,
  "questionType": "free_response",
  "prepSeconds": 45,
  "responseSeconds": 60,
  "criteria": [ { "code": "PRON" }, { "code": "INT" }, { "code": "GRAM" }, { "code": "REL" } ]
}
```

  The rubric itself is the skill's `GradingCriteria`, managed by admins and used by AI grading (ADR-0004). `criteria` only asserts which ones the author wrote the question against, so each code must already exist for the section's skill; an unknown code is an error, and nothing is created.
- Unknown fields are rejected, so a typo (`"exma"`) is reported rather than silently ignored. `$schema` is accepted as an editor hint.
- Media paths are relative to the package root and must sit under `media/` (`media/part1-photo-01.wav`). Imported files are stored through `IFileStore` under `imports/{batchId}/` with generated names; the question rows hold `/media/imports/{batchId}/{file}`, which the MVC site serves to any signed-in user by forwarding to the API (ADR-0002).

### 3. Validation Rules

The whole file is validated before anything is written, and every problem is reported, not just the first. Each rule mirrors a schema constraint, so a file that passes never reaches SQL Server as a constraint error (ADR-0001).

1. **Certificate isolation**: the admin picks one certificate per upload; section, category and criterion codes are resolved only within it.
2. **Exam header**: `title` required, at most 160 characters, and not already the name of a live exam of that certificate; `durationMinutes > 0`; `purpose` a known code.
3. **MCQ**: at least 2 options, **exactly one** `isCorrect: true` (`UX_Options_OneCorrect`), unique non-empty labels of at most 4 characters (`UQ_Options_Question_Label`), text on every option; no `prepSeconds`, `responseSeconds`, `minWords`, `maxWords` or `criteria` (`CK_Questions_Timing`, `CK_Questions_Words`); only in listening or reading sections.
4. **Free response**: no options; `prepSeconds >= 0`, `responseSeconds > 0`, `minWords >= 1`, `maxWords >= minWords` (`CK_Questions_Timing`, `CK_Questions_Words`); only in speaking or writing sections; criteria codes must exist for the skill.
5. **Every question**: `stem` required, `difficultyLevel` 1–5, `hint` at most 1000 characters.
6. **Media integrity**: `audioFile` is `.mp3` or `.wav`, `imageFile` is `.jpg`, `.jpeg` or `.png`; each must exist in the package's `media/` folder and may not climb out of it. A plain `.json` upload cannot reference media. `imageAlt` is required with `imageFile` (`CK_QGroups_ImageAlt`).
7. **Audio slicing**: `audioStartMs` and `audioEndMs` come together, with `audioEndMs > audioStartMs >= 0` (`CK_QGroups_AudioSlice`).
8. **Modality**: a `speaking_practice` or `writing_practice` exam holds only sections of that skill (`trg_QGroups_SkillPracticeOneModality`).
9. **Limits**: upload up to 100 MB, manifest up to 5 MB, each media file up to 60 MB, at most 500 entries and 200 MB unpacked per package.

**Outcome and logging.** Each upload for a real certificate creates one `ImportBatches` row.
- Validation failure: one `ImportLog` row per problem (`Level = 'error'`; `RowNo` is the question's 1-based position in the file, null for file- or group-level problems, and the message names the section, group and question), batch `failed`, nothing else written.
- Success: media saved through `IFileStore`, then the exam, groups, questions and options written in one transaction, one `info` log line, batch `done`.
- If storing media or the transaction fails, the rows roll back, the files already written are deleted, and the batch is `failed` with the reason.

## Consequences

- Authors can readily generate valid packages using standard LLM prompts from PDF or textbook source materials.
- Media assets remain decoupled from the database, stored securely through `IFileStore`.
- The import engine is deterministic, self-contained, and thoroughly testable using static sample files. `docs/samples/` (`sample-reading-mcq.json`, `sample-free-response.json`, `sample-listening-package.zip` and its `sample-listening-manifest.json`) import cleanly into the seeded TOEIC certificate, and `QuestionImportServiceTests` imports them as its happy path, so the documented format and the importer cannot drift apart.
- Authors write the schema's codes (`P5`, `grammar_prep`, `mock`), so a manifest is tied to one certificate's catalog. That is the point of rule 1; it does mean an author needs the code list, which the error message for an unknown code supplies.
- One upload is one exam. Adding questions to an existing exam stays in the question editor.
