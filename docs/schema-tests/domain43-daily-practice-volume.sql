SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #235 / D12 / PLC-64: the daily practice volume is DERIVED, and this file pins the arithmetic.
--
-- What this file proves, and the input that would pass a weaker version while being wrong:
--
--   1. The view exists and starts empty. A view over sessions would already hold a row here, because
--      the session row is written with all of its answers blank.
--   2. A session written with unanswered answers contributes NOTHING. The input that would pass "the
--      view counts answers" while being wrong is counting `PracticeAnswers` rows rather than answered
--      ones - the drill writes those rows at start.
--   3. One answered item moves AnsweredCount to 1 and DistinctCells to 1, and NOTHING ELSE is written.
--      A stored counter would need a second UPDATE; this asserts the read alone moved.
--   4. Answering a second item in a different cell moves AnsweredCount to 2 and DistinctCells to 2.
--      The input that would pass while being wrong is counting cells from Sessions alone: both
--      sessions exist before either answer, so a session-based count would already say 2 at step 3.
--   5. A second answer in the SAME cell moves the volume but not the cell count.
--   6. A productive answer is counted in ProductiveTaskCount, an objective one is not.
--   7. A row with no AnsweredAt contributes to no day rather than to an invented one.

INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner'); DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('VOL235', N'Volume cert', 1); DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'READ', N'Reading', 'reading', 1);
DECLARE @skill INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
VALUES (@role, 'vol235@x.com', 0x01, N'Volume', @cert); DECLARE @user INT = SCOPE_IDENTITY();
INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Detail'); DECLARE @t1 INT = SCOPE_IDENTITY();
INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-INF', N'Inference'); DECLARE @t2 INT = SCOPE_IDENTITY();

INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag) VALUES
  ('reading', @t1, 'B1', 'sentence_cloze', 'production', 'active', 'vol-a'),
  ('reading', @t1, 'B1', 'sentence_cloze', 'production', 'active', 'vol-b'),
  ('reading', @t2, 'B1', 'sentence_cloze', 'production', 'active', 'vol-c');
DECLARE @ia INT = (SELECT ItemId FROM Items WHERE BatchTag = 'vol-a');
DECLARE @ib INT = (SELECT ItemId FROM Items WHERE BatchTag = 'vol-b');
DECLARE @ic INT = (SELECT ItemId FROM Items WHERE BatchTag = 'vol-c');
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel) VALUES
  (@ia, 1, N'a', N'a', N'b', N'c', N'd', 'A'),
  (@ib, 1, N'b', N'a', N'b', N'c', N'd', 'A'),
  (@ic, 1, N'c', N'a', N'b', N'c', N'd', 'A');
DECLARE @va INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @ia);
DECLARE @vb INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @ib);
DECLARE @vc INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @ic);

DECLARE @today DATE = CAST(SYSUTCDATETIME() AS DATE);

-- 1. Nothing answered: no row at all. Not a zero row.
IF EXISTS (SELECT 1 FROM vw_DailyPracticeVolume WHERE UserId = @user)
  THROW 50000, 'EXPECT_REJECT FAILED: the view reports a day for a learner who has answered nothing', 1;
PRINT 'a learner who has answered nothing has no day';

-- 2. A started-but-unanswered drill contributes nothing. This is exactly the shape PracticeService
--    writes when a session starts: the session and its answer rows exist, every answer column is null.
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
VALUES (@user, @cert, @t1, 'B1', 'reading', 2, 'in_progress', 'all');
DECLARE @s1 INT = SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, DisplayOrder) VALUES
  (@s1, @ia, @va, 1), (@s1, @ib, @vb, 2);

IF EXISTS (SELECT 1 FROM vw_DailyPracticeVolume WHERE UserId = @user)
  THROW 50000, 'EXPECT_REJECT FAILED: an unanswered session row counted as practice', 1;
PRINT 'starting a drill charges the learner nothing';

-- 3. One answer written. Nothing else changes: no counter, no session state.
UPDATE PracticeAnswers SET SelectedLabel = 'A', IsCorrect = 1, AnsweredAt = DATEADD(HOUR, 1, CAST(@today AS DATETIME2))
WHERE SessionId = @s1 AND ItemId = @ia;

IF (SELECT AnsweredCount FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn = @today) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: one answered item did not move the volume to 1', 1;
IF (SELECT DistinctCells FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn = @today) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: one answered item did not move the cell count to 1', 1;
IF (SELECT ProductiveTaskCount FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn = @today) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: a reading answer was counted as a productive task', 1;
PRINT 'one answer moves the volume, and nothing else was written';

-- 4. A second answer in a DIFFERENT cell. Both sessions exist already, so a view over sessions alone
--    would not move here - only the answers do.
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
VALUES (@user, @cert, @t2, 'B1', 'reading', 1, 'in_progress', 'all');
DECLARE @s2 INT = SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, AnsweredAt, DisplayOrder)
VALUES (@s2, @ic, @vc, 'B', 0, DATEADD(HOUR, 2, CAST(@today AS DATETIME2)), 1);

IF (SELECT AnsweredCount FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn = @today) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a second answered item did not move the volume to 2', 1;
IF (SELECT DistinctCells FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn = @today) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a second cell did not move the cell count to 2', 1;
PRINT 'a second cell moves both numbers';

-- 5. A second answer in the SAME cell moves the volume and not the cell count.
UPDATE PracticeAnswers SET SelectedLabel = 'B', IsCorrect = 0, AnsweredAt = DATEADD(HOUR, 3, CAST(@today AS DATETIME2))
WHERE SessionId = @s1 AND ItemId = @ib;

IF (SELECT AnsweredCount FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn = @today) <> 3
  THROW 50000, 'EXPECT_REJECT FAILED: a third answered item did not move the volume to 3', 1;
IF (SELECT DistinctCells FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn = @today) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a second item in the same cell invented a third cell', 1;
PRINT 'a repeated cell moves the volume only';

-- 6. A productive answer counts as a productive task. Writing is a different modality, so its own item.
INSERT INTO Topics (Modality, Code, Name) VALUES ('writing', 'W-ARG', N'Argument'); DECLARE @w INT = SCOPE_IDENTITY();
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag, RenderFormat)
VALUES ('writing', @w, 'B1', 'persuasive_essay', 'production', 'active', 'vol-w', 'free_response');
DECLARE @iw INT = (SELECT ItemId FROM Items WHERE BatchTag = 'vol-w');
-- A productive version is the other legal shape: all four options NULL and no key.
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem)
VALUES (@iw, 1, N'Write an essay.');
DECLARE @vw INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @iw);

INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
VALUES (@user, @cert, @w, 'B1', 'writing', 1, 'in_progress', 'all');
DECLARE @s3 INT = SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, ResponseText, AnsweredAt, DisplayOrder)
VALUES (@s3, @iw, @vw, N'A full essay response.', DATEADD(HOUR, 4, CAST(@today AS DATETIME2)), 1);

IF (SELECT ProductiveTaskCount FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn = @today) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: a writing answer was not counted as a productive task', 1;
IF (SELECT AnsweredCount FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn = @today) <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: a writing answer did not move the volume to 4', 1;
PRINT 'a productive answer is counted as a task';

-- 7. A row with no AnsweredAt contributes to no day. It is a row from before #156, and inventing a day
--    for it would attribute old work to today.
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
VALUES (@user, @cert, @t1, 'B1', 'reading', 1, 'finished', 'all');
DECLARE @s4 INT = SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, DisplayOrder)
VALUES (@s4, @ia, @va, 'C', 1, 1);

IF (SELECT AnsweredCount FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn = @today) <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: an undated answer was attributed to today', 1;
IF EXISTS (SELECT 1 FROM vw_DailyPracticeVolume WHERE UserId = @user AND PractisedOn IS NULL)
  THROW 50000, 'EXPECT_REJECT FAILED: an undated answer became a NULL day', 1;
PRINT 'an undated answer belongs to no day';

PRINT '#235 daily practice volume: derived, moves with answers, moves with nothing else';
