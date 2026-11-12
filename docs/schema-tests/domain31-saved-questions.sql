SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #224 / #269: a saved link pins the item version. There is ONE shape now.
--
-- QuestionId went with the Questions table, so the entire "two shapes are exclusive" section of this
-- file is gone - and named, because each of its four assertions tested something that no longer has a
-- representation:
--   * "a legacy link names its question and no pool column" - no question column exists.
--   * "a link cannot mix the two shapes" - there is no second shape to mix.
--   * "a link cannot half-fill the pool pair" - ItemId and ItemVersionId are NOT NULL, so SQL Server's
--     own NOT NULL refusal is the guard (that is a weaker report than a named CHECK, so it is not
--     dressed up as one below).
--   * "a legacy link carrying a certificate is refused" - CertId is NOT NULL now, and a legacy link is
--     unrepresentable. What survives is the reason the column is required: an item is
--     certificate-agnostic, so the link carries the scope.
-- CK_SavedQuestions_Shape itself was dropped in #269 for the same reason: with one shape there is
-- nothing to keep apart.
--
-- What survives, and is proved here: the pin (the link names the version it saved and a re-versioned
-- item leaves it alone), the composite foreign key, and one bookmark per item per learner.
--
-- Every CATCH checks the specific guard by name: a catch that proves only "something failed" cannot
-- tell a working guard from a typo in this file.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 0. the shape: one certificate, one reading topic, two items, one learner
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

-- ---------------------------------------------------------------------------------------------
-- 1. a link pins the item and the version, and a re-versioned item leaves it alone
-- ---------------------------------------------------------------------------------------------
INSERT INTO SavedQuestions (UserId, CertId, ItemId, ItemVersionId, Source)
VALUES (@user, @cert, @item1, @v1, 'manual');

IF (SELECT ItemVersionId FROM SavedQuestions WHERE UserId = @user AND ItemId = @item1) <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: a link did not store the version it was given', 1;
PRINT 'a saved link pins the item version';

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
VALUES (@item1, 2, N'Corrected stem.', N'a', N'b', N'c', N'd', 'A', N'Corrected.');

IF (SELECT ItemVersionId FROM SavedQuestions WHERE UserId = @user AND ItemId = @item1) <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: re-versioning an item moved a saved link off the version it saved', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the re-versioned item did not keep both versions', 1;
PRINT 'a re-versioned item leaves the pinned saved link and both versions intact';

-- ---------------------------------------------------------------------------------------------
-- 2. half a pair is unrepresentable: the columns are NOT NULL, so this is the database's refusal
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO SavedQuestions (UserId, CertId, ItemId, Source) VALUES (@user, @cert, @item2, 'manual');
  THROW 50000, 'EXPECT_REJECT FAILED: an item with no version was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%ItemVersionId%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the item-only pair was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an item with no version, by the NOT NULL on ItemVersionId';
END CATCH

BEGIN TRY
  INSERT INTO SavedQuestions (UserId, ItemId, ItemVersionId, Source) VALUES (@user, @item2, @v2, 'manual');
  THROW 50000, 'EXPECT_REJECT FAILED: a link with no certificate was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CertId%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the certificate-less link was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a link with no certificate, by the NOT NULL on CertId';
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
-- 4. one bookmark per item, and a second item is a second bookmark
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

-- ---------------------------------------------------------------------------------------------
-- 5. the count, once more: no refusal above left a row behind
-- ---------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM SavedQuestions WHERE UserId = @user) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a refusal above left a saved link behind', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a refusal above created or removed a version', 1;
PRINT 'the pin stands, every guard fired by name, and no refusal left a row behind';
