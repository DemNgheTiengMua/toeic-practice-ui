-- docs/schema-tests/domain38-stratified-audit.sql — #206 / S-03: the stratified audit and quarantine queue.
--
-- The sample, the blind solve and the quarantine all live in a service, and a service is tested in
-- tests/Services.Tests. What has to hold in the DATABASE is the part a service can quietly get wrong,
-- and the part an endpoint written later would bypass:
--
--   1. A stratum is one cell of one batch. Two rows for the same (batch, topic, level) is two answers
--      to "was this cell promoted", so it is unstorable rather than merely avoided.
--   2. A stratum names the batch's own items. An item with no verdict in this batch - another author's
--      stock, or another certificate's bank - cannot be sampled by it.
--   3. A blind solve cannot be recorded after the key was read (W-01, story 30). The service refuses
--      this at the boundary too; this is what holds for writes the service does not make.
--   4. A verdict follows a solve, so an item nobody solved cannot be adjudicated - the self-review the
--      whole ticket exists to prevent, made unstorable.
--   5. The stratum's outcome is derived and has one writer (ADR-0012), including the UPDATE() half that
--      a value-only guard would miss.
--   6. CRITERION 5, the one this file exists for: a quarantined stratum's items are refused by
--      SELECTION. The assertion is the same selector a learner's drill runs, not a flag on the item.
--
-- Every refusal sits beside an accepting control that differs in exactly the property under test.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

DECLARE @msg NVARCHAR(2048);

-- ================================================================================================
-- 1. The fixture: one certificate, two accounts, one pool batch, one 20-item stratum plus a 1-item cell.
-- ================================================================================================
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'd38-staff')
  INSERT INTO Roles (Code, Name) VALUES ('d38-staff', N'D38 staff');
DECLARE @role INT = (SELECT RoleId FROM Roles WHERE Code = 'd38-staff');

IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'd38-uploader@test.local')
  INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
  VALUES (@role, 'd38-uploader@test.local', 0x01, N'D38 uploader', SYSDATETIMEOFFSET());
IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'd38-reviewer@test.local')
  INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
  VALUES (@role, 'd38-reviewer@test.local', 0x01, N'D38 reviewer', SYSDATETIMEOFFSET());
DECLARE @uploader INT = (SELECT UserId FROM Users WHERE Email = 'd38-uploader@test.local');
DECLARE @reviewer INT = (SELECT UserId FROM Users WHERE Email = 'd38-reviewer@test.local');

-- A pool upload: no certificate (CNT-01), which is also why a stratum cannot straddle two banks.
INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy, CreatedAt)
VALUES (NULL, N'd38-pool-reading.json', 'done', @uploader, SYSDATETIMEOFFSET());
DECLARE @batch INT = SCOPE_IDENTITY();

IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET')
  INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching');
IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-INF')
  INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-INF', N'Bridging inference and logical deduction');
DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET');
DECLARE @otherTopic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-INF');

-- Twenty staged items in one cell, each with a version, each with an accepted verdict in THIS batch.
-- The 20 is load-bearing: criterion 3 says one key error quarantines the WHOLE stratum, and a test that
-- moved one item would pass against a guard that set a single item.
DECLARE @n INT = 1;
DECLARE @item INT, @version INT;
DECLARE @cellItems TABLE (ItemId INT NOT NULL PRIMARY KEY, ItemVersionId INT NOT NULL);
WHILE @n <= 20
BEGIN
  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
  VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'validated', 'd38-main');
  SET @item = SCOPE_IDENTITY();

  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
  VALUES (@item, 1, N'Stem ' + CONVERT(NVARCHAR(10), @n), N'a', N'b', N'c', N'd', 'A');
  SET @version = SCOPE_IDENTITY();

  INSERT INTO ImportLog (BatchId, RowNo, Level, Message, RuleCode, ItemId, Attempt)
  VALUES (@batch, @n, 'info', N'accepted and staged', NULL, @item, 1);

  INSERT INTO @cellItems (ItemId, ItemVersionId) VALUES (@item, @version);
  SET @n = @n + 1;
END

-- One item in a different cell of the same batch, which is the "a C1 cell with one item" case: a 10%
-- floor rounded down to zero would drop it from the audit entirely.
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'C1', 'sentence_cloze', 'demo', 'validated', 'd38-small');
DECLARE @smallItem INT = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@smallItem, 1, N'Small cell stem', N'a', N'b', N'c', N'd', 'B');
DECLARE @smallVersion INT = SCOPE_IDENTITY();
INSERT INTO ImportLog (BatchId, RowNo, Level, Message, RuleCode, ItemId, Attempt)
VALUES (@batch, 100, 'info', N'accepted and staged', NULL, @smallItem, 1);

-- ================================================================================================
-- 2. A stratum is one cell of one batch, and a sample never exceeds its stratum.
-- ================================================================================================
INSERT INTO ReviewStrata (BatchId, TopicId, CefrLevel, ItemCount, SampleSize)
VALUES (@batch, @topic, 'B1', 20, 2);
DECLARE @stratum INT = SCOPE_IDENTITY();
IF (SELECT Status FROM ReviewStrata WHERE StratumId = @stratum) <> 'pending'
  THROW 50000, 'EXPECT_REJECT FAILED: a freshly drawn stratum did not start pending', 1;
IF (SELECT WrittenByService FROM ReviewStrata WHERE StratumId = @stratum) IS NOT NULL
  THROW 50000, 'EXPECT_REJECT FAILED: drawing a sample - which derives nothing - demanded a writer declaration', 1;
PRINT 'accept: a pending stratum needs no writer, because a sample is not yet a decision';

BEGIN TRY
  INSERT INTO ReviewStrata (BatchId, TopicId, CefrLevel, ItemCount, SampleSize)
  VALUES (@batch, @topic, 'B1', 20, 3);
  THROW 50000, 'EXPECT_REJECT FAILED: a second stratum for one cell of one batch was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF OBJECT_NAME(ERROR_NUMBER()) IS NULL AND ERROR_NUMBER() NOT IN (2601, 2627)
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the duplicate cell was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refuse: two strata for one (batch, topic, level)';

-- A 1-item cell still samples one. The floor is the rule that makes story 29 true.
INSERT INTO ReviewStrata (BatchId, TopicId, CefrLevel, ItemCount, SampleSize)
VALUES (@batch, @topic, 'C1', 1, 1);
DECLARE @smallStratum INT = SCOPE_IDENTITY();
IF (SELECT SampleSize FROM ReviewStrata WHERE StratumId = @smallStratum) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: a one-item cell did not sample its one item', 1;
PRINT 'accept: a one-item cell samples one item, never zero';

-- A sample larger than its stratum is not a sample.
BEGIN TRY
  INSERT INTO ReviewStrata (BatchId, TopicId, CefrLevel, ItemCount, SampleSize)
  VALUES (@batch, @otherTopic, 'B1', 2, 5);
  THROW 50000, 'EXPECT_REJECT FAILED: a sample larger than its stratum was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ReviewStrata_SampleSize%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the oversized sample was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refuse: a sample larger than the cell it samples';

-- ================================================================================================
-- 3. Blind solve before key: W-01, and a verdict only on a solve.
-- ================================================================================================
DECLARE @sample1 INT = (SELECT TOP 1 ItemId FROM @cellItems ORDER BY ItemId);
DECLARE @version1 INT = (SELECT ItemVersionId FROM @cellItems WHERE ItemId = @sample1);
DECLARE @sample2 INT = (SELECT ItemId FROM @cellItems ORDER BY ItemId OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY);
DECLARE @version2 INT = (SELECT ItemVersionId FROM @cellItems WHERE ItemId = @sample2);

INSERT INTO ReviewSampleItems (StratumId, BatchId, ItemId, ItemVersionId)
VALUES (@stratum, @batch, @sample1, @version1);
DECLARE @drawn1 INT = SCOPE_IDENTITY();

-- The control: solve first, then the key. That is the only order the ticket allows.
UPDATE ReviewSampleItems
  SET SolvedLabel = 'A', SolvedAt = SYSDATETIMEOFFSET(), SolvedBy = @reviewer
WHERE SampleItemId = @drawn1;
BEGIN TRY
  UPDATE ReviewSampleItems SET KeyRevealedAt = SYSDATETIMEOFFSET() WHERE SampleItemId = @drawn1;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: solve-then-reveal was refused: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
PRINT 'accept: a key revealed after the solve was recorded';

-- The refusal: the SAME item, a fresh sample row, key revealed first and solve recorded after.
INSERT INTO ReviewSampleItems (StratumId, BatchId, ItemId, ItemVersionId)
VALUES (@stratum, @batch, @sample2, @version2);
DECLARE @drawn2 INT = SCOPE_IDENTITY();
UPDATE ReviewSampleItems SET KeyRevealedAt = SYSDATETIMEOFFSET() WHERE SampleItemId = @drawn2;
BEGIN TRY
  UPDATE ReviewSampleItems
    SET SolvedLabel = 'C', SolvedAt = SYSDATETIMEOFFSET(), SolvedBy = @reviewer
  WHERE SampleItemId = @drawn2;
  THROW 50000, 'EXPECT_REJECT FAILED: a solve recorded after the key was read was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%W-01%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the late solve was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT SolvedLabel FROM ReviewSampleItems WHERE SampleItemId = @drawn2) IS NOT NULL
  THROW 50000, 'EXPECT_REJECT FAILED: the refused late solve was recorded anyway', 1;
PRINT 'refuse: a solve recorded after the authored key was read';

-- And a verdict on an item nobody solved is not an adjudication, it is a self-review.
BEGIN TRY
  UPDATE ReviewSampleItems
    SET Verdict = 'key_error', VerdictAt = SYSDATETIMEOFFSET(), VerdictBy = @reviewer
  WHERE SampleItemId = @drawn2;
  THROW 50000, 'EXPECT_REJECT FAILED: an unsolved item was adjudicated', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ReviewSampleItems_VerdictFollowsSolve%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the unsolved adjudication was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refuse: a verdict on an item no reviewer solved';

-- ================================================================================================
-- 4. A stratum samples its own batch's items, and nothing else.
-- ================================================================================================
-- An item that exists but arrived in no batch this stratum audits - the "point it at someone else's
-- stock" bypass. It is a perfectly valid item with a valid version, so nothing but the batch rule can
-- refuse it.
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'validated', 'd38-foreign');
DECLARE @foreignItem INT = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@foreignItem, 1, N'Foreign stem', N'a', N'b', N'c', N'd', 'A');
DECLARE @foreignVersion INT = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO ReviewSampleItems (StratumId, BatchId, ItemId, ItemVersionId)
  VALUES (@stratum, @batch, @foreignItem, @foreignVersion);
  THROW 50000, 'EXPECT_REJECT FAILED: a stratum sampled an item that arrived in no batch it audits', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%may only sample items that arrived in its own batch%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the foreign item was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refuse: a stratum sampling stock from outside its batch';

-- A second batch's item, with a verdict in ITS batch, is still not this stratum's to decide.
INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy, CreatedAt)
VALUES (NULL, N'd38-other-pool.json', 'done', @uploader, SYSDATETIMEOFFSET());
DECLARE @otherBatch INT = SCOPE_IDENTITY();
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'validated', 'd38-otherbatch');
DECLARE @otherItem INT = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@otherItem, 1, N'Other batch stem', N'a', N'b', N'c', N'd', 'A');
DECLARE @otherVersion INT = SCOPE_IDENTITY();
INSERT INTO ImportLog (BatchId, RowNo, Level, Message, RuleCode, ItemId, Attempt)
VALUES (@otherBatch, 1, 'info', N'accepted and staged', NULL, @otherItem, 1);

BEGIN TRY
  INSERT INTO ReviewSampleItems (StratumId, BatchId, ItemId, ItemVersionId)
  VALUES (@stratum, @batch, @otherItem, @otherVersion);
  THROW 50000, 'EXPECT_REJECT FAILED: a stratum sampled another batch''s item', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%may only sample items that arrived in its own batch%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the other-batch item was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refuse: a stratum sampling another batch''s item';

-- A version that belongs to a different item is not this item''s key that was solved.
DECLARE @sample3 INT = (SELECT ItemId FROM @cellItems ORDER BY ItemId OFFSET 2 ROWS FETCH NEXT 1 ROWS ONLY);
BEGIN TRY
  INSERT INTO ReviewSampleItems (StratumId, BatchId, ItemId, ItemVersionId)
  VALUES (@stratum, @batch, @sample3, @version1);
  THROW 50000, 'EXPECT_REJECT FAILED: a sample pinned another item''s version', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ReviewSampleItems_VersionItem%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the mismatched version was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refuse: a sample pinning a version that belongs to another item';

-- One item is sampled once per stratum: a second draw of the same item is a double count, which would
-- let a 2-of-20 sample look like 2 of 21.
BEGIN TRY
  INSERT INTO ReviewSampleItems (StratumId, BatchId, ItemId, ItemVersionId)
  VALUES (@stratum, @batch, @sample1, @version1);
  THROW 50000, 'EXPECT_REJECT FAILED: the same item was sampled twice into one stratum', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UQ_ReviewSampleItems_Stratum_Item%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the double draw was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refuse: the same item drawn twice into one stratum';

-- ================================================================================================
-- 5. The decision is derived, and has exactly one writer (ADR-0012).
-- ================================================================================================
BEGIN TRY
  UPDATE ReviewStrata
    SET Status = 'approved', DecidedAt = SYSDATETIMEOFFSET(),
        WrittenByService = 'StratifiedAuditService', ProjectionVersion = 1
  WHERE StratumId = @stratum;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the stratum writer could not take its own decision: '
    + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
IF (SELECT Status FROM ReviewStrata WHERE StratumId = @stratum) <> 'approved'
  THROW 50000, 'EXPECT_REJECT FAILED: the owner''s stratum decision did not take effect', 1;
PRINT 'accept: a stratum decision taken by StratifiedAuditService';

-- A second writer. Clearing a stratum is what #205's per-item clearance did; a service other than the
-- one that computes the outcome declaring the decision is the second answer to "was this promoted".
BEGIN TRY
  UPDATE ReviewStrata
    SET Status = 'quarantined', QuarantineReason = 'key_error', DecidedAt = SYSDATETIMEOFFSET(),
        WrittenByService = 'IngestionWorkbenchService', ProjectionVersion = 2
  WHERE StratumId = @stratum;
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer took a stratum decision', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%stratum decision has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the second stratum writer was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT Status FROM ReviewStrata WHERE StratumId = @stratum) <> 'approved'
  THROW 50000, 'EXPECT_REJECT FAILED: the refused stratum write changed the row anyway', 1;

-- The input that would pass a value-only guard while being wrong: a second writer that changes only
-- Status, leaving WrittenByService holding the owner's name from the statement above.
BEGIN TRY
  UPDATE ReviewStrata
    SET Status = 'quarantined', QuarantineReason = 'key_error', DecidedAt = SYSDATETIMEOFFSET(),
        ProjectionVersion = 2
  WHERE StratumId = @stratum;
  THROW 50000, 'EXPECT_REJECT FAILED: a second writer inherited the stratum owner''s name', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%stratum decision has exactly one writer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the inherited-name stratum write was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
IF (SELECT Status FROM ReviewStrata WHERE StratumId = @stratum) <> 'approved'
  THROW 50000, 'EXPECT_REJECT FAILED: a stratum write that inherited the owner''s name changed the outcome', 1;
PRINT 'refuse: a stratum decision by a second writer, and one that inherited the owner''s name';

-- Equal is the case a plausible `>=` lets through; lower is the case a `<>` lets through.
BEGIN TRY
  UPDATE ReviewStrata
    SET Status = 'quarantined', QuarantineReason = 'key_error', DecidedAt = SYSDATETIMEOFFSET(),
        WrittenByService = 'StratifiedAuditService', ProjectionVersion = 1
  WHERE StratumId = @stratum;
  THROW 50000, 'EXPECT_REJECT FAILED: an equal projection version overwrote a taken decision', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%projection version must be newer%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the equal-version stratum write was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refuse: an equal projection version on a taken stratum decision';

-- The two ways a decision can contradict itself.
BEGIN TRY
  INSERT INTO ReviewStrata (BatchId, TopicId, CefrLevel, ItemCount, SampleSize, Status, QuarantineReason)
  VALUES (@batch, @otherTopic, 'B1', 2, 2, 'quarantined', NULL);
  THROW 50000, 'EXPECT_REJECT FAILED: a quarantined stratum without a reason was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ReviewStrata_QuarantineNamesItsReason%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the unexplained quarantine was refused by the wrong guard: '
      + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
BEGIN TRY
  INSERT INTO ReviewStrata (BatchId, TopicId, CefrLevel, ItemCount, SampleSize, Status)
  VALUES (@batch, @otherTopic, 'B1', 2, 2, 'approved');
  THROW 50000, 'EXPECT_REJECT FAILED: a decided stratum with no decision time was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ReviewStrata_DecisionIsStamped%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the undated decision was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
END CATCH
PRINT 'refuse: a quarantine with no reason, and a decision with no timestamp';

-- ================================================================================================
-- 6. CRITERION 5: a quarantined stratum's items are refused by SELECTION.
-- ================================================================================================
-- The selector below is the pool drill's own predicate (PracticeService.PlanPoolItemsAsync):
-- lifecycle = 'active' AND tier, in the cell. It is written out here rather than trusted, because
-- "quarantined is invisible" is a claim about THIS query, not about the six-state CHECK.
--
-- The items start `validated`, so the approval edge runs first: that is the promotion a stratum
-- approval performs, and it is what makes the "before" count 20 rather than 0.
UPDATE Items SET LifecycleStatus = 'active' WHERE ItemId IN (SELECT ItemId FROM @cellItems);

DECLARE @activeBefore INT = (
  SELECT COUNT(*) FROM Items i
  WHERE i.TopicId = @topic AND i.CefrLevel = 'B1' AND i.Modality = 'reading'
    AND i.LifecycleStatus = 'active' AND i.ContentTier = 'demo');

-- Quarantine the whole stratum, exactly as one key error would.
UPDATE Items
  SET LifecycleStatus = 'quarantined'
WHERE ItemId IN (SELECT ItemId FROM @cellItems);

DECLARE @activeAfter INT = (
  SELECT COUNT(*) FROM Items i
  WHERE i.TopicId = @topic AND i.CefrLevel = 'B1' AND i.Modality = 'reading'
    AND i.LifecycleStatus = 'active' AND i.ContentTier = 'demo');

IF @activeAfter <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: a quarantined stratum''s items are still returned by selection', 1;
IF @activeBefore <> 20
BEGIN
  SET @msg = N'EXPECT_REJECT FAILED: the selection control is not measuring the stratum - '
    + CONVERT(NVARCHAR(10), @activeBefore) + N' active items before the quarantine, not 20';
  THROW 50000, @msg, 1;
END

-- The control that makes the zero mean something: the small cell is NOT quarantined, so a selector that
-- returned nothing at all would fail this. That is the difference between "the stratum is excluded" and
-- "the query is broken".
UPDATE Items SET LifecycleStatus = 'active' WHERE ItemId = @smallItem;
IF (SELECT COUNT(*) FROM Items i
    WHERE i.TopicId = @topic AND i.CefrLevel = 'C1' AND i.Modality = 'reading'
      AND i.LifecycleStatus = 'active' AND i.ContentTier = 'demo') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the selector returned nothing for an active cell, so the zero above proves nothing', 1;
PRINT 'a quarantined stratum is invisible to selection, and an active one is not';

-- And the whole stratum moved, not one item: criterion 3's "the entire stratum".
IF (SELECT COUNT(*) FROM Items WHERE ItemId IN (SELECT ItemId FROM @cellItems)
    AND LifecycleStatus = 'quarantined') <> 20
  THROW 50000, 'EXPECT_REJECT FAILED: quarantining the stratum moved one item instead of all twenty', 1;
PRINT 'all twenty items of the stratum are quarantined, not the one the key error was found on';

-- ================================================================================================
-- 7. The count: nothing above created or removed a row it did not mean to.
-- ================================================================================================
IF (SELECT COUNT(*) FROM ReviewStrata) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the stratum fixture holds a row it should not', 1;
IF (SELECT COUNT(*) FROM ReviewSampleItems) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the sample fixture holds a row it should not', 1;
PRINT 'the stratified audit: one cell per batch, blind before key, and a quarantine selection cannot see';
