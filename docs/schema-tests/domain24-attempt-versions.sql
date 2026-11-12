SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #186 / #269: an attempt answers a roster POSITION and the version it pinned, and a past attempt
-- renders the version it sat.
--
-- The half a constraint can express is here; the half only a service can show (a re-versioned item
-- leaves a finished attempt's review unchanged) is in
-- tests/Services.Tests/AttemptVersionPinTests.cs.
--
--   1. an answer pins the item version, and the pin survives a re-versioned item
--   2. an answer cannot name a version of an item other than the one it claims
--      (FK_AttemptAnswers_VersionItem)
--   3. the same item version bound at two positions is two answers, because the POSITION is the
--      answer's identity (UX_AttemptAnswers_ExamItem) - this replaces the old "an answer may name no
--      version at all" case, which only existed because a Questions row could have no pool pin. A
--      position always has one now, so that case is unrepresentable and its assertion is gone.
--   4. half a pair is unrepresentable too: ItemId, ItemVersionId and ExamItemId are all NOT NULL, so
--      CK_AttemptAnswers_ItemPair was dropped with the nullable columns it guarded.
--   5. an answer cannot name a label outside A-D (CK_AttemptAnswers_SelectedLabel)
--
-- Every CATCH checks the specific guard by name: a catch that proves only "something failed" cannot
-- tell a working guard from a typo in this file.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. the shape: one certificate, one reading section, one published exam, three pool items
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('PIN', N'Pinned answers', 1);
DECLARE @cert INT = SCOPE_IDENTITY();

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'READ', N'Reading', 'reading', 1);
DECLARE @skill INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 2, 1);
DECLARE @sec INT = SCOPE_IDENTITY();

INSERT INTO Users (RoleId, Email, PasswordHash, FullName) VALUES (@role, 'pin@x.com', 0x01, N'Pin Learner');
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES (@cert, N'Pin paper', 'published', 60, 'mock');
DECLARE @exam INT = SCOPE_IDENTITY();

INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching');
DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-DET');

INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag) VALUES
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'pin-a'),
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'pin-b'),
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'pin-c');
DECLARE @item1 INT = (SELECT ItemId FROM Items WHERE BatchTag = 'pin-a');
DECLARE @item2 INT = (SELECT ItemId FROM Items WHERE BatchTag = 'pin-b');
DECLARE @item3 INT = (SELECT ItemId FROM Items WHERE BatchTag = 'pin-c');

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel) VALUES
  (@item1, 1, N'Pinned stem one.', N'a', N'b', N'c', N'd', 'C'),
  (@item2, 1, N'Pinned stem two.', N'a', N'b', N'c', N'd', 'A'),
  (@item3, 1, N'Pinned stem three.', N'a', N'b', N'c', N'd', 'B');
DECLARE @v1 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item1);
DECLARE @v2 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item2);
DECLARE @v3 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item3);

-- item1 bound TWICE: the same version at two positions is the case the old uniqueness index could not
-- express, and the reason the roster position - not the version pair - is the answer's identity.
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder) VALUES
  (@exam, @sec, @cert, @v1, @item1, 'reading', 1),
  (@exam, @sec, @cert, @v2, @item2, 'reading', 2),
  (@exam, @sec, @cert, @v1, @item1, 'reading', 3),
  (@exam, @sec, @cert, @v3, @item3, 'reading', 4);
DECLARE @pos1 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND DisplayOrder = 1);
DECLARE @pos2 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND DisplayOrder = 2);
DECLARE @pos3 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND DisplayOrder = 3);
DECLARE @pos4 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND DisplayOrder = 4);

INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @att INT = SCOPE_IDENTITY();

-- ---------------------------------------------------------------------------------------------
-- 2. the happy path: an answer pins the version the binding pinned
-- ---------------------------------------------------------------------------------------------
INSERT INTO AttemptAnswers (AttemptId, ExamId, ItemId, ItemVersionId, ExamItemId, SelectedLabel, IsCorrect)
VALUES (@att, @exam, @item1, @v1, @pos1, 'C', 1);

IF (SELECT ItemVersionId FROM AttemptAnswers WHERE AttemptId = @att AND ExamItemId = @pos1) <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: a pinned answer did not store the version it was given', 1;
IF (SELECT SelectedLabel FROM AttemptAnswers WHERE AttemptId = @att AND ExamItemId = @pos1) <> 'C'
  THROW 50000, 'EXPECT_REJECT FAILED: a pinned answer did not store its label', 1;
PRINT 'an answer pins the item version and the label it recorded';

-- the item is re-versioned, and the answer goes on naming version 1 - copy-on-write is what makes a
-- past attempt renderable after a correction
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@item1, 2, N'Pinned stem one, corrected.', N'a', N'b', N'c', N'd', 'A');
IF (SELECT ItemVersionId FROM AttemptAnswers WHERE AttemptId = @att AND ExamItemId = @pos1) <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: re-versioning an item moved an answer off the version it sat', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the re-versioned item did not keep both versions', 1;
PRINT 'a re-versioned item leaves the pinned answer and both versions intact';

-- ---------------------------------------------------------------------------------------------
-- 3. one version bound at two positions is two answers, and one answer per position
-- ---------------------------------------------------------------------------------------------
INSERT INTO AttemptAnswers (AttemptId, ExamId, ItemId, ItemVersionId, ExamItemId, SelectedLabel)
VALUES (@att, @exam, @item1, @v1, @pos3, 'A');
PRINT 'accepted a second answer to the same version at a different position';

BEGIN TRY
  INSERT INTO AttemptAnswers (AttemptId, ExamId, ItemId, ItemVersionId, ExamItemId, SelectedLabel)
  VALUES (@att, @exam, @item1, @v1, @pos3, 'B');
  THROW 50000, 'EXPECT_REJECT FAILED: one position was answered twice', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UX_AttemptAnswers_ExamItem%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the duplicate position was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a second answer to one roster position, by UX_AttemptAnswers_ExamItem';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 4. the version must be a version of the item the answer names
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO AttemptAnswers (AttemptId, ExamId, ItemId, ItemVersionId, ExamItemId)
  VALUES (@att, @exam, @item2, @v1, @pos4);
  THROW 50000, 'EXPECT_REJECT FAILED: an answer named one item and another item''s version', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_AttemptAnswers_VersionItem%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the mismatched version was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an answer naming one item and another item''s version, by FK_AttemptAnswers_VersionItem';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 5. a label outside A-D is not an answer
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  UPDATE AttemptAnswers SET SelectedLabel = 'E' WHERE AttemptId = @att AND ExamItemId = @pos1;
  THROW 50000, 'EXPECT_REJECT FAILED: label E was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_AttemptAnswers_SelectedLabel%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the bad label was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected label E, by CK_AttemptAnswers_SelectedLabel';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 6. the count, once more: no refusal above left a row behind
-- ---------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM AttemptAnswers WHERE AttemptId = @att) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a refused answer left a row behind', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a refusal above created or removed a version', 1;
PRINT 'the pin stands, every guard fired by name, and no refusal left a row behind';
