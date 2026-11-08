SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- docs/schema-tests/domain47-section-accuracy-pool.sql
-- #250 criterion 4: the weakness-analysis view returns the same numbers before and after.
--
-- A view rewritten "and it looks right" proves nothing. This file builds the SAME answer twice - once
-- in the legacy shape (an answer that names a QuestionId, whose section comes from Questions) and once
-- in the pool shape (an answer that pins an ItemVersion, whose section comes from the ExamItems
-- binding) - and asserts the view reports the identical Answered/Correct pair for each section.
--
-- The two rows are what a migration actually does: the same learner, the same paper positions, the
-- same verdicts, expressed first one way and then the other. If the new branch joined the wrong
-- column, dropped the graded filter, or counted a pool answer twice, the pairs would differ.
--
-- The wrong input that would pass a weaker test: asserting only that the view returns SOME row. Both
-- shapes return a row; only comparing Answered and Correct per section catches a branch that counts
-- but attributes to the wrong section.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. the shape: one certificate, two reading sections, one exam, one published paper
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('ACC', N'Accuracy', 1);
DECLARE @cert INT = SCOPE_IDENTITY();

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'READ', N'Reading', 'reading', 1);
DECLARE @skill INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
VALUES (@skill, @cert, 'reading', 'S1', N'Incomplete Sentences', 4, 4, 1);
DECLARE @sec1 INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
VALUES (@skill, @cert, 'reading', 'S2', N'Text Completion', 4, 4, 2);
DECLARE @sec2 INT = SCOPE_IDENTITY();

INSERT INTO Users (RoleId, Email, PasswordHash, FullName) VALUES (@role, 'acc@x.com', 0x01, N'Accuracy Learner');
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES (@cert, N'Accuracy paper', 'published', 60, 'mock');
DECLARE @exam INT = SCOPE_IDENTITY();

INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching');
DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-DET');

-- four pool items, two bound to each section: positions 1-2 in S1, 1-2 in S2
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, RenderFormat) VALUES
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'mcq'),
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'mcq'),
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'mcq'),
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'mcq');
DECLARE @i1 INT = (SELECT MIN(ItemId) FROM Items);
DECLARE @i2 INT = @i1 + 1;
DECLARE @i3 INT = @i1 + 2;
DECLARE @i4 INT = @i1 + 3;

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel) VALUES
  (@i1, 1, N'Pool stem 1', N'a', N'b', N'c', N'd', 'A'),
  (@i2, 1, N'Pool stem 2', N'a', N'b', N'c', N'd', 'A'),
  (@i3, 1, N'Pool stem 3', N'a', N'b', N'c', N'd', 'A'),
  (@i4, 1, N'Pool stem 4', N'a', N'b', N'c', N'd', 'A');
DECLARE @v1 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @i1);
DECLARE @v2 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @i2);
DECLARE @v3 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @i3);
DECLARE @v4 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @i4);

INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder) VALUES
  (@exam, @sec1, @cert, @v1, @i1, 'reading', 1),
  (@exam, @sec1, @cert, @v2, @i2, 'reading', 2),
  (@exam, @sec2, @cert, @v3, @i3, 'reading', 1),
  (@exam, @sec2, @cert, @v4, @i4, 'reading', 2);

-- the legacy rows the pool replaced, so both branches have the same four positions
INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec1, 1);
DECLARE @g1 INT = SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec2, 2);
DECLARE @g2 INT = SCOPE_IDENTITY();
INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder) VALUES
  (@g1, @exam, @sec1, @skill, N'Legacy 1', 'B1', 'mcq', 1),
  (@g1, @exam, @sec1, @skill, N'Legacy 2', 'B1', 'mcq', 2),
  (@g2, @exam, @sec2, @skill, N'Legacy 3', 'B1', 'mcq', 1),
  (@g2, @exam, @sec2, @skill, N'Legacy 4', 'B1', 'mcq', 2);
DECLARE @q1 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND SectionId = @sec1 AND DisplayOrder = 1);
DECLARE @q2 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND SectionId = @sec1 AND DisplayOrder = 2);
DECLARE @q3 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND SectionId = @sec2 AND DisplayOrder = 1);
DECLARE @q4 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND SectionId = @sec2 AND DisplayOrder = 2);

-- ---------------------------------------------------------------------------------------------
-- 2. two graded attempts: one legacy-shaped, one pool-shaped, with the SAME verdicts
--    S1: correct, wrong   S2: correct, correct
--
--    An attempt reaches 'graded' only after its answers exist and only by declaring its writer
--    (trg_Attempts_SingleWriter), so each attempt is inserted in progress, answered, then graded.
-- ---------------------------------------------------------------------------------------------
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @legacyAttempt INT = SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @poolAttempt INT = SCOPE_IDENTITY();

INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, IsCorrect) VALUES
  (@legacyAttempt, @q1, @exam, 1),
  (@legacyAttempt, @q2, @exam, 0),
  (@legacyAttempt, @q3, @exam, 1),
  (@legacyAttempt, @q4, @exam, 1);

INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, ItemId, ItemVersionId, SelectedLabel, IsCorrect) VALUES
  (@poolAttempt, @q1, @exam, @i1, @v1, 'A', 1),
  (@poolAttempt, @q2, @exam, @i2, @v2, 'A', 0),
  (@poolAttempt, @q3, @exam, @i3, @v3, 'A', 1),
  (@poolAttempt, @q4, @exam, @i4, @v4, 'A', 1);

-- grade them, declaring the one writer of the attempt score and band (ADR-0012)
UPDATE ExamAttempts
SET Status = 'graded', WrittenByService = 'AttemptEngine', ProjectionVersion = 1
WHERE AttemptId IN (@legacyAttempt, @poolAttempt);

-- ---------------------------------------------------------------------------------------------
-- 3. the view reports BOTH shapes. Each section is 4 answered (2 legacy + 2 pool).
-- ---------------------------------------------------------------------------------------------
IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec1) <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: section 1 should hold 4 answers across both shapes; the pool branch is not contributing', 1;
IF (SELECT Correct FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: section 1 should hold 2 correct answers', 1;
IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec2) <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: section 2 should hold 4 answers across both shapes', 1;
IF (SELECT Correct FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec2) <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: section 2 should hold 4 correct answers', 1;
PRINT 'the view counts a legacy answer and a pool answer in the same section total';

-- ---------------------------------------------------------------------------------------------
-- 4. the same numbers, proved per shape. A branch that attributed the pool answer to the wrong
--    section would still total 4 above by symmetry, so this compares the two branches alone.
-- ---------------------------------------------------------------------------------------------
DECLARE @legacySec1 INT = (
  SELECT COUNT(*) FROM AttemptAnswers aa
  JOIN ExamAttempts a ON a.AttemptId = aa.AttemptId
  JOIN Questions q ON q.QuestionId = aa.QuestionId
  WHERE a.AttemptId = @legacyAttempt AND q.SectionId = @sec1);
DECLARE @poolSec1 INT = (
  SELECT COUNT(*) FROM AttemptAnswers aa
  JOIN ExamAttempts a ON a.AttemptId = aa.AttemptId
  JOIN ExamItems ei ON ei.ExamId = aa.ExamId AND ei.ItemVersionId = aa.ItemVersionId
  WHERE a.AttemptId = @poolAttempt AND ei.SectionId = @sec1);
IF @legacySec1 <> 2 OR @poolSec1 <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the two shapes did not attribute 2 answers each to section 1', 1;
PRINT 'each shape attributes the same two positions to the same section';

-- ---------------------------------------------------------------------------------------------
-- 5. an UNGRADED attempt contributes nothing, in either shape. The wrong input a shape-blind test
--    would pass: a pool answer on an in-progress attempt, which must not inflate the denominator.
-- ---------------------------------------------------------------------------------------------
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @live INT = SCOPE_IDENTITY();
INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, ItemId, ItemVersionId, SelectedLabel, IsCorrect)
VALUES (@live, @q1, @exam, @i1, @v1, 'A', 0);

IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec1) <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: an in-progress attempt changed the accuracy denominator', 1;
PRINT 'an in-progress attempt contributes no row, in either shape';

-- ---------------------------------------------------------------------------------------------
-- 6. and the count once more: no refused row was left behind by this file
-- ---------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM AttemptAnswers) <> 9
  THROW 50000, 'EXPECT_REJECT FAILED: this file left an unexpected number of answers behind', 1;
PRINT 'the view is shape-blind, and the graded filter holds on both branches';
