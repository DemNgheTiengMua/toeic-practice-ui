SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #185: an exam binds items instead of owning them.
--
-- This file proves the binding table's four guards, and nothing proves them by proxy:
--   1. certificate isolation - a binding whose exam and section belong to different certificates is
--      refused, and certificate isolation is a composite foreign key rather than a trigger, so the
--      refusal names the constraint.
--   2. INV-6 - a production exam cannot bind a demo-tier item. This is the first place that rule can
--      be enforced at all (#184 closed the tier vocabulary; nothing read it).
--   3. the bound version must belong to the bound item, so a re-versioned item cannot silently
--      re-point a published paper.
--   4. a section can only bind an item of its own skill.
--
-- Certificate isolation and the skill rule are foreign keys, so their refusals name a constraint. The
-- tier rule is a trigger, so its refusal names the trigger's message. Every CATCH below checks the
-- specific one: a catch that only proved "something failed" cannot tell a working guard from a typo
-- in this file, and this repo has already shipped a test that voted nobody (lib.sql, domain04).
--
-- STRUCTURE. Two certificates - TOEIC with a listening and a reading section, IELTS with one reading
-- section - so "right cert, wrong cert" and "right cert, wrong skill" are both expressible. One pool
-- item per (modality, tier) so a binding can be legal in one combination and illegal in another.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. the shape: two certificates, each with its own skills and sections, and no exam yet
-- ---------------------------------------------------------------------------------------------
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC', N'TOEIC', 1), ('IELTS', N'IELTS', 1);
DECLARE @toeic INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @ielts INT = (SELECT CertId FROM Certificates WHERE Code = 'IELTS');

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
  (@toeic, 'LIST', N'Listening', 'listening', 1),
  (@toeic, 'READ', N'Reading',   'reading',   2),
  (@ielts, 'READ', N'Reading',   'reading',   1);
DECLARE @toeicList INT = (SELECT SkillId FROM Skills WHERE CertId = @toeic AND Code = 'LIST');
DECLARE @toeicRead INT = (SELECT SkillId FROM Skills WHERE CertId = @toeic AND Code = 'READ');
DECLARE @ieltsRead INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'READ');

-- Sections.OptionCount is NOT NULL and 2..6; ExpectedQuestionCount is NOT NULL and > 0.
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
  (@toeicList, @toeic, 'listening', 'P1', N'Photographs',            4, 10, 1),
  (@toeicRead, @toeic, 'reading',   'P5', N'Incomplete Sentences',   4, 10, 2),
  (@ieltsRead, @ielts, 'reading',   'PAS', N'Academic Reading',      4, 10, 1);
DECLARE @secP1  INT = (SELECT SectionId FROM Sections WHERE CertId = @toeic AND Code = 'P1');
DECLARE @secP5  INT = (SELECT SectionId FROM Sections WHERE CertId = @toeic AND Code = 'P5');
DECLARE @secPas INT = (SELECT SectionId FROM Sections WHERE CertId = @ielts AND Code = 'PAS');

-- An exam defaults to demo tier: an exam is not production until someone says so.
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
  (@toeic, N'Demo paper', 'draft', 60, 'mock'),
  (@toeic, N'Production paper', 'draft', 60, 'mock'),
  (@ielts, N'IELTS paper', 'draft', 60, 'mock');
DECLARE @demoExam  INT = (SELECT ExamId FROM Exams WHERE Name = N'Demo paper');
DECLARE @prodExam  INT = (SELECT ExamId FROM Exams WHERE Name = N'Production paper');
DECLARE @ieltsExam INT = (SELECT ExamId FROM Exams WHERE Name = N'IELTS paper');
UPDATE Exams SET ContentTier = 'production' WHERE ExamId = @prodExam;

IF (SELECT ContentTier FROM Exams WHERE ExamId = @demoExam) <> 'demo'
  THROW 50000, 'EXPECT_REJECT FAILED: an exam did not default to the demo tier', 1;
IF (SELECT ContentTier FROM Exams WHERE ExamId = @prodExam) <> 'production'
  THROW 50000, 'EXPECT_REJECT FAILED: a production exam could not be marked production', 1;
PRINT 'two certificates, three sections, and an exam content tier that defaults to demo';

-- ---------------------------------------------------------------------------------------------
-- 2. the pool the bindings draw from: one item per (skill, tier), each with one version
-- ---------------------------------------------------------------------------------------------
INSERT INTO Topics (Modality, Code, Name) VALUES
  ('listening','L-DET', N'Explicit factual detail and extraction'),
  ('reading',  'R-DET', N'Local factual retrieval and paraphrase matching');

DECLARE @lTopic INT = (SELECT TopicId FROM Topics WHERE Code = 'L-DET');
DECLARE @rTopic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-DET');

-- demo listening, production listening, demo reading, production reading
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus) VALUES
  ('listening', @lTopic, 'B1', 'short_exchange',        'demo',       'active'),
  ('listening', @lTopic, 'B1', 'short_exchange',        'production', 'active'),
  ('reading',   @rTopic, 'B1', 'passage_comprehension', 'demo',       'active'),
  ('reading',   @rTopic, 'B1', 'passage_comprehension', 'production', 'active');
DECLARE @lDemo INT = (SELECT ItemId FROM Items WHERE Modality = 'listening' AND ContentTier = 'demo');
DECLARE @lProd INT = (SELECT ItemId FROM Items WHERE Modality = 'listening' AND ContentTier = 'production');
DECLARE @rDemo INT = (SELECT ItemId FROM Items WHERE Modality = 'reading'   AND ContentTier = 'demo');
DECLARE @rProd INT = (SELECT ItemId FROM Items WHERE Modality = 'reading'   AND ContentTier = 'production');

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel) VALUES
  (@lDemo, 1, N'Listening demo stem.',       N'a', N'b', N'c', N'd', 'A'),
  (@lProd, 1, N'Listening production stem.', N'a', N'b', N'c', N'd', 'A'),
  (@rDemo, 1, N'Reading demo stem.',         N'a', N'b', N'c', N'd', 'A'),
  (@rProd, 1, N'Reading production stem.',   N'a', N'b', N'c', N'd', 'A');
DECLARE @vLDemo INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @lDemo);
DECLARE @vLProd INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @lProd);
DECLARE @vRDemo INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @rDemo);
DECLARE @vRProd INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @rProd);

-- ---------------------------------------------------------------------------------------------
-- 3. the happy paths, because a table that only refuses is not proven to work
-- ---------------------------------------------------------------------------------------------
-- a demo exam may bind demo stock ...
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
VALUES (@demoExam, @secP5, @toeic, @vRDemo, @rDemo, 'reading', 1, 1);
-- ... and production stock, because promoting stock must not strand a working demo paper
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
VALUES (@demoExam, @secP5, @toeic, @vRProd, @rProd, 'reading', 2, 1);
-- a production exam binds production stock
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
VALUES (@prodExam, @secP1, @toeic, @vLProd, @lProd, 'listening', 1, 2);

IF (SELECT COUNT(*) FROM ExamItems) <> 3
  THROW 50000, 'EXPECT_REJECT FAILED: a legal binding was not stored', 1;
IF (SELECT UnitNumber FROM ExamItems WHERE ExamId = @prodExam AND SectionId = @secP1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the binding did not keep its unit number', 1;
IF (SELECT DisplayOrder FROM ExamItems WHERE ExamId = @demoExam AND ItemId = @rProd) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the binding did not keep its display order', 1;
PRINT 'a demo exam binds either tier, a production exam binds production stock, position and unit are kept';

-- a demo item's binding names the version, not the item: adding version 2 leaves the binding alone
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@rDemo, 2, N'Reading demo stem, corrected.', N'a', N'b', N'c', N'd', 'A');
IF (SELECT ItemVersionId FROM ExamItems WHERE ExamId = @demoExam AND SectionId = @secP5 AND DisplayOrder = 1) <> @vRDemo
  THROW 50000, 'EXPECT_REJECT FAILED: re-versioning an item moved a published binding off its pinned version', 1;
PRINT 'a re-versioned item does not move an existing binding off the version it pinned';

-- the same item may sit in two sections of one paper at each section's first position, because the
-- order is the paper's per section. This is the inversion the whole ticket exists for.
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
VALUES (@demoExam, @secP1, @toeic, @vLProd, @lProd, 'listening', 1);
PRINT 'one item sits in two sections of one paper - the binding belongs to the paper';

-- ---------------------------------------------------------------------------------------------
-- 4. INV-6: a production exam cannot bind demo-tier stock
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@prodExam, @secP5, @toeic, @vRDemo, @rDemo, 'reading', 9);
  THROW 50000, 'EXPECT_REJECT FAILED: a production exam bound a demo-tier item', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%INV-6%production exam cannot bind a demo-tier item%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the demo item was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a production exam binding demo stock, by trg_ExamItems_ProductionBindsProduction';
END CATCH

-- the trigger also catches the edit that would do it from the other side: promoting an exam whose
-- paper still binds demo stock is refused at the moment of promotion, not left for a later read.
BEGIN TRY
  UPDATE Exams SET ContentTier = 'production' WHERE ExamId = @demoExam;
  THROW 50000, 'EXPECT_REJECT FAILED: an exam with demo bindings was promoted to production', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%INV-6%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: promoting a demo-bound exam was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected promoting an exam whose paper still binds demo stock, by trg_Exams_TierKeepsContent';
END CATCH
IF (SELECT ContentTier FROM Exams WHERE ExamId = @demoExam) <> 'demo'
  THROW 50000, 'EXPECT_REJECT FAILED: the refused promotion left the exam marked production', 1;

-- and once its demo bindings are gone, the same promotion succeeds - so the refusal above was the
-- rule and not "promotion is impossible"
DELETE FROM ExamItems WHERE ExamId = @demoExam AND ItemId = @rDemo;
UPDATE Exams SET ContentTier = 'production' WHERE ExamId = @demoExam;
BEGIN TRY
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@demoExam, @secP5, @toeic, @vRDemo, @rDemo, 'reading', 3);
  THROW 50000, 'EXPECT_REJECT FAILED: a newly production exam bound demo stock on insert', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%INV-6%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: a promoted exam accepted demo stock: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a demo binding on an exam that is now production';
END CATCH
-- a production binding is still accepted by the same paper, so the refusal was the tier
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
VALUES (@demoExam, @secP5, @toeic, @vRProd, @rProd, 'reading', 4);
PRINT 'accepted production stock on the promoted exam';
UPDATE Exams SET ContentTier = 'demo' WHERE ExamId = @demoExam;
-- put the demo binding back, because the later sections assert the paper's full state
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
VALUES (@demoExam, @secP5, @toeic, @vRDemo, @rDemo, 'reading', 1);

-- ---------------------------------------------------------------------------------------------
-- 5. certificate isolation: the exam's certificate and the section's must agree
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  -- a TOEIC exam and a TOEIC item, but an IELTS section
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@demoExam, @secPas, @toeic, @vRDemo, @rDemo, 'reading', 8);
  THROW 50000, 'EXPECT_REJECT FAILED: a binding whose exam and section belong to different certificates was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  -- the section FK refuses the pair; the exam FK cannot, because @demoExam/@toeic is a real pair
  IF ERROR_MESSAGE() NOT LIKE '%FK_ExamItems_SectionCert%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: cross-certificate binding refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a cross-certificate binding, by FK_ExamItems_SectionCert';
END CATCH

BEGIN TRY
  -- the same defect from the other side: an IELTS exam with a TOEIC section and TOEIC CertId
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@ieltsExam, @secP5, @toeic, @vRDemo, @rDemo, 'reading', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an IELTS exam binding a TOEIC section was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ExamItems_ExamCert%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: wrong-cert exam binding refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an IELTS exam binding a TOEIC section, by FK_ExamItems_ExamCert';
END CATCH

-- and a legal IELTS binding on the same section still works, so the refusal above was the certificate
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
VALUES (@ieltsExam, @secPas, @ielts, @vRProd, @rProd, 'reading', 1);
PRINT 'accepted an IELTS exam binding an IELTS section';

-- ---------------------------------------------------------------------------------------------
-- 6. the skill rule: a listening section cannot bind a reading item's version
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@demoExam, @secP1, @toeic, @vRDemo, @rDemo, 'reading', 8);
  THROW 50000, 'EXPECT_REJECT FAILED: a listening section bound a reading item', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ExamItems_SectionSkill%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the cross-skill binding was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a listening section binding a reading item, by FK_ExamItems_SectionSkill';
END CATCH

BEGIN TRY
  -- the item side of the same rule: a reading item cannot claim to be listening to slip past
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@demoExam, @secP1, @toeic, @vRDemo, @rDemo, 'listening', 8);
  THROW 50000, 'EXPECT_REJECT FAILED: a reading item claimed a listening skill', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ExamItems_ItemSkill%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: a mislabelled item was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a reading item labelled listening, by FK_ExamItems_ItemSkill';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 7. the bound version must belong to the bound item
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  -- a listening production version named as the reading production item
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@demoExam, @secP5, @toeic, @vLProd, @rProd, 'reading', 8);
  THROW 50000, 'EXPECT_REJECT FAILED: a binding pointed an item at another item''s version', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ExamItems_VersionItem%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the mismatched version was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a binding pointing an item at another item''s version, by FK_ExamItems_VersionItem';
END CATCH

-- an UPDATE cannot move a binding onto a version of a different item either
BEGIN TRY
  UPDATE ExamItems SET ItemVersionId = @vLProd, ItemId = @lProd, Modality = 'listening'
  WHERE ExamId = @demoExam AND SectionId = @secP5 AND DisplayOrder = 2;
  THROW 50000, 'EXPECT_REJECT FAILED: an update moved a binding to a listening item in a reading section', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ExamItems_%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the cross-skill update was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an update moving a reading binding onto a listening version';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 8. positions and units are the paper's, and are unique where they must be
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@demoExam, @secP5, @toeic, @vRProd, @rProd, 'reading', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: two items claimed position 1 of one section', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UQ_ExamItems_Exam_Section_Order%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the duplicate position was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a duplicate display position in one section, by UQ_ExamItems_Exam_Section_Order';
END CATCH

BEGIN TRY
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@demoExam, @secP5, @toeic, @vRProd, @rProd, 'reading', 0);
  THROW 50000, 'EXPECT_REJECT FAILED: position 0 was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ExamItems_DisplayOrder%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: position 0 was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected position 0, by CK_ExamItems_DisplayOrder';
END CATCH

BEGIN TRY
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
  VALUES (@demoExam, @secP5, @toeic, @vRProd, @rProd, 'reading', 7, 0);
  THROW 50000, 'EXPECT_REJECT FAILED: unit 0 was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ExamItems_UnitNumber%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: unit 0 was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected unit 0, by CK_ExamItems_UnitNumber';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 9. the count, once more: no refusal above left a row behind. Six legal bindings over four items
--    is the fingerprint of the "one item, two sections" pair plus the restored demo binding.
-- ---------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM ExamItems) <> 6
  THROW 50000, 'EXPECT_REJECT FAILED: a refused binding left a row behind', 1;
IF (SELECT COUNT(*) FROM Items) <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: the refusal tests above created or removed a pool item', 1;
IF (SELECT COUNT(DISTINCT ItemId) FROM ItemVersions) <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: the refusal tests above created or removed a version', 1;
IF (SELECT COUNT(*) FROM Certificates) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the refusal tests above created or removed a certificate', 1;
PRINT 'the binding table stands, every guard fired by name, and no refusal left a row behind';
