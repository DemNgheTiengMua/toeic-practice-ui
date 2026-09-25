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
-- reject: grading status outside the set
BEGIN TRY
  INSERT INTO AiGradings (FreeResponseId,SkillId,Status) VALUES (1,@sk,'thinking');
  THROW 50000,'EXPECT_REJECT FAILED: bad grading status accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad grading status as expected';
END CATCH
