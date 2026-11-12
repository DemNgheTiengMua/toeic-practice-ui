-- docs/schema-tests/domain05-practice.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #269: a practice answer names a pool item and the version it was written against. The legacy
-- containment columns (QuestionId, SectionId) are gone, and with them the three rules that read them:
--
--   * "an answer selecting an option of a different question is refused" (H-6,
--     FK_PracticeAnswers_OptionQuestion). An ItemVersion stores labels A-D rather than option rows, so
--     a pool answer records a label; the label's domain is CK_PracticeAnswers_SelectedLabel (domain25).
--   * "a question outside the session's own section is refused" (the legacy containment FK). The schema
--     states the replacement: a drill answer's containment is the session's own CELL (TopicId +
--     CefrLevel), which a pool drill has instead of a section. The item is certificate-agnostic and
--     section-free by design, so no foreign key can express the old rule.
--   * "an answer must name an item and a version of that item" survives, as
--     FK_PracticeAnswers_VersionItem - proved in domain25 rather than duplicated here.
--
-- What is asserted here is the session's own shape, its coupling to the section's modality, and the
-- section-accuracy view's arithmetic.
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'READ',N'R','reading',1);
DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount,DisplayOrder) VALUES (@sk,@c,'reading','P5',N'IC',4,30,1);
DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'Spk','speaking',2);
DECLARE @skSpk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount,DisplayOrder) VALUES (@skSpk,@c,'speaking','S1',N'Interview',4,3,2);
DECLARE @secSpk INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO PracticeSessions (UserId,CertId,SectionId,Modality,QuestionCount,Status,CorrectCount)
  VALUES (@u,@c,@sec,'reading',10,'in_progress',0);
DECLARE @sess INT=SCOPE_IDENTITY();
PRINT 'valid practice session accepted';
-- reject: bad status
BEGIN TRY
  INSERT INTO PracticeSessions (UserId,CertId,SectionId,Modality,QuestionCount,Status) VALUES (@u,@c,@sec,'reading',1,'exploding');
  THROW 50000,'EXPECT_REJECT FAILED: bad status accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad status as expected';
END CATCH
-- #150: the CHECK now admits all four modalities, so the schema cannot be what blocks a Speaking-only
-- certificate. The row is legal at this level; whether the free drill builds one is a service rule.
INSERT INTO PracticeSessions (UserId,CertId,SectionId,Modality,QuestionCount,Status) VALUES (@u,@c,@secSpk,'speaking',1,'in_progress');
PRINT 'speaking practice session accepted';
-- M-18: the composite FK still refuses a session whose modality is not its section's own.
-- Assert the constraint by name: any other error must not satisfy this test.
BEGIN TRY
  INSERT INTO PracticeSessions (UserId,CertId,SectionId,Modality,QuestionCount,Status) VALUES (@u,@c,@sec,'speaking',1,'in_progress');
  THROW 50000,'EXPECT_REJECT FAILED: mismatched modality accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_Practice_SectionModality%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by something other than FK_Practice_SectionModality',1;
  PRINT 'rejected a session whose modality is not its section''s own (FK_Practice_SectionModality)';
END CATCH
-- the pool the drill draws from: two reading items, one version each
INSERT INTO Topics (Modality,Code,Name) VALUES ('reading','R-GRAM',N'Morphosyntactic structure and lexical precision');
DECLARE @topic INT=(SELECT TopicId FROM Topics WHERE Modality='reading' AND Code='R-GRAM');
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat) VALUES
  ('reading',@topic,'B2','sentence_cloze','mcq'),
  ('reading',@topic,'B2','sentence_cloze','mcq');
DECLARE @i1 INT=(SELECT MIN(ItemId) FROM Items);
DECLARE @i2 INT=(SELECT MAX(ItemId) FROM Items);
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,OptionA,OptionB,OptionC,OptionD,KeyLabel) VALUES
  (@i1,1,N'q1',N'a',N'b',N'c',N'd',N'A'),
  (@i2,1,N'q2',N'a',N'b',N'c',N'd',N'A');
DECLARE @v1 INT=(SELECT ItemVersionId FROM ItemVersions WHERE ItemId=@i1);
DECLARE @v2 INT=(SELECT ItemVersionId FROM ItemVersions WHERE ItemId=@i2);
INSERT INTO PracticeAnswers (SessionId,ItemId,ItemVersionId,SelectedLabel,IsCorrect)
  VALUES (@sess,@i1,@v1,'A',1);
PRINT 'valid practice answer accepted';
-- Low: vw_UserSectionAccuracy math on one answered + one skipped question
INSERT INTO PracticeAnswers (SessionId,ItemId,ItemVersionId,SelectedLabel,IsCorrect)
  VALUES (@sess,@i2,@v2,NULL,NULL);
IF NOT EXISTS (
  SELECT 1 FROM vw_UserSectionAccuracy WHERE UserId=@u AND SectionId=@sec AND Answered=1 AND Correct=1
)
  THROW 50000,'EXPECT_REJECT FAILED: vw_UserSectionAccuracy math wrong for answered/skipped mix',1;
PRINT 'vw_UserSectionAccuracy correctly excludes skipped questions from Answered';
