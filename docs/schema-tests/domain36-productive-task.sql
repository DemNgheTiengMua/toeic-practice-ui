SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #204 / S-07: a productive task's output demand and its two timed windows.
--
-- D0.4 states a word range for writing and a preparation window and a recording/composition window
-- for both productive skills. They live on ItemVersions, not on Stimuli, because a two-part writing
-- task carries a range and a budget for EACH part and one pair of columns on the prompt could
-- describe only one of them. This file proves the three things prose cannot:
--
--   1. an undeclared window is NULL, and a declared one is a real window - zero is not "no
--      preparation", and an inverted or negative window is a sitting the learner cannot finish;
--   2. a word range is a range: both ends present, positive, ceiling at or above the floor;
--   3. the four columns are SCORED. A sitting renders them and the under-length penalty reads
--      MinWords, so an in-place edit rewrites what a past attempt was asked for and must be refused
--      by copy-on-write (CNT-43), while a new version row is the approved way to change it.
--
-- The fixture guard is the reason this file exists in this shape: every refusal below checks that
-- the SPECIFIC constraint fired, because a catch that only proves "something failed" cannot tell a
-- working guard from a typo in this file.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. a productive task with a word range and two windows is accepted, and stores them
-- ---------------------------------------------------------------------------------------------
DECLARE @topic INT;
INSERT INTO Topics (Modality, Code, Name) VALUES ('writing','W-ARG',N'Thesis-driven persuasive and opinion argumentation');
SET @topic = SCOPE_IDENTITY();

DECLARE @stim INT;
INSERT INTO Stimuli (CefrLevel, PromptCopy, DomainContext)
VALUES ('B1', N'Write an essay answering the question above.', 'general');
SET @stim = SCOPE_IDENTITY();

DECLARE @item INT;
INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, RenderFormat)
VALUES (@stim, 'writing', @topic, 'B1', 'persuasive_essay', 'demo', 'draft', 'free_response');
SET @item = SCOPE_IDENTITY();

DECLARE @v INT;
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, MinWords, MaxWords, PrepSeconds, ResponseSeconds)
VALUES (@item, 1, N'Write a persuasive essay of 110-150 words.', 110, 150, 300, 900);
SET @v = SCOPE_IDENTITY();

IF (SELECT MinWords FROM ItemVersions WHERE ItemVersionId = @v) <> 110
  THROW 50000, 'EXPECT_REJECT FAILED: the version did not keep its minimum word count', 1;
IF (SELECT MaxWords FROM ItemVersions WHERE ItemVersionId = @v) <> 150
  THROW 50000, 'EXPECT_REJECT FAILED: the version did not keep its maximum word count', 1;
IF (SELECT PrepSeconds FROM ItemVersions WHERE ItemVersionId = @v) <> 300
  THROW 50000, 'EXPECT_REJECT FAILED: the version did not keep its preparation window', 1;
IF (SELECT ResponseSeconds FROM ItemVersions WHERE ItemVersionId = @v) <> 900
  THROW 50000, 'EXPECT_REJECT FAILED: the version did not keep its response window', 1;
PRINT 'a productive task stores its word range and its two windows';

-- ---------------------------------------------------------------------------------------------
-- 2. an objective item is unchanged: both are NULL, which is "no output demand", not zero
-- ---------------------------------------------------------------------------------------------
DECLARE @readTopic INT;
INSERT INTO Topics (Modality, Code, Name) VALUES ('reading','R-DET',N'Local factual retrieval and paraphrase matching');
SET @readTopic = SCOPE_IDENTITY();

DECLARE @readItem INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus)
VALUES ('reading', @readTopic, 'B1', 'passage_comprehension', 'demo', 'draft');
SET @readItem = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@readItem, 1, N'What time does the depot open?', N'Six', N'Seven', N'Eight', N'Nine', 'A');

IF EXISTS (SELECT 1 FROM ItemVersions WHERE ItemId = @readItem
           AND (MinWords IS NOT NULL OR MaxWords IS NOT NULL
                OR PrepSeconds IS NOT NULL OR ResponseSeconds IS NOT NULL))
  THROW 50000, 'EXPECT_REJECT FAILED: an objective item carries a productive task''s columns', 1;
PRINT 'an objective item carries no word range and no windows';

-- ---------------------------------------------------------------------------------------------
-- 3. the wrong-but-passing inputs for the word range
-- ---------------------------------------------------------------------------------------------
-- 3a. a floor with no ceiling. "MinWords 110" alone reads as "at least 110", which is a demand the
--     pool can serve but a RANGE it cannot: the live word counter needs both ends to draw a target.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, MinWords)
  VALUES (@item, 2, N'A floor with no ceiling.', 110);
  THROW 50000, 'EXPECT_REJECT FAILED: a word floor with no ceiling was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_WordRange%'
    THROW 50000, 'EXPECT_REJECT FAILED: the range was refused by the wrong guard', 1;
  PRINT 'rejected a word floor with no ceiling, by CK_ItemVersions_WordRange';
END CATCH

-- 3b. an inverted range: the ceiling below the floor is a target no answer can reach.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, MinWords, MaxWords)
  VALUES (@item, 2, N'A ceiling below its floor.', 150, 110);
  THROW 50000, 'EXPECT_REJECT FAILED: an inverted word range was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_WordRange%'
    THROW 50000, 'EXPECT_REJECT FAILED: the inverted range was refused by the wrong guard', 1;
  PRINT 'rejected a ceiling below its floor, by CK_ItemVersions_WordRange';
END CATCH

-- 3c. a zero floor. A task demanding "0 to 150 words" demands nothing, and the under-length penalty
--     reads it as satisfied by an empty answer.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, MinWords, MaxWords)
  VALUES (@item, 2, N'A word range that demands nothing.', 0, 150);
  THROW 50000, 'EXPECT_REJECT FAILED: a zero word floor was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_WordRange%'
    THROW 50000, 'EXPECT_REJECT FAILED: the zero floor was refused by the wrong guard', 1;
  PRINT 'rejected a zero word floor, by CK_ItemVersions_WordRange';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 4. the wrong-but-passing inputs for the two windows
-- ---------------------------------------------------------------------------------------------
-- 4a. a zero preparation window. NULL already means "none"; a zero says "none" in the one value the
--     countdown renders as "" and the sitting reads as "preparation finished" before it started.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, PrepSeconds)
  VALUES (@item, 2, N'A zero preparation window.', 0);
  THROW 50000, 'EXPECT_REJECT FAILED: a zero preparation window was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_Timing%'
    THROW 50000, 'EXPECT_REJECT FAILED: the zero preparation window was refused by the wrong guard', 1;
  PRINT 'rejected a zero preparation window, by CK_ItemVersions_Timing';
END CATCH

-- 4b. a zero recording window. A speaking task nobody can record is not a speaking task.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, ResponseSeconds)
  VALUES (@item, 2, N'A zero recording window.', 0);
  THROW 50000, 'EXPECT_REJECT FAILED: a zero recording window was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_Timing%'
    THROW 50000, 'EXPECT_REJECT FAILED: the zero recording window was refused by the wrong guard', 1;
  PRINT 'rejected a zero recording window, by CK_ItemVersions_Timing';
END CATCH

-- 4c. a negative window. The same guard, from the other side, and the side a "greater than zero"
--     check written as "= 0" would let through.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, ResponseSeconds)
  VALUES (@item, 2, N'A negative recording window.', -30);
  THROW 50000, 'EXPECT_REJECT FAILED: a negative recording window was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_Timing%'
    THROW 50000, 'EXPECT_REJECT FAILED: the negative window was refused by the wrong guard', 1;
  PRINT 'rejected a negative recording window, by CK_ItemVersions_Timing';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 5. the four columns are SCORED: copy-on-write refuses an in-place move (CNT-43)
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  UPDATE ItemVersions SET MaxWords = 400 WHERE ItemVersionId = @v;
  THROW 50000, 'EXPECT_REJECT FAILED: an in-place move of the word range was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%scored field is immutable%'
    THROW 50000, 'EXPECT_REJECT FAILED: the in-place word-range move was refused by the wrong guard', 1;
  PRINT 'rejected an in-place word-range move, by trg_ItemVersions_ScoredFieldsImmutable';
END CATCH

DECLARE @guardWrong2 NVARCHAR(2048);
BEGIN TRY
  UPDATE ItemVersions SET ResponseSeconds = 60 WHERE ItemVersionId = @v;
  THROW 50000, 'EXPECT_REJECT FAILED: an in-place move of the recording window was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%scored field is immutable%'
    THROW 50000, 'EXPECT_REJECT FAILED: the in-place window move was refused by the wrong guard', 1;
  PRINT 'rejected an in-place recording-window move, by trg_ItemVersions_ScoredFieldsImmutable';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 6. and a new version row is the approved way to change it
-- ---------------------------------------------------------------------------------------------
DECLARE @v2 INT;
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, MinWords, MaxWords, PrepSeconds, ResponseSeconds)
VALUES (@item, 2, N'Write a persuasive essay of 180-250 words.', 180, 250, 300, 1200);
SET @v2 = SCOPE_IDENTITY();

IF (SELECT MaxWords FROM ItemVersions WHERE ItemVersionId = @v) <> 150
  THROW 50000, 'EXPECT_REJECT FAILED: the earlier version lost its own word ceiling', 1;
IF (SELECT MaxWords FROM ItemVersions WHERE ItemVersionId = @v2) <> 250
  THROW 50000, 'EXPECT_REJECT FAILED: the new version did not carry the new word ceiling', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a scored change did not fork a version', 1;
PRINT 'a scored change forks a version, and the pinned one keeps its own range';
