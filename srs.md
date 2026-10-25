# **TÀI LIỆU ĐẶC TẢ YÊU CẦU PHẦN MỀM (SRS)**

**Dự án:** Hệ thống Khảo thí & Luyện thi Trực tuyến Chuẩn Quốc tế (IELTS & TOEIC Mock Test Platform) **Phạm vi:** Chứng chỉ 2 Kỹ năng & 4 Kỹ năng (CBT / iBT / Adaptive Online)

## **1\. GIỚI THIỆU TỔNG QUAN (INTRODUCTION)**

### **1.1. Mục đích Tài liệu (Purpose)**

Tài liệu này định nghĩa toàn bộ yêu cầu chức năng (Functional Requirements), yêu cầu phi chức năng (Non-Functional Requirements), quy tắc nghiệp vụ khảo thí (Business Rules) và đặc tả giao diện người dùng (UI/UX Specifications) cho nền tảng thi thử IELTS và TOEIC trên máy tính.

### **1.2. Phạm vi Sản phẩm (Product Scope)**

Hệ thống cung cấp môi trường mô phỏng phòng thi máy tính sát thực tế cho 4 dòng bài thi cốt lõi:

> 1. **IELTS on Computer (Academic & General Training):** 4 kỹ năng (Listening, Reading, Writing thi nối tiếp trên máy; Speaking thi qua Video Call hoặc mô phỏng Cue Card/Giám khảo ảo).  
> 2. **TOEIC Listening & Reading (Standard CBT \- 2 Kỹ năng):** Bài thi tiêu chuẩn 200 câu trong 120 phút.  
> 3. **TOEIC Listening & Reading (Multi-Stage Adaptive / CAT Online \- 2 Kỹ năng):** Bài thi thích ứng nhiều giai đoạn 90 câu trong \~60 phút.  
> 4. **TOEIC Speaking & Writing (iBT \- 2 Kỹ năng, kết hợp với L\&R thành 4 Kỹ năng):** 11 câu Speaking (\~20 phút) và 8 câu Writing (60 phút).

### **1.3. Chế độ Vận hành Hệ thống (System Operating Modes)**

Hệ thống bắt buộc phân tách thành 2 chế độ vận hành độc lập ngay từ màn hình khởi tạo bài thi:

> * **Mode A – Strict Exam Mode (Thi thật):** Bắt buộc vào chế độ toàn màn hình (Fullscreen), nạp trước toàn bộ âm thanh, khóa mọi thanh tua audio, ẩn nút nộp bài sớm ở các phần thi không cho phép nộp sớm, và áp dụng nghiêm ngặt các giới hạn phím tắt của từng tổ chức khảo thí.  
> * **Mode B – Practice / Familiarisation Mode (Luyện tập):** Cho phép thi từng kỹ năng lẻ (Skill Test), từng Part lẻ (Mini Test), cho phép bấm nút Submit Test sớm trong menu tùy chọn, hỗ trợ giao diện nền tối (Dark Mode), và mở khóa trình phát lại âm thanh (kèm nút tua lùi 5 giây, Audio Transcript, giải thích đáp án) ở màn hình kết quả.

## **2\. KIẾN TRÚC MÁY TRẠNG THÁI & LUỒNG ĐIỀU HƯỚNG (STATE MACHINE & NAVIGATION ENGINE)**

Hệ thống không sử dụng chung một luồng điều hướng cho mọi bài thi mà phải triển khai một **Configurable State Machine Engine** kiểm soát ở 2 cấp độ: cấp độ Kỹ năng (Macro-State) và cấp độ Câu hỏi (Micro-State).

### **2.1. Quy tắc Chuyển giao Kỹ năng Cấp độ Vĩ mô (Macro-Level Rules)**

> * **BR-NAV-01 (Macro-Linearity):** Thứ tự các kỹ năng trong một bài thi Full Test là cố định tuyệt đối:  
  * **IELTS:** Listening → Reading → Writing (Speaking tách phiên riêng).  
  * **TOEIC L\&R:** Listening → Reading.  
  * **TOEIC S\&W:** Speaking → Writing.  
> * **BR-NAV-02 (Permanent Section Lock):** Khi thời gian của một kỹ năng (hoặc một Unit trong TOEIC CAT) kết thúc hoặc khi thí sinh xác nhận chuyển sang phần tiếp theo, phân đoạn cũ sẽ chuyển sang trạng thái LOCKED vĩnh viễn. Thí sinh không thể xem lại hay sửa đáp án của phần đã khóa.  
> * **BR-NAV-03 (No Time Rollover):** Thời gian còn dư ở một kỹ năng, một câu hỏi độc lập hoặc một Unit (khi thí sinh bấm chuyển sớm) sẽ bị hủy bỏ hoàn toàn, tuyệt đối không được cộng dồn sang phần hoặc câu hỏi tiếp theo.

### **2.2. Ma trận Chế độ Điều hướng Cấp độ Vi mô (Micro-Navigation Modes)**

| Mã Chế độ (Enum) | Áp dụng cho Phân hệ | Nút Next / Back | Nhảy câu (Jump / Palette) | Gắn cờ (Review) & Đổi đáp án | Quy tắc Chuyển câu & Khóa (Transition Logic) |
| :---- | :---- | :---- | :---- | :---- | :---- |
| FULL\_SANDBOX | IELTS Listening, IELTS Reading, IELTS Writing, TOEIC Reading (Standard) | Mở cả Next & Back | Mở toàn bộ câu trong Section | Cho phép gắn cờ & đổi đáp án không giới hạn | Trong IELTS Listening, thí sinh được bấm nhảy sang xem và highlight câu hỏi ở Part 2, 3, 4 trong khi audio vẫn đang phát ở Part 1 mà không làm ngắt hay nhảy file audio. |
| UNIT\_LOCKED\_SANDBOX | TOEIC L\&R Online Adaptive (CAT \- Reading) | Mở Next & Back trong nội bộ Unit | Mở thông qua Review Screen của Unit | Có hộp tích Mark item for review và bộ lọc câu chưa làm | Chia làm Unit 1 (25 câu) và Unit 2 (20 câu thích ứng). Tự do sửa trong Unit 1; khi bấm Next Unit → Finish Unit, khóa cứng Unit 1 và mở Unit 2\. |
| AUDIO\_DRIVEN | TOEIC Listening (Standard CBT) | Khóa Back (Tiến tới theo băng) | Khóa nhảy tự do | Chỉ chọn/đổi khi câu hỏi đang hiển thị trên màn hình | Hệ thống tự động hiển thị câu hỏi/hình ảnh đồng bộ với mốc thời gian (timestamp) của luồng âm thanh 45 phút. |
| SYSTEM\_PACED\_AUTO | TOEIC Speaking (Q1–Q11) | Khóa hoàn toàn Next & Back | Khóa hoàn toàn | Không cho phép làm lại hoặc thu âm lại | Hệ thống tự động chạy: Prep Timer → Phát tiếng Beep → Recording Timer → Tự động lưu audio và đẩy sang câu kế tiếp. |
| HYBRID\_GROUP\_LOCKED | TOEIC Writing (Q1–Q8) | Q1–5: Mở Next & Back Q6–8: Mở Next, Khóa Back | Q1–5: Di chuyển tuần tự trong 5 câu Q6–8: Khóa | Q1–5: Sửa tự do trong 8 phút Q6–8: Khóa câu cũ khi đã Next | Cụm Q1–5 dùng chung 8 phút (không tự nhảy câu). Ở Q6 (10p), Q7 (10p), Q8 (30p): Bấm Next sớm sẽ bật Modal xác nhận (\[Back\] để ở lại, \[Next\] để đi tiếp và khóa vĩnh viễn câu hiện tại). |

## **3\. ĐẶC TẢ YÊU CẦU CHỨC NĂNG CHI TIẾT (FUNCTIONAL REQUIREMENTS)**

### **3.1. Phân hệ Tiền kiểm & Khởi tạo Phòng thi (FR-PRE: Pre-Flight & Onboarding)**

> * **FR-PRE-01 (Audio Preflight & Buffering):** Trước khi mở khóa nút \[Start Test\] của bất kỳ phần thi Listening hoặc Speaking nào, Frontend phải tải trước (fetch & buffer) và kiểm tra kết nối thành công tới toàn bộ các URL âm thanh của bài thi. Nếu bất kỳ file audio nào lỗi, hệ thống chặn bắt đầu bài thi và hiển thị thông báo tải lại, ngăn chặn tuyệt đối rủi ro đứt mạng giữa chừng ở Part 3/Part 4 (do bài thi thật không cho phép tạm dừng audio).  
> * **FR-PRE-02 (Microphone Check Workflow):** Trước khi vào phần thi Speaking, hệ thống hiển thị màn hình hướng dẫn vị trí đặt mic và yêu cầu thí sinh thu âm thử một câu ngắn. Sau khi thu, thí sinh phải bấm nghe lại (Playback) và chọn \[Yes\] (âm thanh rõ, cho phép đi tiếp) hoặc \[No\] (thu âm thử lại).  
> * **FR-PRE-03 (Post-Speaking Audio Verification):** Ngay sau khi kết thúc câu hỏi cuối cùng của TOEIC Speaking (Câu 11), hệ thống hiển thị màn hình kiểm tra danh sách file ghi âm của các câu trả lời để thí sinh nghe lại và xác nhận dữ liệu âm thanh không bị lỗi trước khi nộp lên máy chủ.

### **3.2. Phân hệ Giao diện & Công cụ Khảo thí IELTS on Computer (FR-IELTS)**

> * **FR-IELTS-01 (Top Control Bar):** Thanh Header cố định trên cùng bao gồm:  
  * **Countdown Timer:** Đặt tại chính giữa thanh Header. Khi thời gian còn đúng **10 phút** và **5 phút**, đồng hồ phải nhấp nháy màu đỏ (flash red) đồng thời hiển thị một banner thông báo cảnh báo ngang phía trên trước khi quay lại đếm ngược bình thường.  
  * **Volume Slider:** Thanh trượt điều chỉnh âm lượng nằm ở góc trên bên phải trong suốt phần thi Listening.  
  * **Settings / Accessibility Modal:** Cho phép đổi trực tiếp kích thước phông chữ (*Standard, Large, Extra Large*) và độ tương phản màu sắc (*Black on White, White on Black, Yellow on Black*) ngay trong lúc làm bài mà không tải lại trang hay làm gián đoạn bộ đếm giờ.  
  * **Help Modal:** Nút \[Help\] hiển thị hướng dẫn làm bài của phần hiện tại.  
> * **FR-IELTS-02 (Workspace Layout):**  
  * **Listening Workspace:** Khung hiển thị đơn toàn màn hình, hỗ trợ cuộn dọc. Đối với dạng bài điền bảng (Table/Note completion) có nhiều cột, thứ tự tab/focus phải hỗ trợ rõ ràng chiều từ trái sang phải rồi từ trên xuống dưới, kèm viền sáng (active focus border) tại ô input tương ứng với câu đang chọn ở thanh Palette.  
  * **Reading & Writing Workspace (Vertical Split-Screen):** Chia đôi màn hình theo chiều dọc thành 2 khung có thanh cuộn (scrollbar) hoạt động hoàn toàn độc lập. Khung trái giữ cố định văn bản bài đọc (Reading Passage) hoặc đề bài/biểu đồ (Writing Prompt); khung phải chứa danh sách câu hỏi hoặc ô soạn thảo bài viết (Candidate Response Box) tự động cuộn khi gõ dài.  
> * **FR-IELTS-03 (Bottom Question Palette):** Thanh điều hướng cố định dưới đáy màn hình hiển thị toàn bộ 40 câu hỏi (chia theo Part 1–4 cho Listening; Passage 1–3 cho Reading) hoặc 2 tab (Task 1 & Task 2 cho Writing). Hỗ trợ nút mũi tên \[Previous\] / \[Next\], nút gắn cờ \[Review\] và cập nhật trạng thái màu sắc/ký hiệu theo thời gian thực cho 4 trạng thái: *Chưa trả lời*, *Đã trả lời*, *Đang chọn*, và *Đánh dấu Review*.  
> * **FR-IELTS-04 (Highlight & Note-Taking Engine):**  
  * Trong phần Listening và Reading, khi người dùng quét chọn (select) văn bản và nhấp chuột phải (Right-click), hệ thống hiển thị Context Menu gồm: Highlight (tô nền vàng), Notes (tô sáng kèm mở popup ghi chú cá nhân), Clear và Clear All.  
  * **Yêu cầu kỹ thuật bắt buộc:** Không lưu trữ vị trí Highlight bằng đối tượng DOM Range trực tiếp. Toàn bộ văn bản bài đọc và câu hỏi phải được tách trước (tokenize) thành các thẻ từ (word spans) gắn chỉ số nguyên toàn cục cố định ({ start: number, end: number }). Trạng thái Highlight phải được render từ mảng chỉ số này để đảm bảo không bị lệch hoặc mất vùng bôi sáng khi React/Vue re-render hoặc khi thí sinh đổi cỡ chữ và màu nền giữa giờ thi.  
> * **FR-IELTS-05 (IELTS Writing Editor):**  
  * Khung nhập liệu văn bản thuần nhiều dòng (multi-line plain text), cập nhật **Live Word Counter** ngay bên dưới sau mỗi thao tác gõ phím (onKeyUp/onInput).  
  * Cho phép sử dụng các phím tắt hệ điều hành tiêu chuẩn: Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z và kéo thả chuột để bôi đen văn bản.  
  * Bắt buộc tắt hoàn toàn tính năng kiểm tra chính tả và gợi ý từ của trình duyệt (spellcheck="false", autocorrect="off", autocomplete="off").  
> * **FR-IELTS-06 (IELTS Listening Timing Policy):** Không áp dụng 10 phút chép đáp án của bài thi giấy; hệ thống phát audio \~30–34 phút (bao gồm các khoảng nghỉ ngắn đọc đề trước mỗi Part và 20–30 giây rà soát sau mỗi Part), sau khi kết thúc băng sẽ kích hoạt đồng hồ đếm ngược đúng **2 phút** cuối giờ để thí sinh kiểm tra chính tả trước khi tự động khóa và nộp bài.

### **3.3. Phân hệ Giao diện & Công cụ Khảo thí TOEIC (FR-TOEIC)**

> * **FR-TOEIC-01 (TOEIC Listening Display Rules \- 100 Câu / 45 Phút):**  
  * **Part 1 (Q1–6 \- Photographs):** Chỉ hiển thị bức ảnh và 4 nút radio (A), (B), (C), (D); tuyệt đối ẩn văn bản của 4 câu mô tả trong lúc thi. Hình ảnh xuất hiện trên màn hình vài giây trước khi băng bắt đầu đọc.  
  * **Part 2 (Q7–31 \- Question-Response):** Chỉ hiển thị số thứ tự câu hỏi và đúng **3 nút radio** (A), (B), (C); ẩn hoàn toàn nội dung câu hỏi và câu trả lời.  
  * **Part 3 (Q32–70 \- Conversations) & Part 4 (Q71–100 \- Short Talks):** Hiển thị theo từng cụm **3 câu hỏi trên cùng một màn hình** (kèm đầy đủ văn bản câu hỏi, 4 lựa chọn A, B, C, D và bảng biểu/đồ thị đính kèm nếu câu hỏi thuộc dạng Graphic questions); ẩn nội dung đoạn băng hội thoại/bài nói.  
> * **FR-TOEIC-02 (TOEIC Reading Workspace & Review Screen \- 100 Câu / 75 Phút):**  
  * **Part 5 (Q101–130):** Hiển thị từng câu đơn khuyết từ kèm 4 lựa chọn A, B, C, D.  
  * **Part 6 (Q131–146):** Hiển thị đoạn văn có 4 chỗ trống đánh số kèm 4 câu hỏi tương ứng trên màn hình chia đôi.  
  * **Part 7 (Q147–200):** Màn hình chia đôi; khung trái hiển thị 1 văn bản (Single Passages: Q147–175), 2 văn bản (Double Passages: Q176–185) hoặc 3 văn bản (Triple Passages: Q186–200), khung phải hiển thị bộ 2–5 câu hỏi tương ứng.  
  * **Review Screen:** Cung cấp nút \[Review\] mở bảng tổng hợp trạng thái toàn bộ câu hỏi trong phần Reading (hoặc trong Unit hiện tại đối với bản CAT), tích hợp 3 nút chức năng lọc nhanh: *Go back to the first unanswered item* (Nhảy về câu trống đầu tiên), *Review only unanswered items* (Chỉ duyệt các câu chưa làm), và *Review only items marked for review* (Chỉ duyệt các câu đã gắn cờ).  
> * **FR-TOEIC-03 (TOEIC Speaking Automation \- 11 Câu / \~20 Phút):**  
  * **Q1–2 (Read a text aloud):** Hiển thị đoạn văn → Đếm ngược 45s chuẩn bị → Phát tiếng Beep → Thu âm 45s.  
  * **Q3–4 (Describe a picture):** Hiển thị hình ảnh → Đếm ngược 45s chuẩn bị → Beep → Thu âm 30s.  
  * **Q5–7 (Respond to questions):** Phát audio đọc câu hỏi đồng thời hiển thị câu hỏi trên màn hình → Đếm ngược 3s chuẩn bị → Beep → Thu âm 15s (cho Q5, Q6) và 30s (cho Q7).  
  * **Q8–10 (Respond using information provided):** Hiển thị bảng thông tin (lịch trình/thông báo) và đếm ngược 45s để đọc. Sau đó giữ nguyên bảng thông tin trên màn hình, hệ thống lần lượt phát audio từng câu hỏi Q8, Q9, Q10 (không in sẵn chữ của câu hỏi trước khi phát) → Mỗi câu có 3s chuẩn bị → Beep → Thu âm 15s (Q8, Q9) và 30s (Q10).  
  * **Q11 (Express an opinion):** Phát audio và hiển thị đề bài → Đếm ngược 30s chuẩn bị → Beep → Thu âm 60s.  
> * **FR-TOEIC-04 (TOEIC Writing Interface & Toolbar Policy \- 8 Câu / 60 Phút):**  
  * **Quy định Màn hình Directions & Cụm Q1–5:** Khi bắt đầu phần Writing, màn hình hiển thị hướng dẫn (Directions) của cụm Q1–5 trong 1 phút; đồng hồ góc trên bên phải đếm lùi từ 09:00 và tự động mở Câu 1 khi chạm mốc 08:00. Trong 8 phút này, giao diện hiển thị bức ảnh kèm 2 từ/cụm từ bắt buộc bên dưới; thí sinh dùng nút \[NEXT\] và \[BACK\] để chuyển qua lại tự do giữa 5 câu.  
  * **Cụm Q6, Q7 (Respond to a written request) & Q8 (Opinion Essay):** Q6 chạy đồng hồ 10:00 riêng; Q7 chạy đồng hồ 10:00 riêng; Q8 chạy đồng hồ 30:00 riêng. Ở Q8, hiển thị thêm nút \[Complete and End Writing Test\] để nộp bài sớm (kèm màn hình xác nhận).  
  * **Chính sách Trình soạn thảo TOEIC (Strict Editor Policy):**  
    * **Vô hiệu hóa phím tắt bàn phím:** Chặn hoàn toàn các sự kiện onCopy, onCut, onPaste từ tổ hợp phím Ctrl+C, Ctrl+X, Ctrl+V trên bàn phím trong chế độ thi thật.  
    * **Thanh công cụ On-Screen:** Hiển thị 4 nút chức năng trực tiếp phía trên vùng gõ văn bản: \[Cut\], \[Paste\], \[Undo\], \[Redo\]. Thí sinh phải quét chọn chữ bằng chuột, bấm nút \[Cut\] trên màn hình, đặt con trỏ vào vị trí mới và bấm nút \[Paste\].  
    * **Tổ hợp phím Hoa/Thường:** Hỗ trợ tổ hợp \[Shift\] \+ \[Caps Lock\] để chuyển đổi trạng thái viết hoa/viết thường.  
    * **Màn hình HELP:** Khi bấm nút \[HELP\], mở giao diện hướng dẫn có nút \[RETURN\] ở góc trên bên phải để quay lại bài làm; đồng hồ đếm ngược của câu hỏi hiện tại bắt buộc vẫn phải chạy ngầm không dừng lại.

### **3.4. Phân hệ Hiển thị Câu hỏi & Chấm điểm Tự động (FR-SCORE)**

> * **FR-SCORE-01 (Interactive Question Components):** Hệ thống Frontend phải hỗ trợ đầy đủ 11 dạng component câu hỏi chuẩn của IELTS và TOEIC:  
  1. *Single Choice Radio (4 options A–D)* – Dùng cho cả IELTS và TOEIC Part 1, 3, 4, 5, 6, 7\.  
  2. *Single Choice Radio (3 options A–C)* – Dùng riêng cho TOEIC Listening Part 2\.  
  3. *Multi-Select Checkbox Group* – Dùng cho IELTS Multiple choice chọn nhiều hơn 1 đáp án (giới hạn số lượng tích chọn theo đúng yêu cầu đề, ví dụ: *Choose TWO letters*).  
  4. *Inline Text Input* – Điền từ vào chỗ trống nằm ngay trong đoạn văn, ghi chú, bảng biểu hoặc sơ đồ (IELTS Completion tasks).  
  5. *Select Dropdown Menu* – Chọn đáp án từ danh sách thả xuống cho các dạng Matching information, Matching sentence endings, True/False/Not Given, Yes/No/Not Given.  
  6. *Drag-and-Drop Zone* – Kéo thẻ tiêu đề/nhãn thả vào đúng ô trống trên bài đọc hoặc bản đồ (Matching headings, Plan/map/diagram labelling).  
> * **FR-SCORE-02 (Validation & Objective Grading Rules):**  
  * **Không trừ điểm câu sai:** Câu trả lời sai và câu bỏ trống đều nhận 0 điểm.  
  * **Kiểm tra Chính tả & Ngữ pháp (IELTS L\&R):** Chuẩn hóa chuỗi nhập vào (trim() khoảng trắng thừa hai đầu, không phân biệt hoa/thường trừ khi quy định riêng), nhưng bắt buộc khớp chính xác 100% ký tự chính tả và hình thái số ít/số nhiều so với bộ đáp án chuẩn.  
  * **Kiểm tra Giới hạn Từ (Word Limit Validator):** Tự động đếm số từ và số trong câu trả lời điền khuyết của IELTS (ví dụ: quy tắc MAX\_WORDS \= 2, ALLOW\_NUMBER \= true); nếu thí sinh nhập vượt quá giới hạn quy định, hệ thống chấm 0 điểm cho câu đó.  
> * **FR-SCORE-03 (Rubric & Score Conversion):**  
  * **IELTS Band Calculation:** Quy đổi điểm thô (0–40) của Listening và Reading ra thang điểm Band 1.0 – 9.0 (bước nhảy 0.5). Đối với Writing, điểm tổng được tính theo công thức trọng số:  
    Band Writing=RoundHalf(3Band Task 1+2×Band Task 2​)  
    trong đó Task 2 có trọng số gấp đôi Task 1\. Hệ thống AI/Giám khảo tự động gắn cờ cảnh báo phạt điểm nếu bài viết dưới 150 từ (Task 1\) hoặc dưới 250 từ (Task 2).  
  * **TOEIC L\&R Scaled Score:** Quy đổi điểm thô (0–100 mỗi kỹ năng) sang thang điểm 5 – 495 (bước nhảy 5 điểm), tổng điểm toàn bài từ 10 – 990\.  
  * **TOEIC S\&W Score & Keyword Check:** Mỗi kỹ năng Speaking và Writing quy đổi độc lập sang thang điểm 0 – 200\. Riêng đối với TOEIC Writing Câu 1–5 (thang điểm thô 0 – 3 điểm/câu), bộ chấm điểm phải tích hợp bộ kiểm tra hình thái từ (Lemmatizer/Stemmer) để xác minh câu trả lời của thí sinh là một câu hoàn chỉnh và có chứa đủ 2 từ/cụm từ bắt buộc của đề bài (cho phép biến đổi thì/dạng từ và đảo vị trí).

## **4\. YÊU CẦU PHI CHỨC NĂNG & XỬ LÝ NGOẠI LỆ (NON-FUNCTIONAL REQUIREMENTS)**

> * **NFR-TIME-01 (Wall-Clock Deadline Synchronization):**  
  * Hệ thống tuyệt đối không sử dụng biến đếm giảm dần đơn thuần qua setInterval trên trình duyệt vì trình duyệt sẽ tự động làm chậm (throttle) bộ đếm khi tab bị ẩn hoặc khi máy tính rơi vào trạng thái Sleep, dẫn đến sai lệch thời gian thi.  
  * Khi bắt đầu mỗi phần thi hoặc mỗi câu hỏi có giới hạn thời gian, hệ thống phải xác định mốc thời gian kết thúc tuyệt đối (Deadline Timestamp=*t*start​\+Δ*t*duration​) và tính toán thời gian còn lại ở mỗi chu kỳ render theo công thức:  
    Remaining Time=max(0,Deadline Timestamp−Current Wall-Clock Time)  
    đảm bảo khi đồng hồ chạm 00:00, hệ thống khóa và thu bài ngay lập tức.  
> * **NFR-SESS-01 (Real-Time Auto-Save & Crash Recovery):**  
  * Mọi sự kiện chọn đáp án (onChange) hoặc gõ văn bản (debounced 500ms kết hợp LocalStorage / IndexedDB và đồng bộ qua API về Server) phải được lưu tự động mà không cần nút Save.  
  * Nếu thí sinh gặp sự cố sập trình duyệt, mất điện hoặc rớt mạng giữa chừng, khi đăng nhập lại và nhập Mã dự thi (*Authorization Code*), hệ thống hiển thị nút \[Resume Test\] để khôi phục phiên thi tại đúng câu hỏi, trạng thái audio/unit và thời gian còn lại lúc xảy ra sự cố.  
  * Bắt sự kiện beforeunload của trình duyệt để cảnh báo và ngăn thí sinh vô tình đóng tab trước khi hệ thống chuyển sang màn hình Hiển thị Điểm (*Score Display Screen*) ở cuối bài thi.  
> * **NFR-UI-01 (Responsiveness & Viewport Lock):** Giao diện phòng thi được thiết kế dạng App-Shell chiếm trọn chiều cao màn hình (100vh, overflow: hidden ở tầng body), chỉ cho phép cuộn độc lập bên trong vùng chứa bài đọc (Left Pane) và vùng chứa câu hỏi/khung soạn thảo (Right Pane).

## **5\. ĐẶC TẢ CẤU TRÚC DỮ LIỆU ĐỀ THI (JSON CONFIGURATION SCHEMA)**

Để Backend và Frontend giao tiếp đồng nhất cho mọi loại chứng chỉ mà không cần viết mã cứng (hardcode) cho từng bài thi, đối tượng cấu hình của mỗi Section hoặc Part/Unit trả về từ API cần tuân thủ bảng thuộc tính chuẩn dưới đây:

| Tên Trường (Field Name) | Kiểu Dữ Liệu | Giá Trị Hợp Lệ / Ví Dụ | Mô Tả Nghiệp Vụ Xử Lý Tại Frontend |
| :---- | :---- | :---- | :---- |
| examType | String | "IELTS\_ACADEMIC", "IELTS\_GT", "TOEIC\_LR\_STD", "TOEIC\_LR\_CAT", "TOEIC\_SW" | Định danh loại chứng chỉ để nạp bộ giao diện (Theme/Layout) và công thức quy đổi điểm tương ứng. |
| deliveryMode | String | "STRICT\_EXAM", "PRACTICE" | Xác định việc bật Fullscreen, khóa thanh tua audio và ẩn/hiện nút nộp bài sớm. |
| navigationMode | String | "FULL\_SANDBOX", "UNIT\_LOCKED\_SANDBOX", "AUDIO\_DRIVEN", "SYSTEM\_PACED\_AUTO", "HYBRID\_GROUP\_LOCKED" | Điều khiển trạng thái đóng/mở của nút Next, Back, thanh Question Palette và màn hình Review Screen. |
| timerPolicy | Object | { type: "SECTION\_SHARED" \| "AUDIO\_PLUS\_CHECK" \| "ITEM\_FIXED", durationSec: 3600, checkTimeSec: 120, warnAtSec: \[600, 300\] } | Cấu hình đồng hồ đếm ngược theo mốc Wall-Clock, thời gian kiểm tra cộng thêm (2 phút cuối IELTS Listening) và mốc nháy đỏ cảnh báo. |
| layoutType | String | "FULL\_WIDTH", "VERTICAL\_SPLIT\_SCREEN" | Quyết định hiển thị 1 khung toàn màn hình hay chia đôi màn hình độc lập thanh cuộn. |
| toolsConfig | Object | { allowHighlight: true, allowNotes: true, showWordCount: true, disableSpellCheck: true, allowKeyboardCutPaste: false, showOnScreenCutPaste: true } | Bật/tắt menu chuột phải Highlight/Note, bộ đếm từ, chặn phím tắt Ctrl+C/V/X và hiển thị thanh nút bấm \[Cut\], \[Paste\], \[Undo\], \[Redo\] cho TOEIC Writing. |
| confirmOnNext | Boolean | true (cho TOEIC Writing Q6–8 & TOEIC CAT Unit transition), false (cho các phần còn lại) | Khi bằng true, bấm nút Next sẽ bật Modal xác nhận trước khi khóa vĩnh viễn câu hoặc Unit hiện tại. |

---

