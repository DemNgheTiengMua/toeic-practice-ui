# UI/UX audit — chạy thật trên trình duyệt (2026-11-15)

Môi trường: `scripts/seed-manual-test.sh fresh` (LocalDB `ExtraEfficiency`, schema vừa load, seed 2 cert
TOEIC/IELTS, payment=mock, ai=fake), SPA ở `http://localhost:5000`, API `:5001`.
Tài khoản: `learner@demo.local`, `learner.fresh@demo.local`, `admin@demo.local` (đều `Demo1234!`).
Công cụ: Playwright MCP (Chromium 1440×900 + 390×844), `browser_evaluate` để đo computed style /
contrast / bounding box, không dựa vào cảm nhận.

Ảnh chụp: `docs/audit/live/` (32 ảnh). Báo cáo tĩnh trước đó: `docs/audit/ui-ux-heroi-audit-2026-11-14.md`.

---

## P0 — Crash trang, mất dữ liệu người dùng

### 1. Lưu goal ở bước 3 OOBE làm trắng cả trang
**Repro:** đăng nhập `learner@demo.local` → `/onboarding` (đang ở Step 3) → nhập Target Exam Date +
Weekly Study Hours → **Save goal & continue**.

**Kết quả:** API trả `200 OK` (`POST /api/learner/goal`), rồi React throw:
```
TypeError: Cannot read properties of undefined (reading 'code')
```
Toàn bộ `<main>` biến mất, chỉ còn khung `Something went wrong!` + nút `Show Error`.
Ảnh: `09-onboarding-crash.png`. Goal **đã** lưu ở server, nhưng người dùng thấy là hỏng — và nếu họ
nhập lại sẽ tạo trạng thái mâu thuẫn.

**Nguyên nhân (đã truy ra, không phải phỏng đoán):**
`OnboardingPage.tsx:126-129` ghi kết quả `SetGoalResultDto` vào cache dưới key của **một kiểu khác**:
```ts
queryClient.setQueryData(['learner', 'target-band', …], updatedView);  // SetGoalResultDto
```
`SetGoalResultDto = { userId, certId, targetBandCode, setAt, goal }`
nhưng chỗ đọc (`:487`) lại dùng `TargetBandViewDto`:
```tsx
Set your study goal · {view.activeCert.code}
```
`SetGoalResultDto` không có `activeCert` ⇒ `undefined.code`.

**Sửa:** đừng overwrite slot bằng type khác; `invalidateQueries({queryKey:['learner','target-band']})`
rồi để query tự fetch lại. Đây cũng là chỗ duy nhất trong app ghi đè cache bằng payload
không cùng shape (grep `setQueryData` chỉ có 3 chỗ, 2 chỗ còn lại đúng).

**Lỗi phụ đáng chú ý:** chính `learner.fresh@demo.local` (chưa làm gì) cũng đã crash ở bước này, và
sau crash thì `derivedStep` nhảy sang **Step 4** vì `hasTargetGoal` đã true — nên người dùng bị đẩy
tới bước "Generate Learning Path (5 credits)" mà chưa từng thấy bước 3 thành công.

### 2. Client cho phép 1–80 giờ/tuần, server chỉ nhận 0–25
**Repro:** Weekly Study Hours = `40` → Save → `400`:
```json
{"title":"Invalid goal","detail":"A week has to hold between 0 and 25 hours of study; 40 is not a pace anyone can keep.","code":"weekly_hours_too_high"}
```
Client: `OnboardingPage.tsx:634` `<Input type="number" min={1} max={80} step={0.5}>`,
và `handleSubmitGoal` (`:178`) tự chặn `<1 || >80`. Server chặn `>25`.
⇒ 26–80 là vùng "client nói OK, server nói không". Người dùng chỉ thấy một câu lỗi chung.
**Sửa:** một nguồn số duy nhất (dùng 25 ở client, hoặc trả `suggestedWeeklyHours` từ server — field này
đã có trong response goal).

### 3. Validation của form là native browser, không phải HeroUI
`<Input type="number" min max>` + `type="submit"` ⇒ trình duyệt chặn submit và bắn bubble mặc định.
Đo được: nhập `9999`, bấm Save → `document.activeElement` đổi nhưng form không submit,
`form.elements` không còn phần tử invalid, **không có `Alert`/`FieldError` nào hiện**;
`validationMessage = "Value must be less than or equal to 80."` (tiếng Anh của Chrome).
`FieldError` đã được import và đặt sẵn nhưng không bao giờ render vì native validation chặn trước.
⇒ Luồng lỗi trong app (`'Weekly study hours must be between 1 and 80.'`) là code chết.

### 4. Target Exam Date nhận giá trị rác
`<Input type="date">` không có `min`/`max`: gõ `2027-03-15` theo kiểu text cho ra `70315-02-02`
(ghi nhận `inputValue`), tức là gõ tay tạo ra năm 70315 mà **không** bị chặn.
Ngày thi là biến quyết định `Feasible / Stretch / Unrealistic` của cả lộ trình.

---

## P1 — Hiển thị sai dữ liệu (không crash, nhưng nói dối người học)

### 5. `WeaknessAnalysisPage` sai tên field ⇒ bảng trống có hình
Đây là phát hiện nặng nhất về dữ liệu. So sánh trực tiếp response và type frontend:

**`sections[]`** — response thật: `{sectionId, code, name, answered, correct, accuracyPercentage, isWeakest, isStrongest}`
Type `SectionAccuracyDto` trong `api.ts`: `{sectionId, sectionCode, sectionName, skillCode, skillName, answeredCount, correctCount, accuracyPercentage}`

⇒ 4 trường lệch tên (`code`→`sectionCode`, `name`→`sectionName`, `answered`→`answeredCount`,
`correct`→`correctCount`), 2 trường type có mà response không có (`skillCode`, `skillName`).
`accuracyPercentage` thì **khớp**.

**`topics[]`** — response thật: `{topicId, topicCode, topicName, modality, levelCode, answered, correct, accuracyPercent, isWeakest, observationFloor, lessonId}`
Type `TopicAccuracyDto`: `{categoryId, topicCode, topicName, skillCode, skillName, answeredCount, correctCount, accuracyPercentage, levelCode, meetsFloor, isWeakest}`

⇒ 6 trường lệch (`topicId`→`categoryId`, `modality`→`skillCode`/`skillName`, `answered`→`answeredCount`,
`correct`→`correctCount`, `accuracyPercent`→`accuracyPercentage`, `observationFloor`→`meetsFloor`).

Hệ quả đo được trên `/weakness` (ảnh `14-weakness.png`), tất cả đều suy ra được từ đúng cặp field trên:
- Bảng topic: cột **Skill = "Skill"** (`skillCode` undefined → `formatModality(undefined)`), **Accuracy = "%"**
  (`accuracyPercentage` undefined trong khi response có `accuracyPercent`), **Evidence = "/ answered"**.
- Bảng "Accuracy by section": **7 dòng, mỗi dòng một chip đỏ `Weakest`, không tên section**.
  Lý do chính xác: `isWeakest = sec.accuracyPercentage < 50` với `0 < 50` → **true cho cả 7 section
  chưa làm** (vì `accuracyPercentage` khớp tên và bằng 0), còn `hasAnswered = sec.answeredCount > 0`
  = `undefined > 0` → false nên mọi dòng ghi `Not attempted yet`. Tức là nhãn "yếu nhất" bị gắn cho
  section chưa ai làm, và tên section biến mất vì `sectionCode`/`sectionName` undefined.
- "Frequently missed topics" in `B2 : % (/ correct)` — ba giá trị undefined.
- "What to practice first" mất section ưu tiên (`priorityPracticeSection` = `null` thật, nên fallback
  đúng, nhưng không thể hiện được section yếu nhất).

Đồng thời API trả `totalAnswered: 0, totalCorrect: 0, overallAccuracyPercentage: 0` trong khi
`topics[].answered = 4, correct = 1` và AI insight cùng trang nói "25% accuracy". ⇒ **ba con số mâu thuẫn
trên cùng một màn hình**. Ảnh `14-weakness.png` cho thấy `ACCURACY: 0%` cạnh "25% accuracy" trong
recommendation. Phần mâu thuẫn tổng số là lỗi service (topics lấy từ view khác với
`OverallAccuracyPercentage`), không phải lỗi UI.

`src/web/src/lib/api.ts` phải khớp `src/Contracts/WeaknessAnalysis/SectionAccuracyDto.cs` và
`TopicAccuracyDto.cs`. `tsc --noEmit` không bắt được vì response là `any` lúc runtime.

### 6. Bảng không có `<Table>` root ⇒ mất toàn bộ style HeroUI
So sánh computed style thật giữa hai trang:

| Trang | `table` class | `th` class | `th` padding | `th` font |
|---|---|---|---|---|
| `/wallet` (đúng) | `table__content` | `table__column` | `10px 16px` | 12px |
| `/exams/5` (**sai**) | *(rỗng)* | *(rỗng)* | **`0px`** | **16px** |
| `/exams/history` (**sai**) | *(rỗng)* | *(rỗng)* | **`0px`** | **16px** |

Nguyên nhân: hai file dùng `Table.ScrollContainer` + `Table.Content` nhưng **thiếu `<Table>` bọc ngoài**
(`MockExamConfirmPage.tsx:370`, `MockExamHistoryPage.tsx:219`). HeroUI gắn class qua context của root.
Tôi đã quét toàn bộ: 14 file có root, đúng 2 file này thiếu.

Hệ quả thấy được trên ảnh `22-exam-confirm.png` và `15-exam-history.png`:
- Header `Skill | Section | Questions | Duration` dính chữ `QuestionsDuration`.
- `ListeningREC` dính liền (skill + section code không có khoảng).
- Cột `Skills` trong Exam history **rỗng** vì `attempt.skillScores` = `[]` (cùng lỗi dữ liệu với
  dòng 2 của mục này) và cột `Actions` hai nút `Result`/`Review` sát nhau, bảng chỉ rộng 475px
  trong khung 1000px (cột `Exam` bị ép còn 118px nên "ETS 2024 — Test 1" tràn).

### 7. "Section & skill breakdown" rỗng, không có empty state
`/exams/result/2`: render heading "Section & skill breakdown" rồi **không có gì bên dưới**
(ảnh `16-exam-result.png`), khoảng trắng ~250px trước nút "Back to all exams".

Đối chiếu DB để biết nguyên nhân (không đoán): `AttemptSkillScores` chỉ có 2 dòng, đều cho
`AttemptId=3` (placement); **`AttemptId=2` (mock exam ETS 2024) không có dòng nào**. Đối chiếu chéo:
`/placement/result/1` (dùng cùng component pattern) render đủ Listening/Reading kèm raw score và
`ProgressBar` — nghĩa là **projection hoạt động đúng**, dữ liệu cho mock exam đơn giản là chưa có
(seed không tạo `AttemptSkillScores` cho attempt mock).
⇒ Đây là **hai lỗi riêng**: (a) seed thiếu dữ liệu per-skill cho mock exam, (b) UI thiếu empty state
khi `skillScores.length === 0` — một heading đứng trên khoảng trắng trông như trang bị lỗi, dù dữ
liệu có thể hợp lệ (đề không có phần kỹ năng nào được đo).

`ExamHistoryPage` cùng nguyên nhân: cột `Skills` rỗng vì `attempt.skillScores = []`, và cột đó không
có fallback text.

### 8. Chip "Weakest"/"Not measured yet" fail WCAG AA
Đo bằng canvas + alpha-composite thật (không ước lượng):

| Text | Màu | Nền | Tỉ lệ | Cần |
|---|---|---|---|---|
| `Not measured yet` (Dashboard) | `#f2b705` | trắng | **1.82:1** | 4.5 |
| `Cannot judge` (Dashboard) | `--muted` | `--surface-secondary` | 4.21:1 | 4.5 |
| `4 SKILLS` | `--muted` | `--surface-secondary` | 4.21:1 | 4.5 |
| `Medium` (chip severity) | `oklab(...)` | vàng nhạt | 4.25:1 | 4.5 |
| `Home / Mock exams` (breadcrumb) | `--muted` | trắng | 4.43:1 | 4.5 |

`text-warning` = `#f2b705` trên trắng là **1.82:1** — thấp hơn cả ngưỡng 3:1 cho chữ lớn. Đây là token
dùng cho ý nghĩa "cảnh báo" nên không thể bỏ; cần `--warning-text` tối hơn (ví dụ `#7a5c00`) cho
văn bản, giữ `#f2b705` cho nền/chấm. 50 chỗ dùng `text-warning` trong code.

**Dark mode tệ hơn:** các token brand là hằng số (`--accent: #0b4ea2`) nên trên nền `oklch(12%)`
chúng thành chữ xanh đậm trên nền đen:
`+ Top up` **1.96:1**, `NOT MEASURED YET` **2.48:1**, `Measure Writing →` **2.22:1**,
`Free analysis` **2.92:1**, `-5` (danger) **2.54:1**, `+1` (success) **2.68:1**.
Ảnh `19-dark-dashboard.png`: "Measure Writing →" gần như không đọc được.
`/practice` có **16** phần tử text fail ở dark (nhiều nhất app).

### 9. Select chứng chỉ trong header tràn khỏi header
Đo: `button` cao **36px**, `scrollHeight` **48px**, `white-space: normal` ⇒ text
`"TOEIC — TOEIC Listening & Reading"` xuống 2 dòng và tràn ra ngoài khung.
Ảnh `13-mobile-dashboard.png` (390px) cho thấy 4 dòng chữ `TOEIC / TOEIC / Listening / & / Reading`
đè lên dòng "Learner workspace"; ảnh desktop (`32-wallet.png`) cho thấy dòng thứ hai
`Listening & Reading` cắt ngang đường viền header. Header cao 57px nhưng nội dung 128px ở mobile.
**Sửa:** `Select.Trigger` cần `truncate`/`whitespace-nowrap` và chỉ hiển thị `cert.code`
(tên đầy đủ đã có trong popover).

---

## P2 — Dùng chưa hết HeroUI (đo được, không phải suy đoán)

### 10. Chỉ 32/73 component, và 41 component chưa từng import
Kiểm tra tự động trên 136 file: **32 used / 41 unused**. Đáng chú ý vì mỗi cái dưới đây đều có
chỗ dùng thật đã xác nhận trên trình duyệt:

- **`Toast`**: mount ở `main.tsx:23` nhưng `toast()` gọi **0 lần**. Trong khi 4 trang giữ
  `successMessage` trong state và render `Alert` vĩnh viễn (đã thấy "Recommendation marked as resolved"
  không tự tắt trên `/weakness`).
- **`Skeleton`**: 0 lần. 4 trang thay cả viewport bằng spinner (`DashboardPage.tsx:61` — đo được
  khung trống 400px rồi mới có nội dung).
- **`SearchField`**: 0 lần. `OrdersTab.tsx:131-144` tự dựng `TextField` + icon `Search` absolute +
  `Input`; `ExamsStudioPage.tsx:400`, `AdminUsersPage.tsx:316` tương tự.
- **`Tooltip`**: 0 lần, trong khi UI đang bày mã trần `LIST/READ/WRIT/SPEA`, `B1`, `OVERALL A2 · Listening`
  và chấm vàng "flagged" chỉ có `aria-hidden` (người dùng chuột không có cách nào biết).
- **`ProgressCircle`**: 0 lần. `DashboardPage.tsx:461` in `A2` + `Position: 40%` dạng chữ.
- **`Meter`**: 1 lần (đúng, ở `WritingPracticePage.tsx:380`) — nhưng tiến độ tuần trên dashboard
  (`goal.weekCompletedMinutes / weekPlannedMinutes`, có sẵn số) lại in thành **một câu văn**:
  `This week: 0m done of 0m planned`.
- **`Drawer`/`Sheet`**: 0 lần. Mobile nav là dải cuộn ngang: đo được `scrollWidth 1553 / clientWidth 390`,
  `overflow-x: auto`, **11 mục**, `nav[aria-label="Primary"]` có `display: none`. Không scrollbar,
  không gradient. Đây là điều hướng duy nhất trên mobile.
- **`Pagination`**: 1 lần đúng (`SavedQuestionsPage.tsx:545`), 3 chỗ tự viết Previous/Next không số trang
  (`OrdersTab.tsx:291`, `RefundsTab.tsx:427`, `ComplaintsTab.tsx:311`).
- **`Kbd`**: 0 lần. `PracticeSessionPage.tsx:328` có 6 phím tắt (`1-4/S/H/B/N/P`) **không một chỗ nào
  nói cho người dùng biết**.

### 11. Mock exam không có phím tắt, practice thì có
Đo trực tiếp trong sitting (`/exams/take/1003?q=1`): bấm `b`, `2`, `n` → **không có phản ứng gì**,
URL không đổi, không radio nào được chọn. `ExamSittingShell.tsx` không có `onKeyDown` nào
(grep = 0). Trong khi `PracticeSessionPage` có đủ. Bài thi có đồng hồ là nơi phím tắt đáng giá nhất
thì lại không có.

### 12. Question navigator: không có focus ring của HeroUI, mỗi ô 1 tab stop
Đo trong sitting (`/exams/take/1003?q=1`): nav button có `outline: 1px auto rgb(24,24,27)` (outline
mặc định của Chrome, không phải token `--focus`), `box-shadow: none`, `data-focus-visible: null`
— tức là **không dùng React Aria focus state**, chỉ là `<button>` trần với class tự viết.
Toàn sitting hiện có **13 tab stop**, trong đó **9** là ô navigator (mỗi câu 1 ô). Đề ETS khai
`QuestionCount = 200` ⇒ với đề đầy đủ sẽ là ~200 tab stop liên tiếp để đi hết navigator.
Không có điều hướng bằng mũi tên (grep `ArrowRight` chỉ là icon).

### 13. 74 cặp `<Link><Button>` ⇒ 2 tab stop và Enter không theo link
Kiểm chứng trong DOM (không chỉ grep) trên 12 trang: `/` **5** cặp, `/practice` **8**,
`/mentors` **3**, `/certificates` **3**, `/wallet` **2**, `/pricing` **1**, `/profile` **1**
(đo tại `13` điểm; con số 74 là đếm source, còn đây là số cặp đã render thực tế ở 12 route
đại diện).
Mỗi cặp là `<button>` lồng trong `<a>`: HTML sai, screen reader đọc 2 lần, và bấm Enter khi focus
vào button **không** navigate (button không có handler).
Ví dụ đã đo: `WalletPage` "Pricing plans" / "Buy credits" — `tabIndex` của `<a>` = 0 **và** có
1 `<button>` con, tức 2 tab stop cho một hành động.
**Sửa:** `<Button render={<Link to=… />}>` (HeroUI v3 hỗ trợ `render`), hoặc `<Link>` style như button.

### 14. 26 `<select>` native sót lại trong admin/learner
Tôi đếm trong DOM sau khi render (không phải đếm source):

| Trang | `<select>` | `<input>/<textarea>` | HeroUI field |
|---|---|---|---|
| `/admin/exams` | **2** | 1 | 0 |
| `/admin/finance` | **2** | 1 | 1 |
| `/admin/operations` | **1** | 1 | 1 |
| `/exam/5`… (learner) | 0 | 0 | 0 |

Cộng thêm `ProfilePage.tsx:342` (timezone), `SupportPage.tsx:383,424,443,462`. Native select mất
typeahead, mất popover positioning, và dùng `bg-background` thay vì token `--field-*` nên lệch màu ở dark mode.

---

## P2 — Lỗi điều hướng / logic

| # | Vấn đề | Bằng chứng |
|---|---|---|
| 15 | `/weakness-analysis` **bỏ qua OOBE gate** | `learner.fresh@demo.local`: `/weakness` → softlock `true`, `/weakness-analysis` → **softlock `false`**, hiện thẳng "Weakness analysis · TOEIC". `router.tsx:447` không bọc `OobeGate` |
| 16 | Deep link bị page bỏ qua | `/support/report` → tab active = **"My reports"** (không phải form). `SupportPage.tsx:44` hard-code `useState('history')`, không đọc `useLocation`. Tương tự `/placement/target-band` → `OnboardingPage` |
| 17 | Onboarding navigate tới param không tồn tại | `OnboardingPage.tsx:115,276` dùng `to: '/placement/take/$placementId'`; route khai báo `$attemptId` (`router.tsx:319`). `tsc` pass, test mock `navigate` nên không ai bắt. **Cần xác nhận lại bằng browser** (app hiện đi qua `/placement` nên không lộ) |
| 18 | Không có error boundary ở router | `router.tsx` không có `errorComponent`, `src/web` không có `ErrorBoundary` tự viết. App **không** trắng hoàn toàn: TanStack Router có khung mặc định `Something went wrong!` + `Show Error` (đã thấy ở P0#1). Nhưng khung đó không có link về/reload/header, và in raw message `Cannot read properties of undefined (reading 'code')` — không hành động được |
| 19 | `/admin/question-bank` gọi endpoint 404 | `GET /api/admin/ingestion` → **404**; `studioApi.ts:286` gọi nó khi mount (`QuestionBankStudioPage.tsx:121`). Backend chỉ có `/api/admin/imports` (`AdminImportEndpoints.cs:14`). Vẫn render được nhờ mock/test nhưng console lỗi mỗi lần vào |
| 20 | Trộn ngôn ngữ | `OobeSoftLockEmptyState.tsx:17-20` và `AppShell.tsx:333` là tiếng Việt; toàn bộ phần còn lại tiếng Anh. Trong khi đó nội dung lesson (`/lessons/3`) là tiếng Việt có chủ đích — nên banner OOBE tiếng Việt là **nhất quán với lesson, không nhất quán với UI shell**; cần quyết định một hướng |

---

## P2 — Trình bày

### 21. Learning Path "Off track" trên cả 4 tuần khi chưa bắt đầu
Ảnh `27-learning-path-toeic.png`: cả Week 1–4 đều chip vàng `Off track`, `0/6 steps done · 0 of 84m planned`.
Người học vừa tạo lộ trình 22 bước đã bị gắn nhãn "off track". Công thức
`weekCompleted < weekPlanned` không loại trừ tuần chưa bắt đầu.
Tương tự: card `Path progress` ghi `0 of 22 steps done · 328m remaining` — chưa làm gì thì
"còn lại" đúng, nhưng đặt ngay dưới "Off track" thì thành hai tín hiệu tiêu cực chồng lên một người
mới bắt đầu.

### 22. Practice: 4 thẻ khoá cùng lúc, thông điệp lặp 4 lần
Ảnh `23-practice-builder.png`: `Locked · complete in Learning Path` xuất hiện trên **4/4** thẻ,
nút `Unlock in Learning Path` **4 lần** giống hệt nhau, và banner `Recommended focus` cũng trỏ tới
một thẻ đang khoá (`Drill weakest section` trỏ vào section đang Locked). Không có đường đi tiếp nào
từ màn hình này ngoài việc tự tìm ra `/learning-path`.
Đối chiếu spec `2026-11-14-…:72` ("Stateless Practice… catalog clearly displays completed accuracy
vs. locked status"): chỗ khoá hiển thị đúng, nhưng **nút dẫn tới nơi mở khoá lại lặp 4 lần và banner
gợi ý mâu thuẫn với trạng thái khoá**.

### 23. Exam history: bảng hẹp hơn khung 2×, cột rỗng
Đo: `table` rộng **475px** trong khung ~1000px. Cột `Skills` **rỗng** (mục 7). `Date 79px`,
`Exam 118px` ⇒ tên đề tràn. Hai nút `Result`/`Review` trong 123px.
Root cause: thiếu `<Table>` root (#6) nên HeroUI không có layout/`px-4`, cộng dữ liệu rỗng (#7).

### 24. Onboarding Step 3 hiển thị 6 nút chọn band **và** một Select cho cùng một giá trị
Ảnh `08-oobe-step3-goal.png`: dãy chip `below_A1 … C1` ở trên, rồi ngay dưới là `Select` "Target Band"
với cùng danh sách. Hai control cùng ghi một state, cùng một nhãn. Không rõ cái nào là chính.

### 25. Timer của Placement đọc như đồng hồ thi
`/placement/take/1` hiển thị `Time remaining 47:59:54` ở đúng vị trí/kích thước/kiểu chữ với đồng hồ
mock exam (`ExamSittingShell.tsx:790-800` dùng chung một khối, cùng `aria-label="Time remaining"`,
cùng chuyển sang đỏ khi `<= 60`).

Đo thực tế: gọi `/api/learner/placement/take/1` hai lần cách 2.5 giây → `169183` rồi `169180`
(**giảm đúng 3 giây**). Nghĩa là nó **có** đếm thật, và component **có** auto-submit khi về 0
(`:238-260`).

Nhưng `PlacementSitting.cs:6-8` nói rõ: placement "has no clock… no elapsed time is deducted while
the tab is closed", deadline là `min(started_at + 72h, last_activity + 48h)`. Vậy con số đang đếm là
**hạn không hoạt động 48 giờ**, không phải thời gian làm bài.
⇒ Hai vấn đề: (a) nhãn "Time remaining" + style đồng hồ đỏ tạo áp lực thi cử không có thật;
(b) người học thấy `47:59:54` sẽ tưởng mình có gần 48 giờ để làm 10 câu, hoặc tưởng bị bấm giờ sai.
Nên đổi nhãn thành "Session expires" và chỉ hiện chi tiết khi còn < 1 giờ, hoặc ẩn với placement.

### 26. Nhãn trạng thái lưu không có `aria-live`
`ExamSittingShell.tsx:775-788`: placard `Saved` / `Offline — N queued` không có `aria-live`, screen
reader không biết. Navigator cũng không có `aria-current`.

### 27. Markdown trong lesson render ra dấu `*` thô
`/lessons/3`: đo được **14 dấu `*`** trong text đã render (`*\`I haven't checked the schedule yet\`*`).
`SafeMarkdown` không xử lý italic, nên nội dung lesson hiển thị `*"..."*` thay vì in nghiêng.

### 28. Ký tự `₫` không có trong font tự host ⇒ rơi sang font hệ thống
Đo chính xác (canvas + `document.fonts.check`):
`document.fonts.check('16px "IBM Plex Mono"', '₫')` = **false**;
`'₫'` đo bằng `"IBM Plex Mono"`, `"IBM Plex Sans"`, `"IBM Plex Sans Condensed"` đều cho
**cùng một bề rộng 51.27px**, bằng đúng `sans-serif` (51.32px) ⇒ cả ba font Plex đều **không** chứa
`U+20AB` và trình duyệt fallback.
Đối chứng: `document.fonts.check('16px "IBM Plex Mono"', '€')` = **true** — `U+20AC` có trong
`unicode-range` (`globals.css:19`), `U+20AB` thì không.

Hệ quả thấy được: `/admin` in `Revenue today` thành `0  ₫` với ký hiệu rơi khỏi baseline và tách
khỏi số (`17-admin-overview.png`); `/wallet` in `290.000 ₫` lệch baseline so với `7` bên cạnh
(`32-wallet.png`). `formatVnd` (`security.ts:170`) nối bằng space thường nên không có gì giữ chúng lại.
**Sửa:** thêm `U+20AB` vào `unicode-range` của cả ba family (hoặc subset lại woff2 có `₫`),
và dùng `&#8203;`/`no-break` giữa số và ký hiệu.

### 29. Chi tiết nhỏ nhưng đo được
- Touch target ở mobile: `/dashboard` **21** phần tử < 44×44 (`credits` 25px, `+ Top up` 25px,
  `Active certificate` 36px, theme toggle 36px), `/wallet` 29, `/exams` 29. WCAG 2.5.5 khuyến nghị 44×44.
- `/wallet` chia tab "Credit history" / "Orders & receipts" bằng HeroUI `Tabs` nhưng không giữ state
  vào URL ⇒ reload luôn về tab đầu.
- `/placement` (sau khi đã đặt) hiện `Retake ready` + `Full: 5 cr · L+R: 3 cr` nhưng **không** còn
  đường nào để làm lại miễn phí — đúng nghiệp vụ, song nhãn "Retake ready" ở góc trên cùng bên phải
  không nói là **có tính credit**; người dùng chỉ biết sau khi bấm vào `Retake options`.

---

## Đối chiếu với spec

`docs/specs/2026-11-14-simplify-…:74` yêu cầu AI grading hiển thị bằng "`Alert`, `ProgressBar` cards,
**interactive inline highlighted text**, a **3-column correction table**, và **side-by-side model
rewrite card**". Kiểm tra `ProductiveAiFeedback.tsx`: có `Table` 3 cột (`:178-200`) ✔, nhưng
**không** có side-by-side rewrite card và không có inline highlight tương tác — chỉ `Table` +
`Chip`. Chưa thấy trên browser vì seed chưa có speaking/writing set ("No speaking practice sets are
currently published for this certificate").

---

## Thứ tự sửa đề xuất

| Ưu tiên | Việc | File | Rủi ro |
|---|---|---|---|
| **P0** | Sửa cache type ở `saveGoalMutation.onSuccess` | `OnboardingPage.tsx:126` | 1 dòng, chỉ ảnh hưởng luồng lưu goal |
| **P0** | Thống nhất 0–25 giờ/tuần client↔server, bỏ native validation | `OnboardingPage.tsx:178,634` | đổi hợp đồng validate — cần test |
| **P1** | Khớp `SectionAccuracyDto`/`TopicAccuracyDto` với contract C# | `lib/api.ts:…` + `WeaknessAnalysisPage` | sửa 8+11 field; test hiện tại không phủ vì dùng mock |
| **P1** | Thêm `<Table>` root | `MockExamConfirmPage.tsx:370`, `MockExamHistoryPage.tsx:219` | 2 dòng mỗi file |
| **P1** | Trả `skillScores` trong result/history | service + contract | Kiểm chứng: `AttemptSkillScores` không có dòng cho mock attempt 2 trong seed ⇒ trước hết là dữ liệu/seed, sau đó là empty state |
| **P1** | `--warning-text` tối + token brand cho dark mode | `globals.css` | đụng token hệ thống, cần re-walk cả 2 theme |
| **P1** | `whitespace-nowrap`/`truncate` cho Select chứng chỉ | `AppShell.tsx:231-244` | 1 dòng |
| **P2** | `Button render={<Link/>}` cho 74 chỗ | nhiều file | cơ học nhưng rộng; làm theo từng trang |
| **P2** | `Skeleton` thay spinner ở 4 trang learner | 4 file | thuần hiển thị |
| **P2** | `toast()` cho 36 banner success | 4 file | xoá state, dễ |
| **P2** | Error boundary ở router | `router.tsx` | 1 file, đổi khung lỗi mặc định thành màn hình có đường về |
| **P2** | Phím tắt cho sitting + `Kbd` hint | `ExamSittingShell.tsx` | hành vi mới, cần test |
| **P3** | `ProgressCircle`/`Meter` ở dashboard, chart ở exam history | 2 file | đúng yêu cầu "biểu đồ" của bạn |
| **P3** | `Drawer` cho mobile nav | `AppShell.tsx` | ảnh hưởng mọi trang mobile |

Việc "vẽ biểu đồ" (mục 27 báo cáo trước) giờ đã có bằng chứng cụ thể hơn: `/exams/history` **có**
`cefrBands` (`below_A1…C1`) và `attempts[].totalScore` trong response nhưng **không** render band track
nào; `ProgressBar` không được dùng ở trang này dù đã có sẵn 6 mức band.

---

## Ghi chú vận hành

- `scripts/preflight.sh` không chạy được qua `bash` trên máy này (WSL relay thiếu `/bin/bash`);
  dùng `C:\Program Files\Git\bin\bash.exe -lc`.
- `seed-manual-test.sh start` từng chết giữa lúc chạy (web/api cùng tắt, không có log lỗi) — đã khởi
  động lại bằng `dotnet bin/Debug/net10.0/WebAPI.dll` + `node node_modules/vite/bin/vite.js preview`
  với `VITE_API_TARGET`. Nếu `Start-Process npx` báo "%1 is not a valid Win32 application", gọi
  `node` trực tiếp.
- `logs/manual/*.log` hiện bật EF command log ở mức `Information` khi chạy `.dll` trực tiếp (biến
  `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command` không set được bằng PowerShell
  do dấu chấm) — script `.sh` set đúng, chạy tay thì không.
- Toàn bộ 32 ảnh trong `docs/audit/live/` được chụp ở 1440×900 (trừ `13-`/`29-` là 390×844).
