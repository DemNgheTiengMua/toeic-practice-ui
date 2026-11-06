SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #186: an attempt answers a version, and a past attempt renders the version it sat.
--
-- The half a constraint can express is here; the half only a service can show (a re-versioned item
-- leaves a finished attempt's review unchanged) is in
-- tests/Services.Tests/AttemptVersionPinTests.cs.
--
--   1. an answer may name an item version, and the pair survives a re-versioned item
--   2. an answer may name no version at all, because a paper the pool has not assembled has none
--   3. an answer cannot name a version of an item other than the one it claims
--      (FK_AttemptAnswers_VersionItem)
--   4. an answer cannot name half a pair - an item with no version, or a version with no item
--      (CK_AttemptAnswers_ItemPair)
--   5. an answer cannot name a label outside A-D (CK_AttemptAnswers_SelectedLabel)
--
-- Every CATCH checks the specific guard by name: a catch that proves only "something failed" cannot
-- tell a working guard from a typo in this file.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. the shape: one certificate, one reading section, one published exam, two pool items
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

-- The legacy shape the sitting projection still reads. Its key is B, which disagrees with the pinned
-- version's key below: that is what makes the pinned read path provable rather than incidental.
INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
DECLARE @grp INT = SCOPE_IDENTITY();
INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
VALUES (@grp, @exam, @sec, @skill, N'Legacy stem one.', 'A2', 'mcq', 1),
       (@grp, @exam, @sec, @skill, N'Legacy stem two.', 'A2', 'mcq', 2),
       (@grp, @exam, @sec, @skill, N'Legacy stem three.', 'A2', 'mcq', 3),
       (@grp, @exam, @sec, @skill, N'Legacy stem four.', 'A2', 'mcq', 4),
       (@grp, @exam, @sec, @skill, N'Legacy stem five.', 'A2', 'mcq', 5);
DECLARE @q1 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 1);
DECLARE @q2 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 2);
-- one question per refusal below, because UQ_AttemptAnswers makes (attempt, question) unique: reusing a
-- question would make the duplicate key win and the guard under test never run
DECLARE @q3 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 3);
DECLARE @q4 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 4);
DECLARE @q5 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 5);
INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
  (@q1, 'mcq', 'A', N'a', 0), (@q1, 'mcq', 'B', N'b', 1), (@q1, 'mcq', 'C', N'c', 0), (@q1, 'mcq', 'D', N'd', 0),
  (@q2, 'mcq', 'A', N'a', 1), (@q2, 'mcq', 'B', N'b', 0), (@q2, 'mcq', 'C', N'c', 0), (@q2, 'mcq', 'D', N'd', 0);

INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching');
DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-DET');

INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus) VALUES
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active'),
  ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active');
DECLARE @item1 INT = (SELECT MIN(ItemId) FROM Items);
DECLARE @item2 INT = (SELECT MAX(ItemId) FROM Items);

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel) VALUES
  (@item1, 1, N'Pinned stem one.', N'a', N'b', N'c', N'd', 'C'),
  (@item2, 1, N'Pinned stem two.', N'a', N'b', N'c', N'd', 'A');
DECLARE @v1 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item1);
DECLARE @v2 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item2);

INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder) VALUES
  (@exam, @sec, @cert, @v1, @item1, 'reading', 1),
  (@exam, @sec, @cert, @v2, @item2, 'reading', 2);

INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @att INT = SCOPE_IDENTITY();

-- ---------------------------------------------------------------------------------------------
-- 2. the happy path: an answer pins the version the binding pinned
-- ---------------------------------------------------------------------------------------------
INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, ItemId, ItemVersionId, SelectedLabel, IsCorrect)
VALUES (@att, @q1, @exam, @item1, @v1, 'C', 1);

IF (SELECT ItemVersionId FROM AttemptAnswers WHERE AttemptId = @att AND QuestionId = @q1) <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: a pinned answer did not store the version it was given', 1;
IF (SELECT SelectedLabel FROM AttemptAnswers WHERE AttemptId = @att AND QuestionId = @q1) <> 'C'
  THROW 50000, 'EXPECT_REJECT FAILED: a pinned answer did not store its label', 1;
PRINT 'an answer pins the item version and the label it recorded';

-- the item is re-versioned, and the answer goes on naming version 1 - copy-on-write is what makes a
-- past attempt renderable after a correction
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@item1, 2, N'Pinned stem one, corrected.', N'a', N'b', N'c', N'd', 'A');
IF (SELECT ItemVersionId FROM AttemptAnswers WHERE AttemptId = @att AND QuestionId = @q1) <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: re-versioning an item moved an answer off the version it sat', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the re-versioned item did not keep both versions', 1;
PRINT 'a re-versioned item leaves the pinned answer and both versions intact';

-- ---------------------------------------------------------------------------------------------
-- 3. an answer with no pool pin is still legal: every paper #231 has not reached has none
-- ---------------------------------------------------------------------------------------------
INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, ItemId, ItemVersionId, SelectedOptionId)
SELECT @att, @q2, @exam, NULL, NULL, OptionId FROM QuestionOptions WHERE QuestionId = @q2 AND IsCorrect = 1;
PRINT 'accepted an answer with no pool pin, because an unbind paper has none';

-- ---------------------------------------------------------------------------------------------
-- 4. the version must be a version of the item the answer names
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, ItemId, ItemVersionId)
  VALUES (@att, @q3, @exam, @item2, @v1);
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
-- 5. half a pair is refused, in both directions. MATCH SIMPLE would let this through the composite
--    foreign key, which is why the CHECK exists.
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, ItemId, ItemVersionId)
  VALUES (@att, @q4, @exam, @item2, NULL);
  THROW 50000, 'EXPECT_REJECT FAILED: an answer named an item with no version', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_AttemptAnswers_ItemPair%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the item-only pair was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an item with no version, by CK_AttemptAnswers_ItemPair';
END CATCH

BEGIN TRY
  INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, ItemId, ItemVersionId)
  VALUES (@att, @q5, @exam, NULL, @v2);
  THROW 50000, 'EXPECT_REJECT FAILED: an answer named a version with no item', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_AttemptAnswers_ItemPair%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the version-only pair was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a version with no item, by CK_AttemptAnswers_ItemPair';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 6. a label outside A-D is not an answer
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  UPDATE AttemptAnswers SET SelectedLabel = 'E' WHERE AttemptId = @att AND QuestionId = @q1;
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
-- 7. the count, once more: no refusal above left a row behind
-- ---------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM AttemptAnswers WHERE AttemptId = @att) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a refused answer left a row behind', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a refusal above created or removed a version', 1;
PRINT 'the pin stands, every guard fired by name, and no refusal left a row behind';
