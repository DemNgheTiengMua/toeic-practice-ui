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
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp0,@exam0,@sec0,@sk,N'fr-q1',3,'free_response',1);
DECLARE @qfr1 INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp0,@exam0,@sec0,@sk,N'fr-q2',3,'free_response',2);
DECLARE @qfr2 INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt) VALUES (@usr0,@exam0,@c,'submitted',SYSDATETIMEOFFSET());
DECLARE @a0 INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,SkillId,QuestionType,ResponseText) VALUES (@a0,@qfr1,@exam0,@sk,'free_response',N'r1');
DECLARE @fr1 INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,SkillId,QuestionType,ResponseText) VALUES (@a0,@qfr2,@exam0,@sk,'free_response',N'r2');
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
  INSERT INTO AiGradingScores (GradingId,CriterionId,SkillId,Score) VALUES (@grading,@crit,@sk,999);
  THROW 50000,'EXPECT_REJECT FAILED: score above MaxScore accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected out-of-range rubric score as expected';
END CATCH
-- valid: score within MaxScore
INSERT INTO AiGradingScores (GradingId,CriterionId,SkillId,Score) VALUES (@grading,@crit,@sk,7);
PRINT 'valid rubric score accepted';
-- M-6: attempt cannot reach graded while a free response is still pending_ai
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@role0,'m6@x.com',0x00); DECLARE @usr INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,DisplayOrder) VALUES (@sk,@c,'speaking','S1',N'Interview',NULL,1);
DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'M6 Exam','published',60);
DECLARE @exam INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@exam,@c,@sec,1);
DECLARE @grp INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp,@exam,@sec,@sk,N'm6-q',3,'free_response',1);
DECLARE @qm6 INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt) VALUES (@usr,@exam,@c,'submitted',SYSDATETIMEOFFSET());
DECLARE @am6 INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,SkillId,QuestionType,ResponseText) VALUES (@am6,@qm6,@exam,@sk,'free_response',N'resp');
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
-- M-3: reject a GradingCriteria row whose skill belongs to a different certificate
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c2 INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c2,'R',N'R','reading',1); DECLARE @sk2 INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,MaxScore,Weight,DisplayOrder)
    VALUES (@c,@sk2,'XCERT',N'Cross-cert',9,1.0,2);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert GradingCriteria accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert GradingCriteria as expected';
END CATCH
-- M-3: reject an AiGradingScores row whose criterion's skill differs from the grading's own skill
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,MaxScore,Weight,DisplayOrder)
  VALUES (@c2,@sk2,'OTH',N'Other',9,1.0,1); DECLARE @critOther INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO AiGradingScores (GradingId,CriterionId,SkillId,Score) VALUES (@grading,@critOther,@sk,5);
  THROW 50000,'EXPECT_REJECT FAILED: criterion from mismatched skill accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected criterion from mismatched skill as expected';
END CATCH
-- M-3 clause 3: reject an AiGradings row naming a skill that is NOT its free response's
-- question's section's skill (cross-certificate, unambiguous)
BEGIN TRY
  INSERT INTO AiGradings (FreeResponseId,SkillId,Status) VALUES (@fr1,@sk2,'pending');
  THROW 50000,'EXPECT_REJECT FAILED: AiGradings skill unrelated to free response chain accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected AiGradings skill unrelated to free response chain as expected';
END CATCH
-- M-3 clause 3: the free response's own skill IS accepted
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp0,@exam0,@sec0,@sk,N'fr-q3',3,'free_response',3);
DECLARE @qfr3 INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,SkillId,QuestionType,ResponseText) VALUES (@a0,@qfr3,@exam0,@sk,'free_response',N'r3');
DECLARE @fr3 INT=SCOPE_IDENTITY();
INSERT INTO AiGradings (FreeResponseId,SkillId,Status) VALUES (@fr3,@sk,'pending');
PRINT 'valid AiGradings skill matching free response chain accepted';
-- M-12: reject a Recommendation with a TargetBandCode not in ScoreBands for its cert
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal,DisplayOrder) VALUES (@c,'A1',N'A1',1,100,1);
BEGIN TRY
  INSERT INTO Recommendations (UserId,CertId,SkillId,Text,Severity,TargetBandCode)
    VALUES (@usr0,@c,@sk,N'reco',N'info','NOT_A_BAND');
  THROW 50000,'EXPECT_REJECT FAILED: unknown TargetBandCode accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected unknown TargetBandCode as expected';
END CATCH
-- M-12: reject a Recommendation whose SkillId belongs to a different certificate
BEGIN TRY
  INSERT INTO Recommendations (UserId,CertId,SkillId,Text,Severity,TargetBandCode)
    VALUES (@usr0,@c,@sk2,N'reco2',N'info',NULL);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert SkillId in Recommendations accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert SkillId in Recommendations as expected';
END CATCH
