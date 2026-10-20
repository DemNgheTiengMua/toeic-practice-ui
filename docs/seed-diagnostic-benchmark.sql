/* ==========================================================================================
   FILE: seed-diagnostic-benchmark.sql
   TARGET: SQL Server / LocalDB (T-SQL)
   PURPOSE: Seed 100% Production-Ready Data for TOEIC 3-Skill Diagnostic Placement Exam 
            ("Diagnostic Benchmark 45M") & Dynamic Learning Path ("TOEIC Diagnostic Sprint")
   AUTHOR: Principal Database Engineer & ETS Assessment Specialist
   DATE: 2026
   ========================================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* ======================================================================================
       0. TRA CỨU & ĐẢM BẢO DỮ LIỆU NỀN TẢNG (CERTIFICATES, SKILLS, SECTIONS)
       ====================================================================================== */
    DECLARE @toeicCert INT;
    DECLARE @toeicList INT, @toeicRead INT, @toeicWrit INT;
    DECLARE @secP1 INT, @secP2 INT, @secP3 INT, @secP4 INT;
    DECLARE @secP5 INT, @secP6 INT, @secP7 INT;
    DECLARE @secW2 INT, @secW3 INT;

    -- 0.1. Tra cứu Certificate TOEIC
    SELECT @toeicCert = CertId FROM dbo.Certificates WHERE Code = N'TOEIC';
    IF @toeicCert IS NULL
    BEGIN
        RAISERROR(N'Không tìm thấy chứng chỉ TOEIC (Code = ''TOEIC'') trong bảng Certificates.', 16, 1);
    END

    -- 0.2. Tra cứu Skills của TOEIC
    SELECT @toeicList = SkillId FROM dbo.Skills WHERE CertId = @toeicCert AND Code = 'LIST';
    SELECT @toeicRead = SkillId FROM dbo.Skills WHERE CertId = @toeicCert AND Code = 'READ';
    SELECT @toeicWrit = SkillId FROM dbo.Skills WHERE CertId = @toeicCert AND Code = 'WRIT';

    -- 0.3. Tra cứu các Sections của TOEIC
    SELECT @secP1 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P1';
    SELECT @secP2 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P2';
    SELECT @secP3 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P3';
    SELECT @secP4 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P4';
    SELECT @secP5 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P5';
    SELECT @secP6 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P6';
    SELECT @secP7 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P7';
    SELECT @secW2 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'W2';
    SELECT @secW3 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'W3';

    IF @secP1 IS NULL OR @secP2 IS NULL OR @secP3 IS NULL OR @secP4 IS NULL 
       OR @secP5 IS NULL OR @secP6 IS NULL OR @secP7 IS NULL 
       OR @secW2 IS NULL OR @secW3 IS NULL
    BEGIN
        RAISERROR(N'Thiếu một hoặc nhiều Section (P1..P7, W2, W3) của chứng chỉ TOEIC trong bảng Sections.', 16, 1);
    END

    /* ======================================================================================
       PHẦN 1: BÀI THI CHẨN ĐOÁN 3 KỸ NĂNG ("Diagnostic Benchmark 45M")
       ====================================================================================== */
    DECLARE @examId INT;
    DECLARE @examName NVARCHAR(200) = N'Diagnostic Benchmark 45M';

    IF NOT EXISTS (SELECT 1 FROM dbo.Exams WHERE CertId = @toeicCert AND Name = @examName)
    BEGIN
        INSERT INTO dbo.Exams (
            CertId, Name, Purpose, Status, DurationMinutes, QuestionCount, Description, SourceName
        )
        VALUES (
            @toeicCert,
            @examName,
            'placement',
            'published',
            45,
            32,
            N'Bài thi chẩn đoán toàn diện 3 kỹ năng (Nghe, Đọc, Viết) được thiết kế để phân tích lỗ hổng kiến thức, đo lường năng lực CEFR ban đầu và tự động kiến tạo lộ trình học tập tối ưu.',
            N'ETS Benchmark Suite 2026'
        );
    END

    SELECT @examId = ExamId FROM dbo.Exams WHERE CertId = @toeicCert AND Name = @examName;

    /* --------------------------------------------------------------------------------------
       1.1. EXAM SECTION AUDIO (LISTENING P1, P2, P3, P4)
       -------------------------------------------------------------------------------------- */
    IF NOT EXISTS (SELECT 1 FROM dbo.ExamSectionAudio WHERE CertId = @toeicCert AND ExamId = @examId AND SectionId = @secP1)
        INSERT INTO dbo.ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
        VALUES (@examId, @secP1, @toeicCert, N'/audio/toeic/placement-p1.mp3', 180, 2880000);

    IF NOT EXISTS (SELECT 1 FROM dbo.ExamSectionAudio WHERE CertId = @toeicCert AND ExamId = @examId AND SectionId = @secP2)
        INSERT INTO dbo.ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
        VALUES (@examId, @secP2, @toeicCert, N'/audio/toeic/placement-p2.mp3', 300, 4800000);

    IF NOT EXISTS (SELECT 1 FROM dbo.ExamSectionAudio WHERE CertId = @toeicCert AND ExamId = @examId AND SectionId = @secP3)
        INSERT INTO dbo.ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
        VALUES (@examId, @secP3, @toeicCert, N'/audio/toeic/test1-p3.mp3', 600, 9600000);

    IF NOT EXISTS (SELECT 1 FROM dbo.ExamSectionAudio WHERE CertId = @toeicCert AND ExamId = @examId AND SectionId = @secP4)
        INSERT INTO dbo.ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
        VALUES (@examId, @secP4, @toeicCert, N'/audio/toeic/test1-p4.mp3', 600, 9600000);

    DECLARE @qId INT;
    /* --------------------------------------------------------------------------------------
       1.2. LISTENING - PART 1: PHOTOGRAPHS (2 CÂU: Q1 - Q2, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP1_1 INT, @grpP1_2 INT;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP1 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript, 
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP1, 1, NULL,
            N'(A) They are boarding a commuter train. (B) They are examining a blueprint on the desk. (C) They are hanging a picture on the wall. (D) The projector screen is being rolled up.',
            N'/img/part1-sample.svg',
            N'Two engineers standing near an office desk looking closely at an architectural blueprint',
            N'/audio/toeic/placement-p1.mp3', 0, 18000
        );
    END
    SELECT @grpP1_1 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP1 AND DisplayOrder = 1;

    -- Câu 1 (P1)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP1_1, @examId, @secP1, @toeicList,
            N'Look at the picture marked Number 1 in your test book and select the statement that best describes what you see.',
            2, 'mcq',
            N'Hai nhân vật trong ảnh đang cúi nhìn vào bản thiết kế trải trên bàn làm việc (examining a blueprint on the desk).',
            1
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) They are boarding a commuter train.', 0, N'Bẫy đồng âm (Homophone trap): Từ "boarding" dễ gây nhầm lẫn âm thanh với "board" (bảng/bàn họp) trong phòng.'),
        (@qId, 'mcq', 'B', N'(B) They are examining a blueprint on the desk.', 1, N'Đáp án đúng: Mô tả chính xác hành động của hai người đang xem xét bản thiết kế trên mặt bàn.'),
        (@qId, 'mcq', 'C', N'(C) They are hanging a picture on the wall.', 0, N'Bẫy hành động sai (Wrong action trap): Có bức tranh/biểu đồ trên tường nhưng đã được treo sẵn, không có ai đang treo.'),
        (@qId, 'mcq', 'D', N'(D) The projector screen is being rolled up.', 0, N'Bẫy bị động hiện tại tiếp diễn (being + V-ed): Màn chiếu đang ở trạng thái tĩnh, không có tác động cuộn lên.');

    -- QuestionGroup cho P1 Câu 2
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP1 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript, 
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP1, 2, NULL,
            N'(A) Some chairs are stacked in the corner. (B) A technician is repairing the light fixture. (C) Documents are being distributed to attendees. (D) Monitor screens are arranged in a row on the table.',
            N'/img/part1-sample.svg',
            N'An empty modern conference room with computer monitors lined up neatly along the table',
            N'/audio/toeic/placement-p1.mp3', 18000, 36000
        );
    END
    SELECT @grpP1_2 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP1 AND DisplayOrder = 2;

    -- Câu 2 (P1)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP1_2, @examId, @secP1, @toeicList,
            N'Look at the picture marked Number 2 in your test book and select the statement that best describes what you see.',
            2, 'mcq',
            N'Tranh tả vật trong phòng họp trống: các màn hình máy tính được xếp thành hàng trên bàn (arranged in a row).',
            2
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 2;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) Some chairs are stacked in the corner.', 0, N'Bẫy trạng thái sai: Ghế được xếp quanh bàn làm việc chứ không chồng lên nhau (stacked) ở góc phòng.'),
        (@qId, 'mcq', 'B', N'(B) A technician is repairing the light fixture.', 0, N'Bẫy suy diễn chủ thể không tồn tại: Trong hình hoàn toàn không có kỹ thuật viên nào.'),
        (@qId, 'mcq', 'C', N'(C) Documents are being distributed to attendees.', 0, N'Bẫy "being + V3/ed" cho tranh tĩnh: Không có người tham dự nên không thể có hành động đang phát tài liệu.'),
        (@qId, 'mcq', 'D', N'(D) Monitor screens are arranged in a row on the table.', 1, N'Đáp án đúng: Diễn tả chính xác trạng thái tĩnh của các màn hình máy tính xếp dọc theo bàn.');
    /* --------------------------------------------------------------------------------------
       1.3. LISTENING - PART 2: QUESTION-RESPONSE (4 CÂU: Q3 - Q6, CHỈ 3 OPTIONS A-C)
       -------------------------------------------------------------------------------------- */
    -- Câu 3 (P2)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
        VALUES (@examId, @toeicCert, @secP2, 3, N'/audio/toeic/placement-p2.mp3');
    END
    DECLARE @grpQ3 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 3);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ3, @examId, @secP2, @toeicList,
            N'When is the quarterly budget proposal due? — Listen and choose the best response (A, B, or C).',
            2, 'mcq',
            N'Câu hỏi "When" nhưng được trả lời gián tiếp bằng cách đẩy trách nhiệm kiểm tra sang lịch trình mà giám đốc tài chính đã gửi.',
            3
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 3;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) Yes, the proposal was approved.', 0, N'Bẫy Yes/No cho câu hỏi Wh- (When) kết hợp bẫy lặp từ "proposal" (Same-word trap).'),
        (@qId, 'mcq', 'B', N'(B) Check the schedule Ms. Lin e-mailed this morning.', 1, N'Đáp án đúng: Phản hồi gián tiếp (Indirect response) chuẩn ETS — chỉ dẫn người hỏi nơi tra cứu thời hạn.'),
        (@qId, 'mcq', 'C', N'(C) About twenty thousand dollars.', 0, N'Bẫy liên tưởng từ vựng (Associative trap): Nghe thấy "budget" nên đưa ra con số tiền tệ, nhưng câu hỏi hỏi về thời gian "When".');

    -- Câu 4 (P2)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 4)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
        VALUES (@examId, @toeicCert, @secP2, 4, N'/audio/toeic/placement-p2.mp3');
    END
    DECLARE @grpQ4 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 4);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 4)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ4, @examId, @secP2, @toeicList,
            N'Who is leading the new client orientation session tomorrow? — Listen and choose the best response (A, B, or C).',
            2, 'mcq',
            N'Câu hỏi "Who" yêu cầu xác định nhân sự hoặc sự thay đổi phân công công việc.',
            4
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 4;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) In the main conference room on the third floor.', 0, N'Bẫy sai từ để hỏi: Trả lời cho câu hỏi "Where" thay vì "Who".'),
        (@qId, 'mcq', 'B', N'(B) Mr. Takahashi volunteered to take it over.', 1, N'Đáp án đúng: Nêu tên người phụ trách buổi định hướng khách hàng mới.'),
        (@qId, 'mcq', 'C', N'(C) It was a very informative session.', 0, N'Bẫy sai thì (Wrong tense trap) và lặp từ "session": Sự kiện diễn ra vào ngày mai (tomorrow) nhưng đáp án lại dùng thì quá khứ "was".');

    -- Câu 5 (P2)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 5)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
        VALUES (@examId, @toeicCert, @secP2, 5, N'/audio/toeic/placement-p2.mp3');
    END
    DECLARE @grpQ5 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 5);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 5)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ5, @examId, @secP2, @toeicList,
            N'Why don''t we postpone the product launch until the packaging is finalized? — Listen and choose the best response (A, B, or C).',
            2, 'mcq',
            N'"Why don''t we..." là câu đề xuất (suggestion), không phải câu hỏi nguyên nhân "Why" thông thường.',
            5
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 5;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) Because the factory is located in Osaka.', 0, N'Bẫy "Because" kinh điển cho câu đề xuất "Why don''t we...": Nội dung không ăn nhập với đề xuất lùi ngày ra mắt.'),
        (@qId, 'mcq', 'B', N'(B) A light lunch at the cafeteria.', 0, N'Bẫy phát âm gần giống (Near-homophone trap): Từ "lunch" (bữa trưa) phát âm gần giống với "launch" (ra mắt sản phẩm).'),
        (@qId, 'mcq', 'C', N'(C) That makes sense, given the printing delays.', 1, N'Đáp án đúng: Đồng tình với lời đề xuất và bổ sung lý do thực tế về việc chậm trễ in ấn bao bì.');

    -- Câu 6 (P2)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 6)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
        VALUES (@examId, @toeicCert, @secP2, 6, N'/audio/toeic/placement-p2.mp3');
    END
    DECLARE @grpQ6 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 6);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 6)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ6, @examId, @secP2, @toeicList,
            N'You''ve already updated the inventory database, haven''t you? — Listen and choose the best response (A, B, or C).',
            2, 'mcq',
            N'Câu hỏi đuôi (Tag question) kiểm tra trạng thái hoàn thành công việc, thường được trả lời bằng lý do chưa thể thực hiện.',
            6
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 6;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) The server was down all afternoon, so I couldn''t.', 1, N'Đáp án đúng: Phản hồi gián tiếp giải thích nguyên nhân chưa thể cập nhật cơ sở dữ liệu.'),
        (@qId, 'mcq', 'B', N'(B) In the warehouse next to the loading dock.', 0, N'Bẫy liên tưởng từ vựng: Nghe thấy "inventory" (hàng tồn kho) nên gán địa điểm nhà kho (warehouse).'),
        (@qId, 'mcq', 'C', N'(C) Yes, the software update is free of charge.', 0, N'Bẫy lặp từ "update": Đánh tráo khái niệm giữa cập nhật dữ liệu kho và bản cập nhật phần mềm miễn phí.');
    /* --------------------------------------------------------------------------------------
       1.4. LISTENING - PART 3: CONVERSATIONS (1 GROUP, 3 CÂU: Q7 - Q9, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP3 INT;
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP3 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript,
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP3, 1,
            N'Questions 7 through 9 refer to the following conversation.',
            N'Man: Hi, Clara. I just received a call from Apex Logistics regarding our shipment of ergonomic office chairs for the new Seattle branch. Unfortunately, two of the delivery trucks are stuck in Denver due to severe weather.
Woman: Oh no! Our interior setup team is scheduled to assemble the workstations this Thursday morning. Did they say when the trucks will arrive?
Man: They estimate a forty-eight-hour delay, which means the chairs won''t get there until Friday afternoon.
Woman: In that case, I''d better contact the setup agency right away to reschedule their technicians for Saturday so we don''t get charged a standby fee.',
            NULL, NULL, N'/audio/toeic/test1-p3.mp3', 0, 48000
        );
    END
    SELECT @grpP3 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP3 AND DisplayOrder = 1;

    -- Câu 7 (P3)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 7)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP3, @examId, @secP3, @toeicList,
            N'What problem does the man mention?',
            2, 'mcq',
            N'Người đàn ông thông báo lô hàng ghế văn phòng bị kẹt ở Denver do thời tiết xấu dẫn đến giao hàng trễ.',
            7
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 7;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'Some office furniture was damaged in transit.', 0, N'Bẫy suy diễn quá đà: Lô hàng bị kẹt do thời tiết chứ không hề bị hư hỏng.'),
        (@qId, 'mcq', 'B', N'A delivery of office chairs has been delayed.', 1, N'Đáp án đúng: Paraphrase từ "shipment of ergonomic office chairs... stuck in Denver... forty-eight-hour delay".'),
        (@qId, 'mcq', 'C', N'A branch office failed a safety inspection.', 0, N'Bẫy thông tin nhiễu không có trong bài nghe.'),
        (@qId, 'mcq', 'D', N'An invoice contains an incorrect standby fee.', 0, N'Bẫy ghép từ (Word-matching trap): Có nhắc tới "standby fee" ở cuối bài nhưng đó là khoản phí người phụ nữ muốn tránh, không phải lỗi hóa đơn.');

    -- Câu 8 (P3)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 8)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP3, @examId, @secP3, @toeicList,
            N'When is the shipment now expected to arrive?',
            2, 'mcq',
            N'Ban đầu dự kiến lắp đặt sáng thứ Năm, nhưng bị trễ 48 giờ nên chiều thứ Sáu hàng mới tới.',
            8
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 8;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'On Thursday morning', 0, N'Bẫy mốc thời gian cũ (Original schedule trap): Thứ Năm là lịch lắp đặt ban đầu của đội kỹ thuật.'),
        (@qId, 'mcq', 'B', N'On Thursday afternoon', 0, N'Bẫy trộn lẫn thông tin giữa "Thursday morning" và "Friday afternoon".'),
        (@qId, 'mcq', 'C', N'On Friday afternoon', 1, N'Đáp án đúng: Người đàn ông nói rõ "the chairs won''t get there until Friday afternoon".'),
        (@qId, 'mcq', 'D', N'On Saturday morning', 0, N'Bẫy mốc thời gian của hành động khác: Thứ Bảy là ngày dời lịch cho đội kỹ thuật viên lắp đặt.');

    -- Câu 9 (P3)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 9)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP3, @examId, @secP3, @toeicList,
            N'What will the woman most likely do next?',
            2, 'mcq',
            N'Người phụ nữ quyết định liên hệ ngay với bên lắp đặt (setup agency) để đổi lịch sang thứ Bảy.',
            9
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 9;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'Order chairs from a local supplier in Seattle', 0, N'Bẫy suy diễn không có căn cứ: Hai người chỉ chờ hàng tới chứ không hủy đơn để mua nhà cung cấp khác.'),
        (@qId, 'mcq', 'B', N'Travel to Denver to inspect the merchandise', 0, N'Bẫy địa danh: Denver chỉ là nơi xe tải bị kẹt lại do thời tiết.'),
        (@qId, 'mcq', 'C', N'Reschedule an appointment with a service provider', 1, N'Đáp án đúng: Paraphrase từ "contact the setup agency right away to reschedule their technicians".'),
        (@qId, 'mcq', 'D', N'Approve an overtime payment for the drivers', 0, N'Bẫy suy diễn từ ngữ cảnh vận tải và chi phí.');
    /* --------------------------------------------------------------------------------------
       1.5. LISTENING - PART 4: TALKS (1 GROUP, 3 CÂU: Q10 - Q12, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP4 INT;
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP4 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript,
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP4, 1,
            N'Questions 10 through 12 refer to the following announcement.',
            N'Attention, attendees of the Global FinTech Summit. Due to an unexpected audio-visual malfunction in Ballroom A, this morning''s keynote address by Dr. Elena Rostova on cybersecurity trends has been relocated to the Sapphire Auditorium on the mezzanine level. Because the Sapphire Auditorium has a slightly smaller seating capacity, overflow seating with a live video feed will be available in Room 204. Also, please remember to scan your digital conference badge at the entrance of the auditorium if you wish to receive Continuing Professional Education credits for attending this session. Thank you for your cooperation.',
            NULL, NULL, N'/audio/toeic/test1-p4.mp3', 0, 52000
        );
    END
    SELECT @grpP4 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP4 AND DisplayOrder = 1;

    -- Câu 10 (P4)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 10)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP4, @examId, @secP4, @toeicList,
            N'Why is the keynote address being moved to a different location?',
            2, 'mcq',
            N'Bài phát biểu chính phải chuyển phòng vì sự cố thiết bị nghe nhìn (audio-visual malfunction) tại Ballroom A.',
            10
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 10;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'More people registered than originally anticipated.', 0, N'Bẫy đảo ngược nguyên nhân: Bài nói có nhắc tới "smaller seating capacity" ở phòng mới, dễ làm thí sinh tưởng nhầm là đổi phòng vì quá đông người.'),
        (@qId, 'mcq', 'B', N'There is a technical problem with the room''s equipment.', 1, N'Đáp án đúng: Paraphrase chuẩn xác cụm "unexpected audio-visual malfunction in Ballroom A".'),
        (@qId, 'mcq', 'C', N'The keynote speaker requested a smaller stage.', 0, N'Bẫy thông tin sai lệch: Diễn giả Dr. Elena Rostova không hề yêu cầu đổi sân khấu.'),
        (@qId, 'mcq', 'D', N'Renovation work is taking place on the mezzanine level.', 0, N'Bẫy ghép từ: "mezzanine level" là tầng của hội trường mới (Sapphire Auditorium), không phải nơi đang sửa chữa.');

    -- Câu 11 (P4)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 11)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP4, @examId, @secP4, @toeicList,
            N'What is available in Room 204?',
            2, 'mcq',
            N'Phòng 204 được bố trí làm phòng dự phòng (overflow seating) có truyền hình trực tiếp (live video feed).',
            11
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 11;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'Complimentary refreshments', 0, N'Bẫy kịch bản hội thảo quen thuộc nhưng không được đề cập trong bài.'),
        (@qId, 'mcq', 'B', N'Printed copies of the presentation slides', 0, N'Bẫy suy diễn không có trong transcript.'),
        (@qId, 'mcq', 'C', N'A live broadcast of the speech', 1, N'Đáp án đúng: "A live broadcast" tương đương với "a live video feed" trong bài thông báo.'),
        (@qId, 'mcq', 'D', N'Technical support for digital badges', 0, N'Bẫy trộn từ: "digital badge" xuất hiện ở câu sau nhưng dùng để quét tại cửa hội trường.');

    -- Câu 12 (P4)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 12)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP4, @examId, @secP4, @toeicList,
            N'How can attendees earn professional credits?',
            2, 'mcq',
            N'Người tham dự cần quét thẻ hội nghị điện tử (scan digital conference badge) tại lối vào hội trường.',
            12
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 12;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'By completing an online feedback survey', 0, N'Bẫy thông lệ thực tế nhưng không có trong bài nói.'),
        (@qId, 'mcq', 'B', N'By signing a registration sheet at the front desk', 0, N'Bẫy phương thức điểm danh truyền thống (signing a sheet) thay vì quét thẻ kỹ thuật số.'),
        (@qId, 'mcq', 'C', N'By scanning their badges at the auditorium entrance', 1, N'Đáp án đúng: Trùng khớp trực tiếp với chỉ dẫn "scan your digital conference badge at the entrance of the auditorium".'),
        (@qId, 'mcq', 'D', N'By submitting a summary to Dr. Rostova', 0, N'Bẫy ghép tên nhân vật trong bài vào hành động sai.');
    /* --------------------------------------------------------------------------------------
       1.6. READING - PART 5: INCOMPLETE SENTENCES (8 CÂU: Q13 - Q20, 8 CHỦ ĐIỂM NGỮ PHÁP)
       -------------------------------------------------------------------------------------- */
    -- Câu 13 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 13)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 13);
    END
    DECLARE @grpQ13 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 13);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 13)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ13, @examId, @secP5, @toeicRead,
            N'All reimbursement requests must be submitted to the accounting department ------- the fifteenth of each month.',
            2, 'mcq',
            N'Phân biệt giới từ chỉ hạn chót "by" (trước hoặc muộn nhất là thời điểm nào) và "until" (kéo dài liên tục đến thời điểm nào). Động từ "submit" là hành động điểm nên bắt buộc đi với "by".',
            13
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 13;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'by', 1, N'Đáp án đúng: Giới từ "by + mốc thời gian" diễn tả hạn chót (deadline) cho một hành động hoàn tất như "submitted".'),
        (@qId, 'mcq', 'B', N'until', 0, N'Bẫy giới từ kinh điển: "until" chỉ hành động kéo dài liên tục, không dùng cho động từ hoàn tất một lần như "submit".'),
        (@qId, 'mcq', 'C', N'during', 0, N'Bẫy giới từ: "during" phải đi với một khoảng thời gian danh từ (e.g., during the month), không đi với ngày cụ thể "the fifteenth".'),
        (@qId, 'mcq', 'D', N'among', 0, N'Bẫy nghĩa giới từ: "among" mang nghĩa "trong số (từ 3 đối tượng trở lên)", không dùng để chỉ mốc thời gian.');
    -- Câu 14 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 14)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 14);
    END
    DECLARE @grpQ14 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 14);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 14)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ14, @examId, @secP5, @toeicRead,
            N'By the time the chief executive officer arrives at the Tokyo headquarters tomorrow, the legal team ------- the merger contract.',
            2, 'mcq',
            N'Cấu trúc "By the time + S + V(hiện tại đơn), S + will have + V3/ed (tương lai hoàn thành)" diễn tả hành động sẽ hoàn tất trước một thời điểm hoặc hành động khác trong tương lai.',
            14
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 14;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'finalized', 0, N'Bẫy sai thì: Thì quá khứ đơn mâu thuẫn với trạng từ chỉ tương lai "tomorrow" và mệnh đề "arrives".'),
        (@qId, 'mcq', 'B', N'will have finalized', 1, N'Đáp án đúng: Thì tương lai hoàn thành (will have + V3) đi chuẩn với mệnh đề trạng ngữ "By the time + Hiện tại đơn".'),
        (@qId, 'mcq', 'C', N'had finalized', 0, N'Bẫy công thức máy móc: Nhiều thí sinh học vẹt "By the time" đi với "had V3" mà quên nhìn động từ "arrives ... tomorrow".'),
        (@qId, 'mcq', 'D', N'is finalizing', 0, N'Bẫy thì hiện tại tiếp diễn: Không phản ánh được tính hoàn tất trước một mốc tương lai của cấu trúc "By the time".');
    -- Câu 15 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 15)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 15);
    END
    DECLARE @grpQ15 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 15);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 15)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ15, @examId, @secP5, @toeicRead,
            N'The newly installed inventory tracking software has made warehouse operations ------- more efficient.',
            2, 'mcq',
            N'Trước tính từ so sánh hơn "more efficient" cần một trạng từ (adverb) để nhấn mạnh mức độ.',
            15
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 15;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'consider', 0, N'Bẫy từ loại: Động từ nguyên mẫu không thể đứng bổ nghĩa cho cụm tính từ so sánh "more efficient".'),
        (@qId, 'mcq', 'B', N'considerable', 0, N'Bẫy cấu trúc make + O + Adj: Thí sinh nhìn thấy "made warehouse operations" nên vội chọn tính từ mà bỏ sót cụm tính từ chính "more efficient" ở phía sau.'),
        (@qId, 'mcq', 'C', N'considerably', 1, N'Đáp án đúng: Trạng từ "considerably" bổ nghĩa mức độ cho tính từ so sánh hơn "more efficient".'),
        (@qId, 'mcq', 'D', N'consideration', 0, N'Bẫy từ loại: Danh từ không thể đứng giữa tân ngữ "operations" và cụm tính từ "more efficient".');
    -- Câu 16 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 16)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 16);
    END
    DECLARE @grpQ16 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 16);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 16)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ16, @examId, @secP5, @toeicRead,
            N'Although the marketing interns were given guidance, they designed the social media campaign entirely by -------.',
            2, 'mcq',
            N'Cụm cố định "by + đại từ phản thân (oneself)" mang nghĩa "tự lực / một mình không ai giúp đỡ". Chủ ngữ là "they" nên dùng "themselves".',
            16
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 16;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'they', 0, N'Bẫy sai hình thái đại từ: Đại từ nhân xưng chủ ngữ không thể đứng sau giới từ "by".'),
        (@qId, 'mcq', 'B', N'them', 0, N'Bẫy đại từ tân ngữ: Khi chủ ngữ và tân ngữ cùng chỉ một đối tượng ("they") và đi sau giới từ "by" để chỉ sự tự lực, phải dùng đại từ phản thân.'),
        (@qId, 'mcq', 'C', N'their', 0, N'Bẫy tính từ sở hữu: "their" không thể đứng độc lập ở cuối câu mà không có danh từ theo sau.'),
        (@qId, 'mcq', 'D', N'themselves', 1, N'Đáp án đúng: Cấu trúc "by themselves" = "on their own" (tự chính họ thực hiện).');
    -- Câu 17 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 17)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 17);
    END
    DECLARE @grpQ17 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 17);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 17)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ17, @examId, @secP5, @toeicRead,
            N'------- the rising cost of raw materials, Falcon Manufacturing managed to keep its retail prices stable this year.',
            2, 'mcq',
            N'Phía sau chỗ trống là một cụm danh từ ("the rising cost of raw materials", không có động từ chia thì) và mang nghĩa nhượng bộ tương phản, do đó cần giới từ "Despite".',
            17
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 17;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'Although', 0, N'Bẫy liên từ vs giới từ: "Although" cùng nghĩa tương phản nhưng là liên từ phụ thuộc, bắt buộc theo sau bởi một mệnh đề (S + V).'),
        (@qId, 'mcq', 'B', N'Despite', 1, N'Đáp án đúng: Giới từ chỉ sự nhượng bộ "Despite + Cụm danh từ (Noun Phrase)".'),
        (@qId, 'mcq', 'C', N'Because of', 0, N'Bẫy ngữ nghĩa: Đúng ngữ pháp giới từ nhưng sai logic nhân quả.'),
        (@qId, 'mcq', 'D', N'Unless', 0, N'Bẫy liên từ điều kiện: "Unless" phải đi với mệnh đề (S + V) và không hợp nghĩa câu.');
    -- Câu 18 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 18)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 18);
    END
    DECLARE @grpQ18 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 18);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 18)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ18, @examId, @secP5, @toeicRead,
            N'Any employee ------- to attend the leadership seminar in Chicago must obtain written approval from a department head.',
            2, 'mcq',
            N'Rút gọn mệnh đề quan hệ: Câu gốc là "Any employee who wishes to attend...". Động từ chính của câu đã là "must obtain", nên chỗ trống cần hiện tại phân từ chủ động "wishing".',
            18
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 18;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'wishes', 0, N'Bẫy hai động từ chia thì trong một mệnh đề đơn: Câu đã có động từ chính "must obtain".'),
        (@qId, 'mcq', 'B', N'wished', 0, N'Bẫy phân từ bị động: Động từ "wish" ở đây mang nghĩa chủ động, không thể rút gọn dạng V-ed bị động.'),
        (@qId, 'mcq', 'C', N'wishing', 1, N'Đáp án đúng: Rút gọn mệnh đề quan hệ chủ động (who wishes -> wishing).'),
        (@qId, 'mcq', 'D', N'to wish', 0, N'Bẫy động từ nguyên mẫu có "to": Không dùng "to V" để rút gọn mệnh đề quan hệ thông thường khi không có số thứ tự.');
    -- Câu 19 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 19)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 19);
    END
    DECLARE @grpQ19 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 19);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 19)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ19, @examId, @secP5, @toeicRead,
            N'A comprehensive review of the company''s cybersecurity protocols and backup systems ------- conducted every six months.',
            2, 'mcq',
            N'Sự hòa hợp Chủ ngữ - Vị ngữ: Chủ ngữ chính là danh từ số ít "A comprehensive review", không phải cụm danh từ số nhiều đứng ngay trước chỗ trống ("protocols and backup systems").',
            19
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 19;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'are', 0, N'Bẫy danh từ gần nhất (Proximity trap): Thí sinh nhìn thấy "protocols and backup systems" số nhiều nên chọn "are".'),
        (@qId, 'mcq', 'B', N'is', 1, N'Đáp án đúng: Động từ to-be số ít "is" hòa hợp với chủ ngữ chính "A comprehensive review" và tạo thể bị động "is conducted".'),
        (@qId, 'mcq', 'C', N'have', 0, N'Bẫy sai cả số lẫn thể.'),
        (@qId, 'mcq', 'D', N'were', 0, N'Bẫy sai số và sai thì (câu có "every six months" chỉ thói quen định kỳ ở hiện tại).');
    -- Câu 20 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 20)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 20);
    END
    DECLARE @grpQ20 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 20);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 20)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ20, @examId, @secP5, @toeicRead,
            N'The new biodegradable packaging is ------- durable enough for international shipping and cost-effective to produce.',
            2, 'mcq',
            N'Cặp liên từ tương quan: Phía sau có liên từ "and" nối hai tính từ, do đó vị trí phía trước bắt buộc là "both".',
            20
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 20;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'either', 0, N'Bẫy cặp liên từ: "either" phải đi với "or", không đi với "and".'),
        (@qId, 'mcq', 'B', N'neither', 0, N'Bẫy cặp liên từ: "neither" phải đi với "nor", không đi với "and".'),
        (@qId, 'mcq', 'C', N'whether', 0, N'Bẫy cặp liên từ: "whether" đi với "or".'),
        (@qId, 'mcq', 'D', N'both', 1, N'Đáp án đúng: Cặp liên từ tương quan chuẩn "both A and B".');
    /* --------------------------------------------------------------------------------------
       1.7. READING - PART 6: TEXT COMPLETION (1 GROUP, 4 CÂU: Q21 - Q24, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP6 INT;
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP6 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript,
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP6, 1,
            N'Questions 21 through 24 refer to the following memorandum.

To: All Regional Sales Representatives
From: Marcus Vance, Director of Operations
Date: October 12
Subject: Transition to CloudSync Expense Portal

Effective November 1, our company will retire the legacy spreadsheet system used for travel expense reporting and transition fully to the CloudSync Expense Portal. This upgrade is designed to [21] ------- the reimbursement process so that employees receive their funds within three business days rather than two weeks.

To ensure a smooth transition, all sales staff are required to complete a 30-minute interactive tutorial before October 25. [22] -------. Once you have finished the module, your login credentials will be activated automatically.

Please note that any paper receipts dated prior to November 1 must still be processed through the old system. [23] -------, expenses incurred on or after that date will only be accepted via the CloudSync mobile app or web dashboard. Should you encounter any technical difficulties during registration, please contact the IT Help Desk [24] ------- extension 4402.',
            NULL, NULL, NULL, NULL, NULL, NULL
        );
    END
    SELECT @grpP6 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP6 AND DisplayOrder = 1;

    -- Câu 21 (P6)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 21)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP6, @examId, @secP6, @toeicRead,
            N'Select the best word to fill in blank [21].',
            2, 'mcq',
            N'Ngữ cảnh phía sau giải thích nhân viên sẽ nhận được tiền hoàn lại trong 3 ngày thay vì 2 tuần, tức là quy trình được đẩy nhanh / tinh gọn ("streamline").',
            21
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 21;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'streamline', 1, N'Đáp án đúng: "streamline" (hợp lý hóa, tinh gọn) khớp hoàn hảo với việc rút ngắn thời gian từ 2 tuần xuống 3 ngày.'),
        (@qId, 'mcq', 'B', N'postpone', 0, N'Bẫy từ vựng ngược nghĩa: "postpone" (trì hoãn) mâu thuẫn với mục đích nâng cấp hệ thống.'),
        (@qId, 'mcq', 'C', N'complicate', 0, N'Bẫy ngữ nghĩa: Không ai nâng cấp phần mềm với mục đích làm phức tạp hóa ("complicate") quy trình.'),
        (@qId, 'mcq', 'D', N'reimburse', 0, N'Bẫy lặp từ trong ngữ cảnh: Phía sau đã có danh từ "reimbursement process".');

    -- Câu 22 (P6)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 22)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP6, @examId, @secP6, @toeicRead,
            N'Select the best sentence to fill in blank [22].',
            2, 'mcq',
            N'Câu trước nhắc tới "a 30-minute interactive tutorial", câu sau nhắc tới "Once you have finished the module...". Do đó câu ở vị trí [22] phải kết nối cách truy cập bài hướng dẫn.',
            22
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 22;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'The quarterly sales targets have been adjusted accordingly.', 0, N'Bẫy lạc đề dựa trên đối tượng người nhận: Đoạn văn đang nói về phần mềm hoàn ứng chi phí, không phải chỉ tiêu doanh số.'),
        (@qId, 'mcq', 'B', N'A link to the training video has been sent to your corporate e-mail address.', 1, N'Đáp án đúng: Liên kết mạch lạc giữa "complete a 30-minute interactive tutorial" ở câu trước và "finished the module" ở câu sau.'),
        (@qId, 'mcq', 'C', N'Several clients have requested digital copies of their contracts.', 0, N'Bẫy từ vựng "digital": Không liên quan đến quy trình đào tạo nội bộ.'),
        (@qId, 'mcq', 'D', N'Travel allowances will be reduced by ten percent next fiscal year.', 0, N'Bẫy liên tưởng từ "travel expense": Đưa thông tin cắt giảm ngân sách không ăn nhập với mạch hướng dẫn kích hoạt tài khoản.');

    -- Câu 23 (P6)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 23)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP6, @examId, @secP6, @toeicRead,
            N'Select the best transition word to fill in blank [23].',
            2, 'mcq',
            N'Câu trước nói hóa đơn trước ngày 1/11 vẫn xử lý qua hệ thống cũ; câu sau nói chi phí từ ngày 1/11 trở đi chỉ được nhận qua ứng dụng mới. Hai mệnh đề đối lập nhau nên dùng "However".',
            23
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 23;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'For instance', 0, N'Bẫy trạng từ liên kết: Câu sau không phải là ví dụ minh họa mà là quy định ngược lại.'),
        (@qId, 'mcq', 'B', N'Similarly', 0, N'Bẫy quan hệ tương đồng: Đây là sự tương phản, không phải tương đồng.'),
        (@qId, 'mcq', 'C', N'However', 1, N'Đáp án đúng: Thể hiện rõ sự đối lập quy trình giữa các hóa đơn trước ngày 1/11 và từ ngày 1/11 trở đi.'),
        (@qId, 'mcq', 'D', N'Therefore', 0, N'Bẫy quan hệ nhân quả: Quy định sau không phải là hệ quả suy ra từ quy định trước.');

    -- Câu 24 (P6)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 24)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP6, @examId, @secP6, @toeicRead,
            N'Select the best word to fill in blank [24].',
            2, 'mcq',
            N'Trong tiếng Anh thương mại, khi liên hệ qua số máy lẻ nội bộ (extension + số), giới từ chuẩn luôn là "at" (contact someone at extension 4402).',
            24
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 24;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'at', 1, N'Đáp án đúng: Collocation chuẩn "reach/contact someone at extension + [number]".'),
        (@qId, 'mcq', 'B', N'to', 0, N'Bẫy dịch từng từ: Động từ "contact" là ngoại động từ không đi với "to".'),
        (@qId, 'mcq', 'C', N'with', 0, N'Bẫy giới từ: "contact with" là lỗi sai phổ biến khi "contact" đóng vai trò là động từ.'),
        (@qId, 'mcq', 'D', N'for', 0, N'Bẫy giới từ chỉ mục đích.');
    /* --------------------------------------------------------------------------------------
       1.8. READING - PART 7: SINGLE PASSAGE (GROUP 1, 3 CÂU: Q25 - Q27, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP7_1 INT;
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP7 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript,
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP7, 1,
            N'Questions 25 through 27 refer to the following workshop schedule.

====================================================================
PACIFIC NORTHWEST SMALL BUSINESS SYMPOSIUM
Date: Friday, November 14 | Venue: Grand Harbor Convention Center
====================================================================

09:00 A.M. – 10:15 A.M. | Session 1: Mastering Supply Chain Resilience
Speaker: Marcus Thorne (COO, Cascade Freight Solutions)
Room: Olympic Hall
Overview: Learn practical strategies for diversifying suppliers and reducing shipping bottlenecks for regional retailers.

10:30 A.M. – 11:45 A.M. | Session 2: Digital Marketing on a Shoestring Budget
Speaker: Priya Patel (Founder, Spark Media Agency)
Room: Rainier Suite
Overview: Discover how to maximize customer engagement using targeted social media campaigns without hiring an external agency. *Note: Attendees should bring a fully charged laptop for the hands-on analytics exercise.*

01:00 P.M. – 02:15 P.M. | Session 3: Navigating Commercial Tax Reforms
Speaker: Evelyn Cho, CPA (Senior Partner, Cho & Associates)
Room: Columbia Room
Overview: A detailed breakdown of upcoming state tax incentives for businesses with fewer than 50 full-time employees.

*Registration includes complimentary morning coffee and a buffet lunch served in the Emerald Atrium from 11:45 A.M. to 1:00 P.M. Seating for Session 2 is limited to 40 participants; advance sign-up via the symposium mobile app is mandatory.*',
            NULL, NULL, NULL, NULL, NULL, NULL
        );
    END
    SELECT @grpP7_1 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP7 AND DisplayOrder = 1;

    -- Câu 25 (P7 Single)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 25)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_1, @examId, @secP7, @toeicRead,
            N'Who would most likely benefit from attending Session 3?',
            2, 'mcq',
            N'Session 3 trình bày về ưu đãi thuế bang dành cho các doanh nghiệp có dưới 50 nhân viên toàn thời gian (fewer than 50 full-time employees).',
            25
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 25;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'International freight forwarders', 0, N'Bẫy thông tin của phần khác: Liên quan tới nội dung chuỗi cung ứng ở Session 1.'),
        (@qId, 'mcq', 'B', N'Freelance graphic designers seeking agency jobs', 0, N'Bẫy từ khóa "agency" lấy từ Session 2.'),
        (@qId, 'mcq', 'C', N'Owners of companies with a small workforce', 1, N'Đáp án đúng: Paraphrase cụm "businesses with fewer than 50 full-time employees" thành "companies with a small workforce".'),
        (@qId, 'mcq', 'D', N'Software developers building mobile applications', 0, N'Bẫy từ khóa "mobile app" xuất hiện ở dòng chú thích cuối bài.');

    -- Câu 26 (P7 Single)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 26)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_1, @examId, @secP7, @toeicRead,
            N'What is indicated about Ms. Patel''s session?',
            2, 'mcq',
            N'Ms. Patel phụ trách Session 2. Chú thích trong Session 2 yêu cầu mang laptop, và chú thích cuối bài ghi rõ Session 2 giới hạn 40 chỗ ngồi và bắt buộc đăng ký trước qua ứng dụng.',
            26
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 26;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'It will take place in the Emerald Atrium.', 0, N'Bẫy địa điểm: Emerald Atrium là nơi phục vụ bữa trưa buffet, còn phiên của Ms. Patel diễn ra tại Rainier Suite.'),
        (@qId, 'mcq', 'B', N'It requires participants to register in advance.', 1, N'Đáp án đúng: Kết nối thông tin "Ms. Patel" -> "Session 2" -> "advance sign-up via the symposium mobile app is mandatory".'),
        (@qId, 'mcq', 'C', N'It is the first session of the morning.', 0, N'Bẫy thứ tự: Phiên đầu tiên lúc 9:00 sáng là của Mr. Marcus Thorne.'),
        (@qId, 'mcq', 'D', N'It charges an additional fee for laptop rentals.', 0, N'Bẫy suy diễn sai: Người tham gia được nhắc tự mang theo laptop đã sạc đầy, không có dịch vụ cho thuê thu phí.');

    -- Câu 27 (P7 Single)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 27)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_1, @examId, @secP7, @toeicRead,
            N'What is included with the symposium registration?',
            2, 'mcq',
            N'Dòng cuối cùng ghi rõ: "Registration includes complimentary morning coffee and a buffet lunch".',
            27
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 27;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'A private consultation with a tax accountant', 0, N'Bẫy suy diễn từ diễn giả Evelyn Cho, CPA ở Session 3.'),
        (@qId, 'mcq', 'B', N'Free parking at the Grand Harbor Convention Center', 0, N'Bẫy thông tin không được đề cập (Not mentioned trap).'),
        (@qId, 'mcq', 'C', N'A complimentary midday meal', 1, N'Đáp án đúng: Paraphrase chuẩn từ "complimentary ... buffet lunch" thành "complimentary midday meal".'),
        (@qId, 'mcq', 'D', N'A one-year subscription to analytics software', 0, N'Bẫy từ khóa "analytics" lấy từ bài tập thực hành ở Session 2.');

    /* --------------------------------------------------------------------------------------
       1.9. READING - PART 7: DOUBLE PASSAGE (GROUP 2, 3 CÂU: Q28 - Q30, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP7_2 INT;
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP7 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript,
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP7, 2,
            N'Questions 28 through 30 refer to the following invoice and e-mail.

--------------------------------------------------------------------
 PASSAGE 1: INVOICE #INV-8841
 Vendor: Lumina Botanicals Wholesale
 Client: Serenity Day Spa (Attn: Hannah Abbott)
 Order Date: March 3 | Delivery Date: March 6
--------------------------------------------------------------------
 Item Code | Description                        | Qty | Unit Price | Total
 LB-102    | Organic Lavender Essential Oil 1L  |  5  | $40.00     | $200.00
 LB-205    | Eucalyptus Massage Lotion 5L       |  4  | $55.00     | $220.00
 LB-310    | Bamboo Facial Towels (Pack of 50)  |  6  | $30.00     | $180.00
 LB-408    | Chamomile Soothing Serum 500ml     |  2  | $65.00     | $130.00
--------------------------------------------------------------------
 Subtotal: $730.00
 Shipping (Express Courier): $0.00 (Waived for orders over $500)
 Total Charged: $730.00
 *Return Policy: Claims for damaged or incorrect items must be submitted within 5 business days of delivery to qualify for a full refund or free replacement.*

--------------------------------------------------------------------
 PASSAGE 2: E-MAIL
 To: billing@luminabotanicals.com
 From: h.abbott@serenitydayspa.com
 Date: March 7
 Subject: Issue with Order #INV-8841
--------------------------------------------------------------------
 Dear Customer Support,

 Thank you for the prompt delivery of our wholesale order yesterday. Everything arrived intact except for one item. While the invoice correctly lists the Bamboo Facial Towels we ordered, the boxes we actually received contained standard cotton hand towels instead.

 Because we have a VIP wellness retreat scheduled for next weekend, we urgently need the correct towels sent out via express courier. Alternatively, if that item is currently out of stock, please issue a refund for that portion of the order to our corporate credit card so we can source them locally. We have left the unopened boxes of cotton towels at our reception desk for your driver to pick up.

 Sincerely,
 Hannah Abbott
 Operations Manager, Serenity Day Spa',
            NULL, NULL, NULL, NULL, NULL, NULL
        );
    END
    SELECT @grpP7_2 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP7 AND DisplayOrder = 2;

    -- Câu 28 (P7 Double)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 28)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_2, @examId, @secP7, @toeicRead,
            N'Why did Ms. Abbott send the e-mail?',
            2, 'mcq',
            N'Trong email, bà Abbott báo rằng thay vì nhận được Bamboo Facial Towels như trên hóa đơn, spa lại nhận nhầm sang khăn tay cotton thông thường.',
            28
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 28;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'To complain about an unexpected shipping charge', 0, N'Bẫy thông tin ngược: Hóa đơn ghi rõ phí giao hàng là $0.00 (được miễn phí).'),
        (@qId, 'mcq', 'B', N'To report that some glass bottles were broken', 0, N'Bẫy từ khóa "damaged" ở phần chính sách đổi trả, nhưng email ghi rõ "Everything arrived intact".'),
        (@qId, 'mcq', 'C', N'To notify the vendor that an incorrect product was delivered', 1, N'Đáp án đúng: Spa đặt khăn mặt sợi tre nhưng lại bị giao nhầm khăn tay cotton.'),
        (@qId, 'mcq', 'D', N'To increase the quantity of lotion before the shipment goes out', 0, N'Bẫy sai trình tự thời gian: Đơn hàng đã được giao vào ngày hôm qua (March 6).');

    -- Câu 29 (P7 Double)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 29)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_2, @examId, @secP7, @toeicRead,
            N'If the requested item is out of stock, how much money should be refunded to Serenity Day Spa?',
            3, 'mcq',
            N'Suy luận chéo 2 văn bản: Trong email (Passage 2), mặt hàng bị giao sai cần hoàn tiền nếu hết hàng là "Bamboo Facial Towels". Đối chiếu lên Hóa đơn (Passage 1), mục "LB-310 | Bamboo Facial Towels" có tổng tiền (Total) là $180.00.',
            29
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 29;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'$30.00', 0, N'Bẫy đơn giá (Unit Price trap): $30.00 chỉ là giá của 1 gói khăn, trong khi spa đặt 6 gói (Tổng $180.00).'),
        (@qId, 'mcq', 'B', N'$130.00', 0, N'Bẫy nhầm dòng trên bảng hóa đơn: $130.00 là tổng tiền của mục LB-408 (Chamomile Soothing Serum).'),
        (@qId, 'mcq', 'C', N'$180.00', 1, N'Đáp án đúng: Suy luận chéo từ "Bamboo Facial Towels" trong email sang cột Total của mã LB-310 trên hóa đơn ($30.00 x 6 = $180.00).'),
        (@qId, 'mcq', 'D', N'$220.00', 0, N'Bẫy nhầm dòng trên bảng hóa đơn: $220.00 là tổng tiền của mục LB-205 (Eucalyptus Massage Lotion).');

    -- Câu 30 (P7 Double)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 30)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_2, @examId, @secP7, @toeicRead,
            N'Why is Serenity Day Spa eligible for a free replacement?',
            3, 'mcq',
            N'Suy luận chéo: Chính sách cuối hóa đơn (Passage 1) quy định khiếu nại gửi trong vòng 5 ngày làm việc kể từ ngày giao hàng (March 6) sẽ được đổi miễn phí. Email (Passage 2) được gửi ngay ngày March 7 (1 ngày sau khi nhận hàng).',
            30
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 30;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'Because the total order exceeded $500.00', 0, N'Bẫy nhầm điều kiện khuyến mãi: Đơn trên $500 là điều kiện để được miễn phí vận chuyển ban đầu, không phải điều kiện đổi trả hàng lỗi.'),
        (@qId, 'mcq', 'B', N'Because Ms. Abbott submitted the claim within five business days of delivery', 1, N'Đáp án đúng: Kết hợp ngày giao hàng (March 6) + chính sách 5 ngày trên Hóa đơn với ngày gửi email khiếu nại (March 7).'),
        (@qId, 'mcq', 'C', N'Because Serenity Day Spa is a first-time wholesale customer', 0, N'Bẫy thông tin không có trong cả hai văn bản.'),
        (@qId, 'mcq', 'D', N'Because the spa paid an extra fee for shipping insurance', 0, N'Bẫy suy diễn không có thật: Không có khoản phí bảo hiểm vận chuyển nào trên hóa đơn.');
    /* --------------------------------------------------------------------------------------
       1.10. WRITING - TASK 1 (SECTION W2 - Q31) & TASK 2 (SECTION W3 - Q32)
       LƯU Ý RÀNG BUỘC: QuestionType = 'free_response', TUYỆT ĐỐI KHÔNG TẠO QuestionOptions!
       -------------------------------------------------------------------------------------- */
    -- Câu 31 (Writing W2 - Respond to a Written Request)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secW2 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, ResponseSeconds)
        VALUES (@examId, @toeicCert, @secW2, 1, 600);
    END
    DECLARE @grpQ31 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secW2 AND DisplayOrder = 1);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 31)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder, PrepSeconds, ResponseSeconds, MinWords, MaxWords
        )
        VALUES (
            @grpQ31, @examId, @secW2, @toeicWrit,
            N'Directions: Read the e-mail below. You will have 10 minutes to read the e-mail and write your response. Your response should be between 60 and 100 words.

--------------------------------------------------------------------
From: David Kowalski, Procurement Manager, Apex Retail Group
To: Customer Fulfillment Team, Vanguard Electronics
Subject: Urgent – Delayed Delivery of Order #VE-9042
Sent: October 19, 10:15 A.M.

Dear Fulfillment Team,

We have still not received our shipment of 200 wireless barcode scanners (Order #VE-9042), which was guaranteed to arrive at our Chicago warehouse yesterday. Because our seasonal inventory audit begins this Thursday, this delay is disrupting our operations. Please explain why our shipment has been delayed and let us know what compensation or expedited shipping solution you can offer.

Regards,
David Kowalski
--------------------------------------------------------------------

Directions: Respond to the e-mail as if you are a Customer Fulfillment Representative at Vanguard Electronics. In your e-mail, do the following:
1. Apologize professionally and explain the specific reason for the shipping delay.
2. Confirm the exact new delivery date before Thursday.
3. Offer a clear form of compensation (e.g., partial refund, discount voucher, or waived shipping fee).
(Target length: 60–100 words | Time limit: 10 minutes)',
            3, 'free_response',
            N'Tiêu chí chấm điểm ETS Writing Part 2: (1) Giải quyết trọn vẹn cả 3 yêu cầu trong đề bài; (2) Văn phong email thương mại lịch sự, trang trọng; (3) Sử dụng chính xác các từ nối và cấu trúc câu phức; (4) Đạt độ dài tiêu chuẩn 60–100 từ.',
            31, 0, 600, 60, 100
        );
    END

    -- Câu 32 (Writing W3 - Write an Opinion Essay)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secW3 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, ResponseSeconds)
        VALUES (@examId, @toeicCert, @secW3, 1, 1200);
    END
    DECLARE @grpQ32 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secW3 AND DisplayOrder = 1);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 32)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder, PrepSeconds, ResponseSeconds, MinWords, MaxWords
        )
        VALUES (
            @grpQ32, @examId, @secW3, @toeicWrit,
            N'Directions: Read the question below. You have 20 minutes to plan, write, and revise your essay. Your essay should be between 150 and 220 words.

--------------------------------------------------------------------
PROMPT:
Do you agree or disagree with the following statement?
"Remote work increases employee productivity more than working in a traditional office."
Use specific reasons and examples to support your answer.
--------------------------------------------------------------------

Essay Guidelines:
- State your position clearly in the introductory paragraph.
- Develop at least two well-structured body paragraphs with concrete business examples.
- Include a concise concluding paragraph summarizing your main argument.
(Target length: 150–220 words | Time limit: 20 minutes)',
            4, 'free_response',
            N'Tiêu chí chấm điểm ETS Writing Part 3: (1) Bố cục 3 phần (Introduction - Body Paragraphs - Conclusion) mạch lạc; (2) Lập luận nhất quán kèm ví dụ thực tiễn nơi công sở; (3) Đa dạng hóa cấu trúc ngữ pháp và từ vựng học thuật/thương mại; (4) Đạt độ dài 150–220 từ.',
            32, 0, 1200, 150, 220
        );
    END
    /* ======================================================================================
       PHẦN 2: LỘ TRÌNH ĐIỀU TRỊ ĐỘNG & BÀI HỌC GUIDED LESSONS (SPRINT 2-3 TUẦN)
       ====================================================================================== */
    DECLARE @tplId INT;
    DECLARE @tplName NVARCHAR(200) = N'TOEIC Diagnostic Sprint (Target B1/B2)';

    -- 2.1. Tạo Master PathTemplate
    IF NOT EXISTS (SELECT 1 FROM dbo.PathTemplates WHERE CertId = @toeicCert AND Name = @tplName)
    BEGIN
        INSERT INTO dbo.PathTemplates (
            CertId, Name, FromBandCode, ToBandCode
        )
        VALUES (
            @toeicCert, @tplName, 'A2', 'B1'
        );
    END
    SELECT @tplId = TemplateId FROM dbo.PathTemplates WHERE CertId = @toeicCert AND Name = @tplName;

    -- 2.2. Tạo 4 PathModules tương ứng với 4 điểm yếu điển hình
    DECLARE @mod1 INT, @mod2 INT, @mod3 INT, @mod4 INT;

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, Description, EstimatedMinutes)
        VALUES (
            @tplId, @toeicRead, @toeicCert, 1,
            N'Tense & Aspect Mastery',
            N'Làm chủ hệ thống thì động từ, sự phối hợp thì (By the time / Since) và thể bị động trong ngữ cảnh doanh nghiệp Part 5 & Part 6.',
            25
        );
    END
    SELECT @mod1 = ModuleId FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, Description, EstimatedMinutes)
        VALUES (
            @tplId, @toeicList, @toeicCert, 2,
            N'Auditory Traps & Indirect Responses',
            N'Nhận diện và hóa giải bẫy lặp từ (Same-word trap), bẫy đồng âm và câu trả lời gián tiếp trong TOEIC Listening Part 2.',
            25
        );
    END
    SELECT @mod2 = ModuleId FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 2;

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, Description, EstimatedMinutes)
        VALUES (
            @tplId, @toeicWrit, @toeicCert, 3,
            N'Business Email Structure & Tone',
            N'Chuẩn hóa cấu trúc email phản hồi đối tác, xử lý khiếu nàn và nâng cấp văn phong chuyên nghiệp cho TOEIC Writing Part 2 (Section W2).',
            25
        );
    END
    SELECT @mod3 = ModuleId FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 3;

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 4)
    BEGIN
        INSERT INTO dbo.PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, Description, EstimatedMinutes)
        VALUES (
            @tplId, @toeicRead, @toeicCert, 4,
            N'Cross-Text Inference & Speed Reading',
            N'Chiến thuật đọc lướt (Skimming/Scanning) và kỹ năng móc nối dữ kiện chéo giữa Hóa đơn - Email trong TOEIC Reading Part 7.',
            30
        );
    END
    SELECT @mod4 = ModuleId FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 4;

    /* --------------------------------------------------------------------------------------
       2.3. TẠO 4 GUIDED LESSONS & 16 LESSON CARDS
       -------------------------------------------------------------------------------------- */
    DECLARE @les1 INT, @les2 INT, @les3 INT, @les4 INT;

    -- LESSON 1
    IF NOT EXISTS (SELECT 1 FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Tense & Aspect Mastery in Business Contexts')
    BEGIN
        INSERT INTO dbo.Lessons (CertId, SkillId, Title, Summary, EstimatedMinutes, DisplayOrder)
        VALUES (
            @toeicCert, @toeicRead,
            N'Tense & Aspect Mastery in Business Contexts',
            N'Phương pháp 3 giây nhận diện dấu hiệu phối hợp thì (Time Markers) và phân biệt chủ động - bị động trong Part 5.',
            15, 1
        );
    END
    SELECT @les1 = LessonId FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Tense & Aspect Mastery in Business Contexts';

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les1 AND DisplayOrder = 0)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les1, @toeicCert, 'rule',
            N'Quy tắc cốt lõi: Sự phối hợp thì theo trục thời gian (Timeline Alignment)',
            N'### 1. Công thức "By the time" kinh điển trong TOEIC' + CHAR(10) +
            N'- **Tương lai hoàn thành**: `By the time + S + V(hiện tại đơn), S + will have + V3/ed`' + CHAR(10) +
            N'  *(Diễn tả một dự án/báo cáo sẽ hoàn tất trước khi một sự kiện tương lai khác diễn ra)*.' + CHAR(10) +
            N'- **Quá khứ hoàn thành**: `By the time + S + V(quá khứ đơn), S + had + V3/ed`' + CHAR(10) +
            N'  *(Diễn tả hành động đã xong trước một mốc trong quá khứ)*.' + CHAR(10) + CHAR(10) +
            N'### 2. Kiểm tra Thể Bị Động (Passive Voice Check)' + CHAR(10) +
            N'- Nếu sau chỗ trống **KHÔNG có cụm danh từ làm tân ngữ** (mà là giới từ `by`, `in`, `at`, `to` hoặc dấu chấm câu) và động từ cần điền là ngoại động từ (`submit`, `approve`, `conduct`, `finalize`), hãy ưu tiên **Thể bị động (`be + V3/ed`)**.',
            0
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les1 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les1, @toeicCert, 'pitfall',
            N'Bẫy kinh điển: Học vẹt dấu hiệu mà bỏ quên động từ mệnh đề phụ',
            N'Thí sinh thường phản xạ máy móc khi thấy `By the time` là chọn ngay `had + V3` mà không kiểm tra thì của mệnh đề đi kèm.' + CHAR(10) + CHAR(10) +
            N'- ❌ **Sai lầm phổ biến**: *By the time the auditor **arrives** next Monday, we **had prepared** all tax documents.*' + CHAR(10) +
            N'  *(Lỗi lệch pha thời gian: `arrives` là hiện tại chỉ tương lai nhưng mệnh đề chính lại lùi về quá khứ hoàn thành)*.' + CHAR(10) + CHAR(10) +
            N'- ✔️ **Chuẩn ETS**: *By the time the auditor **arrives** next Monday, we **will have prepared** all tax documents.*' + CHAR(10) +
            N'  *(Mệnh đề phụ chia Hiện tại đơn `arrives` -> Mệnh đề chính bắt buộc chia Tương lai hoàn thành `will have V3`)*.',
            1
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les1 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les1, @toeicCert, 'example',
            N'Phân tích ngữ cảnh thực tế trong đề thi TOEIC',
            N'> *"Ms. Gomez **has overseen** the regional logistics department since the company **expanded** its operations to Southeast Asia in 2022."*' + CHAR(10) + CHAR(10) +
            N'**Giải phẫu câu:**' + CHAR(10) +
            N'1. Mệnh đề sau `since` chỉ một mốc sự kiện đã xảy ra và khép lại vào năm 2022 -> Dùng **Quá khứ đơn (`expanded`)**.' + CHAR(10) +
            N'2. Mệnh đề chính diễn tả trách nhiệm quản lý bắt đầu từ năm 2022 và kéo dài liên tục đến hiện tại -> Dùng **Hiện tại hoàn thành (`has overseen`)**.',
            2
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les1 AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.LessonCards (
            LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder,
            QuestionPrompt, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation, Rationale
        )
        VALUES (
            @les1, @toeicCert, 'micro_check',
            N'Kiểm tra nhanh: Phối hợp thì & Thể bị động',
            N'Áp dụng quy tắc trục thời gian và kiểm tra tân ngữ phía sau chỗ trống để chọn đáp án đúng.',
            3,
            N'By the time the board of directors convenes at 3:00 P.M. today, the revised financial report ------- to all shareholders.',
            N'distributed',
            N'had been distributed',
            N'will have been distributed',
            N'is distributing',
            'C',
            N'Mệnh đề "By the time + convenes (hiện tại đơn) ... today" đòi hỏi thì Tương lai hoàn thành ở mệnh đề chính. Đồng thời chủ ngữ "the revised financial report" chịu tác động và sau chỗ trống là giới từ "to" (không có tân ngữ) nên phải chia ở thể bị động: "will have been distributed".',
            N'(A) Sai thì và thiếu bị động; (B) Bẫy quá khứ hoàn thành với "By the time"; (C) Đúng cả thì tương lai hoàn thành lẫn thể bị động; (D) Sai thì và sai thể chủ động.'
        );
    END

    -- LESSON 2
    IF NOT EXISTS (SELECT 1 FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Eliminating Auditory Traps & Mastering Indirect Responses')
    BEGIN
        INSERT INTO dbo.Lessons (CertId, SkillId, Title, Summary, EstimatedMinutes, DisplayOrder)
        VALUES (
            @toeicCert, @toeicList,
            N'Eliminating Auditory Traps & Mastering Indirect Responses',
            N'Kỹ thuật loại trừ đáp án lặp âm/lặp từ và nhận diện các mẫu câu trả lời gián tiếp đạt điểm tuyệt đối Part 2.',
            15, 2
        );
    END
    SELECT @les2 = LessonId FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Eliminating Auditory Traps & Mastering Indirect Responses';

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les2 AND DisplayOrder = 0)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les2, @toeicCert, 'rule',
            N'Quy tắc cốt lõi: 3 Dạng trả lời gián tiếp (Indirect Responses) phổ biến nhất',
            N'Trong đề thi TOEIC hiện đại, hơn **45% câu hỏi Part 2** không trả lời trực tiếp vào từ để hỏi mà sử dụng phản hồi gián tiếp:' + CHAR(10) + CHAR(10) +
            N'1. **Báo không biết / Chưa quyết định**: *"I haven''t checked the schedule yet"*, *"It hasn''t been finalized"*.' + CHAR(10) +
            N'2. **Chuyển hướng sang người hoặc tài liệu khác**: *"Ask Ms. Carter in Accounting"*, *"Check the memo on the bulletin board"*.' + CHAR(10) +
            N'3. **Đưa ra lý do cản trở thay vì nói Yes/No**: Khi được hỏi *"Have you submitted the report?"*, người nghe trả lời *"The network has been down all morning"* (ngầm hiểu là *Chưa nộp*).',
            0
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les2 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les2, @toeicCert, 'pitfall',
            N'Bẫy kinh điển: Bẫy lặp từ (Same-Word Trap) & Bẫy đồng âm (Sound-Alike Trap)',
            N'Nguyên tắc vàng của ETS: **Đáp án nào lặp lại nguyên xi danh từ/động từ của câu hỏi hoặc dùng từ phát âm na ná thì 90% là bẫy!**' + CHAR(10) + CHAR(10) +
            N'- **Câu hỏi**: *"Where did you leave the **client contract**?"*' + CHAR(10) +
            N'  - ❌ **Bẫy lặp từ & đồng âm**: *"Yes, my **contact** lens is on the desk."* (Lặp âm `contract` -> `contact` và dùng `Yes` cho câu hỏi `Where`).' + CHAR(10) +
            N'  - ✔️ **Đáp án chuẩn**: *"I gave it to Mr. Evans before lunch."* (Trả lời gián tiếp vị trí của hợp đồng bằng hành động đã đưa cho ông Evans).',
            1
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les2 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les2, @toeicCert, 'example',
            N'Phân tích câu hỏi đề xuất "Why don''t we..." trong Part 2',
            N'> **Question**: *"Why don''t we interview the final candidate via video conference?"*' + CHAR(10) + CHAR(10) +
            N'**Phân tích phản xạ:**' + CHAR(10) +
            N'- Mặc dù bắt đầu bằng `Why`, đây là **lời đề nghị (Suggestion)** tương đương với `Let''s...`.' + CHAR(10) +
            N'- Loại bỏ ngay các phương án bắt đầu bằng `Because...`.' + CHAR(10) +
            N'- **Đáp án đúng**: *"That would save travel expenses."* (Đồng tình gián tiếp bằng cách nêu lợi ích của đề xuất).',
            2
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les2 AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.LessonCards (
            LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder,
            QuestionPrompt, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation, Rationale
        )
        VALUES (
            @les2, @toeicCert, 'micro_check',
            N'Kiểm tra nhanh: Nhận diện phản hồi gián tiếp',
            N'Đọc câu hỏi dưới đây và loại bỏ các bẫy âm thanh/lặp từ để chọn phản hồi tự nhiên nhất.',
            3,
            N'Question: "How much will it cost to upgrade our server capacity?" — Which response is best?',
            N'Yes, the server is at full capacity.',
            N'I''m still waiting for a quote from the vendor.',
            N'Along the coast of California.',
            N'To improve our data processing speed.',
            'B',
            N'Câu hỏi "How much" hỏi về chi phí, nhưng người trả lời chưa có con số chính xác vì vẫn đang đợi bảng báo giá (waiting for a quote) từ nhà cung cấp — đây là phản hồi gián tiếp cực kỳ phổ biến trong TOEIC.',
            N'(A) Bẫy Yes/No cho câu hỏi Wh- kèm lặp từ "server" và "capacity"; (B) Đáp án đúng dạng gián tiếp; (C) Bẫy đồng âm "cost" và "coast"; (D) Bẫy trả lời cho câu hỏi mục đích "Why" thay vì "How much".'
        );
    END

    -- LESSON 3
    IF NOT EXISTS (SELECT 1 FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'High-Scoring Business Email Architecture (Writing W2)')
    BEGIN
        INSERT INTO dbo.Lessons (CertId, SkillId, Title, Summary, EstimatedMinutes, DisplayOrder)
        VALUES (
            @toeicCert, @toeicWrit,
            N'High-Scoring Business Email Architecture (Writing W2)',
            N'Khung sườn 4 bước viết email giải quyết sự cố thương mại trong 10 phút với văn phong trang trọng chuẩn ETS.',
            20, 3
        );
    END
    SELECT @les3 = LessonId FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'High-Scoring Business Email Architecture (Writing W2)';

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les3 AND DisplayOrder = 0)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les3, @toeicCert, 'rule',
            N'Quy tắc cốt lõi: Khung 4 đoạn chuẩn mực cho Business Email',
            N'Để đạt điểm tối đa (Level 5) trong TOEIC Writing Task 1, email phải tuân thủ nghiêm ngặt 4 khối nội dung:' + CHAR(10) + CHAR(10) +
            N'1. **Salutation & Purpose (Chào hỏi & Nêu mục đích)**: *"Thank you for contacting us regarding..."*, *"I am writing to apologize for the delay in..."*' + CHAR(10) +
            N'2. **Cause & Details (Giải trình nguyên nhân)**: Trình bày lý do khách quan bằng cấu trúc bị động hoặc danh từ hóa (*"Due to an unexpected supply chain disruption..."*).' + CHAR(10) +
            N'3. **Action & Compensation (Giải pháp & Đền bù)**: Đưa ra mốc thời gian giải quyết cụ thể và phương án bồi thường thỏa đáng (*"We have dispatched a replacement order via express air freight..."*).' + CHAR(10) +
            N'4. **Professional Closing (Lời chúc & Ký tên)**: *"Please accept our sincere apologies for any inconvenience caused. Sincerely, [Name]"*.',
            0
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les3 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les3, @toeicCert, 'pitfall',
            N'Bẫy kinh điển: Thiếu một trong các yêu cầu của đề bài (Missing Task Element)',
            N'ETS luôn đưa ra **từ 2 đến 3 chỉ dẫn bắt buộc** trong phần Directions (ví dụ: giải thích lý do, hẹn ngày mới, và đề xuất bồi thường).' + CHAR(10) + CHAR(10) +
            N'- ❌ **Lỗi mất điểm nặng nhất**: Dù tiếng Anh của bạn hoàn hảo, ngữ pháp không sai một lỗi nào, nhưng nếu bạn **quên đề xuất phương án bồi thường**, bài viết sẽ bị giới hạn ở mức **điểm 3/5**!' + CHAR(10) +
            N'- ✔️ **Kỹ thuật Check-off**: Trước khi bấm nộp bài, hãy dành 60 giây đối chiếu từng gạch đầu dòng của đề bài xem đã có trong bài viết chưa.',
            1
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les3 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les3, @toeicCert, 'example',
            N'Mẫu câu ghi điểm tuyệt đối cho phần Đền bù (Compensation)',
            N'- *"To compensate for this inconvenience, we would like to offer a 15% discount on your next wholesale order."*' + CHAR(10) +
            N'- *"In addition, we have waived all freight charges for this delivery and credited your corporate account accordingly."*' + CHAR(10) +
            N'- *"As a gesture of goodwill, please accept a complimentary one-year warranty extension on all delivered equipment."*',
            2
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les3 AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.LessonCards (
            LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder,
            QuestionPrompt, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation, Rationale
        )
        VALUES (
            @les3, @toeicCert, 'micro_check',
            N'Kiểm tra nhanh: Lựa chọn câu đền bù chuẩn văn phong thương mại',
            N'Chọn câu văn thể hiện thái độ đền bù khách hàng chuyên nghiệp và trang trọng nhất.',
            3,
            N'Which sentence best demonstrates appropriate tone and professional vocabulary when offering compensation?',
            N'We feel really bad, so you don''t have to pay for shipping this time.',
            N'As a gesture of goodwill, we have waived the shipping fee and credited a 10% discount to your account.',
            N'You should take a 10% discount because our delivery driver was late.',
            N'If you want your money back, tell our accounting team right now.',
            'B',
            N'Phương án (B) sử dụng cụm từ thương mại chuẩn mực "As a gesture of goodwill", động từ học thuật "waived" (miễn phí) và "credited" (hoàn/cộng vào tài khoản), thể hiện sự chủ động và tôn trọng khách hàng.',
            N'(A) Quá suồng sã ("feel really bad"); (B) Chuẩn văn phong Business English; (C) Thiếu lịch sự và đổ lỗi trực tiếp; (D) Giọng điệu mệnh lệnh gây phản cảm cho đối tác.'
        );
    END

    -- LESSON 4
    IF NOT EXISTS (SELECT 1 FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Cross-Text Inference in Multi-Passage Reading (Part 7)')
    BEGIN
        INSERT INTO dbo.Lessons (CertId, SkillId, Title, Summary, EstimatedMinutes, DisplayOrder)
        VALUES (
            @toeicCert, @toeicRead,
            N'Cross-Text Inference in Multi-Passage Reading (Part 7)',
            N'Phương pháp tìm "Từ khóa cầu nối" (Bridge Keywords) để giải quyết câu hỏi suy luận chéo giữa Hóa đơn, Lịch trình và Email.',
            20, 4
        );
    END
    SELECT @les4 = LessonId FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Cross-Text Inference in Multi-Passage Reading (Part 7)';

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les4 AND DisplayOrder = 0)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les4, @toeicCert, 'rule',
            N'Quy tắc cốt lõi: Kỹ thuật "Cầu nối thực thể" (Entity Bridging)',
            N'Trong bài đọc ghép (Double/Triple Passages), câu hỏi suy luận chéo **không bao giờ** nêu trực tiếp đáp án ở một văn bản duy nhất. Hãy thực hiện 3 bước:' + CHAR(10) + CHAR(10) +
            N'1. **Xác định Clue A ở Văn bản 2 (Email/Review)**: Tìm mô tả gián tiếp (ví dụ: *"the item that arrived damaged"*, *"the session conducted by the CPA"*).' + CHAR(10) +
            N'2. **Tìm Điểm neo (Anchor/Bridge)**: Lấy tên món hàng, mã đơn, hoặc ngày tháng làm cầu nối.' + CHAR(10) +
            N'3. **Tra cứu Clue B ở Văn bản 1 (Invoice/Schedule)**: Đối chiếu sang cột Giá tổng (`Total`), Mã sản phẩm (`Item Code`), hoặc Điều khoản (`Policy Note`) ở cuối bảng.',
            0
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les4 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les4, @toeicCert, 'pitfall',
            N'Bẫy kinh điển: Bẫy Đơn giá vs Tổng tiền & Bỏ qua dòng chú thích (*Asterisk Note*)',
            N'Hai cái bẫy lấy đi nhiều điểm nhất của thí sinh ở Part 7:' + CHAR(10) + CHAR(10) +
            N'- ❌ **Bẫy cột Unit Price (Đơn giá)**: Khi câu hỏi hỏi *"How much will be refunded for the towels?"*, thí sinh dò đúng dòng Khăn nhưng lại lấy con số ở cột `Unit Price ($30.00)` thay vì cột `Total ($180.00 cho 6 gói)`.' + CHAR(10) +
            N'- ❌ **Bỏ qua dòng chữ nhỏ có dấu hoa thị (`*`) ở đáy bảng**: hơn **70% đáp án suy luận** về điều kiện miễn phí, hạn chót đổi trả hoặc yêu cầu đăng ký trước đều nằm ở dòng `*Note:` cuối bảng hoặc cuối hóa đơn!',
            1
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les4 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les4, @toeicCert, 'example',
            N'Minh họa chuỗi suy luận chéo thực tế',
            N'- **Dữ kiện 1 (Trên Hóa đơn)**:' + CHAR(10) +
            N'  - `Delivery Date: March 6`' + CHAR(10) +
            N'  - `*Note: Claims submitted within 5 business days of delivery qualify for a free replacement.`' + CHAR(10) +
            N'- **Dữ kiện 2 (Trên Email)**:' + CHAR(10) +
            N'  - `Sent Date: March 7` (Tức là chỉ **1 ngày** sau khi giao hàng).' + CHAR(10) +
            N'- **Kết luận suy luận**: Khách hàng hoàn toàn đủ điều kiện nhận hàng thay thế miễn phí vì gửi yêu cầu trong hạn 5 ngày làm việc.',
            2
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les4 AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.LessonCards (
            LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder,
            QuestionPrompt, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation, Rationale
        )
        VALUES (
            @les4, @toeicCert, 'micro_check',
            N'Kiểm tra nhanh: Thực hành suy luận chéo (Cross-Text Inference)',
            N'Đọc nhanh 2 trích đoạn ngắn dưới đây và trả lời câu hỏi suy luận.',
            3,
            N'[Menu]: "Package A ($15/person): Sandwiches & Salad | Package B ($22/person): Hot Pasta Buffet. *Orders for 20 or more people receive free delivery."
[Email]: "We would like to order the Hot Pasta Buffet for our 25-member department meeting." — How much will the delivery fee be?',
            N'$0.00',
            N'$15.00',
            N'$22.00',
            N'$25.00',
            'A',
            N'Email đặt suất ăn cho 25 người ("25-member department"). Đối chiếu với chú thích có dấu hoa thị (*) trên Menu: đơn hàng từ 20 người trở lên được miễn phí giao hàng ("free delivery"), do đó phí giao hàng là $0.00.',
            N'(A) Đúng nhờ kết nối số lượng 25 người với điều kiện >= 20 người ở dòng chú thích; (B) Bẫy giá của Package A; (C) Bẫy đơn giá của Package B; (D) Bẫy con số 25 thành viên trong email.'
        );
    END

    /* --------------------------------------------------------------------------------------
       2.4. LIÊN KẾT PATH MODULE ITEMS & TOPICS
       -------------------------------------------------------------------------------------- */
    -- Module 1 Items
    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod1 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefLessonId, DisplayOrder)
        VALUES (@mod1, @toeicCert, @tplId, 'lesson', @les1, 1);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod1 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
        VALUES (@mod1, @toeicCert, @tplId, 'practice_section', @secP5, 20, 2);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleTopics WHERE ModuleId = @mod1 AND Title = N'Tense & Aspect Foundations')
    BEGIN
        INSERT INTO dbo.PathModuleTopics (ModuleId, Title, QuestionCount, DisplayOrder)
        VALUES (@mod1, N'Tense & Aspect Foundations', 15, 1);
    END

    -- Module 2 Items
    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod2 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefLessonId, DisplayOrder)
        VALUES (@mod2, @toeicCert, @tplId, 'lesson', @les2, 1);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod2 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
        VALUES (@mod2, @toeicCert, @tplId, 'practice_section', @secP2, 25, 2);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleTopics WHERE ModuleId = @mod2 AND Title = N'Auditory Distractors & Traps')
    BEGIN
        INSERT INTO dbo.PathModuleTopics (ModuleId, Title, QuestionCount, DisplayOrder)
        VALUES (@mod2, N'Auditory Distractors & Traps', 25, 1);
    END

    -- Module 3 Items
    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod3 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefLessonId, DisplayOrder)
        VALUES (@mod3, @toeicCert, @tplId, 'lesson', @les3, 1);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod3 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
        VALUES (@mod3, @toeicCert, @tplId, 'practice_section', @secW2, 2, 2);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleTopics WHERE ModuleId = @mod3 AND Title = N'Business Email Formatting')
    BEGIN
        INSERT INTO dbo.PathModuleTopics (ModuleId, Title, QuestionCount, DisplayOrder)
        VALUES (@mod3, N'Business Email Formatting', 5, 1);
    END

    -- Module 4 Items
    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod4 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefLessonId, DisplayOrder)
        VALUES (@mod4, @toeicCert, @tplId, 'lesson', @les4, 1);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod4 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
        VALUES (@mod4, @toeicCert, @tplId, 'practice_section', @secP7, 20, 2);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleTopics WHERE ModuleId = @mod4 AND Title = N'Double Passage Cross-Inference')
    BEGIN
        INSERT INTO dbo.PathModuleTopics (ModuleId, Title, QuestionCount, DisplayOrder)
        VALUES (@mod4, N'Double Passage Cross-Inference', 15, 1);
    END

    COMMIT TRANSACTION;
    PRINT N'=== SEED DIAGNOSTIC BENCHMARK 45M & DYNAMIC LEARNING PATH COMPLETED SUCCESSFULLY ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrSeverity INT = ERROR_SEVERITY();
    DECLARE @ErrState INT = ERROR_STATE();
    DECLARE @ErrLine INT = ERROR_LINE();

    RAISERROR(N'Lỗi khi chạy seed-diagnostic-benchmark.sql tại dòng %d: %s', @ErrSeverity, @ErrState, @ErrLine, @ErrMsg);
END CATCH;
GO
