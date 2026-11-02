# Trạng thái dự án — đọc file này trước

> Cập nhật 2026-11-02, sau khi **toàn bộ #141–#162 đóng**.
> Mọi thứ khác (STATE.md, MASTER-SPEC.md, docs/audit/) là tài liệu tham chiếu. **File này là nơi duy nhất
> nói "đang ở đâu".** GitHub issues đã đóng hết; việc còn lại nằm ở §2 và ở
> `docs/HANDOFF-2026-11-02.md` (quyết định cần người, cải tiến về sau).

---

## 1. Đang ở đâu

**Cả 5 giai đoạn đã xong.** Không còn issue mở, không còn epic mở.

| Giai đoạn | Nội dung | Trạng thái |
|---|---|---|
| 1. Nền móng đa chứng chỉ & engine điểm | #146–#150 | ✅ **xong** |
| 2. Chấm Writing & Speaking | #151–#152 | ✅ **xong** |
| 3. Mastery, lộ trình có hạn, hiệu chuẩn | #153–#156 | ✅ **xong** |
| 4. Studio, preview, dashboard | #157–#159 | ✅ **xong** |
| 5. Dọn dẹp | #160–#162 | ✅ **xong** |

`dotnet test` trên `main` sau khi merge #162: **815 Services + 717 WebMVC + 19 WebAPI = 1551 pass, 0 fail**.
Schema-test: **5 fail**, đúng 5 file và đúng message number như trước đợt này
(`domain04, 06, 07, 08, 15`) — không hồi quy.

## 2. Việc tiếp theo

Không còn ticket. Ba việc con người nên quyết, ghi ở `docs/HANDOFF-2026-11-02.md`:

1. **5 file schema-test hỏng sẵn** (`domain04, 06, 07, 08, 15`). Đây là lỗi test fixture, không phải
   lỗi schema — nhưng chúng che mất hồi quy thật trong 5 miền đó. Cần một ticket riêng.
2. **14 E2E journey fail khi có app chạy**, giống hệt trên parent của mọi ticket trong đợt này.
   Bộ E2E phụ thuộc trạng thái (cần seed mới + host yên tĩnh), nên nó không phải lưới an toàn đáng tin.
3. **`PathModuleTopics` chưa nối vào mastery**: bước luyện tập theo lộ trình chọn câu theo `DisplayOrder`
   (`LearningPathService.Steps.cs`), không qua `SmartPracticePlanner`. Hai bộ chọn câu cùng tồn tại.

`wrong_before`/`unseen` vẫn là giá trị hợp lệ trong `CK_Practice_Source` để row cũ đọc được, nhưng
không còn ai ghi — #162 xoá cột `FeedbackMode` chứ không xoá giá trị này (xem quyết định D27).

## 3. Đã xong (đã push lên `origin/main`)

| Ticket | Commit | Kết quả đo được |
|---|---|---|
| #139 — khoá JWT bị commit | `6929a3a` | Key cũ bị từ chối lúc boot, script tự thay |
| #140 — trang degraded chậm 12s | `37fc74e` | **12s → 2.1s**, breaker mở thì **0.07s**, tự hồi phục |
| #146 — thang điểm theo skill | `d1d222a` + `ac27350` | 3 cert chấm 3 thang riêng; `GetFallbackScale` đã xoá |
| #149 — render động theo `Cert.Skills` | `94428a2` + `5be51e3` | cert kỹ năng tên khác LIST/READ hiện đúng số thẻ |
| #147 — wizard ghi scale + band + rubric | `6a407ab` + `3baa688` | cert tạo xong chấm được ngay; `/admin/score-conversion` đã xoá |
| #148 — kỹ năng sản phẩm có band | `e786295` | Claude thật: `AiGradings.BandCode` `NULL` → `A2` (TOEIC) / `B2` (IELTS); trigger band đã gỡ |
| #157 — đóng cổng publish | (commit này) | 2 đường vòng audit bị bịt; `ExpectedQuestionCount` NOT NULL; 2 lối ghi cấu trúc khác (quick ingest, AI generate) cũng khoá; `hidden` có đường ghi riêng (withdraw) |
| #150 - practice for a Speaking-only or Writing-only cert | `4d2b9b3` + round 2 | `CK_Practice_Modality` widened to all 4 (the schema no longer blocks); a Writing-only cert drills and is marked by `MinWords` + `RequiredKeywords`, free, no credit or AI; **Speaking is not drilled** - a spoken answer cannot be typed, so the page links it to the recorded, AI-graded speaking practice. Free-response feedback is re-evaluated on read so it survives a refresh; a wrong written answer is no longer filed in the saved-question bank (it has no options); one shared predicate decides drillability for both the catalog count and the session builder |
| #151 — Writing chấm theo rubric thật, phản hồi sửa được bài | (commit này) | Prompt mang band descriptor từng tiêu chí; checklist (wordCount/meetsLength/usesAllKeywords/addressesPrompt) chốt **trước** khi cho điểm; `Weight` nay có tác dụng khi cộng; criterion model bỏ sót → retry 1 lần → `Failed`, không còn `MaxScore × 0.7`; `errorSpans` ≤5 + `priorityFix` + `modelRewrite` lưu vào `AiGradings` và hiện trên trang Review. Claude thật: bài 201 từ → `done`, 4 điểm + 2 lỗi có vị trí + 1 rewrite, band `B2`; "dog run fast" → `Failed`, 0 điểm, nút "Retry grading" |
| #155 — Luyện tập thành ôn tập thật | (commit này) | `UserCategoryMasteries` (4 bậc Untested→Learning→Reviewing→Mastered) + `UserQuestionHistories.CooldownUntil`. Sai 1 câu → category sang `learning`, hạn ôn +24h; câu đó không xuất hiện lại trong 24h; hết hạn thì quay lại (đúng câu đã sai, ở vị trí câu cũ lâu nhất). Đúng 5/5 → `reviewing` +3 ngày, lần ôn sau +7; 85% trên ≥10 câu qua ≥2 ngày → `mastered` +14 ngày; 2 câu sai liên tiếp khi `mastered` → `reviewing` +24h. Phiên **cố định 10 câu** (≤4 câu cũ đến hạn + ≥6 câu mới), bỏ `all/wrong_before/unseen` và ô số lượng. Micro-check trong Lesson chấm ở server (`POST /lessons/{id}/cards/{cardId}/micro-check`), ghi cùng mastery row |
| #154 — lộ trình động 4 tuần | `6fc998e` → `cd57645` | `LearningPathPlanner` sinh bước từ section yếu nhất của **từng người** + `WeeklyStudyHours` của goal; 4 tuần × 3–5 bước × 25–45 phút. Hai học viên cùng cặp B1→B2 nhận nội dung khác nhau (P4+P5 so với P1+P7). `mock_exam` và `lesson` nay đóng được qua `PathStepCompletion` |
| #156 — hiệu chuẩn placement | `1091fd6` → `ee09668` | `AttemptAnswers.AnsweredAt`; đề placement 4 câu và 2 câu bị xoá, thay bằng đề 24 câu; bỏ câu chữ "Adaptive" sai sự thật. `CalibrationService` chỉ đề xuất sau **20 câu hợp lệ** dưới 40% (ngưỡng chọn vì 10 câu cho 16.6% báo nhầm, 20 câu còn 5.7%); không bao giờ tự sửa lộ trình (D7) |
| #158 — preview kiểu thi thật | `f59b736` | Dùng chính view thi thật sau `?preview=true`, chỉ admin; đi hết 20 câu mà `ExamAttempts`/`AttemptAnswers`/`FreeResponses` không đổi (có test). Nhảy số câu, nhảy section, j/k: **200 câu trong 52 giây đo được**. Sửa 2 bug thật: `lockOneWay` ReferenceError khi nhảy tiếp từ câu AUDIO_DRIVEN trên trang thi thật; preview dùng lại `ee_exam_pending_0` làm hiện banner OFFLINE giả |
| #159 — studio 2 pane + dashboard 3 khối | `7f76ae9` + `a9c4a3b` → `146c5b3`, `ef4bd3c`, `24297aa` | Studio 3→2 pane; MCQ 4 đáp án còn **14 control** thay vì ~18; `AudioPath`/`ImagePath` là file picker thật; `AudioStartMs/EndMs` lấy từ 2 nút player. Dashboard còn **3 khối**, 8 khối cũ bị xoá cùng service/view model; sidebar 17→5 với Writing/Speaking có mục riêng; Skill Cards render từ `Cert.Skills`. Sửa 2 defect thật: partial tự set `Layout`+`Title` làm trang Writing/Speaking có `<title>` rỗng và một trang lồng trong trang; và Goal Standing in "Not placed yet" trong khi goal ghi B1 |
| #160 — xoá code chết | `11e3f7a` → `01ca3df` | −801 dòng / 52 file: simulator, `SaveGroup`/`SaveQuestion`, shim `/admin/imports`, `CompleteStepAsync`, `FeedbackMode` (cột + entity + enum + DTO + view), predicate chết ở Preflight |
| #161 — bỏ hardcode hiển thị | `9f9e4e9`, `d35e083` → `f8c2c64` | `Certificates.SectionNoun` (NOT NULL DEFAULT 'Section') thay cho `Code == "TOEIC" ? "Part" : "Section"`; band strip vẽ theo `MinTotal`/`MaxTotal` (DEMO 4 band ra đúng 1:1:2:1, trước đó 4 band bị nhét vào 6 cột); `TargetBandViewModel`'s `"B2"` fallback bị xoá → trang nói "chưa chọn band đích". Audit bỏ sót 12 chỗ, gồm `aria-label="TOEIC Practice"` trên favicon |
| #152 — SpeechSuper de-IELTS-ified | `9b1d90a`, `10ac945`, `bf3f0d0` | Chấm thật bằng audio thật: **13.40/36 → band B1** trên IELTS, provider trả 10 485 byte; clip im lặng → 0.00 và **không có band**. Báo cáo + file mẫu ở `docs/audit/152-speechsuper-live-scores.md` |
| #162 — xoá feature bị thay thế | `16a3362` → `b9bf579` | −3142 dòng / 93 file: premium path (IsPremiumOnly, `PremiumPathService`, khoản trừ 5 credit, `/learning-path/upgrade`) và AI sinh đề (3 file service, dialog, endpoint, CLI + 2 script gọi nó). Học viên **0 credit** nay mở được mọi bước — có test và có ảnh browser. `CreditReason.PremiumPath` **giữ lại** vì ledger append-only (D25) |

`dotnet test`: **1551 pass, 0 fail** (Services 815 + WebMVC 717 + WebAPI 19) trên `main` sau #162.
47 E2E skip khi không có app chạy; với cả hai app chạy, **4** journey fail và **đúng 4 đó cũng fail
trên parent** — hỏng sẵn. Schema-test: **5 fail, đúng 5 file và message number như parent**
(`domain04, 06, 07, 08, 15`).

**Nợ kỹ thuật đã biết:** bộ `WebMVC.E2ETests` phụ thuộc trạng thái — cần seed mới và host yên tĩnh,
và số test chạy được thay đổi tuỳ app có đang chạy hay không. Nó không phải lưới an toàn đáng tin;
cần ticket riêng để làm nó tất định.

## 4. Quyết định mới chốt sau STATE.md

| # | Quyết định | Lý do |
|---|---|---|
| D10 | **Cột thang điểm trên `Skills` là NULLable**, backfill từ `ScoreScales`, chưa ép NOT NULL | NOT NULL làm vỡ 12 file schema-test + 16 seed. "Thiếu cấu hình = lỗi" ép lúc **gọi**, không phải lúc **lưu** |
| D27 | **Giữ `CreditReason.PremiumPath` và giá trị `'premium_path'` trong `CK_CreditTxn_Reason`, dù tính năng premium path đã bị xoá (#162)** | `CreditTransactions` là append-only — `trg_CreditTransactions_AppendOnly` ném lỗi khi DELETE. Row `premium_path` đã tồn tại trong DB thật và trong seed demo. Xoá giá trị khỏi CHECK không xoá row, nhưng làm `EnumMapping.ParseWireCode<CreditReasonDto>` ném lỗi trên mọi row cũ → trang ví của học viên đó hỏng. Xoá từ vựng của dữ liệu quá khứ là migration dữ liệu, không phải dọn code. Có test `GetLedgerHistoryAsync_StillReadsHistoricalPremiumPathRows` và `domain10-payment.sql` canh giữ |

D1–D26 giữ nguyên, xem `docs/STATE.md` §1 (đợt này thêm D19–D27 ở đó; D25–D27 là của #160 và #162).
Quy tắc cũ vẫn hiệu lực: kết luận nào đòi đảo D1–D27 thì bỏ.

## 5. Lỗ hổng còn lại trong hệ ticket

Research liệt kê 17 mục "phải xoá" nhưng chỉ **1 mục không có ticket nào** (mục kia đã được xử lý):

- **Mục 5 — VẪN CÒN** — luật `independent` trả `TotalScore = 0` (`AttemptEngine.Scoring.cs:58`), nên
  cert dùng luật này không bao giờ có band: điểm luôn 0, band tra theo 0. Bản chất là đúng khi một
  chứng chỉ chấm từng kỹ năng và không có tổng — nhưng khi đó `ExamAttempts.TotalScore` và `BandCode`
  phải NULL chứ không phải 0, vì 0 là một tuyên bố ("không được điểm nào") mà hệ thống không có bằng
  chứng để nói. Cùng dạng lỗi với D14 (điểm 0 không có band) nhưng ngược chiều.
- **Mục 15 — ĐÃ XONG** trong #155: micro-check chấm ở server
  (`POST /lessons/{id}/cards/{cardId}/micro-check`), lưu điểm và ghi mastery row. Không còn `data-correct`
  trong markup.

Mục 5 nên thành một ticket riêng nếu có cert nào thật sự dùng `independent`.

## 6. Đã dọn

Xoá 2 file đã bị thay thế hoàn toàn và không ai tham chiếu:

- `docs/manual-test-run-report.md` — báo cáo chạy tay 2026-10-26 ghi "ALL 13 SCENARIOS PASSED (100%)". Đó là **trước** đợt rebuild; các lần crawl sau tìm ra hàng loạt lỗi thật. Giữ lại là tài liệu nói dối.
- `docs/roadmap-llm-and-ui-enhancement.md` — đề xuất "Contextual AI Mentor", chưa từng lên ticket nào. Research file + bộ ticket #141–#162 là bản thay thế đầy đủ.

**Không xoá** (đã kiểm tra có người dùng tới):

| File | Ai dùng |
|---|---|
| `docs/manual-test-plan.md` | `specs/sitting-room-navigation-and-practice-qol-defects.md` gọi là "valid regression script" — 5 chỗ |
| `docs/MANUAL-TEST-FLOW.md` | spec F9 trong `2026-10-29-remediation-spec-crawl-findings.md`; AGENTS.md trỏ tới |
| `docs/MANUAL-TEST-CREDENTIALS.md` | AGENTS.md §6 |
| `docs/toeic-user-flow.md` | PRODUCT.md |
| `docs/mainflow-demo-guide.md` | kịch bản demo bảo vệ |
| `docs/research-brief.md` | đề bài gốc của research |

## 7. Cách dùng file này

- Mở file này là biết đang ở đâu. Không cần đọc 22 issue.
- Xong một ticket: cập nhật §2 và §3, rồi `gh issue close`.
- Quyết định mới: thêm một dòng vào §4, đừng viết file mới.

## 8. Bảo trì graphify

Git hook `post-commit` đã cài, tự rebuild sau mỗi commit (đã kiểm chứng: commit `da6c835`
→ `built_at_commit` khớp). Kiểm tra bằng `graphify hook status`.

Sau **mỗi đợt commit** (nhiều commit, hoặc commit từ môi trường bỏ qua hook), chạy tay:

```sh
python -m graphify update .
```

Phải dùng `python -m`, **không** dùng `graphify` trần: shim `graphify` chạy MSYS Python
thiếu `tree_sitter_sql`, nên 20 file `.sql` bị bỏ im lặng và schema không vào graph.
Chênh lệch ~1700 node.

Sau khi sửa nhiều tài liệu (docs/*.md, ADR, spec), chạy thêm `/graphify --update` trong
agent để trích xuất lại phần ngữ nghĩa.

Kiểm tra graph có khớp code không:

```sh
python -c "import json;print(json.load(open('graphify-out/graph.json'))['built_at_commit'])"
git rev-parse HEAD
```
