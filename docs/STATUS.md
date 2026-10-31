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
| 1. Nền móng đa chứng chỉ & engine điểm | #146–#150 | 🟡 **đang làm** — #146 xong |
| 2. Chấm Writing & Speaking | #151–#152 | ⬜ chưa |
| 3. Mastery, lộ trình có hạn, hiệu chuẩn | #153–#156 | ⬜ chưa |
| 4. Studio, preview, dashboard | #157–#159 | ⬜ chưa |
| 5. Dọn dẹp | #160–#162 | ⬜ chưa |

## 2. Việc tiếp theo

**#149 — Render động theo `Cert.Skills`.** Không chờ ai, mở khoá 4 ticket (#150, #157, #159, #161).

Sửa lỗi giảng viên sẽ thấy đầu tiên khi bấm thử: `AttemptViewModelMapper.cs:72` vẫn tìm kỹ năng bằng
`SkillCode == "LIST"`, nên cert đặt mã khác hiện **0 thẻ**.

Sau đó: **#147** → **#153** → (#154/#155/#156) → #157 → #159 → #160–#162.

## 3. Đã xong (đã push lên `origin/main`)

| Ticket | Commit | Kết quả đo được |
|---|---|---|
| #139 — khoá JWT bị commit | `6929a3a` | Key cũ bị từ chối lúc boot, script tự thay |
| #140 — trang degraded chậm 12s | `37fc74e` | **12s → 2.1s**, breaker mở thì **0.07s**, tự hồi phục |
| #146 — thang điểm theo skill | `d1d222a` + `ac27350` | 3 cert chấm 3 thang riêng; `GetFallbackScale` đã xoá |

`dotnet test`: **1351 pass, 0 fail**.

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
