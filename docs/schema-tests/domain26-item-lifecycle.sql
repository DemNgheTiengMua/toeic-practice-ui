SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #190: the item lifecycle - six states, the named transitions, and no hard delete.
--
-- The half a constraint can express is here; the half only a service can show (selection reads
-- active items of one tier, review reads a retired item) is in
-- tests/Services.Tests/ItemLifecycleTests.cs.
--
--   1. every named transition is reachable (CNT-42, CNT-43, CNT-44, CNT-45)
--   2. every other transition between the six states is refused, by name
--   3. a no-op write of the same state is accepted, because the bulk decommission is one
--      (CNT-46: UPDATE ... SET lifecycle_status = 'retired' WHERE content_tier = 'demo')
--   4. one illegal row refuses the whole statement, so a multi-row update cannot part-apply
--   5. a draft item is deletable, and every state that has left draft is not
--   6. an item a learner has referenced is not deletable, even in draft
--
-- Every CATCH checks the specific guard by name: a catch that proves only "something failed" cannot
-- tell a working guard from a typo in this file.
--
-- The edge list below is written out rather than derived from a rank. A rank ("the new state is
-- later in the list") accepts draft -> quarantined and validated -> retired_defective, which the
-- specification does not name; the pair table is what makes "and no other transition is reachable"
-- testable at all.
--
-- Two edges exist by derivation rather than by a sentence naming them, and both are needed for the
-- graph to be sound:
--
--   * `draft -> retired`. CNT-43 says an item that has left draft *or that any learner has
--     referenced* cannot be deleted. A draft item a paper or a learner already reached is therefore
--     undeletable and unservable, and retirement is the only disposal it has. Without this edge such
--     a row can never leave `draft` by any legal move.
--   * `validated -> quarantined`. W-01 quarantines the stratum that is awaiting audit - the items in
--     `validated`, not the live `active` ones - under the zero-tolerance key rule. Telemetry
--     quarantine (CNT-44) is the `active -> quarantined` edge and does not cover that path.
--
-- The invariant this file asserts on top of the edge list: every state a row can reach is either
-- deletable or has a legal move out of it. A transition table that lets a row arrive somewhere with
-- neither is the defect this test is for, so section 8 checks it directly instead of trusting that
-- the edge list above was written carefully.

DECLARE @guardWrong NVARCHAR(2048);
DECLARE @msg NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 0. the six states, and the eight named edges between them
-- ---------------------------------------------------------------------------------------------
DECLARE @states TABLE (State VARCHAR(20) NOT NULL PRIMARY KEY);
INSERT INTO @states (State) VALUES
  ('draft'), ('validated'), ('active'), ('quarantined'), ('retired'), ('retired_defective');

IF (SELECT COUNT(*) FROM @states) <> 6
  THROW 50000, 'EXPECT_REJECT FAILED: the state list is not the six CNT-42 names', 1;

DECLARE @allowed TABLE (FromState VARCHAR(20) NOT NULL, ToState VARCHAR(20) NOT NULL);
INSERT INTO @allowed (FromState, ToState) VALUES
  -- CNT-42: draft -> validated -> active
  ('draft',       'validated'),
  -- CNT-43: an item that has left draft can never be deleted, only retired. A validated item that
  -- will not be promoted is therefore retired rather than stranded between two forbidden acts.
  ('validated',   'retired'),
  ('validated',   'active'),
  -- CNT-40 / W-01: the zero-tolerance key rule quarantines the stratum awaiting audit, which is the
  -- validated one. Telemetry quarantine is the active -> quarantined edge below and does not cover it.
  ('validated',   'quarantined'),
  -- CNT-44: telemetry flips a live item to quarantined, and resolving it returns it to service.
  ('active',      'quarantined'),
  ('quarantined', 'active'),
  ('active',      'retired'),
  ('quarantined', 'retired'),
  -- CNT-45: a quarantined item confirmed defective becomes retired_defective, never active again.
  ('quarantined', 'retired_defective'),
  -- CNT-43: a draft item a paper or a learner already reached cannot be deleted, so retirement is
  -- its only disposal. Without this edge such a row could never leave draft.
  ('draft',       'retired');

IF (SELECT COUNT(*) FROM @allowed) <> 10
  THROW 50000, 'EXPECT_REJECT FAILED: the named-edge list is not the ten this file asserts', 1;

INSERT INTO Topics (Modality, Code, Name)
VALUES ('reading', 'R-GRAM', N'Morphosyntactic structure and lexical precision');
DECLARE @topic INT = SCOPE_IDENTITY();

-- ---------------------------------------------------------------------------------------------
-- 1 and 2. the 30 ordered pairs of distinct states: 10 accepted, 20 refused by name.
--
-- A fresh item per pair, inserted straight into its starting state: INSERT is the authoring act and
-- is not a transition (the schema's own default is draft, and an import writes draft). What CNT-42
-- constrains is the move between states after the row exists.
-- ---------------------------------------------------------------------------------------------
DECLARE @from VARCHAR(20), @to VARCHAR(20), @item INT, @isAllowed BIT;
DECLARE @checked INT = 0, @accepted INT = 0, @refused INT = 0;

DECLARE pair CURSOR LOCAL FAST_FORWARD FOR
  SELECT f.State, t.State, CASE WHEN a.FromState IS NULL THEN 0 ELSE 1 END
  FROM @states f
  CROSS JOIN @states t
  LEFT JOIN @allowed a ON a.FromState = f.State AND a.ToState = t.State
  WHERE f.State <> t.State;

OPEN pair;
FETCH NEXT FROM pair INTO @from, @to, @isAllowed;
WHILE @@FETCH_STATUS = 0
BEGIN
  SET @checked = @checked + 1;

  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
  VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', @from, 'lifecycle-' + CONVERT(VARCHAR(4), @checked));
  SET @item = SCOPE_IDENTITY();

  IF @isAllowed = 1
  BEGIN
    -- wrapped, so a guard that refuses a NAMED edge fails with a sentence rather than a raw THROW
    -- from the trigger: the verdict must say which transition it was, not which line raised 50060
    BEGIN TRY
      UPDATE Items SET LifecycleStatus = @to WHERE ItemId = @item;
    END TRY
    BEGIN CATCH
      SET @msg = N'EXPECT_REJECT FAILED: the named transition ' + @from + N' -> ' + @to
        + N' was refused: ' + ERROR_MESSAGE();
      THROW 50000, @msg, 1;
    END CATCH
    IF (SELECT LifecycleStatus FROM Items WHERE ItemId = @item) <> @to
    BEGIN
      SET @msg = N'EXPECT_REJECT FAILED: the named transition ' + @from + N' -> ' + @to + N' did not take effect';
      THROW 50000, @msg, 1;
    END
    SET @accepted = @accepted + 1;
  END
  ELSE
  BEGIN
    BEGIN TRY
      UPDATE Items SET LifecycleStatus = @to WHERE ItemId = @item;
      SET @msg = N'EXPECT_REJECT FAILED: the unnamed transition ' + @from + N' -> ' + @to + N' was accepted';
      THROW 50000, @msg, 1;
    END TRY
    BEGIN CATCH
      IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
      IF ERROR_MESSAGE() NOT LIKE '%CNT-42%'
      BEGIN
        SET @guardWrong = N'EXPECT_REJECT FAILED: ' + @from + N' -> ' + @to
          + N' was refused by the wrong guard: ' + ERROR_MESSAGE();
        THROW 50000, @guardWrong, 1;
      END
      IF (SELECT LifecycleStatus FROM Items WHERE ItemId = @item) <> @from
      BEGIN
        SET @guardWrong = N'EXPECT_REJECT FAILED: the refused transition ' + @from + N' -> ' + @to
          + N' still moved the row';
        THROW 50000, @guardWrong, 1;
      END
      SET @refused = @refused + 1;
    END CATCH
  END

  FETCH NEXT FROM pair INTO @from, @to, @isAllowed;
END
CLOSE pair; DEALLOCATE pair;

IF @checked <> 30
  THROW 50000, 'EXPECT_REJECT FAILED: the pair matrix did not run over 30 ordered pairs', 1;
IF @accepted <> 10 OR @refused <> 20
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the matrix accepted ' + CONVERT(NVARCHAR(4), @accepted)
      + N' and refused ' + CONVERT(NVARCHAR(4), @refused) + N' of 10 and 20';
    THROW 50000, @msg, 1;
  END
PRINT 'every one of the 30 ordered state pairs is either one of the ten named edges or refused by name';

-- ---------------------------------------------------------------------------------------------
-- 3. a write that does not move the state is not a transition. CNT-46's demo decommission is one
--    bulk UPDATE, and it must be re-runnable on rows that are already retired.
-- ---------------------------------------------------------------------------------------------
DECLARE @terminal INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'retired', 'lifecycle-stay');
SET @terminal = SCOPE_IDENTITY();

-- a no-op state write. Wrapped, because the guard under test here is the state comparison inside the
-- trigger: a version of the trigger without it raises 50060 on this statement, and the verdict must
-- name the write that was wrongly refused rather than quote a raw trigger error.
BEGIN TRY
  UPDATE Items SET LifecycleStatus = 'retired' WHERE ItemId = @terminal;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: re-writing the same state is not a transition and must be accepted: '
    + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
IF (SELECT LifecycleStatus FROM Items WHERE ItemId = @terminal) <> 'retired'
  THROW 50000, 'EXPECT_REJECT FAILED: re-writing the same state is not a transition and must be accepted', 1;
-- an update that does not touch the column at all is untouched by the guard
BEGIN TRY
  UPDATE Items SET PlacementEligible = 1 WHERE ItemId = @terminal;
END TRY
BEGIN CATCH
  SET @msg = N'EXPECT_REJECT FAILED: the lifecycle guard blocked an update to another column: ' + ERROR_MESSAGE();
  THROW 50000, @msg, 1;
END CATCH
IF (SELECT PlacementEligible FROM Items WHERE ItemId = @terminal) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the lifecycle guard blocked an update to another column', 1;
PRINT 'accepted a no-op state write and an update to another column';

-- ---------------------------------------------------------------------------------------------
-- 4. a multi-row update is judged on every row, not on the first. A trigger that inspected one row
--    would let the legal half through and leave the batch half-applied.
-- ---------------------------------------------------------------------------------------------
DECLARE @legal INT, @illegal INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'draft', 'lifecycle-bulk-ok'),
       ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'retired', 'lifecycle-bulk-bad');
SET @legal   = (SELECT ItemId FROM Items WHERE BatchTag = 'lifecycle-bulk-ok');
SET @illegal = (SELECT ItemId FROM Items WHERE BatchTag = 'lifecycle-bulk-bad');

BEGIN TRY
  UPDATE Items SET LifecycleStatus = 'validated' WHERE ItemId IN (@legal, @illegal);
  THROW 50000, 'EXPECT_REJECT FAILED: a batch update carrying one illegal row was accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CNT-42%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the mixed batch was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH

IF (SELECT LifecycleStatus FROM Items WHERE ItemId = @legal) <> 'draft'
  THROW 50000, 'EXPECT_REJECT FAILED: a refused batch update part-applied - the legal row moved', 1;
PRINT 'refused a batch update whose second row is an unnamed transition, and part-applied nothing';

-- ---------------------------------------------------------------------------------------------
-- 5. no hard delete. A draft item is deletable - that is the whole point of "has left draft" - and
--    every other state is not.
--
--    A draft item is authored with a version here, because that is the shape authoring actually
--    produces (#188 writes item and version in one save) and it is the shape that makes the boundary
--    interesting: the version's own foreign key would refuse to leave the item orphaned, so
--    "a draft item is deletable" is only true if the delete takes its version with it.
-- ---------------------------------------------------------------------------------------------
DECLARE @draftItem INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'draft', 'lifecycle-delete-draft');
SET @draftItem = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@draftItem, 1, N'Draft stem.', N'a', N'b', N'c', N'd', 'A');
DECLARE @draftVersion INT = SCOPE_IDENTITY();

DELETE FROM Items WHERE ItemId = @draftItem;
IF EXISTS (SELECT 1 FROM Items WHERE ItemId = @draftItem)
  THROW 50000, 'EXPECT_REJECT FAILED: a draft item could not be deleted, so the guard refuses every delete', 1;
IF EXISTS (SELECT 1 FROM ItemVersions WHERE ItemVersionId = @draftVersion)
  THROW 50000, 'EXPECT_REJECT FAILED: a deleted draft item left its version behind', 1;
PRINT 'accepted the delete of a draft item, and took its version with it';

DECLARE @state VARCHAR(20), @doomed INT;
DECLARE doomed CURSOR LOCAL FAST_FORWARD FOR
  SELECT State FROM @states WHERE State <> 'draft';
OPEN doomed;
FETCH NEXT FROM doomed INTO @state;
WHILE @@FETCH_STATUS = 0
BEGIN
  INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
  VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', @state, 'lifecycle-delete-' + @state);
  SET @doomed = SCOPE_IDENTITY();
  -- a version, so a delete that somehow got past the state clause would hit the version foreign key
  -- too: this test must fail on the guard it names, not on a child row it forgot to consider
  INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
  VALUES (@doomed, 1, N'Doomed stem.', N'a', N'b', N'c', N'd', 'A');
  DECLARE @doomedVersion INT = SCOPE_IDENTITY();

  BEGIN TRY
    DELETE FROM Items WHERE ItemId = @doomed;
    SET @msg = N'EXPECT_REJECT FAILED: the guard accepted the delete of an item in state ' + @state;
    THROW 50000, @msg, 1;
  END TRY
  BEGIN CATCH
    IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
    -- the STATE clause, by its own words: "left draft". A catch that accepted any CNT-43 would pass
    -- while the learner-reference clause was the one firing, or while a blanket refusal fired.
    IF ERROR_MESSAGE() NOT LIKE '%left draft cannot be deleted%'
    BEGIN
      SET @guardWrong = N'EXPECT_REJECT FAILED: the delete of a ' + @state
        + N' item was refused by the wrong guard: ' + ERROR_MESSAGE();
      THROW 50000, @guardWrong, 1;
    END
  END CATCH

  IF EXISTS (SELECT 1 FROM Items WHERE ItemId = @doomed)
    PRINT 'refused the delete of a ' + @state + ' item';
  ELSE
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the refused delete of a ' + @state + N' item removed the row anyway';
    THROW 50000, @msg, 1;
  END

  -- and the version the refused delete would have taken is still there
  IF NOT EXISTS (SELECT 1 FROM ItemVersions WHERE ItemVersionId = @doomedVersion)
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the refused delete of a ' + @state + N' item cascaded anyway';
    THROW 50000, @msg, 1;
  END

  FETCH NEXT FROM doomed INTO @state;
END
CLOSE doomed; DEALLOCATE doomed;
PRINT 'refused the delete of an item in each of the five states that have left draft';

-- ---------------------------------------------------------------------------------------------
-- 6. and a learner reference holds even a draft item. The attempt and practice answer pin a version,
--    so the version has to exist; that is what makes this reachable at all.
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash, FullName) VALUES (@role, 'lifecycle@x.com', 0x01, N'Lifecycle Learner');
DECLARE @user INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('LCY', N'Lifecycle', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'READ', N'Reading', 'reading', 1);
DECLARE @skill INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1);
DECLARE @sec INT = SCOPE_IDENTITY();
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES (@cert, N'Lifecycle paper', 'published', 60, 'mock');
DECLARE @exam INT = SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
DECLARE @grp INT = SCOPE_IDENTITY();
INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
VALUES (@grp, @exam, @sec, @skill, N'Lifecycle legacy stem.', 1, 'mcq', 1);
DECLARE @q INT = SCOPE_IDENTITY();
INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES (@q, 'mcq', 'A', N'a', 1);
DECLARE @opt INT = SCOPE_IDENTITY();

-- a draft item a learner has answered
DECLARE @answered INT, @answeredVersion INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'draft', 'lifecycle-delete-answered');
SET @answered = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@answered, 1, N'Answered draft stem.', N'a', N'b', N'c', N'd', 'A');
SET @answeredVersion = SCOPE_IDENTITY();

INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @att INT = SCOPE_IDENTITY();
INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, ItemId, ItemVersionId, SelectedLabel, IsCorrect)
VALUES (@att, @q, @exam, @answered, @answeredVersion, 'A', 1);

BEGIN TRY
  DELETE FROM Items WHERE ItemId = @answered;
  THROW 50000, 'EXPECT_REJECT FAILED: a draft item an attempt answered was deleted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  -- the LEARNER clause, by its own words. This is the assertion that a state-only trigger fails:
  -- the item below is draft, so the state clause has nothing to say about it.
  IF ERROR_MESSAGE() NOT LIKE '%learner has answered cannot be deleted%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the answered draft item was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH

IF NOT EXISTS (SELECT 1 FROM Items WHERE ItemId = @answered)
  THROW 50000, 'EXPECT_REJECT FAILED: the refused delete of an answered draft item removed the row anyway', 1;
PRINT 'refused the delete of a draft item an attempt answered';

-- a draft item a practice answer points at
DECLARE @practised INT, @practisedVersion INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'draft', 'lifecycle-delete-practised');
SET @practised = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@practised, 1, N'Practised draft stem.', N'a', N'b', N'c', N'd', 'A');
SET @practisedVersion = SCOPE_IDENTITY();

INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status)
VALUES (@user, @cert, @topic, 'B1', 'reading', 1, 'in_progress');
DECLARE @psess INT = SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, DisplayOrder)
VALUES (@psess, @practised, @practisedVersion, 'A', 1, 1);

BEGIN TRY
  DELETE FROM Items WHERE ItemId = @practised;
  THROW 50000, 'EXPECT_REJECT FAILED: a draft item a practice answer points at was deleted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%learner has answered cannot be deleted%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the practised draft item was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH

IF NOT EXISTS (SELECT 1 FROM Items WHERE ItemId = @practised)
  THROW 50000, 'EXPECT_REJECT FAILED: the refused delete of a practised draft item removed the row anyway', 1;
PRINT 'refused the delete of a draft item a practice answer points at';

-- the delete guard must not be a blanket refusal: a draft item nothing points at still goes
DECLARE @unused INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'draft', 'lifecycle-delete-unused');
SET @unused = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@unused, 1, N'Unused draft stem.', N'a', N'b', N'c', N'd', 'A');
DELETE FROM Items WHERE ItemId = @unused;
IF EXISTS (SELECT 1 FROM Items WHERE ItemId = @unused)
  THROW 50000, 'EXPECT_REJECT FAILED: a draft item nothing references could not be deleted', 1;
PRINT 'still accepted the delete of a draft item after refusing two referenced ones';

-- a draft item a PUBLISHED PAPER bound is also undeletable, but by the binding's own foreign key
-- rather than by this trigger: the cascade above deletes the version, and ExamItems points at it. The
-- refusal is not CNT-43 and does not claim to be - it is the paper's protection, which is why it is
-- asserted here rather than described as part of the no-delete rule.
DECLARE @bound INT, @boundVersion INT;
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'draft', 'lifecycle-delete-bound');
SET @bound = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@bound, 1, N'Bound draft stem.', N'a', N'b', N'c', N'd', 'A');
SET @boundVersion = SCOPE_IDENTITY();
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
VALUES (@exam, @sec, @cert, @boundVersion, @bound, 'reading', 1);

BEGIN TRY
  DELETE FROM Items WHERE ItemId = @bound;
  THROW 50000, 'EXPECT_REJECT FAILED: a draft item a published paper bound was deleted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_ExamItems_VersionItem%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the bound draft item was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
END CATCH

IF NOT EXISTS (SELECT 1 FROM Items WHERE ItemId = @bound)
  THROW 50000, 'EXPECT_REJECT FAILED: the refused delete of a bound draft item removed the row anyway', 1;
IF NOT EXISTS (SELECT 1 FROM ItemVersions WHERE ItemVersionId = @boundVersion)
  THROW 50000, 'EXPECT_REJECT FAILED: the refused delete of a bound draft item cascaded its version away', 1;
PRINT 'refused the delete of a draft item a paper bound, by the binding foreign key';

-- ---------------------------------------------------------------------------------------------
-- 7. no state is a dead end. Two ways a row can be stranded, and the edge list has been wrong about
--    both at some point:
--
--      * a non-draft state with no move out of it, which cannot be deleted either, so the row can
--        never be disposed of;
--      * `draft` with no move out of it, once a paper has bound it or a learner has answered it and
--        CNT-43 makes it undeletable.
--
--    So every state except the two terminal retirement states must have somewhere to go. Retired and
--    retired_defective are the disposal states on purpose and are asserted to be terminal by the
--    pair matrix above.
--
--    Written as a scalar count rather than an `INSERT ... SELECT` into a table variable: the harness
--    injects a rows-matched assertion after every line-leading `INSERT ... SELECT`, and this check
--    is *supposed* to match zero rows. The guard is right to complain about a fixture that matches
--    nothing; it would be wrong to silence it here.
-- ---------------------------------------------------------------------------------------------
DECLARE @strandedCount INT = (
  SELECT COUNT(*)
  FROM @states s
  WHERE s.State NOT IN ('retired', 'retired_defective')
    AND NOT EXISTS (SELECT 1 FROM @allowed a WHERE a.FromState = s.State));

IF @strandedCount <> 0
BEGIN
  DECLARE @strandedNames NVARCHAR(200) = (
    SELECT STRING_AGG(s.State, ', ')
    FROM @states s
    WHERE s.State NOT IN ('retired', 'retired_defective')
      AND NOT EXISTS (SELECT 1 FROM @allowed a WHERE a.FromState = s.State));
  SET @msg = N'EXPECT_REJECT FAILED: a state has no legal move out of it, and a row that reaches it can never be deleted or retired: '
    + @strandedNames;
  THROW 50000, @msg, 1;
END
IF EXISTS (SELECT 1 FROM @allowed WHERE FromState IN ('retired','retired_defective'))
  THROW 50000, 'EXPECT_REJECT FAILED: a terminal retirement state has a move out of it', 1;
PRINT 'every state is either a disposal state or has a legal move out of it';

-- ---------------------------------------------------------------------------------------------
-- 8. the count: every refusal above left its row where it was
-- ---------------------------------------------------------------------------------------------
-- 30 pair items + 1 no-op + 2 batch + 5 non-draft delete targets + 2 referenced drafts + 1 bound
-- draft. The three draft items section 5 and 6 delete are gone by design and are not counted.
IF (SELECT COUNT(*) FROM Items WHERE BatchTag LIKE 'lifecycle-%') <> 41
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the lifecycle fixture holds '
      + CONVERT(NVARCHAR(10), (SELECT COUNT(*) FROM Items WHERE BatchTag LIKE 'lifecycle-%'))
      + N' items, not 41; a refusal created or removed a row';
    THROW 50000, @msg, 1;
  END
IF (SELECT COUNT(*) FROM Items WHERE BatchTag LIKE 'lifecycle-delete-draft%'
       OR BatchTag LIKE 'lifecycle-delete-unused%') <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: a draft item the test deleted is still present', 1;
IF (SELECT COUNT(*) FROM ItemVersions WHERE ItemId IN (@answered, @practised)) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a refusal above changed the versions it protected', 1;
PRINT 'the six states, the ten named edges, and both halves of the no-delete rule; no refusal left a row behind';
