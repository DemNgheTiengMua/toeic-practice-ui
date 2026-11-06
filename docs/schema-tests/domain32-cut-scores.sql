-- docs/schema-tests/domain32-cut-scores.sql
-- #210 / D6 / LVL-13: the per-skill cut-score table is a versioned lookup, not a second band table.
--
-- Every refusal below is an input that is VALID in every other respect. That matters here more than
-- usual, because three of the four shapes a correct-looking guard misses are structural:
--
--   1. a range that overlaps its neighbour inside one version  -> the lookup has two answers
--   2. a guard that forgets TableVersion                      -> rejects a legitimate new version
--   3. a trigger that compares only against pre-existing rows -> a single multi-row INSERT that
--      overlaps itself passes, which is exactly how a version is loaded
--   4. the same range under a second skill                    -> must stay legal; skills are separate
--
-- Case 3 is why the fixture loads a whole version in ONE statement below: the AFTER trigger sees the
-- inserted rows in the base table, so a set-based comparison catches self-overlap.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('CUT210', N'Cut scores 210', 1);
DECLARE @c INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@c, 'LIST', N'Listening', 'listening', 1);
DECLARE @l INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@c, 'READ', N'Reading', 'reading', 2);
DECLARE @r INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@c, 'SPEA', N'Speaking', 'speaking', 3);
DECLARE @s INT = SCOPE_IDENTITY();

-- the version header carries the provenance and the "no combined total" bit, so a recorded
-- projection can be reproduced from the version alone.
INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt) VALUES
  (@c, 1, 0, N'ETS 2023 standard-setting study', '2023-06-01'),
  (@c, 2, 0, N'ETS 2025 standard-setting study', '2025-06-01');
PRINT 'accepted two version headers for one certificate';

-- reject: a cut row naming a version its certificate does not declare. Without this a recorded
-- projection could name a version nothing can look up.
BEGIN TRY
  INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion)
    VALUES (@c, @l, 'A1', 'cut', 1, 2, 99);
  THROW 50000, 'EXPECT_REJECT FAILED: a cut row naming an undeclared version was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a cut row naming an undeclared version as expected';
END CATCH

-- reject: a ceiling row that also carries a range. Two shapes in one row is the ambiguity this
-- table exists to remove: is 490-495 a level, or the top of the scale?
BEGIN TRY
  INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion)
    VALUES (@c, @l, 'B2', 'display_ceiling', 490, 495, 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a display-ceiling row carrying a range was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a display-ceiling row that carries a range as expected';
END CATCH

-- accept: one version carrying all three row kinds at once, loaded in a single statement. The
-- display_ceiling row records only the top of the scale (495) - §4.6's "display caps at 495".
INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion) VALUES
  (@c, @l, 'pre_a1',         'cut',              5,  55, 1),
  (@c, @l, 'A1',             'cut',             60, 105, 1),
  (@c, @l, 'A2',             'cut',            110, 270, 1),
  (@c, @l, 'B1',             'cut',            275, 395, 1),
  (@c, @l, 'B2',             'cut',            400, 485, 1),
  (@c, @l, 'C1',             'cut',            490, 495, 1),
  (@c, @l, 'C2',             'display_ceiling', NULL, 495, 1);
PRINT 'accepted a full version with all three row kinds';

-- reject: a range that overlaps a row already committed. SPEA is used because nothing else touches
-- it, and the TRY body holds exactly ONE insert: a statement inside a TRY that succeeds stays
-- committed when a later statement in the same TRY throws, and a leaked fixture row would make the
-- next assertion fail for a reason that has nothing to do with the guard.
INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion)
  VALUES (@c, @s, 'A1', 'cut', 100, 200, 1);
BEGIN TRY
  INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion)
    VALUES (@c, @s, 'A2', 'cut', 150, 250, 1);
  THROW 50000, 'EXPECT_REJECT FAILED: overlap against an existing row was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected overlap against an existing row as expected';
END CATCH

-- reject: one statement that overlaps ITSELF. A trigger that only joins against rows that already
-- existed before this statement would accept this and the table would hold two answers from load.
-- Every row in this statement is refused together, so nothing leaks into the later fixtures.
BEGIN TRY
  INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion) VALUES
    (@c, @r, 'B1', 'cut', 275, 400, 1),
    (@c, @r, 'B2', 'cut', 395, 500, 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a multi-row insert that overlaps itself was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a self-overlapping multi-row insert as expected';
END CATCH

-- reject: a second row for the same level within one version. The unique key is what stops the
-- table carrying two answers for one level even when the ranges do not overlap.
BEGIN TRY
  INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion)
    VALUES (@c, @l, 'C1', 'cut', 496, 500, 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a duplicate level row in one version was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a duplicate level row as expected';
END CATCH

-- reject: a row kind outside the three. A fourth kind would be a range whose meaning the projector
-- has to guess, and "guess the kind" is how a display ceiling is emitted as a level.
BEGIN TRY
  INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion)
    VALUES (@c, @l, 'A2', 'estimated', 600, 700, 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an unknown row kind was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an unknown row kind as expected';
END CATCH

-- reject: a skill belonging to another certificate.
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('CUT210B', N'Other', 1);
DECLARE @cz INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cz, 'SPEA', N'Speaking', 'speaking', 1);
DECLARE @sz INT = SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion)
    VALUES (@c, @sz, 'B1', 'cut', 0, 10, 1);
  THROW 50000, 'EXPECT_REJECT FAILED: cross-cert skill in CefrCutScores accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert skill in CefrCutScores as expected';
END CATCH

-- accept: the same ranges under a NEW version. This is the guard that keeps a re-standardised table
-- loadable, and it fails the moment the overlap trigger drops TableVersion from its predicate.
INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion) VALUES
  (@c, @l, 'B1', 'cut',             275, 395, 2),
  (@c, @l, 'B2', 'cut',             400, 485, 2),
  (@c, @l, 'C2', 'display_ceiling', NULL, 495, 2);
PRINT 'accepted identical ranges under a new table version';

-- accept: the same range under a different skill. Listening and Reading are cut separately, so
-- sharing a range between them is normal and a guard scoped to the certificate alone would break it.
INSERT INTO CefrCutScores (CertId, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled, TableVersion) VALUES
  (@c, @r, 'B1', 'cut', 275, 380, 1),
  (@c, @r, 'B2', 'cut', 385, 450, 1);
PRINT 'accepted the same range under a different skill';

-- the projection reproduces only if the version is stored with it: both versions of B1 must exist
-- side by side, and the two answers must be distinguishable by TableVersion alone.
IF (SELECT COUNT(*) FROM CefrCutScores WHERE CertId = @c AND SkillId = @l AND CefrLevel = 'B1') <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: two versions of one level are not both stored, so a recorded projection cannot be reproduced', 1;
IF (SELECT COUNT(*) FROM CefrCutScores WHERE CertId = @c AND SkillId = @l AND CefrLevel = 'C2'
      AND RowKind = 'display_ceiling' AND MaxScaled = 495) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a display-ceiling row does not record the highest value the scale can display', 1;
PRINT 'versions coexist and the ceiling row records the scale top';
