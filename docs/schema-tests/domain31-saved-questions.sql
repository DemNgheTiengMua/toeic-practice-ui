SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #224: a saved link pins the item version, in one of two shapes.
--
-- The half a constraint can express is here; the half only a service can show (a re-versioned item
-- leaves the saved link alone, a retired-defective item is flagged) is in
-- tests/Services.Tests/SavedQuestionServiceTests.cs.
--
--   1. a pool link names its item and version, and survives a re-versioned item
--   2. a legacy link names its question and no pool column
--   3. a link cannot mix the two shapes, nor half-fill the pool pair (CK_SavedQuestions_Shape)
--   4. a link cannot name one item and another item's version (FK_SavedQuestions_VersionItem)
--   5. one bookmark per question and one per item, and a second item is a second bookmark
--
-- Every CATCH checks the specific guard by name: a catch that proves only "something failed" cannot
-- tell a working guard from a typo in this file.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 0. the shape: one certificate, one reading topic, one item with two versions, one learner
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('SQ224', N'Saved links', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'READ', N'Reading', 'reading', 1);
DECLARE @skill INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1);
DECLARE @sec INT = SCOPE_IDENTITY();
INSERT INTO QuestionCategories (CertId, Code, Name) VALUES (@cert, 'sq224-cat', N'Grammar');
DECLARE @cat INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
VALUES (@role, 'sq224@x.com', 0x01, N'Saved Learner', @cert);
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GRAM', N'Morphosyntactic structure and lexical precision');
DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM');

INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag) VALUES
  ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', 'sq224-a'),
  ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', 'sq224-b');
DECLARE @item1 INT = (SELECT ItemId FROM Items WHERE BatchTag = 'sq224-a');
DECLARE @item2 INT = (SELECT ItemId FROM Items WHERE BatchTag = 'sq224-b');

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation) VALUES
  (@item1, 1, N'Original stem.', N'a', N'b', N'c', N'd', 'B', N'Original.'),
  (@item2, 1, N'Second stem.', N'a', N'b', N'c', N'd', 'A', N'Second.');
DECLARE @v1 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item1 AND VersionNumber = 1);
DECLARE @v2 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item2 AND VersionNumber = 1);

-- the legacy shape still works, unchanged: a question-shaped link with no pool column
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
VALUES (@cert, N'Saved links paper', 'draft', 60, 'mock', 1);
DECLARE @exam INT = SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
DECLARE @grp INT = SCOPE_IDENTITY();
INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
VALUES (@grp, @exam, @sec, @skill, @cat, N'Legacy stem.', 1, 'mcq', 1);
DECLARE @q INT = SCOPE_IDENTITY();
INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
VALUES (@grp, @exam, @sec, @skill, @cat, N'Legacy stem two.', 1, 'mcq', 2);
DECLARE @q2 INT = SCOPE_IDENTITY();

INSERT INTO SavedQuestions (UserId, QuestionId, Source) VALUES (@user, @q, 'manual');
PRINT 'accepted a legacy saved link, unchanged';

-- ---------------------------------------------------------------------------------------------
-- 1. a pool link pins the item and the version, and a re-versioned item leaves it alone
-- ---------------------------------------------------------------------------------------------
INSERT INTO SavedQuestions (UserId, CertId, ItemId, ItemVersionId, Source)
VALUES (@user, @cert, @item1, @v1, 'manual');

IF (SELECT ItemVersionId FROM SavedQuestions WHERE UserId = @user AND ItemId = @item1) <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: a pool link did not store the version it was given', 1;
PRINT 'a pool link pins the item version';

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
VALUES (@item1, 2, N'Corrected stem.', N'a', N'b', N'c', N'd', 'A', N'Corrected.');

IF (SELECT ItemVersionId FROM SavedQuestions WHERE UserId = @user AND ItemId = @item1) <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: re-versioning an item moved a saved link off the version it saved', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the re-versioned item did not keep both versions', 1;
PRINT 'a re-versioned item leaves the pinned saved link and both versions intact';

-- ---------------------------------------------------------------------------------------------
-- 2. the two shapes are exclusive: no half pool pair, no row that is both
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO SavedQuestions (UserId, CertId, ItemId, Source) VALUES (@user, @cert, @item2, 'manual');
  THROW 50000, 'EXPECT_REJECT FAILED: an item with no version was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_SavedQuestions_Shape%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the item-only pair was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an item with no version, by CK_SavedQuestions_Shape';
END CATCH

BEGIN TRY
  INSERT INTO SavedQuestions (UserId, ItemId, ItemVersionId, Source) VALUES (@user, @item2, @v2, 'manual');
  THROW 50000, 'EXPECT_REJECT FAILED: a pool link with no certificate was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_SavedQuestions_Shape%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the certificate-less pool link was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a pool link with no certificate, by CK_SavedQuestions_Shape';
END CATCH

BEGIN TRY
  INSERT INTO SavedQuestions (UserId, CertId, QuestionId, ItemId, ItemVersionId, Source)
  VALUES (@user, @cert, @q2, @item2, @v2, 'manual');
  THROW 50000, 'EXPECT_REJECT FAILED: a link carrying both shapes was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_SavedQuestions_Shape%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the two-shape link was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a link carrying both shapes, by CK_SavedQuestions_Shape';
END CATCH

-- a legacy link with a certificate has no meaning either: a question already carries one
BEGIN TRY
  INSERT INTO SavedQuestions (UserId, CertId, QuestionId, Source) VALUES (@user, @cert, @q2, 'manual');
  THROW 50000, 'EXPECT_REJECT FAILED: a legacy link with a certificate was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_SavedQuestions_Shape%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the certificate-bearing legacy link was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a legacy link carrying a certificate, by CK_SavedQuestions_Shape';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 3. the version must be a version of the item the link names
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO SavedQuestions (UserId, CertId, ItemId, ItemVersionId, Source)
  VALUES (@user, @cert, @item2, @v1, 'manual');
  THROW 50000, 'EXPECT_REJECT FAILED: a link named one item and another item''s version', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_SavedQuestions_VersionItem%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the mismatched version was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a link naming one item and another item''s version, by FK_SavedQuestions_VersionItem';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 4. one bookmark per item, one per question, and a second item is a second bookmark
-- ---------------------------------------------------------------------------------------------
INSERT INTO SavedQuestions (UserId, CertId, ItemId, ItemVersionId, Source)
VALUES (@user, @cert, @item2, @v2, 'incorrect');
PRINT 'accepted a second, different item as a second bookmark';

BEGIN TRY
  INSERT INTO SavedQuestions (UserId, CertId, ItemId, ItemVersionId, Source)
  VALUES (@user, @cert, @item2, @v2, 'manual');
  THROW 50000, 'EXPECT_REJECT FAILED: the same item was bookmarked twice', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UQ_SavedQuestions_Item%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the duplicate item was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a second bookmark on the same item, by UQ_SavedQuestions_Item';
END CATCH

BEGIN TRY
  INSERT INTO SavedQuestions (UserId, QuestionId, Source) VALUES (@user, @q, 'incorrect');
  THROW 50000, 'EXPECT_REJECT FAILED: the same question was bookmarked twice', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UQ_SavedQuestions_Question%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the duplicate question was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a second bookmark on the same question, by UQ_SavedQuestions_Question';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 5. the count, once more: no refusal above left a row behind
-- ---------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM SavedQuestions WHERE UserId = @user) <> 3
  THROW 50000, 'EXPECT_REJECT FAILED: a refusal above left a saved link behind', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a refusal above created or removed a version', 1;
PRINT 'the pin stands, every guard fired by name, and no refusal left a row behind';
