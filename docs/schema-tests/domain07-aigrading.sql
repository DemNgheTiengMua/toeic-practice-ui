-- docs/schema-tests/domain07-aigrading.sql — abbreviated setup via the harness state
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- minimal chain: cert, skill(speaking), rubric criterion
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('IELTS',N'I',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'S','speaking',1);
DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,MaxScore,Weight,DisplayOrder)
  VALUES (@c,@sk,'FLU',N'Fluency',9,1.0,1);
DECLARE @crit INT=SCOPE_IDENTITY();
PRINT 'valid rubric criterion accepted';
-- content chain to get real FreeResponses rows for AiGradings FKs
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @role0 INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@role0,'m9@x.com',0x00); DECLARE @usr0 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,DisplayOrder) VALUES (@sk,@c,'speaking','S0',N'Warmup',NULL,1);
DECLARE @sec0 INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'M9 Exam','published',60);
DECLARE @exam0 INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@exam0,@c,@sec0,1);
DECLARE @grp0 INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp0,@exam0,@sec0,N'fr-q1',3,'free_response',1);
DECLARE @qfr1 INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp0,@exam0,@sec0,N'fr-q2',3,'free_response',2);
DECLARE @qfr2 INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt) VALUES (@usr0,@exam0,@c,'submitted',SYSDATETIMEOFFSET());
DECLARE @a0 INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,QuestionType,ResponseText) VALUES (@a0,@qfr1,@exam0,'free_response',N'r1');
DECLARE @fr1 INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,QuestionType,ResponseText) VALUES (@a0,@qfr2,@exam0,'free_response',N'r2');
DECLARE @fr2 INT=SCOPE_IDENTITY();
-- reject: grading status outside the set
BEGIN TRY
  INSERT INTO AiGradings (FreeResponseId,SkillId,Status) VALUES (@fr1,@sk,'thinking');
  THROW 50000,'EXPECT_REJECT FAILED: bad grading status accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad grading status as expected';
END CATCH
-- M-9: reject a rubric score exceeding its criterion's MaxScore
INSERT INTO AiGradings (FreeResponseId,SkillId,Status) VALUES (@fr2,@sk,'pending');
DECLARE @grading INT=(SELECT GradingId FROM AiGradings WHERE FreeResponseId=@fr2);
BEGIN TRY
  INSERT INTO AiGradingScores (GradingId,CriterionId,Score) VALUES (@grading,@crit,999);
  THROW 50000,'EXPECT_REJECT FAILED: score above MaxScore accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected out-of-range rubric score as expected';
END CATCH
-- valid: score within MaxScore
INSERT INTO AiGradingScores (GradingId,CriterionId,Score) VALUES (@grading,@crit,7);
PRINT 'valid rubric score accepted';
-- M-6: attempt cannot reach graded while a free response is still pending_ai
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@role0,'m6@x.com',0x00); DECLARE @usr INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,DisplayOrder) VALUES (@sk,@c,'speaking','S1',N'Interview',NULL,1);
DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'M6 Exam','published',60);
DECLARE @exam INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@exam,@c,@sec,1);
DECLARE @grp INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp,@exam,@sec,N'm6-q',3,'free_response',1);
DECLARE @qm6 INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt) VALUES (@usr,@exam,@c,'submitted',SYSDATETIMEOFFSET());
DECLARE @am6 INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,QuestionType,ResponseText) VALUES (@am6,@qm6,@exam,'free_response',N'resp');
BEGIN TRY
  UPDATE ExamAttempts SET Status='graded' WHERE AttemptId=@am6;
  THROW 50000,'EXPECT_REJECT FAILED: graded attempt with pending_ai free response accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected graded attempt with pending_ai free response as expected';
END CATCH
-- once the AiGradings row reaches 'done', FreeResponses.Status flips to 'graded' automatically
DECLARE @frm6 INT=(SELECT FreeResponseId FROM FreeResponses WHERE AttemptId=@am6);
INSERT INTO AiGradings (FreeResponseId,SkillId,Status) VALUES (@frm6,@sk,'done');
IF NOT EXISTS (SELECT 1 FROM FreeResponses WHERE FreeResponseId=@frm6 AND Status='graded')
  THROW 50000,'EXPECT_REJECT FAILED: FreeResponses.Status did not flip to graded',1;
UPDATE ExamAttempts SET Status='graded' WHERE AttemptId=@am6;
PRINT 'attempt reaches graded once free response is graded, as expected';
