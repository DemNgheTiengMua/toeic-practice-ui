SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #194: gate one's structural half lives on the row it judges.
--
-- Gate one itself is a pure function (BusinessObjects.GateOne) and is tested without a database:
-- "the expected input names every option, its rationale and the cell" is a fixture, not a table. What
-- the database must hold is only what the gate's last two rules need to be answerable:
--
--   1. a rationale beside each option, so "every option carries a rationale" is a column and not a
--      side table a join can lose;
--   2. a rationale only where its option exists, because a distractor rationale on a productive task
--      renders above nothing;
--   3. versioning: the review screen shows a learner the rationale for the option they picked, so
--      rewriting one rewrites what a past attempt renders - the same reason option text is immutable.
--
-- The fourth claim is the one that needs proving rather than asserting: the copy-on-write trigger
-- must compare the new columns, and the one in-place edit it still allows (a typo in Explanation)
-- must still be allowed. A trigger that forgot the new columns is exactly the defect this file
-- exists to catch.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 0. the pool needs no certificate, exam or section: this file is about a row, not a paper
-- ---------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM Certificates) OR EXISTS (SELECT 1 FROM Exams) OR EXISTS (SELECT 1 FROM Sections)
  THROW 50000, 'EXPECT_REJECT FAILED: this file must run on a database with no certificate, exam or section', 1;

INSERT INTO Topics (Modality, Code, Name) VALUES
  ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching'),
  ('writing', 'W-ARG', N'Thesis-driven persuasive and opinion argumentation');
IF @@ROWCOUNT <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the fixture topics were not inserted', 1;

DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-DET');
DECLARE @writingTopic INT = (SELECT TopicId FROM Topics WHERE Modality = 'writing' AND Code = 'W-ARG');
IF @topic IS NULL OR @writingTopic IS NULL
  THROW 50000, 'EXPECT_REJECT FAILED: a fixture topic was not inserted', 1;

DECLARE @item INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier)
VALUES ('reading', @topic, 'B1', 'passage_comprehension', 'demo');
SET @item = SCOPE_IDENTITY();
IF @item IS NULL
  THROW 50000, 'EXPECT_REJECT FAILED: the fixture item was not inserted', 1;

-- ---------------------------------------------------------------------------------------------
-- 1. four options, four rationales, one key. The legal shape.
-- ---------------------------------------------------------------------------------------------
DECLARE @version INT;
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem,
                          OptionA, OptionB, OptionC, OptionD, KeyLabel,
                          RationaleA, RationaleB, RationaleC, RationaleD, Explanation)
VALUES (@item, 1, N'What must every visitor do at the gate?',
        N'The night ledger.', N'The depot shutter.', N'The depot supervisor.', N'The office door.',
        'A',
        N'Correct: the notice names the ledger.',
        N'The passage never mentions a shutter.',
        N'No supervisor appears in the passage.',
        N'A door is not what the notice requires.',
        N'The notice says every visitor signs the night ledger.');
SET @version = SCOPE_IDENTITY();

IF (SELECT COUNT(*) FROM ItemVersions
    WHERE ItemVersionId = @version
      AND RationaleA IS NOT NULL AND RationaleB IS NOT NULL
      AND RationaleC IS NOT NULL AND RationaleD IS NOT NULL) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the per-option rationales were not stored', 1;
PRINT 'stored four options with a rationale beside each';

-- ---------------------------------------------------------------------------------------------
-- 2. a rationale may not exist without its option. A productive task has no options, so a
--    distractor rationale on it explains nothing.
-- ---------------------------------------------------------------------------------------------
DECLARE @itemW INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, RenderFormat)
VALUES ('writing', @writingTopic, 'B2', 'persuasive_essay', 'demo', 'free_response');
SET @itemW = SCOPE_IDENTITY();
IF @itemW IS NULL
  THROW 50000, 'EXPECT_REJECT FAILED: the productive-task fixture item was not inserted', 1;

BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, RationaleA)
  VALUES (@itemW, 1, N'Argue for or against a congestion charge.',
          N'This explains an option that does not exist.');
  THROW 50000, 'EXPECT_REJECT FAILED: a rationale with no option to explain was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_RationalesNeedOptions%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the orphan rationale was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a rationale with no option, by CK_ItemVersions_RationalesNeedOptions';
END CATCH

-- the productive shape itself is still legal
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem)
VALUES (@itemW, 1, N'Argue for or against a congestion charge.');
IF @@ROWCOUNT <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: a productive version without rationales was refused', 1;
PRINT 'accepted a productive version with no options and no rationales';

-- ---------------------------------------------------------------------------------------------
-- 3. a rationale is copy-on-write, like the option it explains.
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  UPDATE ItemVersions SET RationaleB = N'A shutter is mentioned after all.'
  WHERE ItemVersionId = @version;
  THROW 50000, 'EXPECT_REJECT FAILED: an in-place change to a per-option rationale was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%scored field%immutable%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the rationale was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an in-place rationale change, by trg_ItemVersions_ScoredFieldsImmutable';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 4. the one in-place edit the trigger allows is still allowed. Adding a compared column must not
--    turn the explanation typo into a refusal.
-- ---------------------------------------------------------------------------------------------
UPDATE ItemVersions SET Explanation = N'The notice says every visitor signs the night ledger (typo fixed).'
WHERE ItemVersionId = @version;
IF @@ROWCOUNT <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: an in-place explanation typo fix was refused after #194', 1;
IF (SELECT Explanation FROM ItemVersions WHERE ItemVersionId = @version) NOT LIKE '%typo fixed%'
  THROW 50000, 'EXPECT_REJECT FAILED: the explanation typo fix did not land', 1;

-- ... and the rationale is still the one version 1 carried: the refused edit did not half-apply
IF (SELECT RationaleB FROM ItemVersions WHERE ItemVersionId = @version) <> N'The passage never mentions a shutter.'
  THROW 50000, 'EXPECT_REJECT FAILED: the refused rationale change was written anyway', 1;
PRINT 'still accepted an in-place explanation typo fix, and left the rationale alone';

-- ---------------------------------------------------------------------------------------------
-- 5. the approved path for a rationale change is a new version
-- ---------------------------------------------------------------------------------------------
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem,
                          OptionA, OptionB, OptionC, OptionD, KeyLabel,
                          RationaleA, RationaleB, RationaleC, RationaleD, Explanation)
VALUES (@item, 2, N'What must every visitor do at the gate?',
        N'The night ledger.', N'The depot shutter.', N'The depot supervisor.', N'The office door.',
        'A',
        N'Correct: the notice names the ledger.',
        N'A shutter is mentioned after all.',
        N'No supervisor appears in the passage.',
        N'A door is not what the notice requires.',
        N'The notice says every visitor signs the night ledger.');
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a corrected rationale could not be written as a new version', 1;

-- PRN: a learner who already answered still reads version 1's rationale, not version 2's
IF (SELECT RationaleB FROM ItemVersions WHERE ItemVersionId = @version) <> N'The passage never mentions a shutter.'
  THROW 50000, 'EXPECT_REJECT FAILED: inserting version 2 mutated version 1', 1;
PRINT 'accepted a corrected rationale as a new version, and left version 1 intact';

-- ---------------------------------------------------------------------------------------------
-- 6. nothing here needed the old shape
-- ---------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM Certificates) OR EXISTS (SELECT 1 FROM Skills)
   OR EXISTS (SELECT 1 FROM Exams) OR EXISTS (SELECT 1 FROM Sections)
  THROW 50000, 'EXPECT_REJECT FAILED: the rationale tests created a certificate, skill, exam or section', 1;
IF (SELECT COUNT(*) FROM ItemVersions) <> 3
  THROW 50000, 'EXPECT_REJECT FAILED: the refusal tests above left rows behind', 1;
PRINT '#194: rationales are stored beside their options, refused without them, and versioned with them';
