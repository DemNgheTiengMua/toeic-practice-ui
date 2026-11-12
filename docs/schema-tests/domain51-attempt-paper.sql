SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #274: a sitting can hold its OWN paper, not the exam's shared roster.
--
-- Two tables, one fact. `AttemptStageBundles` records WHY a skill was drawn as it was (the stage and
-- the routed band pair) and `AttemptItemResponses` records WHAT was served (roster positions, in
-- sitting order). This file proves the parts only the database can:
--
--   1. the registry names the fact, the band pair is an interval (never a point, never inverted), and
--      the stage is 1 or 2;
--   2. the single-writer guard refuses a write declaring another writer or none, including the
--      inherited-name case a value-only check would pass, and refuses a projection version that is
--      not newer - the ADR-0012 rule criterion 4 names;
--   3. two attempts of one exam can be served DIFFERENT positions, and the shared roster is NOT
--      rewritten - criterion 2, and the reason the roster's unique key is untouched. Proved by a
--      second paper existing beside the first, which the unique key would refuse on ExamItems;
--   4. a served position must be of the attempt's OWN exam (the #269 idiom), so one paper's sitting
--      cannot be served another paper's position;
--   5. the same position cannot be served twice on one sitting.
--
-- Every refusal below uses a row that is valid in every other respect, so the guard that fires is the
-- one under test. A row that also broke a foreign key or the primary key would be refused before the
-- trigger ever ran, and the test would go green with the guard deleted.

DECLARE @msg NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. the registry, and the shape the ticket asks for
-- ---------------------------------------------------------------------------------------------
IF NOT EXISTS (
  SELECT 1 FROM DerivedFactOwners
  WHERE FactCode = 'attempt_paper' AND OwningService = 'AttemptEngine' AND Coverage = 'covered'
    AND CoveredTable = 'AttemptStageBundles')
  THROW 50000, 'EXPECT_REJECT FAILED: the paper a sitting was served is not registered as a covered fact owned by AttemptEngine', 1;

DECLARE @required TABLE (TableName SYSNAME NOT NULL, ColumnName SYSNAME NOT NULL,
                         PRIMARY KEY (TableName, ColumnName));
INSERT INTO @required (TableName, ColumnName) VALUES
  ('AttemptStageBundles', 'AttemptId'), ('AttemptStageBundles', 'CertId'),
  ('AttemptStageBundles', 'SkillId'), ('AttemptStageBundles', 'StageNumber'),
  ('AttemptStageBundles', 'BandLower'), ('AttemptStageBundles', 'BandUpper'),
  ('AttemptStageBundles', 'WrittenByService'), ('AttemptStageBundles', 'ProjectionVersion'),
  ('AttemptItemResponses', 'AttemptId'), ('AttemptItemResponses', 'ExamId'),
  ('AttemptItemResponses', 'PositionNumber'), ('AttemptItemResponses', 'ExamItemId');
IF EXISTS (
  SELECT 1 FROM @required r
  WHERE NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS c
                    WHERE c.TABLE_NAME = r.TableName AND c.COLUMN_NAME = r.ColumnName))
  THROW 50000, 'EXPECT_REJECT FAILED: a column the ticket names is missing from the attempt-scoped paper', 1;

-- The absence check is proved able to see a column that exists, or it reports "fine" for any name.
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'AttemptStageBundles' AND COLUMN_NAME = 'BandLower')
  THROW 50000, 'EXPECT_REJECT FAILED: the column audit cannot see BandLower, so its absence checks prove nothing', 1;

-- The served positions carry NO writer column of their own: they are the same fact, written by the
-- same owner in the same transaction, and a second guard would be two mechanisms on one fact.
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_NAME = 'AttemptItemResponses' AND COLUMN_NAME = 'WrittenByService')
  THROW 50000, 'EXPECT_REJECT FAILED: AttemptItemResponses carries a second writer column for the paper fact', 1;
PRINT '1 ok: the paper fact is registered, both tables carry the columns the ticket names';

-- ---------------------------------------------------------------------------------------------
-- 2. the fixture: one certificate with two skills, one paper, one learner, two sittings
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('paper274', N'Paper 274');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'paper274@x.com', 0x01);
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('AP274', N'Attempt paper', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'READ', N'Reading', 'reading', 1);
DECLARE @read INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'WRIT', N'Writing', 'writing', 2);
DECLARE @writ INT = SCOPE_IDENTITY();

INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
VALUES (@read, @cert, 'reading', 'R1', N'Reading', 4, 4, 1);
DECLARE @sec INT = SCOPE_IDENTITY();

INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
VALUES (@cert, N'Attempt paper exam', 'published', 30, 'placement');
DECLARE @exam INT = SCOPE_IDENTITY();

INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Detail');
DECLARE @topic INT = SCOPE_IDENTITY();

-- Four positions on ONE paper, which is the shared roster the sitting draws FROM. Four, because two
-- sittings must be able to be served different SUBSETS of them.
DECLARE @n INT = 1;
WHILE @n <= 4
BEGIN
  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat, ContentTier, LifecycleStatus)
  VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'mcq', 'demo', 'active');
  DECLARE @item INT = SCOPE_IDENTITY();
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
  VALUES (@item, 1, N'Position stem.', N'a', N'b', N'c', N'd', 'A');
  DECLARE @ver INT = SCOPE_IDENTITY();
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@exam, @sec, @cert, @ver, @item, 'reading', @n);
  SET @n = @n + 1;
END
DECLARE @pos1 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND DisplayOrder = 1);
DECLARE @pos2 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND DisplayOrder = 2);
DECLARE @pos3 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND DisplayOrder = 3);
DECLARE @pos4 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @exam AND DisplayOrder = 4);

INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @attemptA INT = SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @attemptB INT = SCOPE_IDENTITY();
-- A third attempt on ANOTHER paper, so "the position belongs to the attempt's own exam" is expressible.
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
VALUES (@cert, N'Other paper', 'published', 30, 'placement');
DECLARE @otherExam INT = SCOPE_IDENTITY();
DECLARE @posItem INT = (SELECT ItemId FROM ExamItems WHERE ExamItemId = @pos1);
DECLARE @posVer INT = (SELECT ItemVersionId FROM ExamItems WHERE ExamItemId = @pos1);
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
VALUES (@otherExam, @sec, @cert, @posVer, @posItem, 'reading', 1);
DECLARE @otherPos INT = SCOPE_IDENTITY();
IF @otherPos = @pos1
  THROW 50000, 'EXPECT_REJECT FAILED: the second paper reused the first paper''s position row', 1;

-- ---------------------------------------------------------------------------------------------
-- 3. the bundle: an interval, a closed band vocabulary, and two stages
-- ---------------------------------------------------------------------------------------------
INSERT INTO AttemptStageBundles
  (AttemptId, CertId, SkillId, StageNumber, BandLower, BandUpper, WrittenByService, ProjectionVersion)
VALUES (@attemptA, @cert, @read, 1, 'A2', 'B1', 'AttemptEngine', 1);
PRINT '3 ok: the owner stored a stage-one bundle';

-- a point is not a pair: B1..B1 routes to nothing, so it cannot be a routed pair
BEGIN TRY
  INSERT INTO AttemptStageBundles
    (AttemptId, CertId, SkillId, StageNumber, BandLower, BandUpper, WrittenByService, ProjectionVersion)
  VALUES (@attemptA, @cert, @writ, 1, 'B1', 'B1', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a band pair that is a point was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT '3 ok: a band pair that is a point is refused';
END CATCH

-- and an inverted pair is refused for the same reason, with its own row so the key cannot be the cause
BEGIN TRY
  INSERT INTO AttemptStageBundles
    (AttemptId, CertId, SkillId, StageNumber, BandLower, BandUpper, WrittenByService, ProjectionVersion)
  VALUES (@attemptA, @cert, @writ, 2, 'B2', 'B1', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an inverted band pair was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT '3 ok: an inverted band pair is refused';
END CATCH

-- the stage is 1 or 2 and nothing else: a third stage is a routing table this product does not have
BEGIN TRY
  INSERT INTO AttemptStageBundles
    (AttemptId, CertId, SkillId, StageNumber, BandLower, BandUpper, WrittenByService, ProjectionVersion)
  VALUES (@attemptA, @cert, @writ, 3, 'B1', 'B2', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a third stage was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT '3 ok: a stage outside 1..2 is refused';
END CATCH

-- the closed band vocabulary: a band this product does not define is refused
BEGIN TRY
  INSERT INTO AttemptStageBundles
    (AttemptId, CertId, SkillId, StageNumber, BandLower, BandUpper, WrittenByService, ProjectionVersion)
  VALUES (@attemptA, @cert, @writ, 1, 'B1', 'B9', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an invented band was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT '3 ok: a band outside the ladder is refused';
END CATCH

-- a bundle's skill must be its attempt's own certificate's, which is what the composite is for
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('AP274B', N'Other attempt paper', 1);
DECLARE @otherCert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@otherCert, 'READ', N'Reading', 'reading', 1);
DECLARE @otherSkill INT = SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO AttemptStageBundles
    (AttemptId, CertId, SkillId, StageNumber, BandLower, BandUpper, WrittenByService, ProjectionVersion)
  VALUES (@attemptA, @cert, @otherSkill, 1, 'B1', 'B2', 'AttemptEngine', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: another certificate''s skill was bundled under this attempt', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT '3 ok: another certificate''s skill is refused by the composite key';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 4. ADR-0012: one writer, the declaration in the statement, and a monotonic version
-- ---------------------------------------------------------------------------------------------
-- a second writer that changes only the upper band. The row is valid in every respect, and the stored
-- WrittenByService still holds AttemptEngine - so a value-only check would pass it.
BEGIN TRY
  UPDATE AttemptStageBundles SET BandUpper = 'B2', ProjectionVersion = 2
  WHERE AttemptId = @attemptA AND SkillId = @read AND StageNumber = 1;
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer inherited the paper owner''s name', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%paper a sitting was served has exactly one writer%'
    THROW 50000, 'EXPECT_REJECT FAILED: the inherited-name paper write was refused by the wrong guard', 1;
  PRINT '4 ok: refused a paper write that inherited the owner''s name';
END CATCH

-- an explicit wrong writer names the natural second writer: a draw service, not the engine
BEGIN TRY
  UPDATE AttemptStageBundles
    SET BandUpper = 'B2', WrittenByService = 'PlacementStageTwoDrawService', ProjectionVersion = 2
  WHERE AttemptId = @attemptA AND SkillId = @read AND StageNumber = 1;
  THROW 50000, 'EXPECT_REJECT FAILED: a draw service rewrote the paper AttemptEngine owns', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%paper a sitting was served has exactly one writer%'
    THROW 50000, 'EXPECT_REJECT FAILED: the second paper writer was refused by the wrong guard', 1;
  PRINT '4 ok: refused a draw service writing the paper';
END CATCH

-- the owner's own write is accepted, and moves the version forward
BEGIN TRY
  UPDATE AttemptStageBundles
    SET BandUpper = 'B2', WrittenByService = 'AttemptEngine', ProjectionVersion = 2
  WHERE AttemptId = @attemptA AND SkillId = @read AND StageNumber = 1;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the paper owner could not write its own bundle: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
PRINT '4 ok: accepted the owner''s own write as it moves the version forward';

-- an EQUAL version is a replay, not progress. The band moves too, so this is a real write and not a
-- no-op that would pass against a broken guard.
BEGIN TRY
  UPDATE AttemptStageBundles
    SET BandLower = 'A1', WrittenByService = 'AttemptEngine', ProjectionVersion = 2
  WHERE AttemptId = @attemptA AND SkillId = @read AND StageNumber = 1;
  THROW 50000, 'EXPECT_REJECT FAILED: an equal projection version restated the paper', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
    THROW 50000, 'EXPECT_REJECT FAILED: the equal-version paper write was refused by the wrong guard', 1;
  PRINT '4 ok: refused an equal projection version';
END CATCH

-- a LOWER version is a draw that arrived late
BEGIN TRY
  UPDATE AttemptStageBundles
    SET BandLower = 'A1', WrittenByService = 'AttemptEngine', ProjectionVersion = 1
  WHERE AttemptId = @attemptA AND SkillId = @read AND StageNumber = 1;
  THROW 50000, 'EXPECT_REJECT FAILED: a stale draw overwrote a newer paper', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
    THROW 50000, 'EXPECT_REJECT FAILED: the stale paper write was refused by the wrong guard', 1;
  PRINT '4 ok: refused a stale projection version';
END CATCH

-- an INSERT must declare too: the whole row IS the fact, so there is no sitting-state column to carve
-- out (unlike ExamAttempts, which legitimately writes without a declaration).
BEGIN TRY
  INSERT INTO AttemptStageBundles
    (AttemptId, CertId, SkillId, StageNumber, BandLower, BandUpper, ProjectionVersion)
  VALUES (@attemptB, @cert, @read, 1, 'A1', 'A2', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a paper insert declaring no writer was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%paper a sitting was served has exactly one writer%'
    THROW 50000, 'EXPECT_REJECT FAILED: the undeclared paper insert was refused by the wrong guard', 1;
  PRINT '4 ok: refused a paper bundle with no declared writer';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 5. CRITERIA 1 AND 2: two sittings of ONE exam are served different positions, and the roster is
--    not rewritten. Served positions are rows of the attempt, never rows of ExamItems.
-- ---------------------------------------------------------------------------------------------
INSERT INTO AttemptItemResponses (AttemptId, ExamId, PositionNumber, ExamItemId) VALUES
  (@attemptA, @exam, 1, @pos1),
  (@attemptA, @exam, 2, @pos2);
INSERT INTO AttemptItemResponses (AttemptId, ExamId, PositionNumber, ExamItemId) VALUES
  (@attemptB, @exam, 1, @pos3),
  (@attemptB, @exam, 2, @pos4);

IF (SELECT COUNT(*) FROM ExamItems WHERE ExamId = @exam) <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: serving a paper changed the shared roster''s row count', 1;
IF (SELECT COUNT(*) FROM AttemptItemResponses WHERE AttemptId = @attemptA) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the first sitting was not served its own two positions', 1;
IF EXISTS (SELECT 1 FROM AttemptItemResponses a JOIN AttemptItemResponses b
           ON b.AttemptId = @attemptB AND b.PositionNumber = a.PositionNumber
           AND b.ExamItemId = a.ExamItemId
           WHERE a.AttemptId = @attemptA)
  THROW 50000, 'EXPECT_REJECT FAILED: two sittings of one exam were served the same position at the same number', 1;
IF (SELECT ExamItemId FROM AttemptItemResponses WHERE AttemptId = @attemptA AND PositionNumber = 1) = @pos3
  THROW 50000, 'EXPECT_REJECT FAILED: both sittings read the same first position', 1;
PRINT '5 ok: two sittings of one exam hold different positions, and the roster was not rewritten';

-- The roster's unique key is what made a per-learner roster impossible, and it is UNTOUCHED: a second
-- paper position at (exam, section, display order) 1 is still refused. If this accepted, the roster had
-- been weakened to hold per-learner positions.
--
-- Written as VALUES rather than INSERT ... SELECT on purpose: the harness injects a fixture-row-count
-- guard after every line-leading INSERT ... SELECT, and a statement whose whole point is to be REFUSED
-- leaves zero rows - the guard would abort naming the fixture rather than letting the CATCH judge it.
BEGIN TRY
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
  VALUES (@exam, @sec, @cert, @posVer, @posItem, 'reading', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: a second roster position at the same display order was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UQ_ExamItems_Exam_Section_Order%'
    THROW 50000, 'EXPECT_REJECT FAILED: the roster position was refused by the wrong constraint', 1;
  PRINT '5 ok: UQ_ExamItems_Exam_Section_Order still refuses a second position (the roster is shared)';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 6. a served position must be of the attempt's OWN exam, one position cannot be served twice, and
--    the sitting order is unique
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO AttemptItemResponses (AttemptId, ExamId, PositionNumber, ExamItemId)
  VALUES (@attemptA, @exam, 3, @otherPos);
  THROW 50000, 'EXPECT_REJECT FAILED: a position from another paper was served into this sitting', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_AIR_ExamItemExam%'
    THROW 50000, 'EXPECT_REJECT FAILED: the foreign position was refused by the wrong constraint', 1;
  PRINT '6 ok: a position of another paper is refused by the composite key';
END CATCH

BEGIN TRY
  INSERT INTO AttemptItemResponses (AttemptId, ExamId, PositionNumber, ExamItemId)
  VALUES (@attemptA, @exam, 3, @pos1);
  THROW 50000, 'EXPECT_REJECT FAILED: one position was served twice into one sitting', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UQ_AIR_Attempt_Item%'
    THROW 50000, 'EXPECT_REJECT FAILED: the repeated position was refused by the wrong constraint', 1;
  PRINT '6 ok: the same position cannot appear twice on one sitting''s paper';
END CATCH

BEGIN TRY
  INSERT INTO AttemptItemResponses (AttemptId, ExamId, PositionNumber, ExamItemId)
  VALUES (@attemptA, @exam, 2, @pos3);
  THROW 50000, 'EXPECT_REJECT FAILED: two positions shared one sitting-order number', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%PK_AttemptItemResponses%'
    THROW 50000, 'EXPECT_REJECT FAILED: the duplicate position number was refused by the wrong constraint', 1;
  PRINT '6 ok: one sitting-order number names exactly one position';
END CATCH

-- a gap is a hole in the navigator, and 0 is not a sitting position at all
BEGIN TRY
  INSERT INTO AttemptItemResponses (AttemptId, ExamId, PositionNumber, ExamItemId)
  VALUES (@attemptA, @exam, 0, @pos4);
  THROW 50000, 'EXPECT_REJECT FAILED: position number 0 was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT '6 ok: sitting order is 1-based';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 7. the attempt-scoped paper is not the shared roster: an attempt with NO served positions is a
--    sitting on the exam's roster, and that is a legal state forever (every sitting before #274).
-- ---------------------------------------------------------------------------------------------
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @legacyAttempt INT = SCOPE_IDENTITY();
IF EXISTS (SELECT 1 FROM AttemptItemResponses WHERE AttemptId = @legacyAttempt)
  THROW 50000, 'EXPECT_REJECT FAILED: an attempt that was never drawn for was served positions', 1;
IF EXISTS (SELECT 1 FROM AttemptStageBundles WHERE AttemptId = @legacyAttempt)
  THROW 50000, 'EXPECT_REJECT FAILED: an attempt that was never drawn for was given a bundle', 1;
PRINT '7 ok: a sitting with no paper of its own reads the shared roster, and that state is legal';

PRINT '#274: a sitting holds its own paper - an interval per stage, one writer, a monotonic version, and a roster that is never rewritten';
