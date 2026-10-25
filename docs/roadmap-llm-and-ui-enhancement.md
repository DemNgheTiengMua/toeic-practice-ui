# Định hướng Phát triển Năng lực LLM & Nhân bản hóa UI/UX Tương tác

> **Ghi chú tiến độ:** Tài liệu này ghi lại chiến lược nâng cấp trải nghiệm học viên và tích hợp sâu LLM. Kế hoạch này sẽ bắt đầu triển khai sau khi hoàn thành dứt điểm các issues và bugs nền tảng hiện có (bao gồm Issue #82 và các tồn đọng về độ ổn định).

---

## 1. Vấn đề Cốt lõi: Thoát khỏi Cảm giác "Sandbox Lạnh Lùng"

Hiện tại, hệ thống `ExtraEfficiency` đã hoàn thiện rất tốt về mặt kiến trúc kỹ thuật (.NET 10, clean layering, database-first, SQL triggers, check constraints, append-only credit ledger). Giao diện tuân thủ triệt để ngôn ngữ "Bảng hiệu phòng thi" (signage palette, flat 2px ink, font đơn khoảng).

Tuy nhiên, về mặt trải nghiệm người dùng (UX), hệ thống đang mang lại cảm giác **"quá sandbox"** vì:
1. **Tương tác mang tính giao dịch đơn thuần:** Học viên vào làm bài $\rightarrow$ nộp bài $\rightarrow$ trừ credit $\rightarrow$ hiện điểm số $\rightarrow$ hết. Hệ thống hành xử như một máy chấm thi tự động hơn là một người bạn đồng hành sư phạm.
2. **Khối AI Insight chưa chạm đến hành động:** Khối AI Insight trên Dashboard hiện tại là một đoạn văn bản nhận xét số liệu dài 3–4 câu trích xuất từ dữ liệu thống kê. Người học đọc xong không có lời thôi thúc hay hướng dẫn cụ thể bước tiếp theo phải làm gì.
3. **Phản hồi sửa sai đơn độc:** Khi làm sai ở phần Practice hay Mock Exam, màn hình Review chỉ đưa ra kết quả đúng/sai cơ học. Người học không được giải tỏa thắc mắc tâm lý: *"Tại sao mình chọn đáp án C lại sai trong khi nghe có vẻ rất hợp lý?"*.
4. **Thiếu phản hồi sinh động tức thời (Micro-feedback):** Trình phát audio tĩnh, không có visualizer sóng âm; ghi âm không có thanh đo âm lượng (VU meter); không có chế độ xem giải thích ngay khi vừa làm xong từng câu.

---

## 2. Triết lý Định hướng: Contextual AI Mentor (Gia sư Ngữ cảnh)

* **Không làm:** Thêm chatbot trôi nổi nói chuyện phiếm, hoặc tự động tạo đề thi hàng loạt làm loãng ngân hàng đề chuẩn.
* **Mục tiêu:** Đưa LLM vào **đúng thời điểm học viên cần được hướng dẫn nhất (Point of Need)**. LLM sẽ đóng vai trò một **Gia sư sư phạm ngữ cảnh (Contextual AI Mentor)** gắn chặt vào từng điểm chạm trên giao diện người dùng.

```
                                  KIẾN TRÚC TƯƠNG TÁC AI GẦN GŨI
                                  
   Dữ liệu đưa vào (Inputs)           Bộ não LLM (Claude Sonnet 3.7)       Trải nghiệm sống động (Outputs/UI)
   -------------------------           ------------------------------       ----------------------------------
   • Câu hỏi + Bài đọc/Audio script   ===> [Giải mã bẫy tâm lý đề thi]  ===> • Thẻ "Vì sao câu này mình sai?"
   • Lựa chọn sai của học viên                                               (Chỉ rõ bẫy âm thanh, từ đồng nghĩa)
   
   • Bài viết / Transcript nói        ===> [Native Upgrade / Rewriter]  ===> • Split-view so sánh Trước / Sau
   • Target band mục tiêu (ví dụ 7.0)                                        (Highlight diff từ vựng, ngữ pháp)
   
   • SQL Views độ chính xác           ===> [Đơn thuốc học tập hôm nay]  ===> • AI Coach chào buổi sáng có cảm xúc
   • Tốc độ trả lời & nhịp học                                              (Nhiệm vụ 3 phút vá đúng lỗ hổng)
```

---

## 3. Bốn Bài toán LLM Trọng tâm

### Bài toán 1: Giải mã Bẫy Sai Ngữ cảnh (Socratic Trap Decoder)

* **Mục tiêu:** Biến mỗi lần làm sai thành một khoảnh khắc giác ngộ sư phạm thay vì cảm giác thất bại.
* **Dữ liệu đưa vào (Inputs):**
  - Nội dung câu hỏi + bài đọc hoặc audio script tương ứng.
  - Đáp án đúng ($B$) và **chính xác phương án người học đã chọn** ($C$).
  - Hồ sơ điểm yếu gần đây của học viên (ví dụ: thường xuyên bị bẫy từ đồng âm Part 1/2, hoặc bẫy liên từ Part 5).
* **Dữ liệu LLM sinh ra (Outputs):**
  - **Cơ chế bẫy (The Trap Mechanism):** *"Bạn chọn C ('since') vì thường thấy từ này đi với thì Hiện tại hoàn thành. Tuy nhiên trong ngữ cảnh này, 'since' đóng vai trò liên từ chỉ nguyên nhân (= because), do đó mệnh đề chính bắt buộc phải dùng thì Quá khứ đơn."*
  - **Từ khóa neo (Anchor Keyword):** Định vị chính xác 1–2 từ khóa trong văn bản giúp nhận diện bẫy ngay lập tức trong lần gặp sau.
  - **Câu hỏi gợi mở (Socratic Prompt):** Một câu hỏi ngắn kích thích người học tự suy ngẫm quy tắc ngữ pháp/từ vựng liên quan.
* **Vị trí hiển thị:** Ngay dưới mỗi câu hỏi sai trên màn hình `Review` đề thi và màn hình `Saved Questions`, dưới dạng accordion hoặc nút bấm:
  `[ 💡 Hỏi Mentor: Vì sao câu này mình sai? ]`

---

### Bài toán 2: Nâng cấp Văn phong Tương tác (Native Upgrade Diff) cho Speaking & Writing

* **Mục tiêu:** Không chỉ làm giám khảo chấm điểm khắt khe, mà làm người thầy sửa bài tận tâm chỉ ra cách diễn đạt chuẩn bản xứ.
* **Dữ liệu đưa vào (Inputs):**
  - Đề bài (Prompt) và bài viết hoặc transcript nói của học viên.
  - Target band mục tiêu (ví dụ: học viên đang ở band 5.5, mục tiêu là 7.0).
  - Tiêu chí chấm chính thức (Task Achievement, Coherence, Lexical Resource, Grammar).
* **Dữ liệu LLM sinh ra (Outputs):**
  - **Bản viết lại nâng band (Native Upgrade):**
    - *Câu gốc:* "I think public transport is good because it reduces traffic jams."
    - *Bản nâng cấp (Band 7.0+):* "Investing in mass transit infrastructure is imperative to mitigating urban gridlock."
  - **Phân loại cải thiện trực quan (Color-coded Diff):**
    - 🔴 **Lỗi ngữ pháp/cấu trúc:** Cần sửa ngay.
    - 🟡 **Từ vựng đơn điệu/lặp từ:** Gợi ý synonym hoặc collocation tự nhiên.
    - 🟢 **Điểm sáng:** Lời khen cụ thể cho những chỗ học viên dùng từ hay hoặc phát triển ý tốt.
* **Vị trí hiển thị:** Màn hình `Speaking/Review` và `Writing/Review` với giao diện **Split-view** (cột trái là bài của bạn, cột phải là bài đã nâng cấp; người học có thể click vào từng câu/từ để xem lý do chỉnh sửa).

---

### Bài toán 3: "Đơn thuốc Hành động Hôm nay" trên Dashboard (Daily Actionable Prescription)

* **Mục tiêu:** Thay thế khối văn bản thống kê tĩnh bằng lời chào có cảm xúc và nhiệm vụ thực hành ngắn gọn.
* **Dữ liệu đưa vào (Inputs):**
  - Lịch sử làm bài từ các view SQL: `vw_UserSectionAccuracyAll`, `vw_UserCategoryAccuracy`.
  - Nhịp học (Streak số ngày học liên tiếp, thời gian từ lần làm bài gần nhất).
  - Khoảng cách điểm hiện tại tới mục tiêu (Score gap to target band).
* **Dữ liệu LLM sinh ra (Outputs):**
  - **Lời chào cá nhân hóa:** *"Chào Khoa! Tuần này bạn đã cải thiện 15% độ chính xác phần Nghe Part 1, nhưng bẫy Mệnh đề quan hệ ở Part 5 hôm qua vẫn khiến bạn đánh rơi 30 điểm đáng tiếc."*
  - **Micro-drill 3 phút:** Đề xuất ngay một thử thách nhỏ 3 câu tập trung đúng vào lỗ hổng vừa phát hiện.
* **Vị trí hiển thị:** Widget đầu trang Dashboard (`_AiInsightPanel.cshtml` được làm mới) với thanh tiến độ sinh động và nút bấm bắt đầu ngay thử thách.

---

### Bài toán 4: Chế độ Luyện Phản xạ Ngắn (Oral & Rapid Check-in)

* **Mục tiêu:** Giảm áp lực làm bài thi 120 phút; tạo thói quen luyện phản xạ tiếng Anh mỗi ngày 5–10 phút.
* **Dữ liệu đưa vào & xử lý:**
  - AI hỏi 1 câu ngắn thuộc chủ đề giao tiếp hoặc phỏng vấn (Part 1 Speaking).
  - Học viên ghi âm trả lời trong 30–45 giây.
  - SpeechSuper phân tích phát âm, Claude phản hồi nhanh bằng 1 nhận xét khen ngợi + 1 câu hỏi đào sâu tiếp theo.
* **Vị trí hiển thị:** Màn hình riêng `Quick Check-in / Mentor Mini-Session`.

---

## 4. Tinh chỉnh UI/UX để Thoát khỏi Cảm giác "Sandbox"

Bên cạnh sức mạnh của LLM, giao diện người dùng cần bổ sung các yếu tố thị giác và nhịp điệu tương tác:

1. **Thị giác & Âm thanh sống động:**
   - Khi phát audio nghe: Tích hợp visualizer sóng âm động (waveform audio player) thay cho thanh timeline đơn điệu.
   - Khi ghi âm: Hiển thị thanh đo mức âm lượng micro (VU meter) chuyển động theo giọng nói thực tế.
2. **Vòng lặp phản hồi tức thì (Instant Micro-Feedback Loops):**
   - Trong chế độ Practice (luyện tập kỹ năng): Bổ sung tùy chọn *"Xem giải thích ngay sau mỗi câu"* bên cạnh chế độ làm liên tục, giúp học viên học được ngay từ sai lầm mà không phải chờ làm xong cả bộ đề.
3. **Mềm hóa không gian học tập ngoài phòng thi:**
   - Duy trì sự nghiêm ngặt, kỷ luật không bo góc ở chế độ **Mock Exam** (đúng tính chất phòng thi).
   - Nhưng ở các màn hình **Practice, Lesson Walkthrough, Flashcards, và Review**, bổ sung khoảng thở (white-space), thẻ lật (flip cards), highlight từ khóa tương tác để giảm bớt sự căng thẳng không cần thiết.

---

## 5. Lộ trình Thực hiện Đề xuất

```
┌────────────────────────────────┐
│ Giai đoạn 1: Nền tảng sạch lỗi │
│ • Sửa dứt điểm Issue #82       │
│ • Hoàn thiện Studio audio      │
│ • Bảo đảm phân quyền & test    │
└───────────────┬────────────────┘
                │
                ▼
┌────────────────────────────────┐
│ Giai đoạn 2: Contextual Mentor │
│ • Socratic Trap Decoder        │
│   (Review & Saved Questions)   │
│ • Native Upgrade Diff          │
│   (Speaking & Writing Review)  │
└───────────────┬────────────────┘
                │
                ▼
┌────────────────────────────────┐
│ Giai đoạn 3: Sinh động hóa UI  │
│ • Dashboard Micro-drill        │
│ • Waveform audio player        │
│ • VU meter & Instant feedback  │
└────────────────────────────────┘
```

1. **Giai đoạn 1 (Ưu tiên hiện tại):** Xử lý toàn bộ các lỗi gãy vụn, routing 404, link admin, kiểm thử cơ sở dữ liệu để tạo nền tảng vững chắc.
2. **Giai đoạn 2:** Triển khai **Socratic Trap Decoder** và **Native Upgrade Diff** — hai tính năng mang lại giá trị học thuật trực tiếp và thuyết phục nhất cho học viên.
3. **Giai đoạn 3:** Hiện đại hóa UI với waveform âm thanh, micro-drill tương tác và cá nhân hóa trải nghiệm Dashboard.
