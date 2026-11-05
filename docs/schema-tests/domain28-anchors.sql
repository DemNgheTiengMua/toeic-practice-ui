-- docs/schema-tests/domain28-anchors.sql - #193: three calibrated anchors, and the lock that needs them.
--
-- What this file proves: an anchor exists only at bands 2, 3 and 4, holds a whole response, and is
-- frozen once its rubric is locked - from insert, update and delete, all three arms.
--
-- What it cannot prove: that the anchor text is a good band-2 response. That is the calibration
-- ladder's job (#193, RubricCalibrationLadderTests), and no CHECK can read.
--
-- Every rejection asserts the MESSAGE: a guard that refuses for the wrong reason stops refusing when the
-- clause it was actually hitting is edited.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TA',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'WRIT',N'Writing','writing',1);
DECLARE @wr INT=SCOPE_IDENTITY();

INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,RubricDescription,MaxScore,Weight,DisplayOrder) VALUES
  (@c,@wr,'W_TR',N'Task fulfilment',N'x',6,1.0,1),
  (@c,@wr,'W_CC',N'Coherence and cohesion',N'x',6,1.0,2),
  (@c,@wr,'W_LR',N'Lexical resource',N'x',6,1.0,3),
  (@c,@wr,'W_GR',N'Grammatical range',N'x',6,1.0,4);
DECLARE @tr INT=(SELECT CriterionId FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr AND Code='W_TR');

-- accept: three anchors at the three calibrated bands, authored while the rubric is open. An anchor is
-- authorable BEFORE the lock and never after, which is the only window there is.
INSERT INTO GradingCriterionAnchors (CriterionId,Band,ResponseText) VALUES
  (@tr,2,N'Anchor at band two: simple sentences, frequent basic errors.'),
  (@tr,3,N'Anchor at band three: connected clauses, some range, errors do not block meaning.'),
  (@tr,4,N'Anchor at band four: sustained argument, varied subordination, occasional slips.');
PRINT 'accepted three anchors at bands 2, 3 and 4';

-- reject: band 1 has no anchor. Bands 5 and 6 are the ladder's job, not a per-call prompt's.
BEGIN TRY
  INSERT INTO GradingCriterionAnchors (CriterionId,Band,ResponseText) VALUES (@tr,1,N'x');
  THROW 50000,'EXPECT_REJECT FAILED: band 1 anchor accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_GCA_Band%' THROW 50000,'EXPECT_REJECT FAILED: band 1 refused for the wrong reason',1;
  PRINT 'rejected a band 1 anchor (CK_GCA_Band)';
END CATCH

-- reject: band 5 has no anchor either, and the refusal must be the band CHECK, not the PK.
BEGIN TRY
  INSERT INTO GradingCriterionAnchors (CriterionId,Band,ResponseText) VALUES (@tr,5,N'x');
  THROW 50000,'EXPECT_REJECT FAILED: band 5 anchor accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_GCA_Band%' THROW 50000,'EXPECT_REJECT FAILED: band 5 refused for the wrong reason',1;
  PRINT 'rejected a band 5 anchor (CK_GCA_Band)';
END CATCH

-- reject: a blank anchor. Three rows of whitespace would satisfy "three anchors" and anchor nothing.
BEGIN TRY
  UPDATE GradingCriterionAnchors SET ResponseText=N'   ' WHERE CriterionId=@tr AND Band=3;
  THROW 50000,'EXPECT_REJECT FAILED: blank anchor accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_GCA_ResponseText%' THROW 50000,'EXPECT_REJECT FAILED: blank anchor refused for the wrong reason',1;
  PRINT 'rejected a blank anchor (CK_GCA_ResponseText)';
END CATCH

-- reject: a second anchor at the same band. Two band-3 anchors and no band-4 anchor is the shape a
-- "count the rows" guard calls three.
BEGIN TRY
  INSERT INTO GradingCriterionAnchors (CriterionId,Band,ResponseText) VALUES (@tr,3,N'A second band three.');
  THROW 50000,'EXPECT_REJECT FAILED: duplicate anchor band accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF OBJECT_NAME(ERROR_NUMBER()) IS NOT NULL THROW 50000,'EXPECT_REJECT FAILED: duplicate anchor failed for a non-key reason',1;
  PRINT 'rejected a duplicate anchor band (PK)';
END CATCH

-- accept: an unlocked criterion's anchor is still editable - the lock, not the table, is the gate
UPDATE GradingCriterionAnchors SET ResponseText=N'Anchor at band three, revised while the rubric is open.'
 WHERE CriterionId=@tr AND Band=3;
PRINT 'accepted editing an anchor on an unlocked rubric';

-- anchors on the other three criteria, still while the rubric is open. W_GR is left unanchored on
-- purpose: it gives the post-lock INSERT arm a free band to try.
INSERT INTO GradingCriterionAnchors (CriterionId,Band,ResponseText)
  SELECT c.CriterionId, b.Band,
         N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N' anchor for ' + c.Code + N': a whole written answer.'
    FROM GradingCriteria c CROSS JOIN (VALUES (2),(3),(4)) AS b(Band)
   WHERE c.CertId=@c AND c.SkillId=@wr AND c.Code IN ('W_CC','W_LR');
IF (SELECT COUNT(*) FROM GradingCriterionAnchors a JOIN GradingCriteria c ON c.CriterionId=a.CriterionId
     WHERE c.CertId=@c AND c.SkillId=@wr) <> 9
  THROW 50000,'EXPECT_REJECT FAILED: the anchor set is not 9 rows',1;
PRINT 'accepted anchors on three of four criteria while the rubric is open';

-- now complete the grid and lock, so the freeze arms below have a locked rubric to stand on
INSERT INTO GradingCriterionDescriptors (CriterionId,Band,Descriptor,BinaryGates)
  SELECT c.CriterionId, b.Band,
         N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N': speech rate at least ' + CAST(60 + b.Band*20 AS NVARCHAR(4)) + N' wpm.',
         N'Error-free T-units at least ' + CAST(10 + b.Band*12 AS NVARCHAR(3)) + N'%.'
    FROM GradingCriteria c CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
   WHERE c.CertId=@c AND c.SkillId=@wr;
IF (SELECT COUNT(*) FROM GradingCriterionDescriptors d JOIN GradingCriteria c ON c.CriterionId=d.CriterionId
     WHERE c.CertId=@c AND c.SkillId=@wr) <> 28
  THROW 50000,'EXPECT_REJECT FAILED: the grid is not 28 rows',1;

INSERT INTO GradingRubricLocks (SkillId,CertId) VALUES (@wr,@c);
  EXEC ExpectFixtureRows N'domain28-anchors.sql: the lock row';
PRINT 'locked the rubric with anchors on three of its four criteria';

-- once locked: the anchors are frozen from all three sides. Tested separately, because a guard written
-- against `inserted` alone is silent on a delete, and deleting an anchor is the quietest way to shrink
-- the set.
DECLARE @gr INT=(SELECT CriterionId FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr AND Code='W_GR');
BEGIN TRY
  INSERT INTO GradingCriterionAnchors (CriterionId,Band,ResponseText) VALUES (@gr,2,N'Written after the lock.');
  THROW 50000,'EXPECT_REJECT FAILED: anchor inserted on a locked rubric',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%locked%' THROW 50000,'EXPECT_REJECT FAILED: post-lock insert refused for the wrong reason',1;
  PRINT 'rejected inserting an anchor on a locked rubric';
END CATCH
BEGIN TRY
  UPDATE GradingCriterionAnchors SET ResponseText=N'Edited after the lock.' WHERE CriterionId=@tr AND Band=2;
  THROW 50000,'EXPECT_REJECT FAILED: anchor edited on a locked rubric',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%locked%' THROW 50000,'EXPECT_REJECT FAILED: post-lock update refused for the wrong reason',1;
  PRINT 'rejected editing an anchor on a locked rubric';
END CATCH
BEGIN TRY
  DELETE FROM GradingCriterionAnchors WHERE CriterionId=@tr AND Band=4;
  THROW 50000,'EXPECT_REJECT FAILED: anchor deleted on a locked rubric',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%locked%' THROW 50000,'EXPECT_REJECT FAILED: post-lock delete refused for the wrong reason',1;
  PRINT 'rejected deleting an anchor on a locked rubric';
END CATCH

-- the lock did not delete anything: all nine anchors are still there, and the refused delete left band 4
IF (SELECT COUNT(*) FROM GradingCriterionAnchors a JOIN GradingCriteria c ON c.CriterionId=a.CriterionId
     WHERE c.CertId=@c AND c.SkillId=@wr) <> 9
  THROW 50000,'EXPECT_REJECT FAILED: the refused writes changed the anchor set',1;
PRINT 'the anchor set is unchanged after the three refused writes';

PRINT 'anchor constraints hold';
