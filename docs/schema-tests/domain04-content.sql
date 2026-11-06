SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
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
-- valid TOEIC exam + group on TOEIC section
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@toeic,N'ETS 1','published',120);
DECLARE @exam INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@exam,@toeic,@tSec,1);
PRINT 'valid same-cert group accepted';
-- REJECT (Review Focus #1): TOEIC exam group pointing at an IELTS section
BEGIN TRY
  INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@exam,@toeic,@iSec,2);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert group accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert group as expected';
END CATCH
-- #230/#231: a question row carries no difficulty at all - #231 dropped the column and its CHECK.
-- What survives is the level, and its gate lives below.
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,QuestionType,DisplayOrder)
  SELECT GroupId,ExamId,SectionId,@tRead,N'ok','mcq',1 FROM QuestionGroups WHERE ExamId=@exam AND SectionId=@tSec;
-- #229 / D1: the level is the authored fact. A level off the authored ladder is refused.
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder)
  SELECT GroupId,ExamId,SectionId,@tRead,N'levelled', 'B1','mcq',6 FROM QuestionGroups WHERE ExamId=@exam AND SectionId=@tSec;
PRINT 'accepted an authored level';
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,QuestionType,DisplayOrder)
  SELECT GroupId,ExamId,SectionId,@tRead,N'unlevelled','mcq',7 FROM QuestionGroups WHERE ExamId=@exam AND SectionId=@tSec;
PRINT 'accepted a question with no level';
BEGIN TRY
  INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder)
    SELECT GroupId,ExamId,SectionId,@tRead,N'bad level','B3','mcq',8 FROM QuestionGroups WHERE ExamId=@exam;
  THROW 50000,'EXPECT_REJECT FAILED: level B3 accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an off-ladder level as expected';
END CATCH
-- H-5: reject a question row asserting an ExamId that isn't its group's own
BEGIN TRY
  INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder)
    SELECT GroupId,@exam+9999,SectionId,@tRead,N'bad exam','B2','mcq',3 FROM QuestionGroups WHERE ExamId=@exam AND SectionId=@tSec;
  THROW 50000,'EXPECT_REJECT FAILED: mismatched ExamId accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected mismatched question ExamId as expected';
END CATCH
-- H-6: reject an option belonging to a different question, and M-7/M-8 cases
DECLARE @q1 INT=(SELECT TOP 1 QuestionId FROM Questions WHERE GroupId IN (SELECT GroupId FROM QuestionGroups WHERE ExamId=@exam AND SectionId=@tSec) AND Stem=N'ok');
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder)
  SELECT GroupId,ExamId,SectionId,@tRead,N'ok2','B2','mcq',4 FROM QuestionGroups WHERE ExamId=@exam AND SectionId=@tSec;
DECLARE @q2 INT=(SELECT QuestionId FROM Questions WHERE Stem=N'ok2');
INSERT INTO QuestionOptions (QuestionId,QuestionType,Label,Text,IsCorrect) VALUES (@q1,'mcq','A',N'opt A',1);
INSERT INTO QuestionOptions (QuestionId,QuestionType,Label,Text,IsCorrect) VALUES (@q2,'mcq','A',N'opt A2',1);
PRINT 'valid options accepted';
-- M-8: reject a second IsCorrect=1 option on the same question
BEGIN TRY
  INSERT INTO QuestionOptions (QuestionId,QuestionType,Label,Text,IsCorrect) VALUES (@q1,'mcq','B',N'opt B',1);
  THROW 50000,'EXPECT_REJECT FAILED: second correct option accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected second correct option as expected';
END CATCH
-- M-7: reject options attached to a free_response question
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder)
  SELECT GroupId,ExamId,SectionId,@tRead,N'fr','B2','free_response',5 FROM QuestionGroups WHERE ExamId=@exam AND SectionId=@tSec;
DECLARE @qfr INT=(SELECT QuestionId FROM Questions WHERE Stem=N'fr');
BEGIN TRY
  INSERT INTO QuestionOptions (QuestionId,QuestionType,Label,Text,IsCorrect) VALUES (@qfr,'mcq','A',N'bad opt',1);
  THROW 50000,'EXPECT_REJECT FAILED: option on free_response question accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected option on free_response question as expected';
END CATCH
