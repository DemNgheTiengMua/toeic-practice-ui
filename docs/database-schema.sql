/* ============================================================
   TOEIC L&R — Lược đồ cơ sở dữ liệu
   Ngày: 2026-09-20
   Tech stack: SQL Server 2019+ / Azure SQL, dựng để dùng với
   EF Core (ASP.NET Core Web API như spec §2).

   Phạm vi: suy ra từ prototype 31 màn đã dựng, cộng hai thay đổi
   đã chốt trong lượt review — xác thực CCCD (KYC) và quy chiếu CEFR.

   Quy ước chung
   - Khoá chính BIGINT IDENTITY, trừ bảng tham chiếu cố định.
   - Thời điểm dùng DATETIMEOFFSET: hết giờ thi phải đúng tuyệt đối
     kể cả khi server đổi múi giờ (spec §2 ràng buộc 1).
   - Chuỗi dùng NVARCHAR vì nội dung có tiếng Việt.
   - Trạng thái lưu dạng VARCHAR + CHECK thay vì TINYINT: đọc được
     trực tiếp trong DB, và CHECK chặn giá trị lạ ngay tầng SQL.
   - Xoá: dùng cờ IsDeleted cho dữ liệu nghiệp vụ (đề, câu hỏi, user),
     không xoá cứng, vì bài thi cũ còn tham chiếu tới.

   YÊU CẦU KHI CHẠY SCRIPT
   Lược đồ dùng filtered index (WHERE ...), nên session chạy script bắt
   buộc phải có QUOTED_IDENTIFIER và ANSI_NULLS = ON. EF Core đặt sẵn hai
   option này; chạy tay bằng sqlcmd thì phải thêm cờ -I, hoặc dựa vào hai
   lệnh SET ngay dưới đây.
   ============================================================ */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ============================================================
   1. Identity — người dùng, role, xác thực CCCD
   ============================================================ */

CREATE TABLE Roles (
    RoleId      TINYINT      NOT NULL PRIMARY KEY,
    Code        VARCHAR(20)  NOT NULL UNIQUE,
    Name        NVARCHAR(50) NOT NULL
);
-- Chỉ hai role trong phạm vi (spec §3): Student, Admin.
INSERT INTO Roles (RoleId, Code, Name) VALUES
    (1, 'Student', N'Học viên'),
    (2, 'Admin',   N'Quản trị viên');

CREATE TABLE Users (
    UserId          BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Email           NVARCHAR(256)  NOT NULL,
    -- Chỉ lưu hash. Không có cột mật khẩu thô ở bất kỳ đâu.
    PasswordHash    NVARCHAR(500)  NOT NULL,
    FullName        NVARCHAR(150)  NOT NULL,
    PhoneNumber     VARCHAR(20)    NULL,
    DateOfBirth     DATE           NULL,
    RoleId          TINYINT        NOT NULL,
    -- Xác thực email (màn đăng ký) tách khỏi xác thực CCCD bên dưới.
    IsEmailConfirmed BIT           NOT NULL CONSTRAINT DF_Users_EmailConfirmed DEFAULT (0),
    IsLocked        BIT            NOT NULL CONSTRAINT DF_Users_IsLocked DEFAULT (0),
    IsDeleted       BIT            NOT NULL CONSTRAINT DF_Users_IsDeleted DEFAULT (0),
    CreatedAt       DATETIMEOFFSET NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
    UpdatedAt       DATETIMEOFFSET NULL,
    CONSTRAINT FK_Users_Roles FOREIGN KEY (RoleId) REFERENCES Roles (RoleId)
);
-- Email là danh tính đăng nhập nên phải unique. Lọc IsDeleted = 0 để
-- email của tài khoản đã xoá mềm không chặn người khác đăng ký lại.
CREATE UNIQUE INDEX UX_Users_Email ON Users (Email) WHERE IsDeleted = 0;
CREATE INDEX IX_Users_RoleId ON Users (RoleId) WHERE IsDeleted = 0;

/* Token đặt lại mật khẩu (màn auth/forgot-password).
   Lưu hash của token, không lưu token thô: rò DB thì token trong email
   vẫn không dùng được. */
CREATE TABLE PasswordResetTokens (
    TokenId     BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    UserId      BIGINT         NOT NULL,
    TokenHash   VARBINARY(64)  NOT NULL,
    ExpiresAt   DATETIMEOFFSET NOT NULL,
    UsedAt      DATETIMEOFFSET NULL,
    CreatedAt   DATETIMEOFFSET NOT NULL CONSTRAINT DF_PwdReset_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT FK_PwdReset_Users FOREIGN KEY (UserId) REFERENCES Users (UserId)
);
CREATE INDEX IX_PwdReset_UserId ON PasswordResetTokens (UserId);
CREATE UNIQUE INDEX UX_PwdReset_TokenHash ON PasswordResetTokens (TokenHash);
-- Job dọn token hết hạn quét theo ExpiresAt; không có index này thì mỗi
-- lần dọn là một lần scan cả bảng.
CREATE INDEX IX_PwdReset_ExpiresAt ON PasswordResetTokens (ExpiresAt) WHERE UsedAt IS NULL;

/* ---- Xác thực CCCD ----------------------------------------
   Gate mới, đứng TRƯỚC khi trừ credit (quyết định ở lượt review:
   xác thực trước, trừ lượt sau — không thì học viên mất lượt rồi
   mới bị chặn).
   Ảnh CCCD là dữ liệu định danh: chỉ lưu đường dẫn tới blob storage
   có kiểm soát truy cập, không lưu ảnh trong DB, không lưu số CCCD
   dạng thô mà lưu hash để đối chiếu trùng.
   ------------------------------------------------------------ */
CREATE TABLE KycVerifications (
    KycId           BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    UserId          BIGINT         NOT NULL,
    -- Tên/ngày sinh khai theo CCCD, để admin đối chiếu với ảnh.
    DeclaredName    NVARCHAR(150)  NOT NULL,
    DeclaredDob     DATE           NOT NULL,
    NationalIdHash  VARBINARY(64)  NOT NULL,
    -- 4 số cuối để admin và học viên nhận ra hồ sơ, không đủ để lộ danh tính.
    NationalIdLast4 CHAR(4)        NOT NULL,
    FrontImagePath  NVARCHAR(400)  NOT NULL,
    BackImagePath   NVARCHAR(400)  NOT NULL,
    SelfieImagePath NVARCHAR(400)  NULL,
    Status          VARCHAR(20)    NOT NULL,
    -- Lý do từ chối là bắt buộc khi rejected, hiển thị ở màn profile.
    RejectReason    NVARCHAR(500)  NULL,
    SubmittedAt     DATETIMEOFFSET NOT NULL CONSTRAINT DF_Kyc_SubmittedAt DEFAULT SYSDATETIMEOFFSET(),
    ReviewedAt      DATETIMEOFFSET NULL,
    ReviewedByUserId BIGINT        NULL,
    CONSTRAINT FK_Kyc_Users FOREIGN KEY (UserId) REFERENCES Users (UserId),
    CONSTRAINT FK_Kyc_Reviewer FOREIGN KEY (ReviewedByUserId) REFERENCES Users (UserId),
    CONSTRAINT CK_Kyc_Status CHECK (Status IN ('pending', 'approved', 'rejected')),
    CONSTRAINT CK_Kyc_RejectReason CHECK (Status <> 'rejected' OR RejectReason IS NOT NULL),
    CONSTRAINT CK_Kyc_Reviewed CHECK (Status = 'pending' OR ReviewedAt IS NOT NULL)
);
-- Hàng đợi duyệt của admin lọc theo Status rồi sắp theo thời gian gửi.
CREATE INDEX IX_Kyc_Status_SubmittedAt ON KycVerifications (Status, SubmittedAt);
-- Mỗi user chỉ được có một hồ sơ đang chờ duyệt.
CREATE UNIQUE INDEX UX_Kyc_UserPending ON KycVerifications (UserId) WHERE Status = 'pending';
-- Một CCCD chỉ gắn với một tài khoản đã duyệt: chặn thi hộ bằng nhiều account.
CREATE UNIQUE INDEX UX_Kyc_NationalIdApproved ON KycVerifications (NationalIdHash) WHERE Status = 'approved';
/* Chiều ngược lại của index trên: một tài khoản chỉ được có MỘT hồ sơ đã
   duyệt. Thiếu nó thì một account gom được nhiều CCCD khác nhau, tức là
   vẫn thi hộ được — chỉ đổi chiều lách. */
CREATE UNIQUE INDEX UX_Kyc_UserApproved ON KycVerifications (UserId) WHERE Status = 'approved';

/* ============================================================
   2. Content — đề thi, part, câu hỏi, đáp án
   ============================================================ */

/* Part 1..7 là hằng số của TOEIC L&R, không phải dữ liệu admin thêm.
   Giữ thành bảng để FK và để join ra tên part khi báo cáo. */
CREATE TABLE Parts (
    PartNumber      TINYINT       NOT NULL PRIMARY KEY,
    Section         VARCHAR(10)   NOT NULL,
    Name            NVARCHAR(50)  NOT NULL,
    QuestionCount   SMALLINT      NOT NULL,
    -- Part 2 chỉ có 3 lựa chọn (spec §4). Số option là thuộc tính của
    -- part, không hardcode 4 ở tầng UI hay tầng API.
    OptionCount     TINYINT       NOT NULL,
    CONSTRAINT CK_Parts_Section CHECK (Section IN ('Listening', 'Reading')),
    CONSTRAINT CK_Parts_OptionCount CHECK (OptionCount BETWEEN 2 AND 4)
);
INSERT INTO Parts (PartNumber, Section, Name, QuestionCount, OptionCount) VALUES
    (1, 'Listening', N'Ảnh',          6,  4),
    (2, 'Listening', N'Hỏi đáp',     25,  3),
    (3, 'Listening', N'Hội thoại',   39,  4),
    (4, 'Listening', N'Bài nói',     30,  4),
    (5, 'Reading',   N'Câu đơn',     30,  4),
    (6, 'Reading',   N'Điền đoạn',   16,  4),
    (7, 'Reading',   N'Đọc hiểu',    54,  4);

/* Các nhãn đáp án hợp lệ của từng part.
   OptionCount ở trên là số để đọc và để tầng UI render; bảng này là thứ
   DB thật sự kiểm được. Không có nó thì Part 2 (3 lựa chọn) vẫn nhận
   được đáp án 'D' — CHECK trên QuestionOptions chỉ biết 'A'..'D' chung
   cho mọi part, không biết part nào dừng ở đâu. */
CREATE TABLE PartOptionLabels (
    PartNumber      TINYINT        NOT NULL,
    OptionLabel     CHAR(1)        NOT NULL,
    CONSTRAINT PK_PartOptionLabels PRIMARY KEY (PartNumber, OptionLabel),
    CONSTRAINT FK_PartOptionLabels_Parts FOREIGN KEY (PartNumber) REFERENCES Parts (PartNumber),
    CONSTRAINT CK_PartOptionLabels_Label CHECK (OptionLabel IN ('A', 'B', 'C', 'D'))
);
-- Sinh từ chính OptionCount để hai nguồn không thể lệch nhau.
INSERT INTO PartOptionLabels (PartNumber, OptionLabel)
SELECT p.PartNumber, l.OptionLabel
FROM Parts p
JOIN (VALUES ('A', 1), ('B', 2), ('C', 3), ('D', 4)) AS l (OptionLabel, Ordinal)
    ON l.Ordinal <= p.OptionCount;

CREATE TABLE Exams (
    ExamId          BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Title           NVARCHAR(200)  NOT NULL,
    Description     NVARCHAR(1000) NULL,
    -- Tổng thời gian tính bằng phút; TOEIC L&R chuẩn là 120.
    DurationMinutes SMALLINT       NOT NULL CONSTRAINT DF_Exams_Duration DEFAULT (120),
    /* Vòng đời đề: draft (đang soạn, wizard 3 bước) → published
       (học viên thấy được) → archived (ẩn khỏi danh sách, bài thi cũ
       vẫn xem lại được). */
    Status          VARCHAR(20)    NOT NULL CONSTRAINT DF_Exams_Status DEFAULT ('draft'),
    CreatedByUserId BIGINT         NOT NULL,
    PublishedAt     DATETIMEOFFSET NULL,
    IsDeleted       BIT            NOT NULL CONSTRAINT DF_Exams_IsDeleted DEFAULT (0),
    CreatedAt       DATETIMEOFFSET NOT NULL CONSTRAINT DF_Exams_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
    UpdatedAt       DATETIMEOFFSET NULL,
    CONSTRAINT FK_Exams_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES Users (UserId),
    CONSTRAINT CK_Exams_Status CHECK (Status IN ('draft', 'published', 'archived')),
    CONSTRAINT CK_Exams_Duration CHECK (DurationMinutes BETWEEN 1 AND 300),
    CONSTRAINT CK_Exams_PublishedAt CHECK (Status <> 'published' OR PublishedAt IS NOT NULL)
);
CREATE INDEX IX_Exams_Status ON Exams (Status) WHERE IsDeleted = 0;

/* Nhóm câu hỏi dùng chung ngữ cảnh: một hội thoại Part 3 gồm 3 câu,
   một đoạn văn Part 7 gồm nhiều câu. Part 1/2/5 mỗi câu độc lập nên
   GroupId để NULL — không bắt tạo group giả cho từng câu. */
CREATE TABLE QuestionGroups (
    GroupId         BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ExamId          BIGINT         NOT NULL,
    PartNumber      TINYINT        NOT NULL,
    -- Đoạn văn Part 6/7. Part 3/4 thì phần đọc hiểu nằm trong audio.
    PassageText     NVARCHAR(MAX)  NULL,
    -- Part 3/4: một file audio cho cả nhóm.
    AudioPath       NVARCHAR(400)  NULL,
    ImagePath       NVARCHAR(400)  NULL,
    DisplayOrder    SMALLINT       NOT NULL,
    CONSTRAINT FK_QGroups_Exams FOREIGN KEY (ExamId) REFERENCES Exams (ExamId),
    CONSTRAINT FK_QGroups_Parts FOREIGN KEY (PartNumber) REFERENCES Parts (PartNumber),
    /* Đích cho FK tổ hợp từ Questions. Bản thân GroupId đã unique, cặp này
       không siết thêm gì — nó chỉ để Questions gắn được (GroupId, ExamId,
       PartNumber) vào một đích duy nhất. */
    CONSTRAINT UQ_QGroups_Id_Exam_Part UNIQUE (GroupId, ExamId, PartNumber)
);
CREATE INDEX IX_QGroups_Exam_Part ON QuestionGroups (ExamId, PartNumber, DisplayOrder);

CREATE TABLE Questions (
    QuestionId      BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ExamId          BIGINT         NOT NULL,
    PartNumber      TINYINT        NOT NULL,
    GroupId         BIGINT         NULL,
    -- Số câu trong đề, 1..200. Đây là số hiển thị trên panel 200 ô.
    QuestionNumber  SMALLINT       NOT NULL,
    -- Part 2 không có đề bài in (spec §4) nên cho phép NULL.
    StemText        NVARCHAR(MAX)  NULL,
    -- Part 1: mỗi câu một ảnh riêng. Part 3/4: audio ở cấp group.
    ImagePath       NVARCHAR(400)  NULL,
    AudioPath       NVARCHAR(400)  NULL,
    /* Giải thích chỉ được trả về ở màn Review, không bao giờ trả trong
       lúc thi (spec §2 ràng buộc 3). Ràng buộc đó thuộc tầng API —
       lưu cùng bảng là được, miễn DTO lúc thi không map cột này. */
    Explanation     NVARCHAR(MAX)  NULL,
    TranscriptText  NVARCHAR(MAX)  NULL,
    IsDeleted       BIT            NOT NULL CONSTRAINT DF_Questions_IsDeleted DEFAULT (0),
    CONSTRAINT FK_Questions_Exams FOREIGN KEY (ExamId) REFERENCES Exams (ExamId),
    CONSTRAINT FK_Questions_Parts FOREIGN KEY (PartNumber) REFERENCES Parts (PartNumber),
    /* FK tổ hợp thay cho FK chỉ-GroupId: group phải thuộc CÙNG đề và CÙNG
       part với câu hỏi. Bản cũ chỉ kiểm group có tồn tại, nên một câu
       Part 2 của đề A gắn được vào group Part 7 của đề B. GroupId NULL
       vẫn bỏ qua kiểm tra (câu độc lập Part 1/2/5). */
    CONSTRAINT FK_Questions_Groups FOREIGN KEY (GroupId, ExamId, PartNumber)
        REFERENCES QuestionGroups (GroupId, ExamId, PartNumber),
    CONSTRAINT CK_Questions_Number CHECK (QuestionNumber BETWEEN 1 AND 200),
    -- Đích cho FK tổ hợp từ QuestionOptions và từ các bảng đáp án.
    CONSTRAINT UQ_Questions_Id_Part UNIQUE (QuestionId, PartNumber)
);
-- Không cho hai câu trùng số trong cùng một đề.
CREATE UNIQUE INDEX UX_Questions_Exam_Number ON Questions (ExamId, QuestionNumber) WHERE IsDeleted = 0;
CREATE INDEX IX_Questions_Exam_Part ON Questions (ExamId, PartNumber) WHERE IsDeleted = 0;

CREATE TABLE QuestionOptions (
    OptionId        BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    QuestionId      BIGINT         NOT NULL,
    /* PartNumber lặp lại từ Questions. Chấp nhận denormalize vì đây là
       cách duy nhất để FK tổ hợp xuống PartOptionLabels kiểm được nhãn:
       FK không nhìn xuyên bảng được. FK_QOptions_Questions bên dưới giữ
       cột này luôn khớp với part thật của câu hỏi. */
    PartNumber      TINYINT        NOT NULL,
    -- 'A'..'D', nhưng số nhãn thực tế do PartOptionLabels chốt theo part.
    OptionLabel     CHAR(1)        NOT NULL,
    OptionText      NVARCHAR(1000) NULL,
    /* Đáp án đúng nằm ở đây. Tầng API phải chiếu cột này ra khỏi
       response trong lúc thi — đây là cột nhạy cảm nhất của cả lược đồ. */
    IsCorrect       BIT            NOT NULL CONSTRAINT DF_QOptions_IsCorrect DEFAULT (0),
    -- Giữ PartNumber khớp với part thật của câu hỏi.
    CONSTRAINT FK_QOptions_Questions FOREIGN KEY (QuestionId, PartNumber)
        REFERENCES Questions (QuestionId, PartNumber),
    -- Chốt nhãn theo part: Part 2 không nhận được 'D'.
    CONSTRAINT FK_QOptions_PartLabels FOREIGN KEY (PartNumber, OptionLabel)
        REFERENCES PartOptionLabels (PartNumber, OptionLabel),
    -- Đích cho FK tổ hợp từ AttemptAnswers/PracticeAnswers.
    CONSTRAINT UQ_QOptions_Question_Option UNIQUE (QuestionId, OptionId)
);
CREATE UNIQUE INDEX UX_QOptions_Question_Label ON QuestionOptions (QuestionId, OptionLabel);
-- Mỗi câu đúng một đáp án đúng: trắc nghiệm một lựa chọn (spec §3).
CREATE UNIQUE INDEX UX_QOptions_OneCorrect ON QuestionOptions (QuestionId) WHERE IsCorrect = 1;

/* ============================================================
   3. Payment — gói lượt, đơn hàng, ví credit
   ============================================================ */

CREATE TABLE Packages (
    PackageId       INT            NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Code            VARCHAR(30)    NOT NULL UNIQUE,
    Name            NVARCHAR(100)  NOT NULL,
    CreditAmount    SMALLINT       NOT NULL,
    -- VND không có đơn vị nhỏ hơn đồng nên DECIMAL(12,0) là đủ.
    Price           DECIMAL(12, 0) NOT NULL,
    Description     NVARCHAR(500)  NULL,
    IsActive        BIT            NOT NULL CONSTRAINT DF_Packages_IsActive DEFAULT (1),
    DisplayOrder    SMALLINT       NOT NULL CONSTRAINT DF_Packages_Order DEFAULT (0),
    CreatedAt       DATETIMEOFFSET NOT NULL CONSTRAINT DF_Packages_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
    UpdatedAt       DATETIMEOFFSET NULL,
    CONSTRAINT CK_Packages_Credit CHECK (CreditAmount > 0),
    CONSTRAINT CK_Packages_Price CHECK (Price >= 0)
);

CREATE TABLE Orders (
    OrderId         BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    -- Mã đơn hiển thị cho người dùng và đối soát với cổng: TOEIC-20260920-4417.
    OrderCode       VARCHAR(40)    NOT NULL,
    UserId          BIGINT         NOT NULL,
    PackageId       INT            NOT NULL,
    /* Chốt giá và số credit tại thời điểm mua. Admin sửa giá gói sau đó
       không được làm thay đổi đơn cũ — nên KHÔNG join ra Packages để
       lấy giá khi hiển thị lịch sử. */
    UnitPrice       DECIMAL(12, 0) NOT NULL,
    CreditAmount    SMALLINT       NOT NULL,
    PaymentMethod   VARCHAR(20)    NOT NULL,
    /* Vòng đời đơn (spec §5.1): pending → paid | failed | expired.
       `expired` là nhánh bình thường, không phải lỗi hệ thống — gộp nó
       vào error thì người dùng tưởng web lỗi và thanh toán lại. */
    Status          VARCHAR(20)    NOT NULL CONSTRAINT DF_Orders_Status DEFAULT ('pending'),
    -- Mã/thông điệp lỗi từ cổng, hiển thị ở màn payment-result state=failed.
    GatewayCode     VARCHAR(50)    NULL,
    GatewayMessage  NVARCHAR(500)  NULL,
    -- Mã giao dịch bên cổng, dùng để đối soát. Unique khi đã có.
    GatewayTxnRef   VARCHAR(100)   NULL,
    CreatedAt       DATETIMEOFFSET NOT NULL CONSTRAINT DF_Orders_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
    -- Đơn hết hiệu lực sau 15 phút không hoàn tất (màn checkout state=pending).
    ExpiresAt       DATETIMEOFFSET NOT NULL,
    PaidAt          DATETIMEOFFSET NULL,
    CONSTRAINT FK_Orders_Users FOREIGN KEY (UserId) REFERENCES Users (UserId),
    CONSTRAINT FK_Orders_Packages FOREIGN KEY (PackageId) REFERENCES Packages (PackageId),
    CONSTRAINT CK_Orders_Status CHECK (Status IN ('pending', 'paid', 'failed', 'expired')),
    CONSTRAINT CK_Orders_Method CHECK (PaymentMethod IN ('vnpay', 'momo')),
    CONSTRAINT CK_Orders_PaidAt CHECK (Status <> 'paid' OR PaidAt IS NOT NULL),
    CONSTRAINT CK_Orders_Amounts CHECK (UnitPrice >= 0 AND CreditAmount > 0),
    /* Đơn không được hết hạn trước cả lúc tạo. ExamAttempts đã có ràng
       buộc tương đương (CK_Attempts_Expires); thiếu ở đây là bỏ sót. */
    CONSTRAINT CK_Orders_Expires CHECK (ExpiresAt > CreatedAt)
);
CREATE UNIQUE INDEX UX_Orders_Code ON Orders (OrderCode);
CREATE UNIQUE INDEX UX_Orders_GatewayTxnRef ON Orders (GatewayTxnRef) WHERE GatewayTxnRef IS NOT NULL;
-- Màn admin/orders lọc theo trạng thái + khoảng ngày.
CREATE INDEX IX_Orders_Status_CreatedAt ON Orders (Status, CreatedAt DESC);
CREATE INDEX IX_Orders_User_CreatedAt ON Orders (UserId, CreatedAt DESC);
/* Chặn tạo đơn thứ hai khi đơn cũ còn pending — đúng cái mà màn
   checkout?state=pending cảnh báo. Không có ràng buộc này thì người
   dùng bấm back từ cổng rồi mua lại là trừ tiền hai lần.

   BẮT BUỘC ĐI KÈM: job hết hạn đơn ở dưới.
   Filtered index không được dùng hàm không tất định, nên không thể viết
   WHERE Status = 'pending' AND ExpiresAt > SYSDATETIMEOFFSET(). Hệ quả:
   một đơn pending đã quá 15 phút vẫn chiếm chỗ và KHOÁ VĨNH VIỄN việc
   mua tiếp của user đó cho tới khi có ai đổi Status. Điều kiện "chưa hết
   hạn" vì vậy phải do job bên dưới thực thi — nó là phần của ràng buộc
   này, không phải việc dọn dẹp cho đẹp. */
CREATE UNIQUE INDEX UX_Orders_OnePendingPerUser ON Orders (UserId) WHERE Status = 'pending';
-- Index cho chính job đó: quét đúng các đơn pending đã quá hạn.
CREATE INDEX IX_Orders_PendingExpiry ON Orders (ExpiresAt) WHERE Status = 'pending';
GO

/* Job hết hạn đơn. Chạy định kỳ (SQL Agent, Hangfire, hoặc
   BackgroundService của ASP.NET Core — tuỳ hạ tầng), chu kỳ <= 1 phút để
   người dùng không phải chờ quá lâu sau khi đơn cũ hết hiệu lực.
   Phải chạy TRƯỚC khi user tạo đơn mới; gọi thẳng nó ở đầu luồng tạo đơn
   là cách chắc nhất, không chỉ dựa vào chu kỳ. */
CREATE PROCEDURE usp_ExpireStalePendingOrders
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Orders
    SET Status = 'expired'
    WHERE Status = 'pending'
      AND ExpiresAt <= SYSDATETIMEOFFSET();
END;
GO

/* ---- Ví credit -------------------------------------------
   Số dư KHÔNG lưu thành một cột đếm. Lưu sổ cái từng biến động rồi
   tính tổng: mọi lần trừ/cộng lượt đều truy được nguồn gốc, và không
   có chuyện số dư lệch âm thầm do một lần update trượt.
   ---------------------------------------------------------- */
CREATE TABLE CreditTransactions (
    TransactionId   BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    UserId          BIGINT         NOT NULL,
    /* Dương = cộng (mua gói, admin cấp bù). Âm = trừ (bắt đầu một
       phiên thi). Không dùng cột Type riêng vì dấu đã nói đủ. */
    Delta           SMALLINT       NOT NULL,
    Reason          VARCHAR(30)    NOT NULL,
    -- Nguồn gốc: đơn hàng nào cộng, phiên thi nào trừ.
    OrderId         BIGINT         NULL,
    AttemptId       BIGINT         NULL,
    Note            NVARCHAR(300)  NULL,
    -- Admin cấp bù thì ghi lại ai cấp.
    CreatedByUserId BIGINT         NULL,
    CreatedAt       DATETIMEOFFSET NOT NULL CONSTRAINT DF_CreditTxn_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT FK_CreditTxn_Users FOREIGN KEY (UserId) REFERENCES Users (UserId),
    CONSTRAINT FK_CreditTxn_Orders FOREIGN KEY (OrderId) REFERENCES Orders (OrderId),
    CONSTRAINT FK_CreditTxn_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES Users (UserId),
    CONSTRAINT CK_CreditTxn_Delta CHECK (Delta <> 0),
    CONSTRAINT CK_CreditTxn_Reason CHECK (Reason IN ('purchase', 'exam_start', 'admin_grant', 'admin_revoke')),
    /* Dấu phải khớp với Reason. Comment ở trên nói "dấu đã nói đủ" — đúng,
       và chính vì thế dấu phải bị ràng buộc: nếu không, một dòng
       'exam_start' với Delta dương là cấp lượt miễn phí, còn 'purchase'
       với Delta âm là trừ tiền của người vừa trả tiền. */
    CONSTRAINT CK_CreditTxn_DeltaSign CHECK (
        (Reason IN ('purchase', 'admin_grant')     AND Delta > 0) OR
        (Reason IN ('exam_start', 'admin_revoke')  AND Delta < 0)),
    /* Nguồn gốc bắt buộc theo Reason. Đây là chỗ hai unique index chống
       cộng/trừ trùng bên dưới dựa vào: cả hai đều lọc IS NOT NULL, nên một
       dòng 'purchase' với OrderId NULL lọt qua index và cộng credit lần
       thứ hai. Không có CHECK này thì hai index đó chỉ là gợi ý. */
    CONSTRAINT CK_CreditTxn_Source CHECK (
        (Reason = 'purchase'   AND OrderId   IS NOT NULL AND AttemptId IS NULL) OR
        (Reason = 'exam_start' AND AttemptId IS NOT NULL AND OrderId   IS NULL) OR
        (Reason IN ('admin_grant', 'admin_revoke') AND OrderId IS NULL AND AttemptId IS NULL)),
    -- Admin cấp/thu lượt thì phải biết ai làm, để truy được trách nhiệm.
    CONSTRAINT CK_CreditTxn_AdminActor CHECK (
        Reason NOT IN ('admin_grant', 'admin_revoke') OR CreatedByUserId IS NOT NULL)
);
CREATE INDEX IX_CreditTxn_User_CreatedAt ON CreditTransactions (UserId, CreatedAt DESC);
-- Một đơn đã thanh toán chỉ được cộng credit một lần: chống double-credit
-- khi cổng gọi callback lặp (IPN retry là hành vi bình thường của VNPay/MoMo).
CREATE UNIQUE INDEX UX_CreditTxn_OrderPurchase
    ON CreditTransactions (OrderId)
    WHERE Reason = 'purchase' AND OrderId IS NOT NULL;

/* ============================================================
   4. Taking — phiên thi, đáp án đã chọn, kết quả
   ============================================================ */

CREATE TABLE ExamAttempts (
    AttemptId       BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    UserId          BIGINT         NOT NULL,
    ExamId          BIGINT         NOT NULL,
    /* State machine phiên thi (spec §5.2):
       in_progress → submitted → graded, hoặc → abandoned.
       `expired` không phải state riêng của phiên: hết giờ thì
       auto-submit, tức là vẫn đi qua submitted → graded. Cờ
       IsAutoSubmitted bên dưới phân biệt hai đường vào submitted. */
    Status          VARCHAR(20)    NOT NULL CONSTRAINT DF_Attempts_Status DEFAULT ('in_progress'),
    StartedAt       DATETIMEOFFSET NOT NULL CONSTRAINT DF_Attempts_StartedAt DEFAULT SYSDATETIMEOFFSET(),
    /* Mốc hết giờ tuyệt đối do server chốt (spec §2 ràng buộc 1).
       Client chỉ đếm ngược để hiển thị; reload tab hay đổi giờ máy
       không dịch được mốc này. */
    ExpiresAt       DATETIMEOFFSET NOT NULL,
    SubmittedAt     DATETIMEOFFSET NULL,
    GradedAt        DATETIMEOFFSET NULL,
    IsAutoSubmitted BIT            NOT NULL CONSTRAINT DF_Attempts_AutoSubmit DEFAULT (0),
    -- Điểm chỉ có sau khi chấm. Thang 5–495 mỗi kỹ năng, tổng 10–990.
    ListeningRaw    SMALLINT       NULL,
    ReadingRaw      SMALLINT       NULL,
    ListeningScore  SMALLINT       NULL,
    ReadingScore    SMALLINT       NULL,
    TotalScore      SMALLINT       NULL,
    /* CEFR suy ra từ TotalScore qua bảng CefrBands. Lưu lại tại thời
       điểm chấm để lịch sử không đổi nếu sau này chuẩn có thay đổi. */
    CefrLevel       VARCHAR(10)    NULL,
    CONSTRAINT FK_Attempts_Users FOREIGN KEY (UserId) REFERENCES Users (UserId),
    CONSTRAINT FK_Attempts_Exams FOREIGN KEY (ExamId) REFERENCES Exams (ExamId),
    CONSTRAINT CK_Attempts_Status CHECK (Status IN ('in_progress', 'submitted', 'graded', 'abandoned')),
    CONSTRAINT CK_Attempts_Submitted CHECK (Status NOT IN ('submitted', 'graded') OR SubmittedAt IS NOT NULL),
    /* Đã chấm thì phải có đủ điểm hai kỹ năng, không chỉ tổng: màn kết quả
       và màn review đều hiển thị tách Listening/Reading, và TotalScore
       phải đúng bằng tổng hai phần — không để tổng trôi khỏi các thành
       phần của nó. */
    CONSTRAINT CK_Attempts_Graded CHECK (Status <> 'graded' OR (
        GradedAt       IS NOT NULL AND
        TotalScore     IS NOT NULL AND
        ListeningScore IS NOT NULL AND
        ReadingScore   IS NOT NULL AND
        ListeningRaw   IS NOT NULL AND
        ReadingRaw     IS NOT NULL AND
        CefrLevel      IS NOT NULL AND
        TotalScore = ListeningScore + ReadingScore)),
    /* CefrLevel chốt theo 6 band của CefrBands. Không dùng FK vì cột này
       cố ý snapshot lại band lúc chấm (ghi chú §8.7) — FK sẽ chặn việc sửa
       bảng band sau này. CHECK theo hằng số giữ được lịch sử mà vẫn chặn
       giá trị sai chính tả. */
    CONSTRAINT CK_Attempts_CefrLevel CHECK (
        CefrLevel IS NULL OR CefrLevel IN ('below_A1', 'A1', 'A2', 'B1', 'B2', 'C1')),
    CONSTRAINT CK_Attempts_Expires CHECK (ExpiresAt > StartedAt),
    CONSTRAINT CK_Attempts_RawRange CHECK (
        (ListeningRaw IS NULL OR ListeningRaw BETWEEN 0 AND 100) AND
        (ReadingRaw   IS NULL OR ReadingRaw   BETWEEN 0 AND 100)),
    CONSTRAINT CK_Attempts_ScoreRange CHECK (
        (ListeningScore IS NULL OR ListeningScore BETWEEN 5 AND 495) AND
        (ReadingScore   IS NULL OR ReadingScore   BETWEEN 5 AND 495) AND
        (TotalScore     IS NULL OR TotalScore     BETWEEN 10 AND 990))
);
-- Màn exam-history và dashboard đều truy theo user, mới nhất trước.
CREATE INDEX IX_Attempts_User_StartedAt ON ExamAttempts (UserId, StartedAt DESC);
CREATE INDEX IX_Attempts_Exam ON ExamAttempts (ExamId);
/* Mỗi user chỉ có một phiên đang làm. Đây là cái đỡ cho màn exam-list:
   thẻ "Đang làm dở" + nút Tiếp tục chỉ đúng khi có tối đa một phiên mở.
   Job dọn phiên quá hạn phải chuyển state trước khi user vào đề mới. */
CREATE UNIQUE INDEX UX_Attempts_OneInProgress ON ExamAttempts (UserId) WHERE Status = 'in_progress';

/* FK này phải đặt rời vì CreditTransactions được tạo ở mục 3, trước khi
   ExamAttempts tồn tại. Thiếu nó thì cột AttemptId có thể trỏ vào phiên
   thi không tồn tại — mất dấu vết lượt credit đã trừ đi đâu. */
ALTER TABLE CreditTransactions
    ADD CONSTRAINT FK_CreditTxn_Attempts
    FOREIGN KEY (AttemptId) REFERENCES ExamAttempts (AttemptId);

/* Một phiên thi chỉ được trừ credit một lần. Cùng vai trò với
   UX_CreditTxn_OrderPurchase ở chiều cộng: chặn trừ hai lần nếu request
   "bắt đầu thi" bị gửi trùng.
   Lọc thêm IS NOT NULL: SQL Server coi các NULL là bằng nhau trong
   unique index, nên nếu có dòng 'exam_start' mà AttemptId NULL thì chỉ
   tồn tại được đúng một dòng như vậy trong cả bảng. */
CREATE UNIQUE INDEX UX_CreditTxn_AttemptStart
    ON CreditTransactions (AttemptId)
    WHERE Reason = 'exam_start' AND AttemptId IS NOT NULL;
GO

/* Số dư không được âm.
   CHECK không làm được việc này: số dư là tổng nhiều dòng, còn CHECK chỉ
   thấy một dòng. Nên phải là trigger. Không có nó thì "gate trước khi
   thi" chỉ tồn tại ở tầng application — hai request bắt đầu thi gửi song
   song đều đọc số dư 1, đều thấy đủ, và cả hai đều trừ.
   UX_Attempts_OneInProgress đỡ được đúng trường hợp thi song song, nhưng
   không đỡ được admin_revoke thu quá số đang có.

   Trigger AFTER + ROLLBACK: chạy sau khi ghi rồi mới huỷ, nên tốn hơn
   INSTEAD OF một chút, nhưng đổi lại không phải viết lại logic insert và
   không có đường nào lách được — kể cả insert nhiều dòng một lệnh. */
CREATE TRIGGER trg_CreditTransactions_NoNegativeBalance
ON CreditTransactions
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM (SELECT DISTINCT UserId FROM inserted) touched
        CROSS APPLY (
            SELECT SUM(ct.Delta) AS Balance
            FROM CreditTransactions ct
            WHERE ct.UserId = touched.UserId
        ) b
        WHERE b.Balance < 0
    )
    BEGIN
        THROW 50001, N'Credit balance cannot go negative.', 1;
    END
END;
GO

/* Chỉ được mở phiên thi trên đề đã published và chưa xoá mềm.
   Cũng không làm được bằng CHECK vì phải đọc bảng Exams. Không có nó thì
   đề đang soạn dở (draft, wizard 3 bước chưa xong) vẫn thi được nếu biết
   ExamId — và học viên mất một lượt credit cho một đề chưa hoàn chỉnh.
   Chỉ kiểm lúc INSERT: đề chuyển sang archived sau đó không được làm hỏng
   phiên đang làm hoặc lịch sử đã chấm (archived cố ý vẫn xem lại được). */
CREATE TRIGGER trg_ExamAttempts_PublishedExamOnly
ON ExamAttempts
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN Exams e ON e.ExamId = i.ExamId
        WHERE e.Status <> 'published' OR e.IsDeleted = 1
    )
    BEGIN
        THROW 50002, N'Exam attempts are allowed only on published exams.', 1;
    END
END;
GO

/* Đáp án của từng câu trong phiên thi.
   Mỗi lần chọn là một lần UPSERT (debounce ~400ms, spec §2 ràng buộc 2)
   nên bảng này ghi rất nhiều — giữ nó hẹp, không nhồi cột phụ. */
CREATE TABLE AttemptAnswers (
    AttemptAnswerId BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    AttemptId       BIGINT         NOT NULL,
    QuestionId      BIGINT         NOT NULL,
    -- NULL = đã mở câu nhưng chưa chọn. Câu chưa trả lời tính là sai.
    SelectedOptionId BIGINT        NULL,
    -- State câu hỏi `marked` (spec §5.3): panel 200 ô tô màu theo cờ này.
    IsMarkedForReview BIT          NOT NULL CONSTRAINT DF_AttemptAnswers_Marked DEFAULT (0),
    /* Chỉ điền khi chấm. Lưu kết quả đúng/sai tại thời điểm chấm thay vì
       join lại QuestionOptions mỗi lần xem: admin sửa đáp án của đề sau
       đó không được làm đổi kết quả bài đã chấm. */
    IsCorrect       BIT            NULL,
    AnsweredAt      DATETIMEOFFSET NULL,
    UpdatedAt       DATETIMEOFFSET NOT NULL CONSTRAINT DF_AttemptAnswers_UpdatedAt DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT FK_AttemptAnswers_Attempts FOREIGN KEY (AttemptId) REFERENCES ExamAttempts (AttemptId),
    CONSTRAINT FK_AttemptAnswers_Questions FOREIGN KEY (QuestionId) REFERENCES Questions (QuestionId),
    /* FK tổ hợp: đáp án đã chọn phải là một lựa chọn CỦA CHÍNH câu hỏi đó.
       Bản cũ chỉ kiểm OptionId có tồn tại, nên lưu được đáp án của câu
       khác vào câu này — chấm xong là sai âm thầm, không bao giờ lộ ra.
       SelectedOptionId NULL vẫn bỏ qua kiểm tra: "đã mở câu, chưa chọn"
       vẫn lưu được như cũ. */
    CONSTRAINT FK_AttemptAnswers_Options FOREIGN KEY (QuestionId, SelectedOptionId)
        REFERENCES QuestionOptions (QuestionId, OptionId)
);
/* Một câu một dòng trong mỗi phiên. Unique ở đây là thứ làm cho việc
   lưu từng đáp án an toàn khi client gửi trùng (mạng chập chờn, retry). */
CREATE UNIQUE INDEX UX_AttemptAnswers_Attempt_Question ON AttemptAnswers (AttemptId, QuestionId);
/* QuestionId là khoá join của vw_UserPartAccuracy (bảng này → Questions).
   Không có index này thì màn phân tích điểm yếu scan cả bảng đáp án, và
   đây là bảng lớn nhất hệ thống (200 dòng mỗi phiên thi). */
CREATE INDEX IX_AttemptAnswers_QuestionId ON AttemptAnswers (QuestionId);

/* ============================================================
   5. Practice — luyện theo part (miễn phí, không trừ credit)
   ============================================================ */

/* Luyện tập KHÔNG tham chiếu credit và KHÔNG ghi CreditTransactions —
   đó là điều làm nó free ở tầng dữ liệu, không chỉ ở tầng UI. */
CREATE TABLE PracticeSessions (
    SessionId       BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    UserId          BIGINT         NOT NULL,
    PartNumber      TINYINT        NOT NULL,
    -- Số câu mỗi phiên do học viên chọn (10/20/…) ở màn practice-select.
    QuestionCount   SMALLINT       NOT NULL,
    Status          VARCHAR(20)    NOT NULL CONSTRAINT DF_Practice_Status DEFAULT ('in_progress'),
    CorrectCount    SMALLINT       NULL,
    StartedAt       DATETIMEOFFSET NOT NULL CONSTRAINT DF_Practice_StartedAt DEFAULT SYSDATETIMEOFFSET(),
    FinishedAt      DATETIMEOFFSET NULL,
    CONSTRAINT FK_Practice_Users FOREIGN KEY (UserId) REFERENCES Users (UserId),
    CONSTRAINT FK_Practice_Parts FOREIGN KEY (PartNumber) REFERENCES Parts (PartNumber),
    CONSTRAINT CK_Practice_Status CHECK (Status IN ('in_progress', 'finished', 'abandoned')),
    CONSTRAINT CK_Practice_Count CHECK (QuestionCount > 0),
    CONSTRAINT CK_Practice_Finished CHECK (Status <> 'finished' OR (FinishedAt IS NOT NULL AND CorrectCount IS NOT NULL))
);
CREATE INDEX IX_Practice_User_StartedAt ON PracticeSessions (UserId, StartedAt DESC);

CREATE TABLE PracticeAnswers (
    PracticeAnswerId BIGINT        NOT NULL IDENTITY(1,1) PRIMARY KEY,
    SessionId       BIGINT         NOT NULL,
    QuestionId      BIGINT         NOT NULL,
    SelectedOptionId BIGINT        NULL,
    /* Luyện tập chấm ngay từng câu (spec §3) nên IsCorrect điền luôn
       lúc trả lời, khác với AttemptAnswers chỉ điền khi chấm cuối bài. */
    IsCorrect       BIT            NULL,
    DisplayOrder    SMALLINT       NOT NULL,
    AnsweredAt      DATETIMEOFFSET NULL,
    CONSTRAINT FK_PracticeAnswers_Sessions FOREIGN KEY (SessionId) REFERENCES PracticeSessions (SessionId),
    CONSTRAINT FK_PracticeAnswers_Questions FOREIGN KEY (QuestionId) REFERENCES Questions (QuestionId),
    -- Cùng lý do như FK_AttemptAnswers_Options: đáp án phải thuộc câu hỏi đó.
    CONSTRAINT FK_PracticeAnswers_Options FOREIGN KEY (QuestionId, SelectedOptionId)
        REFERENCES QuestionOptions (QuestionId, OptionId)
);
CREATE UNIQUE INDEX UX_PracticeAnswers_Session_Question ON PracticeAnswers (SessionId, QuestionId);
-- Khoá join của vw_UserPartAccuracy, như IX_AttemptAnswers_QuestionId.
CREATE INDEX IX_PracticeAnswers_QuestionId ON PracticeAnswers (QuestionId);

/* ============================================================
   6. Quy đổi điểm và CEFR
   ============================================================ */

/* Bảng raw→scaled: số câu đúng đổi sang thang 5–495.
   Không tuyến tính và KHÁC NHAU GIỮA CÁC ĐỀ, nên gắn theo ExamId và
   để admin sửa được (màn admin/score-conversion).
   ExamId NULL = bảng mặc định dùng cho đề chưa có bảng riêng. */
CREATE TABLE ScoreConversions (
    ConversionId    BIGINT         NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ExamId          BIGINT         NULL,
    Section         VARCHAR(10)    NOT NULL,
    RawScore        SMALLINT       NOT NULL,
    ScaledScore     SMALLINT       NOT NULL,
    CONSTRAINT FK_ScoreConv_Exams FOREIGN KEY (ExamId) REFERENCES Exams (ExamId),
    CONSTRAINT CK_ScoreConv_Section CHECK (Section IN ('Listening', 'Reading')),
    CONSTRAINT CK_ScoreConv_Raw CHECK (RawScore BETWEEN 0 AND 100),
    CONSTRAINT CK_ScoreConv_Scaled CHECK (ScaledScore BETWEEN 5 AND 495)
);
-- Một mốc raw chỉ có một điểm quy đổi trong mỗi (đề, kỹ năng).
CREATE UNIQUE INDEX UX_ScoreConv_Exam_Section_Raw
    ON ScoreConversions (ExamId, Section, RawScore) WHERE ExamId IS NOT NULL;
CREATE UNIQUE INDEX UX_ScoreConv_Default_Section_Raw
    ON ScoreConversions (Section, RawScore) WHERE ExamId IS NULL;

/* Ngưỡng CEFR — HẰNG SỐ theo chuẩn, không phải dữ liệu admin cấu hình.
   Đây là lý do bảng này không có cột UpdatedBy/UpdatedAt: không có
   luồng nào sửa nó. Admin chỉ sửa ScoreConversions ở trên.
   Thang tổng 10–990; band đầu là "dưới A1", không phải A1. */
CREATE TABLE CefrBands (
    CefrBandId      TINYINT        NOT NULL PRIMARY KEY,
    Level           VARCHAR(10)    NOT NULL UNIQUE,
    MinTotalScore   SMALLINT       NOT NULL,
    MaxTotalScore   SMALLINT       NOT NULL,
    CONSTRAINT CK_CefrBands_Range CHECK (MinTotalScore <= MaxTotalScore),
    CONSTRAINT CK_CefrBands_Bounds CHECK (MinTotalScore >= 10 AND MaxTotalScore <= 990)
);
INSERT INTO CefrBands (CefrBandId, Level, MinTotalScore, MaxTotalScore) VALUES
    (1, 'below_A1',  10, 119),
    (2, 'A1',       120, 224),
    (3, 'A2',       225, 549),
    (4, 'B1',       550, 784),
    (5, 'B2',       785, 944),
    (6, 'C1',       945, 990);

/* ============================================================
   7. View đọc — số dư ví và phân tích điểm yếu
   ============================================================ */
GO

/* Số dư credit = tổng sổ cái. Mọi chỗ cần "còn bao nhiêu lượt"
   (badge trên topbar, gate trước khi thi, màn wallet) đọc từ đây
   thay vì tự SUM lại mỗi nơi một kiểu. */
CREATE VIEW vw_UserCreditBalance
AS
SELECT
    u.UserId,
    CAST(ISNULL(SUM(ct.Delta), 0) AS INT) AS CreditBalance
FROM Users u
LEFT JOIN CreditTransactions ct ON ct.UserId = u.UserId
WHERE u.IsDeleted = 0
GROUP BY u.UserId;
GO

/* Phân tích điểm yếu theo part — nguồn dữ liệu cho màn phân tích mới
   và cho card "Độ chính xác theo part" ở dashboard.
   Gộp cả hai nguồn: câu trong bài thi đã chấm và câu trong phiên luyện.
   Tính năng này miễn phí nên view không lọc theo credit. */
/* MẪU SỐ LÀ MỌI CÂU ĐÃ GIAO, KHÔNG PHẢI MỌI CÂU ĐÃ TRẢ LỜI.
   Câu bỏ trắng tính là SAI (quy tắc ở AttemptAnswers.SelectedOptionId).
   Bản trước đếm COUNT(*) trên các dòng đáp án đã lưu và lọc
   IsCorrect IS NOT NULL, mà câu chưa mở thì KHÔNG có dòng nào — nên bỏ
   trắng cả part vẫn ra 100%. Đây là nguồn dữ liệu của màn phân tích điểm
   yếu, tức là nó sẽ báo "part yếu nhất" là part học viên chưa từng làm. */
CREATE VIEW vw_UserPartAccuracy
AS
WITH AllAnswers AS (
    /* Bài thi đã chấm: mẫu số là toàn bộ câu của đề trong part đó, lấy từ
       Questions rồi LEFT JOIN sang đáp án — câu không có dòng đáp án vẫn
       được đếm, với flag 0. */
    SELECT
        a.UserId,
        q.PartNumber,
        CASE WHEN aa.IsCorrect = 1 THEN 1 ELSE 0 END AS IsCorrectFlag
    FROM ExamAttempts a
    JOIN Questions q
        ON q.ExamId = a.ExamId
       AND q.IsDeleted = 0
    LEFT JOIN AttemptAnswers aa
        ON aa.AttemptId = a.AttemptId
       AND aa.QuestionId = q.QuestionId
    WHERE a.Status = 'graded'

    UNION ALL

    /* Phiên luyện đã xong: tập câu được giao chính là các dòng
       PracticeAnswers (đề luyện sinh ra lúc bắt đầu phiên), nên mẫu số là
       số dòng. Bỏ trắng (IsCorrect NULL) vẫn tính, với flag 0. */
    SELECT
        ps.UserId,
        q.PartNumber,
        CASE WHEN pa.IsCorrect = 1 THEN 1 ELSE 0 END AS IsCorrectFlag
    FROM PracticeAnswers pa
    JOIN PracticeSessions ps ON ps.SessionId = pa.SessionId
    JOIN Questions q ON q.QuestionId = pa.QuestionId
    WHERE ps.Status = 'finished'
)
SELECT
    aa.UserId,
    aa.PartNumber,
    p.Name              AS PartName,
    p.Section,
    -- Số câu đã GIAO trong part, kể cả câu bỏ trắng.
    COUNT(*)                  AS DeliveredCount,
    SUM(aa.IsCorrectFlag)     AS CorrectCount,
    -- Nhân 100.0 để ra số thực, không bị chia nguyên thành 0.
    CAST(100.0 * SUM(aa.IsCorrectFlag) / COUNT(*) AS DECIMAL(5, 2)) AS AccuracyPercent
FROM AllAnswers aa
JOIN Parts p ON p.PartNumber = aa.PartNumber
GROUP BY aa.UserId, aa.PartNumber, p.Name, p.Section;
GO

/* ============================================================
   8. Ghi chú thiết kế — những chỗ lược đồ cố tình KHÔNG làm
   ============================================================

   1. Không có cột CreditBalance trên Users.
      Số dư là tổng của CreditTransactions. Thêm cột đếm thì phải giữ
      đồng bộ hai nơi, và lần nào lệch cũng là lệch tiền của người dùng.
      Nếu sau này cần tốc độ, thêm indexed view hoặc cache — đừng thêm
      cột ghi tay.
      Số dư không âm do trg_CreditTransactions_NoNegativeBalance giữ, ở
      tầng DB — không chỉ ở gate của application.

   2. Không có bảng Refunds.
      Hoàn tiền nằm ngoài phạm vi (spec §3). Huỷ bài đang làm dở là mất
      lượt, đã chốt ở lượt review — nên nó chỉ là một dòng
      CreditTransactions âm với Reason = 'exam_start' đã ghi từ trước,
      không sinh thêm bản ghi hoàn.

   3. Không lưu thời gian còn lại của phiên thi.
      Chỉ lưu ExpiresAt tuyệt đối. Lưu "còn bao nhiêu giây" thì mỗi lần
      reload phải ghi lại, và client có cơ hội mặc cả về thời gian.

   4. Không có bảng riêng cho state `expired` của phiên thi.
      Hết giờ là auto-submit: Status = 'submitted' + IsAutoSubmitted = 1.
      Tách state riêng thì mọi truy vấn báo cáo phải nhớ cộng thêm một
      trạng thái nữa.

   5. Đáp án đúng và giải thích nằm cùng bảng với câu hỏi.
      Không tách bảng "bí mật" riêng vì tách cũng không thêm an toàn —
      ranh giới thật nằm ở tầng API: DTO lúc thi không map IsCorrect và
      Explanation (spec §2 ràng buộc 3). Đây là chỗ dễ rò nhất của cả
      hệ thống, nên khi port sang EF Core cần projection tường minh
      cho màn thi, không trả thẳng entity.

   6. Ảnh CCCD không nằm trong DB.
      Chỉ lưu đường dẫn tới blob storage có kiểm soát truy cập, và số
      CCCD lưu hash + 4 số cuối. Lưu ảnh định danh trong DB làm mọi
      bản backup thành một bản sao dữ liệu định danh.

   7. Bảng CefrBands không có luồng sửa.
      Ngưỡng CEFR là chuẩn cố định. Bảng admin sửa được là
      ScoreConversions (raw→scaled), vì bảng đó thật sự khác nhau giữa
      các đề. ExamAttempts.CefrLevel vì thế dùng CHECK theo hằng số chứ
      không FK sang bảng này: cột đó snapshot band lúc chấm.

   8. Hai ràng buộc phải dùng trigger, không phải CHECK.
      CHECK chỉ thấy một dòng của một bảng, nên hai việc sau không biểu
      diễn được bằng CHECK:
      - trg_CreditTransactions_NoNegativeBalance: số dư là tổng nhiều dòng.
      - trg_ExamAttempts_PublishedExamOnly: phải đọc Exams.Status.
      Không thích trigger thì lựa chọn thay thế là stored procedure độc
      quyền cho hai luồng đó — nhưng đừng bỏ xuống tầng application, vì
      cả hai đều là ràng buộc về tiền và về tính toàn vẹn của bài thi.

   9. PHẢI CÓ job hết hạn đơn — nó là phần của ràng buộc, không phải dọn dẹp.
      usp_ExpireStalePendingOrders phải chạy định kỳ (<= 1 phút) VÀ nên
      gọi ở đầu luồng tạo đơn. Lý do ở comment của
      UX_Orders_OnePendingPerUser: filtered index không dùng được hàm
      không tất định, nên "pending và chưa hết hạn" không viết được thành
      index. Không chạy job thì một đơn pending quá hạn khoá vĩnh viễn
      việc mua tiếp của user đó.

  10. Những gì vẫn thuộc tầng API, cố ý không đưa xuống DB.
      - Chiếu IsCorrect/Explanation ra khỏi DTO lúc thi (spec §2 rb 3).
        Xem ghi chú 5 ở trên; đây vẫn là chỗ dễ rò nhất.
      - Gate KYC approved trước khi trừ credit. DB giữ được "một hồ sơ
        duyệt cho mỗi user và mỗi CCCD" (UX_Kyc_UserApproved,
        UX_Kyc_NationalIdApproved), nhưng thứ tự "duyệt xong mới trừ lượt"
        là luồng, không phải ràng buộc dữ liệu.
      - Số câu mỗi phiên luyện khớp PracticeSessions.QuestionCount.
      - TotalScore suy từ ScoreConversions. DB chỉ kiểm
        TotalScore = ListeningScore + ReadingScore, không kiểm việc tra
        bảng quy đổi có đúng hay không.
   ============================================================ */
