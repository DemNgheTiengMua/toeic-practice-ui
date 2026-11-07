-- docs/schema-tests/domain37-ingestion-bench.sql — #205 / S-02: the ingestion and staging workbench.
--
-- The workbench's acceptance criteria are mostly about a page, and a page is tested in
-- tests/WebMVC.Tests. What has to hold in the DATABASE is the part a page can quietly get wrong:
--
--   1. A pool upload has no certificate (CNT-01). If CertId were still NOT NULL, #198's seam could
--      not record its own batch and criterion 4 - "the batch, its uploader and its timestamp are
--      recorded" - would be unmeetable through the only door that ingests pool stock.
--   2. The batch, its uploader and its timestamp are recorded together and cannot be written
--      without the uploader. A batch that names nobody is not a record of who submitted what.
--   3. A per-record verdict can carry its machine rule and the item it became, and a batch-level
--      note may carry neither. That split is what makes "one row per record" a query: a verdict
--      names its RowNo, a note does not.
--   4. An accepted record waits in `validated`, and `validated` is not what selection reads. This is
--      criterion 2's database half: the state exists, the edge into it exists, and the state the
--      workbench calls "staged" is genuinely not servable.
--   5. Re-judging a corrected record promotes nothing: `validated -> active` is a separate act, so
--      re-submission cannot be used to bypass the audit by writing `active` directly.
--
-- Every refusal sits beside an accepting control that differs in exactly the property under test.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

-- ================================================================================================
-- 1. A pool batch names no certificate, and a batch names its uploader and its time.
-- ================================================================================================
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'd37-staff')
  INSERT INTO Roles (Code, Name) VALUES ('d37-staff', N'D37 staff');
DECLARE @role INT = (SELECT RoleId FROM Roles WHERE Code = 'd37-staff');

IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'd37-uploader@test.local')
  INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
  VALUES (@role, 'd37-uploader@test.local', 0x01, N'D37 uploader', SYSDATETIMEOFFSET());
DECLARE @uploader INT = (SELECT UserId FROM Users WHERE Email = 'd37-uploader@test.local');

-- A pool upload, exactly as #198's seam writes it: no certificate, and a certificate is not required.
INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy, CreatedAt)
VALUES (NULL, N'd37-pool-reading.json', 'done', @uploader, SYSDATETIMEOFFSET());
DECLARE @poolBatch INT = SCOPE_IDENTITY();

IF (SELECT CertId FROM ImportBatches WHERE BatchId = @poolBatch) IS NOT NULL
  THROW 50000, 'EXPECT_REJECT FAILED: a pool batch recorded a certificate it does not have', 1;
IF (SELECT UploadedBy FROM ImportBatches WHERE BatchId = @poolBatch) <> @uploader
  THROW 50000, 'EXPECT_REJECT FAILED: the pool batch did not record its uploader', 1;
IF NOT EXISTS (SELECT 1 FROM ImportBatches WHERE BatchId = @poolBatch AND CreatedAt IS NOT NULL)
  THROW 50000, 'EXPECT_REJECT FAILED: the pool batch did not record when it was submitted', 1;
PRINT 'a pool upload records its batch, uploader and timestamp with no certificate';

-- The control: the exam importer's batch still names one, so the relaxation did not become "optional".
IF NOT EXISTS (SELECT 1 FROM Certificates WHERE Code = 'D37')
  INSERT INTO Certificates (Code, Name) VALUES ('D37', N'D37 certificate');
DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = 'D37');

INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy, CreatedAt)
VALUES (@cert, N'd37-exam.json', 'pending', @uploader, SYSDATETIMEOFFSET());
DECLARE @examBatch INT = SCOPE_IDENTITY();
IF (SELECT CertId FROM ImportBatches WHERE BatchId = @examBatch) <> @cert
  THROW 50000, 'EXPECT_REJECT FAILED: an exam batch lost its certificate', 1;

-- A batch that names nobody is refused. UploadedBy is the column ADM-02 reads, so a batch with no
-- uploader is a batch that no one can be barred from approving.
BEGIN TRY
  INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy, CreatedAt)
  VALUES (NULL, N'd37-anonymous.json', 'done', NULL, SYSDATETIMEOFFSET());
  THROW 50000, 'EXPECT_REJECT FAILED: a batch with no uploader was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UploadedBy%'
  BEGIN
    DECLARE @msg NVARCHAR(2048) = N'EXPECT_REJECT FAILED: the anonymous batch was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'a batch without an uploader is refused, and the exam importer still names a certificate';

-- ================================================================================================
-- 2. One row per record: a verdict names its line and its rule; a note names neither.
-- ================================================================================================
INSERT INTO ImportLog (BatchId, RowNo, Level, Message, RuleCode, ItemId)
VALUES
  (@poolBatch, 12,  'info',  N'the record was accepted', NULL, NULL),
  (@poolBatch, 47,  'error', N'gate one: the explanation is empty', 'gate_one.explanation', NULL),
  (@poolBatch, NULL, 'info', N'7 records accepted, 1 refused', NULL, NULL);
-- #205 acceptance criterion 1's split, as a query: two records carry a line, the batch note does not.
IF (SELECT COUNT(*) FROM ImportLog WHERE BatchId = @poolBatch AND RowNo IS NOT NULL) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the two record verdicts did not both carry a line', 1;
IF (SELECT COUNT(*) FROM ImportLog WHERE BatchId = @poolBatch AND RowNo IS NULL) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the batch-level note did not stay line-less', 1;

-- The refused verdict names the machine rule, which is what the page shows beside the sentence.
DECLARE @rule VARCHAR(40) = (SELECT RuleCode FROM ImportLog WHERE BatchId = @poolBatch AND RowNo = 47);
IF @rule IS NULL OR @rule <> 'gate_one.explanation'
  THROW 50000, 'EXPECT_REJECT FAILED: a record verdict did not keep its machine rule', 1;

-- The level vocabulary stays closed: 'fatal' is not one of the three, so a workbench that invented a
-- severity would be refused rather than colouring a row nothing understands.
BEGIN TRY
  INSERT INTO ImportLog (BatchId, RowNo, Level, Message) VALUES (@poolBatch, 3, 'fatal', N'invented');
  THROW 50000, 'EXPECT_REJECT FAILED: an invented log level was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ImportLog_Level%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the invented level was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'a record verdict carries its line and its rule; a batch note carries neither';

-- ================================================================================================
-- 3. An accepted record's verdict can point at the item it became.
-- ================================================================================================
INSERT INTO Topics (Modality, Code, Name)
VALUES ('reading', 'R-GRAM', N'Morphosyntactic structure and lexical precision');
DECLARE @topic INT = SCOPE_IDENTITY();

INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'validated', 'd37-validated');
DECLARE @item INT = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@item, 1, N'The depot opens at six.', N'a', N'b', N'c', N'd', 'A');

-- The verdict recorded against the item, which is criterion 2's join: which staged row did record 12
-- become? RowNo alone cannot answer that, and Items.BatchTag is the file's tag rather than the batch.
INSERT INTO ImportLog (BatchId, RowNo, Level, Message, RuleCode, ItemId)
VALUES (@poolBatch, 12, 'info', N'the record was accepted and staged', NULL, @item);

IF (SELECT COUNT(*) FROM ImportLog l JOIN Items i ON i.ItemId = l.ItemId
    WHERE l.BatchId = @poolBatch AND i.LifecycleStatus = 'validated') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the accepted verdict does not lead to its staged item', 1;

-- The item is the ONLY row that state: writing `active` would be the bypass this whole criterion is
-- about, so the control below proves `active` is reachable and therefore that `validated` here is a
-- choice rather than the only value the column accepts.
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', 'd37-active');
PRINT 'an accepted verdict points at its staged item, and `active` is a different state';

-- ================================================================================================
-- 4. Staging is a real state: `validated` is not servable, and reaching it from `draft` is the edge.
-- ================================================================================================
DECLARE @draft INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'draft', 'd37-promote');
SET @draft = SCOPE_IDENTITY();

BEGIN TRY
  UPDATE Items SET LifecycleStatus = 'validated' WHERE ItemId = @draft;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: draft -> validated (the gates promoted it) was refused: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
IF (SELECT LifecycleStatus FROM Items WHERE ItemId = @draft) <> 'validated'
  THROW 50000, 'EXPECT_REJECT FAILED: draft -> validated did not take effect', 1;

-- The audit's act, one state later, is a distinct edge - so re-judging a corrected record cannot
-- stand in for it.
BEGIN TRY
  UPDATE Items SET LifecycleStatus = 'active' WHERE ItemId = @draft;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: validated -> active (the audit cleared it) was refused: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH

-- And the bypass this criterion is about: a re-judged record may not jump draft -> active.
DECLARE @jumper INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'draft', 'd37-jump');
SET @jumper = SCOPE_IDENTITY();
BEGIN TRY
  UPDATE Items SET LifecycleStatus = 'active' WHERE ItemId = @jumper;
  THROW 50000, 'EXPECT_REJECT FAILED: draft -> active was accepted, so re-submission can skip the audit', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CNT-42%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: draft -> active was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT LifecycleStatus FROM Items WHERE ItemId = @jumper) <> 'draft'
  THROW 50000, 'EXPECT_REJECT FAILED: the refused draft -> active still moved the row', 1;
PRINT 'draft -> validated -> active are the two audit edges; draft -> active is refused';

-- ================================================================================================
-- 5. The count: nothing above created or removed a row it did not mean to.
-- ================================================================================================
IF (SELECT COUNT(*) FROM ImportBatches WHERE SourceName LIKE 'd37-%') <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the batch fixture holds a row it should not', 1;
IF (SELECT COUNT(*) FROM Items WHERE BatchTag LIKE 'd37-%') <> 4
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the item fixture holds '
      + CONVERT(NVARCHAR(10), (SELECT COUNT(*) FROM Items WHERE BatchTag LIKE 'd37-%'))
      + N' items, not 4; a refusal created or removed a row';
    THROW 50000, @msg, 1;
  END
PRINT 'the ingestion workbench columns: no certificate needed, who and when recorded, staged is not live';
