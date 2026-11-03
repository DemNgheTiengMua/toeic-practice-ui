# Workflow — từ SRS về đến code

Áp dụng khi 6 session deep research trả kết quả về. Đây là quy trình cho **session trong repo**, không
phải cho agent ngoài.

Đọc `README.md` trước nếu chưa biết 6 brief là gì.

---

## Bước 0 — Lưu 6 file, chưa mở

```
docs/research/srs/content-srs.md
docs/research/srs/placement-srs.md
docs/research/srs/path-srs.md
docs/research/srs/ui-srs.md
docs/research/srs/admin-srs.md
docs/research/srs/transition-srs.md
```

**Giữ nguyên văn.** Không sửa, không tóm tắt, không format lại. Đây là bản gốc để đối chiếu khi hai SRS
mâu thuẫn — và để biết agent nói gì so với bạn nhớ là nó nói gì.

---

## Bước 1 — Kiểm tra, chưa spec

Mở session mới, paste:

```
Đọc 6 file trong docs/research/srs/ và docs/research/README.md.

Chưa viết spec, chưa cắt ticket. Chỉ làm ba việc:

1. Kiểm mỗi SRS qua 6 điều kiện trong README mục "What to check when an SRS comes
   back". Báo cái nào trượt, trích nguyên văn chỗ trượt.

2. Tìm mâu thuẫn giữa 6 SRS. Dùng bảng "Who owns a disputed area" để xác định ai
   đúng. Với mâu thuẫn ngoài bảng, liệt kê ra để tôi quyết.

3. Liệt kê những chỗ SRS nói mà repo này không làm được — thiếu engine, thiếu
   bảng, vi phạm design system, hoặc giả định code đã có.

Trả về một báo cáo ngắn. Đừng sửa file nào.
```

**Nếu SRS trượt check → gửi lại agent ngoài, đừng sửa tay.** Câu để gửi:

```
Mục X của bạn nói "...". Brief (mục Y) nói "...". Sửa hoặc giải thích.
```

Sửa tay nghĩa là bạn tự viết SRS, và mất chỗ để biết thiết kế sai ở đâu khi nó sai.

**Mâu thuẫn ngoài bảng phân xử → bạn quyết.** Ghi lý do vào `docs/STATE.md` theo dạng D-số, giống các
quyết định khác.

---

## Bước 2 — Spec, cắt theo đợt chứ không một lần

Không đợi đủ 6 SRS rồi mới spec hết. Mỗi đợt một vòng spec → ticket → build.

| Đợt | SRS đã có | Spec + ticket cho |
|---|---|---|
| 1 | content | Content: schema, upload, validate, authoring UI |
| 2 | + placement, admin | Placement, Admin |
| 3 | + path, ui, transition | Path, UI, Transition |

Lý do: content là long pole. Code viết song song được, 500 câu đã validate thì không. Bắt đầu build
content ở đợt 1 trong khi placement còn đang nghiên cứu.

Với mỗi đợt:

```
Đọc docs/research/srs/<tên>-srs.md và AGENTS.md.

Ra spec cho <vùng> theo to-spec. Spec phải tính đến:
- repo dùng database-first EF, schema ở docs/database-schema.sql
- AttemptEngine đã có sẵn (đọc src/Services/Attempts/) — không dựng engine thứ hai
- design system "Bảng hiệu phòng thi" (AGENTS.md §5)

SRS không biết code; spec của bạn phải biết.
```

**SRS ≠ spec.** SRS mô tả sản phẩm nên là gì, không thấy code. Spec mô tả thay đổi gì trong repo này.
Nhảy thẳng SRS → ticket sẽ ra ticket toàn việc đã có sẵn.

---

## Bước 3 — Ticket

`to-tickets`, vertical slice, có blocking edges.

Quy tắc:
- Slice đầu tiên phải nhỏ và chứng minh được
- Mỗi ticket ship được một mình, hoặc nói rõ nó chờ ai
- Ticket nào xoá code cũ → chỉ mở sau khi code mới xanh (xem `docs/STATUS.md` §2)

---

## Bước 4 — Build

`implement`. Red first. `code-review` trước khi push.

Xoá luồng cũ **theo từng phần**, ngay khi phần mới pass — không xoá trước, không đợi hết. Repo luôn
chạy được.

---

## Bước 5 — Ticket tích hợp, bắt buộc

Sáu SRS độc lập sẽ có khớp nối sai. Bảng phân xử giải quyết tranh chấp về nội dung, nhưng không đảm
bảo sáu thiết kế lắp được vào nhau.

Vì vậy ticket cuối phải **block bởi tất cả**, nội dung:

> Một learner chạy thật end-to-end: placement → goal → path → practice → mock exam → kết quả, trên một
> chứng chỉ có 4 kỹ năng và một chứng chỉ có 2 kỹ năng.

Không có ticket này, bạn phát hiện chỗ hở sau khi đã build xong cả sáu.

---

## Tóm tắt một dòng

**Lưu → kiểm → quyết mâu thuẫn → spec theo đợt → ticket → build → tích hợp.**

---

## Điều chưa biết

Chưa có SRS nào, nên chưa biết:

- Content SRS sẽ đòi bao nhiêu câu, và mất bao lâu để có. Nếu nó nói "3 tháng", placement phải launch
  hẹp (một chứng chỉ, một dải level) thay vì chờ đủ — câu hỏi 16 của `transition-brief.md`.
- Agent ngoài sửa SRS nhanh hay không. Nếu không sửa được sau 2 vòng, phương án dự phòng là bạn sửa và
  ghi rõ đã sửa gì.
