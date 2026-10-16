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

The manifest is structured hierarchically to mirror the domain model (`Exams` → `Sections` → `QuestionGroups` → `Questions` → `QuestionOptions` / `RubricCriteria`):

```json
{
  "$schema": "https://extra-efficiency.local/schemas/question-ingest.v1.json",
  "version": "1.0",
  "exam": {
    "code": "TOEIC-ETS-2024-TEST-01",
    "title": "TOEIC ETS 2024 Practice Test 1",
    "purpose": "mock_exam",
    "durationMinutes": 120,
    "totalScore": 990
  },
  "sections": [
    {
      "sectionCode": "PART_5",
      "groups": [
        {
          "passage": null,
          "audioFile": null,
          "audioStartMs": null,
          "audioEndMs": null,
          "imageFile": null,
          "imageAlt": null,
          "transcript": null,
          "displayOrder": 1,
          "questions": [
            {
              "stem": "Ms. Tanaka requested that all department managers submit their quarterly budgets by _____ Friday.",
              "difficultyLevel": 3,
              "questionType": "mcq",
              "categoryCode": "GRAMMAR",
              "explanation": "The adjective 'next' correctly precedes a specific day of the week to denote future timing.",
              "hint": "Look for a temporal adjective that describes the upcoming day.",
              "displayOrder": 1,
              "options": [
                { "label": "A", "text": "next", "isCorrect": true, "rationale": "Correct temporal adjective modifying Friday." },
                { "label": "B", "text": "nearly", "isCorrect": false, "rationale": "Adverb meaning almost; cannot modify Friday directly." },
                { "label": "C", "text": "nearness", "isCorrect": false, "rationale": "Noun meaning proximity; grammatically incorrect here." },
                { "label": "D", "text": "near", "isCorrect": false, "rationale": "Preposition or adjective typically requiring 'to' for temporal use." }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

For **free response questions** (Speaking and Writing), the question defines timing/word boundaries and a `criteria` collection instead of `options`:

```json
{
  "stem": "Describe a difficult problem you solved at your workplace.",
  "difficultyLevel": 4,
  "questionType": "free_response",
  "prepSeconds": 45,
  "responseSeconds": 60,
  "minWords": null,
  "maxWords": null,
  "explanation": "Provide a well-structured answer detailing the problem, your action, and the outcome.",
  "hint": "Use the STAR method: Situation, Task, Action, Result.",
  "criteria": [
    { "code": "PRON", "name": "Pronunciation & Intonation", "maxScore": 5.0, "weight": 1.0 },
    { "code": "FLUENCY", "name": "Fluency & Coherence", "maxScore": 5.0, "weight": 1.0 },
    { "code": "GRAMMAR", "name": "Grammatical Accuracy", "maxScore": 5.0, "weight": 1.0 },
    { "code": "VOCAB", "name": "Lexical Resource", "maxScore": 5.0, "weight": 1.0 }
  ]
}
```

### 3. Validation Rules

The ingest service enforces the following validation invariants before committing:
1. **Certificate Isolation**: Every import is scoped to a specific `CertId`. All sections (`sectionCode`) must belong to that certificate.
2. **MCQ Correctness Invariant**: Every `mcq` question must contain at least 2 options and **exactly one** option with `isCorrect: true` (enforced by `UX_Options_OneCorrect`).
3. **Option Label Uniqueness**: Option labels within a question must be unique (`A`, `B`, `C`, `D`).
4. **Media Path Integrity**:
   - Any relative path specified in `audioFile` or `imageFile` must exist within the ZIP's `media/` directory.
   - If `imageFile` is provided, `imageAlt` is mandatory (`CK_QGroups_ImageAlt`).
5. **Audio Slicing Bounds**: If `audioStartMs` or `audioEndMs` is specified, both must be present, and `audioEndMs > audioStartMs >= 0` (`CK_QGroups_AudioSlice`).
6. **Timing & Word Limits**:
   - `mcq` questions must have `prepSeconds = null` and `responseSeconds = null` (`CK_Questions_Timing`).
   - `free_response` questions may specify `prepSeconds >= 0` and `responseSeconds > 0`.
   - `minWords` and `maxWords` are valid only for `free_response`, with `maxWords >= minWords >= 1` (`CK_Questions_Words`).
7. **Modality Invariant**: For exams with purpose `speaking_practice` or `writing_practice`, all groups must belong strictly to sections of that modality (`trg_QGroups_SkillPracticeOneModality`).
8. **Atomic Transaction & Logging**:
   - Import execution is wrapped in a single database transaction.
   - Validation failures write detailed diagnostic entries to `ImportLog` (`RowNo`, `Level`, `Message`) and mark `ImportBatches.Status = 'failed'`.
   - On success, media assets are saved to the persistent file store (`IFileStore`), the database rows are committed, and `ImportBatches.Status = 'done'`.

## Consequences

- Authors can readily generate valid packages using standard LLM prompts from PDF or textbook source materials.
- Media assets remain decoupled from the database, stored securely through `IFileStore`.
- The import engine is deterministic, self-contained, and thoroughly testable using static sample files.
