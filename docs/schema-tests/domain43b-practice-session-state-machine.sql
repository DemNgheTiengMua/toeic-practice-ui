SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #269 audit: a practice session's STATUS is a state machine, and nothing guarded it.
--
-- What this file proves, and the input that would pass a weaker version while being wrong:
--
--   1. The legal moves still work: in_progress -> finished, and in_progress -> abandoned. A guard that
--      refused those would be a guard nobody could satisfy, so the test asserts them FIRST.
--   2. finished -> in_progress is REFUSED. The input that would pass a weaker version is "the column
--      is only ever written by the service", which was true and did not help: two readers
--      (PracticeService.Results and AiInsightService.Practice) each finished a session on a GET, and a
--      fix in one place could not stop the next one. The state itself is what has to be defended.
--   3. abandoned -> in_progress is refused for the same reason.
--   4. finished -> abandoned is refused: both are terminal, and moving between them would re-stamp
--      FinishedAt on a session whose statistics were already published.
--
-- Each refusal is asserted by catching the THROW, not by checking a count afterwards, so a trigger
-- that silently allowed the write would fail on the missing exception.
SET NOCOUNT ON;

INSERT INTO Roles (Code, Name) VALUES ('probe-role-43b', N'Probe role');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@role, 'probe43b@probe.local', 0x01);
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('PROBE43B', N'Probe cert', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
VALUES (@cert, 'READ', N'Reading', 'reading', 1, 0, 100, 1);
DECLARE @skill INT = SCOPE_IDENTITY();
INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount, NavigationMode, LastWrittenBy)
VALUES (@skill, @cert, 'reading', 'R1', N'Reading', 1, 1, 'FULL_SANDBOX', @user);
DECLARE @sec INT = SCOPE_IDENTITY();

-- 1. The legal moves still work.
INSERT INTO PracticeSessions (UserId, CertId, SectionId, Modality, QuestionCount, Status)
VALUES (@user, @cert, @sec, 'reading', 1, 'in_progress');
DECLARE @s1 INT = SCOPE_IDENTITY();
UPDATE PracticeSessions SET Status = 'finished', FinishedAt = SYSDATETIMEOFFSET() WHERE SessionId = @s1;
IF (SELECT Status FROM PracticeSessions WHERE SessionId = @s1) <> 'finished'
  THROW 50000, 'in_progress -> finished was refused; the guard is wrong, not the write', 1;

INSERT INTO PracticeSessions (UserId, CertId, SectionId, Modality, QuestionCount, Status)
VALUES (@user, @cert, @sec, 'reading', 1, 'in_progress');
DECLARE @s2 INT = SCOPE_IDENTITY();
UPDATE PracticeSessions SET Status = 'abandoned', FinishedAt = SYSDATETIMEOFFSET() WHERE SessionId = @s2;
IF (SELECT Status FROM PracticeSessions WHERE SessionId = @s2) <> 'abandoned'
  THROW 50000, 'in_progress -> abandoned was refused; the guard is wrong, not the write', 1;
PRINT 'the legal transitions still work';

-- 2. finished -> in_progress is refused.
BEGIN TRY
  UPDATE PracticeSessions SET Status = 'in_progress', FinishedAt = NULL WHERE SessionId = @s1;
  THROW 50000, 'EXPECT_REJECT FAILED: a finished session was reopened', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'reopening a finished session is refused';
END CATCH;

-- 3. abandoned -> in_progress is refused.
BEGIN TRY
  UPDATE PracticeSessions SET Status = 'in_progress', FinishedAt = NULL WHERE SessionId = @s2;
  THROW 50000, 'EXPECT_REJECT FAILED: an abandoned session was reopened', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'reopening an abandoned session is refused';
END CATCH;

-- 4. finished -> abandoned is refused: both are terminal.
BEGIN TRY
  UPDATE PracticeSessions SET Status = 'abandoned' WHERE SessionId = @s1;
  THROW 50000, 'EXPECT_REJECT FAILED: a finished session was moved to abandoned', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'finished -> abandoned is refused';
END CATCH;

PRINT 'PracticeSessions state machine: OK';
