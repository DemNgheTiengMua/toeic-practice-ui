-- docs/database-schema.sql
-- Multi-certificate mock-exam companion schema.
-- Spec: docs/superpowers/specs/2026-09-24-multi-cert-companion-design.md
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- ============ Domain 2: Catalog ============
CREATE TABLE Certificates (
  CertId       INT IDENTITY(1,1) PRIMARY KEY,
  Code         VARCHAR(20)  NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  Description  NVARCHAR(500) NULL,
  IsActive     BIT NOT NULL DEFAULT 1,
  CONSTRAINT UQ_Certificates_Code UNIQUE (Code)
);
GO
CREATE TABLE Skills (
  SkillId      INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  Modality     VARCHAR(10) NOT NULL
    CONSTRAINT CK_Skills_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Skills_Cert_Code UNIQUE (CertId, Code),
  -- composite target so Content can carry CertId down and pin isolation:
  CONSTRAINT UQ_Skills_Id_Cert UNIQUE (SkillId, CertId)
);
GO
CREATE TABLE Sections (
  SectionId    INT IDENTITY(1,1) PRIMARY KEY,
  SkillId      INT NOT NULL,
  CertId       INT NOT NULL,
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  OptionCount  INT NULL CONSTRAINT CK_Sections_OptionCount CHECK (OptionCount IS NULL OR OptionCount BETWEEN 2 AND 6),
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Sections_Skill_Code UNIQUE (SkillId, Code),
  -- composite FK pins the section's cert to its skill's cert (isolation, enforced because CertId is NOT NULL):
  CONSTRAINT FK_Sections_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- composite target so QuestionGroups can require exam-cert = section-cert:
  CONSTRAINT UQ_Sections_Id_Cert UNIQUE (SectionId, CertId)
);
GO
-- ============ Domain 1: Identity ============
CREATE TABLE Roles (
  RoleId INT IDENTITY(1,1) PRIMARY KEY,
  Code   VARCHAR(20) NOT NULL CONSTRAINT UQ_Roles_Code UNIQUE,
  Name   NVARCHAR(60) NOT NULL
);
GO
CREATE TABLE Users (
  UserId       INT IDENTITY(1,1) PRIMARY KEY,
  RoleId       INT NOT NULL CONSTRAINT FK_Users_Role REFERENCES Roles(RoleId),
  Email        VARCHAR(255) NOT NULL,
  PasswordHash VARBINARY(256) NOT NULL,
  ActiveCertId INT NULL CONSTRAINT FK_Users_ActiveCert REFERENCES Certificates(CertId),
  IsDeleted    BIT NOT NULL CONSTRAINT DF_Users_IsDeleted DEFAULT 0,
  CreatedAt    DATETIMEOFFSET NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSDATETIMEOFFSET()
);
GO
-- one account per live email (filtered so soft-deleted emails can be reused)
CREATE UNIQUE INDEX UX_Users_Email ON Users(Email) WHERE IsDeleted = 0;
GO
CREATE TABLE PasswordResetTokens (
  TokenId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId     INT NOT NULL CONSTRAINT FK_PasswordResetTokens_User REFERENCES Users(UserId),
  TokenHash  VARBINARY(256) NOT NULL,
  ExpiresAt  DATETIMEOFFSET NOT NULL,
  UsedAt     DATETIMEOFFSET NULL,
  CreatedAt  DATETIMEOFFSET NOT NULL CONSTRAINT DF_PasswordResetTokens_CreatedAt DEFAULT SYSDATETIMEOFFSET()
);
GO
-- ============ Domain 4: Scoring ============
CREATE TABLE ScoreBands (
  BandId       INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(60) NOT NULL,
  MinTotal     INT NOT NULL,
  MaxTotal     INT NOT NULL,
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT CK_ScoreBands_Range CHECK (MaxTotal >= MinTotal),
  CONSTRAINT UQ_ScoreBands_Cert_Code UNIQUE (CertId, Code)
);
GO
CREATE TABLE ScoreScales (
  ScaleId     INT IDENTITY(1,1) PRIMARY KEY,
  CertId      INT NOT NULL REFERENCES Certificates(CertId),
  SkillId     INT NULL REFERENCES Skills(SkillId),   -- NULL = total
  ExamId      INT NULL,                              -- NULL = default; FK added in Task 4
  RawScore    INT NOT NULL,
  ScaledScore INT NOT NULL
);
GO
-- ============ Domain 3: Content ============
-- (Sections already carries CertId + composite FK from Task 1)
CREATE TABLE Exams (
  ExamId          INT IDENTITY(1,1) PRIMARY KEY,
  CertId          INT NOT NULL REFERENCES Certificates(CertId),
  Name            NVARCHAR(160) NOT NULL,
  Status          VARCHAR(12) NOT NULL DEFAULT 'draft'
    CONSTRAINT CK_Exams_Status CHECK (Status IN ('draft','published','archived')),
  DurationMinutes INT NOT NULL CONSTRAINT CK_Exams_Duration CHECK (DurationMinutes > 0),
  IsDeleted       BIT NOT NULL DEFAULT 0,
  CreatedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT UQ_Exams_Id_Cert UNIQUE (ExamId, CertId)   -- composite target for isolation
);
GO
-- now that Exams exists, pin ScoreScales.ExamId
ALTER TABLE ScoreScales ADD CONSTRAINT FK_ScoreScales_Exam FOREIGN KEY (ExamId) REFERENCES Exams(ExamId);
GO
CREATE TABLE QuestionGroups (
  GroupId      INT IDENTITY(1,1) PRIMARY KEY,
  ExamId       INT NOT NULL,
  CertId       INT NOT NULL,
  SectionId    INT NOT NULL,
  Passage      NVARCHAR(MAX) NULL,
  AudioPath    NVARCHAR(400) NULL,
  DisplayOrder INT NOT NULL DEFAULT 0,
  -- both composite FKs share CertId, so exam-cert and section-cert must agree:
  CONSTRAINT FK_QGroups_ExamCert    FOREIGN KEY (ExamId, CertId)    REFERENCES Exams(ExamId, CertId),
  CONSTRAINT FK_QGroups_SectionCert FOREIGN KEY (SectionId, CertId) REFERENCES Sections(SectionId, CertId)
);
GO
CREATE TABLE Questions (
  QuestionId      INT IDENTITY(1,1) PRIMARY KEY,
  GroupId         INT NOT NULL REFERENCES QuestionGroups(GroupId),
  Stem            NVARCHAR(MAX) NOT NULL,
  DifficultyLevel INT NOT NULL CONSTRAINT CK_Questions_Difficulty CHECK (DifficultyLevel BETWEEN 1 AND 5),
  QuestionType    VARCHAR(14) NOT NULL
    CONSTRAINT CK_Questions_Type CHECK (QuestionType IN ('mcq','free_response')),
  Explanation     NVARCHAR(MAX) NULL,
  DisplayOrder    INT NOT NULL DEFAULT 0
);
GO
CREATE TABLE QuestionOptions (
  OptionId   INT IDENTITY(1,1) PRIMARY KEY,
  QuestionId INT NOT NULL REFERENCES Questions(QuestionId),
  Label      VARCHAR(4) NOT NULL,
  Text       NVARCHAR(MAX) NOT NULL,
  IsCorrect  BIT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Options_Question_Label UNIQUE (QuestionId, Label)
);
GO
CREATE TABLE ImportBatches (
  BatchId     INT IDENTITY(1,1) PRIMARY KEY,
  CertId      INT NOT NULL REFERENCES Certificates(CertId),
  SourceName  NVARCHAR(260) NOT NULL,
  Status      VARCHAR(10) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_ImportBatches_Status CHECK (Status IN ('pending','done','failed')),
  UploadedBy  INT NOT NULL REFERENCES Users(UserId),
  CreatedAt   DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CompletedAt DATETIMEOFFSET NULL
);
GO
CREATE TABLE ImportLog (
  LogId   INT IDENTITY(1,1) PRIMARY KEY,
  BatchId INT NOT NULL REFERENCES ImportBatches(BatchId),
  RowNo   INT NULL,
  Level   VARCHAR(5) NOT NULL CONSTRAINT CK_ImportLog_Level CHECK (Level IN ('info','warn','error')),
  Message NVARCHAR(1000) NOT NULL,
  LoggedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO

-- ============ Domain 6: Practice (free) ============
CREATE TABLE PracticeSessions (
  SessionId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId       INT NOT NULL REFERENCES Users(UserId),
  SectionId    INT NOT NULL REFERENCES Sections(SectionId),
  QuestionCount INT NOT NULL CONSTRAINT CK_Practice_Count CHECK (QuestionCount > 0),
  Status       VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Practice_Status CHECK (Status IN ('in_progress','finished','abandoned')),
  CorrectCount INT NOT NULL DEFAULT 0,
  StartedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  FinishedAt   DATETIMEOFFSET NULL
);
GO
CREATE TABLE PracticeAnswers (
  AnswerId         INT IDENTITY(1,1) PRIMARY KEY,
  SessionId        INT NOT NULL REFERENCES PracticeSessions(SessionId),
  QuestionId       INT NOT NULL REFERENCES Questions(QuestionId),
  SelectedOptionId INT NULL REFERENCES QuestionOptions(OptionId),
  IsCorrect        BIT NULL,
  DisplayOrder     INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_PracticeAnswers UNIQUE (SessionId, QuestionId)
);
GO

-- ============ Domain 7: MockExam ============
CREATE TABLE ExamAttempts (
  AttemptId       INT IDENTITY(1,1) PRIMARY KEY,
  UserId          INT NOT NULL REFERENCES Users(UserId),
  ExamId          INT NOT NULL REFERENCES Exams(ExamId),
  Status          VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Attempts_Status CHECK (Status IN ('in_progress','submitted','graded','abandoned')),
  StartedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  ExpiresAt       DATETIMEOFFSET NOT NULL,          -- server-authoritative
  IsAutoSubmitted BIT NOT NULL DEFAULT 0,
  TotalScore      INT NULL,
  BandCode        VARCHAR(20) NULL                  -- snapshot at grading
);
GO
CREATE TABLE AttemptSkillScores (
  AttemptId   INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  SkillId     INT NOT NULL REFERENCES Skills(SkillId),
  RawScore    INT NOT NULL,
  ScaledScore INT NOT NULL,
  CONSTRAINT PK_AttemptSkillScores PRIMARY KEY (AttemptId, SkillId)
);
GO
CREATE TABLE AttemptAnswers (
  AnswerId         INT IDENTITY(1,1) PRIMARY KEY,
  AttemptId        INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  QuestionId       INT NOT NULL REFERENCES Questions(QuestionId),
  SelectedOptionId INT NULL REFERENCES QuestionOptions(OptionId),
  IsCorrect        BIT NULL,
  CONSTRAINT UQ_AttemptAnswers UNIQUE (AttemptId, QuestionId)
);
GO
CREATE TABLE FreeResponses (
  FreeResponseId INT IDENTITY(1,1) PRIMARY KEY,
  AttemptId      INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  QuestionId     INT NOT NULL REFERENCES Questions(QuestionId),
  ResponseText   NVARCHAR(MAX) NULL,
  AudioPath      NVARCHAR(400) NULL,
  Status         VARCHAR(12) NOT NULL DEFAULT 'pending_ai'
    CONSTRAINT CK_FreeResp_Status CHECK (Status IN ('pending_ai','graded')),
  SubmittedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT UQ_FreeResponses UNIQUE (AttemptId, QuestionId),
  CONSTRAINT CK_FreeResp_HasContent CHECK (ResponseText IS NOT NULL OR AudioPath IS NOT NULL)
);
GO
-- attempts may only target published exams (carried from old schema)
CREATE TRIGGER trg_ExamAttempts_PublishedExamOnly ON ExamAttempts AFTER INSERT AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM inserted i JOIN Exams e ON e.ExamId=i.ExamId WHERE e.Status <> 'published')
  BEGIN
    THROW 50010, 'Attempt must target a published exam.', 1;
  END
END;
GO
-- Review Focus #2: TotalScore must equal SUM of scaled skill scores (when both present)
CREATE TRIGGER trg_Attempts_TotalMatchesSkills
ON AttemptSkillScores AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT a.AttemptId
    FROM ExamAttempts a
    WHERE a.TotalScore IS NOT NULL
      AND a.AttemptId IN (SELECT AttemptId FROM inserted UNION SELECT AttemptId FROM deleted)
      AND a.TotalScore <> (SELECT ISNULL(SUM(ScaledScore),0) FROM AttemptSkillScores s WHERE s.AttemptId=a.AttemptId)
  )
    THROW 50011, 'TotalScore must equal the sum of scaled skill scores.', 1;
END;
GO
-- same check when TotalScore itself changes
CREATE TRIGGER trg_Attempts_TotalMatchesSkills_OnAttempt
ON ExamAttempts AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF UPDATE(TotalScore) AND EXISTS (
    SELECT i.AttemptId FROM inserted i
    WHERE i.TotalScore IS NOT NULL
      AND EXISTS (SELECT 1 FROM AttemptSkillScores s WHERE s.AttemptId=i.AttemptId)
      AND i.TotalScore <> (SELECT ISNULL(SUM(ScaledScore),0) FROM AttemptSkillScores s WHERE s.AttemptId=i.AttemptId)
  )
    THROW 50011, 'TotalScore must equal the sum of scaled skill scores.', 1;
END;
GO
-- ============ Domain 8: AI Grading ============
CREATE TABLE GradingCriteria (
  CriterionId  INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  SkillId      INT NOT NULL REFERENCES Skills(SkillId),
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  MaxScore     DECIMAL(5,2) NOT NULL,
  Weight       DECIMAL(5,2) NOT NULL DEFAULT 1.0,
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Criteria_Skill_Code UNIQUE (SkillId, Code)
);
GO
CREATE TABLE AiGradings (
  GradingId      INT IDENTITY(1,1) PRIMARY KEY,
  FreeResponseId INT NOT NULL REFERENCES FreeResponses(FreeResponseId),
  SkillId        INT NOT NULL REFERENCES Skills(SkillId),
  Status         VARCHAR(8) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_AiGradings_Status CHECK (Status IN ('pending','done','failed')),
  OverallScaled  DECIMAL(6,2) NULL,
  BandCode       VARCHAR(20) NULL,             -- snapshot
  Model          NVARCHAR(80) NULL,
  RequestedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CompletedAt    DATETIMEOFFSET NULL,
  CONSTRAINT UQ_AiGradings_FreeResponse UNIQUE (FreeResponseId)
);
GO
CREATE TABLE AiGradingScores (
  GradingId   INT NOT NULL REFERENCES AiGradings(GradingId),
  CriterionId INT NOT NULL REFERENCES GradingCriteria(CriterionId),
  Score       DECIMAL(5,2) NOT NULL,
  Comment     NVARCHAR(MAX) NULL,
  CONSTRAINT PK_AiGradingScores PRIMARY KEY (GradingId, CriterionId)
);
GO
CREATE TABLE Recommendations (
  RecId          INT IDENTITY(1,1) PRIMARY KEY,
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  GradingId      INT NULL REFERENCES AiGradings(GradingId),   -- NULL = aggregate
  SkillId        INT NULL REFERENCES Skills(SkillId),
  Text           NVARCHAR(MAX) NOT NULL,
  Severity       VARCHAR(8) NOT NULL DEFAULT 'info'
    CONSTRAINT CK_Reco_Severity CHECK (Severity IN ('info','minor','major')),
  TargetBandCode VARCHAR(20) NULL,
  IsResolved     BIT NOT NULL DEFAULT 0,
  CreatedAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
-- ============ Domain 9: LearningPath ============
CREATE TABLE PlacementTests (
  PlacementId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  ExamId         INT NULL REFERENCES Exams(ExamId),
  Status         VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Placement_Status CHECK (Status IN ('in_progress','done','abandoned')),
  ResultBandCode VARCHAR(20) NULL,        -- snapshot
  TakenAt        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
CREATE TABLE TargetBands (
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  TargetBandCode VARCHAR(20) NOT NULL,
  SetAt          DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT PK_TargetBands PRIMARY KEY (UserId, CertId),
  CONSTRAINT FK_TargetBands_Band FOREIGN KEY (CertId, TargetBandCode) REFERENCES ScoreBands(CertId, Code)
);
GO
CREATE TABLE PathTemplates (
  TemplateId   INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  FromBandCode VARCHAR(20) NOT NULL,
  ToBandCode   VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  CONSTRAINT FK_PathTpl_FromBand FOREIGN KEY (CertId, FromBandCode) REFERENCES ScoreBands(CertId, Code),
  CONSTRAINT FK_PathTpl_ToBand   FOREIGN KEY (CertId, ToBandCode)   REFERENCES ScoreBands(CertId, Code)
);
GO
CREATE TABLE PathModules (
  ModuleId     INT IDENTITY(1,1) PRIMARY KEY,
  TemplateId   INT NOT NULL REFERENCES PathTemplates(TemplateId),
  SkillId      INT NOT NULL REFERENCES Skills(SkillId),
  DisplayOrder INT NOT NULL DEFAULT 0,
  Title        NVARCHAR(120) NOT NULL
);
GO
CREATE TABLE PathModuleItems (
  ItemId          INT IDENTITY(1,1) PRIMARY KEY,
  ModuleId        INT NOT NULL REFERENCES PathModules(ModuleId),
  ItemType        VARCHAR(16) NOT NULL
    CONSTRAINT CK_PMI_Type CHECK (ItemType IN ('practice_section','mock_exam','ai_task','mentor')),
  RefSectionId    INT NULL REFERENCES Sections(SectionId),
  RefExamId       INT NULL REFERENCES Exams(ExamId),
  DifficultyLevel INT NULL CONSTRAINT CK_PMI_Difficulty CHECK (DifficultyLevel IS NULL OR DifficultyLevel BETWEEN 1 AND 5),
  DisplayOrder    INT NOT NULL DEFAULT 0,
  -- Review Focus #3: the ref column must match ItemType
  CONSTRAINT CK_PMI_RefMatchesType CHECK (
       (ItemType='practice_section' AND RefSectionId IS NOT NULL AND RefExamId IS NULL)
    OR (ItemType='mock_exam'        AND RefExamId    IS NOT NULL AND RefSectionId IS NULL)
    OR (ItemType IN ('ai_task','mentor') AND RefSectionId IS NULL AND RefExamId IS NULL)
  )
);
GO
CREATE TABLE LearnerPaths (
  LearnerPathId   INT IDENTITY(1,1) PRIMARY KEY,
  UserId          INT NOT NULL REFERENCES Users(UserId),
  CertId          INT NOT NULL REFERENCES Certificates(CertId),
  TemplateId      INT NOT NULL REFERENCES PathTemplates(TemplateId),
  CurrentBandCode VARCHAR(20) NULL,
  Status          VARCHAR(10) NOT NULL DEFAULT 'active'
    CONSTRAINT CK_LearnerPaths_Status CHECK (Status IN ('active','completed','reset')),
  IsPremium       BIT NOT NULL DEFAULT 0,
  StartedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
-- Review Focus #5: at most one active path per (UserId,CertId)
CREATE UNIQUE INDEX UX_LearnerPaths_OneActive
  ON LearnerPaths(UserId, CertId) WHERE Status = 'active';
GO
CREATE TABLE LearnerPathSteps (
  StepId          INT IDENTITY(1,1) PRIMARY KEY,
  LearnerPathId   INT NOT NULL REFERENCES LearnerPaths(LearnerPathId),
  PathModuleItemId INT NOT NULL REFERENCES PathModuleItems(ItemId),
  Status          VARCHAR(10) NOT NULL DEFAULT 'locked'
    CONSTRAINT CK_Steps_Status CHECK (Status IN ('locked','available','done')),
  Score           DECIMAL(6,2) NULL,
  CompletedAt     DATETIMEOFFSET NULL
);
GO
-- ============ Domain 10: Mentor ============
CREATE TABLE MentorProfiles (
  MentorId     INT IDENTITY(1,1) PRIMARY KEY,
  UserId       INT NULL REFERENCES Users(UserId),   -- NULL for AI mentors
  DisplayName  NVARCHAR(120) NOT NULL,
  Bio          NVARCHAR(MAX) NULL,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  SkillId      INT NULL REFERENCES Skills(SkillId),
  Type         VARCHAR(6) NOT NULL CONSTRAINT CK_Mentor_Type CHECK (Type IN ('human','ai')),
  PricePerSlot DECIMAL(10,2) NOT NULL CONSTRAINT CK_Mentor_Price CHECK (PricePerSlot >= 0),
  IsActive     BIT NOT NULL DEFAULT 1
);
GO
CREATE TABLE MentorSlots (
  SlotId   INT IDENTITY(1,1) PRIMARY KEY,
  MentorId INT NOT NULL REFERENCES MentorProfiles(MentorId),
  StartAt  DATETIMEOFFSET NOT NULL,
  EndAt    DATETIMEOFFSET NOT NULL,
  Status   VARCHAR(6) NOT NULL DEFAULT 'open'
    CONSTRAINT CK_Slots_Status CHECK (Status IN ('open','booked','closed')),
  CONSTRAINT CK_Slots_Range CHECK (EndAt > StartAt)
);
GO
CREATE TABLE Bookings (
  BookingId INT IDENTITY(1,1) PRIMARY KEY,
  UserId    INT NOT NULL REFERENCES Users(UserId),
  MentorId  INT NOT NULL REFERENCES MentorProfiles(MentorId),
  SlotId    INT NULL REFERENCES MentorSlots(SlotId),   -- NULL for AI
  Status    VARCHAR(10) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_Bookings_Status CHECK (Status IN ('pending','confirmed','done','cancelled')),
  MeetLink  NVARCHAR(400) NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
-- Review Focus #5: one live booking per slot (cancelled frees it)
CREATE UNIQUE INDEX UX_Bookings_OnePerSlot
  ON Bookings(SlotId) WHERE SlotId IS NOT NULL AND Status <> 'cancelled';
GO
CREATE TABLE Reviews (
  ReviewId  INT IDENTITY(1,1) PRIMARY KEY,
  BookingId INT NOT NULL REFERENCES Bookings(BookingId),
  UserId    INT NOT NULL REFERENCES Users(UserId),
  Rating    INT NOT NULL CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5),
  Text      NVARCHAR(MAX) NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT UQ_Reviews_OnePerBooking UNIQUE (BookingId)
);
GO
CREATE TABLE Complaints (
  ComplaintId          INT IDENTITY(1,1) PRIMARY KEY,
  BookingId            INT NOT NULL REFERENCES Bookings(BookingId),
  UserId               INT NOT NULL REFERENCES Users(UserId),
  Reason               NVARCHAR(MAX) NOT NULL,
  Status               VARCHAR(10) NOT NULL DEFAULT 'open'
    CONSTRAINT CK_Complaints_Status CHECK (Status IN ('open','reviewing','resolved')),
  Resolution           NVARCHAR(MAX) NULL,
  ResolvedByGrantTxnId INT NULL,      -- FK added in Task 10
  CreatedAt            DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
CREATE TABLE MentorPayouts (
  PayoutId     INT IDENTITY(1,1) PRIMARY KEY,
  MentorId     INT NOT NULL REFERENCES MentorProfiles(MentorId),
  PeriodStart  DATETIMEOFFSET NOT NULL,
  PeriodEnd    DATETIMEOFFSET NOT NULL,
  BookingCount INT NOT NULL DEFAULT 0,
  GrossAmount  DECIMAL(12,2) NOT NULL DEFAULT 0,
  Status       VARCHAR(7) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_Payouts_Status CHECK (Status IN ('pending','paid')),
  PaidAt       DATETIMEOFFSET NULL,
  CONSTRAINT CK_Payouts_Range CHECK (PeriodEnd > PeriodStart)
);
GO
CREATE TABLE MentorPayoutItems (
  PayoutId  INT NOT NULL REFERENCES MentorPayouts(PayoutId),
  BookingId INT NOT NULL REFERENCES Bookings(BookingId),
  CONSTRAINT PK_PayoutItems PRIMARY KEY (PayoutId, BookingId),
  CONSTRAINT UQ_PayoutItems_Booking UNIQUE (BookingId)   -- a booking settles once
);
GO

-- ============ Domain 5: Payment ============
CREATE TABLE Packages (
  PackageId INT IDENTITY(1,1) PRIMARY KEY,
  Name      NVARCHAR(120) NOT NULL,
  Credits   INT NOT NULL CONSTRAINT CK_Packages_Credits CHECK (Credits > 0),
  Price     DECIMAL(12,2) NOT NULL CONSTRAINT CK_Packages_Price CHECK (Price >= 0),
  IsActive  BIT NOT NULL DEFAULT 1
);
GO
CREATE TABLE Orders (
  OrderId   INT IDENTITY(1,1) PRIMARY KEY,
  UserId    INT NOT NULL REFERENCES Users(UserId),
  PackageId INT NOT NULL REFERENCES Packages(PackageId),
  Status    VARCHAR(8) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_Orders_Status CHECK (Status IN ('pending','paid','failed','expired')),
  Amount    DECIMAL(12,2) NOT NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  PaidAt    DATETIMEOFFSET NULL
);
GO
-- Review Focus #5: one pending order per user
CREATE UNIQUE INDEX UX_Orders_OnePendingPerUser
  ON Orders(UserId) WHERE Status = 'pending';
GO
CREATE TABLE CreditTransactions (
  TxnId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId   INT NOT NULL REFERENCES Users(UserId),
  Reason   VARCHAR(16) NOT NULL
    CONSTRAINT CK_CreditTxn_Reason CHECK (Reason IN
      ('purchase','admin_grant','mock_exam_start','ai_grading','mentor_booking','premium_path','admin_revoke')),
  Delta    INT NOT NULL CONSTRAINT CK_CreditTxn_NonZero CHECK (Delta <> 0),
  OrderId       INT NULL REFERENCES Orders(OrderId),
  AttemptId     INT NULL REFERENCES ExamAttempts(AttemptId),
  BookingId     INT NULL REFERENCES Bookings(BookingId),
  LearnerPathId INT NULL REFERENCES LearnerPaths(LearnerPathId),
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- sign per reason
  CONSTRAINT CK_CreditTxn_DeltaSign CHECK (
       (Reason IN ('purchase','admin_grant') AND Delta > 0)
    OR (Reason IN ('mock_exam_start','ai_grading','mentor_booking','premium_path','admin_revoke') AND Delta < 0)
  ),
  -- exactly the source column matching the reason is set (admin_* carry none)
  CONSTRAINT CK_CreditTxn_Source CHECK (
       (Reason='purchase'        AND OrderId IS NOT NULL AND AttemptId IS NULL AND BookingId IS NULL AND LearnerPathId IS NULL)
    OR (Reason='mock_exam_start' AND AttemptId IS NOT NULL AND OrderId IS NULL AND BookingId IS NULL AND LearnerPathId IS NULL)
    OR (Reason='ai_grading'      AND AttemptId IS NOT NULL AND OrderId IS NULL AND BookingId IS NULL AND LearnerPathId IS NULL)
    OR (Reason='mentor_booking'  AND BookingId IS NOT NULL AND OrderId IS NULL AND AttemptId IS NULL AND LearnerPathId IS NULL)
    OR (Reason='premium_path'    AND LearnerPathId IS NOT NULL AND OrderId IS NULL AND AttemptId IS NULL AND BookingId IS NULL)
    OR (Reason IN ('admin_grant','admin_revoke') AND OrderId IS NULL AND AttemptId IS NULL AND BookingId IS NULL AND LearnerPathId IS NULL)
  )
);
GO
CREATE VIEW vw_UserCreditBalance AS
  SELECT UserId, SUM(Delta) AS Balance
  FROM CreditTransactions GROUP BY UserId;
GO
-- Review Focus #4: no debit may drive the running balance below zero
CREATE TRIGGER trg_CreditTransactions_NoNegativeBalance
ON CreditTransactions AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM (SELECT DISTINCT UserId FROM inserted) u
    WHERE (SELECT SUM(Delta) FROM CreditTransactions t WHERE t.UserId=u.UserId) < 0
  )
    THROW 50020, 'Transaction would drive wallet balance negative.', 1;
END;
GO
-- backfill the complaint make-good FK now that CreditTransactions exists
ALTER TABLE Complaints
  ADD CONSTRAINT FK_Complaints_GrantTxn FOREIGN KEY (ResolvedByGrantTxnId) REFERENCES CreditTransactions(TxnId);
GO
-- expire stale pending orders (carried from old schema; frees UX_Orders_OnePendingPerUser)
CREATE PROCEDURE usp_ExpireStalePendingOrders @OlderThanMinutes INT = 15 AS
BEGIN
  SET NOCOUNT ON;
  UPDATE Orders SET Status='expired'
  WHERE Status='pending'
    AND CreatedAt < DATEADD(minute, -@OlderThanMinutes, SYSDATETIMEOFFSET());
END;
GO
-- ============ Views ============
CREATE VIEW vw_UserSectionAccuracy AS
  SELECT ps.UserId, s.SectionId, s.Name AS SectionName,
         COUNT(*) AS Answered,
         SUM(CASE WHEN pa.IsCorrect=1 THEN 1 ELSE 0 END) AS Correct
  FROM PracticeAnswers pa
  JOIN PracticeSessions ps ON ps.SessionId = pa.SessionId
  JOIN Sections s ON s.SectionId = ps.SectionId
  GROUP BY ps.UserId, s.SectionId, s.Name;
GO
