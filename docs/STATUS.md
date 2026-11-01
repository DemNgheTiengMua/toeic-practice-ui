# Trạng thái dự án — đọc file này trước

> Cập nhật 2026-10-31, sau khi #140 và #146 xong.
> Mọi thứ khác (STATE.md, MASTER-SPEC.md, docs/audit/) là tài liệu tham chiếu. **File này là nơi duy nhất
> nói "đang ở đâu".** Nguồn sự thật về công việc là GitHub issues #141–#162; đồ thị `blockedBy` ở đó
> tự cập nhật, nên đừng chép tay danh sách ticket vào đây.

---

## 1. Đang ở đâu

Research ước lượng **14 ngày người**, 4 giai đoạn. Đã xong **~1.5 ngày** — giữa Giai đoạn 1.

| Giai đoạn | Nội dung | Trạng thái |
|---|---|---|
| 1. Nền móng đa chứng chỉ & engine điểm | #146–#150 | ✅ **xong** — #146, #148, #149, #150 |
| 2. Chấm Writing & Speaking | #151–#152 | ⬜ chưa |
| 3. Mastery, lộ trình có hạn, hiệu chuẩn | #153–#156 | 🟡 **đang làm** — #153 xong |
| 4. Studio, preview, dashboard | #157–#159 | 🟡 **đang làm** — #157 xong |
| 5. Dọn dẹp | #160–#162 | ⬜ chưa |

## 2. Việc tiếp theo

**#159** (dashboard 3 khối) hoặc **#158** (preview kiểu thi thật) — cả hai đã mở khoá.
Giai đoạn 1 đã xong.

Sau đó: #154/#155/#156 → #158/#159 → #160–#162.

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

`dotnet test`: **1430 pass, 0 fail** (47 E2E skip when the apps are not running). Schema-test:
**5 fail, the same 5 files and message numbers as the parent `4d2b9b3`** (`Msg 515`/`547`/`50011`/`50000`).

**Nợ kỹ thuật đã biết:** 14 test trong `WebMVC.E2ETests` fail khi có app chạy, **giống hệt trên
`edb1e0e`** (chỉ nhiều hơn một test đã sửa: `#gen-status` mà `8b2a47b` xoá nhưng test vẫn đòi).
Hỏng sẵn, không phải do #148/#157. Trước giờ ẩn vì thiếu `E2E_BASE_URL`. Cần ticket riêng.

## 4. Quyết định mới chốt sau STATE.md

| # | Quyết định | Lý do |
|---|---|---|
| D10 | **Cột thang điểm trên `Skills` là NULLable**, backfill từ `ScoreScales`, chưa ép NOT NULL | NOT NULL làm vỡ 12 file schema-test + 16 seed. "Thiếu cấu hình = lỗi" ép lúc **gọi**, không phải lúc **lưu** |

D1–D9 giữ nguyên, xem `docs/STATE.md` §1. Quy tắc cũ vẫn hiệu lực: kết luận nào đòi đảo D1–D10 thì bỏ.

## 5. Hai lỗ hổng trong hệ ticket

Research liệt kê 17 mục "phải xoá" nhưng **2 mục không có ticket nào**:

- **Mục 5** — luật `independent` trả `TotalScore = 0` → cert dùng luật này không bao giờ có band. Chỗ: `AttemptEngine.Scoring.cs`.
- **Mục 15** — micro-check trong Lessons chấm ở client, không lưu điểm → không đóng góp vào mastery. Chỗ: `LessonCards`.

Chưa quyết có làm hay bỏ. Đừng để mất.

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
