SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #211 / D15: the learner's per-skill state. One row per learner per skill, carrying the level, the
-- position inside it, the continuous coordinate the level was projected from, where the measurement
-- came from, and when it expires.
--
-- Three things are asserted here that a plausible implementation gets wrong while looking right:
--
--   1. **No overall is stored.** The overall is the weakest measured skill, computed at read. A
--      committed `Overall` column, a computed column, or an "overall" pseudo-skill row are three
--      different ways to store it, and a test that only checked the C# code for a property would miss
--      two of them. The audit below is a catalog scan, and it is proved non-empty first, because a
--      scan for a name nothing has reports "absent" for every table and passes without looking.
--   2. **The writer is the statement's declaration, not the stored value.** A second writer that
--      changes only PositionInBand leaves WrittenByService holding SkillStateWriter from the previous
--      write. A guard comparing the value passes it. `UPDATE(WrittenByService)` refuses it.
--   3. **Expiry is the measurement plus the window, not an opinion.** A CHECK that only demanded
--      "later than MeasuredAt" would accept 91 days and 365 days. Both are refused below.
--
-- Every refusal uses a row that is valid in every other respect. A fixture that also broke a CHECK or
-- a foreign key would be refused before the guard under test ran, and the test would go green with
-- that guard deleted.

DECLARE @msg NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. shape: one row per learner per skill, and the five facts the ticket names are columns
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID('LearnerSkillStates') IS NULL
  THROW 50000, 'EXPECT_REJECT FAILED: LearnerSkillStates does not exist, so no screen can read one answer', 1;

-- "one row per learner per skill" is the primary key, not a convention: two rows for one learner and
-- one skill is two answers to "what is my level", which is the defect D15 exists to remove.
IF NOT EXISTS (
  SELECT 1
  FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
  JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
    ON k.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND k.TABLE_NAME = tc.TABLE_NAME
  WHERE tc.TABLE_NAME = 'LearnerSkillStates' AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
  GROUP BY tc.CONSTRAINT_NAME
  HAVING COUNT(*) = 3
     AND SUM(CASE WHEN k.COLUMN_NAME IN ('UserId','CertId','SkillId') THEN 1 ELSE 0 END) = 3
)
  THROW 50000, 'EXPECT_REJECT FAILED: the key of LearnerSkillStates is not (UserId, CertId, SkillId)', 1;

DECLARE @required TABLE (ColumnName SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @required (ColumnName) VALUES
  ('CefrLevel'), ('PositionInBand'), ('ContinuousCoordinate'), ('Source'),
  ('MeasuredAt'), ('ExpiresAt'), ('ProjectionVersion');
IF EXISTS (
  SELECT 1 FROM @required r
  WHERE NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS c
    WHERE c.TABLE_NAME = 'LearnerSkillStates' AND c.COLUMN_NAME = r.ColumnName AND c.IS_NULLABLE = 'NO')
)
  THROW 50000, 'EXPECT_REJECT FAILED: a required per-skill state column is missing or nullable', 1;
-- The writer column is nullable here for the same reason it is on the other three guarded tables:
-- they predate per-write enforcement, and the trigger - not the column - is what refuses a write with
-- no declaration. Asserting NOT NULL here would be a stricter rule than #191 shipped.
IF NOT EXISTS (
  SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_NAME = 'LearnerSkillStates' AND COLUMN_NAME = 'WrittenByService' AND IS_NULLABLE = 'YES')
  THROW 50000, 'EXPECT_REJECT FAILED: WrittenByService does not follow the #191 nullable-column convention', 1;
PRINT 'LearnerSkillStates holds one row per learner per skill and carries level, position, coordinate, source and expiry';

-- ---------------------------------------------------------------------------------------------
-- 2. the fixtures
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'skillstate211@x.com', 0x01);
DECLARE @user INT = SCOPE_IDENTITY();
-- A second learner, so each refusal below is the FIRST row for its (learner, skill) and the only thing
-- that can refuse it is the guard under test - never the primary key, and never another certificate's
-- skill. A fixture that fails on the key instead proves nothing about the CHECK it was written for.
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'skillstate211b@x.com', 0x01);
DECLARE @user2 INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'skillstate211c@x.com', 0x01);
DECLARE @user3 INT = SCOPE_IDENTITY();
-- One learner per shape refusal. Each of the five CHECKs below is exercised on the FIRST row for its
-- (learner, skill) pair, so no refusal can be produced by the primary key instead of the rule under
-- test. The first draft of this file reused one learner and one skill throughout, and
-- prove-guard-skill-state.sh reported four of the five CHECKs as NOT LOAD-BEARING: the fixture was
-- being refused by the duplicate key, so the test passed with the CHECK deleted.
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'skillstate211d@x.com', 0x01);
DECLARE @uPos INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'skillstate211e@x.com', 0x01);
DECLARE @uLvl INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'skillstate211f@x.com', 0x01);
DECLARE @uCoord INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'skillstate211g@x.com', 0x01);
DECLARE @uSrc INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'skillstate211h@x.com', 0x01);
DECLARE @uExp INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'skillstate211i@x.com', 0x01);
DECLARE @uIso INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'skillstate211j@x.com', 0x01);
DECLARE @uDup INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('SS211', N'Skill state', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
  VALUES (@cert, 'LIST', N'Listening', 'listening', 1);
DECLARE @list INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
  VALUES (@cert, 'READ', N'Reading', 'reading', 2);
DECLARE @read INT = SCOPE_IDENTITY();
-- a third skill, so a wrong-writer INSERT can differ from the known-good rows by exactly its
-- declaration rather than colliding on the primary key first
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
  VALUES (@cert, 'SPEA', N'Speaking', 'speaking', 3);
DECLARE @spea INT = SCOPE_IDENTITY();
-- a second certificate, whose skill must not be storable under the first
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('SS211B', N'Other certificate', 1);
DECLARE @otherCert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
  VALUES (@otherCert, 'WRIT', N'Writing', 'writing', 1);
DECLARE @otherSkill INT = SCOPE_IDENTITY();

-- ---------------------------------------------------------------------------------------------
-- 3. the owner writes a row, and every scale boundary the row states is refused when invented
-- ---------------------------------------------------------------------------------------------
-- The accepted row sets everything the fact is made of, so every later refusal differs from a
-- known-good row in exactly one respect.
INSERT INTO LearnerSkillStates
  (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
   MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
VALUES
  (@user, @cert, @list, 'B1', 42, 3.62, 'placement',
   CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
   DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)),
   'SkillStateWriter', 1);
PRINT 'accepted a per-skill state row written by SkillStateWriter';

-- a second skill of the same learner is a second row, not a violation: the guard is per row
INSERT INTO LearnerSkillStates
  (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
   MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
VALUES
  (@user, @cert, @read, 'B2', 71, 4.71, 'placement',
   CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
   DATEADD(DAY, 90, CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET)),
   'SkillStateWriter', 1);
PRINT 'accepted a second skill for the same learner';

-- POSITION: 101 is outside the 0-100 band. A `>= 0` CHECK passes it, so the upper bound is the half
-- worth testing.
BEGIN TRY
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@uPos, @cert, @list, 'B1', 101, 3.62, 'placement',
          SYSDATETIMEOFFSET(), DATEADD(DAY, 90, SYSDATETIMEOFFSET()), 'SkillStateWriter', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a position of 101 inside a 0-100 band was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a position above 100';
END CATCH

-- LEVEL: the reportable set is the pre-A1 floor plus A1-C2. A value the enum cannot read is a row no
-- reader can interpret, so the CHECK is what stops it, not a C# parse that throws at read time.
BEGIN TRY
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@uLvl, @cert, @list, 'D1', 50, 3.50, 'placement',
          SYSDATETIMEOFFSET(), DATEADD(DAY, 90, SYSDATETIMEOFFSET()), 'SkillStateWriter', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an invented level code was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a level code outside the reportable ladder';
END CATCH

-- COORDINATE: the continuous axis runs 1.00 (A1) to 6.99 (above C2). 7.10 is a coordinate no band
-- can contain, and a column typed DECIMAL(3,2) stores it happily.
BEGIN TRY
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@uCoord, @cert, @list, 'C2', 50, 7.10, 'placement',
          SYSDATETIMEOFFSET(), DATEADD(DAY, 90, SYSDATETIMEOFFSET()), 'SkillStateWriter', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a coordinate above the top of the scale was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a continuous coordinate above the top of the scale';
END CATCH

-- SOURCE: a closed vocabulary. The three values are the three things that can measure a skill.
BEGIN TRY
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@uSrc, @cert, @list, 'B1', 50, 3.50, 'guessed',
          SYSDATETIMEOFFSET(), DATEADD(DAY, 90, SYSDATETIMEOFFSET()), 'SkillStateWriter', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an invented measurement source was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a measurement source outside the vocabulary';
END CATCH

-- EXPIRY: 91 days is the input that passes "later than MeasuredAt" while being wrong. D18 fixes the
-- window at 90 calendar days, so both directions are refused.
BEGIN TRY
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@uExp, @cert, @list, 'B1', 50, 3.50, 'placement',
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          CAST('2026-04-02T00:00:00+00:00' AS DATETIMEOFFSET), 'SkillStateWriter', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a 91-day expiry was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused an expiry one day past the 90-day window';
END CATCH
BEGIN TRY
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@uExp, @cert, @read, 'B1', 50, 3.50, 'placement',
          CAST('2026-01-01T00:00:00+00:00' AS DATETIMEOFFSET),
          CAST('2026-03-01T00:00:00+00:00' AS DATETIMEOFFSET), 'SkillStateWriter', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a 59-day expiry was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused an expiry short of the 90-day window';
END CATCH

-- ISOLATION: a skill of another certificate. A single-column FK on SkillId accepts it; the composite
-- (SkillId, CertId) is what refuses it, and this is the row that tells the two apart.
BEGIN TRY
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@uIso, @cert, @otherSkill, 'B1', 50, 3.50, 'placement',
          SYSDATETIMEOFFSET(), DATEADD(DAY, 90, SYSDATETIMEOFFSET()), 'SkillStateWriter', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: another certificate''s skill was storable in this one', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused another certificate''s skill';
END CATCH

-- ONE ROW PER SKILL: the same learner and skill twice is two answers.
BEGIN TRY
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@uDup, @cert, @list, 'B1', 50, 3.50, 'placement',
          SYSDATETIMEOFFSET(), DATEADD(DAY, 90, SYSDATETIMEOFFSET()), 'SkillStateWriter', 1);
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@uDup, @cert, @list, 'A2', 10, 2.10, 'placement',
          SYSDATETIMEOFFSET(), DATEADD(DAY, 90, SYSDATETIMEOFFSET()), 'SkillStateWriter', 2);
  THROW 50000, 'EXPECT_REJECT FAILED: a second row for one learner and skill was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a second row for the same learner and skill';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 4. the single writer. Every wrong declaration below is a row that is valid in every other respect.
-- ---------------------------------------------------------------------------------------------
-- The real second writer. SkillStateWriter is the only service allowed to write this row, and
-- CalibrationService is the service that already holds a per-skill level on screen (#156) - it is the
-- writer a mistaken refactor would add, not an invented string.
--
-- This is the FIRST row for (@user2, @spea), so the primary key cannot be what refuses it: the only
-- thing wrong with the statement is its declaration.
BEGIN TRY
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
  VALUES (@user2, @cert, @spea, 'A2', 10, 2.10, 'calibration',
          CAST('2026-02-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-02-01T00:00:00+00:00' AS DATETIMEOFFSET)),
          'CalibrationService', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: CalibrationService wrote a row SkillStateWriter owns', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%per-skill state has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the second writer was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF EXISTS (SELECT 1 FROM LearnerSkillStates WHERE UserId = @user2 AND CertId = @cert AND SkillId = @spea)
  THROW 50000, 'EXPECT_REJECT FAILED: the refused second-writer insert wrote the row anyway', 1;
PRINT 'refused a per-skill state row written by CalibrationService';

-- and a write that declares nothing at all. Silently defaulting the writer is the failure mode the
-- guard exists to prevent, so "no writer" is refused as firmly as "the wrong writer".
BEGIN TRY
  INSERT INTO LearnerSkillStates
    (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source,
     MeasuredAt, ExpiresAt, ProjectionVersion)
  VALUES (@user3, @cert, @spea, 'A2', 10, 2.10, 'calibration',
          CAST('2026-02-01T00:00:00+00:00' AS DATETIMEOFFSET),
          DATEADD(DAY, 90, CAST('2026-02-01T00:00:00+00:00' AS DATETIMEOFFSET)), 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a per-skill state row with no declared writer was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%per-skill state has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the undeclared write was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF EXISTS (SELECT 1 FROM LearnerSkillStates WHERE UserId = @user3 AND CertId = @cert AND SkillId = @spea)
  THROW 50000, 'EXPECT_REJECT FAILED: the undeclared insert wrote the row anyway', 1;
PRINT 'refused a per-skill state row with no declared writer';

-- THE INPUT THAT PASSES A VALUE-ONLY GUARD: a second writer changes only the level and the position,
-- so WrittenByService still holds SkillStateWriter from the insert. The row stays valid in every
-- respect - the level is a real one, the expiry matches its own measured time - and only the
-- declaration is absent. `UPDATE(WrittenByService)` is the half that catches it.
BEGIN TRY
  UPDATE LearnerSkillStates
    SET CefrLevel = 'A2', PositionInBand = 5, ProjectionVersion = 2
  WHERE UserId = @user AND CertId = @cert AND SkillId = @list;
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer inherited the owner''s name', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%per-skill state has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the inherited-name write was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT CefrLevel FROM LearnerSkillStates WHERE UserId = @user AND CertId = @cert AND SkillId = @list) <> 'B1'
  THROW 50000, 'EXPECT_REJECT FAILED: a write that inherited the owner''s name changed the level', 1;
PRINT 'refused a per-skill state write that inherited the owner''s name';

-- the owner's own update is accepted, and moves the version forward
BEGIN TRY
  UPDATE LearnerSkillStates
    SET CefrLevel = 'B2', PositionInBand = 20, ProjectionVersion = 2,
        WrittenByService = 'SkillStateWriter'
  WHERE UserId = @user AND CertId = @cert AND SkillId = @list;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the per-skill writer could not update its own row: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
IF (SELECT CefrLevel FROM LearnerSkillStates WHERE UserId = @user AND CertId = @cert AND SkillId = @list) <> 'B2'
  THROW 50000, 'EXPECT_REJECT FAILED: the owner''s update did not take effect', 1;
PRINT 'accepted a per-skill state update by SkillStateWriter';

-- A NO-OP write is allowed: nothing derived was written, so nothing needs declaring. This is the
-- control that keeps the guard from being a blanket refusal on a table every screen reads.
BEGIN TRY
  UPDATE LearnerSkillStates
    SET CefrLevel = 'B2', PositionInBand = 20
  WHERE UserId = @user AND CertId = @cert AND SkillId = @list;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the guard blocked a write that derives nothing: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
PRINT 'accepted a write that changes nothing, because it derives nothing';

-- THE VERSION. Equal is the case a plausible `>=` lets through; a decrease is the case a `<>` lets
-- through. Both write a DIFFERENT level, because a write that changes nothing is a no-op and is
-- allowed - so an equal-version test that also changed nothing would pass against a broken guard.
BEGIN TRY
  UPDATE LearnerSkillStates
    SET CefrLevel = 'A2', WrittenByService = 'SkillStateWriter', ProjectionVersion = 2
  WHERE UserId = @user AND CertId = @cert AND SkillId = @list;
  THROW 50000, 'EXPECT_REJECT FAILED: an equal projection version overwrote the stored state', 1;
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
  UPDATE LearnerSkillStates
    SET CefrLevel = 'A2', WrittenByService = 'SkillStateWriter', ProjectionVersion = 1
  WHERE UserId = @user AND CertId = @cert AND SkillId = @list;
  THROW 50000, 'EXPECT_REJECT FAILED: a lower projection version overwrote the stored state', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the lower-version write was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT CefrLevel FROM LearnerSkillStates WHERE UserId = @user AND CertId = @cert AND SkillId = @list) <> 'B2'
  THROW 50000, 'EXPECT_REJECT FAILED: a version that is not newer overwrote the stored state', 1;
PRINT 'refused equal and lower projection versions';

-- a stale writer cannot make itself look newer by moving the token alone
BEGIN TRY
  UPDATE LearnerSkillStates SET ProjectionVersion = 3
  WHERE UserId = @user AND CertId = @cert AND SkillId = @list;
  THROW 50000, 'EXPECT_REJECT FAILED: a projection version was bumped with no writer declared', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%per-skill state has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the undeclared version bump was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT ProjectionVersion FROM LearnerSkillStates WHERE UserId = @user AND CertId = @cert AND SkillId = @list) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the refused version bump moved the token anyway', 1;
PRINT 'refused a projection version bumped by code that did not declare its writer';

-- ---------------------------------------------------------------------------------------------
-- 5. NO STORED OVERALL. The assertion is the EXACT column set, not a name-shaped search.
-- ---------------------------------------------------------------------------------------------
-- The scan is proved non-empty first: a lookup for a name no column has reports "absent" for every
-- table and would pass without looking at anything (lib.sql records that exact failure from domain04).
IF NOT EXISTS (
  SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_NAME = 'LearnerSkillStates' AND COLUMN_NAME = 'CefrLevel')
  THROW 50000, 'EXPECT_REJECT FAILED: the column lookup cannot see LearnerSkillStates.CefrLevel, so its absence check proves nothing', 1;
PRINT 'the column lookup finds a column that exists';

-- A stored overall called AggregateLevel, Standing or MinLevel passes a LIKE '%overall%' search, which
-- is the obvious guard and the wrong one. The whole row is listed instead, so ANY added column fails
-- and names itself. That is the only shape that cannot be walked past by a rename.
DECLARE @actual TABLE (ColumnName SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @actual (ColumnName)
  SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LearnerSkillStates';

DECLARE @expectedColumns TABLE (ColumnName SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @expectedColumns (ColumnName) VALUES
  ('UserId'), ('CertId'), ('SkillId'),
  ('CefrLevel'), ('PositionInBand'), ('ContinuousCoordinate'),
  ('Source'), ('MeasuredAt'), ('ExpiresAt'),
  ('WrittenByService'), ('ProjectionVersion');

IF EXISTS (
  SELECT 1
  FROM @expectedColumns e
  FULL OUTER JOIN @actual a ON a.ColumnName = e.ColumnName
  WHERE e.ColumnName IS NULL OR a.ColumnName IS NULL
)
BEGIN
  SET @msg = N'EXPECT_REJECT FAILED: LearnerSkillStates carries an extra or missing column ('
    + ISNULL((SELECT TOP 1 a.ColumnName FROM @actual a
              WHERE NOT EXISTS (SELECT 1 FROM @expectedColumns e WHERE e.ColumnName = a.ColumnName)), N'none')
    + N' extra) - a stored overall, whatever it is called, is the second writer this table exists to prevent';
  THROW 50000, @msg, 1;
END
PRINT 'LearnerSkillStates carries exactly the eleven columns the fact is made of, and no overall';

-- An "overall" pseudo-skill would be the same fact in a different place: a Skills row whose code reads
-- OVERALL, a state row pinned to it, and every reader doing MIN would silently pick up a fourth
-- "skill" that is not one. The certificate's own vector is what the overall ranges over.
IF EXISTS (SELECT 1 FROM Skills WHERE UPPER(Code) IN ('OVERALL', 'ALL'))
  THROW 50000, 'EXPECT_REJECT FAILED: a skill named OVERALL/ALL exists, which is a stored overall wearing a skill code', 1;
PRINT 'no skill is named OVERALL, so the overall cannot be stored as a row';

PRINT '#211: one row per learner per skill, one writer, expiry fixed by the measurement, and no overall';
