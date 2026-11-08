SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #212 / PLC-33, PLC-35, PLC-36: the per-skill placement result. One row per skill per placement,
-- carrying the level, the position inside it, the continuous coordinate, the certificate projection,
-- the cut-score version that projection was computed from, the single-task and truncation flags, the
-- measurement time and its expiry — and the status that says WHICH of five outcomes this skill had.
--
-- Three things are asserted here that a plausible implementation gets wrong while looking right:
--
--   1. **The null level no longer means three things.** Skipped, timed out and still grading were all
--      a null CefrLevel before this table. Each now has its own status, and
--      CK_PSR_StatusCarriesItsShape makes "measured without a level" and "grading with one"
--      unstorable rather than merely unhandled. A test that only counted statuses would pass a table
--      whose status column was never derived.
--   2. **A `not_measured` row cannot satisfy completion.** The guard counts measured statuses, not
--      rows: four `not_measured` rows is a placement that measured nothing.
--   3. **The cut-score version is stored AND is what the projection was computed from.** A row that
--      records version 3 beside a score computed from version 4 passes any "the column is populated"
--      check; CK_PSR_ProjectionIsATriple plus the read-back in section 6 is what refuses it.
--
-- Every refusal uses a row that is valid in every other respect, and each shape refusal that would
-- collide on the primary key uses its own skill or placement.

DECLARE @msg NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. the registry names the fact, and the table carries the columns the ticket asks for
-- ---------------------------------------------------------------------------------------------
IF NOT EXISTS (
  SELECT 1 FROM DerivedFactOwners
  WHERE FactCode = 'placement_skill_result' AND OwningService = 'AttemptEngine' AND Coverage = 'covered'
    AND CoveredTable = 'PlacementSkillResults')
  THROW 50000, 'EXPECT_REJECT FAILED: the per-skill placement result is not registered as a covered fact owned by AttemptEngine', 1;

-- The split moved 4/2/1 -> 5/2/1 in #212 and 6/2/1 in #206, and only in that direction: a fact leaving
-- `awaiting_table` for `covered`. The three counts are asserted separately, so a swap that kept the sum
-- would still fail.
IF (SELECT COUNT(*) FROM DerivedFactOwners WHERE Coverage = 'covered') <> 6
  THROW 50000, 'EXPECT_REJECT FAILED: the covered set is not the six tables that exist today (#206 added the stratum decision)', 1;
IF (SELECT COUNT(*) FROM DerivedFactOwners WHERE Coverage = 'awaiting_table') <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the awaiting-table set is not the two facts no ticket has given a table', 1;
IF (SELECT COUNT(*) FROM DerivedFactOwners WHERE Coverage = 'exempt') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the exempt set is not the one fact that already carries its own guard', 1;
PRINT 'the registry names ten facts: seven covered now, two awaiting a table, one exempt';

DECLARE @required TABLE (ColumnName SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @required (ColumnName) VALUES
  ('PlacementId'), ('SkillId'), ('CertId'),
  ('ResultStatus'), ('CefrLevel'), ('PositionInBand'), ('ContinuousCoordinate'),
  ('ProjectedBandCode'), ('ProjectedScore'), ('CutScoreTableVersion'),
  ('IsEstimatedFromOneTask'), ('IsTruncated'), ('ReusedFromPrior'), ('FailureReason'),
  -- #219: the placement's only contribution to path initialisation, and the productive profile.
  ('TopicPriorsJson'), ('CriterionProfileJson'),
  ('MeasuredAt'), ('ExpiresAt'), ('WrittenByService'), ('ProjectionVersion');
IF EXISTS (
  SELECT 1 FROM @required r
  WHERE NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS c
                    WHERE c.TABLE_NAME = 'PlacementSkillResults' AND c.COLUMN_NAME = r.ColumnName))
  THROW 50000, 'EXPECT_REJECT FAILED: a column the ticket names is missing from PlacementSkillResults', 1;
-- The absence check is proved able to see a column that exists, or it reports "fine" for any name.
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'PlacementSkillResults' AND COLUMN_NAME = 'ResultStatus')
  THROW 50000, 'EXPECT_REJECT FAILED: the column audit cannot see ResultStatus, so its absence checks prove nothing', 1;

-- NO STORED OVERALL, the same rule as LearnerSkillStates: it is MIN over the measured rows. The exact
-- column set is asserted, so an "Overall" or "AggregateLevel" added later names itself.
DECLARE @actual TABLE (ColumnName SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @actual (ColumnName)
  SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PlacementSkillResults';
DECLARE @expectedColumns TABLE (ColumnName SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @expectedColumns (ColumnName)
  SELECT ColumnName FROM @required;
IF EXISTS (SELECT 1 FROM @expectedColumns e FULL OUTER JOIN @actual a ON a.ColumnName = e.ColumnName
           WHERE e.ColumnName IS NULL OR a.ColumnName IS NULL)
BEGIN
  SET @msg = N'EXPECT_REJECT FAILED: PlacementSkillResults carries an extra or missing column ('
    + ISNULL((SELECT TOP 1 a.ColumnName FROM @actual a
              WHERE NOT EXISTS (SELECT 1 FROM @expectedColumns e WHERE e.ColumnName = a.ColumnName)), N'none')
    + N' extra) - a stored overall is a second writer of the fact the rows already state';
  THROW 50000, @msg, 1;
END
PRINT 'PlacementSkillResults carries the twenty columns the fact is made of, and no stored overall';

-- ---------------------------------------------------------------------------------------------
-- 2. the fixtures
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'placement212@x.com', 0x01);
DECLARE @user INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'placement212b@x.com', 0x01);
DECLARE @user2 INT = SCOPE_IDENTITY();

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('PS212', N'Placement skill result', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'LIST', N'Listening', 'listening', 1);
DECLARE @list INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'READ', N'Reading', 'reading', 2);
DECLARE @read INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'WRIT', N'Writing', 'writing', 3);
DECLARE @writ INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'SPEA', N'Speaking', 'speaking', 4);
DECLARE @spea INT = SCOPE_IDENTITY();
-- a second certificate, whose skill must not be storable under the first
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('PS212B', N'Other certificate', 1);
DECLARE @otherCert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@otherCert, 'LIST', N'Listening', 'listening', 1);
DECLARE @otherSkill INT = SCOPE_IDENTITY();

INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
VALUES (@cert, N'Placement 212', 'published', 30, 'placement');
DECLARE @exam INT = SCOPE_IDENTITY();
-- a graded attempt, so the placement's own snapshot guard has a real parent to point at
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
VALUES (@user, @exam, @cert, 'graded', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()), 385, 'A2', 'AttemptEngine', 1);
DECLARE @attempt INT = SCOPE_IDENTITY();
INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, ResultBandCode, TotalScore,
                            WrittenByService, ProjectionVersion)
VALUES (@user, @cert, @exam, @attempt, 'in_progress', 'A2', 385, 'AttemptEngine', 1);
DECLARE @placement INT = SCOPE_IDENTITY();
-- a second placement for the completion guard's refusals, so each fixture is a DIFFERENT parent and
-- no refusal below can be the primary key
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
VALUES (@user2, @exam, @cert, 'graded', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()), 385, 'A2', 'AttemptEngine', 1);
DECLARE @attempt2 INT = SCOPE_IDENTITY();
INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, ResultBandCode, TotalScore,
                            WrittenByService, ProjectionVersion)
VALUES (@user2, @cert, @exam, @attempt2, 'in_progress', 'A2', 385, 'AttemptEngine', 1);
DECLARE @placement2 INT = SCOPE_IDENTITY();

-- ---------------------------------------------------------------------------------------------
-- 3. each of the five statuses is storable, and each carries exactly its own shape
-- ---------------------------------------------------------------------------------------------
-- measured: everything the fact is made of, so every later refusal differs from this row in one respect
INSERT INTO PlacementSkillResults
  (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
   ProjectedBandCode, ProjectedScore, CutScoreTableVersion,
   IsEstimatedFromOneTask, IsTruncated, FailureReason, MeasuredAt, ExpiresAt,
   WrittenByService, ProjectionVersion)
VALUES
  (@placement, @list, @cert, 'measured', 'B1', 42, 3.62, 'A2', 385, 1,
   0, 0, NULL, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
   DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)),
   'AttemptEngine', 1);
PRINT 'accepted a measured per-skill result written by AttemptEngine';

-- estimated_from_one_task: same shape, and the flag IS the status
INSERT INTO PlacementSkillResults
  (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
   ProjectedBandCode, ProjectedScore, CutScoreTableVersion,
   IsEstimatedFromOneTask, IsTruncated, FailureReason, MeasuredAt, ExpiresAt,
   WrittenByService, ProjectionVersion)
VALUES
  (@placement, @writ, @cert, 'estimated_from_one_task', 'B2', 9, 4.09, 'A2', 385, 1,
   1, 0, NULL, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
   DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)),
   'AttemptEngine', 1);
PRINT 'accepted a skill estimated from one task';

-- grading: a productive task is still in the AI queue. The sitting stays in progress, and this row is
-- what a screen reads to say "still grading" instead of "not measured".
INSERT INTO PlacementSkillResults
  (PlacementId, SkillId, CertId, ResultStatus, WrittenByService, ProjectionVersion)
VALUES (@placement, @spea, @cert, 'grading', 'AttemptEngine', 1);
PRINT 'accepted a skill that is still grading';

-- not_measured with each of the three reasons the ticket names
DECLARE @uSkip INT = @placement2;
INSERT INTO PlacementSkillResults
  (PlacementId, SkillId, CertId, ResultStatus, FailureReason, WrittenByService, ProjectionVersion)
VALUES (@uSkip, @list, @cert, 'not_measured', 'candidate_skipped', 'AttemptEngine', 1);
INSERT INTO PlacementSkillResults
  (PlacementId, SkillId, CertId, ResultStatus, FailureReason, WrittenByService, ProjectionVersion)
VALUES (@uSkip, @read, @cert, 'not_measured', 'inactivity_timeout', 'AttemptEngine', 1);
INSERT INTO PlacementSkillResults
  (PlacementId, SkillId, CertId, ResultStatus, FailureReason, WrittenByService, ProjectionVersion)
VALUES (@uSkip, @writ, @cert, 'not_measured', 'ai_grading_failed', 'AttemptEngine', 1);
PRINT 'accepted the three failure reasons';

-- the three nulls are distinguishable, which is the whole point of the status
IF (SELECT COUNT(DISTINCT ResultStatus) FROM PlacementSkillResults WHERE PlacementId = @placement) <> 3
  THROW 50000, 'EXPECT_REJECT FAILED: the three outcomes did not store as three distinct statuses', 1;
PRINT 'measured, estimated_from_one_task and grading are three distinct rows, not three nulls';

-- ---------------------------------------------------------------------------------------------
-- 4. the shape of each status is enforced — this is what makes the status derived, not decorative
-- ---------------------------------------------------------------------------------------------
-- A fourth skill row for a fresh placement, so no refusal below collides on the key.
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
VALUES (@user, @exam, @cert, 'graded', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()), 385, 'A2', 'AttemptEngine', 1);
DECLARE @attempt3 INT = SCOPE_IDENTITY();
INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, ResultBandCode, TotalScore,
                            WrittenByService, ProjectionVersion)
VALUES (@user, @cert, @exam, @attempt3, 'in_progress', 'A2', 385, 'AttemptEngine', 1);
DECLARE @placement3 INT = SCOPE_IDENTITY();

-- THE INPUT THAT PASSES A NARROWER GUARD: a `measured` row with no level. A guard that only checked
-- "ResultStatus is one of five" accepts it, and it is the original defect wearing the new column.
BEGIN TRY
  INSERT INTO PlacementSkillResults (PlacementId, SkillId, CertId, ResultStatus, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'measured', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a measured skill with no level was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a measured skill carrying no level';
END CATCH

-- and the mirror: a `grading` row that already states a level. A screen reading it would print a
-- result for a task that has not been graded.
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'grading', 'B1', 42, 3.62,
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a grading skill carrying a level was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a grading skill that already states a level';
END CATCH

-- not_measured must carry a reason: "we do not know why" is the state the ticket removes
BEGIN TRY
  INSERT INTO PlacementSkillResults (PlacementId, SkillId, CertId, ResultStatus, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'not_measured', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a not_measured skill with no failure reason was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a not_measured skill with no reason';
END CATCH

-- grading must NOT carry a reason: a queued task is not a failed one
BEGIN TRY
  INSERT INTO PlacementSkillResults (PlacementId, SkillId, CertId, ResultStatus, FailureReason, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'grading', 'ai_grading_failed', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a grading skill was refused as a failure while still queued', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a grading skill that claims a failure reason';
END CATCH

-- an invented status is a row no reader can interpret
BEGIN TRY
  INSERT INTO PlacementSkillResults (PlacementId, SkillId, CertId, ResultStatus, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'probably_fine', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an invented result status was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a status outside the five';
END CATCH

-- the single-task flag and the status must agree in BOTH directions
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     IsEstimatedFromOneTask, MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'measured', 'B1', 42, 3.62, 1,
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a measured row claimed to be estimated from one task', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a measured row whose estimate flag disagrees with its status';
END CATCH

-- a position outside the band, and a coordinate outside the axis
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'measured', 'B1', 101, 3.62,
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a position of 101 inside a 0-100 band was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a position above 100';
END CATCH
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'measured', 'C2', 50, 7.10,
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a coordinate above the top of the axis was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a coordinate above 6.99';
END CATCH

-- EXPIRY: 91 days is the input that passes "later than MeasuredAt" while being wrong
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'measured', 'B1', 50, 3.50,
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          CAST('2026-04-02T00:00:00+00:00' AS DATETIMEOFFSET), 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a 91-day expiry was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused an expiry one day past the 90-day window';
END CATCH

-- ISOLATION: another certificate's skill cannot be recorded under this one
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @otherSkill, @cert, 'measured', 'B1', 50, 3.50,
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: another certificate''s skill was storable in this one', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused another certificate''s skill';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 5. the projection is a triple, and the version is part of it
-- ---------------------------------------------------------------------------------------------
-- THE INPUT THAT PASSES "the column is populated": a score with no version. It is a projection that
-- cannot be reproduced (LVL-13), and a non-null check on ProjectedScore lets it through.
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     ProjectedBandCode, ProjectedScore, MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'measured', 'B1', 42, 3.62, 'A2', 385,
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a projected score with no cut-score version was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a projected score with no cut-score table version';
END CATCH
-- and a version with no score
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     CutScoreTableVersion, MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'measured', 'B1', 42, 3.62, 1,
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a cut-score version with no projected score was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a cut-score version with no projected score';
END CATCH
-- a not_measured row cannot carry a projection: there is nothing to project
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, ProjectedBandCode, ProjectedScore, CutScoreTableVersion,
     FailureReason, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @list, @cert, 'not_measured', 'A2', 385, 1, 'candidate_skipped', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a not_measured skill carried a certificate projection', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a projection on a skill that was never measured';
END CATCH

-- THE VERSION IS READ BACK, NOT MERELY STORED. Two versions of the cut-score table, and a projection
-- recorded against each; the score computed from version 1 must not be readable as version 2. This is
-- the #222 risk the ticket names, and the assertion is that the stored version is what a reader must
-- use to recompute — the two numbers differ, so picking the wrong table changes the answer.
INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
VALUES (@cert, 1, 0, N'#212 test version 1', '2026-01-01'),
       (@cert, 2, 0, N'#212 test version 2', '2026-02-01');
INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled)
VALUES
  (@cert, 1, @list, 'B1', 'cut', 275, 395),
  (@cert, 2, @list, 'B1', 'cut', 300, 420);
-- the same position through the two versions, computed the way CefrProjectionService.Interpolate does:
-- min + round(span * position / 100). 40% of 275..395 is 323; of 300..420 it is 348.
DECLARE @v1 INT = 275 + ROUND((395 - 275) * 40 / 100.0, 0);
DECLARE @v2 INT = 300 + ROUND((420 - 300) * 40 / 100.0, 0);
IF @v1 = @v2
  THROW 50000, 'EXPECT_REJECT FAILED: the two cut-score versions project to the same score, so the version read-back proves nothing', 1;
INSERT INTO PlacementSkillResults
  (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
   ProjectedBandCode, ProjectedScore, CutScoreTableVersion, MeasuredAt, ExpiresAt,
   WrittenByService, ProjectionVersion)
VALUES (@placement3, @read, @cert, 'measured', 'B1', 40, 3.40, 'A2', @v1, 1,
        CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
        DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
-- recomputing from the row's OWN recorded version, using the table it names, must reproduce its score
IF (SELECT r.ProjectedScore
    FROM PlacementSkillResults r
    JOIN CefrCutScores c
      ON c.CertId = r.CertId AND c.TableVersion = r.CutScoreTableVersion
     AND c.SkillId = r.SkillId AND c.CefrLevel = r.CefrLevel AND c.RowKind = 'cut'
    WHERE r.PlacementId = @placement3 AND r.SkillId = @read)
   <> @v1
  THROW 50000, 'EXPECT_REJECT FAILED: the recorded score is not reproducible from the version the row records', 1;
PRINT 'a recorded projection is reproducible from the cut-score version stored on its own row';

-- ---------------------------------------------------------------------------------------------
-- 5b. the topic and criterion signals (#219)
-- ---------------------------------------------------------------------------------------------
-- A measured row may carry both. The topic hint is placement's only contribution to path
-- initialisation and is explicitly NOT mastery: three items cannot support a mastery judgement.
INSERT INTO PlacementSkillResults
  (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
   TopicPriorsJson, CriterionProfileJson, MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
VALUES (@placement3, @list, @cert, 'measured', 'B1', 42, 3.62,
        '[{"topicId":7,"evaluatedBand":"B1","itemsSeen":3,"itemsCorrect":1,"routingSignal":"weak"}]',
        '{"scores":[{"criterionId":1,"code":"GRAM","score":3,"maxScore":6}],"weakestCriterion":"GRAM","priorityFix":null}',
        CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
        DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
PRINT 'accepted a measured row carrying a topic hint and a criterion profile';

-- THE INPUT THAT PASSES A NON-NULL CHECK: a `not_measured` row with priors. Nothing was measured, so
-- a path that read it would order week one from a sitting that produced no evidence at all.
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
VALUES (@user, @exam, @cert, 'graded', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()), 385, 'A2', 'AttemptEngine', 1);
DECLARE @attempt8 INT = SCOPE_IDENTITY();
INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, ResultBandCode, TotalScore,
                            WrittenByService, ProjectionVersion)
VALUES (@user, @cert, @exam, @attempt8, 'in_progress', 'A2', 385, 'AttemptEngine', 1);
DECLARE @placement8 INT = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, FailureReason, TopicPriorsJson, WrittenByService, ProjectionVersion)
  VALUES (@placement8, @list, @cert, 'not_measured', 'candidate_skipped',
          '[{"topicId":7,"evaluatedBand":"B1","itemsSeen":2,"itemsCorrect":0,"routingSignal":"weak"}]',
          'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an unmeasured skill carried a topic hint', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a topic hint on a skill that measured nothing';
END CATCH

-- Malformed JSON is refused at the column, not discovered by a reader later.
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     MeasuredAt, ExpiresAt, TopicPriorsJson, WrittenByService, ProjectionVersion)
  VALUES (@placement8, @list, @cert, 'measured', 'B1', 10, 3.10,
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)),
          'not json at all', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a topic hint that is not JSON was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a topic hint that is not JSON';
END CATCH

-- THE INPUT THAT PASSES ISJSON: the empty array. It is valid JSON and claims topics were touched
-- while naming none - a path reading it would find no hint and no reason why.
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     MeasuredAt, ExpiresAt, TopicPriorsJson, WrittenByService, ProjectionVersion)
  VALUES (@placement8, @list, @cert, 'measured', 'B1', 10, 3.10,
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)),
          '[]', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an empty topic hint array was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused an empty topic hint array';
END CATCH

-- THE INPUT THAT PASSES A VALUE-ONLY GUARD: a write that changes ONLY the topic hint. Under the old
-- touch predicate no column the trigger compared had changed, the write declared nothing, and it was
-- allowed - a second writer could rewrite the path's hint with the owner's name still on the row.
BEGIN TRY
  UPDATE PlacementSkillResults
    SET TopicPriorsJson = '[{"topicId":9,"evaluatedBand":"B2","itemsSeen":1,"itemsCorrect":1,"routingSignal":"on_level"}]',
        ProjectionVersion = 2
  WHERE PlacementId = @placement3 AND SkillId = @list;
  THROW 50000, 'EXPECT_REJECT FAILED: a topic-hint-only write inherited the owner''s name', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%per-skill result has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the hint-only write was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT TopicPriorsJson FROM PlacementSkillResults WHERE PlacementId = @placement3 AND SkillId = @list)
   NOT LIKE '%"topicId":7%'
  THROW 50000, 'EXPECT_REJECT FAILED: a topic-hint-only write that inherited the owner''s name changed the hint', 1;
PRINT 'refused a write that changed only the topic hint without declaring its writer';

-- and the owner may rewrite its own hint, which is what a re-derivation does
BEGIN TRY
  UPDATE PlacementSkillResults
    SET TopicPriorsJson = '[{"topicId":7,"evaluatedBand":"B1","itemsSeen":3,"itemsCorrect":2,"routingSignal":"on_level"}]',
        WrittenByService = 'AttemptEngine', ProjectionVersion = 2
  WHERE PlacementId = @placement3 AND SkillId = @list;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the writer could not rewrite its own topic hint: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
PRINT 'accepted the writer rewriting its own topic hint';

-- ---------------------------------------------------------------------------------------------
-- 6. the single writer
-- ---------------------------------------------------------------------------------------------
-- The real second writer: PlacementService is the service the confirmed defect was made of, and it is
-- the name a mistaken refactor would declare. The row is the FIRST for its (placement, skill), so the
-- primary key cannot be what refuses it.
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@placement3, @spea, @cert, 'measured', 'A2', 10, 2.10,
          CAST('2026-02-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-02-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'PlacementService', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: PlacementService wrote a per-skill result AttemptEngine owns', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%per-skill result has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the second writer was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF EXISTS (SELECT 1 FROM PlacementSkillResults WHERE PlacementId = @placement3 AND SkillId = @spea)
  THROW 50000, 'EXPECT_REJECT FAILED: the refused second-writer insert wrote the row anyway', 1;
PRINT 'refused a per-skill result written by PlacementService';

-- a write that declares nothing at all
BEGIN TRY
  INSERT INTO PlacementSkillResults
    (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
     MeasuredAt, ExpiresAt, ProjectionVersion)
  VALUES (@placement3, @spea, @cert, 'measured', 'A2', 10, 2.10,
          CAST('2026-02-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-02-01T00:00:00+00:00' AS DATETIMEOFFSET)), 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a per-skill result with no declared writer was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%per-skill result has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the undeclared write was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refused a per-skill result with no declared writer';

-- THE INPUT THAT PASSES A VALUE-ONLY GUARD: a second writer changes only the level and position, so
-- WrittenByService still holds AttemptEngine from the insert. The row stays valid in every respect.
BEGIN TRY
  UPDATE PlacementSkillResults
    SET CefrLevel = 'A2', PositionInBand = 5, ContinuousCoordinate = 2.05, ProjectionVersion = 2
  WHERE PlacementId = @placement AND SkillId = @list;
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer inherited the owner''s name', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%per-skill result has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the inherited-name write was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT CefrLevel FROM PlacementSkillResults WHERE PlacementId = @placement AND SkillId = @list) <> 'B1'
  THROW 50000, 'EXPECT_REJECT FAILED: a write that inherited the owner''s name changed the level', 1;
PRINT 'refused a per-skill write that inherited the owner''s name';

-- the owner's own update is accepted
BEGIN TRY
  UPDATE PlacementSkillResults
    SET CefrLevel = 'B2', PositionInBand = 20, ContinuousCoordinate = 4.20, ProjectionVersion = 2,
        WrittenByService = 'AttemptEngine'
  WHERE PlacementId = @placement AND SkillId = @list;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the per-skill writer could not update its own row: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
IF (SELECT CefrLevel FROM PlacementSkillResults WHERE PlacementId = @placement AND SkillId = @list) <> 'B2'
  THROW 50000, 'EXPECT_REJECT FAILED: the owner''s update did not take effect', 1;
PRINT 'accepted a per-skill result update by AttemptEngine';

-- a no-op write derives nothing and needs no declaration
BEGIN TRY
  UPDATE PlacementSkillResults SET CefrLevel = 'B2', PositionInBand = 20, ContinuousCoordinate = 4.20
  WHERE PlacementId = @placement AND SkillId = @list;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the guard blocked a write that derives nothing: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
PRINT 'accepted a per-skill write that changes nothing';

-- the version. Equal is what a `>=` lets through; lower is what a `<>` lets through. Both write a
-- DIFFERENT level, because a write that changes nothing is allowed.
BEGIN TRY
  UPDATE PlacementSkillResults
    SET CefrLevel = 'A2', WrittenByService = 'AttemptEngine', ProjectionVersion = 2
  WHERE PlacementId = @placement AND SkillId = @list;
  THROW 50000, 'EXPECT_REJECT FAILED: an equal projection version overwrote the stored result', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the equal-version write was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
BEGIN TRY
  UPDATE PlacementSkillResults
    SET CefrLevel = 'A2', WrittenByService = 'AttemptEngine', ProjectionVersion = 1
  WHERE PlacementId = @placement AND SkillId = @list;
  THROW 50000, 'EXPECT_REJECT FAILED: a lower projection version overwrote the stored result', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the lower-version write was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT CefrLevel FROM PlacementSkillResults WHERE PlacementId = @placement AND SkillId = @list) <> 'B2'
  THROW 50000, 'EXPECT_REJECT FAILED: a version that is not newer overwrote the stored result', 1;
PRINT 'refused equal and lower projection versions';

-- a stale writer cannot make itself look newer by moving the token alone
BEGIN TRY
  UPDATE PlacementSkillResults SET ProjectionVersion = 3
  WHERE PlacementId = @placement AND SkillId = @list;
  THROW 50000, 'EXPECT_REJECT FAILED: a projection version was bumped with no writer declared', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused an undeclared version bump';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 7. completion: at least one MEASURED skill, or a recorded failure (PLC-36)
-- ---------------------------------------------------------------------------------------------
-- a placement with one measured skill can be done. The declaration is required: the move to `done`
-- IS the derived fact, so the writer guard and this one both see the statement.
BEGIN TRY
  UPDATE PlacementTests SET Status = 'done', WrittenByService = 'AttemptEngine', ProjectionVersion = 2
  WHERE PlacementId = @placement;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: a placement with a measured skill could not be marked done: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
PRINT 'accepted a done placement that measured a skill';

-- THE INPUT THAT PASSES A ROW-COUNT GUARD: four skills, every one `not_measured`. There are rows, so
-- a guard counting rows passes it, and the learner finished a placement that measured nothing.
BEGIN TRY
  INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
  VALUES (@user, @exam, @cert, 'graded', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()), 385, 'A2', 'AttemptEngine', 1);
  DECLARE @attempt4 INT = SCOPE_IDENTITY();
  INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, ResultBandCode, TotalScore,
                              WrittenByService, ProjectionVersion)
  VALUES (@user, @cert, @exam, @attempt4, 'in_progress', 'A2', 385, 'AttemptEngine', 1);
  DECLARE @placement4 INT = SCOPE_IDENTITY();
  INSERT INTO PlacementSkillResults (PlacementId, SkillId, CertId, ResultStatus, FailureReason, WrittenByService, ProjectionVersion)
  VALUES (@placement4, @list, @cert, 'not_measured', 'candidate_skipped', 'AttemptEngine', 1),
         (@placement4, @read, @cert, 'not_measured', 'candidate_skipped', 'AttemptEngine', 1),
         (@placement4, @writ, @cert, 'not_measured', 'candidate_skipped', 'AttemptEngine', 1),
         (@placement4, @spea, @cert, 'not_measured', 'candidate_skipped', 'AttemptEngine', 1);
  UPDATE PlacementTests SET Status = 'done', WrittenByService = 'AttemptEngine', ProjectionVersion = 2 WHERE PlacementId = @placement4;
  THROW 50000, 'EXPECT_REJECT FAILED: a placement with four not_measured skills was marked done', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%without at least one measured skill%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the empty placement was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT Status FROM PlacementTests WHERE PlacementId = @placement4) <> 'in_progress'
  THROW 50000, 'EXPECT_REJECT FAILED: the refused completion marked the placement done anyway', 1;
PRINT 'refused a done placement whose only rows are not_measured';

-- the same placement with a recorded completion failure CAN be done: the learner skipped, and a
-- partial result beats no result
BEGIN TRY
  UPDATE PlacementTests SET Status = 'done', CompletionFailureReason = 'candidate_skipped',
         WrittenByService = 'AttemptEngine', ProjectionVersion = 3
  WHERE PlacementId = @placement4;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: a placement with a recorded completion failure could not be done: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
PRINT 'accepted a done placement that recorded a completion failure instead of a measurement';

-- an `exempt_accommodated` row is NOT a measurement (LRN-55 excludes it from the overall)
BEGIN TRY
  INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
  VALUES (@user, @exam, @cert, 'graded', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()), 385, 'A2', 'AttemptEngine', 1);
  DECLARE @attempt5 INT = SCOPE_IDENTITY();
  INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, ResultBandCode, TotalScore,
                              WrittenByService, ProjectionVersion)
  VALUES (@user, @cert, @exam, @attempt5, 'in_progress', 'A2', 385, 'AttemptEngine', 1);
  DECLARE @placement5 INT = SCOPE_IDENTITY();
  INSERT INTO PlacementSkillResults (PlacementId, SkillId, CertId, ResultStatus, WrittenByService, ProjectionVersion)
  VALUES (@placement5, @list, @cert, 'exempt_accommodated', 'AttemptEngine', 1);
  UPDATE PlacementTests SET Status = 'done', WrittenByService = 'AttemptEngine', ProjectionVersion = 2 WHERE PlacementId = @placement5;
  THROW 50000, 'EXPECT_REJECT FAILED: an accommodated-only placement was marked done', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a done placement whose only row is exempt_accommodated';
END CATCH

-- a `grading` row is likewise not yet a measurement: PLC-16/PLC-34 say the placement is still in
-- progress while a productive task is in the queue.
BEGIN TRY
  INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
  VALUES (@user, @exam, @cert, 'graded', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()), 385, 'A2', 'AttemptEngine', 1);
  DECLARE @attempt6 INT = SCOPE_IDENTITY();
  INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, ResultBandCode, TotalScore,
                              WrittenByService, ProjectionVersion)
  VALUES (@user, @cert, @exam, @attempt6, 'in_progress', 'A2', 385, 'AttemptEngine', 1);
  DECLARE @placement6 INT = SCOPE_IDENTITY();
  INSERT INTO PlacementSkillResults (PlacementId, SkillId, CertId, ResultStatus, WrittenByService, ProjectionVersion)
  VALUES (@placement6, @writ, @cert, 'grading', 'AttemptEngine', 1);
  UPDATE PlacementTests SET Status = 'done', WrittenByService = 'AttemptEngine', ProjectionVersion = 2 WHERE PlacementId = @placement6;
  THROW 50000, 'EXPECT_REJECT FAILED: a placement was marked done while its only skill was still grading', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a done placement whose only skill is still grading';
END CATCH

-- and the OTHER DIRECTION: a done placement cannot have its LAST measured skill rewritten to
-- not_measured. A guard that only fired on the PlacementTests write would accept this. The placement
-- here has exactly one measured row, so removing it leaves nothing. The setup sits outside the TRY:
-- a fixture failure inside it would be caught and reported as "the guard refused a legal write".
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
VALUES (@user, @exam, @cert, 'graded', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()), 385, 'A2', 'AttemptEngine', 1);
DECLARE @attempt7 INT = SCOPE_IDENTITY();
INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, ResultBandCode, TotalScore,
                            WrittenByService, ProjectionVersion)
VALUES (@user, @cert, @exam, @attempt7, 'in_progress', 'A2', 385, 'AttemptEngine', 1);
DECLARE @placement7 INT = SCOPE_IDENTITY();
INSERT INTO PlacementSkillResults
  (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
   MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
VALUES (@placement7, @list, @cert, 'measured', 'B1', 42, 3.62,
        CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
        DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
UPDATE PlacementTests SET Status = 'done', WrittenByService = 'AttemptEngine', ProjectionVersion = 2
WHERE PlacementId = @placement7;

BEGIN TRY
  UPDATE PlacementSkillResults
    SET ResultStatus = 'not_measured', FailureReason = 'ai_grading_failed',
        CefrLevel = NULL, PositionInBand = NULL, ContinuousCoordinate = NULL,
        ProjectedBandCode = NULL, ProjectedScore = NULL, CutScoreTableVersion = NULL,
        IsEstimatedFromOneTask = 0, MeasuredAt = NULL, ExpiresAt = NULL,
        WrittenByService = 'AttemptEngine', ProjectionVersion = 3
  WHERE PlacementId = @placement7 AND SkillId = @list;
  THROW 50000, 'EXPECT_REJECT FAILED: a done placement lost its last measured skill', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%without at least one measured skill%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the lost measurement was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refused a done placement losing its last measured skill';

-- the done-with-a-failure placement still legitimately holds not_measured rows: the failure arm is
-- what makes it honest, and a rewrite of a not_measured row must not be refused
BEGIN TRY
  UPDATE PlacementSkillResults SET FailureReason = 'inactivity_timeout', WrittenByService = 'AttemptEngine', ProjectionVersion = 2
  WHERE PlacementId = @placement4 AND SkillId = @list;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: a done placement with a completion failure refused a not_measured rewrite: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
PRINT 'accepted a not_measured rewrite on a placement that recorded a completion failure';

PRINT '#212: one writer per skill row, a status that says which of five outcomes, and completion that is earned';
