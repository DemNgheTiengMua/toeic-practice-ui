# Research brief — TOEIC practice app: nghiệp vụ học, lộ trình, chấm điểm, UI

> 2026-10-30. Dành cho agent deep-research **không truy cập được repo**. Mọi dữ kiện dưới đây là đọc từ code thật.
> Trả lời bằng tiếng Việt, giữ thuật ngữ tiếng Anh (band, rubric, spaced repetition…).

---

## 0. Bối cảnh

Đồ án sinh viên: web luyện thi chứng chỉ ngoại ngữ, backend .NET 10, server-rendered MVC. Giảng viên sẽ **bấm thử như người dùng thật**, không đọc test.

App đã có nhiều tính năng nhưng tác giả không biết **thế nào là "đúng"**. Nghi ngờ chính: bloat — có mặt nhưng không có hồn.

Kết quả của bạn dùng để viết **spec sửa**, không phải thêm tính năng. Tiêu chí: sau khi áp dụng, app ít tính năng hơn nhưng mỗi thứ còn lại làm thật và đúng.

---

## 1. Ràng buộc

1. **Mỗi kết luận phải có nguồn.** Ưu tiên tài liệu chính thức (ETS, CEFR, nghiên cứu về spaced repetition / mastery learning / psychometrics), rồi tài liệu sản phẩm thật (ELSA, Duolingo, Anki, Grammarly). "Best practice" không nguồn = không tính.
2. **Mỗi đề xuất phải nói nó thay hoặc xóa cái gì.** Thêm tính năng được phép, với điều kiện kèm một dòng "đổi lấy cái này".
3. **Phân 3 mức:** (a) bắt buộc để đúng nghiệp vụ, (b) nên có để dễ hiểu, (c) chỉ để trông đẹp. Đồ án chết ở (c).
4. **Không thuê dịch vụ mới, không đổi stack.** 0 đồng, còn vài tuần.
5. Nếu câu hỏi là **câu hỏi sai**, nói thẳng và trả lời câu đúng hơn.
6. **Phải có danh sách XÓA** — nêu tên, không "cân nhắc".

**Đã chốt, thiết kế xung quanh (không bàn lại):**

- **Multi-certificate là yêu cầu thật** — demo tạo chứng chỉ mới ngay trên máy giảng viên. Không được đề xuất khoá cứng một cert.
- **Chuyển đổi điểm không được sai** (raw → scaled → band). Sai thang điểm là bug nghiệp vụ, không phải chi tiết kỹ thuật.
- **Đề do người soạn hoặc import. Không có sinh nội dung đề tự động.**
- **Cấu hình điểm thuộc về định nghĩa chứng chỉ.** Tạo cert là đủ để cert đó chấm được — không có màn hình cấu hình điểm riêng. Xem Q8.
- **Preview dưới góc học viên là bắt buộc**, dạng chế độ thi thật có điều hướng để admin duyệt đề nhanh.
- **Exam Studio sẽ dựng lại front-end.**
- **Goal gồm band đích VÀ mốc thời gian.**
- Khi phát hiện trình độ thật lệch placement: **nói rõ với người học**, không âm thầm hạ lộ trình.

---

## 2. Hiện trạng

### 2.1 Dữ liệu

`Certificates` → `Skills` → `Sections` → `QuestionGroups` → `Questions` → `QuestionOptions`. Câu tự luận ở `FreeResponses`.

- `ScoreBands`: map điểm thô sang **band CEFR** (`A1`…`B2`). Không có thang TOEIC 10–990 ở tầng band.
- `TargetBands(UserId, CertId, TargetBandCode, SetAt)` — **chỉ band, không có ngày/tuần/giờ-tuần**.
- `PathTemplates(CertId, FromBandCode, ToBandCode)` → `PathModules(EstimatedMinutes)` → `PathModuleItems(ItemType, QuestionCount, IsPremiumOnly)` → `LearnerPaths` → `LearnerPathSteps(Status, Score)`.
- `PracticeSessions.LearnerPathStepId` nối practice ↔ path.
- `AiGradings(Status, OverallScaled, BandCode, Model)` + `AiGradingScore(CriterionId, Score, Comment)`.
- `GradingCriterion` **không có cột mô tả** — không có chỗ chứa rubric.
- `Lessons` / `LessonCards` / `LearnerLessonProgress`. Micro-check chấm ở client, không lưu điểm.

### 2.2 Học vs thi

| | Practice | Mock exam |
|---|---|---|
| Chọn câu | người học chọn section, số lượng, nguồn (`all`/`wrong_before`/`unseen`) | blueprint admin, khoá theo thứ tự |
| Feedback | ngay từng câu: đúng/sai + đáp án + giải thích | không có gì |
| Điểm | đúng/tổng → % | thô → `ScoreScales` → band |

**Không có mastery ở cấp câu hỏi.** Câu sai vào tập `wrong_before` chỉ để chọn lại — không lịch ôn, không trạng thái thành thạo, làm đúng một lần là biến mất khỏi tập sai. Không có khái niệm ghi nhớ dài hạn.

### 2.3 Lộ trình

Không do LLM sinh. Luồng: chọn target band → lưu → tìm `PathTemplates` khớp đúng cặp `(from, to)` → không khớp thì "from gần nhất" → không nữa thì "template bất kỳ của cert". `startingBand` từ placement gần nhất, không có thì mock gần nhất, không có thì **band đầu tiên**; `toBand` không có thì **band cuối cùng** — mặc định nhắm band cao nhất. Sắp lại module theo section yếu nhất. Sinh step: đầu `available`, còn lại `locked`. LLM **chỉ viết 3–4 câu bình luận** cho path đã dựng.

Hai người cùng cặp band nhận cùng template, khác nhau chỉ ở thứ tự module. Không có thời lượng, không có "tuần này làm gì".

Step `practice_section` khép kín: trả lời câu cuối → `done`, mở step kế. Step `mock_exam` **không đường nào set done** — thi xong cũng không cập nhật. Step `lesson` tính lại lúc đọc, không persist.

### 2.4 Chấm Writing

Gửi model: đề bài, min/max từ, bài làm, tiêu chí dạng `{ID, Code, Name, MaxScore, Weight}`.
**Không gửi:** band descriptor, mô tả mức điểm, target band, CEFR, rubric.

System prompt: *"You are a senior, certified official English exam evaluator (IELTS, TOEIC, VSTEP). You score strictly according to CEFR and official rubric criteria…"* — dặn theo CEFR nhưng không cung cấp CEFR.

Output: `{"scores":[{"criterionId","score","comment"}]}`. Không band, không chữa lỗi, không bản viết lại.

- Criterion bị bỏ sót → code **tự điền `MaxScore × 0.7`** + comment `"Standard performance."`.
- `Weight` gửi đi nhưng **không dùng** khi cộng.
- `AiGradings.BandCode` **không bao giờ ghi**.
- UI in: *"No band is shown for writing alone."*

### 2.5 Chấm Speaking

SpeechSuper (`speak.eval.pro`), tích hợp HTTP thật: `transcription, overall, pronunciation, fluency_coherence, grammar, lexical_resource, relevance`.

- Payload hardcode `test_type: "ielts"`, `task_type: "ielts_part1"` cho mọi cert.
- `refText` = **chính đề bài** — không có reference answer.
- Field thiếu → mặc định `6.0` (relevance `80`).
- Quy đổi 0–200 hardcode, UI in "0–200 scale" cho mọi cert.

### 2.6 Placement & tín hiệu hành vi

- **Không adaptive** (dù copy UI ghi "Adaptive across both sections"). Nạp toàn bộ câu theo thứ tự cố định.
- **Không bảo đảm 4 kỹ năng** — dùng lại đề admin soạn. Seed: "Standard" = **4 câu**, Listening+Reading; "Quick" = **2 câu**; "Diagnostic Benchmark" = 32 câu, không Speaking.
- **Không có per-question telemetry.** `AttemptAnswers` = 8 cột, **không có timestamp nào**. Ghi đè tại chỗ → không đếm được số lần đổi đáp án. `IsFlagged` là bit → không có lịch sử.
- Không có bảng event/log hành vi, không IP, không device, không focus/paste.
- Có: `ExamAttempts.{StartedAt, ExpiresAt, PhaseDeadlineAt, SubmittedAt, IsAutoSubmitted}`, `FreeResponses.{DraftSavedAt, SubmittedAt, DurationMs}`, `PracticeSessions.{StartedAt, FinishedAt}`, `Users.LastLoginAt` (ghi đè).
- Độ chính xác category chỉ có **tổng hợp toàn lịch sử** (`vw_UserCategoryAccuracy`), không tách được một attempt.

### 2.7 Admin soạn đề

**Ba bàn tách rời:** wizard cert (định nghĩa `Section`, **28 field/card**, 44 control/4 bước) · exam editor (metadata + audio) · studio (3 pane, autosave, 25 control tag mỗi câu, ≈18 input hiển thị cho mcq 4 đáp án).

**Ba đường tạo đề:** form thủ công, import JSON/ZIP (không CSV/Excel), quick ingest (paste text).

**Audit:** ~26 mã lỗi, có link "Jump to issue".

**Vấn đề:**
- Audit chỉ chặn `POST /publish`. Dropdown Status ở step 1 đặt thẳng `Published` trên đề rỗng → **publish bỏ qua audit**.
- **Không có preview dưới góc học viên.** Muốn xem phải publish thật.
- Không chặn sửa đề đã publish đã có người thi.
- Audio phải **cắt sẵn bên ngoài**, rồi gõ tay millisecond vào `AudioStartMs/EndMs`. Slicer chỉ là phụ trợ đồng bộ.
- `AudioPath`/`ImagePath` là ô text tự do → gõ sai là asset hỏng im lặng.
- `AcceptedAnswersJson` một ô nhận hai cú pháp, không validate.
- `QuestionCount` optional nhưng là điều kiện cổng publish.
- Section không sửa được từ màn soạn đề.

### 2.8 Dashboard

11 khối: alert kích hoạt · nhắc verify email · chào · banner bài dở · banner thi dở · dải 4 ô readiness (Current Standing / Target Band / Learning Path / Diagnostic Accuracy) · panel "AI Study Mentor" · "Today's Directive" 3 bước · "Accuracy Matrix" 5 dòng · bảng recent exams (6 cột) + bảng recent practice (5 cột) · "Mentor Boarding Pass". Không chart, chỉ progress bar.

Sidebar: 17 mục / 6 nhóm. **Writing và Speaking không có mục nào**; `/practice` render chúng thành card mờ "Available in full mock exams". Người mới không có đường tới 2 chức năng AI chấm bài.

---

### 2.9 Chứng chỉ & điểm số

**Wizard cert chỉ ghi 3 bảng:** `Certificate`, `Skill`, `Section`. Bốn bước là một form phẳng, không phải bốn lần ghi.
**`ScoreScale` và `ScoreBand` chỉ được ghi từ `/admin/score-conversion`** — không phải trùng lặp, mà là **phụ thuộc**: cert mới tạo ra có **0 dòng scale, 0 dòng band**.

**Hệ quả cứng:** không có `ScoreBand` → `FindBandCodeAsync` trả `null` → mọi attempt `BandCode = null` → `CK_Placement_DoneHasResult` **chặn luôn mọi lần nộp placement** của cert đó.

**Pipeline (đúng, không phải từ raw):**
```
per-skill raw = số câu đúng
  → ScoreScale (RawScore → ScaledScore)        [không có dòng → rơi vào fallback hardcode]
  → AggregateTotalScore (sum | avg_half_step | avg_whole | independent)
  → ScoreBands (MinTotal ≤ total ≤ MaxTotal)
  → ExamAttempts.BandCode / PlacementTests.ResultBandCode
```
`ScoreScale` khoảng trên **số câu đúng**; `ScoreBand` khoảng trên **tổng scaled**. Hai bảng, hai trục, nối nhau chỉ bằng code C#.

Bốn chỗ làm `constant` giả dạng dữ liệu:

1. **`GetFallbackScale(certCode)`** — `"IELTS"` → 10–90 step 5 · `"VSTEP"` → 10–100 step 1 · **mọi thứ khác → 5–495 step 5 (TOEIC per-skill)**. Cert mới không tên IELTS/VSTEP bị chấm ngầm theo thang TOEIC. Hàm này **được chép nguyên một bản nữa** ở `ScoreConversionService.Preview.cs`.
2. **Preview ở wizard (JS) tự chế 3 bảng band khác nhau**, không đọc `ScoreBands`:
   - `sum` → `GOLD ≥905 / BLUE ≥785 / GREEN ≥605 / BROWN ≥405 / ORANGE`
   - `avg_half_step` → `C2 ≥85 / C1 ≥70 / B2 ≥55 / B1 ≥40`
   - `avg_whole` → `C1 ≥85 / B2 ≥60 / B1 ≥40`
   Bảng GOLD/BLUE **mâu thuẫn** với `ScoreBands` seed thật (CEFR `below_A1` 10–119 … `C1` 945–990). Và bảng `avg_half_step` khác `avg_whole` ở ngưỡng B2 (55 vs 60) và C1/C2. Admin thấy preview một đằng, engine chấm một nẻo.
3. **Thang 0–200 cho Speaking** hardcode (`(total/max)*200`), UI in label "0–200 scale" cho mọi cert.
4. **`MaximumScore = 990`** làm fallback khi cert không có band.

**Ràng buộc khác đang là enum đóng, không sửa được bằng dữ liệu:**
- `Modality` = `Listening|Reading|Speaking|Writing` (DB CHECK). Không định nghĩa được modality thứ năm.
- `ScoreAggregationRule` = 4 giá trị, validate cứng trong service.
- `avg_half_step` = chính xác cách làm tròn 0.25/0.75 **của IELTS**, gắn vào một luật chung — cert nào chọn luật này cũng thừa hưởng rounding của IELTS. Hàm này có bản JS chép lại.
- `independent` trả `TotalScore = 0` → cert dùng luật này **không bao giờ có band**.
- `trg_Attempts_NoBandForSpeakingPractice`: **cấm tuyệt đối** `BandCode`/`TotalScore` cho attempt speaking/writing practice.
- `ScoreBands` có trigger chống chồng lấn, nhưng **không** validate phủ kín toàn dải — chỗ hở được lấp bằng **clamp về band gần nhất**, không báo lỗi.
- `ScoreScale` int overload lấy **dòng gần nhất** (không nội suy); decimal overload **nội suy tuyến tính**. Hai hành vi khác nhau cho cùng một việc.

**Kỹ năng sản phẩm không có band:** `GradingCriterion` scoped theo `(CertId, SkillId)` và có `MaxScore` riêng từng cert — nhưng **không có admin UI nào để sửa**; dòng chỉ đến từ seed SQL. Cert tự tạo → rubric rỗng → `maxScore = 0` → Writing/Speaking đóng góp **0** vào điểm.

**Preset không định nghĩa điểm.** 6 preset hardcode (`ielts_academic`, `toeic_lr_std`, `toeic_lr_cat`, `toeic_sw`, `vstep`, `custom`) chỉ mô tả **cấu trúc**. Admin được tạo cert ngoài preset (chọn `custom`, hoặc sửa tự do `Code` của preset bất kỳ — `PresetCode` lưu nguyên, không validate). Để cert đó chấm được, admin phải tự tay làm 5 việc: scale, band, criterion, `ExpectedQuestionCount`, path template.

**Chỗ khác hardcode theo tên cert/skill:** `AttemptViewModelMapper` dựng đúng **2 card Listening/Reading** bằng `SkillCode == "LIST" || Name.Contains("Listening")` → cert chỉ có Writing hiện 2 card rỗng; `navigation.js` khoá một chiều khi `skillCode === 'LIST'`; `AdminExamsController` chọn blueprint mặc định `IELTS→"PAS"`, `VSTEP→"R4"` (**`R4` không tồn tại trong bất kỳ preset nào**), còn lại `"P5"`; và có một danh sách cert fallback bịa (`(1,"toeic"), (2,"ielts"), (3,"vstep")`) khi API lỗi.

---

## 3. Câu hỏi

### Q1. "Học" khác "thi" ở đâu — và tối thiểu cần gì?

- Về khoa học học tập, tối thiểu app "luyện" phải có gì để không chỉ là thi thử nhiều lần? (retrieval practice? spaced repetition? mastery threshold? error log có cấu trúc?)
- ETS có cơ chế luyện chính thức không, hay chỉ đề mẫu?
- Đủ cho đồ án vài tuần là gì — ví dụ "mastery theo câu (3 lần đúng liên tiếp) + hàng đợi ôn" có đủ? Cần bao nhiêu trạng thái?
- Phản biện: spaced repetition trên **câu hỏi cụ thể** có vô nghĩa (nhớ đáp án chứ không nhớ ngữ pháp)? Nếu vậy đơn vị đúng là **category/tag**? Nêu bằng chứng.
- Nên bỏ gì trong practice hiện tại (`wrong_before`/`unseen`/chọn số lượng) để thay bằng thứ có nghĩa hơn?

### Q2. Lộ trình từ "band hiện tại + band đích + thời hạn"

- Có số liệu công khai về giờ học để tăng X điểm TOEIC không? Nếu không, nói rõ và đề xuất cách khác thay vì bịa.
- **Công thức phân bổ**: (điểm hiện tại, điểm đích, số tuần, giờ/tuần) → (số buổi/tuần, loại hoạt động, khối lượng). Cái gì tính bằng code, cái gì để LLM diễn đạt?
- **Ranh giới để LLM không bịa số**: mọi con số do code tính, LLM chỉ chọn từ ngữ và lý do — có mô hình nào làm tốt để tham khảo?
- **Tiêu chí "tuần này đạt/chưa đạt"** — % câu đúng đủ chưa, hay cần gì hơn?
- Ngưỡng bỏ cuộc của người học trong app giáo dục là bao nhiêu (nghiên cứu churn)? Lộ trình bao nhiêu bước là vừa?
- Nếu chưa có dữ liệu thời gian thực (ngày đăng nhập, phút học) thì nên xây hay dùng proxy?
- **Thời hạn phi thực tế** (B1→C1 trong 2 tuần): nói thật là không khả thi, hay tự nới? Tiêu chuẩn nào để biết "không khả thi"?

### Q3. Chấm Writing/Speaking

- **Rubric chính thức** TOEIC Writing/Speaking: bao nhiêu tiêu chí, thang mấy bậc, mô tả ở đâu (dẫn ETS). Nên nhét **band descriptor** vào prompt, hay dùng checklist binary thay vì cho điểm liên tục?
- **Input tối thiểu** để chấm đáng tin? Cái nào là thừa?
- **Output nào là hành động được** — chữa từng lỗi kèm vị trí? bản viết lại? một lỗi lớn nhất? Nêu ví dụ từ Grammarly/ELSA/IELTS Writing Assistant về cái gì **thực sự khiến người học sửa được bài**.
- **Độ tin cậy LLM grader**: nghiên cứu về độ lệch so với giám khảo người? Không hiển thị band cho writing lẻ là đúng hay là lảng tránh?
- **Speaking**: SpeechSuper có hỗ trợ TOEIC không? Nếu không thì khung hoá là "luyện phát âm"? `refText` đúng nên là gì? Không có reference answer thì điểm "relevance/content" có nghĩa gì?

### Q4. Dashboard & IA

- Nguyên tắc IA cho app học: màn hình đầu giải quyết đúng **một** câu hỏi gì? (single primary action, progressive disclosure)
- Với người học có mục tiêu thi, thứ tự ưu tiên chuẩn: "đang ở đâu → làm gì tiếp → bằng chứng tiến bộ"? Hay khác?
- Trong 11 khối ở §2.8, cái nào nên bỏ hẳn?
- Màn hình home của Duolingo/ELSA/TOEIC Official/Anki có bao nhiêu khối, khối nào chủ đạo?
- Nếu chỉ giữ **3 khối**, là 3 khối nào và vì sao?

### Q5. Phát hiện trình độ thật thấp hơn placement

Ràng buộc dữ liệu (§2.6): không có telemetry. Chỉ tính được tổng thời gian ÷ số câu, tỉ lệ auto-submit, và so category của attempt với baseline lịch sử (rỗng với người mới).

- Trong psychometrics, "câu trả lời không nhất quán với năng lực ước lượng" tên là gì? Chỉ số nào chạy được **trên ít dữ liệu** (lz, Guttman's caution index)? Cần **tối thiểu bao nhiêu item** mới có ý nghĩa?
- **Nếu chỉ được thêm MỘT cột/bảng**, nên thêm gì để bắt nhiều tín hiệu nhất (`AnsweredAt` per answer? `AttemptEvents`? `AnswerRevisions`)? So sánh chi phí/lợi ích.
- Tín hiệu nào có bằng chứng, tín hiệu nào là dân gian: thời gian/câu, tỉ lệ đúng câu khó vs dễ đảo ngược, chênh lệch section, số lần đổi đáp án, pattern đúng-liên-tiếp-rồi-sai?
- **Thời điểm** phát hiện: session đầu? sau N câu? sau N session? Kết luận sớm quá sẽ oan.
- **Ngưỡng** nào thì điều chỉnh, và điều chỉnh **bao nhiêu** (hạ 1 bậc? hạ tới mức quan sát được? giữ nguyên + remediation)? Cách nào không làm người học thấy bị trừng phạt?
- **Baseline rỗng** (đúng kịch bản người thi hộ): phát hiện thế nào khi không có gì để so? Có cần bài "kiểm tra xác nhận" ngắn, và làm nó **không giống bị soi**?
- **Cách nói với người học**: sản phẩm nào đã làm việc này minh bạch mà không gây mất mặt? Câu chữ nên thế nào (ngôn ngữ "hiệu chuẩn lại ước lượng" thay vì "khai gian")?
- Nếu dữ liệu không đủ chắc: im, hay hiển thị "độ tin cậy thấp"? Tiền lệ UX tốt?
- **False positive**: cách nào dễ bắt oan (giỏi nhưng làm nhanh / sai vì mất tập trung) và làm người học bực?

### Q6. Thông tin người học cần thu — vừa đủ, không hơn

- Trước mock test đầu, tối thiểu cần gì để ra lộ trình có nghĩa? (band hiện tại? ngày thi? giờ rảnh/tuần? kỹ năng yếu? mục tiêu dùng TOEIC để làm gì?)
- Cái nào **phải hỏi lúc onboarding**, cái nào **suy ra từ hành vi**? (nguồn về onboarding friction / drop-off)
- Không biết điểm hiện tại thì sao — placement đóng vai gì?
- Thu **ngày thi dự kiến** có nên hỏi, hay để mở?
- Bao nhiêu câu hỏi onboarding là "vừa" trước khi người học bỏ đi?

### Q7. Admin soạn đề: luồng tối thiểu, preview kiểu thi thật, studio

- Một đề TOEIC hoàn chỉnh tối thiểu gồm gì (sections, số câu/phần, thời lượng, audio, transcript)? Dẫn ETS. Con số này quyết định blueprint có cần hay chỉ là nghi thức.
- **Ranh giới blueprint vs nội dung đề**: cấu hình section thuộc về cert hay thuộc về exam? Tiền lệ từ Moodle/Canvas/Typeform/Google Forms về chỗ đặt cấu hình.
- **Draft → publish**: validation bắt buộc là gì? Làm sao để **không tồn tại đường nào** publish đề chưa đạt? Pattern chuẩn (state machine? server không nhận `status` từ client?)
- **Sửa đề đã có người thi**: cấm sửa, hay version bất biến? Cái nào rẻ hơn cho đồ án, rủi ro của bản rẻ?
- **Bulk import**: nền tảng thi thật nhập đề bằng gì? Định dạng nào chịu được nội dung đa dạng (nhiều dòng, hình, audio) mà dễ viết tay? Giữ import hay chỉ soạn tay?
- **Upload audio**: cách nào để admin không phải cắt file ngoài? Slicer trong app cần gì để thay được việc cắt ngoài?
- **Preview kiểu thi thật**: pattern nào cho phép admin duyệt đề nhanh mà không làm lại luồng thi? (render cùng view với cờ readOnly? route riêng?) **Điều hướng** nên thế nào để xem 200 câu trong vài phút chứ không click từng câu?
- **Công cụ soạn nội dung cho 200 câu**: kiểu UI nào khả thi thật (bảng spreadsheet? form tuyến tính? chia bước)? Nêu tiền lệ cụ thể. Cái gì là "đủ dùng" cho vài tuần — đừng đề xuất Notion-clone.
- Admin có cần thấy **sức khoẻ ngân hàng câu hỏi** (mỗi category bao nhiêu câu, có đủ để không lặp)? Tối thiểu hiển thị gì?

### Q8. Multi-certificate thật + đường đi của điểm

Ràng buộc: demo tạo cert mới ngay trên máy giảng viên. Không được khoá cứng một cert.

- **Cái gì là dữ liệu bắt buộc** khi định nghĩa một cert mới, cái gì được phép là code? Cụ thể: số phần/kỹ năng, thang thô, thang scaled, cách map raw→scaled (linear? table? weight theo section?), band, ngưỡng đậu, thời lượng, loại câu cho phép, rubric chấm sản phẩm, số lần nghe, thứ tự khoá phần. Có chuẩn nào định nghĩa khái niệm này (ETS, Cambridge, IELTS, CEFR alignment, "test blueprint/specification" trong assessment)? Dẫn nguồn.
- **Pipeline đúng**: `raw → scaled → band`, hay `raw → band` trực tiếp? TOEIC (10–990), IELTS (band 0–9, .0/.5), VSTEP (0–10) map CEFR khác nhau hoàn toàn. **Band có nên tồn tại độc lập hay chỉ là nhãn của scaled score?** Nếu chỉ là nhãn thì bảng `ScoreBand` có cần? Các mô hình đang dùng thật.
- **Kỹ năng sản phẩm (Speaking/Writing) có band không**, hay chỉ điểm rời (0–200, 0–8)? Nếu lộ trình và dashboard so sánh theo band, thiếu band cho 2 kỹ năng là lỗ hổng nghiệp vụ? Cách xử lý chuẩn (band riêng? quy đổi? không xếp lộ trình theo band cho kỹ năng sản phẩm)?
- **Cấu hình điểm thuộc về đâu**: về định nghĩa chứng chỉ, hay về một bảng cấu hình độc lập? Tiền lệ từ hệ thống nào? **Điểm cần trả lời:** nếu gộp vào một chỗ (tạo cert là đủ), làm sao một form định nghĩa cert chứa được cả cấu trúc (số phần, số câu) **và** bảng map điểm **và** band mà vẫn dùng được? Nêu pattern UI cho "bảng tra cứu dài" (raw → scaled cho ~200 dòng) nằm trong một form tạo thực thể — sinh tự động? nhập tay? công thức? nhập bằng file?
- **Kiểm chứng tính đúng**: làm sao *chứng minh* map raw→scaled→band đúng với từng cert, và làm sao cert mới **không thể** có cấu hình vô lý (band chồng nhau, tổng scaled ≠ max, thiếu band cuối, khoảng hở)? Pattern validate nào (invariant? property-based test?)
- **Bao nhiêu cert mẫu là đủ** để demo thuyết phục? (ví dụ: một cert chỉ Listening+Reading, một cert có Speaking, thang điểm khác nhau)
- **Điểm dừng hợp lý** cho vài tuần: hỗ trợ cert khác **cấu trúc** (số kỹ năng khác, có/không Speaking), hay chỉ khác **tham số** (số câu, thời lượng, thang điểm)?

---

## 4. Định dạng trả về

**A. Chẩn đoán** (tối đa 1 trang). Tám mảng, mỗi mảng 3–5 câu: điều gì **thật sự** sai về nghiệp vụ. Chỗ nào là "giả" đáng cắt hơn sửa.

**B. Tám phần spec**, mỗi phần theo khung:
```
Q<n>: <tên>
- Kết luận nghiệp vụ (kèm nguồn)
- Mô hình dữ liệu tối thiểu (field cần có, field bỏ)
- Luật hành vi (lúc nào đổi trạng thái, ngưỡng số)
- Cái gì giao cho LLM, cái gì là code
- Input/Output nếu có AI (liệt kê field, không cần code)
- Cắt gì
- Rủi ro nếu làm sai
```

**C. XÓA GÌ** — từng luồng / màn hình / tính năng **đang có** nên bỏ hẳn, mỗi dòng một lý do. Không được bỏ trống.

**D. Nếu chỉ được làm 3 thứ**, là 3 thứ nào và vì sao. Chỉ rõ nó nằm ở phần nào của A/B.

**E. Thứ tự thực hiện** kèm phụ thuộc, ước lượng theo ngày người.

**F. Những gì KHÔNG chắc** — liệt kê riêng.

---

## 5. Lưu ý cuối

Không cần giúp nghĩ thêm tính năng. Thứ có giá trị là: *cái gì đang làm nửa vời, và chuẩn mực nào cho biết khi nào là "xong"*. Nếu câu trả lời dài hơn phần "nên làm" thì coi như thất bại.
