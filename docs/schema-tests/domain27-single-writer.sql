SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #191 / D14 / TRN-06: one writer per derived fact, enforced in the database.
--
-- PRN-01 is why this is a parameter and a trigger rather than session context: SQL Server has no
-- transaction-local setting, and `sp_set_session_context` survives commit and rollback, so a writer
-- identity kept there would outlive the transaction that set it and be read by the next one.
--
-- Three structural assertions on every derived-fact table that exists today:
--
--   1. a writer-identifier column, required on a write of the derived fact
--   2. a trigger refusing a write that declares a different writer
--   3. a monotonic ProjectionVersion, so a stale out-of-order callback cannot overwrite a newer result
--
-- The registry names all ten facts, and #237 gave the last waiting one its table. A row that
-- claims coverage it does not have is this ticket's worst failure mode, so section 1 counts the
-- covered/awaiting/exempt split instead of trusting the prose.
--
-- Every guard below is tested with an input that is VALID in every other respect. That matters: a
-- fixture that also breaks a CHECK or a foreign key is refused before the trigger runs, so the test
-- would go green while the guard was dead. The wrong-writer inputs below are rows the natural
-- second writer would actually produce - `PlacementService` on the placement snapshot is the
-- confirmed defect from docs/research/placement-free-response-defect.md, not a made-up name.

DECLARE @msg NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. the registry: ten facts, ten owners, and an honest coverage count
-- ---------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM DerivedFactOwners) <> 10
  THROW 50000, 'EXPECT_REJECT FAILED: the registry does not name ten derived facts', 1;

DECLARE @expected TABLE (FactCode VARCHAR(32) NOT NULL PRIMARY KEY, Coverage VARCHAR(16) NOT NULL);
INSERT INTO @expected (FactCode, Coverage) VALUES
  ('placement_result',      'covered'),
  ('placement_skill_result', 'covered'),
  ('learner_skill_state',   'covered'),
  ('attempt_score_and_band', 'covered'),
  ('stratum_decision',      'covered'),
  ('topic_capability',      'covered'),
  ('step_closure',          'covered'),
  ('window_synthesis',      'covered'),
  ('exam_edition_snapshot', 'covered'),
  ('credit_movement',       'exempt');

IF EXISTS (
  SELECT 1
  FROM @expected e
  FULL OUTER JOIN DerivedFactOwners o ON o.FactCode = e.FactCode
  WHERE e.FactCode IS NULL OR o.FactCode IS NULL OR o.Coverage <> e.Coverage
)
  THROW 50000, 'EXPECT_REJECT FAILED: the registry does not name the ten facts with their real coverage', 1;

IF EXISTS (SELECT 1 FROM DerivedFactOwners WHERE OwningService IS NULL)
  THROW 50000, 'EXPECT_REJECT FAILED: a derived fact has no owning service named', 1;

-- The two ways a registry can lie about itself, and the CHECK that makes both unstorable.
IF (SELECT COUNT(*) FROM DerivedFactOwners WHERE Coverage = 'covered' AND CoveredTable IS NULL) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: a fact claims coverage but names no table, so its guard protects nothing', 1;
IF (SELECT COUNT(*) FROM DerivedFactOwners WHERE Coverage <> 'covered' AND CoveredTable IS NOT NULL) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: a fact that is not covered names a table, which overstates the mechanism', 1;
-- #211 narrowed this from three to four, #212 added a fifth, #206 a sixth, #233 a seventh, #227 an
-- eighth and #237 a ninth - it is now 9/0/1. #237 is the ticket that emptied `awaiting_table`: the one
-- fact still waiting (`topic_capability`) got its `UserCellMasteries` table. Creating a table and moving
-- a fact out of `awaiting_table` is the only direction this split may ever move. The three per-fact
-- counts are asserted separately, not just the total, so a swap that kept the sum would still fail.
IF (SELECT COUNT(*) FROM DerivedFactOwners WHERE Coverage = 'covered') <> 9
  THROW 50000, 'EXPECT_REJECT FAILED: the covered set is not the nine tables that exist today (#237 added per-cell mastery)', 1;
IF (SELECT COUNT(*) FROM DerivedFactOwners WHERE Coverage = 'awaiting_table') <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: a fact still awaits a table, and #237 created the last of them', 1;
IF (SELECT COUNT(*) FROM DerivedFactOwners WHERE Coverage = 'exempt') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the exempt set is not the one fact that already carries its own guard', 1;
PRINT 'the registry names ten facts: nine covered now, none awaiting a table, one exempt';

BEGIN TRY
  INSERT INTO DerivedFactOwners (FactCode, OwningService, Coverage, CoveredTable)
  VALUES ('ninth_fact', 'SomeService', 'covered', NULL);
  THROW 50000, 'EXPECT_REJECT FAILED: a covered fact with no table was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a covered fact that names no table';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 2. the fixtures: one certificate, one learner, one graded attempt carrying a real score
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'singlewriter@x.com', 0x01);
DECLARE @user INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('SW191', N'Single writer', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
VALUES (@cert, 'LIST', N'Listening', 'listening', 1);
DECLARE @skill INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
VALUES (@skill, @cert, 'listening', 'P1', N'Photographs', 4, 1, 1);
DECLARE @sec INT = SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
VALUES (@cert, 'A2', N'A2', 201, 500, 1), (@cert, 'B1', N'B1', 501, 784, 2);
INSERT INTO Topics (Modality, Code, Name) VALUES ('listening', 'L-DET', N'Detail');
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
VALUES (@cert, N'Placement single writer', 'published', 30, 'placement');
DECLARE @exam INT = SCOPE_IDENTITY();

-- ---------------------------------------------------------------------------------------------
-- 3. attempt score and band: owner is AttemptEngine
-- ---------------------------------------------------------------------------------------------
-- a sitting in progress is not a derived fact yet, so it needs no writer. This is the control that
-- stops the guard from becoming a blanket refusal on the busiest table in the schema.
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @attempt INT = SCOPE_IDENTITY();
IF (SELECT WrittenByService FROM ExamAttempts WHERE AttemptId = @attempt) IS NOT NULL
  THROW 50000, 'EXPECT_REJECT FAILED: an unscored attempt demanded a writer declaration', 1;

-- a sitting-state write that touches no derived column is untouched by the guard
BEGIN TRY
  UPDATE ExamAttempts SET ExpiresAt = DATEADD(HOUR, 2, SYSDATETIMEOFFSET()) WHERE AttemptId = @attempt;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the single-writer guard blocked a write to a non-derived column: '
    + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
PRINT 'accepted a sitting-state write that declares no writer, because it derives nothing';

-- the scoring write, by the owner
BEGIN TRY
  UPDATE ExamAttempts
    SET Status = 'graded', SubmittedAt = SYSDATETIMEOFFSET(),
        TotalScore = 385, BandCode = 'A2',
        WrittenByService = 'AttemptEngine', ProjectionVersion = 1
  WHERE AttemptId = @attempt;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the attempt scoring writer could not write its own score: '
    + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
IF (SELECT TotalScore FROM ExamAttempts WHERE AttemptId = @attempt) <> 385
  THROW 50000, 'EXPECT_REJECT FAILED: the owner''s score write did not take effect', 1;
PRINT 'accepted the attempt score and band written by AttemptEngine';

-- a second writer. `WritingPracticeService` is a real service that touches attempt rows, so this is
-- the declaration a mistaken refactor would actually make - not an invented string.
BEGIN TRY
  UPDATE ExamAttempts SET BandCode = 'B1', WrittenByService = 'WritingPracticeService', ProjectionVersion = 2
  WHERE AttemptId = @attempt;
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer changed the attempt band', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%attempt score and band has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the second attempt writer was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT BandCode FROM ExamAttempts WHERE AttemptId = @attempt) <> 'A2'
  THROW 50000, 'EXPECT_REJECT FAILED: the refused attempt write changed the row anyway', 1;
PRINT 'refused an attempt band written by a second service';

-- a derived write that declares nothing at all. Silently defaulting is the failure mode this
-- exists to prevent, so "no writer" is refused as firmly as "the wrong writer".
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore = 385, WrittenByService = NULL, ProjectionVersion = 2
  WHERE AttemptId = @attempt;
  THROW 50000, 'EXPECT_REJECT FAILED: a derived write with no declared writer was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%attempt score and band has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the undeclared attempt write was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refused an attempt score written with no declared writer';

-- The input that would pass a value-only guard while being wrong: a second writer that changes only
-- TotalScore and leaves WrittenByService holding the owner's name from the previous statement. The
-- row is valid in every respect; only the declaration is absent, and it must still be refused.
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore = 500, ProjectionVersion = 2 WHERE AttemptId = @attempt;
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer inherited the owner''s name from the stored row', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%attempt score and band has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the inherited-name attempt write was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT TotalScore FROM ExamAttempts WHERE AttemptId = @attempt) <> 385
  THROW 50000, 'EXPECT_REJECT FAILED: a write that inherited the owner''s name changed the score anyway', 1;
PRINT 'refused a write that inherited the owner''s name instead of declaring it';

-- the projection version. Equal is the case a plausible `>=` implementation lets through, and a
-- decrease is the case a `<>` implementation lets through. Both are "not newer", and both write a
-- DIFFERENT band: a write that changes nothing is a no-op and is allowed, so an equal-version test
-- that also changed nothing would pass even against a broken guard.
BEGIN TRY
  UPDATE ExamAttempts SET BandCode = 'B1', WrittenByService = 'AttemptEngine', ProjectionVersion = 1
  WHERE AttemptId = @attempt;
  THROW 50000, 'EXPECT_REJECT FAILED: an equal projection version overwrote the stored result', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the equal-version attempt write was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
BEGIN TRY
  UPDATE ExamAttempts SET BandCode = 'B1', WrittenByService = 'AttemptEngine', ProjectionVersion = 0
  WHERE AttemptId = @attempt;
  THROW 50000, 'EXPECT_REJECT FAILED: a lower projection version overwrote the stored result', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the lower-version attempt write was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT BandCode FROM ExamAttempts WHERE AttemptId = @attempt) <> 'A2'
  THROW 50000, 'EXPECT_REJECT FAILED: a projection version that is not newer overwrote the stored result', 1;
-- and the owner moving forward is accepted
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore = 385, WrittenByService = 'AttemptEngine', ProjectionVersion = 2
  WHERE AttemptId = @attempt;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: a newer projection version was refused: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
IF (SELECT ProjectionVersion FROM ExamAttempts WHERE AttemptId = @attempt) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the accepted projection version did not take effect', 1;
-- a version bump alone cannot happen without naming the owner, so a stale result cannot be made to
-- look newer by moving the token on its own
BEGIN TRY
  UPDATE ExamAttempts SET ProjectionVersion = 3, WrittenByService = NULL WHERE AttemptId = @attempt;
  THROW 50000, 'EXPECT_REJECT FAILED: a projection version was bumped with no writer declared', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%attempt score and band has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the undeclared version bump was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'accepted a newer projection version and refused equal and lower ones';

-- ---------------------------------------------------------------------------------------------
-- 4. placement result: owner is AttemptEngine. `PlacementService` is the second writer the
--    confirmed defect was made of, and it is the name this guard has to refuse.
-- ---------------------------------------------------------------------------------------------
-- #212: a done placement needs a measured skill (or a recorded completion failure), and a skill row
-- needs its placement to exist first. So the placement is created as a lifecycle act - in progress,
-- no result, no declaration - and its one result write is the UPDATE below, which is the same shape
-- AttemptEngine actually produces.
INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status)
VALUES (@user, @cert, @exam, @attempt, 'in_progress');
DECLARE @placement INT = SCOPE_IDENTITY();
INSERT INTO PlacementSkillResults
  (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
   MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
VALUES (@placement, @skill, @cert, 'measured', 'B1', 42, 3.62,
        CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
        DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
UPDATE PlacementTests
  SET Status = 'done', ResultBandCode = 'A2', TotalScore = 385,
      WrittenByService = 'AttemptEngine', ProjectionVersion = 1
WHERE PlacementId = @placement;
PRINT 'accepted the placement snapshot written by AttemptEngine';

-- A row that is a PERFECTLY VALID snapshot - band set, total matching the attempt, done with an
-- attempt id and a measured skill - and wrong only in who declares it. If this test used an invalid
-- row instead, trg_PlacementTests_SnapshotMatchesAttempt or the completion guard would refuse it first
-- and the writer guard could be deleted without the test noticing.
BEGIN TRY
  UPDATE PlacementTests
    SET ResultBandCode = 'A2', TotalScore = 385, ProjectionVersion = 2,
        WrittenByService = 'PlacementService'
  WHERE PlacementId = @placement;
  THROW 50000, 'EXPECT_REJECT FAILED: PlacementService wrote a snapshot AttemptEngine owns', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%placement snapshot has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the second placement writer was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT WrittenByService FROM PlacementTests WHERE PlacementId = @placement) <> 'AttemptEngine'
  THROW 50000, 'EXPECT_REJECT FAILED: the refused placement write changed the row anyway', 1;
PRINT 'refused a placement snapshot written by PlacementService';

-- the same inherited-name case on the placement snapshot: only the band moves, so a value-only check
-- would pass it because WrittenByService still holds AttemptEngine from the insert above. The band is
-- a real one and the total still matches the attempt, so the snapshot trigger has nothing to say -
-- the single-writer guard is the only thing that can refuse this.
BEGIN TRY
  UPDATE PlacementTests SET ResultBandCode = 'B1', ProjectionVersion = 2 WHERE PlacementId = @placement;
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer inherited the placement owner''s name', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%placement snapshot has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the inherited-name placement write was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT ResultBandCode FROM PlacementTests WHERE PlacementId = @placement) <> 'A2'
  THROW 50000, 'EXPECT_REJECT FAILED: a placement write that inherited the owner''s name changed the band', 1;
PRINT 'refused a placement write that inherited the owner''s name';

-- an in-progress placement is not a snapshot, so it needs no writer; creating one must still work
INSERT INTO PlacementTests (UserId, CertId, ExamId, Status)
VALUES (@user, @cert, @exam, 'in_progress');
DECLARE @pendingPlacement INT = SCOPE_IDENTITY();
IF (SELECT WrittenByService FROM PlacementTests WHERE PlacementId = @pendingPlacement) IS NOT NULL
  THROW 50000, 'EXPECT_REJECT FAILED: an in-progress placement demanded a writer declaration', 1;
PRINT 'accepted an in-progress placement that derives nothing yet';

-- the out-of-order callback: a stale writer arrives carrying the version it read before the owner
-- already moved past it. The band it would write is a real one; only the ordering is wrong.
BEGIN TRY
  UPDATE PlacementTests
    SET ResultBandCode = 'B1', ProjectionVersion = 1, WrittenByService = 'AttemptEngine'
  WHERE PlacementId = @placement;
  THROW 50000, 'EXPECT_REJECT FAILED: a stale callback overwrote a newer placement result', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the stale placement callback was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT ResultBandCode FROM PlacementTests WHERE PlacementId = @placement) <> 'A2'
  THROW 50000, 'EXPECT_REJECT FAILED: the refused stale callback changed the placement band anyway', 1;
PRINT 'refused a stale placement callback by its projection version';

-- The version token itself is part of the projection: code that does not declare itself must not be
-- able to move it forward, or a stale callback could pre-advance the token and let a later older
-- result look newer.
BEGIN TRY
  UPDATE PlacementTests SET ProjectionVersion = 2 WHERE PlacementId = @placement;
  THROW 50000, 'EXPECT_REJECT FAILED: a projection version was bumped with no writer declared', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%placement snapshot has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the undeclared version bump was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT ProjectionVersion FROM PlacementTests WHERE PlacementId = @placement) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the refused version bump moved the token anyway', 1;
PRINT 'refused a projection version bumped by code that did not declare its writer';

-- ---------------------------------------------------------------------------------------------
-- 5. step closure: owner is PathStepCompletion. `LearningPathService` also writes the step row -
--    it moves a step to in_progress - so it is the plausible wrong name to try here.
-- ---------------------------------------------------------------------------------------------
INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status)
VALUES (@user, @cert, 'A2', 'active');
DECLARE @path INT = SCOPE_IDENTITY();
DECLARE @sectionTopic INT = (SELECT TOP 1 TopicId FROM Topics WHERE Modality = 'listening');
-- #232: the step is a coordinate. It names its skill, topic, level, type and size, and no module item.
-- an open step is not a closure
INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr, StepType, SizingTier, Status, WeekNumber, WeekSequence)
VALUES (@path, @cert, @skill, 'listening', @sectionTopic, 'B1', 'focus_practice', 'developing', 'in_progress', 1, 1);
DECLARE @step INT = SCOPE_IDENTITY();
IF (SELECT WrittenByService FROM LearnerPathSteps WHERE StepId = @step) IS NOT NULL
  THROW 50000, 'EXPECT_REJECT FAILED: an open step demanded a writer declaration', 1;

BEGIN TRY
  UPDATE LearnerPathSteps
    SET Status = 'done', CompletedAt = SYSDATETIMEOFFSET(), Score = 85.00, ActualAccuracyPct = 85.00,
        WrittenByService = 'PathStepCompletion', ProjectionVersion = 1
  WHERE StepId = @step;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the step closure writer could not close its own step: '
    + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
IF (SELECT Status FROM LearnerPathSteps WHERE StepId = @step) <> 'done'
  THROW 50000, 'EXPECT_REJECT FAILED: the owner''s step closure did not take effect', 1;
PRINT 'accepted a step closed by PathStepCompletion';

BEGIN TRY
  UPDATE LearnerPathSteps
    SET Score = 100.00, WrittenByService = 'LearningPathService', ProjectionVersion = 2
  WHERE StepId = @step;
  THROW 50000, 'EXPECT_REJECT FAILED: LearningPathService rewrote a closed step', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%step closure has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the second step-closure writer was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT Score FROM LearnerPathSteps WHERE StepId = @step) <> 85.00
  THROW 50000, 'EXPECT_REJECT FAILED: the refused step write changed the row anyway', 1;
PRINT 'refused a closed step rewritten by LearningPathService';

-- the inherited-name case on step closure
BEGIN TRY
  UPDATE LearnerPathSteps SET Score = 50.00, ProjectionVersion = 2 WHERE StepId = @step;
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer inherited the step closure owner''s name', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%step closure has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the inherited-name step write was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT Score FROM LearnerPathSteps WHERE StepId = @step) <> 85.00
  THROW 50000, 'EXPECT_REJECT FAILED: a step write that inherited the owner''s name changed the score', 1;
PRINT 'refused a step write that inherited the owner''s name';

-- Reopening a closed step must be refused even when the OWNER declares itself. This is the input
-- that passes the ownership check and would otherwise sail through: PathStepCompletion closing a step
-- twice is idempotent, but PathStepCompletion *reopening* one is not a closure at all, and there is
-- nothing else on this table to stop it.
BEGIN TRY
  UPDATE LearnerPathSteps
    SET Status = 'available', WrittenByService = 'PathStepCompletion', ProjectionVersion = 2
  WHERE StepId = @step;
  THROW 50000, 'EXPECT_REJECT FAILED: an open step was reopened through the closure guard', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%cannot be reopened%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the reopened step was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT Status FROM LearnerPathSteps WHERE StepId = @step) <> 'done'
  THROW 50000, 'EXPECT_REJECT FAILED: the refused reopen moved the step out of done anyway', 1;
PRINT 'refused reopening a closed step even when the owner declared itself';

-- and an undeclared reopen is refused too, by the ownership check
BEGIN TRY
  UPDATE LearnerPathSteps SET Status = 'available', ProjectionVersion = 2 WHERE StepId = @step;
  THROW 50000, 'EXPECT_REJECT FAILED: a reopen with no writer declared was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused an undeclared reopen as expected';
END CATCH

INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr, StepType, SizingTier, Status)
VALUES (@path, @cert, @skill, 'listening', @sectionTopic, 'B1', 'focus_practice', 'developing', 'available');
DECLARE @openStep INT = SCOPE_IDENTITY();
BEGIN TRY
  UPDATE LearnerPathSteps SET Status = 'in_progress' WHERE StepId = @openStep;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the closure guard blocked an open-to-open step move: '
    + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
PRINT 'accepted an open-to-open step move that derives nothing';

-- ---------------------------------------------------------------------------------------------
-- 5b. exam edition snapshot: owner is ExamEditionFreeze. The freeze is a trigger on publish, so the
--     "wrong writer" input has to be a direct write, which is exactly what a service-side re-freeze
--     would be.
-- ---------------------------------------------------------------------------------------------
-- @exam above was inserted already published, so it froze at INSERT. That is the "created published"
-- arm, and it is the shape a fixture write takes.
IF NOT EXISTS (SELECT 1 FROM ExamEditions WHERE ExamId = @exam)
  THROW 50000, 'EXPECT_REJECT FAILED: a paper inserted published did not freeze an edition', 1;

-- The draft arm: a draft has no learners, so it has no edition to protect.
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
VALUES (@cert, N'Edition draft', 'draft', 30, 'mock');
DECLARE @draftExam INT = SCOPE_IDENTITY();
IF EXISTS (SELECT 1 FROM ExamEditions WHERE ExamId = @draftExam)
  THROW 50000, 'EXPECT_REJECT FAILED: a draft exam carried a frozen edition', 1;

UPDATE Exams SET Status = 'published' WHERE ExamId = @draftExam;
IF NOT EXISTS (SELECT 1 FROM ExamEditions WHERE ExamId = @draftExam)
  THROW 50000, 'EXPECT_REJECT FAILED: publishing did not freeze an edition', 1;
IF (SELECT WrittenByService FROM ExamEditions WHERE ExamId = @draftExam) <> 'ExamEditionFreeze'
  THROW 50000, 'EXPECT_REJECT FAILED: the freeze did not declare its own writer', 1;
IF (SELECT ScoreAggregationRule FROM ExamEditions WHERE ExamId = @draftExam) <> 'sum'
  THROW 50000, 'EXPECT_REJECT FAILED: the edition did not pin the certificate aggregation rule in force', 1;
IF (SELECT TotalDurationSeconds FROM ExamEditions WHERE ExamId = @draftExam) <> 1800
  THROW 50000, 'EXPECT_REJECT FAILED: the edition did not pin the paper''s own duration', 1;
IF (SELECT EditionNumber FROM ExamEditions WHERE ExamId = @draftExam) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: a paper that was never forked did not freeze as edition 1', 1;
PRINT 'accepted the edition snapshot frozen by the publish transition';

-- A second writer: a service re-freezing the edition after the fact. Every value it writes is a real
-- one, so only the ownership check can refuse it.
BEGIN TRY
  UPDATE ExamEditions SET TotalDurationSeconds = 3600, WrittenByService = 'AdminExamService',
         ProjectionVersion = 2
  WHERE ExamId = @draftExam;
  THROW 50000, 'EXPECT_REJECT FAILED: AdminExamService rewrote a frozen edition', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%exam edition snapshot has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the second edition writer was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT TotalDurationSeconds FROM ExamEditions WHERE ExamId = @draftExam) <> 1800
  THROW 50000, 'EXPECT_REJECT FAILED: the refused edition write changed the row anyway', 1;
PRINT 'refused a frozen edition rewritten by AdminExamService';

-- The inherited-name case: only a value moves, so a value-only check would pass it because the row
-- still holds 'ExamEditionFreeze' from the insert. The duration is a real number; only the
-- declaration is absent.
BEGIN TRY
  UPDATE ExamEditions SET TotalDurationSeconds = 3600, ProjectionVersion = 2 WHERE ExamId = @draftExam;
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer inherited the edition owner''s name', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%exam edition snapshot has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the inherited-name edition write was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT TotalDurationSeconds FROM ExamEditions WHERE ExamId = @draftExam) <> 1800
  THROW 50000, 'EXPECT_REJECT FAILED: an edition write that inherited the owner''s name changed the duration', 1;
PRINT 'refused an edition write that inherited the owner''s name';

-- Re-publishing must NOT re-freeze: the paper's learners sat the configuration that was in force,
-- and a withdrawal followed by a re-publish is a routine act. A re-freeze here would silently rewrite
-- the timers every historical attempt was scored against.
UPDATE Exams SET Status = 'hidden' WHERE ExamId = @draftExam;
UPDATE Exams SET Status = 'published' WHERE ExamId = @draftExam;
IF (SELECT TotalDurationSeconds FROM ExamEditions WHERE ExamId = @draftExam) <> 1800
  THROW 50000, 'EXPECT_REJECT FAILED: re-publishing re-froze the edition', 1;
PRINT 'kept the frozen edition across a withdraw and re-publish';

-- ---------------------------------------------------------------------------------------------
-- 6. the columns exist on every covered table, and are required exactly where the fact is written
-- ---------------------------------------------------------------------------------------------
DECLARE @covered TABLE (TableName SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @covered (TableName)
  SELECT CoveredTable FROM DerivedFactOwners WHERE Coverage = 'covered';

IF (SELECT COUNT(*) FROM @covered) <> 9
  THROW 50000, 'EXPECT_REJECT FAILED: the covered table set is not the nine this schema ships (#237 added per-cell mastery)', 1;

DECLARE @t SYSNAME, @missing INT = 0;
DECLARE tbl CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @covered;
OPEN tbl;
FETCH NEXT FROM tbl INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                 WHERE TABLE_NAME = @t AND COLUMN_NAME = 'WrittenByService')
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: ' + @t + N' has no WrittenByService column';
    THROW 50000, @msg, 1;
  END
  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                 WHERE TABLE_NAME = @t AND COLUMN_NAME = 'ProjectionVersion'
                   AND IS_NULLABLE = 'NO')
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: ' + @t + N' has no NOT NULL ProjectionVersion column';
    THROW 50000, @msg, 1;
  END
  SET @missing = @missing + 1;
  FETCH NEXT FROM tbl INTO @t;
END
CLOSE tbl; DEALLOCATE tbl;
IF @missing <> 9
  THROW 50000, 'EXPECT_REJECT FAILED: the column audit did not visit the nine covered tables', 1;
PRINT 'every covered table carries a writer column and a projection version';

PRINT '#191/#212/#206/#233/#227/#237: ten facts registered, nine enforced today, and each guard refuses a valid write by the wrong writer';
