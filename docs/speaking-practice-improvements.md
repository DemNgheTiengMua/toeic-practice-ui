# Speaking Practice — Enterprise Improvements & Resiliency Roadmap

This document outlines architectural and UX improvements to elevate the current speaking practice implementation ([#19](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/19)) to commercial, enterprise-grade standards (comparable to *Duolingo English Test*, *ETS TOEFL/TOEIC Online*, *Pearson PTE Academic*, and *ELSA Speak*).

---

## 1. Architectural Baseline (What We Have Today)

The core architecture follows the industry-standard decoupled pipeline:
```
[Browser MediaRecorder]
       │
       ▼ (multipart/form-data upload)
[WebMVC Proxy Endpoint]
       │
       ▼ (Forward with HttpOnly Bearer Token)
[WebAPI SpeakingEndpoints]
       │
       ▼
[LocalFileStore (App_Data/audio outside web root)]
       │
       ▼ (Transaction: ExamAttempts submitted + CreditTransactions -1 debit)
[AiGradings Queue]
       │
       ▼ (Polled asynchronously)
[AiGradingBackgroundService] ──► [Azure AI Speech / FakeAiGrader / Claude]
```

### Existing Strengths
- **Decoupled Asynchrony**: Audio upload is decoupled from AI grading; HTTP timeouts or slow LLMs do not drop user requests.
- **Strict Storage Security**: Audio is stored outside the public web root and served exclusively through ownership-verified endpoints.
- **Financial Invariants**: "Block before you charge" ledger debit with database-level uniqueness (`UX_CreditTxn_Attempt_Reason`) preventing double-billing on retry.
- **Client In-Memory Buffer**: Transient upload failures keep recorded audio in browser RAM so learners can retry without re-speaking.
- **Pruning & Cleanup**: Re-recording deletes orphaned disk files immediately; unrecorded questions are pruned prior to submit.

---

## 2. Planned Improvement Items

### Phase 1: High-Impact UX & Guardrails (P1)

#### 1.1 Pre-Flight Audio & Microphone Test Wizard
- **Problem**: Microphone permissions and volume are only checked when Question 1 begins. A user with a muted mic, 0% input gain, or bad headset discovers it only after finishing their exam.
- **Specification**:
  - Add a **"Test Your Audio & Microphone"** modal/step on `Instructions.cshtml` before starting the attempt.
  - Record a 3-second audio snippet.
  - Render an animated volume meter (green decibel bars) using `AudioContext` and `AnalyserNode`.
  - Provide instant playback so the user can hear how their voice sounds before spending credits.

#### 1.2 Accidental Navigation Guard (`beforeunload`)
- **Problem**: Pressing the browser "Back" button, hitting a bookmarked link, or performing a trackpad back-swipe closes the exam without confirmation.
- **Specification**:
  - Attach a `beforeunload` event handler in `Take.cshtml` while the exam is in progress:
    ```javascript
    window.addEventListener('beforeunload', (e) => {
      if (examInProgress && !isSubmitting) {
        e.preventDefault();
        e.returnValue = ''; // Standard browser exit prompt
      }
    });
    ```
  - Suppress the prompt only when the submission form is programmatically dispatched.

---

### Phase 2: Audio & Codec Resiliency (P2)

#### 2.1 Cross-Browser Codec Fallback (iOS / Safari Support)
- **Problem**: `MediaRecorder` defaults to `audio/webm;codecs=opus` on Chrome/Firefox. Safari on iOS and older macOS versions natively favor `audio/mp4` (AAC) and can fail or produce unsupported blobs.
- **Specification**:
  - Implement progressive MIME type negotiation in `Take.cshtml`:
    ```javascript
    const preferredMimes = [
      'audio/webm;codecs=opus',
      'audio/webm',
      'audio/mp4',
      'audio/aac',
      'audio/ogg'
    ];
    const mimeType = preferredMimes.find(m => MediaRecorder.isTypeSupported(m)) || '';
    const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
    ```
  - Both `AudioValidation` and `LocalFileStore` already support `.mp4` and `.webm`.

#### 2.2 Client-Side Voice Activity Detection (VAD) / Silence Warning
- **Problem**: If a user speaks too softly or forgets to speak, silence is recorded and uploaded, consuming an AI grading credit for no purpose.
- **Specification**:
  - Compute Root Mean Square (RMS) volume during recording using the Web Audio API.
  - If average audio energy is below $-50\text{ dBFS}$ across the speaking window, display an inline prompt:
    > *"We didn't detect any audible speech. Would you like to re-record this answer before moving on?"*

---

### Phase 3: Distributed Scalability & Cloud Storage (P3)

#### 3.1 Chunked / Incremental Audio Upload for Long Responses
- **Problem**: For 60-second answers (e.g. Question 11), a network drop at second 59 loses the entire recording from the server's perspective.
- **Specification**:
  - Supply a timeslice to `mediaRecorder.start(5000)` (every 5 seconds).
  - Stream chunks to a temporary session endpoint `/speaking/upload-chunk` or buffer them in IndexedDB.
  - If a tab crash occurs at second 55, 50 seconds of audio are already persisted on the server.

#### 3.2 Horizontal Storage Provider (`S3FileStore` / `AzureBlobFileStore`)
- **Problem**: `LocalFileStore` writes to the local machine disk (`App_Data/audio`). In a multi-instance cloud deployment (Kubernetes or Azure App Service scaled to multiple instances), node A cannot read files written by node B.
- **Specification**:
  - Implement `IFileStore` using Azure Blob Storage / AWS S3 SDK.
  - Stream files using managed private identities with short-lived SAS tokens or internal API streaming proxies to maintain owner-checked isolation.

#### 3.3 Real-Time Push Completion (SignalR / SSE)
- **Problem**: `Pending.cshtml` polls the API every 2 seconds until grading finishes.
- **Specification**:
  - Replace client-side polling with a lightweight SignalR hub or Server-Sent Events (SSE) notification triggered when `AiGradingBackgroundService` completes the attempt.

---

## 3. Priority & Effort Matrix

| Initiative | Priority | Complexity | Risk | Target Benefit |
|---|---|---|---|---|
| **Accidental Navigation Guard (`beforeunload`)** | P1 | Very Low | None | Prevents accidental loss of exam progress |
| **Pre-Flight Microphone Test Wizard** | P1 | Low | Low | Prevents silent/failed exams due to mic settings |
| **Cross-Browser Codec Negotiation** | P2 | Low | Low | 100% compatibility on iOS/macOS Safari |
| **Silence / VAD Warning** | P2 | Medium | Low | Saves credit waste on muted microphones |
| **Incremental Chunked Upload** | P3 | High | Medium | Eliminates data loss on long spoken responses |
| **S3 / Azure Blob Storage Provider** | P3 | Medium | Low | Enables horizontal multi-container scaling |
