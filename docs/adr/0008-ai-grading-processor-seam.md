# Dedicated AI Grading Processor Seam

## Status
Accepted

## Context
Previously, asynchronous free-response queue processing (`ProcessPendingGradingsAsync`) lived inside `WritingPracticeService`. This resulted in three major architectural friction points:
1. **Shallow & Distorted Responsibility**: `WritingPracticeService` was responsible for both user-facing writing practice attempt lifecycle and offline background worker processing for *both* writing (text) and speaking (audio) free responses. It had to know about audio file paths, transcript generation, SpeechSuper results, and database triggers (`trg_Attempts_NoBandForSpeakingPractice`).
2. **False Coupling Across Seams**: `SpeakingPracticeService` had to inject `IWritingPracticeService` solely to forward calls to `ProcessPendingGradingsAsync`. This forced unit and integration tests (such as `SpeakingPracticeServiceTests` and `MentorServiceTests`) to construct dummy `WritingPracticeService` instances and mock unrelated dependencies just to instantiate `SpeakingPracticeService`.
3. **Queue Worker Dependency Leakage**: The WebAPI background worker `AiGradingBackgroundService` injected `IWritingPracticeService` to process the entire queue—including speaking evaluations—obscuring the worker's true purpose.

## Decision
Extract a dedicated, deep AI grading module in `src/Services/AiGradingProcessor.cs`:
- Interface `IAiGradingProcessor`:
  - `Task<int> ProcessPendingGradingsAsync(int batchSize = 10, CancellationToken cancellationToken = default);`
  - `Task<bool> GradeFreeResponseAsync(int freeResponseId, CancellationToken cancellationToken = default);`
- Implementation `AiGradingProcessor`:
  - Injects `CompanionDbContext`, `IAiGrader`, and optional `ILogger<AiGradingProcessor>`.
  - Encapsulates queue polling over `IX_AiGradings_Queue` (`AiGradings` status `pending`).
  - Resolves applicable grading criteria (section overrides via `SectionCriteria` with fallback to skill-wide `GradingCriteria`).
  - Clamps criterion scores strictly to `[0, MaxScore]`.
  - Dispatches requests to `IAiGrader` for both text (writing) and audio (speaking).
  - Handles audio transcript back-fill into `FreeResponses.ResponseText`.
  - Populates `AiGradingScores` and marks `AiGradings` status as `done` (or `failed` with failure reason).
  - Finalizes `ExamAttempt` records to `graded` once all free responses are scored, ensuring `TotalScore` and `BandCode` remain `NULL` for practice attempts to satisfy SQL triggers `50032` and `50041`.

## Consequences
- **Decoupled Practice Services**: `SpeakingPracticeService` no longer depends on `IWritingPracticeService`. Both services focus strictly on learner attempt flows (starting attempts, saving drafts/audio, submitting, and rendering reviews).
- **Cleaner Dependencies**: `WritingPracticeService` no longer injects `IAiGrader`, and `SpeakingPracticeService` only requires `CompanionDbContext`, `ILearnerService`, and `IFileStore`.
- **Worker Locality**: `AiGradingBackgroundService` in `WebAPI` resolves `IAiGradingProcessor` directly.
- **Isolated Testing**: Test fixtures (`SpeakingPracticeServiceTests`, `MentorServiceTests`, `WritingPracticeServiceTests`) no longer instantiate cross-service dummies. A dedicated test suite (`AiGradingProcessorTests`) verifies clamping, failure handling, transcript population, and queue draining.
