-- docs/seed-demo.sql
-- Demo seed: the public catalog (certificates, skills, sections, score bands), the two identity
-- roles, and the two demo accounts (learner@demo.local, admin@demo.local). Loaded after
-- database-schema.sql. Later slices append their own demo data here (exams, packages...) —
-- keep everything idempotent by key where practical.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
GO

-- ============ Identity: roles ============
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
  INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
  INSERT INTO Roles (Code, Name) VALUES ('admin', N'Administrator');
GO

-- ============ TOEIC ============
INSERT INTO Certificates (Code, Name, Description, IsActive) VALUES
  ('TOEIC', N'TOEIC Listening & Reading',
   N'The standard workplace English test. 200 multiple-choice questions, about 2 hours, scored 10–990. Practice speaking and writing separately with AI-graded tasks.', 1);
DECLARE @toeic INT = SCOPE_IDENTITY();

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
  (@toeic, 'LIST', N'Listening', 'listening', 1, 45),
  (@toeic, 'READ', N'Reading', 'reading', 2, 75);
DECLARE @toeicList INT = (SELECT SkillId FROM Skills WHERE CertId = @toeic AND Code = 'LIST');
DECLARE @toeicRead INT = (SELECT SkillId FROM Skills WHERE CertId = @toeic AND Code = 'READ');

INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
  (@toeicList, @toeic, 'listening', 'P1', N'Photographs', 4, 6, 1),
  (@toeicList, @toeic, 'listening', 'P2', N'Question-Response', 3, 25, 2),
  (@toeicList, @toeic, 'listening', 'P3', N'Conversations', 4, 39, 3),
  (@toeicList, @toeic, 'listening', 'P4', N'Talks', 4, 30, 4),
  (@toeicRead, @toeic, 'reading', 'P5', N'Incomplete Sentences', 4, 30, 1),
  (@toeicRead, @toeic, 'reading', 'P6', N'Text Completion', 4, 16, 2),
  (@toeicRead, @toeic, 'reading', 'P7', N'Reading Comprehension', 4, 54, 3);

INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
  (@toeic, 'A1', N'Beginner', 120, 220, 1),
  (@toeic, 'A2', N'Elementary', 225, 545, 2),
  (@toeic, 'B1', N'Intermediate', 550, 780, 3),
  (@toeic, 'B2', N'Upper intermediate', 785, 940, 4),
  (@toeic, 'C1', N'Advanced', 945, 990, 5);
GO

-- ============ IELTS ============
INSERT INTO Certificates (Code, Name, Description, IsActive) VALUES
  ('IELTS', N'IELTS Academic & General',
   N'The four-skill test for study, migration and work abroad. Scored in 0.5 steps from 0.0 to 9.0, with a separate band for each skill.', 1);
DECLARE @ielts INT = SCOPE_IDENTITY();

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
  (@ielts, 'LIST', N'Listening', 'listening', 1, 30),
  (@ielts, 'READ', N'Reading', 'reading', 2, 60),
  (@ielts, 'WRIT', N'Writing', 'writing', 3, 60),
  (@ielts, 'SPEA', N'Speaking', 'speaking', 4, 14);
DECLARE @ieltsList INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'LIST');
DECLARE @ieltsRead INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'READ');
DECLARE @ieltsWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'WRIT');
DECLARE @ieltsSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'SPEA');

INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
  (@ieltsList, @ielts, 'listening', 'REC', N'Four recordings: everyday conversation to academic lecture', 4, 40, 1),
  (@ieltsRead, @ielts, 'reading', 'PAS', N'Three long passages with a mix of question types', 4, 40, 1),
  (@ieltsWrit, @ielts, 'writing', 'T1', N'Task 1 — describe a chart (150 words)', NULL, 1, 1),
  (@ieltsWrit, @ielts, 'writing', 'T2', N'Task 2 — write an essay (250 words)', NULL, 1, 2),
  (@ieltsSpea, @ielts, 'speaking', 'INT', N'Interview, cue-card talk and discussion', NULL, 3, 1);

-- ScoreBands are INT-min/max: IELTS bands are stored x10 (0.0–9.0 → 0–90).
INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
  (@ielts, 'B1', N'Intermediate (4.0–5.0)', 40, 50, 1),
  (@ielts, 'B2', N'Upper intermediate (5.5–6.5)', 55, 65, 2),
  (@ielts, 'C1', N'Advanced (7.0–8.0)', 70, 80, 3),
  (@ielts, 'C2', N'Expert (8.5–9.0)', 85, 90, 4);
GO

-- ============ VSTEP ============
INSERT INTO Certificates (Code, Name, Description, IsActive) VALUES
  ('VSTEP', N'VSTEP — the Vietnamese national test',
   N'The national English proficiency test used by Vietnamese universities and employers. Four skills, reported on a 0–10 scale mapped to CEFR levels A2 to C1.', 1);
DECLARE @vstep INT = SCOPE_IDENTITY();

INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
  (@vstep, 'LIST', N'Listening', 'listening', 1, 40),
  (@vstep, 'READ', N'Reading', 'reading', 2, 60),
  (@vstep, 'WRIT', N'Writing', 'writing', 3, 60),
  (@vstep, 'SPEA', N'Speaking', 'speaking', 4, 12);
DECLARE @vstepList INT = (SELECT SkillId FROM Skills WHERE CertId = @vstep AND Code = 'LIST');
DECLARE @vstepRead INT = (SELECT SkillId FROM Skills WHERE CertId = @vstep AND Code = 'READ');
DECLARE @vstepWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @vstep AND Code = 'WRIT');
DECLARE @vstepSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @vstep AND Code = 'SPEA');

INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
  (@vstepList, @vstep, 'listening', 'L3', N'Three parts, played once, with conversations and talks', 4, 35, 1),
  (@vstepRead, @vstep, 'reading', 'R4', N'Four passages, 40 multiple-choice questions', 4, 40, 1),
  (@vstepWrit, @vstep, 'writing', 'W1', N'A letter or email', NULL, 1, 1),
  (@vstepWrit, @vstep, 'writing', 'W2', N'An essay', NULL, 1, 2),
  (@vstepSpea, @vstep, 'speaking', 'S3', N'Social interaction, description and discussion', NULL, 3, 1);

-- Stored x10 like IELTS (0–10 → 0–100).
INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
  (@vstep, 'A2', N'A2 (4.0)', 40, 45, 1),
  (@vstep, 'B1', N'B1 (5.5–6.0)', 50, 60, 2),
  (@vstep, 'B2', N'B2 (6.5–7.5)', 65, 75, 3),
  (@vstep, 'C1', N'C1 (8.5+)', 80, 100, 4);
GO

PRINT 'demo catalog seeded';
GO

-- ============ Demo accounts (issue #4) ============
-- One learner and one admin. Passwords are framework PasswordHasher hashes (PBKDF2-SHA256,
-- format v1, stored raw in the VARBINARY column — the app converts base64 at the boundary):
--   learner@demo.local / Learner!1234      admin@demo.local / Admin!1234
-- TermsAcceptedAt is set because registration requires it. EmailVerifiedAt is left NULL —
-- email activation is a later slice (see EmailVerificationTokens), and a demo account should
-- look like a real registered account until then.
IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'learner@demo.local')
  INSERT INTO Users (RoleId, Email, PasswordHash, FullName, Phone, TermsAcceptedAt)
  VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'learner@demo.local',
    0x0100000002000186A000000010D7A3A03C3CDA12DC7DE9F8E355177F9E52878DFC5F8E1C648FB5B6A7E779DD958F54F2355062DA531CBA010CFA0A06C6,
    N'Demo Learner', '+84 901 000 001', SYSDATETIMEOFFSET());
IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'admin@demo.local')
  INSERT INTO Users (RoleId, Email, PasswordHash, FullName, TermsAcceptedAt)
  VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin@demo.local',
    0x0100000002000186A00000001021E5B23D0E21492CC2BAA81AA8B2AFBAAEA0790A49CDD2323CDFC2FC1AC71F58BFCBC50B442992BA08751C83CC16A1A4,
    N'Demo Admin', SYSDATETIMEOFFSET());
GO

PRINT 'demo accounts seeded';
GO

-- ============ Active certificate and learner history (issue #7) ============
-- The demo learner prepares for TOEIC, with two graded mock attempts and two practice sessions
-- under it and nothing under IELTS or VSTEP: switching the certificate on the dashboard shows
-- the isolation directly. The admin account gets no active certificate — admins have no learner
-- shell. Credits are granted by the admin so the balance on the shell is a real ledger sum.
DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @adminUser INT = (SELECT UserId FROM Users WHERE Email = 'admin@demo.local');
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');

UPDATE Users SET ActiveCertId = @toeicCert WHERE UserId = @learner AND ActiveCertId IS NULL;

IF NOT EXISTS (SELECT 1 FROM CreditTransactions WHERE UserId = @learner)
  INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
  VALUES (@learner, 'admin_grant', 8, @adminUser, N'Demo starting credits');
GO

-- A published TOEIC mock exam to hang the demo attempts off. Questions arrive in issue #15;
-- an exam with no questions is still a legal published exam and is all the dashboard reads.
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS 2024 — Test 1')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@toeicCert, N'ETS 2024 — Test 1', 'published', 120, 'mock',
          N'A full TOEIC Listening & Reading form.', N'ETS 2024', 200);
GO

DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @toeicExam INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS 2024 — Test 1');
-- Scores are stored on the attempt only (AttemptSkillScores would have to sum to the total;
-- the per-skill breakdown arrives with the real scoring slice).
IF NOT EXISTS (SELECT 1 FROM ExamAttempts WHERE UserId = @learner AND CertId = @toeicCert)
  INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode) VALUES
    (@learner, @toeicExam, @toeicCert, 'graded',
     DATEADD(DAY, -21, SYSDATETIMEOFFSET()), DATEADD(DAY, -21, DATEADD(HOUR, 2, SYSDATETIMEOFFSET())),
     DATEADD(DAY, -21, DATEADD(MINUTE, 113, SYSDATETIMEOFFSET())), 690, 'B1'),
    (@learner, @toeicExam, @toeicCert, 'graded',
     DATEADD(DAY, -4, SYSDATETIMEOFFSET()), DATEADD(DAY, -4, DATEADD(HOUR, 2, SYSDATETIMEOFFSET())),
     DATEADD(DAY, -4, DATEADD(MINUTE, 109, SYSDATETIMEOFFSET())), 760, 'B1');

IF NOT EXISTS (SELECT 1 FROM PracticeSessions p JOIN Sections s ON s.SectionId = p.SectionId
               WHERE p.UserId = @learner AND s.CertId = @toeicCert)
  INSERT INTO PracticeSessions (UserId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt) VALUES
    (@learner, (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P2'), 'listening',
     25, 'finished', 19, DATEADD(DAY, -3, SYSDATETIMEOFFSET()), DATEADD(DAY, -3, DATEADD(MINUTE, 18, SYSDATETIMEOFFSET()))),
    (@learner, (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P5'), 'reading',
     30, 'finished', 21, DATEADD(DAY, -1, SYSDATETIMEOFFSET()), DATEADD(DAY, -1, DATEADD(MINUTE, 24, SYSDATETIMEOFFSET())));
GO

PRINT 'demo learner history seeded';
GO
