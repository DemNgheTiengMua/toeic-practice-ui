SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #187: practice reads through the pool.
--
-- The half a constraint can express is here; the half only a service can show (a re-versioned item
-- leaves a finished session's summary unchanged) is in
-- tests/Services.Tests/PracticePoolTests.cs.
--
--   1. a session may name a cell instead of a section, and carries its own certificate either way
--   2. a session cannot name half a cell, or a section and a cell at once (CK_PracticeSessions_Shape)
--   3. a pool answer pins an item and a version, and survives a re-versioned item
--   4. a pool answer cannot name a version of an item other than the one it claims
--      (FK_PracticeAnswers_VersionItem)
--   5. half a pool pair cannot be written at all: ItemId and ItemVersionId are NOT NULL (#269)
--   6. a pool answer cannot name a label outside A-D (CK_PracticeAnswers_SelectedLabel)
--   7. a session holds one answer per item version (UQ_PracticeAnswers_Version)
--
-- Every CATCH checks the specific guard by name: a catch that proves only "something failed" cannot
-- tell a working guard from a typo in this file.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. the shape: one certificate, one reading section, two pool items, one learner
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('PPOOL', N'Pool practice', 1);
DECLARE @cert INT = SCOPE_IDENTITY();

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'READ', N'Reading', 'reading', 1);
DECLARE @skill INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 2, 1);
DECLARE @sec INT = SCOPE_IDENTITY();

INSERT INTO Users (RoleId, Email, PasswordHash, FullName) VALUES (@role, 'ppool@x.com', 0x01, N'Pool Learner');
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GRAM', N'Morphosyntactic structure and lexical precision');
DECLARE @topic INT = SCOPE_IDENTITY();

INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag) VALUES
  ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', 'ppool-a'),
  ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', 'ppool-b');
DECLARE @item1 INT = (SELECT ItemId FROM Items WHERE BatchTag = 'ppool-a');
DECLARE @item2 INT = (SELECT ItemId FROM Items WHERE BatchTag = 'ppool-b');

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation) VALUES
  (@item1, 1, N'Pool stem one.', N'one-a', N'one-b', N'one-c', N'one-d', 'C', N'Pool explanation one.'),
  (@item2, 1, N'Pool stem two.', N'two-a', N'two-b', N'two-c', N'two-d', 'A', N'Pool explanation two.');
DECLARE @v1 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item1);
DECLARE @v2 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item2);

-- the legacy shape still works, unchanged: a section session with its own certificate
INSERT INTO PracticeSessions (UserId, CertId, SectionId, Modality, QuestionCount, Status)
VALUES (@user, @cert, @sec, 'reading', 1, 'in_progress');
PRINT 'accepted a section session that names its own certificate';

-- ---------------------------------------------------------------------------------------------
-- 2. a pool session names a cell, no section, and no exam anywhere
-- ---------------------------------------------------------------------------------------------
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status)
VALUES (@user, @cert, @topic, 'B1', 'reading', 2, 'in_progress');
DECLARE @psess INT = SCOPE_IDENTITY();
IF (SELECT COUNT(*) FROM PracticeSessions WHERE SessionId = @psess AND SectionId IS NULL) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: a pool session did not store with a null section', 1;
PRINT 'accepted a pool session with a cell and no section';

BEGIN TRY
  INSERT INTO PracticeSessions (UserId, CertId, TopicId, Modality, QuestionCount, Status)
  VALUES (@user, @cert, @topic, 'reading', 1, 'in_progress');
  THROW 50000, 'EXPECT_REJECT FAILED: a cell with no level was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_PracticeSessions_Shape%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the half cell was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a cell with no level, by CK_PracticeSessions_Shape';
END CATCH

BEGIN TRY
  INSERT INTO PracticeSessions (UserId, CertId, SectionId, TopicId, CefrLevel, Modality, QuestionCount, Status)
  VALUES (@user, @cert, @sec, @topic, 'B1', 'reading', 1, 'in_progress');
  THROW 50000, 'EXPECT_REJECT FAILED: a session naming a section and a cell was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_PracticeSessions_Shape%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the two-shape session was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a session naming both a section and a cell, by CK_PracticeSessions_Shape';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 3. a pool answer pins the version, and a re-versioned item leaves it alone
-- ---------------------------------------------------------------------------------------------
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, DisplayOrder)
VALUES (@psess, @item1, @v1, 'C', 1, 1);

IF (SELECT ItemVersionId FROM PracticeAnswers WHERE SessionId = @psess AND ItemId = @item1) <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: a pool answer did not store the version it was given', 1;
IF (SELECT SelectedLabel FROM PracticeAnswers WHERE SessionId = @psess AND ItemId = @item1) <> 'C'
  THROW 50000, 'EXPECT_REJECT FAILED: a pool answer did not store its label', 1;
PRINT 'a pool answer pins the item version and the label it recorded';

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
VALUES (@item1, 2, N'Pool stem one, corrected.', N'one-a', N'one-b', N'one-c', N'one-d', 'A', N'Corrected.');
IF (SELECT ItemVersionId FROM PracticeAnswers WHERE SessionId = @psess AND ItemId = @item1) <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: re-versioning an item moved a practice answer off the version it sat', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the re-versioned item did not keep both versions', 1;
PRINT 'a re-versioned item leaves the pinned practice answer and both versions intact';

-- ---------------------------------------------------------------------------------------------
-- 4. the version must be a version of the item the answer names
--
-- A session of its own, because UQ_PracticeAnswers_Version would otherwise refuse the row first and
-- the foreign key under test would never run.
-- ---------------------------------------------------------------------------------------------
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status)
VALUES (@user, @cert, @topic, 'B1', 'reading', 1, 'in_progress');
DECLARE @mismatchSess INT = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, DisplayOrder)
  VALUES (@mismatchSess, @item2, @v1, 'A', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an answer named one item and another item''s version', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_PracticeAnswers_VersionItem%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the mismatched version was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a pool answer naming one item and another item''s version, by FK_PracticeAnswers_VersionItem';
END CATCH

IF (SELECT COUNT(*) FROM PracticeAnswers WHERE SessionId = @mismatchSess) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: a refused mismatched answer left a row behind', 1;

-- ---------------------------------------------------------------------------------------------
-- 5. the pool pair is fully required: half a pair cannot be written at all
--
-- #269: ItemId and ItemVersionId are NOT NULL now, so SQL Server refuses half a pair with its own
-- NOT NULL error BEFORE CK_PracticeAnswers_Shape is consulted. The CHECK's remaining job is to keep
-- the card arm and the item arm apart, which domain40 owns (a lesson micro-check names a CardId and
-- nothing else). What is asserted here is that the columns admit no half pair.
--
-- #269: the guard is now CK_PracticeAnswers_Shape, not a NOT NULL. ItemId and ItemVersionId had to
-- become nullable again because the CARD arm requires both to be NULL - and a NOT NULL column makes that
-- arm unreachable, which broke lesson micro-check grading at runtime. So a half pair is refused by the
-- CHECK rather than by the column. The rule is identical; only which guard states it changed.
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO PracticeAnswers (SessionId, ItemId, SelectedLabel, DisplayOrder)
  VALUES (@psess, @item2, 'A', 3);
  THROW 50000, 'EXPECT_REJECT FAILED: an item with no version was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_PracticeAnswers_Shape%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the item-only pair was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an item with no version, by CK_PracticeAnswers_Shape';
END CATCH

BEGIN TRY
  INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, DisplayOrder)
  VALUES (@psess, NULL, @v2, 'A', 4);
  THROW 50000, 'EXPECT_REJECT FAILED: a version with no item was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_PracticeAnswers_Shape%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the version-only pair was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a version with no item, by CK_PracticeAnswers_Shape';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 6. a label outside A-D is not an answer
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  UPDATE PracticeAnswers SET SelectedLabel = 'E' WHERE SessionId = @psess AND ItemId = @item1;
  THROW 50000, 'EXPECT_REJECT FAILED: label E was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_PracticeAnswers_SelectedLabel%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the bad label was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected label E, by CK_PracticeAnswers_SelectedLabel';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 7. one answer per item version per session.
--
-- #269: UQ_PracticeAnswers_Question and the "one per question on the legacy path" arm are GONE with
-- the legacy shape - there is no second shape left to have a second uniqueness rule. That assertion is
-- deleted, not weakened: the legacy answer it inserted names a Questions row that no longer exists.
-- ---------------------------------------------------------------------------------------------
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, DisplayOrder)
VALUES (@psess, @item2, @v2, 'A', 2);
PRINT 'accepted a second, different item version in the same session';

BEGIN TRY
  INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, DisplayOrder)
  VALUES (@psess, @item2, @v2, 'B', 3);
  THROW 50000, 'EXPECT_REJECT FAILED: the same item version was answered twice in one session', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UQ_PracticeAnswers_Version%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the duplicate version was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a second answer to the same item version, by UQ_PracticeAnswers_Version';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 8. the count, once more: no refusal above left a row behind
-- ---------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM PracticeAnswers WHERE SessionId = @psess) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a refused pool answer left a row behind', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a refusal above created or removed a version', 1;
PRINT 'the pin stands, every guard fired by name, and no refusal left a row behind';
