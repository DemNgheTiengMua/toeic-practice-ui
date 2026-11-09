-- docs/schema-tests/domain45-mastery.sql
-- #237 / D4 / PLC-66..71: per-cell mastery as the database enforces it.
--
-- WHERE THE TABLE COMES FROM: docs/database-schema.sql, immediately after Topics. It was briefly in a
-- docs/schema-changes-237.sql delta while #250 owned that file, and the delta was applied by the harness
-- but NOT by the application - so the suite had a table the product did not, and every practice answer
-- returned 500 with "Invalid object name 'UserCellMasteries'". The delta is folded in and deleted;
-- database-schema.sql is the single source of truth (ADR-0001).

--
-- WHAT THIS FILE PROVES, and the wrong input that would pass without each guard:
--
--   1. the registry row says 'covered' and names the table   - a fact marked covered with no table
--                                                             protects nothing
--   2. the four states are a closed set                       - a fifth state stored as a string
--   3. the legal transitions are the only reachable ones      - a setter that accepts any state
--   4. entering mastered needs p_m >= 0.80 AND n_min          - a row that asserts 'mastered' on 4 answers
--   5. the single writer refuses a foreign name               - a second service setting the estimate
--   6. the single writer refuses an inherited name            - a value-only write, the PRN-01 case
--   7. the projection version must move forward               - an out-of-order callback
--
-- THE ARITHMETIC IS NOT TESTED HERE. p_m is a measure of the answers, not of the row: a schema test that
-- reimplemented Beta-Binomial in T-SQL would be the "test that reimplements the code under test" trap, and
-- it would pass against a broken service. That half is MasteryEstimateTests (the closed form) and
-- MasteryServiceTests (through the writer). What this file checks is that the DATABASE refuses a state the
-- arithmetic did not earn.

SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

-- ---------------------------------------------------------------------------------------------
-- 0. the registry row: this fact now HAS a table, which is the only direction the split may move
-- ---------------------------------------------------------------------------------------------
IF (SELECT Coverage FROM DerivedFactOwners WHERE FactCode = 'topic_capability') <> 'covered'
  THROW 50000, 'EXPECT_REJECT FAILED: topic_capability is not registered as covered', 1;
IF (SELECT CoveredTable FROM DerivedFactOwners WHERE FactCode = 'topic_capability') <> 'UserCellMasteries'
  THROW 50000, 'EXPECT_REJECT FAILED: topic_capability does not name UserCellMasteries', 1;
IF (SELECT OwningService FROM DerivedFactOwners WHERE FactCode = 'topic_capability') <> 'CapabilityWriter'
  THROW 50000, 'EXPECT_REJECT FAILED: topic_capability is owned by the wrong service', 1;
PRINT 'the registry names per-cell mastery as a covered fact with one owner';

-- ---------------------------------------------------------------------------------------------
-- 1. fixtures: one learner, one reading topic and one listening topic, at two levels
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'cellmastery@x.com', 0x01);
DECLARE @user INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'cellmastery2@x.com', 0x01);
DECLARE @user2 INT = SCOPE_IDENTITY();

INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local detail');
DECLARE @topic INT = SCOPE_IDENTITY();
INSERT INTO Topics (Modality, Code, Name) VALUES ('speaking', 'S-ARG', N'Argument');
DECLARE @topicS INT = SCOPE_IDENTITY();

-- A MASTERED row that is legitimate in every respect, built with the writer's own declaration. It is
-- EARNED through the named transitions, because a row that appears already mastered is a state change
-- with no event - the same hole as a setter that accepts any state, one layer down. The numbers are the
-- service's for 12 perfect answers: wc = wn = 12, so p = (2+12)/(4+12) = 0.875.
DECLARE @at DATETIMEOFFSET = CAST('2026-11-01T09:00:00+00:00' AS DATETIMEOFFSET);

INSERT INTO UserCellMasteries
  (UserId, TopicId, CefrLevel, State, WeightedCorrect, WeightedObservations, ObservationCount,
   WrittenByService, ProjectionVersion)
VALUES (@user, @topic, 'B2', 'untested', 0, 0, 0, 'CapabilityWriter', 1);

UPDATE UserCellMasteries
  SET State = 'learning', WeightedCorrect = 1, WeightedObservations = 1, ObservationCount = 1,
      LastPractisedAt = @at, WrittenByService = 'CapabilityWriter', ProjectionVersion = 2
WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2';

UPDATE UserCellMasteries
  SET State = 'mastered', WeightedCorrect = 12, WeightedObservations = 12, ObservationCount = 12,
      MasteredAt = @at, NextReviewDueAt = DATEADD(DAY, 14, @at),
      WrittenByService = 'CapabilityWriter', ProjectionVersion = 3
WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2';
PRINT 'a mastered cell earned through learning -> mastered is accepted';

-- ---------------------------------------------------------------------------------------------
-- 2. the four states are a closed set
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO UserCellMasteries
    (UserId, TopicId, CefrLevel, State, WeightedCorrect, WeightedObservations, ObservationCount,
     WrittenByService, ProjectionVersion)
  VALUES (@user2, @topic, 'B1', 'fluent', 0, 0, 0, 'CapabilityWriter', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an invented mastery state was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an invented mastery state';
END CATCH

-- The ticket's four, by name, are the stored vocabulary: unmeasured is `untested`, low is `learning`,
-- developing is `reviewing`, mastered is `mastered`. A second four-state vocabulary would be two answers.
DECLARE @states TABLE (Code VARCHAR(12) NOT NULL PRIMARY KEY);
INSERT INTO @states (Code) VALUES ('untested'), ('learning'), ('reviewing'), ('mastered');
IF EXISTS (
  SELECT 1 FROM @states s
  WHERE NOT EXISTS (SELECT 1 FROM sys.check_constraints cc
                    JOIN sys.columns c ON c.object_id = cc.parent_object_id
                    WHERE cc.name = 'CK_UserCellMasteries_State'
                      AND cc.definition LIKE '%' + s.Code + '%'))
  THROW 50000, 'EXPECT_REJECT FAILED: the state CHECK does not admit one of the four states', 1;
PRINT 'all four states are admitted by the CHECK and nothing else is';

-- ---------------------------------------------------------------------------------------------
-- 3. entering mastered needs the probability AND the observation minimum
-- ---------------------------------------------------------------------------------------------
-- THE INPUT THAT PASSES A THRESHOLD-ONLY GUARD: eleven perfect answers. (2 + 11)/(4 + 11) = 0.867,
-- which is above 0.80, so the estimate alone says mastered and ONLY the count refuses it. Four answers
-- would NOT isolate the count half - 4/4 is (2+4)/(4+4) = 0.75, below the cut - so a fixture that used
-- four would stay green with the count deleted, which is what the mutation proof caught.
INSERT INTO UserCellMasteries
  (UserId, TopicId, CefrLevel, State, WeightedCorrect, WeightedObservations, ObservationCount,
   WrittenByService, ProjectionVersion)
VALUES (@user2, @topic, 'B2', 'learning', 1, 1, 1, 'CapabilityWriter', 1);

BEGIN TRY
  UPDATE UserCellMasteries
    SET State = 'mastered', WeightedCorrect = 11, WeightedObservations = 11, ObservationCount = 11,
        LastPractisedAt = @at, MasteredAt = @at, WrittenByService = 'CapabilityWriter',
        ProjectionVersion = 2
  WHERE UserId = @user2 AND TopicId = @topic AND CefrLevel = 'B2';
  THROW 50000, 'EXPECT_REJECT FAILED: eleven answers pronounced a topic learned', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%observation minimum%'
    THROW 50000, 'EXPECT_REJECT FAILED: the eleven-answer row was refused by the wrong guard', 1;
  PRINT 'refused mastered on eleven answers, where the threshold alone would have passed (the count half)';
END CATCH

-- The control that keeps the case above honest: twelve perfect answers, the same estimate shape, is
-- accepted. Without this, a trigger that refused everything at eleven would look like a working guard.
UPDATE UserCellMasteries
  SET State = 'mastered', WeightedCorrect = 12, WeightedObservations = 12, ObservationCount = 12,
      LastPractisedAt = @at, MasteredAt = @at, WrittenByService = 'CapabilityWriter',
      ProjectionVersion = 3
WHERE UserId = @user2 AND TopicId = @topic AND CefrLevel = 'B2';
PRINT 'accepted mastered at twelve answers, because both halves are met';

-- The OTHER input that passes a count-only guard: twelve observations whose estimate is far below 0.80,
-- because nine of the twelve were wrong. The count is met; the threshold refuses. It starts from a fresh
-- cell so the state it is refused from is `learning` rather than the `mastered` the control above reached.
INSERT INTO UserCellMasteries
  (UserId, TopicId, CefrLevel, State, WeightedCorrect, WeightedObservations, ObservationCount,
   WrittenByService, ProjectionVersion)
VALUES (@user2, @topic, 'C2', 'learning', 1, 1, 1, 'CapabilityWriter', 1);

BEGIN TRY
  UPDATE UserCellMasteries
    SET State = 'mastered', WeightedCorrect = 3, WeightedObservations = 12, ObservationCount = 12,
        LastPractisedAt = @at, MasteredAt = @at, WrittenByService = 'CapabilityWriter',
        ProjectionVersion = 2
  WHERE UserId = @user2 AND TopicId = @topic AND CefrLevel = 'C2';
  THROW 50000, 'EXPECT_REJECT FAILED: a low estimate was pronounced mastered on count alone', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%p_m >= 0.80%'
    THROW 50000, 'EXPECT_REJECT FAILED: the low-estimate row was refused by the wrong guard', 1;
  PRINT 'refused mastered on the count alone (the threshold half)';
END CATCH

-- A productive cell's minimum is 3 tasks, not 12 items. It reaches mastered at seven tasks on the same
-- estimate, which proves the minimum follows the topic's modality: three is a floor, not a sufficiency,
-- because 3 perfect tasks is only (2+3)/(4+3) = 0.714. Seven gives (2+7)/(4+7) = 0.818.
INSERT INTO UserCellMasteries
  (UserId, TopicId, CefrLevel, State, WeightedCorrect, WeightedObservations, ObservationCount,
   WrittenByService, ProjectionVersion)
VALUES (@user2, @topicS, 'B1', 'learning', 1, 1, 1, 'CapabilityWriter', 1);

UPDATE UserCellMasteries
  SET State = 'mastered', WeightedCorrect = 7, WeightedObservations = 7, ObservationCount = 7,
      LastPractisedAt = @at, MasteredAt = @at, WrittenByService = 'CapabilityWriter', ProjectionVersion = 2
WHERE UserId = @user2 AND TopicId = @topicS AND CefrLevel = 'B1';
PRINT 'a productive cell masters at seven tasks on a minimum of three, because the estimate must also clear';

-- ---------------------------------------------------------------------------------------------
-- 4. the legal transitions are the only reachable ones
-- ---------------------------------------------------------------------------------------------
-- untested -> mastered is not a named transition, even with a perfect estimate and a full count. The
-- gate is on the transition, so this is refused by the transition table rather than by the mastered gate.
INSERT INTO UserCellMasteries
  (UserId, TopicId, CefrLevel, State, WeightedCorrect, WeightedObservations, ObservationCount,
   WrittenByService, ProjectionVersion)
VALUES (@user2, @topic, 'C1', 'untested', 0, 0, 0, 'CapabilityWriter', 1);
BEGIN TRY
  UPDATE UserCellMasteries
    SET State = 'mastered', WeightedCorrect = 14, WeightedObservations = 14, ObservationCount = 14,
        MasteredAt = @at, WrittenByService = 'CapabilityWriter', ProjectionVersion = 2
  WHERE UserId = @user2 AND TopicId = @topic AND CefrLevel = 'C1';
  THROW 50000, 'EXPECT_REJECT FAILED: untested jumped straight to mastered', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%not one the specification names%'
    THROW 50000, 'EXPECT_REJECT FAILED: the jump was refused by the wrong guard', 1;
  PRINT 'refused untested -> mastered: nothing reaches mastered from unmeasured';
END CATCH

-- ...but untested -> learning is named, and is the route in.
UPDATE UserCellMasteries
  SET State = 'learning', WeightedCorrect = 1, WeightedObservations = 1, ObservationCount = 1,
      LastPractisedAt = @at, WrittenByService = 'CapabilityWriter', ProjectionVersion = 2
WHERE UserId = @user2 AND TopicId = @topic AND CefrLevel = 'C1';
PRINT 'accepted untested -> learning, the first answer';

-- reviewing -> learning is named. It is the wrong answer, not the calendar. The estimate is above the
-- lower cut so the reviewing step is legitimate: wc = 6, wn = 7, n = 8 -> (2+6)/(4+8) = 0.667...
-- which is BELOW 0.733, so use wc = 8 of wn = 8: p = (2+8)/(4+8) = 0.833.
UPDATE UserCellMasteries
  SET State = 'reviewing', WeightedCorrect = 8, WeightedObservations = 8, ObservationCount = 8,
      WrittenByService = 'CapabilityWriter', ProjectionVersion = 3
WHERE UserId = @user2 AND TopicId = @topic AND CefrLevel = 'C1';
UPDATE UserCellMasteries
  SET State = 'learning', WeightedCorrect = 8, WeightedObservations = 9, ObservationCount = 9,
      WrittenByService = 'CapabilityWriter', ProjectionVersion = 4
WHERE UserId = @user2 AND TopicId = @topic AND CefrLevel = 'C1';
PRINT 'accepted reviewing -> learning: a wrong answer is not a lapse and not a decay';

-- Nothing returns to untested: evidence cannot be unobserved. A machine that recomputed from "no recent
-- answers" would hand this back after enough misses.
BEGIN TRY
  UPDATE UserCellMasteries
    SET State = 'untested', WrittenByService = 'CapabilityWriter', ProjectionVersion = 5
  WHERE UserId = @user2 AND TopicId = @topic AND CefrLevel = 'C1';
  THROW 50000, 'EXPECT_REJECT FAILED: a measured cell returned to unmeasured', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused learning -> untested';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 5. ADR-0012: exactly one writer, and it must be declared in the statement
-- ---------------------------------------------------------------------------------------------
-- A second writer. `PracticeService` is the name that would actually try this: it is the service with
-- the answers in hand and the most plausible place for someone to add a direct mastery write.
BEGIN TRY
  UPDATE UserCellMasteries
    SET State = 'reviewing', WrittenByService = 'PracticeService', ProjectionVersion = 2
  WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2';
  THROW 50000, 'EXPECT_REJECT FAILED: PracticeService wrote a fact CapabilityWriter owns', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%exactly one writer, CapabilityWriter%'
    THROW 50000, 'EXPECT_REJECT FAILED: the second writer was refused by the wrong guard', 1;
  PRINT 'refused a mastery write by a second service';
END CATCH

-- The PRN-01 case the brief names: a write that changes ONLY the value, leaving WrittenByService holding
-- 'CapabilityWriter' from the previous statement. A value-only guard passes it; UPDATE(WrittenByService)
-- is what refuses it. The row is valid in every other respect.
BEGIN TRY
  UPDATE UserCellMasteries
    SET State = 'reviewing', ProjectionVersion = 2
  WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2';
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer inherited the owner''s name', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%exactly one writer, CapabilityWriter%'
    THROW 50000, 'EXPECT_REJECT FAILED: the inherited-name write was refused by the wrong guard', 1;
  PRINT 'refused a write that inherited the owner''s name instead of declaring it';
END CATCH
IF (SELECT State FROM UserCellMasteries WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2') <> 'mastered'
  THROW 50000, 'EXPECT_REJECT FAILED: a refused write changed the row anyway', 1;

-- A write that declares nothing is refused as firmly as a foreign one.
BEGIN TRY
  UPDATE UserCellMasteries
    SET State = 'reviewing', WrittenByService = NULL, ProjectionVersion = 2
  WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2';
  THROW 50000, 'EXPECT_REJECT FAILED: an undeclared mastery write was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%exactly one writer, CapabilityWriter%'
    THROW 50000, 'EXPECT_REJECT FAILED: the undeclared write was refused by the wrong guard', 1;
  PRINT 'refused an undeclared mastery write';
END CATCH

-- The owner moving forward is accepted.
UPDATE UserCellMasteries
  SET State = 'reviewing', ConsecutiveWrong = 2, WrittenByService = 'CapabilityWriter', ProjectionVersion = 5
WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2';
IF (SELECT State FROM UserCellMasteries WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2') <> 'reviewing'
  THROW 50000, 'EXPECT_REJECT FAILED: the owner''s lapse write did not take effect', 1;
PRINT 'accepted the owner''s own lapse write';

-- ---------------------------------------------------------------------------------------------
-- 6. the projection version: equal is a replay, lower is stale, both must be refused
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  UPDATE UserCellMasteries
    SET State = 'mastered', MasteredAt = @at, WrittenByService = 'CapabilityWriter', ProjectionVersion = 5
  WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2';
  THROW 50000, 'EXPECT_REJECT FAILED: an equal projection version overwrote the stored state', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
    THROW 50000, 'EXPECT_REJECT FAILED: the equal-version write was refused by the wrong guard', 1;
  PRINT 'refused an equal projection version';
END CATCH

BEGIN TRY
  UPDATE UserCellMasteries
    SET State = 'mastered', MasteredAt = @at, WrittenByService = 'CapabilityWriter', ProjectionVersion = 4
  WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2';
  THROW 50000, 'EXPECT_REJECT FAILED: a stale callback overwrote a newer state', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
    THROW 50000, 'EXPECT_REJECT FAILED: the stale write was refused by the wrong guard', 1;
  PRINT 'refused a stale callback by its projection version';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 7. a cell is a (topic, level) and its level is authored content, never the reportable floor
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO UserCellMasteries (UserId, TopicId, CefrLevel, State, WrittenByService, ProjectionVersion)
  VALUES (@user2, @topic, 'pre_a1', 'learning', 'CapabilityWriter', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a mastery row at the pre-A1 floor was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a mastery row at the pre-A1 floor';
END CATCH

-- One row per cell: two rows for the same (learner, topic, level) is two answers to one question.
BEGIN TRY
  INSERT INTO UserCellMasteries
    (UserId, TopicId, CefrLevel, State, WeightedCorrect, WeightedObservations, ObservationCount,
     WrittenByService, ProjectionVersion)
  VALUES (@user, @topic, 'B2', 'learning', 1, 1, 1, 'CapabilityWriter', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a duplicate cell was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a duplicate (learner, topic, level) row';
END CATCH

-- A row cannot claim more correct weight than it has evidence for.
BEGIN TRY
  UPDATE UserCellMasteries
    SET WeightedCorrect = 20, WeightedObservations = 12,
        WrittenByService = 'CapabilityWriter', ProjectionVersion = 3
  WHERE UserId = @user AND TopicId = @topic AND CefrLevel = 'B2';
  THROW 50000, 'EXPECT_REJECT FAILED: correct weight exceeded total weight', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused correct weight exceeding total weight';
END CATCH

PRINT '#237: the database enforces the four states, the earned transitions, the mastered gate and one writer';
