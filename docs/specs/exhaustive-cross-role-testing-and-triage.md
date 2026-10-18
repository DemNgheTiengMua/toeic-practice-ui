# Rà soát lỗi theo luồng (Admin ↔ Learner) và chuyển thành GitHub issue

> Agent nhận việc: đọc hết file này trước khi chạm vào app. Thay thế #58–#62
> (đã đóng "not planned"): bản cũ reset DB sai cách, sai route, sai số credit
> và phụ thuộc các skill `/grill-me`, `/to-spec`, `/to-ticket` không phải môi
> trường nào cũng có. Quy trình dưới đây chỉ cần Playwright MCP, `bash`,
> `sqlcmd` và `gh`.

## 1. Vì sao

Dự án làm một mình cùng AI. Test tự động (Services.Tests, WebMVC.Tests,
WebMVC.E2ETests) đi đường thẳng nên không chạm tới những chỗ test tay đang nổ:
trigger SQL, composite key `CertId`, phân quyền, ví credit. Mục tiêu: đi hết
các luồng nghiệp vụ trên app thật, và **mỗi lỗi tìm được phải thành một GitHub
issue tự đủ ngữ cảnh** để người khác (với bất kỳ AI nào) sửa được mà không
phải hỏi lại.

Ưu tiên: luồng + backend. Lỗi giao diện thuần túy ghi lại nhưng để sau.

## 2. Môi trường — bắt buộc dùng `scripts/crawl-env.sh`

```sh
scripts/crawl-env.sh fresh    # trước MỖI loop: dừng app → tạo lại DB → build → chạy
scripts/crawl-env.sh errors   # stack trace phía server (trình duyệt không thấy)
scripts/crawl-env.sh status   # cổng, số dư learner, đường dẫn log
scripts/crawl-env.sh stop
```

- **DB riêng `ExtraEfficiencyCrawl`**, không đụng DB `ExtraEfficiency` hằng
  ngày. `fresh` drop + tạo lại + `database-schema.sql` + `seed-demo.sql`.
- **Không** "reset" bằng cách chạy lại `seed-demo.sql`: seed chỉ `IF NOT
  EXISTS`, credit đã tiêu và attempt/session/khiếu nại cũ vẫn còn nguyên.
- **Thanh toán = mock** (`Payment__Gateway=mock`, chỉ trong script).
  `appsettings.json` vẫn là payOS cho dev bình thường. Checkout đi qua
  `/checkout/mock-gateway` để giả lập thành công / thất bại / hủy.
- **Chấm AI = fake** (`AI_PROVIDER=fake`): Writing/Speaking chấm offline, tất
  định, không tốn tiền. `CRAWL_AI=real scripts/crawl-env.sh fresh` nếu thật sự
  cần gọi SpeechSuper/Claude.
- Log: `logs/crawl/api.log`, `logs/crawl/mvc.log` (gitignored). API trả mọi
  exception dưới dạng ProblemDetails chung chung — nguyên nhân thật (trigger
  nào, FK nào) chỉ có trong `api.log`.
- Mail kích hoạt / quên mật khẩu: file `.eml` mới nhất trong
  `src/WebAPI/mail-drop/`.

Tài khoản (mật khẩu chung `Demo1234!`, số liệu sau `fresh`):

| Tài khoản | Vai trò | Ghi chú |
|---|---|---|
| `admin@demo.local` | admin | |
| `learner@demo.local` | learner | TOEIC, **8 credit**, có lịch sử thi/luyện/đặt mentor |
| `learner.fresh@demo.local` | learner | chưa có lịch sử — dùng cho luồng lần đầu (placement, lộ trình) |

Playwright MCP chỉ có một phiên trình duyệt: đổi vai bằng đăng xuất / đăng
nhập lại.

## 3. Quy tắc an toàn

- Mọi bản ghi agent tạo ra mang tiền tố `[TEST-CRAWL]`.
- Xóa / khóa / hủy kích hoạt **chỉ** trên bản ghi `[TEST-CRAWL]`. Dù DB là
  bản dùng một lần, giữ quy tắc này để kết quả các bước sau còn đối chiếu được.
- Không sửa code trong lúc rà soát. Tìm lỗi và sửa lỗi là hai việc tách rời.
- Không chạy rà soát trên DB nào khác ngoài `ExtraEfficiencyCrawl`.

## 4. Cách test một luồng

Không "bấm mọi nút". Với mỗi luồng trong §5:

1. **Đường chính**: đi trọn từ đầu đến cuối, ghi lại id sinh ra (attemptId,
   orderId…).
2. **3–5 ca biên có chủ đích**, chọn trong:
   - form rỗng / sai định dạng / quá dài / ký tự Unicode;
   - bấm gửi hai lần, F5 sau POST, nút Back sau khi nộp;
   - sai vai (learner mở `/admin/...`), id của người khác hoặc id không tồn
     tại trên URL;
   - hết credit, hết giờ, đổi chứng chỉ đang học giữa chừng.
3. **Sau mỗi thao tác** kiểm tra: mã HTTP (`browser_network_requests`), lỗi
   console, `scripts/crawl-env.sh errors`, và khi luồng đụng tới tiền / điểm /
   trạng thái thì kiểm tra DB bằng `sqlcmd -d ExtraEfficiencyCrawl` (số dư
   = `SUM(Delta)` trên `CreditTransactions`).
4. **Đối chiếu chéo vai**: thứ vai A vừa tạo/sửa phải hiện đúng ở vai B.

## 5. Các loop

Route đã đối chiếu với controller ngày 2026-10-18. Luồng nào ghi "kỳ vọng"
mà tài liệu (`PRODUCT.md`, `CONTEXT.md`, `docs/adr/`) không nói rõ thì ghi là
câu hỏi, đừng tự kết luận là lỗi.

**Loop 0 — Tài khoản**
- Khách: `/auth/register` → mail → `/auth/verify-email` → `/auth/sign-in`;
  `/auth/resend-activation`; `/auth/forgot-password` → mail →
  `/auth/reset-password`; đăng nhập sai nhiều lần.
- Learner: `/profile`, `/profile/password`.
- Admin: `/admin/users` (lock / unlock / resend-activation / adjust-credits).
- Chéo: user bị khóa không đăng nhập được; adjust-credits hiện ở `/wallet`.

**Loop 1 — Đề thi & luyện tập**
- Admin: `/admin/exams` → `create` → `{id}/studio` (groups, questions, audio,
  media, reorder) → `{id}/audit` → `{id}/publish`; `{id}/edit`, `{id}/export`,
  `{id}/delete` (chỉ `[TEST-CRAWL]`).
- Learner thi: `/exams` → `/exams/{id}/instructions` → start (trừ credit) →
  `/exams/take/{attemptId}` (trả lời, cancel, submit) → `/exams/result/{id}` →
  `/exams/review/{id}` → `/exams/insight/{id}`; `/exams/history`.
- Learner luyện: `/practice` → start → `/practice/{sessionId}` (answer, hint)
  → `/practice/{id}/summary` → `/practice/insight/{id}`; mở session mới khi
  session cũ còn dở.
- Chéo: đề vừa publish hiện ở `/exams`; đề chưa qua audit không hiện; bấm start
  hai lần chỉ trừ một credit.

**Loop 2 — Bài học & lộ trình**
- Admin: `/admin/lessons` → `create` / `{id}/edit` → `save`; `{id}/preview`,
  `upload`, `template`, `{id}/export`, `{id}/delete`.
- Learner: `/learning-path` (generate, reset, unlock, `upgrade` — tốn credit),
  `/learning-path/modules/{id}`, steps start, `/lessons/{id}` (xem thẻ,
  micro-check), `/lessons/by-category/{id}`.
- Chéo: bài vừa tạo/sửa hiện đúng ở learner; xóa bài đang nằm trong lộ trình
  của learner thì lộ trình không nổ.

**Loop 3 — Nhập câu hỏi (hai đường)**
- Admin: `/admin/imports` (POST file) so với Quick Ingest trong studio
  (`quick-ingest/parse` → `commit`). Cùng một file đúng, một file thiếu trường,
  một file sai định dạng cho cả hai đường.
- Ghi `[REDUNDANCY]` nếu hai đường xử lý cùng dữ liệu khác nhau.

**Loop 4 — Gói credit, thanh toán, ví**
- Admin: `/admin/packages` (create, edit, deactivate, toggle-featured).
- Learner: `/pricing` → `/checkout` → `/checkout/mock-gateway` (thành công /
  thất bại / hủy) → `/payment/result` → `/wallet`.
- Admin: `/admin/orders`, `{id}/detail`, `{id}/reconcile`.
- Chéo: thành công cộng đúng một lần (kể cả F5 trang kết quả); thất bại / hủy
  không cộng; gói đã deactivate không mua được; trạng thái đơn hai bên khớp.

**Loop 5 — Quy đổi điểm & test đầu vào**
- Admin: `/admin/score-conversion` (sửa bảng quy đổi, ngưỡng band).
- Learner (`learner.fresh`): `/placement` → start → take → submit → result
  (miễn phí, không ghi ledger) → `/placement/target-band`; `/dashboard`.
- Chéo: đổi bảng quy đổi ở admin thì kết quả / dashboard learner phản ánh
  đúng; bảng chồng ngưỡng bị chặn (trigger `trg_ScoreBands_NoOverlap`) với
  thông báo dễ hiểu, không phải 500.

**Loop 6 — Hỗ trợ, khiếu nại, mentor**
- Learner: `/support` → `/support/report` → `/support/history`; `/mentors` →
  `/mentors/{id}` → book → confirm → `/mentors/bookings` (cancel, rate).
- Admin: `/admin/complaints` (`{id}`, status, resolve); `/admin/mentors`
  (create, edit, slots create / close).
- Chéo: admin resolve thì learner thấy ở history; slot đã đặt không đặt lại
  được; hủy booking trả slot và credit theo đúng quy tắc.

**Loop 7 — Writing / Speaking (AI fake) & phần còn lại**
- Learner: `/writing/{id}/instructions` → take → submit → pending / status →
  review; `/speaking/...` tương tự kèm upload-audio, retry.
- `/saved-questions`, `/weakness-analysis`, `/learner/insights/dashboard`.
- Với AI fake chỉ kiểm vòng đời (pending → graded → review), không kiểm chất
  lượng chấm.

## 6. Phân loại

| Nhãn | Khi nào | Ưu tiên |
|---|---|---|
| `[CRASH]` | 500, exception, trang lỗi, lỗi trigger / FK lộ ra người dùng | cao |
| `[LOGIC]` | không nổ nhưng sai: credit, điểm, trạng thái, phân quyền | cao |
| `[REDUNDANCY]` | hai đường làm cùng một việc, kết quả khác nhau | vừa |
| `[UX]` | khó hiểu, thiếu phản hồi | để sau |

Lỗi xem / sửa được dữ liệu người khác, hoặc learner vào được trang admin:
`[LOGIC]`, ưu tiên cao nhất, ghi rõ là lỗi bảo mật.

## 7. Từ phát hiện đến issue

Nháp từng phát hiện vào `scratch/findings-loop-{N}.md` (gitignored) trong lúc
chạy. Rồi:

| Loại | Làm gì |
|---|---|
| `[CRASH]` có stack trace | tạo issue ngay, nhãn `bug` + `ready-for-agent` |
| `[LOGIC]` mà tài liệu nói rõ hành vi đúng | tạo issue ngay, trích dẫn tài liệu, `bug` + `ready-for-agent` |
| `[LOGIC]` chưa rõ đúng sai, `[REDUNDANCY]` | gom lại, hỏi dev **một lần** cuối loop, rồi mới tạo issue (`ready-for-human` nếu cần quyết định thiết kế) |
| `[UX]` | một issue gom cho cả loop, nhãn `enhancement` |

Trước khi tạo: `gh issue list --state all --search "<từ khóa>"` để tránh trùng.
Tạo bằng mẫu `.github/ISSUE_TEMPLATE/crawl-finding.md`:

```sh
gh issue create --title "[CRASH] Loop 1: <mô tả ngắn>" \
  --label bug --label ready-for-agent --body-file scratch/issue-<slug>.md
```

Một issue đạt chuẩn khi người chưa từng thấy dự án vẫn tái hiện được trong 5
phút: bước tái hiện kèm tài khoản và URL, kỳ vọng (có nguồn) và thực tế, stack
trace từ `api.log`, file / hàm nghi ngờ, quy tắc `AGENTS.md` liên quan, tiêu
chí nghiệm thu.

## 8. Người sửa issue làm gì

Đã ghi vào `AGENTS.md` (§ "Fixing a crawl-finding issue") và trong mẫu issue,
để không phụ thuộc skill riêng của ai:

1. Viết test tái hiện lỗi trước (`tests/Services.Tests` chạy trên SQL Server
   thật, trigger thật) — test phải **đỏ**.
2. Sửa đến khi test đó và toàn bộ `dotnet test` **xanh**.
3. `scripts/crawl-env.sh fresh`, đi lại đúng bước tái hiện trong issue.
4. PR ghi `Fixes #N`, mô tả nguyên nhân gốc.

## 9. Bàn giao sau mỗi loop

Báo lại cho dev: danh sách issue đã tạo (số + tiêu đề), các câu hỏi
`[LOGIC]` / `[REDUNDANCY]` đang chờ quyết định, và luồng nào chưa chạy được
(kèm lý do).
