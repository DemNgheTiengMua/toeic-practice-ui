SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #269: the FreeResponse section pin. Every writer now supplies SectionId, and the two constraints that
-- replaced FK_FreeResp_QuestionSkill must accept the honest row and refuse the dishonest one.
SET NOCOUNT ON;

INSERT INTO Roles (Code, Name) VALUES ('frauthor', N'FR author');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Permissions (Code) VALUES ('pool.content.author');
INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@role, 'pool.content.author');
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('FRX', N'Free response pin', 1);
DECLARE @cert INT = SCOPE_IDENTITY();

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
  (@cert, 'READ', N'Reading', 'reading', 1, 30),
  (@cert, 'WRIT', N'Writing', 'writing', 2, 30);
DECLARE @readSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');
DECLARE @writSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'WRIT');

INSERT INTO Users (RoleId, Email, PasswordHash, ActiveCertId)
VALUES (@role, 'fr@x.com', 0x01, @cert);
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, LastWrittenBy) VALUES
  (@readSkill, @cert, 'reading', 'R1', N'Reading', 4, 1, 1, @user),
  (@writSkill, @cert, 'writing', 'W1', N'Writing', 4, 1, 2, @user);
DECLARE @readSec INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'R1');
DECLARE @writSec INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'W1');

INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, LastWrittenBy)
VALUES (@cert, N'FR paper', 'draft', 30, 'mock', @user);
DECLARE @exam INT = SCOPE_IDENTITY();
INSERT INTO ExamPublishAudits (ExamId, PassedAt, WrittenByService)
VALUES (@exam, DATEADD(SECOND, 1, SYSDATETIMEOFFSET()), 'AdminExamService');
UPDATE Exams SET Status = 'published', LastWrittenBy = @user WHERE ExamId = @exam;

INSERT INTO Topics (Modality, Code, Name) VALUES ('writing', 'W-CORR', N'Transactional correspondence');
DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code = 'W-CORR');

INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat, ContentTier, LifecycleStatus, BatchTag, LastWrittenBy)
VALUES ('writing', @topic, 'B1', 'transactional_correspondence', 'free_response', 'demo', 'active', 'fr-a', @user);
DECLARE @item INT = (SELECT ItemId FROM Items WHERE BatchTag = 'fr-a');
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, MinWords, MaxWords, ResponseSeconds, LastWrittenBy)
VALUES (@item, 1, N'Write a short email.', 10, 100, 600, @user);
DECLARE @ver INT = SCOPE_IDENTITY();

INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, LastWrittenBy)
VALUES (@exam, @writSec, @cert, @ver, @item, 'writing', 1, @user);
DECLARE @pos INT = SCOPE_IDENTITY();

INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @attempt INT = SCOPE_IDENTITY();

-- ---- 1. the honest row: the section and skill are the position's own ----------------------------
INSERT INTO FreeResponses (AttemptId, ExamItemId, ExamId, SectionId, SkillId, QuestionType, ResponseText, Status, SubmittedAt)
VALUES (@attempt, @pos, @exam, @writSec, @writSkill, 'free_response', N'Dear team, ...', 'pending_ai', SYSDATETIMEOFFSET());
PRINT '1 ok: a response naming its position''s own section and skill is accepted';

-- ---- 2. a listening/reading section with a writing skill must be refused ------------------------
BEGIN TRY
  INSERT INTO FreeResponses (AttemptId, ExamItemId, ExamId, SectionId, SkillId, QuestionType, ResponseText, Status, SubmittedAt)
  VALUES (@attempt, @pos, @exam, @readSec, @writSkill, 'free_response', N'x', 'pending_ai', SYSDATETIMEOFFSET());
  THROW 50000, 'EXPECT_REJECT FAILED: a response named a section that is not its position''s', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT '2 ok: a section that is not the position''s is refused';
END CATCH

-- ---- 3. the writing skill under the READING section must be refused by the skill pin ------------
BEGIN TRY
  INSERT INTO FreeResponses (AttemptId, ExamItemId, ExamId, SectionId, SkillId, QuestionType, ResponseText, Status, SubmittedAt)
  VALUES (@attempt, @pos, @exam, @writSec, @readSkill, 'free_response', N'x', 'pending_ai', SYSDATETIMEOFFSET());
  THROW 50000, 'EXPECT_REJECT FAILED: a response claimed a skill its section does not hold', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT '3 ok: a skill that is not the section''s is refused';
END CATCH
