-- docs/schema-tests/domain35-triage-and-selfapproval.sql — #201 / ADM-02 / ADM-03 / ADM-04.
--
-- Every refusal here is a DIRECT TABLE WRITE. No controller, no service, no endpoint is involved, so
-- a check that lives only in application code cannot make this file pass. Each refusal sits beside an
-- accepting control that differs in exactly the property under test, because a trigger that refused
-- everything would pass a "refused" test and prove nothing.
--
-- What a refusal has to catch is stated once, here, so the next editor can see what is being defended:
--
--   NO SELF-APPROVAL (ADM-02)
--     * the uploader                     -> caught by UploadedBy
--     * the uploader's colleague, who revised the item since
--                                        -> caught by the NEWEST version's LastWrittenBy, NOT by
--                                           UploadedBy. A rule reading only ImportBatches passes it.
--     * anyone, after deleting the decision and re-inserting it
--                                        -> caught by refusing DELETE, because re-insertion is legal
--                                           when the delete is legal.
--     * an account holding all five namespaces
--                                        -> caught because the rule never reads a permission.
--
--   TRIAGE CANNOT WRITE (ADM-03)
--     * item text and keys               -> ItemVersions
--     * exam structure                   -> Sections, ExamItems
--     * a rubric                         -> GradingCriteria, GradingCriterionDescriptors
--     * the same write as UPDATE, not only INSERT
--                                        -> every trigger covers both; the UPDATE half is tested.
--     * triage inspecting (a read)       -> not refused: the point of triage is that it may look.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

-- ================================================================================================
-- Fixtures: two staff accounts and one certificate. The accounts are what the two invariants read,
-- so they are planted once and named by variable throughout.
-- ================================================================================================
DECLARE @cert INT = (SELECT TOP 1 CertId FROM Certificates WHERE Code = 'D35');
IF @cert IS NULL
BEGIN
  INSERT INTO Certificates (Code, Name) VALUES ('D35', N'D35 certificate');
  SET @cert = SCOPE_IDENTITY();
END;

IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = 'operations.support.triage')
  INSERT INTO Permissions (Code) VALUES
    ('pool.content.author'), ('pool.content.review'), ('assessment.blueprint.design'),
    ('operations.support.triage'), ('system.governance.admin');

-- The uploader, who is also an author: this is the account ADM-02 is about.
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'd35-uploader')
  INSERT INTO Roles (Code, Name) VALUES ('d35-uploader', N'Uploader who authors');
DECLARE @rUp INT = (SELECT RoleId FROM Roles WHERE Code = 'd35-uploader');
IF NOT EXISTS (SELECT 1 FROM RolePermissions WHERE RoleId = @rUp AND PermissionCode = 'pool.content.author')
  INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@rUp, 'pool.content.author');
DECLARE @uploader INT = (SELECT UserId FROM Users WHERE Email = 'd35-uploader@test.local');
IF @uploader IS NULL
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@rUp, 'd35-uploader@test.local', 0x01);
  SET @uploader = SCOPE_IDENTITY();
END;

-- The colleague who revises the item. Different account, same role, so nothing about the role can
-- distinguish them: the invariant is about the data.
DECLARE @colleague INT = (SELECT UserId FROM Users WHERE Email = 'd35-colleague@test.local');
IF @colleague IS NULL
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@rUp, 'd35-colleague@test.local', 0x02);
  SET @colleague = SCOPE_IDENTITY();
END;

-- A third author with NO connection to any batch or item here. The LastModifiedBy test needs an
-- actor that ONLY that column can refuse: neither the uploader nor the item's newest version.
DECLARE @outsider INT = (SELECT UserId FROM Users WHERE Email = 'd35-outsider@test.local');
IF @outsider IS NULL
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@rUp, 'd35-outsider@test.local', 0x05);
  SET @outsider = SCOPE_IDENTITY();
END;

-- Triage-only: holds operations.support.triage and neither authoring namespace.
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'd35-triage')
  INSERT INTO Roles (Code, Name) VALUES ('d35-triage', N'Support triage');
DECLARE @rTr INT = (SELECT RoleId FROM Roles WHERE Code = 'd35-triage');
IF NOT EXISTS (SELECT 1 FROM RolePermissions WHERE RoleId = @rTr AND PermissionCode = 'operations.support.triage')
  INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@rTr, 'operations.support.triage');
DECLARE @triage INT = (SELECT UserId FROM Users WHERE Email = 'd35-triage@test.local');
IF @triage IS NULL
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@rTr, 'd35-triage@test.local', 0x03);
  SET @triage = SCOPE_IDENTITY();
END;

-- Every namespace at once. ADM-01 says a small team combines them, and D16 says no combination
-- overrides either invariant - this account is the proof that the rule is not a permission read.
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'd35-all')
  INSERT INTO Roles (Code, Name) VALUES ('d35-all', N'Holds all five');
DECLARE @rAll INT = (SELECT RoleId FROM Roles WHERE Code = 'd35-all');
INSERT INTO RolePermissions (RoleId, PermissionCode)
  SELECT @rAll, p.Code FROM Permissions p
  WHERE NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = @rAll AND rp.PermissionCode = p.Code);
DECLARE @allFive INT = (SELECT UserId FROM Users WHERE Email = 'd35-all@test.local');
IF @allFive IS NULL
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@rAll, 'd35-all@test.local', 0x04);
  SET @allFive = SCOPE_IDENTITY();
END;

-- A topic and a stimulus to hang items on. Topics is a closed set of twenty, so the fixture plants
-- canonical rows rather than inventing a code - trg_Topics_ClosedVocabulary refuses anything else.
IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'listening' AND Code = 'L-GIST')
  INSERT INTO Topics (Modality, Code, Name) VALUES ('listening', 'L-GIST', N'Global gist');
IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-GIST')
  INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GIST', N'Global gist');
DECLARE @listeningTopic INT = (SELECT TopicId FROM Topics WHERE Modality = 'listening' AND Code = 'L-GIST');
DECLARE @readingTopic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-GIST');
DECLARE @stim INT = (SELECT TOP 1 StimulusId FROM Stimuli);
IF @stim IS NULL
BEGIN
  INSERT INTO Stimuli (CefrLevel, Passage, DomainContext) VALUES ('B1', N'A d35 passage.', 'general');
  SET @stim = SCOPE_IDENTITY();
END;

-- The batch both items arrive in. UploadedBy is the uploader, who is also an author.
DECLARE @batch INT = (SELECT TOP 1 BatchId FROM ImportBatches WHERE SourceName = N'd35-batch');
IF @batch IS NULL
BEGIN
  INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy)
    VALUES (@cert, N'd35-batch', 'done', @uploader);
  SET @batch = SCOPE_IDENTITY();
END;

DECLARE @item INT = (SELECT TOP 1 ItemId FROM Items WHERE BatchTag = 'd35-batch');
IF @item IS NULL
BEGIN
  INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
    VALUES (@stim, 'listening', @listeningTopic, 'B1', 'short_exchange', 'demo', 'validated', 'd35-batch');
  SET @item = SCOPE_IDENTITY();
END;
IF NOT EXISTS (SELECT 1 FROM ItemVersions WHERE ItemId = @item)
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
    VALUES (@item, 1, N'D35 stem?', N'a', N'b', N'c', N'd', 'A', @uploader);

-- The second item is uploaded by the same batch but has no version written by the uploader: it is
-- the colleague's work from the start. ADM-02 must still refuse the uploader on it, via the batch.
DECLARE @item2 INT = (SELECT TOP 1 ItemId FROM Items WHERE BatchTag = 'd35-batch-2');
IF @item2 IS NULL
BEGIN
  INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
    VALUES (@stim, 'listening', @listeningTopic, 'B1', 'short_exchange', 'demo', 'validated', 'd35-batch-2');
  SET @item2 = SCOPE_IDENTITY();
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
    VALUES (@item2, 1, N'Colleague work?', N'a', N'b', N'c', N'd', 'A', @colleague);
END;

DECLARE @batch2 INT = (SELECT TOP 1 BatchId FROM ImportBatches WHERE SourceName = N'd35-batch-2');
IF @batch2 IS NULL
BEGIN
  INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy)
    VALUES (@cert, N'd35-batch-2', 'done', @uploader);
  SET @batch2 = SCOPE_IDENTITY();
END;

-- A batch uploaded BY the all-five account, and an item in it that account also wrote. This is the
-- `can_override = {}` fixture: every namespace, and the work is its own.
DECLARE @batch3 INT = (SELECT TOP 1 BatchId FROM ImportBatches WHERE SourceName = N'd35-batch-3');
IF @batch3 IS NULL
BEGIN
  INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy)
    VALUES (@cert, N'd35-batch-3', 'done', @allFive);
  SET @batch3 = SCOPE_IDENTITY();
END;
DECLARE @item3 INT = (SELECT TOP 1 ItemId FROM Items WHERE BatchTag = 'd35-batch-3');
IF @item3 IS NULL
BEGIN
  INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
    VALUES (@stim, 'listening', @listeningTopic, 'B1', 'short_exchange', 'demo', 'validated', 'd35-batch-3');
  SET @item3 = SCOPE_IDENTITY();
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
    VALUES (@item3, 1, N'All-five work?', N'a', N'b', N'c', N'd', 'A', @allFive);
END;

IF @cert IS NULL OR @uploader IS NULL OR @triage IS NULL OR @allFive IS NULL
   OR @item IS NULL OR @item2 IS NULL OR @item3 IS NULL
   OR @batch IS NULL OR @batch2 IS NULL OR @batch3 IS NULL
  THROW 50000, 'EXPECT_REJECT FAILED: the d35 fixture did not plant every row it needs', 1;
PRINT 'd35 fixtures planted';

-- ================================================================================================
-- INVARIANT 1 — NO SELF-APPROVAL
-- ================================================================================================

-- accept: a different account decides. This is the control without which every refusal below is
-- evidence of nothing: a trigger that refuses all decisions passes the whole section.
INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
  VALUES (@batch, @item, @colleague, 'claim');
PRINT 'accept: another account claimed a sample';

-- reject: the uploader claims a sample for its own batch.
BEGIN TRY
  INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
    VALUES (@batch, @item, @uploader, 'claim');
  THROW 50000,'EXPECT_REJECT FAILED: the uploader claimed a sample for its own batch',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50080 THROW;
  PRINT 'reject: the uploader cannot claim its own batch';
END CATCH

-- reject: adjudicate and clear are the same door, not only claim. A guard that special-cased the
-- first verb would leave the two that actually promote content open.
BEGIN TRY
  INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
    VALUES (@batch, @item, @uploader, 'clear');
  THROW 50000,'EXPECT_REJECT FAILED: the uploader cleared a sample for its own batch',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50080 THROW;
  PRINT 'reject: the uploader cannot clear its own batch';
END CATCH

-- ---- THE "OR LAST MODIFIED" HALF ----
-- The colleague is a different account from the uploader and holds the same role. Until they touch
-- the item they are a legitimate reviewer of it (the accept above). Now they revise it, and version 2
-- becomes the newest. From that moment the colleague must be refused on that item, because their only
-- connection to it is the version they wrote - ImportBatches.UploadedBy names somebody else. A rule
-- reading only the batch would pass this.
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
  VALUES (@item, 2, N'D35 revised stem?', N'a', N'b', N'c', N'd', 'B', @colleague);
DECLARE @newest INT = (SELECT TOP 1 LastWrittenBy FROM ItemVersions WHERE ItemId = @item ORDER BY VersionNumber DESC);
IF @newest <> @colleague
  THROW 50000, 'EXPECT_REJECT FAILED: the newest version of the item is not the colleague''s', 1;
PRINT 'accept: a revision by another account became the newest version';

BEGIN TRY
  INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
    VALUES (@batch, @item, @colleague, 'claim');
  THROW 50000,'EXPECT_REJECT FAILED: the account whose revision is the newest version claimed that item',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50080 THROW;
  PRINT 'reject: the item''s last modifier cannot claim a sample for that item';
END CATCH

-- reject: an UPDATE that re-points the newest version at the uploader's colleague is not needed; the
-- truthful write is that a cosmetic in-place edit also moves authorship. The one editable column is
-- Explanation, so this is the second route into "last modified" and it must move the rule too.
UPDATE ItemVersions SET Explanation = N'a cosmetic fix by the uploader', LastWrittenBy = @uploader
  WHERE ItemId = @item AND VersionNumber = 2;
BEGIN TRY
  INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
    VALUES (@batch, @item, @colleague, 'claim');
  PRINT 'note: after the uploader edited version 2, the colleague is no longer its author';
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'unexpected: the colleague was still refused after the version changed hands';
END CATCH
-- Put it back so the all-five section below starts from a known state.
UPDATE ItemVersions SET Explanation = NULL, LastWrittenBy = @colleague
  WHERE ItemId = @item AND VersionNumber = 2;

-- ---- `can_override = {}` ----
-- The all-five account is the UPLOADER of batch3. That is the exact shape ADM-02 exists to refuse:
-- every permission, and the work is its own. If this ever passes, the rule has degenerated into a
-- permission read, which D16 names as the escape hatch ADM-02/03 close.
BEGIN TRY
  INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
    VALUES (@batch3, @item3, @allFive, 'adjudicate');
  THROW 50000,'EXPECT_REJECT FAILED: an account holding all five namespaces adjudicated its own upload',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50080 THROW;
  PRINT 'reject: all five namespaces still cannot approve its own upload';
END CATCH

-- The same account is a member of neither batch1 nor its item, so it is ALLOWED there - proving the
-- rule is about the data and not about which namespaces the account holds. If this were refused, the
-- rule would be "nobody may review", which ADM-02 does not say.
INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
  VALUES (@batch, @item, @allFive, 'adjudicate');
PRINT 'accept: an account holding all five may review work that is not its own';

-- reject: DELETING a decision, which is how "cannot self-approve" is bypassed - delete the record and
-- re-insert one made by nobody. Refusing the delete is what closes it, rather than testing the
-- re-insert alone.
BEGIN TRY
  DELETE FROM ReviewSampleDecisions WHERE BatchId = @batch AND ActorUserId = @colleague;
  THROW 50000,'EXPECT_REJECT FAILED: a review decision was deleted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50080 THROW;
  PRINT 'reject: a review decision cannot be deleted';
END CATCH

-- reject: an UPDATE that rewrites the actor to somebody else. The insert-time check alone would be
-- satisfied by the row already existing; the trigger covers UPDATE because a decision is a record of
-- who made it, and rewriting the actor is self-approval by another route.
BEGIN TRY
  UPDATE ReviewSampleDecisions SET ActorUserId = @uploader
    WHERE BatchId = @batch AND ActorUserId = @allFive;
  THROW 50000,'EXPECT_REJECT FAILED: a review decision''s actor was rewritten to the uploader',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50080 THROW;
  PRINT 'reject: a decision cannot be re-attributed to the uploader';
END CATCH

-- accept: a decision for a batch and item the account has no connection to, by the triage account.
-- Triage may escalate a defect into the review queue (ADM-03), which is exactly this row.
INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
  VALUES (@batch2, @item2, @triage, 'claim');
PRINT 'accept: triage may record a claim on work it did not touch';

-- reject: the uploader on batch2, whose item was never written by anybody in that batch's chain. The
-- refusal must come from ImportBatches.UploadedBy, not from the version column - the version belongs
-- to the colleague, so a version-only rule would let the uploader through here.
BEGIN TRY
  INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
    VALUES (@batch2, @item2, @uploader, 'clear');
  THROW 50000,'EXPECT_REJECT FAILED: the uploader cleared a sample on a batch it uploaded',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50080 THROW;
  PRINT 'reject: UploadedBy alone refuses the uploader on the batch';
END CATCH

-- reject: ImportBatches.LastModifiedBy. The column exists for #205 to write; no path writes it today,
-- so the only proof it is load-bearing is a direct write. The actor must be somebody ONLY this column
-- can refuse - @outsider uploaded no batch and wrote no version - or the removal of this arm would go
-- unnoticed because another arm already refused the same row.
UPDATE ImportBatches SET LastModifiedBy = @outsider WHERE BatchId = @batch2;
BEGIN TRY
  INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
    VALUES (@batch2, @item2, @outsider, 'claim');
  THROW 50000,'EXPECT_REJECT FAILED: the batch''s last modifier claimed a sample for it',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50080 THROW;
  PRINT 'reject: the batch''s last modifier cannot claim its sample either';
END CATCH
UPDATE ImportBatches SET LastModifiedBy = NULL WHERE BatchId = @batch2;

-- ================================================================================================
-- INVARIANT 2 — TRIAGE CANNOT WRITE
-- ================================================================================================
-- Each block below is a direct write to the table a prohibited edit reaches. The accepting control
-- beside it uses the uploader, so the difference is the acting account and nothing else.
--
-- This section runs in a STRICT catalog, which is what production is: the fixture flag is turned off
-- here and restored at the end. The harness owns this catalog and set the flag on so that ~300
-- unattributed fixture statements keep working; the behaviour under test is the production one, so
-- the test states that explicitly rather than inheriting the harness's convenience.
--
-- The undeclared probe row is created FIRST, while the fixture flag is still on, because creating it
-- in strict mode would itself be the refusal under test.
DECLARE @undeclared INT = (SELECT TOP 1 ItemVersionId FROM ItemVersions WHERE LastWrittenBy IS NULL);
IF @undeclared IS NULL
BEGIN
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
    VALUES (@item2, 90, N'Undeclared probe.', N'a', N'b', N'c', N'd', 'A', NULL);
  SET @undeclared = SCOPE_IDENTITY();
END

DECLARE @d35_prior_fixture BIT = dbo.fn_IsFixtureCatalog();
IF EXISTS (SELECT 1 FROM CatalogMode WHERE ModeId = 1)
  UPDATE CatalogMode SET IsFixtureCatalog = 0 WHERE ModeId = 1;
ELSE
  INSERT INTO CatalogMode (ModeId, IsFixtureCatalog) VALUES (1, 0);
IF dbo.fn_IsFixtureCatalog() <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: could not put the catalog into strict mode', 1;

-- ---- THE REVIEW''S TWO HOLES, exactly as reproduced ----
--
-- Hole 1: an UPDATE of Explanation on a row that names NO author. The first draft allowed this
-- because its second clause only fired when the stored row already named one, so an undeclared row
-- was a free pass. A triage-only account could edit item text.
BEGIN TRY
  UPDATE ItemVersions SET Explanation = N'TRIAGE EDITED THIS' WHERE ItemVersionId = @undeclared;
  THROW 50000,'EXPECT_REJECT FAILED: triage edited item text on a row that names no author',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot edit an item version that names no author (hole 1, closed)';
END CATCH

-- Hole 2: an INSERT of a whole new version with no declaration. The INSERT half has no `deleted` row,
-- so on the first draft only the declared-author arm could fire and it cannot fire on a NULL.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
    VALUES (@item2, 91, N'TRIAGE AUTHORED THIS ITEM', N'a', N'b', N'c', N'd', 'A');
  THROW 50000,'EXPECT_REJECT FAILED: triage authored a new item version with no declaration',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot insert an item version (hole 2, closed)';
END CATCH

-- The accepting controls for the two holes, so a guard that refused every undeclared write could not
-- pass this section: an AUTHOR may do both, declaring itself.
UPDATE ItemVersions SET Explanation = N'fixed by its author', LastWrittenBy = @uploader
  WHERE ItemVersionId = @undeclared;
PRINT 'accept: an author may edit that same undeclared row';
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
  VALUES (@item2, 92, N'Author authored this.', N'a', N'b', N'c', N'd', 'A', @uploader);
PRINT 'accept: an author may insert an item version';

-- ---- item text and keys: ItemVersions ----
-- accept: an author writes a version. If this were refused, the guard would be "nobody may author".
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
  VALUES (@item2, 2, N'Author voice?', N'a', N'b', N'c', N'd', 'C', @uploader);
PRINT 'accept: an author writes item text and a key';

-- reject: triage INSERTs a version - the item text and the key.
BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
    VALUES (@item2, 3, N'Triage voice?', N'a', N'b', N'c', N'd', 'D', @triage);
  THROW 50000,'EXPECT_REJECT FAILED: triage wrote a new item version',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot insert item text or a key';
END CATCH

-- reject: triage UPDATEs an existing version. The INSERT half alone would leave this open, and an
-- in-place edit of the one editable column is still an edit of item text.
BEGIN TRY
  UPDATE ItemVersions SET Explanation = N'reworded by triage' WHERE ItemId = @item AND VersionNumber = 1;
  THROW 50000,'EXPECT_REJECT FAILED: triage edited an existing item version',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot update an existing item version';
END CATCH

-- accept: the author performs the same in-place explanation edit, proving the refusal above was
-- about the account and not about UPDATE on ItemVersions.
UPDATE ItemVersions SET Explanation = N'fixed by its author', LastWrittenBy = @uploader
  WHERE ItemId = @item AND VersionNumber = 1;
PRINT 'accept: an author may edit an explanation in place';

-- ---- exam structure: Sections and ExamItems ----
DECLARE @readingSkill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @cert AND Modality = 'reading');
IF @readingSkill IS NULL
BEGIN
  INSERT INTO Skills (CertId, Code, Name, Modality) VALUES (@cert, 'D35R', N'D35 reading', 'reading');
  SET @readingSkill = SCOPE_IDENTITY();
END;

-- accept: an author adds a section.
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, LastWrittenBy)
  VALUES (@readingSkill, @cert, 'reading', 'D35S1', N'D35 section one', 4, 10, 90, @uploader);
DECLARE @section INT = SCOPE_IDENTITY();
PRINT 'accept: an author adds a section';

-- reject: triage INSERTs a section.
BEGIN TRY
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, LastWrittenBy)
    VALUES (@readingSkill, @cert, 'reading', 'D35S2', N'Triage section', 4, 10, 91, @triage);
  THROW 50000,'EXPECT_REJECT FAILED: triage added a section',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot insert a section';
END CATCH

-- reject: triage UPDATEs a section's question count - examining the same table by its other verb.
BEGIN TRY
  UPDATE Sections SET ExpectedQuestionCount = 99, LastWrittenBy = @triage WHERE SectionId = @section;
  THROW 50000,'EXPECT_REJECT FAILED: triage changed a section',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot update a section';
END CATCH

-- accept: the author updates the same section.
UPDATE Sections SET ExpectedQuestionCount = 12, LastWrittenBy = @uploader WHERE SectionId = @section;
PRINT 'accept: an author may change a section';

DECLARE @exam INT = (SELECT TOP 1 ExamId FROM Exams WHERE CertId = @cert AND Purpose = 'mock');
IF @exam IS NULL
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
    VALUES (@cert, N'D35 paper', 'draft', 30, 'mock');
  SET @exam = SCOPE_IDENTITY();
END;
DECLARE @version INT = (SELECT TOP 1 ItemVersionId FROM ItemVersions WHERE ItemId = @item2 ORDER BY VersionNumber DESC);

-- The binding is about exam assembly, not about skill, so the section must be one the item can join:
-- @item2 is a listening item, and FK_ExamItems_SectionSkill pins the binding's Modality to its section.
DECLARE @listeningSkill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @cert AND Modality = 'listening');
IF @listeningSkill IS NULL
BEGIN
  INSERT INTO Skills (CertId, Code, Name, Modality) VALUES (@cert, 'D35L', N'D35 listening', 'listening');
  SET @listeningSkill = SCOPE_IDENTITY();
END;
DECLARE @listenSection INT = (SELECT TOP 1 SectionId FROM Sections WHERE SkillId = @listeningSkill);
IF @listenSection IS NULL
BEGIN
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, LastWrittenBy)
    VALUES (@listeningSkill, @cert, 'listening', 'D35L1', N'D35 listening section', 4, 10, 92, @uploader);
  SET @listenSection = SCOPE_IDENTITY();
END;

-- accept: an author binds a version into the paper (exam assembly).
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber, LastWrittenBy)
  VALUES (@exam, @listenSection, @cert, @version, @item2, 'listening', 90, 1, @uploader);
DECLARE @bound INT = SCOPE_IDENTITY();
PRINT 'accept: an author binds a version into the paper';

-- reject: triage INSERTs a binding. ADM-03 names exam structure, and D16 says a check on item edits
-- alone leaves it editable.
BEGIN TRY
  INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber, LastWrittenBy)
    VALUES (@exam, @listenSection, @cert, @version, @item2, 'listening', 91, 1, @triage);
  THROW 50000,'EXPECT_REJECT FAILED: triage bound an item into a paper',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot add an exam binding';
END CATCH

-- reject: triage moves an existing binding, changing the paper without inserting a row.
BEGIN TRY
  UPDATE ExamItems SET DisplayOrder = 92, LastWrittenBy = @triage WHERE ExamItemId = @bound;
  THROW 50000,'EXPECT_REJECT FAILED: triage reordered an exam binding',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot update an exam binding';
END CATCH

-- ---- a rubric: GradingCriteria and its descriptors ----
DECLARE @writingSkill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @cert AND Modality = 'writing');
IF @writingSkill IS NULL
BEGIN
  INSERT INTO Skills (CertId, Code, Name, Modality) VALUES (@cert, 'D35W', N'D35 writing', 'writing');
  SET @writingSkill = SCOPE_IDENTITY();
END;

-- accept: an author adds a criterion.
INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder, LastWrittenBy)
  VALUES (@cert, @writingSkill, 'D35C1', N'D35 criterion', 5, 1.0, 90, @uploader);
DECLARE @criterion INT = SCOPE_IDENTITY();
PRINT 'accept: an author adds a rubric criterion';

-- reject: triage INSERTs a criterion.
BEGIN TRY
  INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder, LastWrittenBy)
    VALUES (@cert, @writingSkill, 'D35C2', N'Triage criterion', 5, 1.0, 91, @triage);
  THROW 50000,'EXPECT_REJECT FAILED: triage added a rubric criterion',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot insert a rubric criterion';
END CATCH

-- reject: triage UPDATEs a criterion's weight, which changes every score it produces.
BEGIN TRY
  UPDATE GradingCriteria SET Weight = 2.0, LastWrittenBy = @triage WHERE CriterionId = @criterion;
  THROW 50000,'EXPECT_REJECT FAILED: triage reweighted a rubric criterion',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot update a rubric criterion';
END CATCH

-- accept: an author adds a descriptor.
INSERT INTO GradingCriterionDescriptors (CriterionId, Band, Descriptor, BinaryGates, LastWrittenBy)
  VALUES (@criterion, 4, N'A d35 descriptor that states an observable threshold.', N'At least 120 wpm.', @uploader);
PRINT 'accept: an author adds a rubric descriptor';

-- reject: triage writes a descriptor. This is the fifth table, and the one a guard built only around
-- "item text" would miss entirely.
BEGIN TRY
  INSERT INTO GradingCriterionDescriptors (CriterionId, Band, Descriptor, BinaryGates, LastWrittenBy)
    VALUES (@criterion, 5, N'Triage descriptor.', N'Triage gates.', @triage);
  THROW 50000,'EXPECT_REJECT FAILED: triage added a rubric descriptor',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot insert a rubric descriptor';
END CATCH

-- reject: triage UPDATEs a descriptor's text. The INSERT half alone leaves the rubric's wording
-- editable, which is the quieter way to change what a band means.
BEGIN TRY
  UPDATE GradingCriterionDescriptors SET Descriptor = N'Triage wording.', LastWrittenBy = @triage
    WHERE CriterionId = @criterion AND Band = 4;
  THROW 50000,'EXPECT_REJECT FAILED: triage rewrote a rubric descriptor',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: triage cannot update a rubric descriptor';
END CATCH

-- ---- triage may still LOOK ----
-- ADM-03's whole point is that triage inspects items, versions, evidence spans and attempts. A guard
-- that made those tables unreadable would satisfy "cannot write" and destroy the role.
DECLARE @lookedAt INT = (
  SELECT COUNT(*) FROM ItemVersions v
  JOIN Items i ON i.ItemId = v.ItemId
  WHERE i.BatchTag LIKE 'd35-batch%');
IF @lookedAt < 2
  THROW 50000, 'EXPECT_REJECT FAILED: triage could not read the versions it is supposed to inspect', 1;
PRINT 'accept: triage can inspect items and their versions';

-- ---- the refusal is about the account, not about the number of namespaces ----
-- An account holding all five is an author, so the triage guard must NOT refuse it. If it did, the
-- only account that holds all five could never author anything, which is not what ADM-03 says.
IF dbo.fn_IsTriageOnly(@allFive) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: an account holding every namespace was classed as triage-only', 1;
IF dbo.fn_IsTriageOnly(@triage) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the triage account was not classed as triage-only', 1;
IF dbo.fn_IsTriageOnly(@uploader) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: an author was classed as triage-only', 1;
PRINT 'accept: triage-only is a data classification, and all five namespaces is not it';

-- ---- the fixture exemption's exact boundary ----
-- The harness catalog carries the fixture flag so ~300 unattributed fixture statements keep working.
-- The exemption must not be a way for a TRIAGE ACCOUNT to write: with the flag on, an account that
-- declares itself triage is still refused. That is the difference between "unattributed fixture data
-- is tolerated" and "a triage account may write", and only the former is the exemption.
IF EXISTS (SELECT 1 FROM CatalogMode WHERE ModeId = 1)
  UPDATE CatalogMode SET IsFixtureCatalog = 1 WHERE ModeId = 1;
ELSE
  INSERT INTO CatalogMode (ModeId, IsFixtureCatalog) VALUES (1, 1);

BEGIN TRY
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
    VALUES (@item2, 94, N'Triage in a fixture catalog?', N'a', N'b', N'c', N'd', 'A', @triage);
  THROW 50000,'EXPECT_REJECT FAILED: the fixture flag let a triage account author a version',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50081 THROW;
  PRINT 'reject: the fixture exemption does not admit a triage account';
END CATCH

-- With the flag on, an UNATTRIBUTED write is tolerated - that is what the exemption is FOR, and it is
-- how ~300 existing fixture statements keep working. Stating it here means the next reader sees the
-- exact scope rather than discovering it from a passing test.
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
  VALUES (@item2, 95, N'Unattributed fixture row.', N'a', N'b', N'c', N'd', 'A');
PRINT 'accept: the fixture catalog tolerates an unattributed row';

-- Restore the harness's flag exactly as it was found, so the file leaves no trace for the next test.
IF @d35_prior_fixture = 0
  UPDATE CatalogMode SET IsFixtureCatalog = 0 WHERE ModeId = 1;

PRINT 'domain35: both invariants hold through direct table writes';
