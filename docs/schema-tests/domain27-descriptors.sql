-- docs/schema-tests/domain27-descriptors.sql - #192: a descriptor at every band.
--
-- What this file can prove and what it cannot: the schema refuses a rubric that is not seven rows of
-- text per criterion, in bands 0..6, on the canonical four codes, locked only when complete. It does
-- NOT prove the text states an observable threshold - "Band 4: writes clearly" passes every check
-- below and still grades nothing. That is the calibration ladder's job (#193), and no CHECK can read.
--
-- Every rejection asserts the MESSAGE, not merely that something was thrown. A guard that refuses for
-- the wrong reason is a guard that will stop refusing when the real clause is edited.
--
-- The descriptor text here is fixture text, not a rubric. It exists to fill the grid so the guards
-- have a complete set to stand on.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TW',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'WRIT',N'Writing','writing',1);
DECLARE @wr INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'LIST',N'Listening','listening',2);
DECLARE @li INT=SCOPE_IDENTITY();

-- an unlocked skill keeps full authoring freedom: three criteria, non-canonical codes, no descriptors.
-- Every seeded and legacy rubric is in this shape today, so the lock must not arrive as a blanket ban.
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,RubricDescription,MaxScore,Weight,DisplayOrder) VALUES
  (@c,@li,'L1',N'Listening one',N'x',6,1.0,1),
  (@c,@li,'L2',N'Listening two',N'x',6,1.0,2),
  (@c,@li,'L3',N'Listening three',N'x',6,1.0,3);
PRINT 'accepted three non-canonical criteria on an unlocked skill';

-- the canonical four for a writing skill
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,RubricDescription,MaxScore,Weight,DisplayOrder) VALUES
  (@c,@wr,'W_TR',N'Task fulfilment',N'x',6,1.0,1),
  (@c,@wr,'W_CC',N'Coherence and cohesion',N'x',6,1.0,2),
  (@c,@wr,'W_LR',N'Lexical resource',N'x',6,1.0,3),
  (@c,@wr,'W_GR',N'Grammatical range',N'x',6,1.0,4);

-- the 28-cell grid. The text is deliberately the shape D0.6 asks for (a rate, a pause count, a
-- T-unit share) so the file also documents what a good descriptor looks like; nothing below verifies
-- that shape, and the file says so at the top.
INSERT INTO GradingCriterionDescriptors (CriterionId,Band,Descriptor,BinaryGates)
  SELECT c.CriterionId, b.Band,
         N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N': speech rate at least ' + CAST(60 + b.Band*20 AS NVARCHAR(4))
           + N' wpm, fewer than ' + CAST(10 - b.Band AS NVARCHAR(2)) + N' pauses over 1500 ms per minute.',
         N'Error-free T-units at least ' + CAST(10 + b.Band*12 AS NVARCHAR(3)) + N'%.'
    FROM GradingCriteria c
    CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
   WHERE c.CertId=@c AND c.SkillId=@wr;
IF (SELECT COUNT(*) FROM GradingCriteria c JOIN GradingCriterionDescriptors d ON d.CriterionId=c.CriterionId
     WHERE c.CertId=@c AND c.SkillId=@wr) <> 28
  THROW 50000,'EXPECT_REJECT FAILED: the grid is not 28 rows',1;
PRINT 'accepted the 28-cell descriptor grid';

-- reject: a band outside 0..6
BEGIN TRY
  INSERT INTO GradingCriterionDescriptors (CriterionId,Band,Descriptor,BinaryGates)
    SELECT TOP 1 CriterionId, 7, N'x', N'y' FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr;
  THROW 50000,'EXPECT_REJECT FAILED: band 7 accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_GCD_Band%' THROW 50000,'EXPECT_REJECT FAILED: wrong reason for band 7',1;
  PRINT 'rejected band 7 (CK_GCD_Band)';
END CATCH

-- reject: a blank descriptor. The band exists but says nothing, which is the defect this table
-- exists to kill, so whitespace is not "a descriptor".
BEGIN TRY
  UPDATE GradingCriterionDescriptors SET Descriptor=N'   '
   WHERE CriterionId=(SELECT TOP 1 CriterionId FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr) AND Band=3;
  THROW 50000,'EXPECT_REJECT FAILED: blank descriptor accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_GCD_Descriptor%' THROW 50000,'EXPECT_REJECT FAILED: wrong reason for blank descriptor',1;
  PRINT 'rejected a blank descriptor (CK_GCD_Descriptor)';
END CATCH

-- reject: a blank gate set - a floor gate that names no gate
BEGIN TRY
  UPDATE GradingCriterionDescriptors SET BinaryGates=N''
   WHERE CriterionId=(SELECT TOP 1 CriterionId FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr) AND Band=3;
  THROW 50000,'EXPECT_REJECT FAILED: blank binary gates accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_GCD_BinaryGates%' THROW 50000,'EXPECT_REJECT FAILED: wrong reason for blank gates',1;
  PRINT 'rejected blank binary gates (CK_GCD_BinaryGates)';
END CATCH

-- reject: a duplicate band for one criterion would give 8 rows for 7 bands
BEGIN TRY
  INSERT INTO GradingCriterionDescriptors (CriterionId,Band,Descriptor,BinaryGates)
    SELECT TOP 1 CriterionId, 3, N'second band 3', N'gate'
      FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr;
  THROW 50000,'EXPECT_REJECT FAILED: duplicate band accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF OBJECT_NAME(ERROR_NUMBER()) IS NOT NULL THROW 50000,'EXPECT_REJECT FAILED: duplicate band failed for a non-key reason',1;
  PRINT 'rejected a duplicate band (PK)';
END CATCH

-- accept: an unlocked criterion's descriptor is still editable (the lock, not the table, is the gate)
UPDATE GradingCriterionDescriptors SET Descriptor=N'Band 3: speech rate at least 120 wpm, at most 4 pauses over 1500 ms per minute.'
 WHERE CriterionId=(SELECT TOP 1 CriterionId FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr) AND Band=3;
PRINT 'accepted editing a descriptor on an unlocked rubric';

-- reject: a criterion one band short of the grid. Six rows, not seven. The refusal must name the gap.
DELETE FROM GradingCriterionDescriptors
 WHERE CriterionId=(SELECT CriterionId FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr AND Code='W_GR') AND Band=6;
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId) VALUES (@wr,@c);
  THROW 50000,'EXPECT_REJECT FAILED: rubric locked with six bands accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%W_GR%missing a descriptor%' THROW 50000,'EXPECT_REJECT FAILED: six-band refusal did not name the criterion',1;
  IF ERROR_MESSAGE() NOT LIKE '%: 6.%' THROW 50000,'EXPECT_REJECT FAILED: six-band refusal did not name band 6',1;
  PRINT 'rejected locking a rubric with six bands, and named W_GR and band 6';
END CATCH
-- restore the seventh band: the next assertions are about the full grid
INSERT INTO GradingCriterionDescriptors (CriterionId,Band,Descriptor,BinaryGates)
  SELECT CriterionId, 6, N'Band 6: speech rate at least 180 wpm, no pause over 1500 ms per minute.', N'Error-free T-units at least 82%.'
    FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr AND Code='W_GR';

-- reject: a fifth criterion, even a complete one. Four is the rubric.
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,RubricDescription,MaxScore,Weight,DisplayOrder)
  VALUES (@c,@wr,'W_XT',N'Extra',N'x',6,1.0,5);
INSERT INTO GradingCriterionDescriptors (CriterionId,Band,Descriptor,BinaryGates)
  SELECT c.CriterionId, b.Band, N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N' fixture.', N'Gate fixture.'
    FROM GradingCriteria c CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
   WHERE c.CertId=@c AND c.SkillId=@wr AND c.Code='W_XT';
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId) VALUES (@wr,@c);
  THROW 50000,'EXPECT_REJECT FAILED: rubric locked with a fifth criterion accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%canonical four%' THROW 50000,'EXPECT_REJECT FAILED: fifth criterion refused for the wrong reason',1;
  PRINT 'rejected locking a rubric with five criteria (canonical-four rule)';
END CATCH
-- descriptors first: the criterion FK is what a truthful delete has to respect
DELETE FROM GradingCriterionDescriptors
 WHERE CriterionId=(SELECT CriterionId FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr AND Code='W_XT');
DELETE FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr AND Code='W_XT';

-- reject: four rows is not four canonical codes. PRON/INT/GRAM/REL is the seeded TOEIC rubric, and a
-- rubric cannot be locked under it: the canonical codes are what the grader and the band cuts read.
UPDATE GradingCriteria SET Code='TR' WHERE CertId=@c AND SkillId=@wr AND Code='W_TR';
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId) VALUES (@wr,@c);
  THROW 50000,'EXPECT_REJECT FAILED: rubric locked under legacy codes accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%canonical four%' THROW 50000,'EXPECT_REJECT FAILED: legacy codes refused for the wrong reason',1;
  PRINT 'rejected locking a rubric whose four criteria are not the canonical four';
END CATCH
UPDATE GradingCriteria SET Code='W_TR' WHERE CertId=@c AND SkillId=@wr AND Code='TR';

-- reject: a listening skill has no analytic rubric, so it cannot be locked, and the reason says
-- "objective skill", not "missing descriptor" - those are different fixes.
INSERT INTO GradingCriterionDescriptors (CriterionId,Band,Descriptor,BinaryGates)
  SELECT c.CriterionId, b.Band, N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N' fixture.', N'Gate fixture.'
    FROM GradingCriteria c CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
   WHERE c.CertId=@c AND c.SkillId=@li;
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId) VALUES (@li,@c);
  THROW 50000,'EXPECT_REJECT FAILED: listening rubric locked accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%objective skill%' THROW 50000,'EXPECT_REJECT FAILED: listening lock refused for the wrong reason',1;
  PRINT 'rejected locking a listening rubric (objective skill)';
END CATCH

-- reject: a lock on a skill of another certificate. The lock must not be able to name a certificate
-- that does not own the skill. Refused by the composite FK, which is the point: certificate isolation
-- is a foreign key here, not a convention.
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TX',N'TX',1); DECLARE @cForeign INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO GradingRubricLocks (SkillId,CertId) VALUES (@wr,@cForeign);
  THROW 50000,'EXPECT_REJECT FAILED: lock named a foreign certificate',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_GradingRubricLocks_SkillCert%'
    THROW 50000,'EXPECT_REJECT FAILED: foreign certificate refused for a reason other than the composite FK',1;
  PRINT 'rejected a lock naming a certificate the skill does not belong to (FK_GradingRubricLocks_SkillCert)';
END CATCH

-- accept: the complete canonical four locks
INSERT INTO GradingRubricLocks (SkillId,CertId) VALUES (@wr,@c);
  EXEC ExpectFixtureRows N'domain27-descriptors.sql: the lock row';
PRINT 'accepted a complete, canonical, seven-band rubric lock';

-- once locked: no fifth criterion, no rename, no removal
BEGIN TRY
  INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,RubricDescription,MaxScore,Weight,DisplayOrder)
    VALUES (@c,@wr,'W_CC2',N'Second cohesion',N'x',6,1.0,6);
  THROW 50000,'EXPECT_REJECT FAILED: fifth criterion added to a locked rubric',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%locked%' THROW 50000,'EXPECT_REJECT FAILED: fifth-after-lock refused for the wrong reason',1;
  PRINT 'rejected a fifth criterion on a locked rubric';
END CATCH
BEGIN TRY
  UPDATE GradingCriteria SET Code='W_ZZ' WHERE CertId=@c AND SkillId=@wr AND Code='W_CC';
  THROW 50000,'EXPECT_REJECT FAILED: locked criterion renamed',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected renaming a locked criterion';
END CATCH
BEGIN TRY
  DELETE FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr AND Code='W_CC';
  THROW 50000,'EXPECT_REJECT FAILED: locked criterion deleted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected deleting a locked criterion';
END CATCH

-- once locked: the descriptor grid is frozen from all three sides. Insert, update and delete are
-- tested separately, because a guard written against `inserted` alone is silent on a delete.
DECLARE @crit INT=(SELECT CriterionId FROM GradingCriteria WHERE CertId=@c AND SkillId=@wr AND Code='W_LR');
BEGIN TRY
  UPDATE GradingCriterionDescriptors SET Descriptor=N'Band 5: edited after lock.'
   WHERE CriterionId=@crit AND Band=5;
  THROW 50000,'EXPECT_REJECT FAILED: descriptor edited on a locked rubric',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected editing a descriptor on a locked rubric';
END CATCH
BEGIN TRY
  DELETE FROM GradingCriterionDescriptors WHERE CriterionId=@crit AND Band=5;
  THROW 50000,'EXPECT_REJECT FAILED: descriptor deleted on a locked rubric',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected deleting a descriptor on a locked rubric';
END CATCH
-- An INSERT onto a locked criterion cannot be reached: the PK allows one row per (criterion, band) and
-- a locked rubric already has all seven, so no free band exists. The two arms above are the whole
-- reachable surface for insert-adjacent change; a test for the insert arm would be testing the PK.

-- the accept direction of the freeze: an unlocked rubric's descriptor is still editable
UPDATE GradingCriterionDescriptors SET Descriptor=N'Band 4: speech rate at least 140 wpm.'
 WHERE CriterionId=(SELECT TOP 1 d.CriterionId FROM GradingCriterionDescriptors d
                      JOIN GradingCriteria c2 ON c2.CriterionId=d.CriterionId
                     WHERE c2.SkillId=@li) AND Band=4;
PRINT 'accepted editing a descriptor on an unlocked rubric while another rubric is locked';

-- ===== the lock is not reversible once it has graded something =====
-- full grading chain, because a score needs a response to belong to
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @role INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@role,'d27@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount,DisplayOrder)
  VALUES (@wr,@c,'writing','W1',N'Essay',4,1,1); DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'D27','published',60,'writing_practice');
DECLARE @ex INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@ex,@c,@sec,1); DECLARE @g INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,MinWords,DisplayOrder)
  VALUES (@g,@ex,@sec,@wr,N'Essay',3,'free_response',100,1); DECLARE @q INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt) VALUES (@u,@ex,@c,'submitted',SYSDATETIMEOFFSET());
DECLARE @a INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,SkillId,QuestionType,ResponseText)
  VALUES (@a,@q,@ex,@wr,'free_response',N'Some text.'); DECLARE @fr INT=SCOPE_IDENTITY();
INSERT INTO AiGradings (FreeResponseId,SkillId,Status) VALUES (@fr,@wr,'pending'); DECLARE @grd INT=SCOPE_IDENTITY();
INSERT INTO AiGradingScores (GradingId,CriterionId,SkillId,Score) VALUES (@grd,@crit,@wr,4);
  EXEC ExpectFixtureRows N'domain27-descriptors.sql: a score against the locked rubric';
PRINT 'accepted a score against the locked rubric';

BEGIN TRY
  DELETE FROM GradingRubricLocks WHERE SkillId=@wr AND CertId=@c;
  THROW 50000,'EXPECT_REJECT FAILED: a rubric that has graded was unlocked',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%already graded%' THROW 50000,'EXPECT_REJECT FAILED: unlock refused for the wrong reason',1;
  PRINT 'rejected unlocking a rubric that has graded';
END CATCH

-- reject: an upgrade in place. "Unlock, edit, re-lock" is not a hole: the unlock of a graded rubric
-- is refused above, so the only path is a new rubric row.
IF EXISTS (SELECT 1 FROM GradingRubricLocks WHERE SkillId=@wr)
  PRINT 'the graded rubric is still locked after the refused unlock';
ELSE
  THROW 50000,'EXPECT_REJECT FAILED: the refused unlock removed the lock',1;
