-- docs/schema-tests/domain39-rubric-ladder-lock.sql - #208: a lock cannot happen without a passing ladder.
--
-- What this file proves: the lock table records the ladder's verdict, only the ladder's own five codes
-- are storable, a verdict without a run time is refused, and the lock trigger refuses all four
-- not-accepted states by name - each with its OWN message, because monotonicity, separation and the
-- trap ceiling are three different rewrites.
--
-- What it cannot prove: that the ladder's arithmetic is right. That is RubricCalibrationLadderTests
-- (#193), which runs a scripted grader. No CHECK and no trigger can read a model's opinion.
--
-- Every rejection asserts the MESSAGE: a guard that refuses for the wrong reason stops refusing when
-- the clause it actually hits is edited, and this file's whole subject is which of four refusals fired.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TL',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'WRIT',N'Writing','writing',1);
DECLARE @wr INT=SCOPE_IDENTITY();

INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,RubricDescription,MaxScore,Weight,DisplayOrder) VALUES
  (@c,@wr,'W_TR',N'Task fulfilment',N'x',6,1.0,1),
  (@c,@wr,'W_CC',N'Coherence and cohesion',N'x',6,1.0,2),
  (@c,@wr,'W_LR',N'Lexical resource',N'x',6,1.0,3),
  (@c,@wr,'W_GR',N'Grammatical range',N'x',6,1.0,4);

-- the 28-cell grid, so every refusal below is about the ladder and not about a missing descriptor
INSERT INTO GradingCriterionDescriptors (CriterionId,Band,Descriptor,BinaryGates)
  SELECT c.CriterionId, b.Band,
         N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N': speech rate at least ' + CAST(60 + b.Band*20 AS NVARCHAR(4)) + N' wpm.',
         N'Error-free T-units at least ' + CAST(10 + b.Band*12 AS NVARCHAR(3)) + N'%.'
    FROM GradingCriteria c CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
   WHERE c.CertId=@c AND c.SkillId=@wr;
IF (SELECT COUNT(*) FROM GradingCriterionDescriptors d JOIN GradingCriteria c ON c.CriterionId=d.CriterionId
     WHERE c.CertId=@c AND c.SkillId=@wr) <> 28
  THROW 50000,'EXPECT_REJECT FAILED: the grid is not 28 rows',1;
PRINT 'accepted the 28-cell grid (the shape is complete for every refusal below)';

-- ===== the verdict's own shape ==============================================================
-- reject: a verdict code outside the ladder's five. A free-text column would let the studio write
-- 'pass' and the trigger's `<> accepted` check would refuse it - but the refusal would then be one
-- generic message instead of naming the failure, which is the defect this code set prevents.
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId,LadderOutcome,LadderReason,LadderRunAt)
    VALUES (@wr,@c,'good_enough',N'x',SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: an invented ladder outcome accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_GRL_LadderOutcome%' THROW 50000,'EXPECT_REJECT FAILED: invented outcome refused for the wrong reason',1;
  PRINT 'rejected an invented ladder outcome (CK_GRL_LadderOutcome)';
END CATCH

-- reject: a verdict with no run time. "This rubric passed" with no moment attached is a claim nobody
-- can date, and the whole point of storing the verdict is that it was reached on some responses.
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId,LadderOutcome,LadderReason,LadderRunAt)
    VALUES (@wr,@c,'accepted',N'x',NULL);
  THROW 50000,'EXPECT_REJECT FAILED: a verdict with no run time accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_GRL_LadderRunIsDated%' THROW 50000,'EXPECT_REJECT FAILED: undated verdict refused for the wrong reason',1;
  PRINT 'rejected a verdict with no run time (CK_GRL_LadderRunIsDated)';
END CATCH

-- reject: a run time with no verdict, the other direction of the same CHECK. A dated row that says
-- nothing would read as "we ran it and recorded no result", which the trigger would then treat as a
-- NULL outcome and refuse - but only after the author had already paid for seven grader calls.
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId,LadderOutcome,LadderReason,LadderRunAt)
    VALUES (@wr,@c,NULL,NULL,SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: a run time with no verdict accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_GRL_LadderRunIsDated%' THROW 50000,'EXPECT_REJECT FAILED: undated-verdict twin refused for the wrong reason',1;
  PRINT 'rejected a run time with no verdict (CK_GRL_LadderRunIsDated)';
END CATCH

-- ===== the lock trigger's four refusals =====================================================
-- THE DEFECT THIS TICKET CLOSES. Before #208 the statement below was ACCEPTED on a complete 28-cell
-- rubric: a rubric nobody calibrated could lock, and `LadderNotPlotted` - the code that exists to name
-- exactly this state - was reachable only from an ingestion call and never from the lock door.
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId) VALUES (@wr,@c);
  THROW 50000,'EXPECT_REJECT FAILED: a rubric with no ladder verdict locked',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%has been run and passed%' THROW 50000,'EXPECT_REJECT FAILED: unplotted lock refused for the wrong reason',1;
  IF ERROR_MESSAGE() NOT LIKE '%not plotted%' THROW 50000,'EXPECT_REJECT FAILED: the unplotted refusal did not say the ladder was not plotted',1;
  PRINT 'rejected locking a complete rubric whose ladder never ran (50091)';
END CATCH

-- reject: an explicitly not-plotted verdict. Same door as NULL, spelled out, because the runner writes
-- this code when a grader call failed and the author has to be told the ladder did not run rather than
-- that their rubric is bad.
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId,LadderOutcome,LadderReason,LadderRunAt)
    VALUES (@wr,@c,'not_plotted',N'The grader returned no usable result for band 4.',SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: an explicit not_plotted verdict locked',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%not plotted%' THROW 50000,'EXPECT_REJECT FAILED: not_plotted refused for the wrong reason',1;
  PRINT 'rejected locking on a not_plotted verdict (50091)';
END CATCH

-- reject: monotonicity. Distinct from separation: here a higher band scored NO better, so the fix is
-- to move a descriptor, not to widen one. A single generic "not calibrated" refusal would send the
-- author to the wrong edit - which is why each of the three has its own code and its own sentence.
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId,LadderOutcome,LadderReason,LadderRunAt)
    VALUES (@wr,@c,'not_monotonic',N'Band 4 scored 3.00 where band 3 scored 3.00. Criterion W_CC did not move.',SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: a non-monotonic ladder locked',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%not strictly monotonic%' THROW 50000,'EXPECT_REJECT FAILED: monotonicity refused for the wrong reason',1;
  IF ERROR_MESSAGE() NOT LIKE '%gate4.ladder.monotonic%' THROW 50000,'EXPECT_REJECT FAILED: the monotonicity refusal did not name its rule code',1;
  IF ERROR_MESSAGE() LIKE '%minimum separation%' THROW 50000,'EXPECT_REJECT FAILED: monotonicity and separation collapsed into one message',1;
  PRINT 'rejected locking on a non-monotonic ladder and named gate4.ladder.monotonic (50092)';
END CATCH

-- reject: separation. Every segment rises, so "is it monotonic" says yes; one rises by too little.
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId,LadderOutcome,LadderReason,LadderRunAt)
    VALUES (@wr,@c,'separation',N'Bands 3 and 4 are only 0.40 apart. Criterion W_GR moved the least.',SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: a ladder below its separation threshold locked',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%minimum separation%' THROW 50000,'EXPECT_REJECT FAILED: separation refused for the wrong reason',1;
  IF ERROR_MESSAGE() NOT LIKE '%gate4.ladder.separation%' THROW 50000,'EXPECT_REJECT FAILED: the separation refusal did not name its rule code',1;
  IF ERROR_MESSAGE() LIKE '%not strictly monotonic%' THROW 50000,'EXPECT_REJECT FAILED: separation and monotonicity collapsed into one message',1;
  PRINT 'rejected locking on a too-narrow separation and named gate4.ladder.separation (50093)';
END CATCH

-- reject: the trap ceiling. The ladder rises and separates perfectly; the verbose trap still climbs.
-- This is the arm a "the ladder passed" guard with two branches leaves open.
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId,LadderOutcome,LadderReason,LadderRunAt)
    VALUES (@wr,@c,'trap_ceiling',N'The verbose trap scored 3.80 and must stay at or below 3.25.',SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: a ladder over the trap ceiling locked',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%verbose trap%' THROW 50000,'EXPECT_REJECT FAILED: trap ceiling refused for the wrong reason',1;
  IF ERROR_MESSAGE() NOT LIKE '%gate4.ladder.trap%' THROW 50000,'EXPECT_REJECT FAILED: the trap refusal did not name its rule code',1;
  PRINT 'rejected locking over the verbose trap ceiling and named gate4.ladder.trap (50094)';
END CATCH

-- the four refused INSERTs left no row behind, so the accept below is a real first lock
IF EXISTS (SELECT 1 FROM GradingRubricLocks WHERE SkillId=@wr)
  THROW 50000,'EXPECT_REJECT FAILED: a refused lock left a row behind',1;

-- accept: an accepted verdict locks, and the verdict is readable afterwards. Without this arm every
-- guard above would still pass with a trigger that refused every lock.
INSERT INTO GradingRubricLocks (SkillId,CertId,LadderOutcome,LadderReason,LadderRunAt)
  VALUES (@wr,@c,'accepted',N'Every adjacent band separated by at least 0.65 and the trap stayed at 3.10.',SYSDATETIMEOFFSET());
  EXEC ExpectFixtureRows N'domain39-rubric-ladder-lock.sql: the lock row';
PRINT 'accepted a lock carrying an accepted ladder verdict';

-- and the lock still freezes the grid: #208 must not have opened a door #192 closed
BEGIN TRY
  UPDATE GradingCriterionDescriptors SET Descriptor=N'Band 5: edited after lock.'
   WHERE CriterionId=(SELECT CriterionId FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr AND Code='W_LR') AND Band=5;
  THROW 50000,'EXPECT_REJECT FAILED: descriptor edited on the #208 lock accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%locked%' THROW 50000,'EXPECT_REJECT FAILED: post-lock edit refused for the wrong reason',1;
  PRINT 'the #208 lock still freezes the descriptor grid';
END CATCH

-- a second skill on the same certificate, so the "any accepted lock is enough" mutation has a target:
-- a NOT-accepted verdict on skill two must be refused even though skill one is already locked.
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'Speaking','speaking',2);
DECLARE @sp INT=SCOPE_IDENTITY();
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,RubricDescription,MaxScore,Weight,DisplayOrder) VALUES
  (@c,@sp,'S_FC',N'Fluency',N'x',6,1.0,1),
  (@c,@sp,'S_PR',N'Pronunciation',N'x',6,1.0,2),
  (@c,@sp,'S_LR',N'Lexical resource',N'x',6,1.0,3),
  (@c,@sp,'S_GR',N'Grammatical range',N'x',6,1.0,4);
INSERT INTO GradingCriterionDescriptors (CriterionId,Band,Descriptor,BinaryGates)
  SELECT c.CriterionId, b.Band,
         N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N' fixture.', N'Gate fixture.'
    FROM GradingCriteria c CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
   WHERE c.CertId=@c AND c.SkillId=@sp;

BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId,LadderOutcome,LadderReason,LadderRunAt)
    VALUES (@sp,@c,'separation',N'Bands 4 and 5 are only 0.30 apart.',SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: a second skill locked on a failed ladder because another skill passed',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%minimum separation%' THROW 50000,'EXPECT_REJECT FAILED: the second skill was refused for the wrong reason',1;
  PRINT 'the refusal is per skill: another skill''s accepted ladder did not vouch for this one';
END CATCH

PRINT 'rubric ladder lock constraints hold';
