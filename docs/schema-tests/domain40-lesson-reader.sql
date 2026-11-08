SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #240: the leveled lesson reader. Five guards, and the wrong input each one has to refuse.
--
-- Every fixture below is valid in every other respect: a fixture that also broke a CHECK or a foreign
-- key would be refused before the guard under test ran, and the test would go green with that guard
-- deleted.
--
--   1. A lesson is addressed by (skill, topic, level) and CARRIES its level. The input that would pass
--      a weaker guard: a lesson whose level is inferred from the topic's stocked items rather than
--      stored - it reads right until the cell's stock moves, and then the lesson's level silently
--      changes with it.
--   2. The topic's modality is the skill's modality. The wrong input: a READING lesson on L-GIST,
--      which every screen renders as a reading lesson over listening content.
--   3. A card belongs to one module, and the two modules are separate card ROWS. The wrong input that
--      passes a flag-only design: one set of cards plus `IsRemedial`, so the remedial visit re-reads
--      the identical instruction with a different title.
--   4. Progress is per learner per lesson per module. The wrong input: one progress row per lesson,
--      which makes a finished primary module mark the unread remedial one complete.
--   5. A micro-check answer is a PracticeAnswers row, so it reaches mastery and weakness analysis. The
--      wrong input that satisfies the ticket on paper: a private counter in LearnerLessonMicroChecks
--      that no view and no cell read ever sees.

DECLARE @msg NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. a lesson carries its own level, topic and modality
-- ---------------------------------------------------------------------------------------------
DECLARE @required TABLE (ColumnName SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @required (ColumnName) VALUES ('Modality'), ('TopicId'), ('CefrLevel');
IF EXISTS (
  SELECT 1 FROM @required r
  WHERE NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS c
    WHERE c.TABLE_NAME = 'Lessons' AND c.COLUMN_NAME = r.ColumnName AND c.IS_NULLABLE = 'NO'))
  THROW 50000, 'EXPECT_REJECT FAILED: a lesson is still not addressed by (skill, topic, level): Modality, TopicId or CefrLevel is missing or nullable', 1;

-- The level is a level, not a free string: a certificate band code ('below_A1') must not become a
-- lesson's level, which is what a column with no CHECK would allow. The rejection itself is below,
-- after the fixtures exist; the column shape is asserted here.

-- ---------------------------------------------------------------------------------------------
-- fixtures: one reading skill and one listening skill, so the cross-modality case has a real target
-- ---------------------------------------------------------------------------------------------
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('C240', N'Lesson reader cert', 1);
DECLARE @cert INT = SCOPE_IDENTITY();

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
  (@cert, 'READ', N'Reading', 'reading', 1),
  (@cert, 'LIST', N'Listening', 'listening', 2);
DECLARE @sRead INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');
DECLARE @sList INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');

INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local detail');
DECLARE @tRead INT = SCOPE_IDENTITY();
INSERT INTO Topics (Modality, Code, Name) VALUES ('listening', 'L-GIST', N'Global gist');
DECLARE @tList INT = SCOPE_IDENTITY();

-- the correct shape
INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
VALUES (@cert, @sRead, 'reading', @tRead, 'B1', N'Detail at B1', 8, 1);
DECLARE @les INT = SCOPE_IDENTITY();
PRINT 'accepted a lesson addressed by (skill, topic, level)';

-- reject (CK_Lessons_Level): a certificate band code is not a CEFR level
BEGIN TRY
  INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
  VALUES (@cert, @sRead, 'reading', @tRead, 'below_A1', N'Band as level', 8, 2);
  THROW 50000, 'EXPECT_REJECT FAILED: a certificate band code was accepted as a lesson level', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Lessons_Level%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the bad level was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected a band code used as a level, by CK_Lessons_Level';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 2. a lesson cannot be a reading lesson on a listening topic
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
  VALUES (@cert, @sRead, 'reading', @tList, 'B1', N'Reading lesson on a listening topic', 8, 3);
  THROW 50000, 'EXPECT_REJECT FAILED: a reading lesson was accepted on a listening topic', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_Lessons_TopicModality%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the cross-modality lesson was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected a reading lesson on a listening topic, by FK_Lessons_TopicModality';
END CATCH

-- and the modality cannot disagree with the skill's own either
BEGIN TRY
  INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
  VALUES (@cert, @sRead, 'listening', @tList, 'B1', N'Reading skill, listening modality', 8, 4);
  THROW 50000, 'EXPECT_REJECT FAILED: a lesson whose modality disagreed with its skill was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_Lessons_SkillModality%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the wrong-modality lesson was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected a lesson whose modality disagreed with its skill, by FK_Lessons_SkillModality';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 3. the two modules are separate card rows
-- ---------------------------------------------------------------------------------------------
INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
VALUES (@les, @cert, 'rule', N'Primary rule', N'Subject + verb', 0);
DECLARE @primaryCard INT = SCOPE_IDENTITY();

INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder, Module)
VALUES (@les, @cert, 'rule', N'Remedial: why the distractor tempts', N'Distractor anatomy', 10, 'remedial');
DECLARE @remedialCard INT = SCOPE_IDENTITY();

-- Criterion 2 says the two modules are SEPARATE, not one module with a different title. Two cards,
-- different modules, different content: reading the row proves the separation, not a flag.
IF (SELECT Module FROM LessonCards WHERE CardId = @primaryCard) <> 'primary'
  THROW 50000, 'EXPECT_REJECT FAILED: a lesson card with no declared module did not default to primary', 1;
IF (SELECT Module FROM LessonCards WHERE CardId = @remedialCard) <> 'remedial'
  THROW 50000, 'EXPECT_REJECT FAILED: a remedial card did not record its module', 1;
IF (SELECT ContentMarkdown FROM LessonCards WHERE CardId = @primaryCard)
   = (SELECT ContentMarkdown FROM LessonCards WHERE CardId = @remedialCard)
  THROW 50000, 'EXPECT_REJECT FAILED: the primary and remedial modules hold identical instruction', 1;
PRINT 'the primary and remedial modules are separate card rows with their own content';

BEGIN TRY
  INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder, Module)
  VALUES (@les, @cert, 'rule', N'Third module', N'Body', 20, 'extra');
  THROW 50000, 'EXPECT_REJECT FAILED: a module outside primary/remedial was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_LessonCards_Module%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the unknown module was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected an unknown module, by CK_LessonCards_Module';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 4. progress is per learner per lesson PER MODULE
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = (SELECT RoleId FROM Roles WHERE Code = 'learner');
INSERT INTO Users (RoleId, Email, PasswordHash, FullName) VALUES (@role, 'reader@c240.local', 0x01, N'Reader');
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO LearnerLessonProgress (UserId, LessonId, CertId, LastViewedCardId, CardsCompleted, TotalCards, IsCompleted, Module)
VALUES (@user, @les, @cert, @primaryCard, 1, 1, 1, 'primary');
INSERT INTO LearnerLessonProgress (UserId, LessonId, CertId, LastViewedCardId, CardsCompleted, TotalCards, IsCompleted, Module)
VALUES (@user, @les, @cert, @remedialCard, 0, 1, 0, 'remedial');
PRINT 'accepted separate primary and remedial progress for one learner and one lesson';

-- The wrong input this refuses: one progress row per lesson, which would carry the primary module's
-- IsCompleted across to a remedial module nobody has read.
BEGIN TRY
  INSERT INTO LearnerLessonProgress (UserId, LessonId, CertId, CardsCompleted, TotalCards, Module)
  VALUES (@user, @les, @cert, 0, 1, 'primary');
  THROW 50000, 'EXPECT_REJECT FAILED: a second progress row for the same module was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UQ_LLP_UserLessonModule%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the duplicate progress was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected a duplicate progress row for one module, by UQ_LLP_UserLessonModule';
END CATCH

-- and the mid-read position is the card, so resume is a card index and not a restart
IF (SELECT LastViewedCardId FROM LearnerLessonProgress WHERE UserId = @user AND LessonId = @les AND Module = 'remedial') IS NULL
  THROW 50000, 'EXPECT_REJECT FAILED: a partially read module has no last-viewed card, so it cannot resume', 1;
PRINT 'a partially read module records the exact card it stopped at';

-- ---------------------------------------------------------------------------------------------
-- 5. a micro-check answer is a PracticeAnswers row, and its shape is its own
-- ---------------------------------------------------------------------------------------------
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, LessonId, QuestionCount, Status)
VALUES (@user, @cert, @tRead, 'B1', 'reading', @les, 1, 'in_progress');
DECLARE @sess INT = SCOPE_IDENTITY();
PRINT 'accepted a lesson sitting that names its lesson, its cell and its certificate';

-- The card-only shape is what makes a micro-check reach mastery: the answer is in the ledger a drill
-- answer is in, not in a counter beside it.
INSERT INTO PracticeAnswers (SessionId, CardId, SelectedLabel, IsCorrect, DisplayOrder)
VALUES (@sess, @remedialCard, 'A', 1, 1);
PRINT 'accepted a micro-check answer as a PracticeAnswers row';

-- The wrong input this refuses: an answer that is a card AND a pool item, which would be counted twice
-- by anything that unions the shapes.
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status)
VALUES (@user, @cert, @tRead, 'B1', 'reading', 1, 'in_progress');
DECLARE @sess2 INT = SCOPE_IDENTITY();
INSERT INTO Stimuli (CefrLevel, Passage) VALUES ('B1', N'Passage');
INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus)
VALUES (SCOPE_IDENTITY(), 'reading', @tRead, 'B1', 'passage_comprehension', 'demo', 'active');
DECLARE @item INT = SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
VALUES (@item, 1, N'Stem', N'A', N'B', N'C', N'D', 'A');
DECLARE @ver INT = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO PracticeAnswers (SessionId, CardId, ItemId, ItemVersionId, SelectedLabel, DisplayOrder)
  VALUES (@sess2, @primaryCard, @item, @ver, 'A', 1);
  THROW 50000, 'EXPECT_REJECT FAILED: an answer that was both a card and a pool item was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_PracticeAnswers_Shape%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the two-shape card answer was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected an answer carrying both a card and an item, by CK_PracticeAnswers_Shape';
END CATCH

-- one card, one grade, in this sitting
BEGIN TRY
  INSERT INTO PracticeAnswers (SessionId, CardId, SelectedLabel, DisplayOrder)
  VALUES (@sess, @remedialCard, 'B', 2);
  THROW 50000, 'EXPECT_REJECT FAILED: one micro-check was graded twice in one sitting', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UQ_PracticeAnswers_Card%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the double grade was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected a second grade of one micro-check in one sitting, by UQ_PracticeAnswers_Card';
END CATCH

-- one lesson, one open sitting: two would leave "where did I stop" with two answers
BEGIN TRY
  INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, LessonId, QuestionCount, Status)
  VALUES (@user, @cert, @tRead, 'B1', 'reading', @les, 1, 'in_progress');
  THROW 50000, 'EXPECT_REJECT FAILED: a second lesson sitting for one learner and one lesson was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UX_Practice_OneLessonSitting%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the duplicate sitting was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected a second lesson sitting for one learner and one lesson, by UX_Practice_OneLessonSitting';
END CATCH

-- the certificate isolation of the sitting's lesson reference
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('C240B', N'Other cert', 1);
DECLARE @cert2 INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert2, 'READ', N'Reading', 'reading', 1);
DECLARE @sRead2 INT = SCOPE_IDENTITY();
INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
VALUES (@cert2, @sRead2, 'reading', @tRead, 'A2', N'Other cert lesson', 8, 1);
DECLARE @les2 INT = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, LessonId, QuestionCount, Status)
  VALUES (@user, @cert, @tRead, 'B1', 'reading', @les2, 1, 'in_progress');
  THROW 50000, 'EXPECT_REJECT FAILED: a sitting named another certificate''s lesson', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_Practice_LessonCert%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the cross-cert lesson sitting was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected a lesson sitting naming another certificate''s lesson, by FK_Practice_LessonCert';
END CATCH

PRINT 'domain40: every lesson-reader guard refused what it exists to refuse';
