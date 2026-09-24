SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- two certs, each one skill + one section
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'TOEIC',1),('IELTS',N'IELTS',1);
DECLARE @toeic INT=(SELECT CertId FROM Certificates WHERE Code='TOEIC');
DECLARE @ielts INT=(SELECT CertId FROM Certificates WHERE Code='IELTS');
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES
  (@toeic,'READ',N'Reading','reading',1),(@ielts,'SPK',N'Speaking','speaking',1);
DECLARE @tRead INT=(SELECT SkillId FROM Skills WHERE CertId=@toeic AND Code='READ');
DECLARE @iSpk  INT=(SELECT SkillId FROM Skills WHERE CertId=@ielts AND Code='SPK');
INSERT INTO Sections (SkillId,CertId,Code,Name,OptionCount,DisplayOrder) VALUES
  (@tRead,@toeic,'P7',N'Reading Comp',4,1),(@iSpk,@ielts,'S1',N'Interview',NULL,1);
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
-- reject: DifficultyLevel outside 1-5
INSERT INTO Questions (GroupId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  SELECT GroupId,N'ok',3,'mcq',1 FROM QuestionGroups WHERE ExamId=@exam AND SectionId=@tSec;
BEGIN TRY
  INSERT INTO Questions (GroupId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
    SELECT GroupId,N'bad',9,'mcq',2 FROM QuestionGroups WHERE ExamId=@exam;
  THROW 50000,'EXPECT_REJECT FAILED: difficulty 9 accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected out-of-range difficulty as expected';
END CATCH
