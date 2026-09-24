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
