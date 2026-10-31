# STATE — quyết định đã chốt

> Nguồn sự thật **duy nhất về "đã chốt cái gì"**. Không chép trạng thái công việc vào đây —
> cái đó ở `docs/STATUS.md`. File này chỉ trả lời một câu: quyết định nào không được đảo?
> Cập nhật lần cuối: 2026-10-31

---

## 1. Quyết định đã chốt

| # | Quyết định | Ngày | Lý do | Ảnh hưởng |
|---|---|---|---|---|
| D1 | **Multi-certificate là yêu cầu thật** — demo tạo cert mới ngay trên máy giảng viên | 10-30 | Đề tài yêu cầu agile, không được hardcode | Mọi thứ đi kèm cert phải là dữ liệu; không có đường tắt "khoá TOEIC" |
| D2 | **Chuyển đổi điểm không được sai** (raw → scaled → band) | 10-30 | Nghiệp vụ cốt lõi | Bug thang điểm là hạng cao nhất, sửa trước UI |
| D3 | **Đề do người soạn hoặc import** — không sinh nội dung đề tự động | 10-30 | Bỏ hẳn tính năng AI gen đề | Xoá `ExamGeneratorService`, `_ExamGenerateDialog`, endpoint generate, `blueprintJson` |
| D4 | **Preview dưới góc học viên bắt buộc**, dạng chế độ thi thật có điều hướng | 10-30 | Hiện không có preview nào; muốn xem phải publish thật | Phải duyệt được đề ~200 câu trong vài phút |
| D5 | **Exam Studio dựng lại front-end** | 10-30 | UI hiện tại không dùng được | Không phải polish — làm lại |
| D6 | **Goal gồm band đích VÀ mốc thời gian** | 10-30 | Chỉ band thì lộ trình vô nghĩa | Thêm khái niệm thời lượng vào toàn hệ thống |
| D7 | **Phát hiện lệch trình độ thì nói rõ với người học** (không âm thầm hạ) | 10-30 | Chọn phương án minh bạch | Cần câu chữ không gây mất mặt + cơ chế điều chỉnh |
| D8 | **Xoá trang `/admin/score-conversion`** — cấu hình điểm thuộc về định nghĩa cert | 10-30 | Đã cho tạo cert thì không cần màn hình convert riêng | ⚠️ Trang này là nơi DUY NHẤT ghi `ScoreScale`/`ScoreBand` → phải chuyển logic sang wizard, nếu không mọi cert mới vỡ |
| D9 | Quy trình làm việc: **làm xong từng mục, manual test thấy đúng mới sang mục tiếp** | 10-30 | Tránh bloat, tránh bug dồn | Mỗi mục phải có khối "Done khi" trước khi bắt đầu |
| D10 | **Cột thang điểm trên `Skills` là NULLable**, backfill từ `ScoreScales`, chưa ép NOT NULL | 10-31 | NOT NULL làm vỡ 12 file schema-test + 16 seed insert trong một commit | "Thiếu cấu hình = lỗi" ép lúc **gọi** (ném `ScoreConversionException` nêu tên skill), không phải lúc **lưu** |

## 2. Quyết định còn treo

| # | Câu hỏi | Cần ai quyết |
|---|---|---|
| P1 | Xoá thêm gì ngoài score-conversion? (mentor, credit/wallet, complaints, saved questions, weakness page…) | Chủ dự án |
| P2 | Hỗ trợ cert khác **cấu trúc** (số kỹ năng khác, có/không Speaking) hay chỉ khác **tham số**? | Chủ dự án |
| P3 | Có giữ hệ thống credit/thanh toán không (điểm cộng demo payOS) hay bỏ phí và giữ ví? | Chủ dự án |

## 3. Artifact đang có

Trạng thái công việc và artifact: xem `docs/STATUS.md`. File này không chép lại.

## 4. Bug đã xác nhận (từ đọc code)

**Tổng hardcode: 166 dòng** — 16 BLOCKER, 82 WRONG_DISPLAY, 68 COSMETIC.

Chi tiết đầy đủ (path:line, giả định gì, vỡ khi nào) ở:
- `docs/audit/hardcode-services.md` — 82 dòng
- `docs/audit/hardcode-views-js.md` — 84 dòng

Dưới đây là **16 BLOCKER**, xếp theo độ chết người. Đây là những chỗ *ngừng hoạt động im lặng* với cert mới, không chỉ sai nhãn.

### 16 BLOCKER

| # | Chỗ | Vỡ thế nào |
|---|---|---|
| K1 | `docs/database-schema.sql:404` `CK_Practice_Modality` | `PracticeSessions.Modality` **chỉ cho `listening, reading`** → cert chỉ có Speaking/Writing **không luyện được** |
| K2 | `SpeechSuperClient.cs:108` | `test_type = "ielts"` cho mọi bài speaking |
| K3 | `SpeechSuperAiGrader.cs:77` | Mọi tiêu chí chia `9.0` (thang IELTS) — cert khác thang đều sai tỉ lệ |
| K4 | `SpeakingPracticeService.cs:642` | `× 200` cứng cho mọi cert |
| K5 | `Modality.cs:10` + `ModalityDto.cs:11` | 4 modality đóng, cả C# lẫn wire |
| K6 | `CertificateService.Wizard.cs:29` | Allowlist 4 luật điểm, chép lại thành danh sách C# thứ hai |
| K7 | `LearningPathService.Generation.cs:63` | Band khởi đầu fallback `"A1"` → không khớp template nào |
| K8 | `LearningPathService.Generation.cs:77` | Band đích fallback `"B2"` → như trên |
| K9 | `QuestionImportService.QuestionValidation.cs:85` | Free-response chỉ nhận speaking/writing |
| K10 | `QuestionImportService.QuestionValidation.cs:92` | Short-answer chỉ nhận listening/reading |
| K11 | `Practice/Index.cshtml:42` | Empty-state khoá theo `ListeningSections.Count == 0 && ReadingSections.Count == 0` → cert chỉ có speaking/writing hiện "No questions to practice yet" |
| K12 | `AttemptViewModelMapper.cs:72` | Listening tìm bằng `SkillCode == "LIST"` → cert đổi code là hiện **0** |
| K13 | `AttemptViewModelMapper.cs:73` | Reading tìm bằng `SkillCode == "READ"` → như trên |
| K14 | `navigation.js:5` | `skillCode === 'LIST'` quyết định khoá điều hướng một chiều |
| K15 | `navigation.js:6` | `skillCode === 'READ'` quyết định nút Review Screen → cert code khác là **không bao giờ có review** |
| K16 | `AttemptEngine` aggregation | Chỉ 2 nhánh: `avg_half_step` và "còn lại" → `avg_whole` và `independent` **là giá trị giả** |

### Bug nghiệp vụ riêng (không phải hardcode)

| # | Bệnh | Chỗ |
|---|---|---|
| A1 | Cert mới có **0 dòng `ScoreBand`** → `BandCode = null` → `CK_Placement_DoneHasResult` **chặn mọi lần nộp placement** | `CertificateService.Wizard.cs` |
| A4 | Cert tự tạo có **rubric rỗng** → `maxScore = 0` → Writing/Speaking đóng góp **0** | không có admin UI cho `GradingCriterion` |
| B1 | Wizard preview tự chế **3 bảng band** không đọc DB; bảng `sum` mâu thuẫn `ScoreBands` seed thật | `certificate-wizard.js:659,665,671` |
| C6 | `ScoreBands` không validate phủ kín dải → chỗ hở bị **clamp im lặng** | `ScoreConversionService.Configuration.cs:179` |
| C7 | `ScoreScale` int lấy **dòng gần nhất**, decimal **nội suy** | `Scoring.cs:45` vs `:155` |
| D7 | Audit publish chỉ chặn `POST /publish`; dropdown Status đặt thẳng `Published` được | `_ExamEditorStep1.cshtml` |
| D8 | Không có preview exam dưới góc học viên | — |
| D9 | Không chặn sửa đề đã publish đã có attempt | — |
| E1 | `SpeechSuperAiGrader.cs:76` | thiếu điểm → mặc định `6.0` (midpoint IELTS) = **67% điểm** trên mọi cert |
| E2 | `AiGrader.cs:55` | `code.Contains("INT")` — substring, khớp cả `PRINT`/`POINT` |
| E3 | `PracticeService.Catalog.cs:32` + `Lifecycle.cs:31` | so `QuestionType` với `"writing"`/`"speaking"` — **không bao giờ khớp**, bộ lọc không làm gì |
| E4 | `simulator.css:232` | `repeat(@sim.Bands.Count, …)` là cú pháp Razor trong file CSS tĩnh — **luôn sai** |

## 5. Bối cảnh nhưng không phải bug (để không nhầm khi đọc code)

- `TargetBands` **không có** cột thời gian — D6 sẽ thêm.
- Placement **không adaptive** dù copy UI ghi "Adaptive".
- Placement **không bảo đảm 4 kỹ năng** — phụ thuộc đề admin soạn.
- **Không có per-question telemetry** (không timestamp, không đếm đổi đáp án). D7 phụ thuộc vào việc thêm gì.
- Lộ trình **không do LLM sinh** — template tra bảng + LLM chỉ viết lời bình.
- Step `mock_exam` trong lộ trình **không bao giờ** set done.

## 6. Cách dùng file này

- Trước khi làm bất cứ việc gì: đọc §1 để biết cái gì đã chốt.
- Sau mỗi quyết định mới: thêm một dòng vào §1 hoặc §2, kèm ngày và lý do.
- Đối chiếu mọi đề xuất (research, agent, người) với §1. Đề xuất nào đòi đảo D1–D10 thì bỏ, không implement.
- Muốn biết ticket nào đang làm: đọc `docs/STATUS.md`, không phải file này.
