# MASTER-SPEC — Multi-certificate exam practice app

> 2026-10-31. Bản chốt để thi hành. Nguồn nghiệp vụ: `Language Exam App Architecture Research.md`.
> Trạng thái sự thật về code hiện tại: `docs/phfSTATE.md` + `docs/audit/*.md`.
> Quyết định không được đảo: §1.

---

## 1. Ràng buộc bất biến

| # | Ràng buộc | Hệ quả nếu vi phạm |
|---|---|---|
| B1 | **Multi-certificate là yêu cầu thật.** Giảng viên sẽ tạo cert mới ngay trên máy khi chấm. | Cert mới vỡ → mất điểm toàn bộ phần nghiệp vụ |
| B2 | **Không hardcode tên cert, mã skill, mã section, hay thang điểm** ở bất kỳ tầng nào | Cert thứ ba vỡ im lặng |
| B3 | **Điểm đi đúng pipeline**: raw → scaled → band, dùng tham số của chính cert đó | Điểm sai = bug nghiệp vụ nặng nhất |
| B4 | **Không bịa điểm khi lỗi.** Không fallback `MaxScore×0.7`, không mặc định `6.0` | Chấm bài bị đánh giá là giả mạo |
| B5 | **Tạo cert xong là dùng được ngay** — không có màn hình cấu hình điểm thứ hai | Cert "chết" với 0 band |
| B6 | **Đề chỉ publish được qua cổng audit.** Không tồn tại đường vòng | Đề rỗng lên production |
| B7 | **Preview dưới góc học viên là bắt buộc**, dạng thi thật có điều hướng nhanh | Admin không duyệt được đề 200 câu |
| B8 | **Goal gồm band đích VÀ thời hạn.** Thời hạn phi thực tế phải bị từ chối kèm số liệu | Lộ trình vô nghĩa |
| B9 | **Phát hiện lệch trình độ thì nói rõ với người học**, không âm thầm hạ | Mất niềm tin / trái yêu cầu |
| B10 | **LLM không được tự tính số.** Mọi con số do code tính; LLM chỉ diễn đạt | Lộ trình bịa |
| B11 | Mỗi việc xong phải **manual test thấy đúng** mới sang việc tiếp | Bug dồn |

---

## 2. Mô hình dữ liệu đích

### Thêm mới

| Bảng / cột | Cấu trúc | Dùng cho |
|---|---|---|
| `Skills.MinScaledScore, MaxScaledScore, ScoreStep` | int/int/decimal | Thay hoàn toàn `GetFallbackScale` — thang điểm là dữ liệu của skill |
| `Skills.LockNavigationOneWay` | bit | Thay `skillCode === 'LIST'` trong navigation |
| `ScoreBands.CumulativeStudyHours` | int NOT NULL | Mốc giờ học tích luỹ → tính ΔH |
| `ScoreBands` **phủ kín liên tục** | MinTotal/MaxTotal nối tiếp, không hở | Chỗ hở không còn bị clamp im lặng |
| `TargetBands.CurrentBandCode, TargetDate, WeeklyStudyHours, RequiredTotalHours, FeasibilityStatus` | varchar/date/decimal/int/varchar | Goal có thời hạn |
| `GradingCriteria.RubricDescription` | nvarchar(1000) NOT NULL | Band descriptors vào prompt |
| `AiGradings.BandCode` (bắt buộc ghi), `PriorityFix`, `ModelRewrite`, `ErrorSpansJson`, `TranscriptText` | — | Output hành động được |
| `Questions.SampleAnswerOrImageDesc`, `RequiredKeywordsJson` | nvarchar | Ngữ cảnh cho Speaking/Writing |
| `AttemptAnswers.AnsweredAt` | datetime2 | Phát hiện bấm lướt |
| `UserCategoryMasteries` | UserId, CategoryId, MasteryState 0..3, RollingAccuracy, ConsecutiveCorrect, LastPracticedAt, NextReviewDueAt | Mastery theo chủ đề |
| `UserQuestionHistories` | UserId, QuestionId, TotalAttempts, CorrectStreak, LastIsCorrect, LastAnsweredAt, CooldownUntil | Cooldown 24h |
| `LearnerPathSteps.WeekNumber, EstimatedMinutes, TargetAccuracyPct, ActualAccuracyPct` | int/int/int/decimal | Tiến độ theo tuần |

### Xoá

`PathModuleItems.IsPremiumOnly` · trigger `trg_Attempts_NoBandForSpeakingPractice` · bộ lọc `all/wrong_before/unseen` + ô chọn số lượng câu · micro-check chấm client · `PracticeSessions.FeedbackMode` · `_DashboardSimulator` (widget + CSS + ViewModel) · `SaveGroup`/`SaveQuestion` form-post · `CompleteStepAsync` · `/admin/imports` + `/admin/exams/{id}/questions` shim · trang `/admin/score-conversion` · 2 đề Placement "Quick"/"Standard" + chữ "Adaptive" · dropdown Status ở Exam Editor step 1 · ô text `AudioPath`/`ImagePath` · 3 bảng band JS · `GetFallbackScale` cả 2 bản · fallback `MaximumScore=990` · nhãn "0–200 scale" · "No band is shown for writing alone" · `if (!fBands.Any()) … ` nhánh `independent → TotalScore=0`.

---

## 3. Tám phân hệ — luật hành vi then chốt

### Q1 Học vs Thi — Mastery
Máy trạng thái 4 bậc trên **Category**, không phải trên câu hỏi:
`Untested(0)` → `Learning(1)` (làm ≥1 câu, accuracy <75% hoặc <5 câu, ôn lại +24h) → `Reviewing(2)` (accuracy ≥75% trên ≥5 câu **hoặc** streak ≥3; giãn +3 ngày, lần hai +7 ngày) → `Mastered(3)` (accuracy ≥85% qua ≥2 phiên cách ngày, ≥10 câu; giãn +14 ngày).
Sai 2 câu liên tiếp khi đang Mastered → **lapse**, về Reviewing, ôn lại +24h.
Phiên luyện **cố định 10 câu**: ≤4 câu cũ `LastIsCorrect=0 AND CooldownUntil<=now` + ≥6 câu unseen cùng Category; hết unseen thì lấy câu có `LastAnsweredAt` xa nhất.

### Q2 Lộ trình có thời hạn
`ΔH = max(30, Hours(Target) − Hours(Current))` từ `ScoreBands.CumulativeStudyHours`.
`W = max(1, ceil((TargetDate − today)/7))`, `H_avail = W × WeeklyStudyHours`, `R = H_avail / ΔH`.
- `R ≥ 0.85` và `WeeklyStudyHours ≤ 25` → **Feasible**
- `0.50 ≤ R < 0.85` → **Stretch** (gợi ý nâng lên `ceil(ΔH/W)` giờ/tuần)
- `R < 0.50` hoặc cần >25 giờ/tuần → **Unrealistic** → **chặn tạo lộ trình**, đưa 2 lựa chọn: lùi ngày thi `ceil(ΔH/WeeklyStudyHours)` tuần, hoặc hạ target.

Sinh lộ trình: **cửa sổ trượt 4 tuần**, mỗi tuần 3–5 bước (25–45 phút/bước). Trong tuần: 60% practice vào **2 section yếu nhất**, 25% lesson + spaced review, 15% mock (cuối mỗi chu kỳ 2 tuần).

Tuần **On Track** khi: hoàn thành ≥80% số bước **VÀ** accuracy trung bình ≥ `TargetAccuracyPct` (70% cho B1/B2, 80% cho C1). Thời lượng đo bằng proxy `DATEDIFF(minute, StartedAt, FinishedAt)` kẹp `[1,120]`.

### Q3 Chấm Writing/Speaking
`S_norm = Σ(Score_i/MaxScore_i × Weight_i) / Σ Weight_i` → map tuyến tính vào `[Skill.MinScaledScore, Skill.MaxScaledScore]`, làm tròn theo `Skill.ScoreStep` → tra `ScoreBands` → **ghi `AiGradings.BandCode`**.
Prompt phải có: `rubricDescription` từng tiêu chí + checklist nhị phân (`addressesPrompt`, `usesAllKeywords`, `meetsLength`) **trước khi** chốt điểm.
Output bắt buộc: `scores[]` khớp đủ criterionId · `errorSpans[]` ≤5 (originalSnippet, correctedSnippet, category, explanationVi) · `priorityFix` · `modelRewrite`.
Thiếu tiêu chí / transcript rỗng → retry 1 lần → `Status='Failed'` + nút "Chấm lại". **Không bịa điểm.**

Speaking tách 2 chế độ:
- **Scripted** (Read Aloud): `refText` = văn bản cần đọc.
- **Unscripted**: `questionPrompt` = đề (chỉ làm ngữ cảnh), `refText` = rỗng; cần `SampleAnswerOrImageDesc` cho câu mô tả tranh, nếu không có thì điểm relevance bị **bỏ**, không được tính.
Chuẩn hoá điểm âm học từ thang gốc về `[0..1]` rồi mới map.

### Q4 Dashboard
**3 khối**: (1) **Hero Next Action** — việc làm tiếp theo duy nhất (bài dở → bước lộ trình khả dụng → số chủ đề đến hạn ôn); (2) **Goal Standing** — band hiện tại, band đích, còn N ngày, tuần này `đã/định` phút; (3) **Skill Cards** — render **động** từ `Cert.Skills`, mỗi cái có điểm gần nhất + 3 category yếu nhất.
Sidebar **5 mục**. Writing/Speaking có lối vào trực tiếp.
Xoá 8 khối còn lại.

### Q5 Hiệu chuẩn placement
Đề placement seed **≥20 câu**, bỏ nhãn "Adaptive". Thêm `AnsweredAt`.
`EvaluateCalibration()` chạy sau **20 câu luyện tập hợp lệ** (lọc câu trả lời <3 giây qua `AnsweredAt`). Nếu accuracy thực <40% trong khi placement ≥1 band cao hơn → hiện hộp thoại **minh bạch**: nêu bằng chứng, đề xuất hạ lộ trình, người học bấm đồng ý mới đổi.

### Q6 Onboarding
**Một màn hình, 4 trường**: cert · band hiện tại (từ placement) · band đích · ngày thi + giờ/tuần. Không hỏi khảo sát cảm tính. Không tạo bảng phụ.

### Q7 Admin soạn đề
- Bỏ dropdown Status. Publish **chỉ** qua `POST /publish` có audit.
- `TargetQuestionCount` chuyển **NOT NULL CHECK (>0)**.
- Preview: dùng **chính view thi thật** với `?preview=true`, có thanh nhảy nhanh 200 câu + nút bật/tắt đáp án, không ghi attempt.
- Studio **2-pane**: mỗi câu **1 dropdown Category**, upload file thật thay ô text đường dẫn, 2 nút gán `AudioStartMs/EndMs` từ player.
- Đề đã có người thi → **khoá cấu trúc**.
- Bảng **Question Bank Health** theo category.

### Q8 Multi-cert + điểm
Bước 3 của Cert Wizard chứa: cấu trúc section **và** tham số thang điểm skill **và** bảng `ScoreScale` **và** dải `ScoreBands` liên tục **và** rubric. Không còn màn hình riêng.
`CertificateConfigurationValidator` chặn 5 bất biến: (1) band phủ kín `[MinTotal, MaxTotal]` không hở không chồng; (2) `ScoreStep > 0`; (3) `MaxScaledScore > MinScaledScore`; (4) mọi rubric tiêu chí có `RubricDescription`; (5) mỗi skill có ≥1 scale row hoặc công thức sinh.

---

## 4. Thứ tự thi hành

Phụ thuộc cứng: **E1 (điểm + đa chứng chỉ) → E2 (AI chấm) → E3 (mastery + lộ trình) → E4 (studio + dashboard)**, E5 dọn dẹp chạy song song được sau khi mục tương ứng xong.

| Epic | Nội dung | Chặn bởi |
|---|---|---|
| E1 | Nền móng đa chứng chỉ & engine điểm | — |
| E2 | Chấm Writing & Speaking | E1 |
| E3 | Mastery, lộ trình có hạn, hiệu chuẩn | E1 |
| E4 | Studio, preview thi thật, dashboard | E1, E3 |
| E5 | Dọn dẹp code chết | — (từng mục sau khi cái thay thế xanh) |

---

## 5. Định nghĩa "Xong"

Một mục chỉ được coi là xong khi **cả bốn**:
1. `dotnet build` sạch, `dotnet test` xanh.
2. Chạy app thật, bấm tay đúng kịch bản trong acceptance criteria, thấy kết quả.
3. Không còn đường code nào của cơ chế cũ (grep sạch).
4. Tạo một cert **mới hoàn toàn** trong wizard và dùng được từ đầu tới cuối — không cần thao tác ngoài UI.

## 6. Cách kiểm chứng đa chứng chỉ (dùng lại ở mọi mục)

Kịch bản chuẩn, chạy tay sau mỗi epic:
1. Admin tạo cert `DEMO` — 2 skill, thang 0–50 bước 1, 4 band liên tục, 2 tiêu chí rubric có mô tả.
2. Soạn 1 đề 20 câu, preview, publish.
3. Học viên: placement → goal (band + 30 ngày + 6h/tuần) → lộ trình sinh ra → luyện 1 phiên → thi thử → thấy band đúng thang 0–50 (không phải 990, không phải 200).
4. Nộp bài Placement **không văng lỗi** `CK_Placement_DoneHasResult`.
