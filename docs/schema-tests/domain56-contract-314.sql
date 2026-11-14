-- ================================================================================================
-- DOMAIN 56: #314 CONTRACT PHASE — ALL 20 RETIRED TABLES, VIEW, AND PROJECTION COLUMNS ARE GONE
-- ================================================================================================
SET NOCOUNT ON;
GO

-- 1. All 20 retired tables must not exist.
DECLARE @retiredTables TABLE (Name SYSNAME NOT NULL);
INSERT INTO @retiredTables (Name) VALUES
  (N'DerivedFactOwners'),
  (N'ReviewStrata'),
  (N'ReviewSampleItems'),
  (N'ReviewSampleDecisions'),
  (N'ItemTelemetryEvaluations'),
  (N'ContentQuotas'),
  (N'AttemptStageBundles'),
  (N'AttemptItemResponses'),
  (N'CefrCutScores'),
  (N'CefrCutScoreVersions'),
  (N'LearnerSkillStates'),
  (N'PlacementSkillResults'),
  (N'UserCellMasteries'),
  (N'CalibrationDecisions'),
  (N'LearnerPathWeeks'),
  (N'ExamEditions'),
  (N'ExamPublishAudits'),
  (N'GradingRubricLocks'),
  (N'GradingCriterionDescriptors'),
  (N'GradingCriterionAnchors');

DECLARE @foundTables INT = (
  SELECT COUNT(*) FROM sys.tables t
  INNER JOIN @retiredTables r ON t.name = r.Name
);
IF @foundTables <> 0
  THROW 50000, 'EXPECT_ABSENT FAILED: one or more #314 retired tables still exist in sys.tables', 1;
PRINT 'accept: all 20 retired tables are absent';

-- 2. vw_CoverageMatrix must not exist.
IF OBJECT_ID(N'dbo.vw_CoverageMatrix', N'V') IS NOT NULL
  THROW 50000, 'EXPECT_ABSENT FAILED: vw_CoverageMatrix still exists', 1;
PRINT 'accept: vw_CoverageMatrix is absent';

-- 3. WrittenByService and ProjectionVersion must be absent from PlacementTests, ExamAttempts, LearnerPathSteps.
DECLARE @foundCols INT = (
  SELECT COUNT(*) FROM sys.columns c
  INNER JOIN sys.tables t ON c.object_id = t.object_id
  WHERE t.name IN (N'PlacementTests', N'ExamAttempts', N'LearnerPathSteps')
    AND c.name IN (N'WrittenByService', N'ProjectionVersion')
);
IF @foundCols <> 0
  THROW 50000, 'EXPECT_ABSENT FAILED: WrittenByService or ProjectionVersion still exists on PlacementTests/ExamAttempts/LearnerPathSteps', 1;
PRINT 'accept: WrittenByService and ProjectionVersion are absent from PlacementTests, ExamAttempts, LearnerPathSteps';
GO
