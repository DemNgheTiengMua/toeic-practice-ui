SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #216 / PLC-13, PLC-15: the two facts a reload must not be able to forge.
--
-- What this file pins, and the input that would pass a weaker version while being wrong:
--   1. `ExamAttempts.LastActivityAt` exists and is NULLABLE. A NOT NULL column with a default would
--      stamp "activity" at insert time, so a sitting nobody touched would look touched, and the
--      inactivity arm would be measured from the wrong instant.
--   2. `FreeResponses.RecordingInterruptions` exists and defaults to 0 - a placement task that has
--      never been interrupted claims no interruption.
--   3. The cap is enforced by the DATABASE, not only the service: a third interruption is refused.
--      The input that would pass a service-only rule while being wrong is a raw UPDATE, which is
--      what a second writer or a replay would do; that UPDATE must throw.
--   4. A token is stored so one interruption counted twice is recognisable.

INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('RES216',N'Resume cert',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'L',N'Listening','listening',1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount,DisplayOrder)
  VALUES (@sk,@c,'listening','L1',N'Listening',1,1); DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Placement 216','published',45,'placement');
DECLARE @e INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@e,@c,@sec,1); DECLARE @g INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,DisplayOrder)
  VALUES (@g,@e,@sec,@sk,N'Record an answer.','B1','free_response',1); DECLARE @q INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'resume216@x.com',0x00,@c); DECLARE @u INT=SCOPE_IDENTITY();

-- 1. the inactivity stamp exists, and is nullable so nothing is invented at insert time
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME='ExamAttempts' AND COLUMN_NAME='LastActivityAt')
  THROW 50000, 'EXPECT_REJECT FAILED: ExamAttempts carries no LastActivityAt, so PLC-13 has only one of its two arms', 1;
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_NAME='ExamAttempts' AND COLUMN_NAME='LastActivityAt' AND IS_NULLABLE='NO')
  THROW 50000, 'EXPECT_REJECT FAILED: LastActivityAt is NOT NULL, so opening a sitting counts as activity it never had', 1;

INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,StartedAt,ExpiresAt)
  VALUES (@u,@e,@c,'in_progress',SYSDATETIMEOFFSET(),DATEADD(hour,48,SYSDATETIMEOFFSET()));
DECLARE @a INT=SCOPE_IDENTITY();
IF (SELECT LastActivityAt FROM ExamAttempts WHERE AttemptId=@a) IS NOT NULL
  THROW 50000, 'EXPECT_REJECT FAILED: a fresh attempt already claims activity', 1;
PRINT 'the inactivity stamp is nullable and starts empty';

-- 2. the retry count defaults to zero and its token starts empty
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,SkillId,QuestionType,Status,DraftSavedAt,SubmittedAt,AudioPath,DurationMs)
  VALUES (@a,@q,@e,@sk,'free_response','draft',SYSDATETIMEOFFSET(),NULL,N'/audio/resume216.webm',9000);
DECLARE @fr INT=SCOPE_IDENTITY();
IF (SELECT RecordingInterruptions FROM FreeResponses WHERE FreeResponseId=@fr) <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: a task that was never interrupted claims interruptions', 1;
IF (SELECT RecordingSessionToken FROM FreeResponses WHERE FreeResponseId=@fr) IS NOT NULL
  THROW 50000, 'EXPECT_REJECT FAILED: a task that was never interrupted carries a token', 1;
PRINT 'the retry count starts at zero and its token at NULL';

-- 3. the cap is the database's, not the service's: a third interruption is refused
BEGIN TRY
  UPDATE FreeResponses SET RecordingInterruptions = 3 WHERE FreeResponseId=@fr;
  THROW 50000, 'EXPECT_REJECT FAILED: a third recording interruption was accepted, so "exactly one retry" is only a service promise', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a third interruption';
END CATCH

-- the legal range is still writable, so the cap is a cap and not a blanket refusal
UPDATE FreeResponses SET RecordingInterruptions = 2, RecordingSessionToken = N'tok-2' WHERE FreeResponseId=@fr;
IF (SELECT RecordingInterruptions FROM FreeResponses WHERE FreeResponseId=@fr) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the second interruption could not be recorded', 1;
PRINT 'the second interruption is storable and the third is not';
