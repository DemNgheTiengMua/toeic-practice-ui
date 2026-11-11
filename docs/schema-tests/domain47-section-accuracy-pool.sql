SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- docs/schema-tests/domain47-section-accuracy-pool.sql
-- #250 criterion 4 / #269: the weakness-analysis view attributes an answer to the section of the
-- POSITION it was written against.
--
-- A view rewritten "and it looks right" proves nothing. This file builds the same graded answers and
-- then asserts the view against a DIRECT computation over the same rows - so a view that joined the
-- wrong column, dropped the graded filter, counted twice, or returned nothing at all fails.
--
-- #269 removed the second shape. Until then the file compared a legacy attempt against a pool attempt
-- for identical figures; with Questions gone there is no legacy attempt to build, and an equality check
-- against a second view that also reads zero would have passed vacuously. The assertions are therefore
-- on exact figures, which is the stronger claim.
--
-- The wrong input that would pass a weaker test: asserting only that the view returns SOME row. A view
-- that attributed every answer to the first section returns rows too. Only the per-section numbers catch
-- a branch that counts but attributes to the wrong section.

-- ---------------------------------------------------------------------------------------------
-- 1. the shape: one certificate, two reading sections, one exam, one published paper
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('accauthor', N'Accuracy author');
DECLARE @role INT = SCOPE_IDENTITY();
-- ADM-03 judges an account by its permissions, so this author needs the content-author permission.
INSERT INTO Permissions (Code) VALUES ('pool.content.author');
INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@role, 'pool.content.author');

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('ACC', N'Accuracy', 1);
DECLARE @cert INT = SCOPE_IDENTITY();

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'READ', N'Reading', 'reading', 1);
DECLARE @skill INT = SCOPE_IDENTITY();

INSERT INTO Users (RoleId, Email, PasswordHash, FullName) VALUES (@role, 'acc@x.com', 0x01, N'Accuracy author');
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, LastWrittenBy)
VALUES (@skill, @cert, 'reading', 'S1', N'Incomplete Sentences', 4, 4, 1, @user);
DECLARE @sec1 INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, LastWrittenBy)
VALUES (@skill, @cert, 'reading', 'S2', N'Text Completion', 4, 4, 2, @user);
DECLARE @sec2 INT = SCOPE_IDENTITY();

-- ADM-11: an exam is created as a draft and published through one door, which records the readiness
-- pass. A fixture that INSERTs `published` is refused by design, so it takes the same two steps.
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, LastWrittenBy) VALUES (@cert, N'Accuracy paper', 'draft', 60, 'mock', @user);
DECLARE @exam INT = SCOPE_IDENTITY();
INSERT INTO ExamPublishAudits (ExamId, PassedAt, WrittenByService)
VALUES (@exam, DATEADD(SECOND, 1, SYSDATETIMEOFFSET()), 'AdminExamService');
UPDATE Exams SET Status = 'published', LastWrittenBy = @user WHERE ExamId = @exam;

INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching');
DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-DET');

-- four pool items, two bound to each section: positions 1-2 in S1, 1-2 in S2
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, RenderFormat, LastWrittenBy) VALUES
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'mcq', @user),
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'mcq', @user),
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'mcq', @user),
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'mcq', @user);
DECLARE @i1 INT = (SELECT MIN(ItemId) FROM Items);
DECLARE @i2 INT = @i1 + 1;
DECLARE @i3 INT = @i1 + 2;
DECLARE @i4 INT = @i1 + 3;

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy) VALUES
  (@i1, 1, N'Pool stem 1', N'a', N'b', N'c', N'd', 'A', @user),
  (@i2, 1, N'Pool stem 2', N'a', N'b', N'c', N'd', 'A', @user),
  (@i3, 1, N'Pool stem 3', N'a', N'b', N'c', N'd', 'A', @user),
  (@i4, 1, N'Pool stem 4', N'a', N'b', N'c', N'd', 'A', @user);
DECLARE @v1 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @i1);
DECLARE @v2 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @i2);
DECLARE @v3 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @i3);
DECLARE @v4 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @i4);

INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, LastWrittenBy) VALUES
  (@exam, @sec1, @cert, @v1, @i1, 'reading', 1, @user),
  (@exam, @sec1, @cert, @v2, @i2, 'reading', 2, @user),
  (@exam, @sec2, @cert, @v3, @i3, 'reading', 1, @user),
  (@exam, @sec2, @cert, @v4, @i4, 'reading', 2, @user);
DECLARE @p1 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND SectionId = @sec1 AND DisplayOrder = 1);
DECLARE @p2 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND SectionId = @sec1 AND DisplayOrder = 2);
DECLARE @p3 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND SectionId = @sec2 AND DisplayOrder = 1);
DECLARE @p4 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND SectionId = @sec2 AND DisplayOrder = 2);

-- ---------------------------------------------------------------------------------------------
-- 2. one graded attempt. S1: correct, wrong   S2: correct, correct
--
--    An attempt reaches 'graded' only after its answers exist and only by declaring its writer
--    (trg_Attempts_SingleWriter), so it is inserted in progress, answered, then graded.
-- ---------------------------------------------------------------------------------------------
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @attempt INT = SCOPE_IDENTITY();

INSERT INTO AttemptAnswers (AttemptId, ExamId, ItemId, ItemVersionId, ExamItemId, SelectedLabel, IsCorrect) VALUES
  (@attempt, @exam, @i1, @v1, @p1, 'A', 1),
  (@attempt, @exam, @i2, @v2, @p2, 'A', 0),
  (@attempt, @exam, @i3, @v3, @p3, 'A', 1),
  (@attempt, @exam, @i4, @v4, @p4, 'A', 1);

UPDATE ExamAttempts
SET Status = 'graded', WrittenByService = 'AttemptEngine', ProjectionVersion = 1
WHERE AttemptId = @attempt;

-- ---------------------------------------------------------------------------------------------
-- 3. the view's figures, per section. S1 holds 2 answers with 1 correct; S2 holds 2 with 2 correct.
--    A view that attributed everything to one section, or counted an answer twice, reads differently.
-- ---------------------------------------------------------------------------------------------
IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: section 1 should hold exactly 2 answers', 1;
IF (SELECT Correct FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec1) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: section 1 should hold 1 correct answer', 1;
IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec2) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: section 2 should hold exactly 2 answers', 1;
IF (SELECT Correct FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec2) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: section 2 should hold 2 correct answers', 1;
PRINT 'the view reports the exact figures per section';

-- ---------------------------------------------------------------------------------------------
-- 4. and the same numbers computed directly, so the view is not merely self-consistent. This is the
--    comparison a "returns some row" test cannot make: the two agree on WHICH section each answer
--    belongs to, not just on the totals.
-- ---------------------------------------------------------------------------------------------
DECLARE @direct1 INT = (
  SELECT COUNT(*) FROM AttemptAnswers aa
  JOIN ExamAttempts a ON a.AttemptId = aa.AttemptId
  JOIN ExamItems ei ON ei.ExamItemId = aa.ExamItemId
  WHERE a.AttemptId = @attempt AND ei.SectionId = @sec1);
DECLARE @direct1Correct INT = (
  SELECT COUNT(*) FROM AttemptAnswers aa
  JOIN ExamAttempts a ON a.AttemptId = aa.AttemptId
  JOIN ExamItems ei ON ei.ExamItemId = aa.ExamItemId
  WHERE a.AttemptId = @attempt AND ei.SectionId = @sec1 AND aa.IsCorrect = 1);
IF @direct1 <> 2 OR @direct1Correct <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the direct computation disagrees with the fixture', 1;
IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec1) <> @direct1
  THROW 50000, 'EXPECT_REJECT FAILED: the view and the direct computation disagree on section 1', 1;
PRINT 'the view agrees with a direct computation over the same rows';

-- ---------------------------------------------------------------------------------------------
-- 5. an UNGRADED attempt contributes nothing. The wrong input a shape-blind test would pass: an answer
--    on an in-progress attempt, which must not inflate the denominator.
-- ---------------------------------------------------------------------------------------------
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @live INT = SCOPE_IDENTITY();
INSERT INTO AttemptAnswers (AttemptId, ExamId, ItemId, ItemVersionId, ExamItemId, SelectedLabel, IsCorrect)
VALUES (@live, @exam, @i1, @v1, @p1, 'A', 0);

IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId = @user AND SectionId = @sec1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: an in-progress attempt changed the accuracy denominator', 1;
PRINT 'an in-progress attempt contributes no row';

-- ---------------------------------------------------------------------------------------------
-- 6. an answer whose position belongs to another paper cannot be written at all, so the view cannot
--    be fed a cross-paper answer in the first place (FK_AttemptAnswers_ExamItemExam). This is the
--    constraint #269 had to restore: without it the view credited one paper's answer to another's
--    section, which is not a rendering bug but a wrong number about a learner.
-- ---------------------------------------------------------------------------------------------
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, LastWrittenBy) VALUES (@cert, N'Other paper', 'draft', 60, 'mock', @user);
DECLARE @otherExam INT = SCOPE_IDENTITY();
INSERT INTO ExamPublishAudits (ExamId, PassedAt, WrittenByService)
VALUES (@otherExam, DATEADD(SECOND, 1, SYSDATETIMEOFFSET()), 'AdminExamService');
UPDATE Exams SET Status = 'published', LastWrittenBy = @user WHERE ExamId = @otherExam;
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, LastWrittenBy)
VALUES (@otherExam, @sec1, @cert, @v1, @i1, 'reading', 1, @user);
DECLARE @otherPos INT = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO AttemptAnswers (AttemptId, ExamId, ItemId, ItemVersionId, ExamItemId, SelectedLabel)
  VALUES (@live, @exam, @i1, @v1, @otherPos, 'A');
  THROW 50000, 'EXPECT_REJECT FAILED: an answer accepted a position belonging to another paper', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'an answer cannot name another paper''s position';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 7. and the count: no refused row was left behind by this file
-- ---------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM AttemptAnswers) <> 5
  THROW 50000, 'EXPECT_REJECT FAILED: this file left an unexpected number of answers behind', 1;
PRINT 'the graded filter holds, and the position-to-section attribution is the only one';
