SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- docs/schema-tests/domain48-topic-accuracy.sql
-- #243 / D3: accuracy per (topic, level), computed by the view rather than by application code.
--
-- WHAT THIS FILE PROVES, and the wrong input that would pass a weaker version:
--
--   1. A cell is (TopicId, CefrLevel), not a topic. The wrong input is one topic at two levels, which
--      a view grouping by topic alone reports as a single blended row.
--   2. Both answer shapes count, and neither is counted twice. AttemptAnswers.QuestionId is NOT NULL,
--      so a pool answer carries one; a branch that also reads a cell through Questions - or a view
--      that joins the two shapes without saying which it owns - doubles the answer count. The fixture
--      below is exactly that shape: a pool answer whose QuestionId also resolves to a legacy question.
--   3. An unanswered practice answer contributes nothing. PracticeService writes one answer row per
--      item when a drill starts, with every answer column null; counting those rows inflates every
--      denominator by the size of the drill.
--   4. A topic with no items produces NO ROW. The wrong input is a zero row, which reads as 0% - a
--      claim about the learner that no answer supports.
--   5. An ungraded attempt contributes nothing, so an in-progress sitting cannot move the figure.
--   6. AccuracyPercent is the database's arithmetic, rounded. The wrong input is a reader dividing in
--      C# and truncating 2/3 to 66.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. fixtures: one certificate, two reading topics, items at two levels
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TA243', N'Topic accuracy', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert, 'READ', N'Reading', 'reading', 1);
DECLARE @skill INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
VALUES (@skill, @cert, 'reading', 'S1', N'Incomplete Sentences', 4, 2, 1);
DECLARE @sec INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
VALUES (@role, 'topic243@x.com', 0x01, N'Topic Learner', @cert);
DECLARE @user INT = SCOPE_IDENTITY();

-- R-DET is answered; R-INF holds no items at all and must never appear (criterion 3).
INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local detail');
DECLARE @tDet INT = SCOPE_IDENTITY();
INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-INF', N'Bridging inference');
DECLARE @tInf INT = SCOPE_IDENTITY();

-- Four items at R-DET: two at B1, two at B2.
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag) VALUES
  ('reading', @tDet, 'B1', 'sentence_cloze', 'demo', 'active', 'ta-b1a'),
  ('reading', @tDet, 'B1', 'sentence_cloze', 'demo', 'active', 'ta-b1b'),
  ('reading', @tDet, 'B2', 'sentence_cloze', 'demo', 'active', 'ta-b2a'),
  ('reading', @tDet, 'B2', 'sentence_cloze', 'demo', 'active', 'ta-b2b');
DECLARE @iB1a INT = (SELECT ItemId FROM Items WHERE BatchTag = 'ta-b1a');
DECLARE @iB1b INT = (SELECT ItemId FROM Items WHERE BatchTag = 'ta-b1b');
DECLARE @iB2a INT = (SELECT ItemId FROM Items WHERE BatchTag = 'ta-b2a');
DECLARE @iB2b INT = (SELECT ItemId FROM Items WHERE BatchTag = 'ta-b2b');

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel) VALUES
  (@iB1a, 1, N'b1a', N'a', N'b', N'c', N'd', 'A'),
  (@iB1b, 1, N'b1b', N'a', N'b', N'c', N'd', 'A'),
  (@iB2a, 1, N'b2a', N'a', N'b', N'c', N'd', 'A'),
  (@iB2b, 1, N'b2b', N'a', N'b', N'c', N'd', 'A');
DECLARE @vB1a INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @iB1a);
DECLARE @vB1b INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @iB1b);
DECLARE @vB2a INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @iB2a);
DECLARE @vB2b INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @iB2b);

-- ---------------------------------------------------------------------------------------------
-- 2. a topic with no items produces no row (criterion 3), before any answer exists
-- ---------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM vw_UserTopicAccuracy WHERE UserId = @user)
  THROW 50000, 'EXPECT_REJECT FAILED: the view reports a cell for a learner who has answered nothing', 1;
PRINT 'a learner who has answered nothing has no cell';

-- ---------------------------------------------------------------------------------------------
-- 3. a pool practice drill: two B1 items, one answered correct, one NOT answered.
--    The unanswered row is what PracticeService writes at start, and it must not move the figure.
-- ---------------------------------------------------------------------------------------------
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
VALUES (@user, @cert, @tDet, 'B1', 'reading', 2, 'in_progress', 'all');
DECLARE @s1 INT = SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, DisplayOrder) VALUES
  (@s1, @iB1a, @vB1a, 1),
  (@s1, @iB1b, @vB1b, 2);

IF EXISTS (SELECT 1 FROM vw_UserTopicAccuracy WHERE UserId = @user)
  THROW 50000, 'EXPECT_REJECT FAILED: an unanswered drill counted as an observation', 1;
PRINT 'starting a drill adds no observation';

UPDATE PracticeAnswers SET SelectedLabel = 'A', IsCorrect = 1 WHERE SessionId = @s1 AND ItemId = @iB1a;

IF (SELECT Answered FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B1') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: one graded answer did not move B1 to 1', 1;
IF (SELECT Correct FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B1') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: one correct answer did not move B1 Correct to 1', 1;
IF (SELECT AccuracyPercent FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B1') <> 100
  THROW 50000, 'EXPECT_REJECT FAILED: the view did not compute 1/1 as 100', 1;
PRINT 'one answered item is one observation, and the unanswered sibling is not';

-- ---------------------------------------------------------------------------------------------
-- 4. the cell is (topic, level): two more B2 answers, one correct, are a DIFFERENT row.
--    A view grouped by topic alone would report B1+B2 as one blended 2/3.
-- ---------------------------------------------------------------------------------------------
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
VALUES (@user, @cert, @tDet, 'B2', 'reading', 2, 'in_progress', 'all');
DECLARE @s2 INT = SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, DisplayOrder) VALUES
  (@s2, @iB2a, @vB2a, 'A', 1, 1),
  (@s2, @iB2b, @vB2b, 'B', 0, 2);

IF (SELECT Answered FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B2') <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: B2 should hold 2 answers', 1;
IF (SELECT Correct FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B2') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: B2 should hold 1 correct answer', 1;
-- 1 of 2 is 50, and it is computed by the view rather than left to a reader.
IF (SELECT AccuracyPercent FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B2') <> 50
  THROW 50000, 'EXPECT_REJECT FAILED: the view did not compute 1/2 as 50', 1;
IF (SELECT COUNT(*) FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: one topic at two levels must be two rows, not one blended row', 1;
PRINT 'a topic at two levels is two cells, each with its own accuracy';

-- ---------------------------------------------------------------------------------------------
-- 5. TWO ATTRIBUTION TRAPS.
--
--    #269: the double count #250 fixed can no longer be expressed. Questions is gone, so an attempt
--    answer has exactly ONE route to a cell - its roster position's item - and there is no second branch
--    to disagree with. The figure below still asserts the answer is counted ONCE, which is the property
--    that mattered; what changed is that a second route no longer exists to be wrong.
--
--    What CAN still go wrong is attribution in two directions, and both are asserted below:
--
--      5a. A session's cell is not the item's cell. Item selection may substitute a sibling topic, so
--          a drill opened at R-DET B1 can serve an R-INF B2 item. Crediting the SESSION's cell would
--          report accuracy for a topic nobody practised - the rule #237 settled for mastery.
--      5b. An ungraded attempt contributes nothing, so an in-progress sitting cannot move the figure.
-- ---------------------------------------------------------------------------------------------
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
VALUES (@cert, N'Topic paper', 'published', 60, 'mock');
DECLARE @exam INT = SCOPE_IDENTITY();
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder) VALUES
  (@exam, @sec, @cert, @vB1b, @iB1b, 'reading', 1);
DECLARE @pos INT = SCOPE_IDENTITY();

INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @exam, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @attempt INT = SCOPE_IDENTITY();
-- #269: the answer names the roster position and the version it pinned. The legacy half of this fixture
-- (the same answer also naming a Questions row) is gone with that table, so "counted twice" is now
-- proved by the figure itself rather than by giving the view two routes to walk.
INSERT INTO AttemptAnswers (AttemptId, ExamId, ItemId, ItemVersionId, ExamItemId, SelectedLabel, IsCorrect)
VALUES (@attempt, @exam, @iB1b, @vB1b, @pos, 'A', 0);

-- 5b: an in-progress attempt contributes nothing.
IF (SELECT Answered FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B1') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: an in-progress attempt moved the accuracy denominator', 1;
PRINT 'an in-progress attempt contributes no observation';

UPDATE ExamAttempts
SET Status = 'graded'
WHERE AttemptId = @attempt;

-- B1 now holds 2 answers: the practice one and the attempt one. NOT 3, which is what a second branch
-- over the same answer would produce.
IF (SELECT Answered FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B1') <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the same attempt answer was counted more than once', 1;
IF (SELECT Correct FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B1') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: B1 should hold 1 correct after the wrong attempt answer', 1;
PRINT 'a graded pool attempt answer is counted exactly once';

-- 5a: a drill whose session cell is NOT its item's cell. The session says B1/R-DET; the item answered
-- is R-INF at A2. A view reading PracticeSessions.TopicId/CefrLevel would put this answer in B1 and
-- leave R-INF absent - reported accuracy for a topic the learner never practised, and a silent gap in
-- the one they did.
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
VALUES ('reading', @tInf, 'A2', 'sentence_cloze', 'demo', 'active', 'ta-sub');
DECLARE @iSub INT = (SELECT ItemId FROM Items WHERE BatchTag = 'ta-sub');
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@iSub, 1, N'sub', N'a', N'b', N'c', N'd', 'A');
DECLARE @vSub INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @iSub);

INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
VALUES (@user, @cert, @tDet, 'B1', 'reading', 1, 'in_progress', 'all');
DECLARE @s4 INT = SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, DisplayOrder)
VALUES (@s4, @iSub, @vSub, 'A', 0, 1);

IF (SELECT Answered FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tInf AND CefrLevel = 'A2') IS NULL
  THROW 50000, 'EXPECT_REJECT FAILED: a substituted sibling item was not credited to its own cell', 1;
IF (SELECT Correct FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tInf AND CefrLevel = 'A2') <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: the substituted item''s verdict was not carried', 1;
IF (SELECT Answered FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B1') <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: a substituted sibling item was credited to the session''s cell', 1;
PRINT 'the cell is the item''s, not the session''s';

-- ---------------------------------------------------------------------------------------------
-- 6. A TOPIC WITH NO ITEMS IS NEVER REPORTED (criterion 3), in both directions:
--    a learner who has answered nothing (asserted at step 2), and a topic with no items at all
--    while other topics do hold answers. The wrong input is a zero row per topic - 0% is a claim
--    about the learner that no answer supports. R-ATT is the canonical topic nothing has been
--    authored in.
-- ---------------------------------------------------------------------------------------------
INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-ATT', N'Authorial stance');
DECLARE @tAtt INT = SCOPE_IDENTITY();

IF EXISTS (SELECT 1 FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tAtt)
  THROW 50000, 'EXPECT_REJECT FAILED: a topic with no items was reported as 0%', 1;
PRINT 'a topic with no items is absent, not 0%';

-- ---------------------------------------------------------------------------------------------
-- 7. the rounding, on a cell that does not divide evenly: 2 of 3 is 67, not a truncation to 66
-- ---------------------------------------------------------------------------------------------
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, QuestionSource)
VALUES (@user, @cert, @tDet, 'B1', 'reading', 1, 'in_progress', 'all');
DECLARE @s3 INT = SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, DisplayOrder)
VALUES (@s3, @iB1b, @vB1b, 'A', 1, 1);

IF (SELECT Answered FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B1') <> 3
  THROW 50000, 'EXPECT_REJECT FAILED: B1 should hold 3 answers', 1;
IF (SELECT AccuracyPercent FROM vw_UserTopicAccuracy WHERE UserId = @user AND TopicId = @tDet AND CefrLevel = 'B1') <> 67
  THROW 50000, 'EXPECT_REJECT FAILED: 2 of 3 must read 67, not a truncation to 66', 1;
PRINT 'the view rounds its own percentage';
