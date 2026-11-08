SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #233 / D10: the four-week window's storage.
--
-- What this file pins, and the input that would pass a weaker version while being wrong:
--   1. LearnerPaths carries the window (start day, active week, slide token).
--   2. An archived week can be written ONCE, by its one declared writer.
--   3. An archived week can never be updated or deleted — the immutability half. The input that would
--      pass a "flag nobody checks" implementation is a plain UPDATE of a valid value; there is no flag
--      to check here, so the UPDATE itself must be refused.
--   4. Two archived weeks cannot share one week number on one path (the database half of idempotence).
--   5. A step's week may exceed 4, because the window slides; 0 and negative are still refused.

INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('WIN233',N'Window cert',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'R',N'Reading','reading',1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'window233@x.com',0x00,@c); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Topics (Modality,Code,Name) VALUES ('reading','R-DET',N'Detail'); DECLARE @t INT=SCOPE_IDENTITY();

-- 1. the window lives on the path, and its defaults let the seeds keep loading
INSERT INTO LearnerPaths (UserId,CertId,CurrentBandCode,Status) VALUES (@u,@c,'A2','active');
DECLARE @lp INT=SCOPE_IDENTITY();
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME='LearnerPaths' AND COLUMN_NAME='WindowStartDate' AND IS_NULLABLE='NO')
  THROW 50000, 'EXPECT_REJECT FAILED: LearnerPaths has no NOT NULL WindowStartDate, so a path has no window', 1;
IF (SELECT ActiveWeek FROM LearnerPaths WHERE LearnerPathId=@lp) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: a new path did not start its window at week 1', 1;
IF (SELECT WindowSlides FROM LearnerPaths WHERE LearnerPathId=@lp) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: a new path claimed a slide it never made', 1;
IF (SELECT WindowStartDate FROM LearnerPaths WHERE LearnerPathId=@lp) <> CAST(SYSDATETIMEOFFSET() AS DATE)
  THROW 50000, 'EXPECT_REJECT FAILED: a new path did not start its window today', 1;
PRINT 'the window is stored on the path and defaults to today, week 1, no slides';

-- reject (CK_Paths_ActiveWeek): a window has no week zero
BEGIN TRY
  INSERT INTO LearnerPaths (UserId,CertId,CurrentBandCode,Status,ActiveWeek) VALUES (@u,@c,'A2','reset',0);
  THROW 50000, 'EXPECT_REJECT FAILED: a path with an active week of zero was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an active week of zero';
END CATCH

-- 2. an archived week is written once, by its declared writer
INSERT INTO LearnerPathWeeks
  (LearnerPathId,CertId,WeekNumber,StartedOn,ClosedOn,CloseReason,PlannedSteps,CompletedSteps,
   PlannedMinutes,AverageAccuracyPct,WrittenByService,ProjectionVersion)
VALUES (@lp,@c,1,'2026-01-01','2026-01-07','calendar',3,2,40,66.67,'PathWindowSynthesizer',1);
PRINT 'accepted an archived week written by PathWindowSynthesizer';

-- reject: a valid snapshot declared by another service. The row is well-formed in every respect —
-- every number is inside its CHECK, the week number is unused — so a guard that checked the values
-- instead of the declaration would pass it.
BEGIN TRY
  INSERT INTO LearnerPathWeeks
    (LearnerPathId,CertId,WeekNumber,StartedOn,ClosedOn,CloseReason,PlannedSteps,CompletedSteps,
     PlannedMinutes,WrittenByService,ProjectionVersion)
  VALUES (@lp,@c,2,'2026-01-08','2026-01-14','calendar',3,0,40,'LearningPathService',2);
  THROW 50000, 'EXPECT_REJECT FAILED: LearningPathService archived a week', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%archived week has exactly one writer%'
  BEGIN
    DECLARE @msg NVARCHAR(2048) = N'EXPECT_REJECT FAILED: the second window writer was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refused an archived week declared by a second service';

-- reject: a derived write that declares nothing at all. "No writer" is refused as firmly as the wrong
-- writer, because a silently defaulted name is how one fact ends up with two authors.
BEGIN TRY
  INSERT INTO LearnerPathWeeks
    (LearnerPathId,CertId,WeekNumber,StartedOn,ClosedOn,CloseReason,PlannedSteps,CompletedSteps,
     PlannedMinutes,WrittenByService,ProjectionVersion)
  VALUES (@lp,@c,3,'2026-01-15','2026-01-21','calendar',3,0,40,NULL,3);
  THROW 50000, 'EXPECT_REJECT FAILED: an archived week with no declared writer was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused an archived week with no declared writer';
END CATCH

-- reject: a projection version of zero. The token is what orders the archive against the path's own
-- slide count; zero means "no slide produced this", which is a snapshot of nothing.
BEGIN TRY
  INSERT INTO LearnerPathWeeks
    (LearnerPathId,CertId,WeekNumber,StartedOn,ClosedOn,CloseReason,PlannedSteps,CompletedSteps,
     PlannedMinutes,WrittenByService,ProjectionVersion)
  VALUES (@lp,@c,4,'2026-01-22','2026-01-28','calendar',3,0,40,'PathWindowSynthesizer',0);
  THROW 50000, 'EXPECT_REJECT FAILED: an archived week with projection version zero was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused an archived week claiming version zero';
END CATCH

-- 3. IMMUTABILITY. This is the ticket's "a late answer cannot alter one" made structural. The late
-- answer arrives as a well-formed UPDATE of a real value, declared by the OWNER itself — the case a
-- "did another service write this?" guard would let through.
BEGIN TRY
  UPDATE LearnerPathWeeks SET CompletedSteps = 3, AverageAccuracyPct = 100.00
  WHERE LearnerPathId = @lp AND WeekNumber = 1;
  THROW 50000, 'EXPECT_REJECT FAILED: a late answer restated an archived week', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%history%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the archived week UPDATE was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT CompletedSteps FROM LearnerPathWeeks WHERE LearnerPathId=@lp AND WeekNumber=1) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the refused UPDATE changed the archived week anyway', 1;
IF (SELECT AverageAccuracyPct FROM LearnerPathWeeks WHERE LearnerPathId=@lp AND WeekNumber=1) <> 66.67
  THROW 50000, 'EXPECT_REJECT FAILED: the refused UPDATE changed the archived accuracy anyway', 1;
PRINT 'refused a late answer restating an archived week, even from the owner';

-- and the DELETE half: history nobody can rewrite is also history nobody can erase
BEGIN TRY
  DELETE FROM LearnerPathWeeks WHERE LearnerPathId = @lp AND WeekNumber = 1;
  THROW 50000, 'EXPECT_REJECT FAILED: an archived week was deleted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused deleting an archived week';
END CATCH
IF NOT EXISTS (SELECT 1 FROM LearnerPathWeeks WHERE LearnerPathId=@lp AND WeekNumber=1)
  THROW 50000, 'EXPECT_REJECT FAILED: the refused delete removed the archived week anyway', 1;

-- 4. IDEMPOTENCE's database half: two slides at the same instant cannot archive one week twice. The
-- input that would pass a read-then-write guard is a second writer that also read "no archive yet".
BEGIN TRY
  INSERT INTO LearnerPathWeeks
    (LearnerPathId,CertId,WeekNumber,StartedOn,ClosedOn,CloseReason,PlannedSteps,CompletedSteps,
     PlannedMinutes,WrittenByService,ProjectionVersion)
  VALUES (@lp,@c,1,'2026-01-01','2026-01-07','calendar',3,3,40,'PathWindowSynthesizer',2);
  THROW 50000, 'EXPECT_REJECT FAILED: one week was archived twice', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a second archive for a week number already archived';
END CATCH

-- the same week number on a DIFFERENT path is a different week, and must be storable
INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'window233b@x.com',0x00,@c); DECLARE @u2 INT=SCOPE_IDENTITY();
INSERT INTO LearnerPaths (UserId,CertId,CurrentBandCode,Status) VALUES (@u2,@c,'A2','active'); DECLARE @lp2 INT=SCOPE_IDENTITY();
INSERT INTO LearnerPathWeeks
  (LearnerPathId,CertId,WeekNumber,StartedOn,ClosedOn,CloseReason,PlannedSteps,CompletedSteps,
   PlannedMinutes,WrittenByService,ProjectionVersion)
VALUES (@lp2,@c,1,'2026-01-01','2026-01-07','calendar',3,3,40,'PathWindowSynthesizer',1);
PRINT 'accepted week 1 on a second path, because a week number is per path';

-- 5. the step's week is bounded below, not above: the window slides, so week 5 exists
INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,WeekNumber,WeekSequence)
  VALUES (@lp,@c,@sk,'reading',@t,'B1','focus_practice','developing',5,9);
PRINT 'accepted week 5, because the far end of a slid window is not week 4';

INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,WeekNumber,WeekSequence)
  VALUES (@lp,@c,@sk,'reading',@t,'B1','focus_practice','developing',52,10);
PRINT 'accepted week 52, because a window can slide for a year';

BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,WeekNumber,WeekSequence)
    VALUES (@lp,@c,@sk,'reading',@t,'B1','focus_practice','developing',0,11);
  THROW 50000, 'EXPECT_REJECT FAILED: a step in week zero was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a step in week zero';
END CATCH

-- and a step with no week is still legal: seed and hand-written rows carry none
INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,WeekSequence)
  VALUES (@lp,@c,@sk,'reading',@t,'B1','focus_practice','developing',12);
PRINT 'accepted a step with no week, which belongs to no window slot';

PRINT '#233: the window is stored, an archived week is written once and never restated, and a slid week number is legal';
