SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- docs/schema-tests/domain04-content.sql
--
-- #269: the paper is a roster of positions over pool items. QuestionGroups, Questions and
-- QuestionOptions are gone, so every assertion below is made on the table that now carries the fact:
-- a position's certificate isolation is FK_ExamItems_SectionCert / FK_ExamItems_ExamCert, and the
-- authored level is Items.CefrLevel with CK_Items_Level.
--
-- THREE ASSERTIONS WENT WITH THE OBJECTS THEY TESTED, and are named here rather than quietly dropped:
--   * "an MCQ with no level is accepted" (domain04 line 37-39). Items.CefrLevel is NOT NULL, so an
--     unlevelled item is not a legal row at all - the pool cannot represent what Questions allowed.
--   * "a second IsCorrect=1 option on one question is refused" (M-8 / UX_Options_OneCorrect).
--     QuestionOptions is gone; a version carries OptionA..D and ONE KeyLabel, so a second correct
--     option has no column to be written into. The rule was not weakened, it became unrepresentable.
--   * "an option attached to a free_response question is refused" (M-7). The same reason: there are no
--     option rows. A free-response item's version is the no-options branch of
--     CK_ItemVersions_OptionsComplete.
--   * "an answer's SelectedOptionId must belong to the answered question" (H-6 /
--     FK_PracticeAnswers_OptionQuestion). An answer now records a LABEL, checked by
--     CK_PracticeAnswers_SelectedLabel (domain25), not an option id.
-- The assertions that survive are kept, and each refusal is still checked by the name of the guard
-- that fired: a catch that only proves "something failed" cannot tell a working constraint from a typo
-- in this file (lib.sql records that failure mode from this very file).

DECLARE @guardWrong NVARCHAR(2048);

-- two certs, each one skill + one section
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'TOEIC',1),('IELTS',N'IELTS',1);
DECLARE @toeic INT=(SELECT CertId FROM Certificates WHERE Code='TOEIC');
DECLARE @ielts INT=(SELECT CertId FROM Certificates WHERE Code='IELTS');
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES
  (@toeic,'READ',N'Reading','reading',1),(@ielts,'SPK',N'Speaking','speaking',1);
DECLARE @tRead INT=(SELECT SkillId FROM Skills WHERE CertId=@toeic AND Code='READ');
DECLARE @iSpk  INT=(SELECT SkillId FROM Skills WHERE CertId=@ielts AND Code='SPK');
-- Sections.OptionCount is NOT NULL (2026-10-26) and 2..6; a speaking section still declares one,
-- and the certificate wizard's answer for a missing count is 4. NULL here cascades into NULL ids.
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount,DisplayOrder) VALUES
  (@tRead,@toeic,'reading','P7',N'Reading Comp',4,54,1),(@iSpk,@ielts,'speaking','S1',N'Interview',4,3,1);
DECLARE @tSec INT=(SELECT SectionId FROM Sections WHERE Code='P7');
DECLARE @iSec INT=(SELECT SectionId FROM Sections WHERE Code='S1');

-- the stock the paper draws from: one reading item (four options, key B) and one speaking task
INSERT INTO Topics (Modality,Code,Name) VALUES
  ('reading','R-DET',N'Local factual retrieval and paraphrase matching'),
  ('speaking','S-DESC',N'Informational description and personal framing');
DECLARE @rTopic INT=(SELECT TopicId FROM Topics WHERE Code='R-DET');
DECLARE @sTopic INT=(SELECT TopicId FROM Topics WHERE Code='S-DESC');
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat) VALUES
  ('reading',@rTopic,'B1','sentence_cloze','mcq'),
  ('speaking',@sTopic,'B1','descriptive_monologue','free_response');
DECLARE @rItem INT=(SELECT ItemId FROM Items WHERE Modality='reading');
DECLARE @sItem INT=(SELECT ItemId FROM Items WHERE Modality='speaking');
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,OptionA,OptionB,OptionC,OptionD,KeyLabel)
  VALUES (@rItem,1,N'The report ___ by Friday.',N'is reviewed',N'will be reviewed',N'reviewing',N'review',N'B');
-- a productive item's version is the other legal shape: no options, no key
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,MinWords,PrepSeconds,ResponseSeconds)
  VALUES (@sItem,1,N'Describe the picture.',NULL,45,90);
DECLARE @rv INT=(SELECT ItemVersionId FROM ItemVersions WHERE ItemId=@rItem);
DECLARE @sv INT=(SELECT ItemVersionId FROM ItemVersions WHERE ItemId=@sItem);

-- valid TOEIC exam + a position on its own TOEIC section
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@toeic,N'ETS 1','published',120);
DECLARE @exam INT=SCOPE_IDENTITY();
INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
  VALUES (@exam,@tSec,@toeic,@rv,@rItem,'reading',1);
IF (SELECT COUNT(*) FROM ExamItems WHERE ExamId=@exam) <> 1
  THROW 50000,'EXPECT_REJECT FAILED: a legal same-cert position was not stored',1;
PRINT 'valid same-cert position accepted';

-- REJECT (Review Focus #1): TOEIC exam position on an IELTS section. Certificate isolation is a
-- composite foreign key, so the refusal names it.
BEGIN TRY
  INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
    VALUES (@exam,@iSec,@toeic,@rv,@rItem,'reading',2);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert position accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ExamItems_SectionCert%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the cross-cert position was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected cross-cert position, by FK_ExamItems_SectionCert';
END CATCH

-- H-5: the other half of the same rule from the parent side - a position must name an exam of its own
-- certificate. The wrong input: a TOEIC exam id carrying the IELTS certificate, which the exam's own
-- composite key refuses.
BEGIN TRY
  INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
    VALUES (@exam,@iSec,@ielts,@sv,@sItem,'speaking',3);
  THROW 50000,'EXPECT_REJECT FAILED: a position naming an exam of another certificate was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ExamItems_ExamCert%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the mismatched exam certificate was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a position whose exam is of another certificate, by FK_ExamItems_ExamCert';
END CATCH

-- #229 / D1: the authored level is the fact, and it lives on the item. A level off the authored ladder
-- is refused at insert, which is where a typo is caught rather than at selection time.
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES ('reading',@rTopic,'B2','passage_comprehension','mcq');
PRINT 'accepted an authored level';
BEGIN TRY
  INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
    VALUES ('reading',@rTopic,'B3','passage_comprehension','mcq');
  THROW 50000,'EXPECT_REJECT FAILED: level B3 accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Items_Level%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the off-ladder level was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected an off-ladder level, by CK_Items_Level';
END CATCH

-- H-6, retargeted: an answer cannot be written against an item/version pair that does not exist. The
-- pool's version of "this option does not belong to this question" is
-- FK_ExamItems_VersionItem - a position cannot pin one item and another item's version.
BEGIN TRY
  INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
    VALUES (@exam,@tSec,@toeic,@sv,@rItem,'reading',4);
  THROW 50000,'EXPECT_REJECT FAILED: a position named one item and another item''s version',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ExamItems_VersionItem%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the mismatched version was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a position pinning one item and another item''s version, by FK_ExamItems_VersionItem';
END CATCH

PRINT 'domain04 ok: certificate isolation and the authored level are enforced on the pool and the roster';
