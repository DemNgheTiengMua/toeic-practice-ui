# AI cháº¥m thi vÃ  AI Ä‘Ã¡nh giÃ¡ â€” luá»“ng, gá»i á»Ÿ Ä‘Ã¢u, tráº£ vá» Ä‘Ã¢u

TÃ i liá»‡u Ä‘á»ƒ tráº£ lá»i Ä‘Ãºng cÃ¢u cháº¯c cháº¯n bá»‹ há»i: **"AI trong há»‡ thá»‘ng cá»§a em hoáº¡t Ä‘á»™ng tháº¿ nÃ o?"**

CÃ³ **hai** AI khÃ¡c nhau, Ä‘á»«ng láº«n:

| | **AI cháº¥m bÃ i** (grading) | **AI Ä‘Ã¡nh giÃ¡** (insight) |
|---|---|---|
| Cháº¥m cÃ¡i gÃ¬ | Má»™t bÃ i viáº¿t hoáº·c má»™t bÃ i nÃ³i | ToÃ n bá»™ lá»‹ch sá»­ há»c cá»§a há»c viÃªn |
| Tráº£ vá» | Äiá»ƒm + nháº­n xÃ©t cho bÃ i Ä‘Ã³ | Lá»i khuyÃªn chiáº¿n lÆ°á»£c ("Ä‘iá»ƒm yáº¿u cá»§a báº¡n lÃ  X") |
| Interface | `IAiGrader` | `IAiInsightGenerator` |
| Báº£ng | `AiGradings` | `AiInsights` |
| Worker | `AiGradingBackgroundService` | `AiInsightBackgroundService` |
| NhÃ  cung cáº¥p | SpeechSuper (nÃ³i) + Claude (viáº¿t) | Claude |

---

## 1. Kiáº¿n trÃºc má»™t cÃ¢u

> **KhÃ´ng cÃ³ endpoint nÃ o gá»i AI trá»±c tiáº¿p.** Request cá»§a há»c viÃªn chá»‰ *ghi má»™t dÃ²ng vÃ o hÃ ng Ä‘á»£i*
> rá»“i tráº£ vá» ngay. Má»™t background worker poll hÃ ng Ä‘á»£i má»—i **2 giÃ¢y**, gá»i AI, rá»“i ghi káº¿t quáº£ láº¡i.
> Web chá»‰ *Ä‘á»c* káº¿t quáº£ Ä‘Ã³.

ÄÃ¢y lÃ  cÃ¢u tráº£ lá»i quan trá»ng nháº¥t. Ba lÃ½ do:
1. **Request khÃ´ng bá»‹ treo.** AI máº¥t 5â€“30 giÃ¢y; khÃ´ng thá»ƒ Ä‘á»ƒ HTTP request chá».
2. **Thá»­ láº¡i Ä‘Æ°á»£c.** DÃ²ng `failed` cÃ³ `AttemptCount`; worker cÃ³ thá»ƒ cháº¡y láº¡i.
3. **KhÃ´ng máº¥t tiá»n khi lá»—i.** Há»c viÃªn ná»™p xong lÃ  xong, khÃ´ng phá»¥ thuá»™c máº¡ng tá»›i AI.

---

## 2. Luá»“ng Ä‘áº§y Ä‘á»§: tá»« lÃºc há»c viÃªn báº¥m Submit Ä‘áº¿n lÃºc tháº¥y Ä‘iá»ƒm

```
â”Œâ”€ GIAI ÄOáº N 1: REQUEST (Ä‘á»“ng bá»™, ~200ms) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                                                                              â”‚
â”‚  Há»c viÃªn báº¥m Submit                                                         â”‚
â”‚    â”‚                                                                          â”‚
â”‚    â–¼ src/web  â†’  POST /api/learner/placement/take/1/submit                   â”‚
â”‚    â”‚                                                                          â”‚
â”‚    â–¼ src/WebAPI/Endpoints/PlacementEndpoints.Submission.cs                   â”‚
â”‚    â–¼ src/Services/PlacementService.SubmitPlacementTestAsync  (dÃ²ng 728)      â”‚
â”‚    â–¼ src/Services/Attempts/AttemptEngine.Submission.cs                        â”‚
â”‚         â€¢ cháº¥m pháº§n tráº¯c nghiá»‡m (so Ä‘Ã¡p Ã¡n) â†’ AttemptSkillScores              â”‚
â”‚         â€¢ táº¡o FreeResponses cho cÃ¢u tá»± luáº­n/nÃ³i                              â”‚
â”‚         â€¢ INSERT AiGradings (Status = 'pending')  â† CHá»ˆ GHI HÃ€NG Äá»¢I         â”‚
â”‚         â€¢ ExamAttempts.Status = 'submitted'                                  â”‚
â”‚    â”‚                                                                          â”‚
â”‚    â—„â”€ TRáº¢ Vá»€ NGAY: HTTP 200 { totalScore, skillScores, isGraded: false }      â”‚
â”‚                                                                              â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜

â”Œâ”€ GIAI ÄOáº N 2: Xá»¬ LÃ (báº¥t Ä‘á»“ng bá»™, 5â€“30 giÃ¢y) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                                                                              â”‚
â”‚  AiGradingBackgroundService  (src/WebAPI/AiGradingBackgroundService.cs)      â”‚
â”‚    loop má»—i 2 giÃ¢y:                                                          â”‚
â”‚      â–²                                                                        â”‚
â”‚      â”‚  IAiGradingProcessor.ProcessPendingGradingsAsync(batchSize: 10)       â”‚
â”‚      â”‚    â”‚                                                                   â”‚
â”‚      â”‚    â–¼ src/Services/AiGradingProcessor.cs  (dÃ²ng 25)                    â”‚
â”‚      â”‚      SELECT TOP 10 * FROM AiGradings WHERE Status='pending'           â”‚
â”‚      â”‚              ORDER BY RequestedAt                                     â”‚
â”‚      â”‚      â”‚                                                                 â”‚
â”‚      â”‚      â–¼ vá»›i má»—i dÃ²ng â†’ ProcessSingleGradingAsync                       â”‚
â”‚      â”‚        â€¢ LoadGradedPositionAsync  â†’ láº¥y stem, min/max words, audio    â”‚
â”‚      â”‚        â€¢ láº¥y rubric tá»« SectionCriteria  â†’ List<GradingCriterionDetail> â”‚
â”‚      â”‚        â€¢ dá»±ng AiGradingRequest  (record: stem, text, audio, rubric)   â”‚
â”‚      â”‚        â”‚                                                               â”‚
â”‚      â”‚        â–¼ IAiGrader.GradeAsync(request)     â† ÄÃ‚Y LÃ€ CHá»– Gá»ŒI AI THáº¬T   â”‚
â”‚      â”‚          â”‚                                                             â”‚
â”‚      â”‚          â”œâ”€â”€ AudioPath != null?  â†’ SpeechSuperAiGrader  (bÃ i NÃ“I)     â”‚
â”‚      â”‚          â””â”€â”€ AudioPath == null?  â†’ ClaudeAiGrader        (bÃ i VIáº¾T)   â”‚
â”‚      â”‚        â”‚                                                               â”‚
â”‚      â”‚        â—„â”€ AiGradingResult { Success, Scores[], Transcript,            â”‚
â”‚      â”‚                              PriorityFix, ModelRewrite, ErrorSpans }  â”‚
â”‚      â”‚        â”‚                                                               â”‚
â”‚      â”‚        â–¼ ghi káº¿t quáº£                                                  â”‚
â”‚      â”‚          Success=true  â†’ AiGradings.Status='done',  OverallScaled     â”‚
â”‚      â”‚                          AiGradingScores (tá»«ng tiÃªu chÃ­ + comment)    â”‚
â”‚      â”‚                          FreeResponses.Status='graded'                â”‚
â”‚      â”‚          Success=false â†’ AiGradings.Status='failed', FailureReason     â”‚
â”‚      â”‚                          (váº«n settle FreeResponse Ä‘á»ƒ khÃ´ng treo)      â”‚
â”‚      â”‚        â”‚                                                               â”‚
â”‚      â”‚        â–¼ cÃ²n bÃ i nÃ o pending?                                         â”‚
â”‚      â”‚          KhÃ´ng â†’ AttemptEngine.FinalizeAiGradedAttemptAsync           â”‚
â”‚      â”‚                  â†’ ExamAttempts.Status='graded'                       â”‚
â”‚      â”‚                  â†’ PlacementTests.Status='done', band, *Pct           â”‚
â”‚      â”‚                                                                        â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜

â”Œâ”€ GIAI ÄOáº N 3: HIá»‚N THá»Š (web poll) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                                                                              â”‚
â”‚  src/web  poll GET /api/learner/placement/result/1/status  má»—i vÃ i giÃ¢y      â”‚
â”‚    isGraded=false â†’ hiá»‡n "Evaluating grammar structure..."                   â”‚
â”‚    isGraded=true  â†’ táº£i GET .../result/1   (band + Ä‘iá»ƒm tá»«ng ká»¹ nÄƒng)        â”‚
â”‚                     nÃºt "Review every answer" báº­t                            â”‚
│                     → GET .../result/1/review  (từng câu + rubric)    │
â”‚                                                                              â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

---

## 3. "Call á»Ÿ Ä‘Ã¢u, tráº£ vá» á»Ÿ Ä‘Ã¢u" â€” báº£ng chÃ­nh xÃ¡c

### 3a. Chá»— gá»i AI (chá»‰ má»™t chá»—)

| | |
|---|---|
| **File** | `src/Services/AiGradingProcessor.cs` |
| **HÃ m** | `ProcessSingleGradingAsync` |
| **DÃ²ng gá»i** | `await aiGrader.GradeAsync(request, cancellationToken)` |
| **Interface** | `src/Services/Interfaces/IAiGrader.cs` â†’ `Task<AiGradingResult> GradeAsync(AiGradingRequest, CancellationToken)` |
| **Ai cung cáº¥p nÃ³** | ÄÄƒng kÃ½ DI trong `src/WebAPI/Extensions/ServiceCollectionExtensions.cs` dÃ²ng ~153 |

**Quan trá»ng:** `AiGradingProcessor` lÃ  **chá»§ sá»Ÿ há»¯u duy nháº¥t** cá»§a hÃ ng Ä‘á»£i. KhÃ´ng service nÃ o khÃ¡c
Ä‘Æ°á»£c gá»i `IAiGrader`. `WritingPracticeService` vÃ  `SpeakingPracticeService` chá»‰ táº¡o bÃ i vÃ  ná»™p, khÃ´ng
bao giá» chá» AI.

### 3b. Hai nhÃ  cung cáº¥p, chá»n theo dá»¯ liá»‡u

```csharp
// SpeechSuperAiGrader.GradeAsync â€” dÃ²ng 44
if (string.IsNullOrEmpty(request.AudioPath))
{
    return await _writtenGrader.GradeAsync(request, cancellationToken);  // â†’ Claude
}
// ... ngÆ°á»£c láº¡i: gá»­i audio tá»›i SpeechSuper
```

| Loáº¡i bÃ i | Grader | VÃ¬ sao |
|---|---|---|
| **Viáº¿t** (khÃ´ng cÃ³ audio) | `ClaudeAiGrader` | Claude Ä‘á»c vÄƒn báº£n, cháº¥m theo rubric |
| **NÃ³i** (cÃ³ audio) | `SpeechSuperAiGrader` | Cáº§n Ä‘o phÃ¡t Ã¢m + Ä‘á»™ lÆ°u loÃ¡t tá»« tÃ­n hiá»‡u Ã¢m thanh |

`SpeechSuperAiGrader` **bá»c** Claude: nÃ³ gá»i SpeechSuper cho pháº§n Ã¢m, rá»“i chuyá»ƒn cho Claude nhá»¯ng
tiÃªu chÃ­ SpeechSuper khÃ´ng Ä‘o Ä‘Æ°á»£c (tá»« vá»±ng, ngá»¯ phÃ¡p). ÄÃ³ lÃ  lÃ½ do transcript cÃ³ thá»ƒ do Claude sinh.

### 3c. Chá»n grader tháº­t hay giáº£ â€” má»™t biáº¿n mÃ´i trÆ°á»ng

```csharp
// ServiceCollectionExtensions.cs dÃ²ng 153
var aiProvider = configuration["AI_PROVIDER"] ?? configuration["AiProviders:Provider"] ?? "real";
if (aiProvider.Equals("real", StringComparison.OrdinalIgnoreCase))
{
    services.AddSingleton<IAiGrader>(sp => new SpeechSuperAiGrader(...ClaudeAiGrader...));
    services.AddSingleton<IAiInsightGenerator, ClaudeAiInsightGenerator>();
}
else
{
    services.AddSingleton<IAiGrader, FakeAiGrader>();           // â† cháº¡y test / demo offline
    services.AddSingleton<IAiInsightGenerator, FakeAiInsightGenerator>();
}
```

`AI_PROVIDER=fake` trong `.env.docker` â†’ khÃ´ng tá»‘n quota, khÃ´ng cáº§n máº¡ng, káº¿t quáº£ táº¥t Ä‘á»‹nh.
ÄÃ¢y lÃ  thá»© Ä‘á»ƒ nÃ³i khi giÃ¡o viÃªn há»i "demo lá»—i máº¡ng thÃ¬ sao".

---

## 4. Request vÃ  response chÃ­nh xÃ¡c

### `AiGradingRequest` â€” gá»­i Ä‘i

```csharp
public record AiGradingRequest(
    int FreeResponseId,
    int SkillId,
    int CertId,
    string QuestionStem,                        // Ä‘á» bÃ i
    int? MinWords, int? MaxWords,               // giá»›i háº¡n tá»« (bÃ i viáº¿t)
    string ResponseText,                        // bÃ i lÃ m
    IReadOnlyList<GradingCriterionDetail> Criteria,  // rubric
    int AttemptCount = 1,
    string? AudioPath = null,                   // â† quyáº¿t Ä‘á»‹nh chá»n grader nÃ o
    int? DurationMs = null,
    string? ReferenceText = null,               // cÃ¢u máº«u Ä‘á»ƒ cháº¥m phÃ¡t Ã¢m
    bool ReferenceDescribesImage = false,
    string? RequiredKeywords = null,            // checklist tá»« khoÃ¡ báº¯t buá»™c
    string? CertName = null,
    string? CurrentBandCode = null,
    string? TargetBandCode = null,
    string? Guide = null);
```

Äiá»ƒm Ä‘Ã¡ng nÃ³i: **rubric lÃ  dá»¯ liá»‡u, khÃ´ng pháº£i chuá»—i trong prompt.** `Criteria` Ä‘Æ°á»£c náº¡p tá»« báº£ng
`SectionCriteria` join `GradingCriteria` â€” nÃªn Ä‘á»•i thang Ä‘iá»ƒm trong database lÃ  Ä‘á»•i cÃ¡ch cháº¥m,
khÃ´ng pháº£i sá»­a code.

### `AiGradingResult` â€” tráº£ vá»

```csharp
public record AiGradingResult(
    bool Success,
    string? FailureReason,                      // â† lÃ½ do khi tá»« chá»‘i
    string? Model,
    IReadOnlyList<AiCriterionScoreResult> Scores,
    string? Transcript = null,                  // â† bÃ i nÃ³i: lá»i â†’ chá»¯
    IReadOnlyList<AiErrorSpan>? ErrorSpans = null,  // tá»‘i Ä‘a 5 lá»—i, trÃ­ch nguyÃªn vÄƒn
    string? PriorityFix = null,                 // 1 viá»‡c nÃªn sá»­a nháº¥t
    string? ModelRewrite = null);               // viáº¿t láº¡i Ä‘oáº¡n yáº¿u nháº¥t
```

**Bá»‘n thá»© nÃ y tráº£ lá»i "AI Ä‘Ã¡nh giÃ¡ cá»¥ thá»ƒ ra sao":**

| TrÆ°á»ng | Hiá»‡n á»Ÿ Ä‘Ã¢u | Ã nghÄ©a |
|---|---|---|
| `Scores[]` | Rubric breakdown | Tá»«ng tiÃªu chÃ­: Ä‘iá»ƒm + nháº­n xÃ©t |
| `Transcript` | Khá»‘i "Your response" cá»§a bÃ i nÃ³i | Lá»i nÃ³i â†’ chá»¯ |
| `PriorityFix` | "AI feedback" | Má»™t viá»‡c nÃªn sá»­a nháº¥t |
| `ErrorSpans[]` | (Ä‘Ã£ lÆ°u, hiá»‡n trong bÃ¡o cÃ¡o) | Tá»‘i Ä‘a 5 lá»—i, trÃ­ch Ä‘Ãºng chá»¯ há»c viÃªn |

### Báº£ng lÆ°u káº¿t quáº£

| Báº£ng | Cá»™t quan trá»ng |
|---|---|
| `AiGradings` | `Status` (pending/done/failed), `OverallScaled`, `FailureReason`, `AttemptCount`, `PriorityFix`, `ModelRewrite`, `ErrorSpansJson` |
| `AiGradingScores` | `CriterionId`, `Score`, `Comment` â€” má»™t dÃ²ng má»—i tiÃªu chÃ­ |
| `FreeResponses` | `ResponseText` (bÃ i lÃ m / transcript), `AudioPath`, `Status` |

---

## 5. Ba nguyÃªn táº¯c thiáº¿t káº¿ â€” dÃ¹ng Ä‘á»ƒ tráº£ lá»i cÃ¢u há»i khÃ³

### NguyÃªn táº¯c 1: Thiáº¿u tiÃªu chÃ­ thÃ¬ **tháº¥t báº¡i**, khÃ´ng bá»‹a Ä‘iá»ƒm

```csharp
// ClaudeAiGrader.cs â€” prompt + kiá»ƒm tra
// A criterion the model leaves out is not filled in with a plausible number:
// the call is retried once, and a second omission fails the grading.
```

Náº¿u model tráº£ vá» 3/4 tiÃªu chÃ­, há»‡ thá»‘ng **thá»­ láº¡i má»™t láº§n**. Láº§n hai váº«n thiáº¿u â†’ `Status='failed'`
kÃ¨m lÃ½ do. KhÃ´ng bao giá» Ä‘iá»n sá»‘ cho Ä‘á»§.

**VÃ¬ sao:** má»™t Ä‘iá»ƒm bá»‹a trÃ´ng giá»‘ng há»‡t má»™t Ä‘iá»ƒm tháº­t. Sai láº·ng láº½ tá»‡ hÆ¡n sai á»“n Ã o.

### NguyÃªn táº¯c 2: AI cÃ³ quyá»n **tá»« chá»‘i** cháº¥m

```csharp
return Fail("The response does not answer the task, so it cannot be scored.");
```

Khi Ä‘Ã³:
- `AiGradings.Status = 'failed'`, `FailureReason` cÃ³ lÃ½ do
- `PlacementTests.WritingPct = NULL`
- **UI pháº£i hiá»‡n lÃ½ do**, khÃ´ng Ä‘Æ°á»£c im láº·ng bá» qua

ÄÃ¢y lÃ  lá»—i tÃ´i Ä‘Ã£ gáº·p tháº­t: trang káº¿t quáº£ Ã¢m tháº§m áº©n ká»¹ nÄƒng Ä‘i, há»c viÃªn tháº¥y thiáº¿u má»™t dÃ²ng mÃ 
khÃ´ng biáº¿t vÃ¬ sao. ÄÃ£ sá»­a (#376, #385): giá» hiá»‡n chip **"Not graded"** + nguyÃªn vÄƒn lÃ½ do.

### NguyÃªn táº¯c 3: KhÃ´ng cÃ³ gÃ¬ treo vÄ©nh viá»…n

Ba Ä‘Æ°á»ng thoÃ¡t cho má»™t bÃ i:

| TÃ¬nh huá»‘ng | Xá»­ lÃ½ |
|---|---|
| Cháº¥m thÃ nh cÃ´ng | `done` + Ä‘iá»ƒm |
| Grader tá»« chá»‘i | `failed` + lÃ½ do, **váº«n settle** bÃ i Ä‘Ã³ |
| Exception (máº¡ng, timeout) | `failed` + message, **váº«n settle** |

Sau Ä‘Ã³: náº¿u **má»i** bÃ i tá»± luáº­n Ä‘Ã£ settle â†’ `FinalizeAiGradedAttemptAsync` â†’ attempt `graded` â†’
placement `done`. KhÃ´ng cÃ²n tráº¡ng thÃ¡i "quay mÃ£i".

**Con sá»‘ tháº­t:** tÃ´i Ä‘o Ä‘Æ°á»£c má»™t ca mÃ  bÃ i nÃ³i khÃ´ng cÃ³ audio (khÃ´ng cÃ³ gÃ¬ Ä‘á»ƒ cháº¥m) â†’ khÃ´ng cÃ³
dÃ²ng `AiGradings` nÃ o â†’ Ä‘iá»u kiá»‡n "má»i bÃ i xong" khÃ´ng bao giá» Ä‘Ãºng â†’ há»c viÃªn chá» vÃ´ háº¡n. ÄÃ£ sá»­a
(#384): chá»‰ Ä‘áº¿m nhá»¯ng bÃ i **thá»±c sá»± Ä‘Æ°á»£c gá»­i Ä‘i cháº¥m**.

---

## 6. AI Ä‘Ã¡nh giÃ¡ (insight) â€” khÃ¡c hoÃ n toÃ n

| | |
|---|---|
| **Interface** | `src/Services/Interfaces/IAiInsightGenerator.cs` |
| **HÃ m** | `GenerateInsightAsync(AiInsightGenerationRequest, CancellationToken)` |
| **Service** | `src/Services/AiInsightService*.cs` (7 file partial) |
| **Worker** | `src/WebAPI/AiInsightBackgroundService.cs`, poll 2 giÃ¢y |
| **Báº£ng** | `AiInsights` (`Status` pending/done/failed, `Narrative`, `Trend`) |

**Input khÃ´ng pháº£i má»™t bÃ i lÃ m, mÃ  lÃ  *dá»¯ liá»‡u tá»•ng há»£p***: Ä‘á»™ chÃ­nh xÃ¡c theo section/topic tá»« cÃ¡c
view `vw_UserSectionAccuracyAll`, `vw_UserTopicAccuracy` â€” tá»©c weakness analysis.

NÃ³i cÃ¡ch khÃ¡c:
- **Grading** tráº£ lá»i: *"BÃ i nÃ y Ä‘Æ°á»£c máº¥y Ä‘iá»ƒm?"*
- **Insight** tráº£ lá»i: *"NhÃ¬n toÃ n bá»™ lá»‹ch sá»­, báº¡n yáº¿u cÃ¡i gÃ¬ vÃ  nÃªn lÃ m gÃ¬?"*

**Äiá»ƒm máº¡nh khi báº£o vá»‡:** insight **khÃ´ng bá»‹a sá»‘**. NÃ³ Ä‘á»c tá»« view SQL â€” cÃ¹ng con sá»‘ mÃ  trang
Weakness Analysis hiá»ƒn thá»‹. AI chá»‰ viáº¿t *lá»i vÄƒn* quanh con sá»‘ Ä‘Ã³, khÃ´ng tá»± tÃ­nh.

---

## 7. CÃ¢u há»i giÃ¡o viÃªn cÃ³ thá»ƒ há»i â€” vÃ  tráº£ lá»i

<details open>
<summary><strong>"Request ná»™p bÃ i cÃ³ chá» AI khÃ´ng?"</strong></summary>

**KhÃ´ng.** Request chá»‰ ghi má»™t dÃ²ng `AiGradings` vá»›i `Status='pending'` rá»“i tráº£ vá» ngay (~200ms).
Má»™t `BackgroundService` riÃªng poll má»—i **2 giÃ¢y**, cháº¥m, ghi káº¿t quáº£. Web poll tráº¡ng thÃ¡i Ä‘á»ƒ biáº¿t khi
nÃ o xong.

Náº¿u chá» trong request: AI máº¥t 5â€“30 giÃ¢y, proxy cÃ³ thá»ƒ timeout, vÃ  má»™t láº§n máº¡ng há»ng lÃ  máº¥t bÃ i Ä‘Ã£ ná»™p.
</details>

<details>
<summary><strong>"AI Ä‘Æ°á»£c gá»i á»Ÿ file nÃ o?"</strong></summary>

`src/Services/AiGradingProcessor.cs`, hÃ m `ProcessSingleGradingAsync`, qua interface
`IAiGrader.GradeAsync`. `IAiGrader` cÃ³ hai cÃ i Ä‘áº·t: `SpeechSuperAiGrader` (bÃ i nÃ³i) vÃ 
`ClaudeAiGrader` (bÃ i viáº¿t). Chá»n theo `request.AudioPath` cÃ³ null hay khÃ´ng.

ÄÄƒng kÃ½ DI á»Ÿ `src/WebAPI/Extensions/ServiceCollectionExtensions.cs` dÃ²ng 153.
</details>

<details>
<summary><strong>"LÃ m sao cháº¯c AI khÃ´ng cho Ä‘iá»ƒm bá»«a?"</strong></summary>

Ba lá»›p:
1. **Checklist trÆ°á»›c khi cháº¥m.** Äá»™ dÃ i, tá»« khoÃ¡ báº¯t buá»™c Ä‘Æ°á»£c kiá»ƒm báº±ng code, khÃ´ng há»i model.
2. **Rubric lÃ  dá»¯ liá»‡u.** Má»—i tiÃªu chÃ­ cÃ³ `MaxScore` trong database; model chá»‰ tráº£ vá» tá»‰ lá»‡.
3. **Thiáº¿u tiÃªu chÃ­ = tháº¥t báº¡i.** Model bá» sÃ³t má»™t tiÃªu chÃ­ â†’ thá»­ láº¡i má»™t láº§n â†’ váº«n thiáº¿u thÃ¬
   `failed` kÃ¨m lÃ½ do. KhÃ´ng bao giá» Ä‘iá»n sá»‘ cho Ä‘á»§.
</details>

<details>
<summary><strong>"Náº¿u AI lá»—i giá»¯a chá»«ng thÃ¬ há»c viÃªn máº¥t bÃ i Ã ?"</strong></summary>

KhÃ´ng. BÃ i Ä‘Ã£ ná»™p náº±m trong `FreeResponses`, khÃ´ng phá»¥ thuá»™c AI. Grading `failed` cÃ³ `AttemptCount`
vÃ  `FailureReason`; cÃ³ endpoint retry (`POST /api/learner/placement/result/{id}/retry`) cháº¡y láº¡i
**miá»…n phÃ­** â€” khÃ´ng ghi thÃªm dÃ²ng ledger nÃ o.

VÃ  quan trá»ng: lá»—i AI **khÃ´ng lÃ m treo** attempt. Má»i Ä‘Æ°á»ng (thÃ nh cÃ´ng / tá»« chá»‘i / exception) Ä‘á»u
settle bÃ i Ä‘Ã³, Ä‘á»ƒ attempt cÃ³ thá»ƒ chá»‘t.
</details>

<details>
<summary><strong>"Transcript bÃ i nÃ³i láº¥y tá»« Ä‘Ã¢u?"</strong></summary>

Tá»« `AiGradingResult.Transcript`. Vá»›i bÃ i nÃ³i, `SpeechSuperAiGrader` gá»­i file audio tá»›i SpeechSuper
(`speak.eval.pro`), nháº­n vá» Ä‘iá»ƒm phÃ¡t Ã¢m/lÆ°u loÃ¡t vÃ  báº£n chuyá»ƒn lá»i. Káº¿t quáº£ Ä‘Æ°á»£c ghi vÃ o
`FreeResponses.ResponseText`, nÃªn trang review Ä‘á»c cÃ¹ng má»™t cá»™t vá»›i bÃ i viáº¿t.

Há»‡ quáº£ cáº§n biáº¿t: **transcript chá»‰ cÃ³ sau khi cháº¥m xong.** TrÆ°á»›c Ä‘Ã³ khá»‘i "Your response" cá»§a bÃ i nÃ³i
Ä‘á»ƒ trá»‘ng â€” Ä‘Ã³ lÃ  lÃ½ do nÃºt Review chá»‰ báº­t khi `isGraded = true`.
</details>

<details>
<summary><strong>"WebM cá»§a trÃ¬nh duyá»‡t, SpeechSuper cÃ³ nháº­n khÃ´ng?"</strong></summary>

CÃ³, nhÆ°ng pháº£i khai Ä‘Ãºng tÃªn. SpeechSuper tá»« chá»‘i `webm` vá»›i lá»—i `41002 "audio type is not
supported"`. File ghi tá»« trÃ¬nh duyá»‡t lÃ  container WebM chá»©a Opus â€” gá»­i vá»›i nhÃ£n **`opus`** thÃ¬ nháº­n,
cho cÃ¹ng transcript vÃ  Ä‘iá»ƒm nhÆ° báº£n mp3. KhÃ´ng cáº§n transcode.

Chi tiáº¿t nÃ y á»Ÿ `SpeechSuperAiGrader` dÃ²ng ~70, vÃ  lÃ  má»™t trong nhá»¯ng lá»—i tÃ´i Ä‘Ã£ sá»­a trÃªn `main`.
</details>

<details>
<summary><strong>"AI Ä‘Ã¡nh giÃ¡ khÃ¡c AI cháº¥m tháº¿ nÃ o?"</strong></summary>

| | Cháº¥m (grading) | ÄÃ¡nh giÃ¡ (insight) |
|---|---|---|
| Input | Má»™t bÃ i lÃ m | ToÃ n bá»™ lá»‹ch sá»­ (tá»« view SQL) |
| Output | Äiá»ƒm tá»«ng tiÃªu chÃ­ + sá»­a lá»—i | Lá»i khuyÃªn chiáº¿n lÆ°á»£c |
| CÃ¢u há»i nÃ³ tráº£ lá»i | "BÃ i nÃ y máº¥y Ä‘iá»ƒm?" | "TÃ´i nÃªn há»c gÃ¬ tiáº¿p?" |

Cáº£ hai Ä‘á»u báº¥t Ä‘á»“ng bá»™, Ä‘á»u cÃ³ báº£ng riÃªng, Ä‘á»u cÃ³ worker poll 2 giÃ¢y.
</details>

<details>
<summary><strong>"Demo mÃ  máº¡ng há»ng thÃ¬ sao?"</strong></summary>

Äáº·t `AI_PROVIDER=fake` trong `.env.docker` rá»“i `docker compose up -d --force-recreate api`.
`FakeAiGrader` tráº£ káº¿t quáº£ táº¥t Ä‘á»‹nh, khÃ´ng gá»i máº¡ng, khÃ´ng tá»‘n quota. ToÃ n bá»™ luá»“ng váº«n cháº¡y y
nguyÃªn â€” chá»‰ khÃ¡c lÃ  Ä‘iá»ƒm do mÃ¡y sinh thay vÃ¬ model.
</details>

---

## 8. Má»™t phÃºt Ä‘á»ƒ nhá»›

```
Ná»™p bÃ i
  â†’ ghi AiGradings(pending)       â† request tráº£ vá» ngay
  â†’ worker poll 2s
      â†’ AiGradingProcessor
          â†’ IAiGrader.GradeAsync   â† CHá»– DUY NHáº¤T gá»i AI
              â”œâ”€ cÃ³ audio â†’ SpeechSuper
              â””â”€ khÃ´ng audio â†’ Claude
          â† AiGradingResult { Scores, Transcript, FailureReason }
      â†’ ghi AiGradings(done|failed)
      â†’ má»i bÃ i xong â†’ FinalizeAiGradedAttemptAsync â†’ attempt graded
  â†’ web poll status â†’ hiá»‡n Ä‘iá»ƒm
```

**Ba cÃ¢u chá»‘t khi báº£o vá»‡:**

1. "AI **khÃ´ng** náº±m trong request cá»§a ngÆ°á»i dÃ¹ng. NÃ³ náº±m trong má»™t worker ná»n, vÃ¬ request khÃ´ng
   thá»ƒ chá» 30 giÃ¢y."
2. "Rubric lÃ  **dá»¯ liá»‡u trong database**, khÃ´ng pháº£i chuá»—i viáº¿t cá»©ng trong prompt. Äá»•i thang Ä‘iá»ƒm lÃ 
   Ä‘á»•i dá»¯ liá»‡u, khÃ´ng pháº£i sá»­a code."
3. "AI **Ä‘Æ°á»£c quyá»n tá»« chá»‘i**, vÃ  khi tá»« chá»‘i thÃ¬ UI pháº£i nÃ³i lÃ½ do. Má»™t cá»™t Ä‘iá»ƒm NULL khÃ´ng pháº£i lÃ 
   khÃ´ng cÃ³ dá»¯ liá»‡u â€” nÃ³ lÃ  má»™t sá»± kiá»‡n cáº§n giáº£i thÃ­ch."
