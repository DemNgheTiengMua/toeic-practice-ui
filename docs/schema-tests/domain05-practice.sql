-- docs/schema-tests/domain05-practice.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'READ',N'R','reading',1);
DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,DisplayOrder) VALUES (@sk,@c,'reading','P5',N'IC',4,1);
DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'Spk','speaking',2);
DECLARE @skSpk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,DisplayOrder) VALUES (@skSpk,@c,'speaking','S1',N'Interview',NULL,2);
DECLARE @secSpk INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO PracticeSessions (UserId,SectionId,Modality,QuestionCount,Status,CorrectCount)
  VALUES (@u,@sec,'reading',10,'in_progress',0);
DECLARE @sess INT=SCOPE_IDENTITY();
PRINT 'valid practice session accepted';
-- reject: bad status
BEGIN TRY
  INSERT INTO PracticeSessions (UserId,SectionId,Modality,QuestionCount,Status) VALUES (@u,@sec,'reading',1,'exploding');
  THROW 50000,'EXPECT_REJECT FAILED: bad status accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad status as expected';
END CATCH
-- M-18: reject free practice on a speaking section
BEGIN TRY
  INSERT INTO PracticeSessions (UserId,SectionId,Modality,QuestionCount,Status) VALUES (@u,@secSpk,'speaking',1,'in_progress');
  THROW 50000,'EXPECT_REJECT FAILED: speaking practice session accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected speaking practice session as expected';
END CATCH
-- Content fixtures for PracticeAnswers containment + H-6 tests
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'Practice Src','published',60);
DECLARE @ex INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@ex,@c,@sec,1);
DECLARE @grp INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp,@ex,@sec,N'q1',3,'mcq',1);
DECLARE @pq1 INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp,@ex,@sec,N'q2',3,'mcq',2);
DECLARE @pq2 INT=SCOPE_IDENTITY();
INSERT INTO QuestionOptions (QuestionId,QuestionType,Label,Text,IsCorrect) VALUES (@pq1,'mcq','A',N'q1 opt',1);
DECLARE @pq1opt INT=SCOPE_IDENTITY();
INSERT INTO QuestionOptions (QuestionId,QuestionType,Label,Text,IsCorrect) VALUES (@pq2,'mcq','A',N'q2 opt',1);
DECLARE @pq2opt INT=SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId,QuestionId,SectionId,SelectedOptionId,IsCorrect)
  VALUES (@sess,@pq1,@sec,@pq1opt,1);
PRINT 'valid practice answer accepted';
-- H-6: reject an answer selecting an option belonging to a different question
BEGIN TRY
  INSERT INTO PracticeAnswers (SessionId,QuestionId,SectionId,SelectedOptionId,IsCorrect)
    VALUES (@sess,@pq2,@sec,@pq1opt,1);
  THROW 50000,'EXPECT_REJECT FAILED: cross-question option accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-question option as expected';
END CATCH
-- Low: reject a question that doesn't belong to the session's own section
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,DisplayOrder) VALUES (@sk,@c,'reading','P6',N'Other',4,2);
DECLARE @secOther INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@ex,@c,@secOther,2);
DECLARE @grpOther INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grpOther,@ex,@secOther,N'other-q',3,'mcq',1);
DECLARE @qOther INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO PracticeAnswers (SessionId,QuestionId,SectionId) VALUES (@sess,@qOther,@sec);
  THROW 50000,'EXPECT_REJECT FAILED: cross-section question accepted in practice answer',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-section practice question as expected';
END CATCH
-- Low: vw_UserSectionAccuracy math on one answered + one skipped question
INSERT INTO PracticeAnswers (SessionId,QuestionId,SectionId,SelectedOptionId,IsCorrect)
  VALUES (@sess,@pq2,@sec,NULL,NULL);
IF NOT EXISTS (
  SELECT 1 FROM vw_UserSectionAccuracy WHERE UserId=@u AND SectionId=@sec AND Answered=1 AND Correct=1
)
  THROW 50000,'EXPECT_REJECT FAILED: vw_UserSectionAccuracy math wrong for answered/skipped mix',1;
PRINT 'vw_UserSectionAccuracy correctly excludes skipped questions from Answered';
