SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #269: the three rules that were LOST when Questions was dropped, and the one that must not be lost
-- with the micro-check arm. Each was found by probing the schema after the drop, and each had a real
-- consequence - not a theoretical one.
--
--   1. an answer's position must belong to the answer's OWN paper.
--      Lost with FK_AttemptAnswers_QuestionExam, which enforced it through Questions(QuestionId, ExamId).
--      Measured without it: an attempt on a TOEIC paper accepted an IELTS position, and
--      vw_UserSectionAccuracyAll then credited one paper's answer to the other paper's section.
--      Restored as UQ_ExamItems_Id_Exam + FK_AttemptAnswers_ExamItemExam.
--
--   2. a free response's skill must be its position's section's skill.
--      Lost with FK_FreeResp_QuestionSkill. Measured without it: a response on a READING position
--      claimed the SPEAKING skill and AiGradings accepted it, so the rubric it was scored against
--      belonged to a different skill. Restored as FreeResponses.SectionId +
--      FK_FreeResp_ExamItemSection + FK_FreeResp_SectionSkill.
--
--   3. a lesson micro-check answer must still be insertable.
--      CK_PracticeAnswers_Shape's card arm requires ItemId and ItemVersionId to BOTH be NULL, so making
--      them NOT NULL would have made the arm unreachable. Measured: the INSERT failed with
--      "Cannot insert the value NULL into column 'ItemId'", and PracticeService.LessonMicroCheck writes
--      exactly that row - so grading a micro-check would have failed at runtime.
--
--   4. and an answer may still not name BOTH shapes.
--
-- Every CATCH names the guard it expects. A catch that proves only "something failed" cannot tell a
-- working constraint from a typo in this file - which is exactly how the three above went unnoticed.

DECLARE @guardWrong NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- fixture: a certificate with a reading and a speaking skill, two papers, a section each
-- ---------------------------------------------------------------------------------------------
INSERT INTO Roles (Code, Name) VALUES ('probeauthor', N'Probe author');
DECLARE @role INT = SCOPE_IDENTITY();
-- ADM-03 judges an account by its permissions: a triage-only account may not write exam structure.
INSERT INTO Permissions (Code) VALUES ('pool.content.author');
INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@role, 'pool.content.author');

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('LOST', N'Rules lost in the drop', 1);
DECLARE @cert INT = SCOPE_IDENTITY();

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
  (@cert, 'READ', N'Reading', 'reading', 1, 30),
  (@cert, 'SPEA', N'Speaking', 'speaking', 2, 30);
DECLARE @readSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');
DECLARE @speaSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'SPEA');

INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
VALUES (@role, 'lost@x.com', 0x01, N'Probe author');
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, LastWrittenBy) VALUES
  (@readSkill, @cert, 'reading',  'R1', N'Reading one',  4, 1, 1, @user),
  (@readSkill, @cert, 'reading',  'R2', N'Reading two',  4, 1, 2, @user),
  (@speaSkill, @cert, 'speaking', 'S1', N'Speaking one', 4, 1, 3, @user);
DECLARE @r1 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'R1');
DECLARE @r2 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'R2');
DECLARE @s1 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'S1');

-- trg_ExamAttempts_PublishedExamOnly: an attempt only exists on a published paper. And
-- trg_Exams_PublishRequiresAudit refuses `published` without a recorded readiness pass newer than the
-- paper's last edit, so the fixture writes the same evidence the publish door does.
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, LastWrittenBy) VALUES
  (@cert, N'Paper one', 'draft', 30, 'mock', @user),
  (@cert, N'Paper two', 'draft', 30, 'mock', @user);
DECLARE @paper1 INT = (SELECT ExamId FROM Exams WHERE CertId = @cert AND Name = N'Paper one');
DECLARE @paper2 INT = (SELECT ExamId FROM Exams WHERE CertId = @cert AND Name = N'Paper two');

UPDATE Exams SET Status = 'published', LastWrittenBy = @user WHERE ExamId IN (@paper1, @paper2);

INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching');
DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-DET');
INSERT INTO Topics (Modality, Code, Name) VALUES ('speaking', 'S-DESC', N'Describing a visual');
DECLARE @spkTopic INT = (SELECT TopicId FROM Topics WHERE Code = 'S-DESC');

INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat, ContentTier, LifecycleStatus, BatchTag, LastWrittenBy)
VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'mcq', 'demo', 'active', 'lost-a', @user);
DECLARE @item INT = (SELECT ItemId FROM Items WHERE BatchTag = 'lost-a');

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
VALUES (@item, 1, N'Probe stem', N'a', N'b', N'c', N'd', 'A', @user);
DECLARE @ver INT = SCOPE_IDENTITY();

-- one position on paper one (in R1) and one on paper two (in R2)
INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, LastWrittenBy) VALUES
  (@paper1, @r1, @cert, @ver, @item, 'reading', 1, @user),
  (@paper2, @r2, @cert, @ver, @item, 'reading', 1, @user);
DECLARE @posOwn INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @paper1);
DECLARE @posForeign INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId = @paper2);

-- A SPEAKING position, so probe 2 can claim the wrong skill against a legitimate row.
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat, ContentTier, LifecycleStatus, BatchTag, LastWrittenBy)
VALUES ('speaking', @spkTopic, 'B1', 'descriptive_monologue', 'free_response', 'demo', 'active', 'lost-spk', @user);
DECLARE @spkItem INT = (SELECT ItemId FROM Items WHERE BatchTag = 'lost-spk');

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, MinWords, MaxWords, ResponseSeconds, LastWrittenBy)
VALUES (@spkItem, 1, N'Speak about the chart.', 10, 100, 60, @user);
DECLARE @spkVer INT = SCOPE_IDENTITY();

INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, LastWrittenBy)
VALUES (@paper1, @s1, @cert, @spkVer, @spkItem, 'speaking', 2, @user);
DECLARE @posSpeakingSection INT = SCOPE_IDENTITY();

INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @paper1, @cert, 'in_progress', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @attempt INT = SCOPE_IDENTITY();

-- ---------------------------------------------------------------------------------------------
-- 1. an answer must not name another paper's position (FK_AttemptAnswers_ExamItemExam)
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  INSERT INTO AttemptAnswers (AttemptId, ExamId, ItemId, ItemVersionId, ExamItemId, SelectedLabel)
  VALUES (@attempt, @paper1, @item, @ver, @posForeign, 'A');
  THROW 50000, 'EXPECT_REJECT FAILED: an answer on paper one accepted a position belonging to paper two', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  SET @guardWrong = ERROR_MESSAGE();
  IF @guardWrong NOT LIKE '%FK_AttemptAnswers_ExamItemExam%'
    THROW 50000, 'the wrong guard refused a cross-paper position', 1;
  PRINT '1 ok: an answer cannot name another paper''s position';
END CATCH

-- the honest row is accepted, so the constraint is not simply refusing everything
INSERT INTO AttemptAnswers (AttemptId, ExamId, ItemId, ItemVersionId, ExamItemId, SelectedLabel)
VALUES (@attempt, @paper1, @item, @ver, @posOwn, 'A');
PRINT '1b ok: an answer on its own position is accepted';

-- ---------------------------------------------------------------------------------------------
-- 2. a free response's skill must be its position's section's skill
-- ---------------------------------------------------------------------------------------------
BEGIN TRY
  -- the position sits in the SPEAKING section, but claims the READING skill
  INSERT INTO FreeResponses (AttemptId, ExamItemId, ExamId, SectionId, SkillId, QuestionType, ResponseText, Status, SubmittedAt)
  VALUES (@attempt, @posSpeakingSection, @paper1, @s1, @readSkill, 'free_response', N'probe text', 'pending_ai', SYSDATETIMEOFFSET());
  THROW 50000, 'EXPECT_REJECT FAILED: a response claimed a skill that is not its section''s', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  SET @guardWrong = ERROR_MESSAGE();
  IF @guardWrong NOT LIKE '%FK_FreeResp_SectionSkill%'
    THROW 50000, 'the wrong guard refused a wrong-skill response', 1;
  PRINT '2 ok: a response cannot claim a skill its section does not hold';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 3. a lesson micro-check answer must be insertable (the card arm of CK_PracticeAnswers_Shape)
-- ---------------------------------------------------------------------------------------------
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionSource, Status, StartedAt, QuestionCount)
VALUES (@user, @cert, @topic, 'B1', 'reading', 'all', 'in_progress', SYSDATETIMEOFFSET(), 1);
DECLARE @sess INT = SCOPE_IDENTITY();

INSERT INTO Lessons (CertId, SkillId, Title, DisplayOrder, Modality, TopicId, CefrLevel)
VALUES (@cert, @readSkill, N'Probe lesson', 1, 'reading', @topic, 'B1');
DECLARE @lesson INT = SCOPE_IDENTITY();
INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder, Module)
VALUES (@lesson, @cert, 'micro_check', N'probe card', N'body', 1, 'primary');
DECLARE @card INT = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO PracticeAnswers (SessionId, CardId, SelectedLabel, IsCorrect) VALUES (@sess, @card, 'A', 1);
  PRINT '3 ok: a card-only practice answer is accepted';
END TRY
BEGIN CATCH
  THROW 50000, 'EXPECT_REJECT FAILED: a card-only practice answer was refused, so a lesson micro-check cannot be graded', 1;
END CATCH

-- the pool arm is still accepted in the same session
INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect)
VALUES (@sess, @item, @ver, 'A', 1);
PRINT '3b ok: a pool practice answer is accepted';

-- ---------------------------------------------------------------------------------------------
-- 4. an answer naming BOTH shapes is still refused
--
-- A THIRD session, so neither unique index can be what refuses the row: this session has answered
-- nothing yet, so only CK_PracticeAnswers_Shape can reject a two-shape answer.
INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionSource, Status, StartedAt, QuestionCount)
VALUES (@user, @cert, @topic, 'B1', 'reading', 'all', 'in_progress', SYSDATETIMEOFFSET(), 1);
DECLARE @sess2 INT = SCOPE_IDENTITY();

INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
VALUES (@item, 2, N'Second probe stem', N'a', N'b', N'c', N'd', 'A', @user);
DECLARE @ver2 INT = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, CardId, SelectedLabel)
  VALUES (@sess2, @item, @ver2, @card, 'A');
  THROW 50000, 'EXPECT_REJECT FAILED: an answer named both a version and a card', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  SET @guardWrong = ERROR_MESSAGE();
  IF @guardWrong NOT LIKE '%CK_PracticeAnswers_Shape%'
    THROW 50000, 'the wrong guard refused a two-shape practice answer', 1;
  PRINT '4 ok: an answer naming both shapes is refused';
END CATCH
