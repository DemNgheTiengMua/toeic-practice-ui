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
  CONSTRAINT UQ_Skills_Id_Cert UNIQUE (SkillId, CertId),
  -- M-18: composite target so Sections can carry Modality down from its skill
  CONSTRAINT UQ_Skills_Id_Modality UNIQUE (SkillId, Modality)
);
GO
CREATE TABLE Sections (
  SectionId    INT IDENTITY(1,1) PRIMARY KEY,
  SkillId      INT NOT NULL,
  CertId       INT NOT NULL,
  -- M-18: carried down from the owning skill so PracticeSessions can pin listening/reading-only
  Modality     VARCHAR(10) NOT NULL
    CONSTRAINT CK_Sections_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  OptionCount  INT NULL CONSTRAINT CK_Sections_OptionCount CHECK (OptionCount IS NULL OR OptionCount BETWEEN 2 AND 6),
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Sections_Skill_Code UNIQUE (SkillId, Code),
  -- composite FK pins the section's cert to its skill's cert (isolation, enforced because CertId is NOT NULL):
  CONSTRAINT FK_Sections_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- M-18: Modality must be the skill's own (pins the literal via the FK)
  CONSTRAINT FK_Sections_SkillModality FOREIGN KEY (SkillId, Modality) REFERENCES Skills(SkillId, Modality),
  -- composite target so QuestionGroups can require exam-cert = section-cert:
  CONSTRAINT UQ_Sections_Id_Cert UNIQUE (SectionId, CertId),
  -- M-18: composite target so PracticeSessions can carry Modality down
  CONSTRAINT UQ_Sections_Id_Modality UNIQUE (SectionId, Modality),
  -- M-3: composite target so Questions can carry SkillId down (pins AI grading to the right skill)
  CONSTRAINT UQ_Sections_Id_Skill UNIQUE (SectionId, SkillId)
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
-- M-10: no two bands within the same certificate may have overlapping [MinTotal,MaxTotal] ranges
-- (a CHECK can't compare across rows/tables, so this is a trigger).
CREATE TRIGGER trg_ScoreBands_NoOverlap
ON ScoreBands AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN ScoreBands b
      ON b.CertId = i.CertId
     AND b.BandId <> i.BandId
     AND i.MinTotal <= b.MaxTotal
     AND i.MaxTotal >= b.MinTotal
  )
    THROW 50027, 'ScoreBands ranges may not overlap within a certificate.', 1;
END;
GO
CREATE TABLE ScoreScales (
  ScaleId     INT IDENTITY(1,1) PRIMARY KEY,
  CertId      INT NOT NULL REFERENCES Certificates(CertId),
  SkillId     INT NULL,   -- NULL = total
  ExamId      INT NULL,   -- NULL = default; FK added below
  RawScore    INT NOT NULL,
  ScaledScore INT NOT NULL,
  -- M-11: a (cert, skill, exam, raw score) mapping must be unambiguous
  CONSTRAINT UQ_ScoreScales_Cert_Skill_Exam_Raw UNIQUE (CertId, SkillId, ExamId, RawScore),
  -- M-11: the skill must belong to this same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_ScoreScales_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId)
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
-- M-11: composite FK so the exam must belong to the same certificate (replaces the single-column FK)
ALTER TABLE ScoreScales ADD CONSTRAINT FK_ScoreScales_Exam FOREIGN KEY (ExamId, CertId) REFERENCES Exams(ExamId, CertId);
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
  CONSTRAINT FK_QGroups_SectionCert FOREIGN KEY (SectionId, CertId) REFERENCES Sections(SectionId, CertId),
  -- composite targets so Questions can carry ExamId/SectionId down from its group (H-5 / Low):
  CONSTRAINT UQ_QGroups_Id_Exam    UNIQUE (GroupId, ExamId),
  CONSTRAINT UQ_QGroups_Id_Section UNIQUE (GroupId, SectionId)
);
GO
CREATE TABLE Questions (
  QuestionId      INT IDENTITY(1,1) PRIMARY KEY,
  GroupId         INT NOT NULL REFERENCES QuestionGroups(GroupId),
  -- H-5 / Low: carried down from the owning QuestionGroups row so attempts/answers can pin isolation
  ExamId          INT NOT NULL,
  SectionId       INT NOT NULL,
  -- M-3: carried down from the section so AI grading can be pinned to the question's own skill
  SkillId         INT NOT NULL,
  Stem            NVARCHAR(MAX) NOT NULL,
  DifficultyLevel INT NOT NULL CONSTRAINT CK_Questions_Difficulty CHECK (DifficultyLevel BETWEEN 1 AND 5),
  QuestionType    VARCHAR(14) NOT NULL
    CONSTRAINT CK_Questions_Type CHECK (QuestionType IN ('mcq','free_response')),
  Explanation     NVARCHAR(MAX) NULL,
  DisplayOrder    INT NOT NULL DEFAULT 0,
  -- ExamId/SectionId must be the group's own (composite FK pins them, not free-standing values)
  CONSTRAINT FK_Questions_GroupExam    FOREIGN KEY (GroupId, ExamId)    REFERENCES QuestionGroups(GroupId, ExamId),
  CONSTRAINT FK_Questions_GroupSection FOREIGN KEY (GroupId, SectionId) REFERENCES QuestionGroups(GroupId, SectionId),
  -- M-3: SkillId must be the section's own
  CONSTRAINT FK_Questions_SectionSkill FOREIGN KEY (SectionId, SkillId) REFERENCES Sections(SectionId, SkillId),
  -- composite targets for H-5 (attempt containment) and M-7 (MCQ/free-response branch)
  CONSTRAINT UQ_Questions_Id_Exam    UNIQUE (QuestionId, ExamId),
  CONSTRAINT UQ_Questions_Id_Section UNIQUE (QuestionId, SectionId),
  CONSTRAINT UQ_Questions_Id_Type    UNIQUE (QuestionId, QuestionType),
  -- M-3: composite target so FreeResponses can carry SkillId down
  CONSTRAINT UQ_Questions_Id_Skill   UNIQUE (QuestionId, SkillId)
);
GO
CREATE TABLE QuestionOptions (
  OptionId     INT IDENTITY(1,1) PRIMARY KEY,
  QuestionId   INT NOT NULL REFERENCES Questions(QuestionId),
  -- M-7: options only belong to MCQ questions
  QuestionType VARCHAR(14) NOT NULL
    CONSTRAINT CK_Options_QuestionType CHECK (QuestionType = 'mcq'),
  Label        VARCHAR(4) NOT NULL,
  Text         NVARCHAR(MAX) NOT NULL,
  IsCorrect    BIT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Options_Question_Label UNIQUE (QuestionId, Label),
  -- H-6: composite target so answer tables can pin the selected option to its own question
  CONSTRAINT UQ_Options_Id_Question UNIQUE (OptionId, QuestionId),
  -- M-7: QuestionType must match the question's own (pins the literal via the FK)
  CONSTRAINT FK_Options_QuestionType FOREIGN KEY (QuestionId, QuestionType) REFERENCES Questions(QuestionId, QuestionType)
  -- M-8: at most one correct option per question enforced below via UX_Options_OneCorrect
);
GO
CREATE UNIQUE INDEX UX_Options_OneCorrect ON QuestionOptions(QuestionId) WHERE IsCorrect = 1;
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
  -- M-18: carried down from the section so free practice can be pinned to listening/reading only
  Modality     VARCHAR(10) NOT NULL
    CONSTRAINT CK_Practice_Modality CHECK (Modality IN ('listening','reading')),
  QuestionCount INT NOT NULL CONSTRAINT CK_Practice_Count CHECK (QuestionCount > 0),
  Status       VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Practice_Status CHECK (Status IN ('in_progress','finished','abandoned')),
  CorrectCount INT NOT NULL DEFAULT 0,
  StartedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  FinishedAt   DATETIMEOFFSET NULL,
  -- M-18: Modality must be the section's own (pins the literal via the FK) and excludes speaking/writing
  CONSTRAINT FK_Practice_SectionModality FOREIGN KEY (SectionId, Modality) REFERENCES Sections(SectionId, Modality),
  -- Low: composite target so PracticeAnswers can require QuestionId's SectionId = the session's own
  CONSTRAINT UQ_PracticeSessions_Id_Section UNIQUE (SessionId, SectionId)
);
GO
CREATE TABLE PracticeAnswers (
  AnswerId         INT IDENTITY(1,1) PRIMARY KEY,
  SessionId        INT NOT NULL REFERENCES PracticeSessions(SessionId),
  QuestionId       INT NOT NULL REFERENCES Questions(QuestionId),
  -- Low: carried down so a question must belong to the session's own section
  SectionId        INT NOT NULL,
  SelectedOptionId INT NULL,
  IsCorrect        BIT NULL,
  DisplayOrder     INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_PracticeAnswers UNIQUE (SessionId, QuestionId),
  -- Low: SectionId must be the session's own
  CONSTRAINT FK_PracticeAnswers_SessionSection FOREIGN KEY (SessionId, SectionId) REFERENCES PracticeSessions(SessionId, SectionId),
  -- Low: QuestionId must belong to the same section (containment)
  CONSTRAINT FK_PracticeAnswers_QuestionSection FOREIGN KEY (QuestionId, SectionId) REFERENCES Questions(QuestionId, SectionId),
  -- H-6: SelectedOptionId must belong to this same question (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_PracticeAnswers_OptionQuestion FOREIGN KEY (SelectedOptionId, QuestionId) REFERENCES QuestionOptions(OptionId, QuestionId)
);
GO

-- ============ Domain 7: MockExam ============
CREATE TABLE ExamAttempts (
  AttemptId       INT IDENTITY(1,1) PRIMARY KEY,
  UserId          INT NOT NULL REFERENCES Users(UserId),
  ExamId          INT NOT NULL REFERENCES Exams(ExamId),
  -- H-5: carried down from the exam so children can be pinned to the same certificate
  CertId          INT NOT NULL REFERENCES Certificates(CertId),
  Status          VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Attempts_Status CHECK (Status IN ('in_progress','submitted','graded','abandoned')),
  StartedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  ExpiresAt       DATETIMEOFFSET NOT NULL,          -- server-authoritative
  IsAutoSubmitted BIT NOT NULL DEFAULT 0,
  TotalScore      INT NULL,
  BandCode        VARCHAR(20) NULL,                 -- snapshot at grading
  -- H-5: ExamId's CertId must be the attempt's own (pins the carried-down value)
  CONSTRAINT FK_Attempts_ExamCert FOREIGN KEY (ExamId, CertId) REFERENCES Exams(ExamId, CertId),
  -- composite target so CreditTransactions can prove the charged user owns the attempt (H-1):
  CONSTRAINT UQ_Attempts_Id_User UNIQUE (AttemptId, UserId),
  -- H-5: composite targets so AttemptAnswers/FreeResponses/AttemptSkillScores can pin containment
  CONSTRAINT UQ_Attempts_Id_Exam UNIQUE (AttemptId, ExamId),
  CONSTRAINT UQ_Attempts_Id_Cert UNIQUE (AttemptId, CertId)
);
GO
CREATE TABLE AttemptSkillScores (
  AttemptId   INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  SkillId     INT NOT NULL REFERENCES Skills(SkillId),
  -- H-5: carried down from the attempt so the skill must belong to the attempt's own certificate
  CertId      INT NOT NULL,
  RawScore    INT NOT NULL,
  ScaledScore INT NOT NULL,
  CONSTRAINT PK_AttemptSkillScores PRIMARY KEY (AttemptId, SkillId),
  -- H-5: the attempt's own CertId must be used
  CONSTRAINT FK_ASS_AttemptCert FOREIGN KEY (AttemptId, CertId) REFERENCES ExamAttempts(AttemptId, CertId),
  -- H-5: the skill must belong to that same certificate (target exists: Skills(SkillId,CertId))
  CONSTRAINT FK_ASS_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId)
);
GO
CREATE TABLE AttemptAnswers (
  AnswerId         INT IDENTITY(1,1) PRIMARY KEY,
  AttemptId        INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  QuestionId       INT NOT NULL REFERENCES Questions(QuestionId),
  -- H-5: carried down from the attempt so the question must belong to the attempt's own exam
  ExamId           INT NOT NULL,
  SelectedOptionId INT NULL,
  IsCorrect        BIT NULL,
  CONSTRAINT UQ_AttemptAnswers UNIQUE (AttemptId, QuestionId),
  -- H-5: ExamId must be the attempt's own
  CONSTRAINT FK_AttemptAnswers_AttemptExam FOREIGN KEY (AttemptId, ExamId) REFERENCES ExamAttempts(AttemptId, ExamId),
  -- H-5: the question must belong to that same exam
  CONSTRAINT FK_AttemptAnswers_QuestionExam FOREIGN KEY (QuestionId, ExamId) REFERENCES Questions(QuestionId, ExamId),
  -- H-6: SelectedOptionId must belong to this same question (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_AttemptAnswers_OptionQuestion FOREIGN KEY (SelectedOptionId, QuestionId) REFERENCES QuestionOptions(OptionId, QuestionId)
);
GO
CREATE TABLE FreeResponses (
  FreeResponseId INT IDENTITY(1,1) PRIMARY KEY,
  AttemptId      INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  QuestionId     INT NOT NULL REFERENCES Questions(QuestionId),
  -- H-5: carried down from the attempt so the question must belong to the attempt's own exam
  ExamId         INT NOT NULL,
  -- M-3: carried down from the question so AiGradings must name that question's own skill
  SkillId        INT NOT NULL,
  -- M-7: pins the question to be a free_response question (a CHECK can't reach Questions)
  QuestionType   VARCHAR(14) NOT NULL
    CONSTRAINT CK_FreeResp_QuestionType CHECK (QuestionType = 'free_response'),
  ResponseText   NVARCHAR(MAX) NULL,
  AudioPath      NVARCHAR(400) NULL,
  Status         VARCHAR(12) NOT NULL DEFAULT 'pending_ai'
    CONSTRAINT CK_FreeResp_Status CHECK (Status IN ('pending_ai','graded')),
  SubmittedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT UQ_FreeResponses UNIQUE (AttemptId, QuestionId),
  CONSTRAINT CK_FreeResp_HasContent CHECK (ResponseText IS NOT NULL OR AudioPath IS NOT NULL),
  -- H-5: ExamId must be the attempt's own
  CONSTRAINT FK_FreeResp_AttemptExam FOREIGN KEY (AttemptId, ExamId) REFERENCES ExamAttempts(AttemptId, ExamId),
  -- H-5: the question must belong to that same exam
  CONSTRAINT FK_FreeResp_QuestionExam FOREIGN KEY (QuestionId, ExamId) REFERENCES Questions(QuestionId, ExamId),
  -- M-7: QuestionType must be the question's own (pins the literal via the FK)
  CONSTRAINT FK_FreeResp_QuestionType FOREIGN KEY (QuestionId, QuestionType) REFERENCES Questions(QuestionId, QuestionType),
  -- M-3: SkillId must be the question's own
  CONSTRAINT FK_FreeResp_QuestionSkill FOREIGN KEY (QuestionId, SkillId) REFERENCES Questions(QuestionId, SkillId),
  -- M-3: composite target so AiGradings can require its SkillId to be this response's own
  CONSTRAINT UQ_FreeResp_Id_Skill UNIQUE (FreeResponseId, SkillId)
);
GO
-- attempts may only target published exams (carried from old schema)
-- M-5: also cover UPDATE so an attempt can't be moved onto a draft exam after creation
CREATE TRIGGER trg_ExamAttempts_PublishedExamOnly ON ExamAttempts AFTER INSERT, UPDATE AS
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
-- M-6: an attempt cannot reach 'graded' while any of its free responses is still 'pending_ai'
CREATE TRIGGER trg_Attempts_NoGradedWithPendingFreeResponses
ON ExamAttempts AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    WHERE i.Status = 'graded'
      AND EXISTS (SELECT 1 FROM FreeResponses fr WHERE fr.AttemptId = i.AttemptId AND fr.Status <> 'graded')
  )
    THROW 50025, 'Attempt cannot be graded while a free response is still pending_ai.', 1;
END;
GO
-- R4: the guard above only watches the ATTEMPT side. The same invariant breaks from the child
-- side: a pending_ai free response inserted into an already-'graded' attempt leaves the attempt
-- graded with ungraded work inside it. Close it on the FreeResponses side too.
CREATE TRIGGER trg_FreeResponses_NotIntoGradedAttempt
ON FreeResponses AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN ExamAttempts a ON a.AttemptId = i.AttemptId
    WHERE a.Status = 'graded' AND i.Status <> 'graded'
  )
    THROW 50030, 'A free response that is not graded cannot belong to a graded attempt.', 1;
END;
GO
-- ============ Domain 8: AI Grading ============
CREATE TABLE GradingCriteria (
  CriterionId  INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  SkillId      INT NOT NULL,
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  MaxScore     DECIMAL(5,2) NOT NULL CONSTRAINT CK_Criteria_MaxScore CHECK (MaxScore > 0),
  Weight       DECIMAL(5,2) NOT NULL DEFAULT 1.0,
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Criteria_Skill_Code UNIQUE (SkillId, Code),
  -- M-3: the criterion's skill must belong to the criterion's own certificate
  CONSTRAINT FK_GradingCriteria_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- composite target so AiGradingScores can carry SkillId down from its criterion
  CONSTRAINT UQ_GradingCriteria_Id_Skill UNIQUE (CriterionId, SkillId)
);
GO
CREATE TABLE AiGradings (
  GradingId      INT IDENTITY(1,1) PRIMARY KEY,
  FreeResponseId INT NOT NULL REFERENCES FreeResponses(FreeResponseId),
  -- M-3: no longer a bare Skills FK — it must be the graded response's OWN skill (see FK below)
  SkillId        INT NOT NULL,
  Status         VARCHAR(8) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_AiGradings_Status CHECK (Status IN ('pending','done','failed')),
  OverallScaled  DECIMAL(6,2) NULL,
  BandCode       VARCHAR(20) NULL,             -- snapshot
  Model          NVARCHAR(80) NULL,
  RequestedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CompletedAt    DATETIMEOFFSET NULL,
  CONSTRAINT UQ_AiGradings_FreeResponse UNIQUE (FreeResponseId),
  -- composite target so AiGradingScores can require the criterion's skill = the grading's own skill
  CONSTRAINT UQ_AiGradings_Id_Skill UNIQUE (GradingId, SkillId),
  -- M-3 clause 3: the graded skill must be the free response's own skill, which the chain
  -- FreeResponses → Questions → Sections pins to a single certificate
  CONSTRAINT FK_AiGradings_ResponseSkill FOREIGN KEY (FreeResponseId, SkillId) REFERENCES FreeResponses(FreeResponseId, SkillId)
);
GO
-- M-6: flip FreeResponses.Status to 'graded' once its AiGradings.Status reaches 'done'
CREATE TRIGGER trg_AiGradings_MarkFreeResponseGraded
ON AiGradings AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  UPDATE fr
    SET Status = 'graded'
  FROM FreeResponses fr
  JOIN inserted i ON i.FreeResponseId = fr.FreeResponseId
  WHERE i.Status = 'done' AND fr.Status <> 'graded';
END;
GO
CREATE TABLE AiGradingScores (
  GradingId   INT NOT NULL,
  CriterionId INT NOT NULL,
  -- M-3: carried down from the grading so the criterion's own skill must match
  SkillId     INT NOT NULL,
  Score       DECIMAL(5,2) NOT NULL CONSTRAINT CK_AGS_ScoreNonNegative CHECK (Score >= 0),
  Comment     NVARCHAR(MAX) NULL,
  CONSTRAINT PK_AiGradingScores PRIMARY KEY (GradingId, CriterionId),
  -- M-3: SkillId must be the grading's own
  CONSTRAINT FK_AGS_GradingSkill FOREIGN KEY (GradingId, SkillId) REFERENCES AiGradings(GradingId, SkillId),
  -- M-3: the criterion's own skill must be that same skill (pins skill AND cert transitively)
  CONSTRAINT FK_AGS_CriterionSkill FOREIGN KEY (CriterionId, SkillId) REFERENCES GradingCriteria(CriterionId, SkillId)
);
GO
-- M-9: a rubric score cannot exceed its criterion's MaxScore (a CHECK can't reach another table)
CREATE TRIGGER trg_AiGradingScores_BoundedByMaxScore
ON AiGradingScores AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN GradingCriteria c ON c.CriterionId = i.CriterionId
    WHERE i.Score > c.MaxScore
  )
    THROW 50026, 'AiGradingScores.Score cannot exceed the criterion MaxScore.', 1;
END;
GO
-- R7: the bound is only checked when a SCORE is written. Lowering a criterion's MaxScore
-- afterwards left stored scores above their own ceiling, so OverallScaled and the band snapshot
-- derived from it become unbounded again by editing the rubric instead of the score.
CREATE TRIGGER trg_GradingCriteria_MaxScoreNotBelowStored
ON GradingCriteria AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN AiGradingScores s ON s.CriterionId = i.CriterionId
    WHERE s.Score > i.MaxScore
  )
    THROW 50031, 'MaxScore cannot be lowered below a score already recorded against this criterion.', 1;
END;
GO
CREATE TABLE Recommendations (
  RecId          INT IDENTITY(1,1) PRIMARY KEY,
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  GradingId      INT NULL REFERENCES AiGradings(GradingId),   -- NULL = aggregate
  SkillId        INT NULL,
  Text           NVARCHAR(MAX) NOT NULL,
  Severity       VARCHAR(8) NOT NULL DEFAULT 'info'
    CONSTRAINT CK_Reco_Severity CHECK (Severity IN ('info','minor','major')),
  TargetBandCode VARCHAR(20) NULL,
  IsResolved     BIT NOT NULL DEFAULT 0,
  CreatedAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- M-12: the skill must belong to this same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_Reco_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- M-12: the target band must be a real band of this same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_Reco_TargetBand FOREIGN KEY (CertId, TargetBandCode) REFERENCES ScoreBands(CertId, Code)
);
GO
-- ============ Domain 9: LearningPath ============
CREATE TABLE PlacementTests (
  PlacementId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  ExamId         INT NULL,
  Status         VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Placement_Status CHECK (Status IN ('in_progress','done','abandoned')),
  ResultBandCode VARCHAR(20) NULL,        -- snapshot
  TakenAt        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- M-2: the placement exam must belong to the same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_Placement_ExamCert FOREIGN KEY (ExamId, CertId) REFERENCES Exams(ExamId, CertId)
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
  CONSTRAINT FK_PathTpl_ToBand   FOREIGN KEY (CertId, ToBandCode)   REFERENCES ScoreBands(CertId, Code),
  -- M-1: composite target so LearnerPaths can pin CertId to the template's own certificate
  CONSTRAINT UQ_PathTemplates_Id_Cert UNIQUE (TemplateId, CertId)
);
GO
CREATE TABLE PathModules (
  ModuleId     INT IDENTITY(1,1) PRIMARY KEY,
  TemplateId   INT NOT NULL,
  SkillId      INT NOT NULL,
  -- M-1: carried down from the owning template so the skill must belong to the same certificate
  CertId       INT NOT NULL,
  DisplayOrder INT NOT NULL DEFAULT 0,
  Title        NVARCHAR(120) NOT NULL,
  -- M-1: CertId must be the template's own
  CONSTRAINT FK_PathModules_TemplateCert FOREIGN KEY (TemplateId, CertId) REFERENCES PathTemplates(TemplateId, CertId),
  -- M-1: the skill must belong to that same certificate
  CONSTRAINT FK_PathModules_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- composite target so PathModuleItems can carry CertId down from its module
  CONSTRAINT UQ_PathModules_Id_Cert UNIQUE (ModuleId, CertId),
  -- M-1: composite target so PathModuleItems/LearnerPathSteps can carry TemplateId down from this module
  CONSTRAINT UQ_PathModules_Id_Template UNIQUE (ModuleId, TemplateId)
);
GO
CREATE TABLE PathModuleItems (
  ItemId          INT IDENTITY(1,1) PRIMARY KEY,
  ModuleId        INT NOT NULL,
  -- M-1: carried down from the owning module so refs must belong to the same certificate
  CertId          INT NOT NULL,
  -- M-1: carried down from the owning module so LearnerPathSteps can pin the item to its own template
  TemplateId      INT NOT NULL,
  ItemType        VARCHAR(16) NOT NULL
    CONSTRAINT CK_PMI_Type CHECK (ItemType IN ('practice_section','mock_exam','ai_task','mentor')),
  RefSectionId    INT NULL,
  RefExamId       INT NULL,
  DifficultyLevel INT NULL CONSTRAINT CK_PMI_Difficulty CHECK (DifficultyLevel IS NULL OR DifficultyLevel BETWEEN 1 AND 5),
  DisplayOrder    INT NOT NULL DEFAULT 0,
  -- Review Focus #3: the ref column must match ItemType
  CONSTRAINT CK_PMI_RefMatchesType CHECK (
       (ItemType='practice_section' AND RefSectionId IS NOT NULL AND RefExamId IS NULL)
    OR (ItemType='mock_exam'        AND RefExamId    IS NOT NULL AND RefSectionId IS NULL)
    OR (ItemType IN ('ai_task','mentor') AND RefSectionId IS NULL AND RefExamId IS NULL)
  ),
  -- M-1: CertId must be the module's own
  CONSTRAINT FK_PMI_ModuleCert FOREIGN KEY (ModuleId, CertId) REFERENCES PathModules(ModuleId, CertId),
  -- M-1: TemplateId must be the module's own
  CONSTRAINT FK_PMI_ModuleTemplate FOREIGN KEY (ModuleId, TemplateId) REFERENCES PathModules(ModuleId, TemplateId),
  -- M-1: a practice_section ref must be a section from that same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_PMI_SectionCert FOREIGN KEY (RefSectionId, CertId) REFERENCES Sections(SectionId, CertId),
  -- M-1: a mock_exam ref must be an exam from that same certificate (NULL stays legal, MATCH SIMPLE)
  CONSTRAINT FK_PMI_ExamCert FOREIGN KEY (RefExamId, CertId) REFERENCES Exams(ExamId, CertId),
  -- M-1: composite target so LearnerPathSteps can pin the item to its own template
  CONSTRAINT UQ_PMI_Id_Template UNIQUE (ItemId, TemplateId)
);
GO
CREATE TABLE LearnerPaths (
  LearnerPathId   INT IDENTITY(1,1) PRIMARY KEY,
  UserId          INT NOT NULL REFERENCES Users(UserId),
  CertId          INT NOT NULL REFERENCES Certificates(CertId),
  TemplateId      INT NOT NULL,
  CurrentBandCode VARCHAR(20) NULL,
  Status          VARCHAR(10) NOT NULL DEFAULT 'active'
    CONSTRAINT CK_LearnerPaths_Status CHECK (Status IN ('active','completed','reset')),
  StartedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- M-1: TemplateId's own CertId must equal the path's own (pins the carried-down value)
  CONSTRAINT FK_LearnerPaths_TemplateCert FOREIGN KEY (TemplateId, CertId) REFERENCES PathTemplates(TemplateId, CertId),
  -- composite target so CreditTransactions can prove the charged user owns the path (H-1):
  CONSTRAINT UQ_LearnerPaths_Id_User UNIQUE (LearnerPathId, UserId),
  -- M-1: composite target so LearnerPathSteps can require the item's module belong to the same template
  CONSTRAINT UQ_LearnerPaths_Id_Template UNIQUE (LearnerPathId, TemplateId)
);
GO
-- Review Focus #5: at most one active path per (UserId,CertId)
CREATE UNIQUE INDEX UX_LearnerPaths_OneActive
  ON LearnerPaths(UserId, CertId) WHERE Status = 'active';
GO
CREATE TABLE LearnerPathSteps (
  StepId          INT IDENTITY(1,1) PRIMARY KEY,
  LearnerPathId   INT NOT NULL,
  PathModuleItemId INT NOT NULL,
  -- M-1: carried down from the owning path so the item's own template must match the path's template
  TemplateId      INT NOT NULL,
  Status          VARCHAR(10) NOT NULL DEFAULT 'locked'
    CONSTRAINT CK_Steps_Status CHECK (Status IN ('locked','available','done')),
  Score           DECIMAL(6,2) NULL,
  CompletedAt     DATETIMEOFFSET NULL,
  -- M-1: TemplateId must be the learner path's own
  CONSTRAINT FK_Steps_PathTemplate FOREIGN KEY (LearnerPathId, TemplateId) REFERENCES LearnerPaths(LearnerPathId, TemplateId),
  -- M-1: the module item must belong to that same template
  CONSTRAINT FK_Steps_ItemTemplate FOREIGN KEY (PathModuleItemId, TemplateId) REFERENCES PathModuleItems(ItemId, TemplateId)
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
  IsActive     BIT NOT NULL DEFAULT 1,
  -- M-14: human mentors are people (UserId set), AI mentors are not
  CONSTRAINT CK_Mentor_TypeUserId CHECK ((Type='human' AND UserId IS NOT NULL) OR (Type='ai' AND UserId IS NULL)),
  -- M-14: a mentor's SkillId (if any) must belong to the mentor's own CertId
  CONSTRAINT FK_Mentor_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- composite target so Bookings can carry MentorType down (M-14 / PartC-1):
  CONSTRAINT UQ_MentorProfiles_Id_Type UNIQUE (MentorId, Type)
);
GO
CREATE TABLE MentorSlots (
  SlotId   INT IDENTITY(1,1) PRIMARY KEY,
  MentorId INT NOT NULL REFERENCES MentorProfiles(MentorId),
  StartAt  DATETIMEOFFSET NOT NULL,
  EndAt    DATETIMEOFFSET NOT NULL,
  Status   VARCHAR(6) NOT NULL DEFAULT 'open'
    CONSTRAINT CK_Slots_Status CHECK (Status IN ('open','booked','closed')),
  CONSTRAINT CK_Slots_Range CHECK (EndAt > StartAt),
  -- PartC-1: composite target so Bookings.SlotId must agree with the slot's owning mentor
  CONSTRAINT UQ_MentorSlots_Id_Mentor UNIQUE (SlotId, MentorId)
);
GO
CREATE TABLE Bookings (
  BookingId INT IDENTITY(1,1) PRIMARY KEY,
  UserId    INT NOT NULL REFERENCES Users(UserId),
  MentorId  INT NOT NULL REFERENCES MentorProfiles(MentorId),
  SlotId    INT NULL REFERENCES MentorSlots(SlotId),   -- NULL for AI
  MentorType VARCHAR(6) NOT NULL
    CONSTRAINT CK_Bookings_MentorType CHECK (MentorType IN ('human','ai')),
  Status    VARCHAR(10) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_Bookings_Status CHECK (Status IN ('pending','confirmed','done','cancelled')),
  MeetLink  NVARCHAR(400) NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- composite target so CreditTransactions can prove the charged user owns the booking (H-1).
  -- Batch 2 (PartC-3) also needs this constraint; reused there, not recreated.
  CONSTRAINT UQ_Bookings_Id_User UNIQUE (BookingId, UserId),
  -- PartC-2: composite target so a payout item must agree with the booking's mentor
  CONSTRAINT UQ_Bookings_Id_Mentor UNIQUE (BookingId, MentorId),
  -- PartC-1: SlotId must belong to the same mentor as MentorId (NULL SlotId stays legal, MATCH SIMPLE)
  CONSTRAINT FK_Bookings_SlotMentor FOREIGN KEY (SlotId, MentorId) REFERENCES MentorSlots(SlotId, MentorId),
  -- M-14: MentorType must be the mentor's actual Type
  CONSTRAINT FK_Bookings_MentorType FOREIGN KEY (MentorId, MentorType) REFERENCES MentorProfiles(MentorId, Type),
  -- M-14: only human mentors need a scheduled slot; AI books without one
  CONSTRAINT CK_Bookings_MentorTypeSlot CHECK ((MentorType='human' AND SlotId IS NOT NULL) OR (MentorType='ai' AND SlotId IS NULL))
);
GO
-- Review Focus #5: one live booking per slot (cancelled frees it)
CREATE UNIQUE INDEX UX_Bookings_OnePerSlot
  ON Bookings(SlotId) WHERE SlotId IS NOT NULL AND Status <> 'cancelled';
GO
CREATE TABLE Reviews (
  ReviewId  INT IDENTITY(1,1) PRIMARY KEY,
  BookingId INT NOT NULL,
  UserId    INT NOT NULL REFERENCES Users(UserId),
  Rating    INT NOT NULL CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5),
  Text      NVARCHAR(MAX) NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT UQ_Reviews_OnePerBooking UNIQUE (BookingId),
  -- PartC-3: a review must be written by the user who made the booking
  CONSTRAINT FK_Reviews_BookingUser FOREIGN KEY (BookingId, UserId) REFERENCES Bookings(BookingId, UserId)
);
GO
CREATE TABLE Complaints (
  ComplaintId          INT IDENTITY(1,1) PRIMARY KEY,
  BookingId            INT NOT NULL,
  UserId               INT NOT NULL REFERENCES Users(UserId),
  Reason               NVARCHAR(MAX) NOT NULL,
  Status               VARCHAR(10) NOT NULL DEFAULT 'open'
    CONSTRAINT CK_Complaints_Status CHECK (Status IN ('open','reviewing','resolved')),
  Resolution           NVARCHAR(MAX) NULL,
  ResolvedByGrantTxnId INT NULL,      -- FK added in Task 10; composite FK to CreditTransactions(TxnId,UserId) added in Batch 1
  CreatedAt            DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- M-16: a make-good grant can only be recorded once the complaint is resolved
  CONSTRAINT CK_Complaints_GrantRequiresResolved CHECK (ResolvedByGrantTxnId IS NULL OR Status='resolved'),
  -- PartC-4: a complaint must be filed by the user who made the booking
  CONSTRAINT FK_Complaints_BookingUser FOREIGN KEY (BookingId, UserId) REFERENCES Bookings(BookingId, UserId)
);
GO
CREATE TABLE MentorPayouts (
  PayoutId     INT IDENTITY(1,1) PRIMARY KEY,
  MentorId     INT NOT NULL REFERENCES MentorProfiles(MentorId),
  PeriodStart  DATETIMEOFFSET NOT NULL,
  PeriodEnd    DATETIMEOFFSET NOT NULL,
  -- M-17: BookingCount/GrossAmount removed (stored aggregates drift) — see vw_MentorPayoutTotals
  Status       VARCHAR(7) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_Payouts_Status CHECK (Status IN ('pending','paid')),
  PaidAt       DATETIMEOFFSET NULL,
  CONSTRAINT CK_Payouts_Range CHECK (PeriodEnd > PeriodStart),
  -- PartC-2: composite target so a payout item must agree with the payout's mentor
  CONSTRAINT UQ_MentorPayouts_Id_Mentor UNIQUE (PayoutId, MentorId)
);
GO
CREATE TABLE MentorPayoutItems (
  PayoutId  INT NOT NULL,
  BookingId INT NOT NULL,
  MentorId  INT NOT NULL,
  CONSTRAINT PK_PayoutItems PRIMARY KEY (PayoutId, BookingId),
  CONSTRAINT UQ_PayoutItems_Booking UNIQUE (BookingId),   -- a booking settles once
  -- PartC-2: the booking's mentor and the payout's mentor must agree
  CONSTRAINT FK_PayoutItems_BookingMentor FOREIGN KEY (BookingId, MentorId) REFERENCES Bookings(BookingId, MentorId),
  CONSTRAINT FK_PayoutItems_PayoutMentor  FOREIGN KEY (PayoutId, MentorId)  REFERENCES MentorPayouts(PayoutId, MentorId)
);
GO
-- M-15: reject bookings placed on a closed slot, and keep MentorSlots.Status in sync
-- with live (non-cancelled) bookings. Booking-side filtered unique index is the double-book
-- guard (kept as the accepted deviation from the spec's slot-side wording); this trigger only
-- closes the remaining `closed`-slot hole and makes Status non-decorative.
-- R6: DELETE is in the event list because a deleted booking must free its slot; without it the
-- slot stays 'booked' forever and no one can ever book that time again.
CREATE TRIGGER trg_Bookings_SlotStatusGuardAndSync
ON Bookings AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM inserted i
    JOIN MentorSlots ms ON ms.SlotId = i.SlotId
    WHERE i.SlotId IS NOT NULL AND i.Status <> 'cancelled' AND ms.Status = 'closed'
  )
    THROW 50024, 'Booking cannot be placed on a closed slot.', 1;

  ;WITH affected AS (
    SELECT SlotId FROM inserted WHERE SlotId IS NOT NULL
    UNION SELECT SlotId FROM deleted WHERE SlotId IS NOT NULL
  )
  UPDATE ms
    SET Status = CASE WHEN EXISTS (
        SELECT 1 FROM Bookings b WHERE b.SlotId = ms.SlotId AND b.Status <> 'cancelled'
      ) THEN 'booked' ELSE 'open' END
  FROM MentorSlots ms
  JOIN affected a ON a.SlotId = ms.SlotId
  WHERE ms.Status <> 'closed';
END;
GO
-- M-13: spec-mandated mentor rating aggregate (never stored, always derived)
CREATE VIEW vw_MentorAvgRating AS
  SELECT b.MentorId, AVG(CAST(r.Rating AS DECIMAL(3,2))) AS AvgRating, COUNT(*) AS ReviewCount
  FROM Reviews r JOIN Bookings b ON b.BookingId = r.BookingId
  GROUP BY b.MentorId;
GO
-- M-17: replaces the dropped MentorPayouts.BookingCount/GrossAmount stored counters
CREATE VIEW vw_MentorPayoutTotals AS
  SELECT mp.PayoutId, mp.MentorId,
         COUNT(*) AS BookingCount,
         SUM(m.PricePerSlot) AS GrossAmount
  FROM MentorPayoutItems mpi
  JOIN MentorPayouts mp ON mp.PayoutId = mpi.PayoutId
  JOIN Bookings b ON b.BookingId = mpi.BookingId
  JOIN MentorProfiles m ON m.MentorId = b.MentorId
  GROUP BY mp.PayoutId, mp.MentorId;
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
  Amount    DECIMAL(12,2) NOT NULL CONSTRAINT CK_Orders_Amount CHECK (Amount >= 0),
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  PaidAt    DATETIMEOFFSET NULL,
  CONSTRAINT CK_Orders_PaidAtRequiresPaid CHECK (PaidAt IS NULL OR Status='paid'),
  -- composite target so CreditTransactions can prove the charged user owns the order (H-1):
  CONSTRAINT UQ_Orders_Id_User UNIQUE (OrderId, UserId)
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
  OrderId       INT NULL,
  AttemptId     INT NULL,
  BookingId     INT NULL,
  LearnerPathId INT NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- M-16: lets Complaints prove the linked grant belongs to the complainant
  CONSTRAINT UQ_CreditTxn_Id_User UNIQUE (TxnId, UserId),
  -- H-1: composite FKs carry UserId so the source row's owner must match the charged wallet.
  -- MATCH SIMPLE (default) skips the FK check when any column is NULL, so admin_grant/admin_revoke
  -- rows (all four source columns NULL) remain legal with no extra guards.
  CONSTRAINT FK_CreditTxn_Order   FOREIGN KEY (OrderId, UserId)       REFERENCES Orders(OrderId, UserId),
  CONSTRAINT FK_CreditTxn_Attempt FOREIGN KEY (AttemptId, UserId)     REFERENCES ExamAttempts(AttemptId, UserId),
  CONSTRAINT FK_CreditTxn_Booking FOREIGN KEY (BookingId, UserId)     REFERENCES Bookings(BookingId, UserId),
  CONSTRAINT FK_CreditTxn_Path    FOREIGN KEY (LearnerPathId, UserId) REFERENCES LearnerPaths(LearnerPathId, UserId),
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
-- Low: IsPremium is derived from a 'premium_path' ledger charge, never a free-standing flag.
CREATE VIEW vw_LearnerPathIsPremium AS
  SELECT lp.LearnerPathId,
         CAST(CASE WHEN EXISTS (
           SELECT 1 FROM CreditTransactions t
           WHERE t.LearnerPathId = lp.LearnerPathId AND t.Reason = 'premium_path'
         ) THEN 1 ELSE 0 END AS BIT) AS IsPremium
  FROM LearnerPaths lp;
GO
-- Review Focus #4 / C-1: no debit may drive the running balance below zero.
-- Must also cover DELETE so a removed credit row can't leave a wallet negative unnoticed.
CREATE TRIGGER trg_CreditTransactions_NoNegativeBalance
ON CreditTransactions AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM (
      SELECT DISTINCT UserId FROM inserted
      UNION SELECT DISTINCT UserId FROM deleted
    ) u
    WHERE (SELECT SUM(Delta) FROM CreditTransactions t WHERE t.UserId=u.UserId) < 0
  )
    THROW 50020, 'Transaction would drive wallet balance negative.', 1;
END;
GO
-- C-1: the ledger is append-only. No row may ever be deleted.
CREATE TRIGGER trg_CreditTransactions_AppendOnly
ON CreditTransactions AFTER DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted)
    THROW 50021, 'CreditTransactions is append-only; rows cannot be deleted.', 1;
END;
GO
-- R2b: append-only means IMMUTABLE, not merely undeletable. Without this, a settled
-- purchase could be rewritten into an admin_grant with OrderId=NULL — severing it from its
-- order, which frees UX_CreditTxn_Order to credit that same order a second time.
CREATE TRIGGER trg_CreditTransactions_NoRewrite
ON CreditTransactions AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN deleted d ON d.TxnId = i.TxnId
    WHERE i.UserId <> d.UserId
       OR i.Reason <> d.Reason
       OR i.Delta  <> d.Delta
       -- source columns are nullable: compare with a sentinel so NULL->value is caught too
       OR ISNULL(i.OrderId,-1)       <> ISNULL(d.OrderId,-1)
       OR ISNULL(i.AttemptId,-1)     <> ISNULL(d.AttemptId,-1)
       OR ISNULL(i.BookingId,-1)     <> ISNULL(d.BookingId,-1)
       OR ISNULL(i.LearnerPathId,-1) <> ISNULL(d.LearnerPathId,-1)
  )
    THROW 50028, 'CreditTransactions is append-only; a posted transaction cannot be rewritten.', 1;
END;
GO
-- H-2: OrderId/BookingId/LearnerPathId each fund at most one txn; AttemptId allows one
-- mock_exam_start AND one ai_grading charge, but only one of each (spec: one charge covers all answers).
CREATE UNIQUE INDEX UX_CreditTxn_Order ON CreditTransactions(OrderId) WHERE OrderId IS NOT NULL;
GO
CREATE UNIQUE INDEX UX_CreditTxn_Booking ON CreditTransactions(BookingId) WHERE BookingId IS NOT NULL;
GO
CREATE UNIQUE INDEX UX_CreditTxn_Path ON CreditTransactions(LearnerPathId) WHERE LearnerPathId IS NOT NULL;
GO
CREATE UNIQUE INDEX UX_CreditTxn_Attempt_Reason ON CreditTransactions(AttemptId, Reason) WHERE AttemptId IS NOT NULL;
GO
-- H-2: a 'purchase' txn must reference a paid order and its Delta must equal that order's package credits.
CREATE TRIGGER trg_CreditTransactions_PurchaseMatchesOrder
ON CreditTransactions AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN Orders o ON o.OrderId = i.OrderId
    JOIN Packages pk ON pk.PackageId = o.PackageId
    WHERE i.Reason = 'purchase'
      AND (o.Status <> 'paid' OR i.Delta <> pk.Credits)
  )
    THROW 50022, 'purchase txn must reference a paid order with Delta equal to the package credits.', 1;
END;
GO
-- R3: the above proves the order was 'paid' AT THE MOMENT OF CREDITING, but nothing stopped the
-- order being reverted to 'pending'/'expired' afterwards, leaving a credit with no paid order
-- behind it. A credited order is settled and may no longer leave 'paid'.
CREATE TRIGGER trg_Orders_CreditedStaysPaid
ON Orders AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN deleted d ON d.OrderId = i.OrderId
    WHERE d.Status = 'paid' AND i.Status <> 'paid'
      AND EXISTS (SELECT 1 FROM CreditTransactions ct
                  WHERE ct.OrderId = i.OrderId AND ct.Reason = 'purchase')
  )
    THROW 50029, 'An order that has already been credited cannot leave the paid status.', 1;
END;
GO
-- backfill the complaint make-good FK now that CreditTransactions exists.
-- M-16: composite FK carries UserId so the grant must belong to the complainant.
ALTER TABLE Complaints
  ADD CONSTRAINT FK_Complaints_GrantTxn FOREIGN KEY (ResolvedByGrantTxnId, UserId) REFERENCES CreditTransactions(TxnId, UserId);
GO
-- M-16: the linked grant must actually be an admin_grant txn (a CHECK cannot read another table).
CREATE TRIGGER trg_Complaints_GrantMustBeAdminGrant
ON Complaints AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN CreditTransactions t ON t.TxnId = i.ResolvedByGrantTxnId
    WHERE i.ResolvedByGrantTxnId IS NOT NULL
      AND t.Reason <> 'admin_grant'
  )
    THROW 50023, 'ResolvedByGrantTxnId must reference an admin_grant transaction.', 1;
END;
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
         COUNT(pa.IsCorrect) AS Answered,
         SUM(CASE WHEN pa.IsCorrect=1 THEN 1 ELSE 0 END) AS Correct
  FROM PracticeAnswers pa
  JOIN PracticeSessions ps ON ps.SessionId = pa.SessionId
  JOIN Sections s ON s.SectionId = ps.SectionId
  GROUP BY ps.UserId, s.SectionId, s.Name;
GO
