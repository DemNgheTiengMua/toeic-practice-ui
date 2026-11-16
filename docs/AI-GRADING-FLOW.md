# AI chấm thi và AI đánh giá — luồng, gọi ở đâu, trả về đâu

Tài liệu để trả lời đúng câu chắc chắn bị hỏi: **"AI trong hệ thống của em hoạt động thế nào?"**

Có **hai** AI khác nhau, đừng lẫn:

| | **AI chấm bài** (grading) | **AI đánh giá** (insight) |
|---|---|---|
| Chấm cái gì | Một bài viết hoặc một bài nói | Toàn bộ lịch sử học của học viên |
| Trả về | Điểm + nhận xét cho bài đó | Lời khuyên chiến lược ("điểm yếu của bạn là X") |
| Interface | `IAiGrader` | `IAiInsightGenerator` |
| Bảng | `AiGradings` | `AiInsights` |
| Worker | `AiGradingBackgroundService` | `AiInsightBackgroundService` |
| Nhà cung cấp | SpeechSuper (nói) + Claude (viết) | Claude |

---

## 1. Kiến trúc một câu

> **Không có endpoint nào gọi AI trực tiếp.** Request của học viên chỉ *ghi một dòng vào hàng đợi*
> rồi trả về ngay. Một background worker poll hàng đợi mỗi **2 giây**, gọi AI, rồi ghi kết quả lại.
> Web chỉ *đọc* kết quả đó.

Đây là câu trả lời quan trọng nhất. Ba lý do:
1. **Request không bị treo.** AI mất 5–30 giây; không thể để HTTP request chờ.
2. **Thử lại được.** Dòng `failed` có `AttemptCount`; worker có thể chạy lại.
3. **Không mất tiền khi lỗi.** Học viên nộp xong là xong, không phụ thuộc mạng tới AI.

---

## 2. Luồng đầy đủ: từ lúc học viên bấm Submit đến lúc thấy điểm

```
┌─ GIAI ĐOẠN 1: REQUEST (đồng bộ, ~200ms) ────────────────────────────────────┐
│                                                                              │
│  Học viên bấm Submit                                                         │
│    │                                                                          │
│    ▼ src/web  →  POST /api/learner/placement/take/1/submit                   │
│    │                                                                          │
│    ▼ src/WebAPI/Endpoints/PlacementEndpoints.Submission.cs                   │
│    ▼ src/Services/PlacementService.SubmitPlacementTestAsync  (dòng 728)      │
│    ▼ src/Services/Attempts/AttemptEngine.Submission.cs                        │
│         • chấm phần trắc nghiệm (so đáp án) → AttemptSkillScores              │
│         • tạo FreeResponses cho câu tự luận/nói                              │
│         • INSERT AiGradings (Status = 'pending')  ← CHỈ GHI HÀNG ĐỢI         │
│         • ExamAttempts.Status = 'submitted'                                  │
│    │                                                                          │
│    ◄─ TRẢ VỀ NGAY: HTTP 200 { totalScore, skillScores, isGraded: false }      │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘

┌─ GIAI ĐOẠN 2: XỬ LÝ (bất đồng bộ, 5–30 giây) ───────────────────────────────┐
│                                                                              │
│  AiGradingBackgroundService  (src/WebAPI/AiGradingBackgroundService.cs)      │
│    loop mỗi 2 giây:                                                          │
│      ▲                                                                        │
│      │  IAiGradingProcessor.ProcessPendingGradingsAsync(batchSize: 10)       │
│      │    │                                                                   │
│      │    ▼ src/Services/AiGradingProcessor.cs  (dòng 25)                    │
│      │      SELECT TOP 10 * FROM AiGradings WHERE Status='pending'           │
│      │              ORDER BY RequestedAt                                     │
│      │      │                                                                 │
│      │      ▼ với mỗi dòng → ProcessSingleGradingAsync                       │
│      │        • LoadGradedPositionAsync  → lấy stem, min/max words, audio    │
│      │        • lấy rubric từ SectionCriteria  → List<GradingCriterionDetail> │
│      │        • dựng AiGradingRequest  (record: stem, text, audio, rubric)   │
│      │        │                                                               │
│      │        ▼ IAiGrader.GradeAsync(request)     ← ĐÂY LÀ CHỖ GỌI AI THẬT   │
│      │          │                                                             │
│      │          ├── AudioPath != null?  → SpeechSuperAiGrader  (bài NÓI)     │
│      │          └── AudioPath == null?  → ClaudeAiGrader        (bài VIẾT)   │
│      │        │                                                               │
│      │        ◄─ AiGradingResult { Success, Scores[], Transcript,            │
│      │                              PriorityFix, ModelRewrite, ErrorSpans }  │
│      │        │                                                               │
│      │        ▼ ghi kết quả                                                  │
│      │          Success=true  → AiGradings.Status='done',  OverallScaled     │
│      │                          AiGradingScores (từng tiêu chí + comment)    │
│      │                          FreeResponses.Status='graded'                │
│      │          Success=false → AiGradings.Status='failed', FailureReason     │
│      │                          (vẫn settle FreeResponse để không treo)      │
│      │        │                                                               │
│      │        ▼ còn bài nào pending?                                         │
│      │          Không → AttemptEngine.FinalizeAiGradedAttemptAsync           │
│      │                  → ExamAttempts.Status='graded'                       │
│      │                  → PlacementTests.Status='done', band, *Pct           │
│      │                                                                        │
└──────────────────────────────────────────────────────────────────────────────┘

┌─ GIAI ĐOẠN 3: HIỂN THỊ (web poll) ──────────────────────────────────────────┐
│                                                                              │
│  src/web  poll GET /api/learner/placement/result/1/status  mỗi vài giây      │
│    isGraded=false → hiện "Evaluating grammar structure..."                   │
│    isGraded=true  → tải GET .../result/1   (band + điểm từng kỹ năng)        │
│                     nút "Review every answer" bật                            │
│                     → GET .../result/1/review  (từng câu + rubric)    │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. "Call ở đâu, trả về ở đâu" — bảng chính xác

### 3a. Chỗ gọi AI (chỉ một chỗ)

| | |
|---|---|
| **File** | `src/Services/AiGradingProcessor.cs` |
| **Hàm** | `ProcessSingleGradingAsync` |
| **Dòng gọi** | `await aiGrader.GradeAsync(request, cancellationToken)` |
| **Interface** | `src/Services/Interfaces/IAiGrader.cs` → `Task<AiGradingResult> GradeAsync(AiGradingRequest, CancellationToken)` |
| **Ai cung cấp nó** | Đăng ký DI trong `src/WebAPI/Extensions/ServiceCollectionExtensions.cs` dòng ~153 |

**Quan trọng:** `AiGradingProcessor` là **chủ sở hữu duy nhất** của hàng đợi. Không service nào khác
được gọi `IAiGrader`. `WritingPracticeService` và `SpeakingPracticeService` chỉ tạo bài và nộp, không
bao giờ chờ AI.

### 3b. Hai nhà cung cấp, chọn theo dữ liệu

```csharp
// SpeechSuperAiGrader.GradeAsync — dòng 44
if (string.IsNullOrEmpty(request.AudioPath))
{
    return await _writtenGrader.GradeAsync(request, cancellationToken);  // → Claude
}
// ... ngược lại: gửi audio tới SpeechSuper
```

| Loại bài | Grader | Vì sao |
|---|---|---|
| **Viết** (không có audio) | `ClaudeAiGrader` | Claude đọc văn bản, chấm theo rubric |
| **Nói** (có audio) | `SpeechSuperAiGrader` | Cần đo phát âm + độ lưu loát từ tín hiệu âm thanh |

`SpeechSuperAiGrader` **bọc** Claude: nó gọi SpeechSuper cho phần âm, rồi chuyển cho Claude những
tiêu chí SpeechSuper không đo được (từ vựng, ngữ pháp). Đó là lý do transcript có thể do Claude sinh.

### 3c. Chọn grader thật hay giả — một biến môi trường

```csharp
// ServiceCollectionExtensions.cs dòng 153
var aiProvider = configuration["AI_PROVIDER"] ?? configuration["AiProviders:Provider"] ?? "real";
if (aiProvider.Equals("real", StringComparison.OrdinalIgnoreCase))
{
    services.AddSingleton<IAiGrader>(sp => new SpeechSuperAiGrader(...ClaudeAiGrader...));
    services.AddSingleton<IAiInsightGenerator, ClaudeAiInsightGenerator>();
}
else
{
    services.AddSingleton<IAiGrader, FakeAiGrader>();           // ← chạy test / demo offline
    services.AddSingleton<IAiInsightGenerator, FakeAiInsightGenerator>();
}
```

`AI_PROVIDER=fake` trong `.env.docker` → không tốn quota, không cần mạng, kết quả tất định.
Đây là thứ để nói khi giáo viên hỏi "demo lỗi mạng thì sao".

---

## 4. Request và response chính xác

### `AiGradingRequest` — gửi đi

```csharp
public record AiGradingRequest(
    int FreeResponseId,
    int SkillId,
    int CertId,
    string QuestionStem,                        // đề bài
    int? MinWords, int? MaxWords,               // giới hạn từ (bài viết)
    string ResponseText,                        // bài làm
    IReadOnlyList<GradingCriterionDetail> Criteria,  // rubric
    int AttemptCount = 1,
    string? AudioPath = null,                   // ← quyết định chọn grader nào
    int? DurationMs = null,
    string? ReferenceText = null,               // câu mẫu để chấm phát âm
    bool ReferenceDescribesImage = false,
    string? RequiredKeywords = null,            // checklist từ khoá bắt buộc
    string? CertName = null,
    string? CurrentBandCode = null,
    string? TargetBandCode = null,
    string? Guide = null);
```

Điểm đáng nói: **rubric là dữ liệu, không phải chuỗi trong prompt.** `Criteria` được nạp từ bảng
`SectionCriteria` join `GradingCriteria` — nên đổi thang điểm trong database là đổi cách chấm,
không phải sửa code.

### `AiGradingResult` — trả về

```csharp
public record AiGradingResult(
    bool Success,
    string? FailureReason,                      // ← lý do khi từ chối
    string? Model,
    IReadOnlyList<AiCriterionScoreResult> Scores,
    string? Transcript = null,                  // ← bài nói: lời → chữ
    IReadOnlyList<AiErrorSpan>? ErrorSpans = null,  // tối đa 5 lỗi, trích nguyên văn
    string? PriorityFix = null,                 // 1 việc nên sửa nhất
    string? ModelRewrite = null);               // viết lại đoạn yếu nhất
```

**Bốn thứ này trả lời "AI đánh giá cụ thể ra sao":**

| Trường | Hiện ở đâu | Ý nghĩa |
|---|---|---|
| `Scores[]` | Rubric breakdown | Từng tiêu chí: điểm + nhận xét |
| `Transcript` | Khối "Your response" của bài nói | Lời nói → chữ |
| `PriorityFix` | "AI feedback" | Một việc nên sửa nhất |
| `ErrorSpans[]` | (đã lưu, hiện trong báo cáo) | Tối đa 5 lỗi, trích đúng chữ học viên |

### Bảng lưu kết quả

| Bảng | Cột quan trọng |
|---|---|
| `AiGradings` | `Status` (pending/done/failed), `OverallScaled`, `FailureReason`, `AttemptCount`, `PriorityFix`, `ModelRewrite`, `ErrorSpansJson` |
| `AiGradingScores` | `CriterionId`, `Score`, `Comment` — một dòng mỗi tiêu chí |
| `FreeResponses` | `ResponseText` (bài làm / transcript), `AudioPath`, `Status` |

---

## 5. Ba nguyên tắc thiết kế — dùng để trả lời câu hỏi khó

### Nguyên tắc 1: Thiếu tiêu chí thì **thất bại**, không bịa điểm

```csharp
// ClaudeAiGrader.cs — prompt + kiểm tra
// A criterion the model leaves out is not filled in with a plausible number:
// the call is retried once, and a second omission fails the grading.
```

Nếu model trả về 3/4 tiêu chí, hệ thống **thử lại một lần**. Lần hai vẫn thiếu → `Status='failed'`
kèm lý do. Không bao giờ điền số cho đủ.

**Vì sao:** một điểm bịa trông giống hệt một điểm thật. Sai lặng lẽ tệ hơn sai ồn ào.

### Nguyên tắc 2: AI có quyền **từ chối** chấm

```csharp
return Fail("The response does not answer the task, so it cannot be scored.");
```

Khi đó:
- `AiGradings.Status = 'failed'`, `FailureReason` có lý do
- `PlacementTests.WritingPct = NULL`
- **UI phải hiện lý do**, không được im lặng bỏ qua

Đây là lỗi tôi đã gặp thật: trang kết quả âm thầm ẩn kỹ năng đi, học viên thấy thiếu một dòng mà
không biết vì sao. Đã sửa (#376, #385): giờ hiện chip **"Not graded"** + nguyên văn lý do.

### Nguyên tắc 3: Không có gì treo vĩnh viễn

Ba đường thoát cho một bài:

| Tình huống | Xử lý |
|---|---|
| Chấm thành công | `done` + điểm |
| Grader từ chối | `failed` + lý do, **vẫn settle** bài đó |
| Exception (mạng, timeout) | `failed` + message, **vẫn settle** |

Sau đó: nếu **mọi** bài tự luận đã settle → `FinalizeAiGradedAttemptAsync` → attempt `graded` →
placement `done`. Không còn trạng thái "quay mãi".

**Con số thật:** tôi đo được một ca mà bài nói không có audio (không có gì để chấm) → không có
dòng `AiGradings` nào → điều kiện "mọi bài xong" không bao giờ đúng → học viên chờ vô hạn. Đã sửa
(#384): chỉ đếm những bài **thực sự được gửi đi chấm**.

---

## 6. AI đánh giá (insight) — khác hoàn toàn

| | |
|---|---|
| **Interface** | `src/Services/Interfaces/IAiInsightGenerator.cs` |
| **Hàm** | `GenerateInsightAsync(AiInsightGenerationRequest, CancellationToken)` |
| **Service** | `src/Services/AiInsightService*.cs` (7 file partial) |
| **Worker** | `src/WebAPI/AiInsightBackgroundService.cs`, poll 2 giây |
| **Bảng** | `AiInsights` (`Status` pending/done/failed, `Narrative`, `Trend`) |

**Input không phải một bài làm, mà là *dữ liệu tổng hợp***: độ chính xác theo section/topic từ các
view `vw_UserSectionAccuracyAll`, `vw_UserTopicAccuracy` — tức weakness analysis.

Nói cách khác:
- **Grading** trả lời: *"Bài này được mấy điểm?"*
- **Insight** trả lời: *"Nhìn toàn bộ lịch sử, bạn yếu cái gì và nên làm gì?"*

**Điểm mạnh khi bảo vệ:** insight **không bịa số**. Nó đọc từ view SQL — cùng con số mà trang
Weakness Analysis hiển thị. AI chỉ viết *lời văn* quanh con số đó, không tự tính.

---

## 7. Câu hỏi giáo viên có thể hỏi — và trả lời

<details open>
<summary><strong>"Request nộp bài có chờ AI không?"</strong></summary>

**Không.** Request chỉ ghi một dòng `AiGradings` với `Status='pending'` rồi trả về ngay (~200ms).
Một `BackgroundService` riêng poll mỗi **2 giây**, chấm, ghi kết quả. Web poll trạng thái để biết khi
nào xong.

Nếu chờ trong request: AI mất 5–30 giây, proxy có thể timeout, và một lần mạng hỏng là mất bài đã nộp.
</details>

<details>
<summary><strong>"AI được gọi ở file nào?"</strong></summary>

`src/Services/AiGradingProcessor.cs`, hàm `ProcessSingleGradingAsync`, qua interface
`IAiGrader.GradeAsync`. `IAiGrader` có hai cài đặt: `SpeechSuperAiGrader` (bài nói) và
`ClaudeAiGrader` (bài viết). Chọn theo `request.AudioPath` có null hay không.

Đăng ký DI ở `src/WebAPI/Extensions/ServiceCollectionExtensions.cs` dòng 153.
</details>

<details>
<summary><strong>"Làm sao chắc AI không cho điểm bừa?"</strong></summary>

Ba lớp:
1. **Checklist trước khi chấm.** Độ dài, từ khoá bắt buộc được kiểm bằng code, không hỏi model.
2. **Rubric là dữ liệu.** Mỗi tiêu chí có `MaxScore` trong database; model chỉ trả về tỉ lệ.
3. **Thiếu tiêu chí = thất bại.** Model bỏ sót một tiêu chí → thử lại một lần → vẫn thiếu thì
   `failed` kèm lý do. Không bao giờ điền số cho đủ.
</details>

<details>
<summary><strong>"Nếu AI lỗi giữa chừng thì học viên mất bài à?"</strong></summary>

Không. Bài đã nộp nằm trong `FreeResponses`, không phụ thuộc AI. Grading `failed` có `AttemptCount`
và `FailureReason`; có endpoint retry (`POST /api/learner/placement/result/{id}/retry`) chạy lại
**miễn phí** — không ghi thêm dòng ledger nào.

Và quan trọng: lỗi AI **không làm treo** attempt. Mọi đường (thành công / từ chối / exception) đều
settle bài đó, để attempt có thể chốt.
</details>

<details>
<summary><strong>"Transcript bài nói lấy từ đâu?"</strong></summary>

Từ `AiGradingResult.Transcript`. Với bài nói, `SpeechSuperAiGrader` gửi file audio tới SpeechSuper
(`speak.eval.pro`), nhận về điểm phát âm/lưu loát và bản chuyển lời. Kết quả được ghi vào
`FreeResponses.ResponseText`, nên trang review đọc cùng một cột với bài viết.

Hệ quả cần biết: **transcript chỉ có sau khi chấm xong.** Trước đó khối "Your response" của bài nói
để trống — đó là lý do nút Review chỉ bật khi `isGraded = true`.
</details>

<details>
<summary><strong>"WebM của trình duyệt, SpeechSuper có nhận không?"</strong></summary>

Có, nhưng phải khai đúng tên. SpeechSuper từ chối `webm` với lỗi `41002 "audio type is not
supported"`. File ghi từ trình duyệt là container WebM chứa Opus — gửi với nhãn **`opus`** thì nhận,
cho cùng transcript và điểm như bản mp3. Không cần transcode.

Chi tiết này ở `SpeechSuperAiGrader` dòng ~70, và là một trong những lỗi tôi đã sửa trên `main`.
</details>

<details>
<summary><strong>"AI đánh giá khác AI chấm thế nào?"</strong></summary>

| | Chấm (grading) | Đánh giá (insight) |
|---|---|---|
| Input | Một bài làm | Toàn bộ lịch sử (từ view SQL) |
| Output | Điểm từng tiêu chí + sửa lỗi | Lời khuyên chiến lược |
| Câu hỏi nó trả lời | "Bài này mấy điểm?" | "Tôi nên học gì tiếp?" |

Cả hai đều bất đồng bộ, đều có bảng riêng, đều có worker poll 2 giây.
</details>

<details>
<summary><strong>"Demo mà mạng hỏng thì sao?"</strong></summary>

Đặt `AI_PROVIDER=fake` trong `.env.docker` rồi `docker compose up -d --force-recreate api`.
`FakeAiGrader` trả kết quả tất định, không gọi mạng, không tốn quota. Toàn bộ luồng vẫn chạy y
nguyên — chỉ khác là điểm do máy sinh thay vì model.
</details>

---

## 8. Một phút để nhớ

```
Nộp bài
  → ghi AiGradings(pending)       ← request trả về ngay
  → worker poll 2s
      → AiGradingProcessor
          → IAiGrader.GradeAsync   ← CHỖ DUY NHẤT gọi AI
              ├─ có audio → SpeechSuper
              └─ không audio → Claude
          ← AiGradingResult { Scores, Transcript, FailureReason }
      → ghi AiGradings(done|failed)
      → mọi bài xong → FinalizeAiGradedAttemptAsync → attempt graded
  → web poll status → hiện điểm
```

**Ba câu chốt khi bảo vệ:**

1. "AI **không** nằm trong request của người dùng. Nó nằm trong một worker nền, vì request không
   thể chờ 30 giây."
2. "Rubric là **dữ liệu trong database**, không phải chuỗi viết cứng trong prompt. Đổi thang điểm là
   đổi dữ liệu, không phải sửa code."
3. "AI **được quyền từ chối**, và khi từ chối thì UI phải nói lý do. Một cột điểm NULL không phải là
   không có dữ liệu — nó là một sự kiện cần giải thích."
