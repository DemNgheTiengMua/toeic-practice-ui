-- docs/schema-tests/domain01-catalog.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- valid: TOEIC with two skills, one section
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC', N'TOEIC L&R', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
  VALUES (@cert, 'LIST', N'Listening', 'listening', 1), (@cert, 'READ', N'Reading', 'reading', 2);
DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId=@cert AND Code='LIST');
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@skill, @cert, 'listening', 'P2', N'Question-Response', 3, 25, 2);
PRINT 'valid catalog accepted';
-- reject: a section that does not declare how many questions it holds. Without the count the
-- publish audit has nothing to compare against, so an empty part would go live unnoticed (#157).
BEGIN TRY
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, DisplayOrder)
    VALUES (@skill, @cert, 'listening', 'P9', N'Undated Part', 4, 9);
  THROW 50000,'EXPECT_REJECT FAILED: section without ExpectedQuestionCount accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected section without ExpectedQuestionCount as expected';
END CATCH
-- reject: a section declaring zero questions (CK_Sections_ExpectedCount)
BEGIN TRY
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
    VALUES (@skill, @cert, 'listening', 'P10', N'Empty Part', 4, 0, 10);
  THROW 50000,'EXPECT_REJECT FAILED: zero ExpectedQuestionCount accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected zero ExpectedQuestionCount as expected';
END CATCH
-- reject: Modality outside the allowed set
BEGIN TRY
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert,'X',N'X','singing',9);
  THROW 50000,'EXPECT_REJECT FAILED: bad modality accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad modality as expected';
END CATCH
-- #171: the aggregation rule set is the three rules that produce a total. `independent` was
-- removed because it produced TotalScore = 0 for every attempt and the total-vs-skills trigger
-- skipped checking it, so a certificate could be created whose learners never score.
BEGIN TRY
  INSERT INTO Certificates (Code, Name, ScoreAggregationRule) VALUES ('INDEP', N'Independent', 'independent');
  THROW 50000,'EXPECT_REJECT FAILED: the removed independent aggregation rule was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected the removed independent aggregation rule as expected';
END CATCH
-- accept: exactly the three rules that remain, so the rejection above cannot be a constraint that
-- rejects everything.
INSERT INTO Certificates (Code, Name, ScoreAggregationRule) VALUES
  ('RULE_SUM', N'Sum', 'sum'),
  ('RULE_HALF', N'Half band', 'avg_half_step'),
  ('RULE_WHOLE', N'Whole number', 'avg_whole');
PRINT 'accepted sum, avg_half_step and avg_whole';
