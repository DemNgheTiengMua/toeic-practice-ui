SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #199 / D0.7 / CNT-44: the telemetry evaluation record.
--
-- The half a constraint can express is here; the half only a service can show (a quarantine flip, a
-- recalculation that never downgrades) is in tests/Services.Tests/ItemTelemetryServiceTests.cs.
--
--   1. the checkpoint vocabulary is exactly D0.7's four, and a fifth is refused by name
--   2. a checkpoint cannot be recorded twice for one item - the idempotency the schedule relies on
--   3. the sample cannot be recounted below 30 administrations
--   4. the four breach bits and the verdict cannot disagree (CK_ItemTelemetry_Verdict)
--   5. NULL discrimination is a legal "undefined", and an out-of-range one is not
--   6. a facility outside [0,1] and a mean band outside [0,6] are refused
--
-- Every CATCH checks the specific guard by name: a catch that proves only "something failed" cannot
-- tell a working guard from a typo in this file.

DECLARE @guardWrong NVARCHAR(2048);
DECLARE @msg NVARCHAR(2048);

INSERT INTO Topics (Modality, Code, Name)
VALUES ('reading', 'R-GRAM', N'Morphosyntactic structure and lexical precision');
DECLARE @topic INT = SCOPE_IDENTITY();

INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', 'telemetry-1');
DECLARE @item INT = SCOPE_IDENTITY();

-- ---------------------------------------------------------------------------------------------
-- 1. the four checkpoints, and only the four
-- ---------------------------------------------------------------------------------------------
DECLARE @good INT;
INSERT INTO ItemTelemetryEvaluations
  (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Quarantined)
VALUES (@item, 30, 30, 30, 0);
SET @good = 30;
PRINT 'accepted the first checkpoint, 30';

DECLARE @checkpoint INT;
DECLARE bad_checkpoint CURSOR LOCAL FAST_FORWARD FOR SELECT v FROM (VALUES (29), (31), (51), (251), (0)) x(v);
OPEN bad_checkpoint;
FETCH NEXT FROM bad_checkpoint INTO @checkpoint;
WHILE @@FETCH_STATUS = 0
BEGIN
  BEGIN TRY
    INSERT INTO ItemTelemetryEvaluations
      (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Quarantined)
    VALUES (@item, @checkpoint, 300, 300, 0);
    SET @msg = N'EXPECT_REJECT FAILED: the checkpoint '
      + CONVERT(NVARCHAR(10), @checkpoint) + N' was accepted, and D0.7 names only 30, 50, 100 and 250';
    THROW 50000, @msg, 1;
  END TRY
  BEGIN CATCH
    IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
    IF ERROR_MESSAGE() NOT LIKE '%CK_ItemTelemetry_Checkpoint%'
    BEGIN
      SET @guardWrong = N'EXPECT_REJECT FAILED: checkpoint ' + CONVERT(NVARCHAR(10), @checkpoint)
        + N' was refused by the wrong guard: ' + ERROR_MESSAGE();
      THROW 50000, @guardWrong, 1;
    END
  END CATCH
  FETCH NEXT FROM bad_checkpoint INTO @checkpoint;
END
CLOSE bad_checkpoint; DEALLOCATE bad_checkpoint;
PRINT 'refused every checkpoint that is not 30, 50, 100 or 250';

-- and the other three named checkpoints are accepted, so the CHECK is not "only 30"
INSERT INTO ItemTelemetryEvaluations
  (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Quarantined)
VALUES (@item, 50, 50, 50, 0), (@item, 100, 100, 100, 0), (@item, 250, 250, 250, 0);
IF (SELECT COUNT(*) FROM ItemTelemetryEvaluations WHERE ItemId = @item) <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: the four named checkpoints were not all recordable', 1;
PRINT 'accepted 30, 50, 100 and 250';

-- ---------------------------------------------------------------------------------------------
-- 2. idempotency: one row per (item, checkpoint). This is what makes re-running the schedule at the
--    same administration count a refusal rather than a double count.
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO ItemTelemetryEvaluations
    (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Quarantined)
  VALUES (@item, 30, 30, 30, 0);
  THROW 50000, 'EXPECT_REJECT FAILED: the same (item, checkpoint) was recorded twice', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%PK_ItemTelemetryEvaluations%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the duplicate checkpoint was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH
PRINT 'refused a second row for the same (item, checkpoint)';

-- ---------------------------------------------------------------------------------------------
-- 3. the sample cannot be recounted below 30 - a row claiming 12 administrations is not an evaluation
-- ---------------------------------------------------------------------------------------------
DECLARE @tooFew INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', 'telemetry-2');
SET @tooFew = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO ItemTelemetryEvaluations
    (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Quarantined)
  VALUES (@tooFew, 30, 12, 12, 0);
  THROW 50000, 'EXPECT_REJECT FAILED: an evaluation over 12 administrations was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemTelemetry_Administrations%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the short sample was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH
PRINT 'refused an evaluation over fewer than 30 administrations';

-- a negative target-band count is not a count
BEGIN TRY
  INSERT INTO ItemTelemetryEvaluations
    (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Quarantined)
  VALUES (@tooFew, 30, 30, -1, 0);
  THROW 50000, 'EXPECT_REJECT FAILED: a negative target-band count was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemTelemetry_TargetBandCount%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the negative target-band count was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH
PRINT 'refused a negative target-band count';

-- ---------------------------------------------------------------------------------------------
-- 4. the verdict agrees with the four bits. The two halves are asserted together, because a CHECK
--    that only refused "clean with a breach" would let "quarantined with no breach" through, and that
--    row is a learner-visible refusal nobody can explain.
-- ---------------------------------------------------------------------------------------------
DECLARE @verdictItem INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', 'telemetry-3');
SET @verdictItem = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO ItemTelemetryEvaluations
    (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Quarantined, BreachFacility)
  VALUES (@verdictItem, 30, 30, 30, 0, 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a clean verdict was recorded beside a facility breach', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemTelemetry_Verdict%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the disagreeing verdict was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH
PRINT 'refused a clean verdict beside a breach';

BEGIN TRY
  INSERT INTO ItemTelemetryEvaluations
    (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Quarantined)
  VALUES (@verdictItem, 30, 30, 30, 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a quarantine was recorded with no breach behind it', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemTelemetry_Verdict%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the unexplained quarantine was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH
PRINT 'refused a quarantine with no breach behind it';

-- and both agreeing shapes are accepted
INSERT INTO ItemTelemetryEvaluations
  (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Quarantined)
VALUES (@verdictItem, 30, 30, 30, 0);
INSERT INTO ItemTelemetryEvaluations
  (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Quarantined, BreachDiscrimination)
VALUES (@verdictItem, 50, 50, 50, 1, 1);
PRINT 'accepted both agreeing verdict shapes';

-- ---------------------------------------------------------------------------------------------
-- 5. NULL discrimination is "undefined" and legal; an out-of-range r is not
-- ---------------------------------------------------------------------------------------------
DECLARE @statItem INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', 'telemetry-4');
SET @statItem = SCOPE_IDENTITY();

INSERT INTO ItemTelemetryEvaluations
  (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Discrimination, Quarantined)
VALUES (@statItem, 30, 30, 30, NULL, 0);
PRINT 'accepted a NULL discrimination, which is "undefined" and not zero';

DECLARE @badR DECIMAL(9,6);
DECLARE bad_r CURSOR LOCAL FAST_FORWARD FOR SELECT v FROM (VALUES (-1.5), (1.5)) x(v);
OPEN bad_r;
FETCH NEXT FROM bad_r INTO @badR;
WHILE @@FETCH_STATUS = 0
BEGIN
  BEGIN TRY
    INSERT INTO ItemTelemetryEvaluations
      (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Discrimination, Quarantined)
    VALUES (@statItem, 50, 50, 50, @badR, 0);
    SET @msg = N'EXPECT_REJECT FAILED: a correlation of ' + CONVERT(NVARCHAR(20), @badR)
      + N' was accepted, and a correlation outside [-1,1] is not one';
    THROW 50000, @msg, 1;
  END TRY
  BEGIN CATCH
    IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
    IF ERROR_MESSAGE() NOT LIKE '%CK_ItemTelemetry_Discrimination%'
    BEGIN
      SET @guardWrong = N'EXPECT_REJECT FAILED: the out-of-range correlation was refused by the wrong guard: ' + ERROR_MESSAGE();
      THROW 50000, @guardWrong, 1;
    END
  END CATCH
  FETCH NEXT FROM bad_r INTO @badR;
END
CLOSE bad_r; DEALLOCATE bad_r;
PRINT 'refused correlations outside [-1, 1]';

-- ---------------------------------------------------------------------------------------------
-- 6. facility is a proportion and a rubric band is 0..6
-- ---------------------------------------------------------------------------------------------
DECLARE @rangeItem INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', 'telemetry-5');
SET @rangeItem = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO ItemTelemetryEvaluations
    (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, TargetFacility, Quarantined)
  VALUES (@rangeItem, 30, 30, 30, 1.20, 0);
  THROW 50000, 'EXPECT_REJECT FAILED: a facility of 1.20 was accepted, and a proportion above 1 is not one', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemTelemetry_Facility%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the out-of-range facility was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH

BEGIN TRY
  INSERT INTO ItemTelemetryEvaluations
    (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, MeanResponseBand, Quarantined)
  VALUES (@rangeItem, 30, 30, 30, 7.00, 0);
  THROW 50000, 'EXPECT_REJECT FAILED: a mean band of 7 was accepted, and D0.6 tops out at C2 = 6', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemTelemetry_ResponseBand%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the out-of-range mean band was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH
PRINT 'refused a facility outside [0,1] and a mean band outside [0,6]';

-- the boundaries themselves are legal, so neither CHECK is off by one
INSERT INTO ItemTelemetryEvaluations
  (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Discrimination, TargetFacility, MeanResponseBand, Quarantined)
VALUES (@rangeItem, 30, 30, 30, -1.000000, 0.000000, 0.000000, 0);
INSERT INTO ItemTelemetryEvaluations
  (ItemId, CheckpointSize, AdministrationCount, TargetBandCount, Discrimination, TargetFacility, MeanResponseBand, Quarantined)
VALUES (@rangeItem, 50, 50, 50, 1.000000, 1.000000, 6.000000, 0);
PRINT 'accepted both ends of every statistic''s range';

-- ---------------------------------------------------------------------------------------------
-- 7. every refusal left the table where it was: @item's 4 checkpoints, @verdictItem's 2 agreeing
--    shapes, @statItem's 1 NULL-discrimination row, and @rangeItem's 2 range ends = 9. Nothing above
--    created a row it then failed to remove.
-- ---------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM ItemTelemetryEvaluations) <> 9
BEGIN
  SET @msg = N'EXPECT_REJECT FAILED: the telemetry fixture holds '
    + CONVERT(NVARCHAR(10), (SELECT COUNT(*) FROM ItemTelemetryEvaluations))
    + N' evaluations, not 9; a refusal created a row';
  THROW 50000, @msg, 1;
END
PRINT 'the four checkpoints, both verdict shapes and both range ends; no refusal left a row behind';
