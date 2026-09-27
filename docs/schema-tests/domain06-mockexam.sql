SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'L',N'L','listening',1),(@c,'R',N'R','reading',2);
DECLARE @sL INT=(SELECT SkillId FROM Skills WHERE CertId=@c AND Code='L');
DECLARE @sR INT=(SELECT SkillId FROM Skills WHERE CertId=@c AND Code='R');
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'Draft','draft',120);
DECLARE @draft INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'Pub','published',120);
DECLARE @pub INT=SCOPE_IDENTITY();
-- reject: attempt on a draft exam
BEGIN TRY
  INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt) VALUES (@u,@draft,@c,'in_progress',SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: draft attempt accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected draft-exam attempt as expected';
END CATCH
-- valid: attempt on published exam, two skill scores, total = 250+250
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt,TotalScore)
  VALUES (@u,@pub,@c,'submitted',SYSDATETIMEOFFSET(),500);
DECLARE @a INT=SCOPE_IDENTITY();
INSERT INTO AttemptSkillScores (AttemptId,SkillId,CertId,RawScore,ScaledScore)
  VALUES (@a,@sL,@c,40,250),(@a,@sR,@c,42,250);
PRINT 'valid published attempt accepted';
-- reject (Review Focus #2): TotalScore that disagrees with the skill sum
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore=999 WHERE AttemptId=@a;
  THROW 50000,'EXPECT_REJECT FAILED: total mismatch accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected total/skill mismatch as expected';
END CATCH
-- M-5: attempt created against published exam then moved to a draft exam by UPDATE
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt)
  VALUES (@u,@pub,@c,'in_progress',SYSDATETIMEOFFSET());
DECLARE @a2 INT=SCOPE_IDENTITY();
BEGIN TRY
  UPDATE ExamAttempts SET ExamId=@draft WHERE AttemptId=@a2;
  THROW 50000,'EXPECT_REJECT FAILED: attempt moved to draft exam accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected attempt moved onto draft exam as expected';
END CATCH
-- H-5: cross-cert fixtures
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('IELTS',N'I',1); DECLARE @c2 INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c2,'SPK',N'Spk','speaking',1);
DECLARE @s2 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,DisplayOrder) VALUES (@s2,@c2,'speaking','S1',N'Interview',NULL,1);
DECLARE @sec2 INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c2,N'IELTS Exam','published',60);
DECLARE @exam2 INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@exam2,@c2,@sec2,1);
DECLARE @grp2 INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp2,@exam2,@sec2,N'ielts-q',3,'free_response',1);
DECLARE @qIelts INT=SCOPE_IDENTITY();
-- H-5: reject a TOEIC attempt answering an IELTS question
BEGIN TRY
  INSERT INTO AttemptAnswers (AttemptId,QuestionId,ExamId) VALUES (@a,@qIelts,@pub);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert attempt answer accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert attempt answer as expected';
END CATCH
-- H-5: reject a per-skill score for a skill outside the attempt's certificate
BEGIN TRY
  INSERT INTO AttemptSkillScores (AttemptId,SkillId,CertId,RawScore,ScaledScore) VALUES (@a,@s2,@c,1,1);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert skill score accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert skill score as expected';
END CATCH
-- H-5: reject a free response to an IELTS question on a TOEIC attempt
BEGIN TRY
  INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,QuestionType,ResponseText) VALUES (@a,@qIelts,@pub,'free_response',N'x');
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert free response accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert free response as expected';
END CATCH
