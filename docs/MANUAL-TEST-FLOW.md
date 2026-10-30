# MANUAL TEST — FLOW & PROCESS

Luồng kiểm thử thủ công cho **ExtraEfficiency** (TOEIC/IELTS/VSTEP practice & mock-exam platform).
Mọi route, label, id phần tử và con số dưới đây **đã được verify trên runtime thật**
(`localhost:5000` WebMVC + `localhost:5001` WebAPI, DB `ExtraEfficiency`, payment `mock`, AI `fake`)
tại thời điểm viết. Không có bước nào là suy đoán từ code.

> Điều kiện tiên quyết: đọc `docs/MANUAL-TEST-CREDENTIALS.md` để lấy account/password
> và chạy `./scripts/seed-manual-test.sh fresh` trước mỗi lượt test.

---

## 0. Chuẩn bị môi trường (bắt buộc, mỗi lượt)

```sh
cd D:/1.Work/Programs/c#/toeic-practice-ui/toeic-practice-ui
./scripts/seed-manual-test.sh fresh      # stop → reset DB → schema → 2 seed → build → start
./scripts/seed-manual-test.sh status     # ports + balance + row counts
```

Hai app phải đang chạy. Sau `fresh`, kiểm tra nhanh:

```sh
curl -s http://localhost:5001/healthz              # -> Healthy
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5000/auth/sign-in   # -> 200
```

Điều kiện "sạch" để kết quả có thể so sánh giữa các lượt:

| Hạng mục | Giá trị đúng |
| :--- | :--- |
| Port 5000 (MVC) / 5001 (API) | LISTENING |
| `GET /healthz` | `Healthy` |
| Credits `learner@demo.local` | **8** |
| Credits `learner.fresh@demo.local` | **0** |
| Users / Certificates / Exams / Questions | 4 / 5 / 18 / 98 |
| CreditTransactions (baseline) | 7 |
| ExamAttempts (baseline) | 2 |
| PracticeSessions (baseline) | 3 |
| Payment gateway | `mock` |
| AI provider | `fake` (deterministic, không tốn quota) |

Runtime mode là **bắt buộc**: `appsettings.json` chọn `"Gateway": "payos"` với credential
placeholder, nên nếu chạy `dotnet run` trần thì checkout trả 500. Chỉ dùng script.

**Thứ tự thực hiện:** Phase 1 (guest) → 2 (admin authoring) → 3 (learner exam) →
4 (practice & AI) → 5 (commerce) → 6 (admin back-office). Phase 3 (mock exam) và 5 (commerce)
làm hao/đổi credit, nên chạy các phase đọc-only trước và chạy lại `fresh` sau khi xong
những phase đó nếu muốn số liệu sạch cho phase tiếp theo.

---

## 1. Bản đồ luồng (flow map)

```
GUEST ──▶ / ─▶ /certificates ─▶ /pricing ─▶ /auth/register ─▶ (mail-drop) ─▶ /auth/verify-email
                                              └─▶ /auth/sign-in ─┐
                                                                 │
ADMIN (admin@demo.local) ────────────────────────────────────────┤
  /Admin ─▶ /admin/certificates ─▶ /admin/certificates/wizard    │
        ─▶ /admin/score-conversion?cert=<CODE>                   │
        ─▶ /admin/exams ─▶ /admin/exams/{id}/studio              │
                          ├─ Quick Ingest (modal)                │
                          ├─ Audio Timestamp Slicer              │
                          └─ Export ZIP / JSON                   │
        ─▶ /admin/users /admin/orders /admin/complaints          │
        ─▶ /admin/lessons /admin/mentors /admin/score-conversions│
                                                                 │
LEARNER (learner@demo.local) ◀───────────────────────────────────┘
  /Dashboard
  ├─ /certificates ─▶ switch active certificate (TOEIC/IELTS/VSTEP/TOEIC_CAT/TOEIC_SW)
  ├─ MOCK EXAM:  /exams ─▶ /exams/{id}/instructions ─▶ [POST /exams/{id}/start]  (−1 credit)
  │                └─▶ /exams/preflight/{attemptId} ─▶ /exams/take/{attemptId}
  │                      └─▶ [submit] ─▶ /exams/result/{attemptId}
  │                            ├─▶ /exams/review/{attemptId}
  │                            └─▶ /exams/insight/{attemptId}   (AI insight, async)
  │                └─▶ /exams (card "In progress" ─▶ nút `Continue` để vào lại sitting)
  ├─ PLACEMENT (free): /placement ─▶ /placement/start ─▶ /exams/preflight/{aid}?placementId=
  │                └─▶ /placement/result/{placementId}
  ├─ PRACTICE (free):  /practice ─▶ [POST /practice/start] ─▶ /practice/{sessionId}
  │                └─▶ /practice/{id}/summary ─▶ /practice/insight/{id}
  ├─ FREE RESPONSE:  /writing/{id}/instructions ─▶ /writing/take/{aid} ─▶ /writing/pending/{aid}
  │                 └─▶ /writing/review/{aid}      (−1 credit at submit)
  │                  /speaking/{id}/instructions ─▶ /speaking/take/{aid} ─▶ /speaking/review/{aid}
  ├─ LEARNING:  /placement/target-band ─▶ /learning-path ─▶ /learning-path/modules/{id}
  │             └─▶ /learning-path/upgrade (−5 credits)
  ├─ INSIGHTS:  /weakness-analysis · /learner/insights/dashboard · /learner/insights/path
  ├─ MENTORS:   /mentors ─▶ /mentors/{id} ─▶ /mentors/{id}/confirm ─▶ /mentors/bookings (−1 credit)
  ├─ COMMERCE:  /pricing ─▶ /checkout?packageId=N ─▶ /checkout/mock-gateway ─▶ /payment/result
  └─ ACCOUNT:   /wallet · /profile · /profile/password · /saved-questions
```

---

## 2. Phase 1 — Guest & Access Control

Mở cửa sổ **Incognito** (không có cookie).

| # | Bước | Kỳ vọng |
| :-- | :--- | :--- |
| 1.1 | `GET /` | 200, landing page render |
| 1.2 | `GET /certificates` | 200, hiện 5 certificate (TOEIC, IELTS, VSTEP, TOEIC_CAT, TOEIC_SW) kèm cấu trúc section từng cái |
| 1.3 | `GET /pricing` | 200, 3 package: Starter 5cr/100.000₫, Intensive 10cr/190.000₫ (featured), Master 20cr/360.000₫ |
| 1.4 | `GET /Dashboard` | **302 → `/auth/sign-in?ReturnUrl=%2FDashboard`** |
| 1.5 | `GET /wallet`, `GET /admin` | 302 → sign-in với `ReturnUrl` tương ứng |
| 1.6 | `GET /auth/register`, `/auth/forgot-password` | 200, form render |
| 1.7 | Đăng ký 1 account mới | 200/302; file activation ghi vào `mail-drop/` (không có SMTP thật). Đăng nhập khi chưa verify → bị chặn ở checkout với `EMAIL_NOT_VERIFIED` |

**Điểm cần bắt lỗi:** bất kỳ route protected nào trả 200 cho guest = hổng authorization.
Redirect thiếu `ReturnUrl` = mất ngữ cảnh sau khi đăng nhập.

---

## 3. Phase 2 — Admin authoring (`admin@demo.local`)

Đăng nhập → tự chuyển tới **`/Admin`**.

### 3.1 Certificate Blueprint Wizard — `/admin/certificates/wizard`

4 step, mỗi step phải giữ state khi bấm Back/Next.

| Bước | Việc làm | Kỳ vọng |
| :-- | :--- | :--- |
| Step 1 | Chọn preset **IELTS Academic** | Auto-fill 4 skills, `avg_half_step`; phải đổi **Code** thành mã chưa tồn tại (vd `IELTS_PRO`) vì `IELTS` đã có → trùng code phải bị từ chối |
| Step 2 | Xem 4 dòng skill | `LIST/30`, `READ/60`, `WRIT/60`, `SPEA/14`; `+ Add Skill` thêm dòng, `Remove` xoá được |
| Step 3 | Xem section cards | Mỗi card đủ 5 hàng: code/weight/expected, navigation+layout, timing policy, slot blueprint, allowed components + sitting-room tools |
| Step 4 | Sửa `SAMPLE CORRECT` | `OVERALL ESTIMATED SCORE`, `MATCHED PROFICIENCY BAND`, skill breakdown **tính lại live** không cần submit |
| Save | `[LƯU] Save & Persist` | 302 → `/admin/certificates` + banner xanh `Certificate '…' (CODE) saved with N skills and M sections.` |

**Kiểm tra âm:** bỏ trống Code, Code trùng, Duration = 0, Expected Items âm → phải chặn ở server,
không chỉ ở client.

### 3.2 Score Conversion — `/admin/score-conversion?cert=TOEIC_CAT`

- Dropdown certificate đổi được giữa 4-5 cert.
- Mỗi skill có bảng `Correct answers (Raw)` → `Scaled Score`.
- Bảng **Proficiency Bands / CEFR Reference** hiển thị đúng band của cert đang chọn.
- Sandbox `LIVE SCORE PREVIEW`: sửa raw → `SIMULATED TOTAL SCORE` và `MATCHED PROFICIENCY BAND` đổi ngay.
- `Save table` → alert xanh.
- **Kiểm tra âm:** raw trùng lặp, scaled giảm dần, band chồng lấn → phải báo lỗi rõ ràng.

### 3.3 Exam Studio — `/admin/exams/{id}/studio`

Layout 3 cột. Dùng **ExamId 17** (`TOEIC CAT Benchmark Test 1`) cho composite widgets và
**ExamId 1** (`ETS 2024 — Test 1`) cho audio slicer.

| Kiểm tra | ExamId 17 | ExamId 1 |
| :--- | :--- | :--- |
| Cột trái (outline) | `Reading 7/45`, `P5_CAT 6/20`, `P7_CAT 1/25`, `P1_CAT 0/6`, `P2_CAT 0/25` | `Listening 4/100`, `Reading 5/100`, `P1 1/6` … `P7 1/54` |
| Cột giữa | `Curriculum Rail 7/11 Qs`, danh sách Q1–Q7 đúng thứ tự | 9 group tương ứng 9 câu |
| Cột phải | Nút `Ready`, `Quick Ingest`, `Settings` | như trên |

**6 component type** trong `P5_CAT` (Q89–Q94) — verify đúng widget và đúng ràng buộc:

| Question | Type | Ràng buộc phải thấy |
| :--- | :--- | :--- |
| Q89 | `mcq` | 4 lựa chọn A–D |
| Q90 | `mcq_3` | **chỉ 3** lựa chọn A–C, không có D |
| Q91 | `multi_select` | `MaxSelections = 2`, chọn cái thứ 3 bị chặn |
| Q92 | `inline_completion` | `AcceptedAnswersJson = ["15","fifteen"]` |
| Q93 | `dropdown` | select A/B/C |
| Q94 | `drag_drop` | slot + chip kéo thả |
| Q95 | `mcq` (P7_CAT) | thuộc **Unit 2** |

**Autosave:** sửa `Explanation` của Q89 → `#autosave-status` đổi `Saving...` rồi `Saved at HH:MM:SS`
sau ~1.5s. F5 phải còn nội dung vừa sửa.

### 3.4 Quick Ingest modal

- `+ Quick Ingest` → modal có `Target Section`, mode `Append` / `Replace section`.
- `Load Section Sample` → textarea đầy + bảng **Pre-Flight Blueprint Preview** badge xanh `READY`.
- Chọn `P2_CAT` (3 options) rồi dán câu **4 options** → badge đỏ `MISMATCH`, nút
  `Commit Questions to Section` **disabled**.
- Quay lại `P5_CAT` → badge xanh, commit → reload studio, số câu tăng.

### 3.5 Audio slicer & Export — `/admin/exams/1/studio`

- Chọn `P3 — Conversations` → thấy ô `Section Master Audio & Visual Timestamp Slicer`.
- Path audio `/audio/toeic/test1-p3.mp3`; play, `[ Set Start ]`, `[ Set End ]` → `Start (ms)`/`End (ms)`
  của group tương ứng cập nhật, `Save Timestamps` giữ lại sau F5.
- Export: `GET /admin/exams/1/export?format=json` → 200 `application/json` (~19 KB);
  `?format=zip` → 200 `application/zip` (~2.7 KB). Đã verify cả hai.

### 3.6 Back-office

| Route | Kỳ vọng |
| :--- | :--- |
| `/admin/exams` | Bảng 18 exam, filter theo Cert/Purpose/Status, cột `Questions` dạng `7/11` (thực tế/blueprint). `Import Exam (.zip / .json)` → `/admin/imports` → 302 `?import=True`; `Generate Exam (AI)`; `Create new exam` |
| `/admin/exams/{id}/questions` | **302 → `/admin/exams/{id}/studio`** (route cũ, đã hợp nhất vào studio) |
| `/admin/exams/manifest-template?certId=1` | 200 `application/json` (~34 KB) — manifest mẫu cho cert đó. Gọi **không có** `certId` → 302 về `/admin/exams` kèm `TempData["Error"]`, đúng thiết kế |
| `/admin/users` | 3 học viên (admin không tính), cột Credits/Best score/Last sign-in/Status; `Edit credits` + `Lock` |
| `/admin/orders` | 2 order `paid` mock; có reconcile |
| `/admin/complaints` | 2 complaint seeded |
| `/admin/lessons` | 13 lesson |
| `/admin/mentors` | 2 mentor profile |

---

## 4. Phase 3 — Learner exam sitting (`learner@demo.local`)

### 4.1 Chọn certificate

`/certificates` → bấm `Switch to <CODE>`. Đổi cert **reset toàn bộ ngữ cảnh**:
practice bank, learning path, placement, weakness đều theo cert đang active.

- Cert hiện tại quyết định danh sách exam ở `/exams`. Exam của cert khác
  → `/exams/{id}/instructions` trả **302 về `/exams/index`** (đã verify: khi active = TOEIC
  thì `/exams/17/instructions`, `/exams/18/instructions`, `/exams/14/instructions`
  đều 302, `/exams/1/instructions` = 200).
- Exam `purpose=placement` (`2`, `14`, `7`) luôn 302 ở `/exams/{id}/instructions` vì chúng
  đi qua `/placement` — đúng thiết kế, không phải cert isolation.
- **Đây là cách verify cert isolation** — không có nội dung cert nào rò sang cert khác.

### 4.2 Luồng mock exam chuẩn (ExamId 1, TOEIC)

1. `/certificates` → `Switch to TOEIC`.
2. `/exams/1/instructions` → tick `I have read the instructions and am ready to begin`
   (nút `Start` **disabled** cho tới khi tick) → bấm **`Start test — uses 1 credit`**.
3. 302 → `/exams/preflight/{attemptId}?id=1`. Kiểm tra:
   - `STEP 1 Audio Asset Pre-Buffering` — progress tới `100%` / `100% BUFFERED`.
   - `STEP 2 Speaker & Headphone Sound Check` — `Play Test Chime` → `TEST READY`.
   - Đủ 2 bước thì notice xanh `All Pre-flight Requirements Satisfied` và `Start Test →` mở.
     Thiếu 1 bước → `Start Test →` **disabled**.
4. Vào `/exams/take/{attemptId}`. Kiểm tra:
   - Topbar không còn placard mã dự thi — mã `EX-` đã bỏ (D4); resume chỉ qua nút `Continue`.
   - Modal `Aa Cỡ chữ & Tương phản`: 3 mức cỡ chữ + 3 chế độ tương phản đổi được.
   - Volume slider đổi % và **persist qua F5** (localStorage).
5. Trả lời và kiểm tra mechanics đặc thù:

| Câu | Section | Cơ chế | Kỳ vọng |
| :-- | :--- | :--- | :--- |
| Q1 | P1 | `AUDIO_DRIVEN`, `HideOptionText=1` | 4 placard A–D **không có text**; không có nút `Previous`; chọn đúng C = "A woman is writing on a whiteboard." |
| Q2 | P2 | `OptionCount=3` | **chỉ 3 placard** A–C, không có D; đáp án đúng B |
| Q3 | P3 | `ClusterSize=3` | render cluster card (stimulus + 3 câu), đáp án B = "By tomorrow noon" |
| Q4 | P4 | `AUDIO_DRIVEN` | đáp án B = "Routine aircraft inspection" |
| Q5–Q7 | P5 | `FULL_SANDBOX` | đi lại tự do; đáp án C, D, B |
| Q8 | P6 | text completion | đáp án A = "have received" |
| Q9 | P7 | reading | đáp án B = "To notify employees of maintenance work" |

6. **Resume bằng nút `Continue`:** trong sitting bấm `Exit` → `Exit (resume later)` → về `/exams`.
   Card exam đang làm hiện badge `In progress` và **một nút `Continue` duy nhất** — bấm vào phải
   về đúng câu đang dở và đúng số giây còn lại. Không còn placard mã dự thi; sitting room và card
   không còn dẫn tới form `Khôi phục phiên thi` (trang `/exams/resume` vẫn còn nếu gõ thẳng URL).
7. **Navigation lock (một chiều):** P1–P4 là `AUDIO_DRIVEN` nên không có nút `Previous`; sửa URL
   thành `?q=` của câu trước (hoặc bấm Back) phải bị đẩy về câu đang active, **không** được nhảy lùi.
   Q5–Q7 (`FULL_SANDBOX`) thì đi lại tự do.
8. Submit → `/exams/result/{attemptId}`. Result page hiển thị total scaled, điểm từng skill,
   band CEFR, và so sánh với attempt trước.

**Kiểm tra credit invariant:** mỗi lần `start` mock exam trừ đúng **1 credit**, hiển thị ngay
trên `/wallet` dưới reason `MockExamStart`. Start 2 lần = 2 dòng ledger. Hết credit → chặn
với `InsufficientCredits`, **không** tạo attempt.

### 4.3 CAT — irreversible unit lock (ExamId 17, TOEIC_CAT)

1. `Switch to TOEIC_CAT` → `/exams/17/instructions` → preflight → take.
2. Palette câu hỏi: Q1–Q6 (Unit 1) mở, **Q7 (Unit 2) khoá**.
3. Trả lời đủ 6 type (bảng ở mục 3.3), trong đó:
   - Q91 `multi_select`: tick A+B, tick C thứ 3 → **phải bị chặn**.
   - Q92 `inline_completion`: gõ `15` hoặc `fifteen` đều accepted.
4. `Bảng xem lại (Review Screen)` → 3 stat box `Answered`/`Flagged`/`Unanswered` + 4 filter
   (`Jump to First Unanswered`, `Review Only Unanswered`, `Review Only Flagged`, `Show All`).
5. Bấm `Hoàn thành Giai đoạn 1 (Finish Unit 1)` → modal `XÁC NHẬN KHÓA GIAI ĐOẠN` với cảnh báo đỏ
   → `Xác nhận khóa và tiếp tục`.
6. **Verify khoá thật:** cố quay lại Q1–Q6 → phải bị chặn vĩnh viễn (reload, back button, nhập URL trực tiếp).
7. Q95 (Unit 2) đáp án A = "Reduced shipping overhead costs" → submit → `7/7`, scaled **850**, band `CAT_B2`.

### 4.4 IELTS split-screen & annotations (ExamId 6)

1. `Switch to IELTS` → `/exams/6/instructions` (200; exam 1/17/18 phải 302).
2. Preflight → take.
3. Bôi đen text trong stem/passage (`.ee-tokenized-text`) → popover 3 nút `Tô sáng` / `Ghi chú` / `Xóa`.
4. `Tô sáng` → chữ chuyển Sign Gold. `Ghi chú` → modal, lưu → xuất hiện pin `[NOTE]`.
5. **F5 → highlight + note phải còn** (persist qua `CandidateNotesJson`).
6. Q22 (section `PAS`) → layout chuyển `#exam-main-layout.exam-layout--split` (50/50),
   passage và question cuộn độc lập.

### 4.5 Placement test (miễn phí)

- `/placement` → nút `Start` (`POST /placement/start`). `examId` phụ thuộc cert đang active:
  **TOEIC → `examId=2`** (TOEIC Placement Test — Standard), **IELTS → `examId=7`** (IELTS-DIAG-01).
- 302 → `/placement/take/{id}` → tiếp tục 302 sang `/exams/preflight/{aid}?placementId={id}`
  → take như exam thường (cùng AttemptEngine, chỉ khác là không trừ credit).
- `/placement/result/{placementId}` hiển thị band + total + thang CEFR + khoảng cách tới band kế.
- **Invariant:** placement là 100% free — chạy xong `/wallet` **không** được xuất hiện dòng
  `ai_grading` hay `mock_exam_start` nào. Đã verify ledger không tăng.
- Exam `purpose=placement` **không** xuất hiện ở `/exams` và `/exams/{id}/instructions`
  trả 302 → đúng thiết kế (placement có luồng riêng), không phải lỗi.
- **Navigation lock giống hệt mock exam:** section `AUDIO_DRIVEN` / `SYSTEM_PACED_AUTO` không
  render nút `Previous`; gõ URL `?q=` của câu trước hoặc bấm Back phải bị đẩy về câu đang active
  và thanh địa chỉ phải hiện đúng số câu server trả về (không lệch với nội dung đang render).

---

## 5. Phase 4 — Practice, free-response & AI

### 5.1 Practice (miễn phí)

`/practice` → form gồm `QuestionCount` (10/20/40/100), `QuestionSource`
(`all` / `wrong_before` / `unseen`), `FeedbackMode` (`immediate` / `end`), và các nút
`SectionId` (mỗi section 1 nút). Bấm section → `POST /practice/start` → `/practice/{sessionId}`.

Trong phiên practice, verify:

| Tính năng | Kỳ vọng |
| :--- | :--- |
| `[-5s]` rewind | playhead lùi 5s, clamp ở `StartMs` của câu |
| `[Xem Lời thoại / Transcript]` | drawer mở/đóng |
| `Controls ?` (hoặc phím `?`) | modal phím tắt: `1`–`4`, `Enter`/`Space`, `H`, `S`, `K`, `N`/`P` |
| `H` | hiện hint |
| `S` | lưu câu vào saved bank |
| `2` + `Enter` | chọn B rồi check answer |
| Explanation accordion | mở/đóng được |
| Sidebar filter | `All` / `Incorrect` / `Saved` |

Form `/practice` thực tế dùng `name="SectionId"` (PascalCase) trên từng nút section, và các
select `QuestionCount` / `QuestionSource` / `FeedbackMode`. Đã verify: `POST /practice/start`
với `SectionId=16, QuestionCount=10, QuestionSource=all, FeedbackMode=immediate`
→ 302 `/practice/{sessionId}` và tạo row `in_progress` trong `PracticeSessions`.

Kết thúc → `/practice/{id}/summary` (Correct/Incorrect/Skipped, accuracy, hint usage)
→ `/practice/insight/{id}`.
**Invariant:** practice **không** ghi ledger. Kiểm tra `/wallet` sau phiên: số dư không đổi.

### 5.2 Writing practice — luồng AI grading (đã verify end-to-end)

1. `/writing/{examId}/instructions` → `POST /writing/{examId}/start` → 302 `/writing/take/{attemptId}`.
   ExamId dùng được: **5** = `ETS TOEIC Writing Practice Test 1` (TOEIC, 2 câu, target 8–35 từ),
   **9** = `Cambridge IELTS 18 — Writing Practice Test 1` (IELTS).
   ExamId 5 chỉ start được khi cert active = **TOEIC**; khi active = IELTS thì `/writing/5/instructions`
   trả 302 (cert isolation áp dụng cho cả free-response).
2. Gõ bài (word counter `#word-count`, `#word-meter-fill`, `#word-status`, `#timer-display` cập nhật live).
   `Save draft` (`#btn-save-draft`) → `POST /writing/save-draft` JSON body
   `{attemptId, questionId, responseText}` + header `RequestVerificationToken`
   → trả `{freeResponseId, wordCount, savedAt}`.
3. `Finish and submit for AI grading` (`#btn-open-submit-modal`) → modal `#submit-modal` xác nhận
   → `POST /writing/submit/{attemptId}` → 302 `/writing/pending/{attemptId}`
   → poll `/writing/status/{attemptId}`.
4. **Credit:** trừ đúng **1 credit** ở bước submit, reason `ai_grading` (không trừ lúc start).
   **Đã verify thật:** submit → txn `ai_grading -1`.
5. Worker `AiGradingBackgroundService` (interval 2s) xử lý queue `IX_AiGradings_Queue`,
   ghi `AiGradings` + `AiGradingScores`, attempt chuyển `submitted → graded`.
6. `/writing/review/{attemptId}` hiển thị điểm tổng, per-criterion rubric, số câu đã làm
   (ví dụ đã verify: `17.2 / 40`, `1 of 2 questions answered`, `1 credit used`).
   Với `AI_PROVIDER=fake` kết quả **deterministic** → so sánh được giữa các lượt.

### 5.3 Speaking practice

`/speaking/4/instructions` → take. Verify pacer `SYSTEM_PACED_AUTO`: đếm ngược prep
(`#speaking-prep-placard`) → beep → đếm ngược record (đỏ) + level meter → tự upload →
`SPEAKING AUDIO VERIFICATION` phát lại được. Không có nút `Previous`/`Next`.
Credit trừ 1 ở submit, như writing.

### 5.4 Async AI artifacts

- `/exams/insight/{attemptId}`, `/practice/insight/{id}`, `/learner/insights/dashboard`,
  `/learner/insights/path` — insight sinh **bất đồng bộ**; trang phải chịu được trạng thái
  `pending`/`running` rồi tự cập nhật khi `done`.
- `/weakness-analysis`: hiển thị `Combined from N graded exams and M practice sessions`,
  overall accuracy, strongest/weakest section, trend, và recommendations có nút
  `Mark resolved`. Đây là SQL view (`vw_UserSectionAccuracyAll`, `vw_UserCategoryAccuracy`),
  không phải app-computed.
- `/saved-questions`: phân biệt `saved automatically from wrong answers` và `saved by you`
  (baseline: 2 câu — 1 auto, 1 thủ công), filter theo source/section/type.

---

## 6. Phase 5 — Commerce & credit ledger

Luồng chuẩn (dùng `learner.fresh@demo.local`, 0 credit):

1. `/pricing` → chọn package → `/checkout?packageId=1`.
2. Bấm tạo order → `POST /checkout` → redirect `/checkout/mock-gateway?orderCode=DH-YYYYMMDD-NNNN`.
3. Mock gateway có **4 nút**, kết quả đã verify từng cái:

| Nút | Status sau | Credit |
| :--- | :--- | :--- |
| `Payment successful` | `paid` | +credits, `/payment/result` báo thành công, `/wallet` tăng ngay |
| `Payment failed` | `failed` (error 24) | không đổi |
| `Slow gateway (reconcile)` | `reconciling` | chưa cộng — dùng để test reconcile ở `/admin/orders` |
| `Order expired` | `expired` | không đổi |

**Ràng buộc phải thấy:** mỗi learner chỉ có **1 order mở** tại một thời điểm
(`order_already_open` → 409). Sau nhánh `reconciling` phải đóng/expire order đó mới tạo được order mới.

**Invariant credit (quan trọng nhất của hệ thống):**
- Balance = `SUM(Delta)` trên `CreditTransactions`, **không có cột balance lưu sẵn**.
  Xác nhận: `/wallet` hiển thị `N purchased − M used = remaining`, khớp từng dòng ledger.
- "Block before you charge": mọi precondition check xong mới ghi debit. Ví dụ hết credit →
  chặn ở start, **không** tạo bảng ghi debit rồi mới rollback.
- Các reason debit: `mock_exam_start` (−1), `ai_grading` (−1), `mentor_booking` (−1),
  `premium_path` (−5). Reason credit vào: `purchase`, `admin_grant`, `booking_cancel`,
  `admin_revoke` (−, dùng cho revoke). Danh sách đầy đủ nằm ở CK constraint
  `CK_CreditTxn_Reason`.

---

## 7. Phase 6 — Mentor booking

`/mentors` → `View profile` → `/mentors/{id}` chọn slot → `GET /mentors/1/confirm?slotId=N`
→ trang confirm hiển thị `Cost: 1 credit`, `Balance: N credits` → xác nhận
(`POST /mentors/{id}/confirm`) → `/mentors/bookings`.

- Verify ledger: −1 credit reason `mentor_booking`, gắn `BookingId`. **Đã verify thật:**
  book slot → txn `mentor_booking -1 BookingId=3`, booking status `confirmed`.
- Cancel → hoàn credit reason **`booking_cancel`** (+1, cùng `BookingId`), booking → `cancelled`.
  **Đã verify thật:** ledger có cặp `mentor_booking -1` / `booking_cancel +1` cho cùng booking.
  Cancel lần 2 phải bị chặn (`BookingAlreadyRefunded`).
- Rate → `POST /mentors/bookings/{id}/rate`, review xuất hiện trên trang mentor.
- **Kiểm tra âm:** book khi balance < price → chặn, không tạo booking.
- Trạng thái rỗng: với cert IELTS, `/mentors` hiện `No mentors available` (mentor chỉ seed cho TOEIC) —
  đây là hành vi đúng, không phải bug.

---

## 8. Ma trận phủ (coverage matrix)

Chạy hết bảng này là phủ đủ các invariant kiến trúc trong `AGENTS.md`:

| Invariant | Cách verify |
| :--- | :--- |
| Cert isolation | Switch cert → exam của cert khác trả 302; practice/weakness/path đổi theo cert |
| Credit = SUM(ledger) | `/wallet` khớp từng dòng `CreditTransactions`; không có cột balance |
| Block before charge | Hết credit / thiếu điều kiện → chặn ở start, không có dòng debit |
| Free diagnostic loop | Practice + placement + weakness **không** sinh ledger row |
| AI grading seam | Chỉ `AiGradingBackgroundService` chạm queue; writing/speaking service không poll |
| Attempt engine seam | Mock + placement cùng đi qua `/exams/preflight` → `/exams/take`; khác nhau chỉ ở credit & target band |
| MVC proxies API | Browser không gọi `:5001`; JWT nằm trong HttpOnly cookie; mọi AJAX qua MVC + anti-forgery |
| Practice retention | Standalone session vượt cap 20/cert bị prune FIFO; session thuộc learning path không bị prune |

---

## 9. Quy trình ghi nhận & xử lý lỗi

1. **Trước khi test:** `./scripts/seed-manual-test.sh fresh` → DB về baseline. Không test trên
   DB đã bẩn vì số credit/history sẽ lệch.
2. **Khi gặp lỗi:** ghi lại 4 thứ — URL đầy đủ, account đang dùng, cert đang active, thao tác cuối.
3. **Lấy stack trace server-side** (browser không thấy):
   ```sh
   ./scripts/seed-manual-test.sh errors
   ```
   grep `^(fail|crit):` trong `logs/manual/{api,mvc}.log`.
4. **Xác nhận lỗi thuộc tầng nào** trước khi báo:
   - 500 từ MVC kèm `Contracts.ApiException: API error 500` → lỗi ở WebAPI.
   - 200 nhưng UI sai → lỗi Razor/JS.
   - 302 loop → lỗi cookie/auth middleware.
5. **Kiểm tra DB trực tiếp** để phân biệt "không lưu" và "lưu nhưng không hiển thị":
   ```sh
   sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -h -1 -W \
     -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM AiGradings;"
   ```
6. **Báo lỗi** theo mẫu trong `bug-report-cho-team.md`; nếu lỗi từ crawl-finding thì theo
   quy trình `AGENTS.md` §7: Red test → Green → re-walk `crawl-env.sh fresh` → root cause trong PR.

---

## 10. Checklist chốt một lượt test

```
[ ] seed-manual-test.sh fresh → status khớp baseline (8 credits, 4/5/18/98)
[ ] Phase 1: guest bị chặn ở mọi route protected, có ReturnUrl
[ ] Phase 2: wizard 4 step giữ state, score preview tính live, save ra banner
[ ] Phase 2: studio autosave 1.5s, 6 component đúng ràng buộc, Quick Ingest chặn MISMATCH
[ ] Phase 2: export json + zip đều 200
[ ] Phase 3: cert switch đổi phạm vi exam (cert khác = 302)
[ ] Phase 3: preflight chặn Start khi thiếu bước
[ ] Phase 3: P1/P2 masked options đúng số lượng, cluster P3 render
[ ] Phase 3: resume bằng nút `Continue` trên card đang làm về đúng câu + đúng timer
[ ] Phase 3: AUDIO_DRIVEN không có `Previous`, sửa URL `?q=` cũ vẫn bị đẩy về câu active
[ ] Phase 3: CAT unit lock không thể quay lại
[ ] Phase 3: annotation persist qua F5
[ ] Phase 3: placement free, ledger không đổi
[ ] Phase 4: practice free, ledger không đổi
[ ] Phase 4: writing submit trừ 1 credit reason ai_grading, review có rubric
[ ] Phase 4: insight async tự chuyển pending → done
[ ] Phase 5: 4 nhánh mock gateway đúng status, credit chỉ cộng ở success
[ ] Phase 5: chỉ 1 order mở / learner
[ ] Phase 6: booking trừ 1 credit, cancel hoàn lại
[ ] errors: không có fail:/crit: mới
[ ] Kết thúc: chạy lại fresh để trả DB về baseline
```

---

## 11. Ghi chú vận hành

- **Real mode:** thêm `--real-ai` vào `start`/`fresh` để gọi SpeechSuper + Claude thật
  (tốn tiền, cần key trong `.env`). Kết quả sẽ **không** deterministic → không so sánh với
  baseline `fake`.
- **Real payOS:** đổi `Payment:Gateway=payos` + điền `PAYOS_*` thật. Không dùng cho manual test
  thường ngày.
- **Audio:** file seed nằm ở `src/WebMVC/wwwroot/audio/{toeic,ielts,vstep}/`. Upload mới ghi vào
  `App_Data/audio/` (gitignored).
- **Mail:** `mail-drop/` (gitignored) nhận activation/reset mail — không có SMTP.
- **Log:** `logs/manual/{api,mvc}.log`, ghi đè mỗi lần `start`.
