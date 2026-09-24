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
