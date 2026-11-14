SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #309: Expand phase schema invariants:
--   1. Items.TargetBands and Lessons.TargetBands default to 'ALL' and accept band lists.
--   2. CreditTransactions accepts Reason = 'learning_path' (Delta < 0, no source FKs, no AdminUserId)
--      and rejects Delta > 0 or non-null source FKs.
--   3. AiGradings.PriorityFix supports NVARCHAR(1000), plus ModelRewrite and ErrorSpansJson.
--   4. PlacementTests allows CertId IS NULL and stores ListeningPct, ReadingPct, SpeakingPct,
--      WritingPct, OverallPct (DECIMAL(5,2)).
--   5. The 5 retired triggers are absent from sys.triggers.

DECLARE @msg NVARCHAR(2048);

-- 5. Retired triggers must be absent
IF EXISTS (
  SELECT 1 FROM sys.triggers
  WHERE name IN (
    'trg_PlacementTests_SingleWriter',
    'trg_Attempts_SingleWriter',
    'trg_LearnerPathSteps_SingleWriter',
    'trg_Items_LifecycleTransitions',
    'trg_PracticeSessions_StatusTransitions'
  )
)
  THROW 50000, 'EXPECT_REJECT FAILED: one or more retired triggers still exist in sys.triggers', 1;

-- Seed minimal fixtures
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @rLearner INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
VALUES (@rLearner, 'expand309@test.local', 0x01, N'Expand 309');
DECLARE @u INT = SCOPE_IDENTITY();

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC_309', N'TOEIC 309', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
VALUES (@cert, 'LIST', N'Listening', 'listening', 1),
       (@cert, 'WRIT', N'Writing', 'writing', 2);
DECLARE @skList INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
DECLARE @skWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'WRIT');

INSERT INTO Topics (Modality, Code, Name)
VALUES ('listening', 'L-GIST', N'Gist Listening'),
       ('writing', 'W-ARG', N'Argumentative Writing');
DECLARE @topList INT = (SELECT TopicId FROM Topics WHERE Code = 'L-GIST');
DECLARE @topWrit INT = (SELECT TopicId FROM Topics WHERE Code = 'W-ARG');

-- 1. Items.TargetBands and Lessons.TargetBands
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat, ContentTier, LifecycleStatus)
VALUES ('listening', @topList, 'B1', 'short_exchange', 'mcq', 'demo', 'active');
DECLARE @itemDefault INT = SCOPE_IDENTITY();

IF (SELECT TargetBands FROM Items WHERE ItemId = @itemDefault) <> 'ALL'
  THROW 50000, 'EXPECT_REJECT FAILED: Items.TargetBands did not default to ALL', 1;

INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat, ContentTier, LifecycleStatus, TargetBands)
VALUES ('listening', @topList, 'B1', 'short_exchange', 'mcq', 'demo', 'active', 'A2,B1,B2');
DECLARE @itemTagged INT = SCOPE_IDENTITY();

IF (SELECT TargetBands FROM Items WHERE ItemId = @itemTagged) <> 'A2,B1,B2'
  THROW 50000, 'EXPECT_REJECT FAILED: Items.TargetBands did not store A2,B1,B2', 1;

INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
VALUES (@cert, @skList, 'listening', @topList, 'B1', N'Lesson Default', 5, 1);
DECLARE @lessonDefault INT = SCOPE_IDENTITY();

IF (SELECT TargetBands FROM Lessons WHERE LessonId = @lessonDefault) <> 'ALL'
  THROW 50000, 'EXPECT_REJECT FAILED: Lessons.TargetBands did not default to ALL', 1;

INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder, TargetBands)
VALUES (@cert, @skList, 'listening', @topList, 'B1', N'Lesson Tagged', 5, 2, '5.5,6.0,6.5');
DECLARE @lessonTagged INT = SCOPE_IDENTITY();

IF (SELECT TargetBands FROM Lessons WHERE LessonId = @lessonTagged) <> '5.5,6.0,6.5'
  THROW 50000, 'EXPECT_REJECT FAILED: Lessons.TargetBands did not store 5.5,6.0,6.5', 1;

-- 2. CreditTransactions 'learning_path'
INSERT INTO Packages (Name, Credits, Price, IsActive) VALUES (N'10 credits', 10, 100000, 1);
DECLARE @pk INT = SCOPE_IDENTITY();
INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway)
VALUES (@u, @pk, 'paid', 100000, 'DH-309-001', NEXT VALUE FOR SEQ_PaymentNo, 'mock');
DECLARE @o INT = SCOPE_IDENTITY();
INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway)
VALUES (@u, @pk, 'paid', 100000, 'DH-309-002', NEXT VALUE FOR SEQ_PaymentNo, 'mock');
DECLARE @oUnused INT = SCOPE_IDENTITY();

INSERT INTO CreditTransactions (UserId, Reason, Delta, OrderId)
VALUES (@u, 'purchase', 10, @o);

INSERT INTO CreditTransactions (UserId, Reason, Delta, Note)
VALUES (@u, 'learning_path', -5, N'4-week learning path');

IF (SELECT Balance FROM vw_UserCreditBalance WHERE UserId = @u) <> 5
  THROW 50000, 'EXPECT_REJECT FAILED: learning_path -5 debit did not leave balance 5', 1;

BEGIN TRY
  INSERT INTO CreditTransactions (UserId, Reason, Delta, Note)
  VALUES (@u, 'learning_path', 5, N'positive learning_path');
  THROW 50000, 'EXPECT_REJECT FAILED: positive delta for learning_path was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_DeltaSign%'
  BEGIN SET @msg = ERROR_MESSAGE(); THROW 50000, @msg, 1; END
END CATCH;

BEGIN TRY
  INSERT INTO CreditTransactions (UserId, Reason, Delta, OrderId, Note)
  VALUES (@u, 'learning_path', -5, @oUnused, N'with order');
  THROW 50000, 'EXPECT_REJECT FAILED: learning_path with OrderId was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_Source%'
  BEGIN SET @msg = ERROR_MESSAGE(); THROW 50000, @msg, 1; END
END CATCH;

-- 3. AiGradings.PriorityFix NVARCHAR(1000) + ModelRewrite + ErrorSpansJson
INSERT INTO Exams (CertId, Purpose, Name, Status, DurationMinutes)
VALUES (@cert, 'writing_practice', N'Writing 309', 'published', 30);
DECLARE @wExam INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount)
VALUES (@skWrit, @cert, 'writing', 'W1', N'Task 1', 1, 1);
DECLARE @wSec INT = SCOPE_IDENTITY();
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat, ContentTier, LifecycleStatus)
VALUES ('writing', @topWrit, 'B1', 'persuasive_essay', 'free_response', 'demo', 'active');
DECLARE @wItem INT = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem)
VALUES (@wItem, 1, N'Write an essay.');
DECLARE @wVer INT = SCOPE_IDENTITY();
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemId, ItemVersionId, Modality, DisplayOrder)
VALUES (@wExam, @wSec, @cert, @wItem, @wVer, 'writing', 1);
DECLARE @wExamItem INT = SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@u, @wExam, @cert, 'submitted', DATEADD(hour, 1, SYSDATETIMEOFFSET()));
DECLARE @wAttempt INT = SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId, ExamItemId, ExamId, SectionId, SkillId, QuestionType, Status, ResponseText)
VALUES (@wAttempt, @wExamItem, @wExam, @wSec, @skWrit, 'free_response', 'pending_ai', N'This is a sample essay.');
DECLARE @fr INT = SCOPE_IDENTITY();

DECLARE @longFix NVARCHAR(1000) = REPLICATE(N'A', 600);
INSERT INTO AiGradings (FreeResponseId, SkillId, Status, OverallScaled, PriorityFix, ModelRewrite, ErrorSpansJson, CompletedAt)
VALUES (@fr, @skWrit, 'done', 6.50, @longFix, N'Improved model rewrite.', N'[{"category":"grammar","originalSnippet":"is","correctedSnippet":"are","explanationVi":"ok"}]', SYSDATETIMEOFFSET());
DECLARE @gr INT = SCOPE_IDENTITY();

IF LEN((SELECT PriorityFix FROM AiGradings WHERE GradingId = @gr)) <> 600
  THROW 50000, 'EXPECT_REJECT FAILED: AiGradings.PriorityFix did not store 600 chars', 1;

-- 4. PlacementTests nullable CertId + diagnostic percentage columns
INSERT INTO PlacementTests (UserId, CertId, Status, Scope, ListeningPct, ReadingPct, SpeakingPct, WritingPct, OverallPct)
VALUES (@u, NULL, 'in_progress', 'all_skills', 75.50, 80.25, 65.00, 70.00, 72.69);
DECLARE @pt INT = SCOPE_IDENTITY();

IF (SELECT OverallPct FROM PlacementTests WHERE PlacementId = @pt) <> 72.69
  THROW 50000, 'EXPECT_REJECT FAILED: PlacementTests.OverallPct did not store 72.69', 1;

PRINT 'PASS domain55-expand-schema-309';
GO
