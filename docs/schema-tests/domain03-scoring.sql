-- docs/schema-tests/domain03-scoring.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC', N'TOEIC', 1);
DECLARE @c INT = SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@c,'below_A1',N'Below A1',10,119,1), (@c,'A1',N'A1',120,224,2);
INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
  VALUES (@c, NULL, NULL, 50, 495);
PRINT 'valid scoring accepted';
-- reject: duplicate band code within a certificate (breaks reference-by-code)
BEGIN TRY
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
    VALUES (@c,'A1',N'dup',1,2,9);
  THROW 50000,'EXPECT_REJECT FAILED: dup band code accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected dup band code as expected';
END CATCH
-- M-10: reject a band range overlapping another band's range within the same certificate
BEGIN TRY
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
    VALUES (@c,'OVL',N'Overlap',115,225,9);
  THROW 50000,'EXPECT_REJECT FAILED: overlapping band range accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected overlapping band range as expected';
END CATCH
-- M-11: reject a duplicate (CertId,SkillId,ExamId,RawScore) mapping with a different ScaledScore
BEGIN TRY
  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
    VALUES (@c, NULL, NULL, 50, 300);
  THROW 50000,'EXPECT_REJECT FAILED: duplicate raw-score mapping accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected duplicate raw-score mapping as expected';
END CATCH
-- M-11: reject a scale row whose SkillId/ExamId belong to a different certificate
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('IELTS', N'IELTS', 1);
DECLARE @c2 INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c2,'SPK',N'Speaking','speaking',1);
DECLARE @sk2 INT = SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
    VALUES (@c, @sk2, NULL, 60, 500);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert skill in ScoreScales accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert skill in ScoreScales as expected';
END CATCH
