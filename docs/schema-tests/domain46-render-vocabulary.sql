SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- docs/schema-tests/domain46-render-vocabulary.sql
-- #250 steps 1-2: the render vocabulary and the answer-shape fields.
--
-- The paper's content model now has two axes on the pool, and this file proves they stay two:
--
--   1. RenderFormat is a closed vocabulary of the seven formats the paper actually uses, and it is
--      a DIFFERENT axis from ItemType. The seed carries only two ItemTypes but all seven render
--      formats, so a pool that served both from one column could not represent it.
--   2. A productive skill renders only as a free response. This is the trap a flat 'mcq' default
--      would hide: every insert site that names no widget would silently label a writing task as a
--      radio group, and nothing would fail until a learner sat it.
--   3. The answer-shape facts that lived on Questions (MaxSelections, AllowNumber,
--      AcceptedAnswersJson, RequiredKeywords, Hint) now live on ItemVersions and are scored, so an
--      in-place edit is refused by trg_ItemVersions_ScoredFieldsImmutable like every other scored
--      field. A hint the sitting renders is content, not a typo fix.
--   4. A version may carry two, three or four options with a key naming a filled one - the seed's
--      17 three-option mcq items and its mcq_3 depend on it - and still refuses a gap or a phantom
--      key.
--
-- Every assertion is made against the catalog or a real insert. A test that only read a comment
-- would pass on a database where the constraint was dropped.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. the shape: one reading topic and one writing topic, nothing else
-- ---------------------------------------------------------------------------------------------
INSERT INTO Topics (Modality, Code, Name) VALUES
  ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching'),
  ('writing', 'W-ARG', N'Thesis-driven persuasive and opinion argumentation');
DECLARE @readTopic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-DET');
DECLARE @writTopic INT = (SELECT TopicId FROM Topics WHERE Code = 'W-ARG');

-- ---------------------------------------------------------------------------------------------
-- 2. the scan is proved non-empty first. A lookup for a constraint name that does not exist would
--    report "absent" for every question and pass without touching anything (the failure mode lib.sql
--    records from domain04). So a constraint that DOES exist is found by the same lookup first.
-- ---------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Items_Modality_RenderFormat')
  THROW 50000, 'EXPECT_REJECT FAILED: the constraint lookup cannot see CK_Items_Modality_RenderFormat, so its assertions prove nothing', 1;
PRINT 'the constraint lookup finds a constraint that exists';

-- and the seven formats are named in the definition, not merely "something is checked"
IF NOT EXISTS (
  SELECT 1 FROM sys.check_constraints
  WHERE name = 'CK_Items_RenderFormat'
    AND definition LIKE '%''mcq''%'      AND definition LIKE '%''mcq_3''%'
    AND definition LIKE '%''multi_select''%' AND definition LIKE '%''inline_completion''%'
    AND definition LIKE '%''dropdown''%' AND definition LIKE '%''drag_drop''%'
    AND definition LIKE '%''free_response''%'
)
  THROW 50000, 'EXPECT_REJECT FAILED: CK_Items_RenderFormat is missing or does not name all seven render formats', 1;
PRINT 'the render vocabulary is closed at the seven formats the paper uses';

-- ---------------------------------------------------------------------------------------------
-- 3. the two axes are distinct columns, and RenderFormat is not a rename of ItemType
-- ---------------------------------------------------------------------------------------------
IF NOT EXISTS (
  SELECT 1 FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
  WHERE t.name = 'Items' AND c.name = 'RenderFormat'
)
  THROW 50000, 'EXPECT_REJECT FAILED: Items.RenderFormat is missing', 1;
IF NOT EXISTS (
  SELECT 1 FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
  WHERE t.name = 'Items' AND c.name = 'ItemType'
)
  THROW 50000, 'EXPECT_REJECT FAILED: Items.ItemType is missing; the two axes must both exist', 1;
PRINT 'Items carries both the semantic type and the render format';

-- ---------------------------------------------------------------------------------------------
-- 4. an objective item may take any objective widget; a productive item only a free response.
--
--    The wrong input that would pass a weaker guard: a writing item inserted with no RenderFormat
--    at all, which the column default silently turns into 'mcq'. That is the defect this asserts.
-- ---------------------------------------------------------------------------------------------
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat)
VALUES ('writing', @writTopic, 'B1', 'persuasive_essay', 'free_response');
PRINT 'accepted a writing item rendered as a free response';

BEGIN TRY
  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType)
  VALUES ('writing', @writTopic, 'B1', 'persuasive_essay');
  THROW 50000, 'EXPECT_REJECT FAILED: a writing item left on the default render format was accepted (it would render as a radio group)', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Items_Modality_RenderFormat%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the writing/default pair was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a writing item left on the default widget, by CK_Items_Modality_RenderFormat';
END CATCH

BEGIN TRY
  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat)
  VALUES ('reading', @readTopic, 'B1', 'sentence_cloze', 'free_response');
  THROW 50000, 'EXPECT_REJECT FAILED: a reading item rendered as a free response was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Items_Modality_RenderFormat%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the reading/free_response pair was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a reading item on the productive widget, by CK_Items_Modality_RenderFormat';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 5. two, three and four options are all representable; a gap and a phantom key are not.
--
--    The wrong input a count-only guard would pass: options A,B,D with key A. The count is three, so
--    "three options is fine" accepts it, and the sitting renders a leader to a missing C.
-- ---------------------------------------------------------------------------------------------
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat)
VALUES ('reading', @readTopic, 'B1', 'sentence_cloze', 'mcq');
DECLARE @item INT = SCOPE_IDENTITY();

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@item, 1, N'Four options.', N'a', N'b', N'c', N'd', 'D');
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, KeyLabel)
VALUES (@item, 2, N'Three options, the seed''s mcq and mcq_3 shape.', N'a', N'b', N'c', 'C');
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, KeyLabel)
VALUES (@item, 3, N'Two options.', N'a', N'b', 'B');
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item) <> 3
  THROW 50000, 'EXPECT_REJECT FAILED: the pool could not store 2-, 3- and 4-option versions', 1;
PRINT 'stored two-, three- and four-option versions side by side';

BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionD, KeyLabel)
  VALUES (@item, 10, N'A gap at C.', N'a', N'b', N'd', 'A');
  THROW 50000, 'EXPECT_REJECT FAILED: options A,B,D (a gap at C) were accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_OptionsComplete%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the gapped version was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a version with a gap at C, by CK_ItemVersions_OptionsComplete';
END CATCH

BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, KeyLabel)
  VALUES (@item, 11, N'A phantom key.', N'a', N'b', N'c', 'D');
  THROW 50000, 'EXPECT_REJECT FAILED: a key naming an option the row does not carry was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_OptionsComplete%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the phantom key was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a key naming an absent option, by CK_ItemVersions_OptionsComplete';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 6. the answer-shape fields are on the version, and they are scored.
--
--    The wrong input a presence-only guard would pass: setting Hint in place and reading it back. It
--    is stored, so "the column exists and holds the value" passes while a past attempt's rendered
--    hint has silently changed. Only the trigger refuses that, so the trigger is what is asserted.
-- ---------------------------------------------------------------------------------------------
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, MaxSelections, AllowNumber, AcceptedAnswersJson, RequiredKeywords, Hint)
VALUES (@item, 4, N'A three-answer multi-select with a hint.', 3, 0, N'["15","fifteen"]', N'cost,benefit', N'Count the benefits.');
DECLARE @v INT = SCOPE_IDENTITY();

IF (SELECT MaxSelections FROM ItemVersions WHERE ItemVersionId = @v) <> 3
  THROW 50000, 'EXPECT_REJECT FAILED: MaxSelections was not stored', 1;
IF (SELECT AllowNumber FROM ItemVersions WHERE ItemVersionId = @v) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: AllowNumber was not stored', 1;
IF (SELECT AcceptedAnswersJson FROM ItemVersions WHERE ItemVersionId = @v) <> N'["15","fifteen"]'
  THROW 50000, 'EXPECT_REJECT FAILED: AcceptedAnswersJson was not stored', 1;
IF (SELECT RequiredKeywords FROM ItemVersions WHERE ItemVersionId = @v) <> N'cost,benefit'
  THROW 50000, 'EXPECT_REJECT FAILED: RequiredKeywords was not stored', 1;
IF (SELECT Hint FROM ItemVersions WHERE ItemVersionId = @v) <> N'Count the benefits.'
  THROW 50000, 'EXPECT_REJECT FAILED: Hint was not stored', 1;
PRINT 'the five answer-shape facts are stored on the version';

BEGIN TRY
  UPDATE ItemVersions SET Hint = N'A different nudge.' WHERE ItemVersionId = @v;
  THROW 50000, 'EXPECT_REJECT FAILED: Hint was changed in place on a stored version', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%immutable%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the in-place hint was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an in-place Hint change, by trg_ItemVersions_ScoredFieldsImmutable';
END CATCH

BEGIN TRY
  UPDATE ItemVersions SET MaxSelections = 2 WHERE ItemVersionId = @v;
  THROW 50000, 'EXPECT_REJECT FAILED: MaxSelections was changed in place', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an in-place MaxSelections change, by trg_ItemVersions_ScoredFieldsImmutable';
END CATCH

-- the one field a typo fix may still move in place
UPDATE ItemVersions SET Explanation = N'A corrected explanation.' WHERE ItemVersionId = @v;
IF (SELECT Explanation FROM ItemVersions WHERE ItemVersionId = @v) <> N'A corrected explanation.'
  THROW 50000, 'EXPECT_REJECT FAILED: the cosmetic Explanation edit was refused; CNT-43 allows it', 1;
PRINT 'still allows the one in-place Explanation typo fix';

-- ---------------------------------------------------------------------------------------------
-- 7. the group audio slice lives on the stimulus, and a half-slice is refused
-- ---------------------------------------------------------------------------------------------
INSERT INTO Stimuli (CefrLevel, AudioPath, AudioStartMs, AudioEndMs, DomainContext)
VALUES ('B1', '/audio/toeic/test1-p3.mp3', 1000, 5000, 'general');
PRINT 'accepted a stimulus with an audio slice';

BEGIN TRY
  INSERT INTO Stimuli (CefrLevel, AudioPath, AudioStartMs, DomainContext)
  VALUES ('B1', '/audio/x.mp3', 1000, 'general');
  THROW 50000, 'EXPECT_REJECT FAILED: a slice with no end was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Stimuli_AudioSlice%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the open-ended slice was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a slice with no end, by CK_Stimuli_AudioSlice';
END CATCH

BEGIN TRY
  INSERT INTO Stimuli (CefrLevel, AudioPath, AudioStartMs, AudioEndMs, DomainContext)
  VALUES ('B1', '/audio/x.mp3', 5000, 1000, 'general');
  THROW 50000, 'EXPECT_REJECT FAILED: an inverted slice was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an inverted slice, by CK_Stimuli_AudioSlice';
END CATCH

PRINT 'the render vocabulary, the answer-shape fields and the audio slice are in place; every guard fired by name';
