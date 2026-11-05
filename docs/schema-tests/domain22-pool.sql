SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #184: the pool - stimulus, item, item version.
--
-- The warehouse stops being an exam's property. Stimuli and Items carry no ExamId, no SectionId
-- and no CertId, and this file proves it the only way prose cannot: it inserts all three with zero
-- certificates, zero skills, zero sections and zero exams in the database.
--
-- It also proves the two guards that make the shape worth having:
--   1. a version is copy-on-write. An in-place edit to any scored field is refused; an explanation
--      typo is allowed; a new version row is accepted.
--   2. a level is authored, so the reportable floor ('pre_a1') is refused here even though a result
--      may report it (BusinessObjects.CefrLevel.TryParseAuthored vs TryParse).
--
-- Every refusal below checks ERROR_MESSAGE() for the specific guard it expects. A catch that only
-- proves "something failed" cannot tell a working constraint from a typo in this file, and this repo
-- has already shipped a test that voted nobody (docs/schema-tests/lib.sql, domain04).

-- THROW takes a variable, not an expression, and the whole file is one batch, so this is declared
-- once. Each CATCH below sets it before deciding whether the guard that fired is the right one.
DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 0. nothing that the old shape required exists yet
-- ---------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM Certificates) OR EXISTS (SELECT 1 FROM Exams) OR EXISTS (SELECT 1 FROM Sections)
  THROW 50000, 'EXPECT_REJECT FAILED: this file must run on a database with no certificate, exam or section, or it proves nothing', 1;
PRINT 'no certificate, exam or section exists';

-- ---------------------------------------------------------------------------------------------
-- 1. the canonical twenty topics, so an item has something canonical to point at
-- ---------------------------------------------------------------------------------------------
INSERT INTO Topics (Modality, Code, Name) VALUES
  ('listening','L-GIST',N'Global gist, purpose, macro-topic'),
  ('listening','L-DET', N'Explicit factual detail and extraction'),
  ('listening','L-INF', N'Pragmatic implication and logical inference'),
  ('listening','L-ATT', N'Speaker stance, attitude, prosodic emotion'),
  ('listening','L-FUNC',N'Communicative function and speech acts'),
  ('listening','L-ORG', N'Discourse structure, signposting, synthesis'),
  ('reading',  'R-GIST',N'Global thesis, purpose, paragraph main idea'),
  ('reading',  'R-DET', N'Local factual retrieval and paraphrase matching'),
  ('reading',  'R-INF', N'Bridging inference and logical deduction'),
  ('reading',  'R-ATT', N'Authorial stance, rhetorical purpose, tone'),
  ('reading',  'R-COH', N'Discourse cohesion, reference, text insertion'),
  ('reading',  'R-GRAM',N'Morphosyntactic structure and lexical precision'),
  ('speaking', 'S-DESC',N'Informational description and personal framing'),
  ('speaking', 'S-INT', N'Transactional interaction and option negotiation'),
  ('speaking', 'S-ARG', N'Sustained monologic argumentation and justification'),
  ('speaking', 'S-SYN', N'Integrated summary and visual/data interpretation'),
  ('writing',  'W-CORR',N'Transactional correspondence and register control'),
  ('writing',  'W-DATA',N'Visual data, process, and informational synthesis'),
  ('writing',  'W-ARG', N'Thesis-driven persuasive and opinion argumentation'),
  ('writing',  'W-DISC',N'Multi-perspective problem-solution and evaluation');

IF (SELECT COUNT(*) FROM Topics) <> 20
  THROW 50000, 'EXPECT_REJECT FAILED: the topic vocabulary is not exactly twenty rows', 1;
IF (SELECT COUNT(*) FROM Topics WHERE Modality = 'listening') <> 6 OR
   (SELECT COUNT(*) FROM Topics WHERE Modality = 'reading')   <> 6 OR
   (SELECT COUNT(*) FROM Topics WHERE Modality = 'speaking')  <> 4 OR
   (SELECT COUNT(*) FROM Topics WHERE Modality = 'writing')   <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: the per-modality split is not 6/6/4/4', 1;

-- exactly these twenty codes, no more and no fewer. A count alone would accept twenty rows of the
-- wrong twenty, and the codes are what every item, mastery row and view joins on.
DECLARE @canonical TABLE (Modality VARCHAR(10) NOT NULL, Code VARCHAR(20) NOT NULL);
INSERT INTO @canonical (Modality, Code) VALUES
  ('listening','L-GIST'),('listening','L-DET'),('listening','L-INF'),
  ('listening','L-ATT'), ('listening','L-FUNC'),('listening','L-ORG'),
  ('reading','R-GIST'),  ('reading','R-DET'),  ('reading','R-INF'),
  ('reading','R-ATT'),   ('reading','R-COH'),  ('reading','R-GRAM'),
  ('speaking','S-DESC'), ('speaking','S-INT'), ('speaking','S-ARG'), ('speaking','S-SYN'),
  ('writing','W-CORR'),  ('writing','W-DATA'), ('writing','W-ARG'), ('writing','W-DISC');

IF EXISTS (
  SELECT Modality, Code FROM @canonical
  EXCEPT
  SELECT Modality, Code FROM Topics
)
  THROW 50000, 'EXPECT_REJECT FAILED: a canonical topic is missing from the vocabulary', 1;
IF EXISTS (
  SELECT Modality, Code FROM Topics
  EXCEPT
  SELECT Modality, Code FROM @canonical
)
  THROW 50000, 'EXPECT_REJECT FAILED: the vocabulary holds a topic that is not canonical', 1;
PRINT 'the topic vocabulary is exactly the canonical twenty, with the 6/6/4/4 split';

-- ---------------------------------------------------------------------------------------------
-- 2. a stimulus exists with no certificate. It carries its own level, its passage, its transcript,
--    its domain context and its optional image with alt text.
-- ---------------------------------------------------------------------------------------------
DECLARE @stim INT;
INSERT INTO Stimuli (CefrLevel, Passage, Transcript, DomainContext, ImagePath, ImageAlt, DisplayOrder)
VALUES ('B1',
        N'The depot opens at six. Staff who arrive before then must sign the night ledger.',
        N'Sign the night ledger if you arrive before six.',
        'workplace', '/img/depot-sign.jpg', N'A notice board beside the depot gate.', 1);
SET @stim = SCOPE_IDENTITY();

IF (SELECT COUNT(*) FROM Certificates) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: inserting a stimulus created or required a certificate', 1;
IF (SELECT CefrLevel FROM Stimuli WHERE StimulusId = @stim) <> 'B1'
  THROW 50000, 'EXPECT_REJECT FAILED: the stimulus did not keep its own level', 1;
PRINT 'a stimulus exists with no certificate, exam or section';

-- ---------------------------------------------------------------------------------------------
-- 3. an item exists with no certificate. Skill, topic, level, placement eligibility, tier and
--    lifecycle all present; no ExamId, no SectionId, no CertId.
-- ---------------------------------------------------------------------------------------------
DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-DET');
DECLARE @item INT;
INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
VALUES (@stim, 'reading', @topic, 'B1', 'passage_comprehension', 1, 'demo', 'draft', 'batch-2026-11-a');
SET @item = SCOPE_IDENTITY();

IF (SELECT COUNT(*) FROM Certificates) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: inserting an item created or required a certificate', 1;
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Items' AND COLUMN_NAME IN ('ExamId','SectionId','CertId'))
  THROW 50000, 'EXPECT_REJECT FAILED: Items still carries an exam, section or certificate column', 1;
PRINT 'an item exists with no certificate, exam or section, and has no exam/section/cert column';

-- ---------------------------------------------------------------------------------------------
-- 4. a version holds everything scorable
-- ---------------------------------------------------------------------------------------------
DECLARE @v1 INT;
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel,
                          Explanation, EvidenceStart, EvidenceEnd, EvidenceUnit, EvidenceQuote,
                          ReferenceText, ReferenceKind)
VALUES (@item, 1, N'What must staff who arrive before six do?',
        N'Sign the night ledger.', N'Open the depot gate.', N'Call the supervisor.', N'Leave a note in the office.',
        'A', N'The passage states the requirement directly.',
        0, 33, 'char', N'Staff who arrive before then must sign the night ledger.',
        NULL, NULL);
SET @v1 = SCOPE_IDENTITY();

IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the version was not stored', 1;
PRINT 'a version exists with stem, four options, key, explanation, evidence span and reference text';

-- ---------------------------------------------------------------------------------------------
-- 5. copy-on-write. An explanation typo may be corrected in place.
-- ---------------------------------------------------------------------------------------------
UPDATE ItemVersions SET Explanation = N'The passage states the requirement directly (typo fixed).'
WHERE ItemVersionId = @v1;
IF (SELECT Explanation FROM ItemVersions WHERE ItemVersionId = @v1) NOT LIKE '%typo fixed%'
  THROW 50000, 'EXPECT_REJECT FAILED: an explanation typo could not be corrected in place', 1;
PRINT 'accepted an in-place explanation typo fix';

-- 5a. a scored field may not be edited in place - the stem.
BEGIN TRY
  UPDATE ItemVersions SET Stem = N'What must every visitor do?' WHERE ItemVersionId = @v1;
  THROW 50000, 'EXPECT_REJECT FAILED: an in-place change to a scored field (Stem) was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%scored field%immutable%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: Stem was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an in-place stem change, by the copy-on-write guard';
END CATCH

-- 5b. the key is the most dangerous scored field.
BEGIN TRY
  UPDATE ItemVersions SET KeyLabel = 'C' WHERE ItemVersionId = @v1;
  THROW 50000, 'EXPECT_REJECT FAILED: an in-place change to the key was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%scored field%immutable%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the key was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an in-place key change, by the copy-on-write guard';
END CATCH

-- 5c. option text is scored too.
BEGIN TRY
  UPDATE ItemVersions SET OptionB = N'Lock the depot gate.' WHERE ItemVersionId = @v1;
  THROW 50000, 'EXPECT_REJECT FAILED: an in-place change to option text was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%scored field%immutable%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: option text was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an in-place option change, by the copy-on-write guard';
END CATCH

-- 5d. the guard must not block the copy: a new version row is the approved path.
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@item, 2, N'What must staff who arrive before six do?',
        N'Sign the night ledger.', N'Lock the depot gate.', N'Call the supervisor.', N'Leave a note.', 'A');
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId = @item) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the copy-on-write guard blocked a new version row', 1;
IF (SELECT Stem FROM ItemVersions WHERE ItemVersionId = @v1) <> N'What must staff who arrive before six do?'
  THROW 50000, 'EXPECT_REJECT FAILED: version 1 was mutated by the refused edit', 1;
PRINT 'accepted a new version and left version 1 intact';

-- ---------------------------------------------------------------------------------------------
-- 6. the level is authored. The floor is reportable, never authored.
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType)
  VALUES (@stim, 'reading', @topic, 'pre_a1', 'passage_comprehension');
  THROW 50000, 'EXPECT_REJECT FAILED: an item authored at the reportable floor (pre_a1) was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Items_Level%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: pre_a1 was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an item authored at pre_a1, by CK_Items_Level';
END CATCH

BEGIN TRY
  INSERT INTO Stimuli (CefrLevel, Passage, DomainContext) VALUES ('C3', N'A level that does not exist.', 'general');
  THROW 50000, 'EXPECT_REJECT FAILED: a stimulus at an invented level (C3) was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Stimuli_Level%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: C3 was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a stimulus at an invented level, by CK_Stimuli_Level';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 7. an item's skill must be the skill of its topic. A listening skill on a reading topic is the
--    defect the composite foreign key exists to stop.
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType)
  VALUES (@stim, 'listening', @topic, 'B1', 'short_exchange');
  THROW 50000, 'EXPECT_REJECT FAILED: an item whose skill disagrees with its topic was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_Items_TopicModality%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the skill/topic mismatch was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an item whose skill disagrees with its topic, by FK_Items_TopicModality';
END CATCH

-- a twenty-first topic is still refused, so this file cannot be used to widen the vocabulary
BEGIN TRY
  INSERT INTO Topics (Modality, Code, Name) VALUES ('reading','R-EXTRA',N'An extra topic nobody asked for');
  THROW 50000, 'EXPECT_REJECT FAILED: a twenty-first topic was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%closed set of twenty%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the twenty-first topic was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a twenty-first topic, by trg_Topics_ClosedVocabulary';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 8. a version is all four options and a key, or none of them. Three options measures nothing.
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, KeyLabel)
  VALUES (@item, 3, N'A half-filled item.', N'a', N'b', N'c', 'A');
  THROW 50000, 'EXPECT_REJECT FAILED: a version with three options and a key was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_OptionsComplete%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the half-filled version was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected three options with a key, by CK_ItemVersions_OptionsComplete';
END CATCH

-- a productive task is the other legal shape: no options, no key
DECLARE @itemW INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier)
VALUES ('writing', (SELECT TopicId FROM Topics WHERE Code = 'W-ARG'), 'B2', 'persuasive_essay', 'production');
SET @itemW = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem) VALUES (@itemW, 1, N'Argue for or against a congestion charge.');
PRINT 'accepted a productive version with no options and no key';

-- a key outside the four labels is refused
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
  VALUES (@item, 4, N'Five-label key.', N'a', N'b', N'c', N'd', 'E');
  THROW 50000, 'EXPECT_REJECT FAILED: a key outside A-D was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_KeyLabel%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: key E was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a key outside A-D, by CK_ItemVersions_KeyLabel';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 9. an image carries alt text; the domain context is one of the three; the lifecycle and tier
--    vocabularies are closed; the item type is one of the fourteen.
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO Stimuli (CefrLevel, Passage, DomainContext, ImagePath)
  VALUES ('A2', N'A photo with no description.', 'general', '/img/undescribed.jpg');
  THROW 50000, 'EXPECT_REJECT FAILED: an image without alt text was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Stimuli_ImageAlt%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the undescribed image was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an image without alt text, by CK_Stimuli_ImageAlt';
END CATCH

BEGIN TRY
  INSERT INTO Stimuli (CefrLevel, Passage, DomainContext) VALUES ('A2', N'A passage.', 'military');
  THROW 50000, 'EXPECT_REJECT FAILED: an invented domain context was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Stimuli_Domain%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the invented domain was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an invented domain context, by CK_Stimuli_Domain';
END CATCH

BEGIN TRY
  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, LifecycleStatus)
  VALUES ('reading', @topic, 'B1', 'passage_comprehension', 'approved');
  THROW 50000, 'EXPECT_REJECT FAILED: a lifecycle state outside the six was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Items_Lifecycle%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the unknown lifecycle was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an unknown lifecycle state, by CK_Items_Lifecycle';
END CATCH

BEGIN TRY
  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier)
  VALUES ('reading', @topic, 'B1', 'passage_comprehension', 'pilot');
  THROW 50000, 'EXPECT_REJECT FAILED: a content tier outside production/demo was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Items_ContentTier%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the unknown tier was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an unknown content tier, by CK_Items_ContentTier';
END CATCH

-- D0.4: the item type vocabulary is a (modality, type) matrix, so it is refused by name: 'true_false_notgiven'
-- is not one of the fourteen, and a writing type on a reading item is refused by the same statement.
BEGIN TRY
  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType) VALUES ('reading', @topic, 'B1', 'true_false_notgiven');
  THROW 50000, 'EXPECT_REJECT FAILED: an item type outside the fourteen was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Items_Modality_Type%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the invented item type was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an item type outside the fourteen, by CK_Items_Modality_Type';
END CATCH

BEGIN TRY
  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType)
  VALUES ('reading', @topic, 'B1', 'persuasive_essay');
  THROW 50000, 'EXPECT_REJECT FAILED: a reading item authored as a writing task was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Items_Modality_Type%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the cross-modality type was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a reading item authored as a writing task, by CK_Items_Modality_Type';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 10. a version belongs to an item that exists
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem) VALUES (2147483000, 1, N'Orphan stem.');
  THROW 50000, 'EXPECT_REJECT FAILED: a version with no item was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ItemVersions_Item%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the orphan version was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a version with no item, by FK_ItemVersions_Item';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 11. and the whole point, once more, at the end: nothing in the old shape was needed
-- ---------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM Certificates) OR EXISTS (SELECT 1 FROM Skills)
   OR EXISTS (SELECT 1 FROM Exams) OR EXISTS (SELECT 1 FROM Sections)
  THROW 50000, 'EXPECT_REJECT FAILED: the pool created a certificate, skill, exam or section', 1;
IF (SELECT COUNT(*) FROM Items) <> 2 OR (SELECT COUNT(*) FROM ItemVersions) <> 3
  THROW 50000, 'EXPECT_REJECT FAILED: the refusal tests above left rows behind, so the database is not in the state this file claims', 1;
PRINT 'the pool stands with no certificate, skill, exam or section in the database';
