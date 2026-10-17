# scripts/build_seed.py
# Generates docs/seed-demo.sql ensuring idempotency and full coverage of Ticket #32 requirements.

import os

sql = """-- docs/seed-demo.sql
-- Demo seed: full demo data set for black-box grading (issue #32).
-- Single source of truth for demo data across all certificates and screens.
-- Can be run cleanly and idempotently with:
--   sqlcmd -S "(localdb)\\MSSQLLocalDB" -d CompanionSchemaTest -I -b -i docs/seed-demo.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET NOCOUNT ON;
GO

-- ============================================================================
-- 1. Identity Roles
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
  INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
  INSERT INTO Roles (Code, Name) VALUES ('admin', N'Administrator');
GO

PRINT '1. Identity roles seeded';
GO

-- ============================================================================
-- 2. Certificates, Skills, Sections, ScoreBands (Catalog)
-- ============================================================================

-- ---------- TOEIC ----------
IF NOT EXISTS (SELECT 1 FROM Certificates WHERE Code = 'TOEIC')
BEGIN
  INSERT INTO Certificates (Code, Name, Description, IsActive) VALUES
    ('TOEIC', N'TOEIC Listening & Reading',
     N'The standard workplace English test. 200 multiple-choice questions, about 2 hours, scored 10–990. Practice speaking and writing separately with AI-graded tasks.', 1);
END;
GO

DECLARE @toeic INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');

-- Skills: LIST, READ, SPEA, WRIT
IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @toeic AND Code = 'LIST')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@toeic, 'LIST', N'Listening', 'listening', 1, 45);

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @toeic AND Code = 'READ')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@toeic, 'READ', N'Reading', 'reading', 2, 75);

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @toeic AND Code = 'SPEA')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@toeic, 'SPEA', N'Speaking', 'speaking', 3, 20);

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @toeic AND Code = 'WRIT')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@toeic, 'WRIT', N'Writing', 'writing', 4, 60);

DECLARE @toeicList INT = (SELECT SkillId FROM Skills WHERE CertId = @toeic AND Code = 'LIST');
DECLARE @toeicRead INT = (SELECT SkillId FROM Skills WHERE CertId = @toeic AND Code = 'READ');
DECLARE @toeicSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @toeic AND Code = 'SPEA');
DECLARE @toeicWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @toeic AND Code = 'WRIT');

-- Listening Sections: P1, P2, P3, P4
IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'P1')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicList, @toeic, 'listening', 'P1', N'Photographs', 4, 6, 1);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'P2')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicList, @toeic, 'listening', 'P2', N'Question-Response', 3, 25, 2);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'P3')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicList, @toeic, 'listening', 'P3', N'Conversations', 4, 39, 3);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'P4')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicList, @toeic, 'listening', 'P4', N'Talks', 4, 30, 4);

-- Reading Sections: P5, P6, P7
IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'P5')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicRead, @toeic, 'reading', 'P5', N'Incomplete Sentences', 4, 30, 1);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'P6')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicRead, @toeic, 'reading', 'P6', N'Text Completion', 4, 16, 2);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'P7')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicRead, @toeic, 'reading', 'P7', N'Reading Comprehension', 4, 54, 3);

-- Speaking Sections: S1 - S5
IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'S1')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicSpea, @toeic, 'speaking', 'S1', N'Read a text aloud', NULL, 2, 1);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'S2')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicSpea, @toeic, 'speaking', 'S2', N'Describe a picture', NULL, 2, 2);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'S3')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicSpea, @toeic, 'speaking', 'S3', N'Respond to questions', NULL, 3, 3);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'S4')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicSpea, @toeic, 'speaking', 'S4', N'Respond using information provided', NULL, 3, 4);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'S5')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicSpea, @toeic, 'speaking', 'S5', N'Express an opinion', NULL, 1, 5);

-- Writing Sections: W1 - W3
IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'W1')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicWrit, @toeic, 'writing', 'W1', N'Write a sentence based on a picture', NULL, 5, 1);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'W2')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicWrit, @toeic, 'writing', 'W2', N'Respond to a written request', NULL, 2, 2);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeic AND Code = 'W3')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@toeicWrit, @toeic, 'writing', 'W3', N'Write an opinion essay', NULL, 1, 3);

-- ScoreBands
IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @toeic AND Code = 'below_A1')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@toeic, 'below_A1', N'Below A1', 10, 119, 0);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @toeic AND Code = 'A1')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@toeic, 'A1', N'Beginner', 120, 220, 1);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @toeic AND Code = 'A2')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@toeic, 'A2', N'Elementary', 225, 545, 2);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @toeic AND Code = 'B1')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@toeic, 'B1', N'Intermediate', 550, 780, 3);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @toeic AND Code = 'B2')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@toeic, 'B2', N'Upper intermediate', 785, 940, 4);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @toeic AND Code = 'C1')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@toeic, 'C1', N'Advanced', 945, 990, 5);
GO

-- ---------- IELTS ----------
IF NOT EXISTS (SELECT 1 FROM Certificates WHERE Code = 'IELTS')
BEGIN
  INSERT INTO Certificates (Code, Name, Description, IsActive) VALUES
    ('IELTS', N'IELTS Academic & General',
     N'The four-skill test for study, migration and work abroad. Scored in 0.5 steps from 0.0 to 9.0, with a separate band for each skill.', 1);
END;
GO

DECLARE @ielts INT = (SELECT CertId FROM Certificates WHERE Code = 'IELTS');

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @ielts AND Code = 'LIST')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@ielts, 'LIST', N'Listening', 'listening', 1, 30);

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @ielts AND Code = 'READ')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@ielts, 'READ', N'Reading', 'reading', 2, 60);

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @ielts AND Code = 'WRIT')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@ielts, 'WRIT', N'Writing', 'writing', 3, 60);

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @ielts AND Code = 'SPEA')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@ielts, 'SPEA', N'Speaking', 'speaking', 4, 14);

DECLARE @ieltsList INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'LIST');
DECLARE @ieltsRead INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'READ');
DECLARE @ieltsWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'WRIT');
DECLARE @ieltsSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'SPEA');

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @ielts AND Code = 'REC')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@ieltsList, @ielts, 'listening', 'REC', N'Four recordings: everyday conversation to academic lecture', 4, 40, 1);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @ielts AND Code = 'PAS')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@ieltsRead, @ielts, 'reading', 'PAS', N'Three long passages with a mix of question types', 4, 40, 1);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @ielts AND Code = 'T1')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@ieltsWrit, @ielts, 'writing', 'T1', N'Task 1 — describe a chart (150 words)', NULL, 1, 1);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @ielts AND Code = 'T2')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@ieltsWrit, @ielts, 'writing', 'T2', N'Task 2 — write an essay (250 words)', NULL, 1, 2);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @ielts AND Code = 'INT')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@ieltsSpea, @ielts, 'speaking', 'INT', N'Interview, cue-card talk and discussion', NULL, 3, 1);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @ielts AND Code = 'B1')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@ielts, 'B1', N'Intermediate (4.0–5.0)', 40, 50, 1);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @ielts AND Code = 'B2')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@ielts, 'B2', N'Upper intermediate (5.5–6.5)', 55, 65, 2);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @ielts AND Code = 'C1')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@ielts, 'C1', N'Advanced (7.0–8.0)', 70, 80, 3);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @ielts AND Code = 'C2')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@ielts, 'C2', N'Expert (8.5–9.0)', 85, 90, 4);
GO

-- ---------- VSTEP ----------
IF NOT EXISTS (SELECT 1 FROM Certificates WHERE Code = 'VSTEP')
BEGIN
  INSERT INTO Certificates (Code, Name, Description, IsActive) VALUES
    ('VSTEP', N'VSTEP — the Vietnamese national test',
     N'The national English proficiency test used by Vietnamese universities and employers. Four skills, reported on a 0–10 scale mapped to CEFR levels A2 to C1.', 1);
END;
GO

DECLARE @vstep INT = (SELECT CertId FROM Certificates WHERE Code = 'VSTEP');

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @vstep AND Code = 'LIST')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@vstep, 'LIST', N'Listening', 'listening', 1, 40);

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @vstep AND Code = 'READ')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@vstep, 'READ', N'Reading', 'reading', 2, 60);

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @vstep AND Code = 'WRIT')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@vstep, 'WRIT', N'Writing', 'writing', 3, 60);

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @vstep AND Code = 'SPEA')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@vstep, 'SPEA', N'Speaking', 'speaking', 4, 12);

DECLARE @vstepList INT = (SELECT SkillId FROM Skills WHERE CertId = @vstep AND Code = 'LIST');
DECLARE @vstepRead INT = (SELECT SkillId FROM Skills WHERE CertId = @vstep AND Code = 'READ');
DECLARE @vstepWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @vstep AND Code = 'WRIT');
DECLARE @vstepSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @vstep AND Code = 'SPEA');

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @vstep AND Code = 'L3')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@vstepList, @vstep, 'listening', 'L3', N'Three parts, played once, with conversations and talks', 4, 35, 1);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @vstep AND Code = 'R4')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@vstepRead, @vstep, 'reading', 'R4', N'Four passages, 40 multiple-choice questions', 4, 40, 1);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @vstep AND Code = 'W1')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@vstepWrit, @vstep, 'writing', 'W1', N'A letter or email', NULL, 1, 1);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @vstep AND Code = 'W2')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@vstepWrit, @vstep, 'writing', 'W2', N'An essay', NULL, 1, 2);

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @vstep AND Code = 'S3')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
  VALUES (@vstepSpea, @vstep, 'speaking', 'S3', N'Social interaction, description and discussion', NULL, 3, 1);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @vstep AND Code = 'A2')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@vstep, 'A2', N'A2 (4.0)', 40, 45, 1);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @vstep AND Code = 'B1')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@vstep, 'B1', N'B1 (5.5–6.0)', 50, 60, 2);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @vstep AND Code = 'B2')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@vstep, 'B2', N'B2 (6.5–7.5)', 65, 75, 3);

IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @vstep AND Code = 'C1')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@vstep, 'C1', N'C1 (8.5+)', 80, 100, 4);
GO

PRINT '2. Demo catalog seeded';
GO

-- ============================================================================
-- 3. Demo Accounts
-- ============================================================================
-- Password for all accounts: Demo1234!
-- PBKDF2-SHA256, format v1 hash
DECLARE @pwd VARBINARY(256) = 0x0100000002000186A0000000103C900F405E0EA707A8C20B5D134127259871478D350B03660E6C246E724D8C436A9004C3BA672FE5A2481FAB2C3223E9;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');

-- Administrator: admin@demo.local
IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'admin@demo.local')
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash, FullName, TermsAcceptedAt, EmailVerifiedAt)
  VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin@demo.local', @pwd,
          N'Admin User', @now, @now);
END
ELSE
BEGIN
  UPDATE Users
  SET FullName = N'Admin User',
      PasswordHash = @pwd,
      TermsAcceptedAt = ISNULL(TermsAcceptedAt, @now),
      EmailVerifiedAt = ISNULL(EmailVerifiedAt, @now)
  WHERE Email = 'admin@demo.local';
END;

-- Demo Learner with rich history: learner@demo.local (ActiveCertId = TOEIC)
IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'learner@demo.local')
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash, FullName, Phone, ActiveCertId, TermsAcceptedAt, EmailVerifiedAt)
  VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'learner@demo.local', @pwd,
          N'Demo Learner', '+84 901 000 001', @toeicCert, @now, @now);
END
ELSE
BEGIN
  UPDATE Users
  SET FullName = N'Demo Learner',
      PasswordHash = @pwd,
      ActiveCertId = ISNULL(ActiveCertId, @toeicCert),
      TermsAcceptedAt = ISNULL(TermsAcceptedAt, @now),
      EmailVerifiedAt = ISNULL(EmailVerifiedAt, @now)
  WHERE Email = 'learner@demo.local';
END;

-- Fresh Learner without any history: learner.fresh@demo.local
IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'learner.fresh@demo.local')
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash, FullName, TermsAcceptedAt, EmailVerifiedAt)
  VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'learner.fresh@demo.local', @pwd,
          N'Fresh Student', @now, @now);
END
ELSE
BEGIN
  UPDATE Users
  SET FullName = N'Fresh Student',
      PasswordHash = @pwd,
      TermsAcceptedAt = ISNULL(TermsAcceptedAt, @now),
      EmailVerifiedAt = ISNULL(EmailVerifiedAt, @now)
  WHERE Email = 'learner.fresh@demo.local';
END;

-- Mentor user: mentor.khoa@demo.local
IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'mentor.khoa@demo.local')
BEGIN
  INSERT INTO Users (RoleId, Email, PasswordHash, FullName, Phone, TermsAcceptedAt, EmailVerifiedAt)
  VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'mentor.khoa@demo.local', @pwd,
          N'Dr. Trần Minh Khoa', '+84 901 000 002', @now, @now);
END
ELSE
BEGIN
  UPDATE Users
  SET FullName = N'Dr. Trần Minh Khoa',
      PasswordHash = @pwd,
      TermsAcceptedAt = ISNULL(TermsAcceptedAt, @now),
      EmailVerifiedAt = ISNULL(EmailVerifiedAt, @now)
  WHERE Email = 'mentor.khoa@demo.local';
END;
GO

PRINT '3. Demo accounts seeded';
GO

-- ============================================================================
-- 4. Credit Packages
-- ============================================================================
-- 5 credits (100,000 VND), 10 credits (190,000 VND, featured), 20 credits (360,000 VND)
IF NOT EXISTS (SELECT 1 FROM Packages WHERE Credits = 10 AND Price = 190000)
BEGIN
  UPDATE Packages SET IsFeatured = 0 WHERE IsFeatured = 1;
  INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
  VALUES (N'Intensive Pack', 10, 190000, 1, 1,
          N'10 full-exam / grading credits' + CHAR(10) + N'Detailed weakness diagnostic' + CHAR(10) + N'Priority AI mentor evaluation' + CHAR(10) + N'Never expires', 2);
END;

IF NOT EXISTS (SELECT 1 FROM Packages WHERE Credits = 5 AND Price = 100000)
BEGIN
  INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
  VALUES (N'Starter Pack', 5, 100000, 1, 0,
          N'5 full-exam / grading credits' + CHAR(10) + N'Access to AI mentor feedback' + CHAR(10) + N'Never expires', 1);
END;

IF NOT EXISTS (SELECT 1 FROM Packages WHERE Credits = 20 AND Price = 360000)
BEGIN
  INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
  VALUES (N'Master Pack', 20, 360000, 1, 0,
          N'20 full-exam / grading credits' + CHAR(10) + N'Complete path unlocking' + CHAR(10) + N'1-on-1 human mentor booking' + CHAR(10) + N'Never expires', 3);
END;
GO

PRINT '4. Demo credit packages seeded';
GO

-- ============================================================================
-- 5. Orders, Payment Events, and Credit Ledger
-- ============================================================================
DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @pkg10 INT = (SELECT TOP 1 PackageId FROM Packages WHERE Credits = 10 AND Price = 190000);
DECLARE @pkg5 INT = (SELECT TOP 1 PackageId FROM Packages WHERE Credits = 5 AND Price = 100000);

-- Initial Purchase 1: 10 credits (190,000 VND)
IF NOT EXISTS (SELECT 1 FROM Orders WHERE OrderCode = 'DH-20261014-0001')
BEGIN
  INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway, GatewayTxnRef, CreatedAt, PaidAt)
  VALUES (@learner, @pkg10, 'paid', 190000, 'DH-20261014-0001', NEXT VALUE FOR SEQ_PaymentNo, 'mock', 'plink-demo-10',
          DATEADD(DAY, -25, SYSDATETIMEOFFSET()), DATEADD(DAY, -25, SYSDATETIMEOFFSET()));
  DECLARE @o1 INT = SCOPE_IDENTITY();

  INSERT INTO PaymentEvents (OrderId, Source, ReportedStatus, SignatureValid, WasApplied, Note, CreatedAt)
  VALUES (@o1, 'webhook', 'paid', 1, 1, N'payOS webhook simulation — 10 credits', DATEADD(DAY, -25, SYSDATETIMEOFFSET()));

  INSERT INTO CreditTransactions (UserId, Reason, Delta, OrderId, CreatedAt)
  VALUES (@learner, 'purchase', 10, @o1, DATEADD(DAY, -25, SYSDATETIMEOFFSET()));
END;

-- Initial Purchase 2: 5 credits (100,000 VND) -> Total purchased credits = 15 credits
IF NOT EXISTS (SELECT 1 FROM Orders WHERE OrderCode = 'DH-20261014-0002')
BEGIN
  INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway, GatewayTxnRef, CreatedAt, PaidAt)
  VALUES (@learner, @pkg5, 'paid', 100000, 'DH-20261014-0002', NEXT VALUE FOR SEQ_PaymentNo, 'mock', 'plink-demo-5',
          DATEADD(DAY, -18, SYSDATETIMEOFFSET()), DATEADD(DAY, -18, SYSDATETIMEOFFSET()));
  DECLARE @o2 INT = SCOPE_IDENTITY();

  INSERT INTO PaymentEvents (OrderId, Source, ReportedStatus, SignatureValid, WasApplied, Note, CreatedAt)
  VALUES (@o2, 'webhook', 'paid', 1, 1, N'payOS webhook simulation — 5 credits', DATEADD(DAY, -18, SYSDATETIMEOFFSET()));

  INSERT INTO CreditTransactions (UserId, Reason, Delta, OrderId, CreatedAt)
  VALUES (@learner, 'purchase', 5, @o2, DATEADD(DAY, -18, SYSDATETIMEOFFSET()));
END;
GO

PRINT '5. Orders and purchases seeded';
GO

-- ============================================================================
-- 6. Question Categories
-- ============================================================================
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @ieltsCert INT = (SELECT CertId FROM Certificates WHERE Code = 'IELTS');
DECLARE @vstepCert INT = (SELECT CertId FROM Certificates WHERE Code = 'VSTEP');

IF NOT EXISTS (SELECT 1 FROM QuestionCategories WHERE CertId = @toeicCert AND Code = 'grammar_tense')
  INSERT INTO QuestionCategories (CertId, Code, Name) VALUES
    (@toeicCert, 'grammar_tense', N'Verb tense'),
    (@toeicCert, 'grammar_prep', N'Preposition'),
    (@toeicCert, 'grammar_pos', N'Part of speech'),
    (@toeicCert, 'grammar_rel', N'Relative pronoun'),
    (@toeicCert, 'grammar_conj', N'Conjunction'),
    (@toeicCert, 'listening_photo', N'Photographs'),
    (@toeicCert, 'listening_detail', N'Detail & facts'),
    (@toeicCert, 'reading_vocab', N'Vocabulary'),
    (@toeicCert, 'reading_inference', N'Inference');

IF NOT EXISTS (SELECT 1 FROM QuestionCategories WHERE CertId = @ieltsCert AND Code = 'ielts_detail')
  INSERT INTO QuestionCategories (CertId, Code, Name) VALUES
    (@ieltsCert, 'ielts_detail', N'Factual detail'),
    (@ieltsCert, 'ielts_inference', N'Inference'),
    (@ieltsCert, 'ielts_main_idea', N'Main idea');

IF NOT EXISTS (SELECT 1 FROM QuestionCategories WHERE CertId = @vstepCert AND Code = 'vstep_detail')
  INSERT INTO QuestionCategories (CertId, Code, Name) VALUES
    (@vstepCert, 'vstep_detail', N'Factual detail'),
    (@vstepCert, 'vstep_grammar', N'Grammar & structure'),
    (@vstepCert, 'vstep_vocab', N'Vocabulary');
GO

PRINT '6. Question categories seeded';
GO

-- ============================================================================
-- 7. Grading Rubrics (GradingCriteria)
-- ============================================================================
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @ieltsCert INT = (SELECT CertId FROM Certificates WHERE Code = 'IELTS');
DECLARE @vstepCert INT = (SELECT CertId FROM Certificates WHERE Code = 'VSTEP');

DECLARE @toeicSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'SPEA');
DECLARE @toeicWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'WRIT');
DECLARE @ieltsSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'SPEA');
DECLARE @ieltsWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'WRIT');
DECLARE @vstepSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @vstepCert AND Code = 'SPEA');
DECLARE @vstepWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @vstepCert AND Code = 'WRIT');

-- TOEIC Speaking Rubric (MaxScore 3.0)
IF NOT EXISTS (SELECT 1 FROM GradingCriteria WHERE CertId = @toeicCert AND SkillId = @toeicSpea AND Code = 'PRON')
  INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
    (@toeicCert, @toeicSpea, 'PRON', N'Pronunciation', 3.0, 1.0, 1),
    (@toeicCert, @toeicSpea, 'INT',  N'Intonation and stress', 3.0, 1.0, 2),
    (@toeicCert, @toeicSpea, 'GRAM', N'Grammar, vocabulary and cohesion', 3.0, 1.0, 3),
    (@toeicCert, @toeicSpea, 'REL',  N'Relevance and completeness', 3.0, 1.0, 4);

-- TOEIC Writing Rubric (MaxScore 5.0)
IF NOT EXISTS (SELECT 1 FROM GradingCriteria WHERE CertId = @toeicCert AND SkillId = @toeicWrit AND Code = 'GRAM')
  INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
    (@toeicCert, @toeicWrit, 'GRAM',  N'Grammar & sentence structure', 5.0, 1.0, 1),
    (@toeicCert, @toeicWrit, 'VOCAB', N'Vocabulary and word choice', 5.0, 1.0, 2),
    (@toeicCert, @toeicWrit, 'ORG',   N'Organization and coherence', 5.0, 1.0, 3),
    (@toeicCert, @toeicWrit, 'REL',   N'Relevance to the prompt', 5.0, 1.0, 4);

-- IELTS Writing Rubric (MaxScore 9.0)
IF NOT EXISTS (SELECT 1 FROM GradingCriteria WHERE CertId = @ieltsCert AND SkillId = @ieltsWrit AND Code = 'TR')
  INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
    (@ieltsCert, @ieltsWrit, 'TR',  N'Task Response', 9.0, 1.0, 1),
    (@ieltsCert, @ieltsWrit, 'CC',  N'Coherence & Cohesion', 9.0, 1.0, 2),
    (@ieltsCert, @ieltsWrit, 'LR',  N'Lexical Resource', 9.0, 1.0, 3),
    (@ieltsCert, @ieltsWrit, 'GRA', N'Grammatical Range & Accuracy', 9.0, 1.0, 4);

-- IELTS Speaking Rubric (MaxScore 9.0)
IF NOT EXISTS (SELECT 1 FROM GradingCriteria WHERE CertId = @ieltsCert AND SkillId = @ieltsSpea AND Code = 'FC')
  INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
    (@ieltsCert, @ieltsSpea, 'FC',   N'Fluency and Coherence', 9.0, 1.0, 1),
    (@ieltsCert, @ieltsSpea, 'LR',   N'Lexical Resource', 9.0, 1.0, 2),
    (@ieltsCert, @ieltsSpea, 'GRA',  N'Grammatical Range and Accuracy', 9.0, 1.0, 3),
    (@ieltsCert, @ieltsSpea, 'PRON', N'Pronunciation', 9.0, 1.0, 4);

-- VSTEP Writing Rubric (MaxScore 10.0)
IF NOT EXISTS (SELECT 1 FROM GradingCriteria WHERE CertId = @vstepCert AND SkillId = @vstepWrit AND Code = 'TASK')
  INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
    (@vstepCert, @vstepWrit, 'TASK',  N'Task Fulfillment', 10.0, 1.0, 1),
    (@vstepCert, @vstepWrit, 'ORG',   N'Organization', 10.0, 1.0, 2),
    (@vstepCert, @vstepWrit, 'VOCAB', N'Vocabulary', 10.0, 1.0, 3),
    (@vstepCert, @vstepWrit, 'GRAM',  N'Grammar & Structures', 10.0, 1.0, 4);

-- VSTEP Speaking Rubric (MaxScore 10.0)
IF NOT EXISTS (SELECT 1 FROM GradingCriteria WHERE CertId = @vstepCert AND SkillId = @vstepSpea AND Code = 'PRON')
  INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
    (@vstepCert, @vstepSpea, 'PRON',  N'Pronunciation', 10.0, 1.0, 1),
    (@vstepCert, @vstepSpea, 'FLU',   N'Fluency', 10.0, 1.0, 2),
    (@vstepCert, @vstepSpea, 'VOCAB', N'Vocabulary & Grammar', 10.0, 1.0, 3),
    (@vstepCert, @vstepSpea, 'DEV',   N'Topic Development', 10.0, 1.0, 4);
GO

PRINT '7. Grading rubrics seeded';
GO

-- ============================================================================
-- 8. Complete Exams of Every Purpose for Every Certificate (12 Exam Types)
-- ============================================================================

DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @ieltsCert INT = (SELECT CertId FROM Certificates WHERE Code = 'IELTS');
DECLARE @vstepCert INT = (SELECT CertId FROM Certificates WHERE Code = 'VSTEP');

DECLARE @toeicList INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'LIST');
DECLARE @toeicRead INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'READ');
DECLARE @toeicSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'SPEA');
DECLARE @toeicWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'WRIT');

DECLARE @ieltsList INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'LIST');
DECLARE @ieltsRead INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'READ');
DECLARE @ieltsSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'SPEA');
DECLARE @ieltsWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'WRIT');

DECLARE @vstepList INT = (SELECT SkillId FROM Skills WHERE CertId = @vstepCert AND Code = 'LIST');
DECLARE @vstepRead INT = (SELECT SkillId FROM Skills WHERE CertId = @vstepCert AND Code = 'READ');
DECLARE @vstepSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @vstepCert AND Code = 'SPEA');
DECLARE @vstepWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @vstepCert AND Code = 'WRIT');

DECLARE @secP1 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P1');
DECLARE @secP2 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P2');
DECLARE @secP3 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P3');
DECLARE @secP4 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P4');
DECLARE @secP5 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P5');
DECLARE @secP6 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P6');
DECLARE @secP7 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P7');
DECLARE @secS1 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'S1');
DECLARE @secS2 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'S2');
DECLARE @secS3 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'S3');
DECLARE @secW1 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'W1');
DECLARE @secW2 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'W2');

DECLARE @secRec INT = (SELECT SectionId FROM Sections WHERE CertId = @ieltsCert AND Code = 'REC');
DECLARE @secPas INT = (SELECT SectionId FROM Sections WHERE CertId = @ieltsCert AND Code = 'PAS');
DECLARE @secT1  INT = (SELECT SectionId FROM Sections WHERE CertId = @ieltsCert AND Code = 'T1');
DECLARE @secT2  INT = (SELECT SectionId FROM Sections WHERE CertId = @ieltsCert AND Code = 'T2');
DECLARE @secInt INT = (SELECT SectionId FROM Sections WHERE CertId = @ieltsCert AND Code = 'INT');

DECLARE @secL3 INT = (SELECT SectionId FROM Sections WHERE CertId = @vstepCert AND Code = 'L3');
DECLARE @secR4 INT = (SELECT SectionId FROM Sections WHERE CertId = @vstepCert AND Code = 'R4');
DECLARE @secVW1 INT = (SELECT SectionId FROM Sections WHERE CertId = @vstepCert AND Code = 'W1');
DECLARE @secVW2 INT = (SELECT SectionId FROM Sections WHERE CertId = @vstepCert AND Code = 'W2');
DECLARE @secVS3 INT = (SELECT SectionId FROM Sections WHERE CertId = @vstepCert AND Code = 'S3');

DECLARE @catTense INT = (SELECT CategoryId FROM QuestionCategories WHERE CertId = @toeicCert AND Code = 'grammar_tense');
DECLARE @catPrep  INT = (SELECT CategoryId FROM QuestionCategories WHERE CertId = @toeicCert AND Code = 'grammar_prep');
DECLARE @catPos   INT = (SELECT CategoryId FROM QuestionCategories WHERE CertId = @toeicCert AND Code = 'grammar_pos');

-------------------------------------------------------------------------------
-- 8.1 TOEIC Exams (Mock, Placement, Speaking, Writing)
-------------------------------------------------------------------------------

-- 1. TOEIC Mock: ETS 2024 — Test 1
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS 2024 — Test 1')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@toeicCert, N'ETS 2024 — Test 1', 'published', 120, 'mock',
          N'A full TOEIC Listening & Reading form.', N'ETS 2024', 200);

DECLARE @toeicMock INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS 2024 — Test 1');

-- ExamSectionAudio for TOEIC Mock
IF NOT EXISTS (SELECT 1 FROM ExamSectionAudio WHERE ExamId = @toeicMock AND SectionId = @secP1)
  INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
  VALUES (@toeicMock, @secP1, @toeicCert, '/audio/toeic/test1-p1.mp3', 180, 2880000);
IF NOT EXISTS (SELECT 1 FROM ExamSectionAudio WHERE ExamId = @toeicMock AND SectionId = @secP2)
  INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
  VALUES (@toeicMock, @secP2, @toeicCert, '/audio/toeic/test1-p2.mp3', 300, 4800000);
IF NOT EXISTS (SELECT 1 FROM ExamSectionAudio WHERE ExamId = @toeicMock AND SectionId = @secP3)
  INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
  VALUES (@toeicMock, @secP3, @toeicCert, '/audio/toeic/test1-p3.mp3', 600, 9600000);
IF NOT EXISTS (SELECT 1 FROM ExamSectionAudio WHERE ExamId = @toeicMock AND SectionId = @secP4)
  INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
  VALUES (@toeicMock, @secP4, @toeicCert, '/audio/toeic/test1-p4.mp3', 600, 9600000);

-- Questions for TOEIC Mock: P1, P2, P3, P4, P5, P6, P7
IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicMock AND SectionId = @secP1)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath, ImagePath, ImageAlt)
  VALUES (@toeicMock, @toeicCert, @secP1, 1, '/audio/toeic/test1-p1.mp3', '/img/part1-sample.svg', N'Office workers gathered around a whiteboard');
  DECLARE @gP1 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gP1, @toeicMock, @secP1, @toeicList, N'Look at the photograph and choose the statement that best describes what you see in the image.', 2, 'mcq',
          N'Focus on what the person in the centre is holding and doing.', N'Option C correctly describes the action: the woman is writing notes on the whiteboard.', 1);
  DECLARE @qP1 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP1, 'mcq', 'A', N'They are leaving the meeting room.', 0),
    (@qP1, 'mcq', 'B', N'A laptop is being repaired on the desk.', 0),
    (@qP1, 'mcq', 'C', N'A woman is writing on a whiteboard.', 1),
    (@qP1, 'mcq', 'D', N'Some chairs are stacked against the wall.', 0);
END;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicMock AND SectionId = @secP2)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
  VALUES (@toeicMock, @toeicCert, @secP2, 2, '/audio/toeic/test1-p2.mp3');
  DECLARE @gP2 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gP2, @toeicMock, @secP2, @toeicList, N'Where did you leave the project folders?', 2, 'mcq',
          N'Listen for a location word that answers "Where".', N'Option B directly answers where the folders are placed.', 1);
  DECLARE @qP2 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP2, 'mcq', 'A', N'Yes, I folded them neatly.', 0),
    (@qP2, 'mcq', 'B', N'On the conference room table.', 1),
    (@qP2, 'mcq', 'C', N'No later than five o''clock.', 0);
END;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicMock AND SectionId = @secP3)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath, Transcript)
  VALUES (@toeicMock, @toeicCert, @secP3, 3, '/audio/toeic/test1-p3.mp3',
          N'Woman: Good morning, Mr. Evans. Did the catering team confirm the order for Friday''s banquet?\nMan: Yes, they did, but they said they need our final head count by tomorrow noon.\nWoman: I will email the department heads right away to get their RSVPs.');
  DECLARE @gP3 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gP3, @toeicMock, @secP3, @toeicList, N'What deadline does the catering team have for the banquet?', 3, 'mcq',
          N'Pay attention to what the man says about when they need the count.', N'The man says they need the final head count by tomorrow noon.', 1);
  DECLARE @qP3 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP3, 'mcq', 'A', N'By the end of today', 0),
    (@qP3, 'mcq', 'B', N'By tomorrow noon', 1),
    (@qP3, 'mcq', 'C', N'On Friday morning', 0),
    (@qP3, 'mcq', 'D', N'Next Monday afternoon', 0);
END;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicMock AND SectionId = @secP4)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath, Transcript)
  VALUES (@toeicMock, @toeicCert, @secP4, 4, '/audio/toeic/test1-p4.mp3',
          N'Attention all passengers on flight VN 214 to Tokyo. Due to routine aircraft inspection, boarding will be delayed by approximately twenty minutes. Please remain seated near Gate 12 until boarding begins.');
  DECLARE @gP4 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gP4, @toeicMock, @secP4, @toeicList, N'What is the reason for the flight delay?', 2, 'mcq',
          N'Listen for the phrase following "Due to".', N'The speaker explicitly announces that the delay is due to routine aircraft inspection.', 1);
  DECLARE @qP4 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP4, 'mcq', 'A', N'Adverse weather conditions', 0),
    (@qP4, 'mcq', 'B', N'Routine aircraft inspection', 1),
    (@qP4, 'mcq', 'C', N'A gate reassignment', 0),
    (@qP4, 'mcq', 'D', N'Crew scheduling changes', 0);
END;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicMock AND SectionId = @secP5)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
  VALUES (@toeicMock, @toeicCert, @secP5, 5);
  DECLARE @gP5 INT = SCOPE_IDENTITY();

  -- Q1: Verb tense
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gP5, @toeicMock, @secP5, @toeicRead, @catTense,
          N'The regional manager ------- the quarterly figures before the board meeting.',
          3, 'mcq', N'Look at the time marker and sentence structure.',
          N'The sentence lacks a main verb. "will review" expresses future scheduled action.', 1);
  DECLARE @qP5_1 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP5_1, 'mcq', 'A', N'review', 0),
    (@qP5_1, 'mcq', 'B', N'reviewing', 0),
    (@qP5_1, 'mcq', 'C', N'will review', 1),
    (@qP5_1, 'mcq', 'D', N'to review', 0);

  -- Q2: Preposition
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gP5, @toeicMock, @secP5, @toeicRead, @catPrep,
          N'All client inquiries must be resolved ------- forty-eight hours of submission.',
          2, 'mcq', N'Which preposition indicates within a time span?',
          N'"within" + time duration indicates action must take place before that span elapses.', 2);
  DECLARE @qP5_2 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP5_2, 'mcq', 'A', N'in', 0),
    (@qP5_2, 'mcq', 'B', N'during', 0),
    (@qP5_2, 'mcq', 'C', N'along', 0),
    (@qP5_2, 'mcq', 'D', N'within', 1);

  -- Q3: Part of speech
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gP5, @toeicMock, @secP5, @toeicRead, @catPos,
          N'The design studio provides ------- solutions for modern office spaces.',
          3, 'mcq', N'The blank modifies the plural noun "solutions".',
          N'"innovative" is the adjective modifying "solutions".', 3);
  DECLARE @qP5_3 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP5_3, 'mcq', 'A', N'innovation', 0),
    (@qP5_3, 'mcq', 'B', N'innovative', 1),
    (@qP5_3, 'mcq', 'C', N'innovatively', 0),
    (@qP5_3, 'mcq', 'D', N'innovate', 0);
END;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicMock AND SectionId = @secP6)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@toeicMock, @toeicCert, @secP6, 6,
          N'Dear Valued Customer, We ------- your recent order and your package is being prepared for shipment.');
  DECLARE @gP6 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gP6, @toeicMock, @secP6, @toeicRead, @catTense,
          N'Choose the word or phrase that best completes the sentence in the customer letter.',
          2, 'mcq', N'The action happened recently with present relevance.',
          N'The present perfect "have received" describes an action that occurred recently with present impact.', 1);
  DECLARE @qP6 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP6, 'mcq', 'A', N'have received', 1),
    (@qP6, 'mcq', 'B', N'will receive', 0),
    (@qP6, 'mcq', 'C', N'are receiving', 0),
    (@qP6, 'mcq', 'D', N'to receive', 0);
END;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicMock AND SectionId = @secP7)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@toeicMock, @toeicCert, @secP7, 7,
          N'MEMORANDUM\nTo: All Branch Employees\nFrom: Facilities Management\nDate: October 14\nSubject: Elevator Maintenance\nPlease note that the main elevators will undergo safety upgrades this Saturday between 8:00 A.M. and 2:00 P.M. The service elevator near the loading dock will remain accessible throughout this period.');
  DECLARE @gP7 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gP7, @toeicMock, @secP7, @toeicRead,
          N'What is the primary purpose of the memorandum?', 2, 'mcq', N'Look at the subject line and the main announcement.',
          N'The memo announces temporary elevator upgrades and directs staff to an alternate service elevator.', 1);
  DECLARE @qP7 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP7, 'mcq', 'A', N'To announce staff promotions', 0),
    (@qP7, 'mcq', 'B', N'To notify employees of maintenance work', 1),
    (@qP7, 'mcq', 'C', N'To schedule an annual fire drill', 0),
    (@qP7, 'mcq', 'D', N'To request feedback on office amenities', 0);
END;

-- ScoreScales for TOEIC Mock
IF NOT EXISTS (SELECT 1 FROM ScoreScales WHERE CertId = @toeicCert AND ExamId = @toeicMock)
BEGIN
  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
    (@toeicCert, @toeicList, @toeicMock, 0, 5),
    (@toeicCert, @toeicList, @toeicMock, 1, 150),
    (@toeicCert, @toeicList, @toeicMock, 2, 260),
    (@toeicCert, @toeicList, @toeicMock, 3, 380),
    (@toeicCert, @toeicList, @toeicMock, 4, 495),
    (@toeicCert, @toeicRead, @toeicMock, 0, 5),
    (@toeicCert, @toeicRead, @toeicMock, 1, 175),
    (@toeicCert, @toeicRead, @toeicMock, 2, 340),
    (@toeicCert, @toeicRead, @toeicMock, 3, 495);
END;

-- 2. TOEIC Placement: Standard (45 min) and Quick (20 min)
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @toeicCert AND Name = N'TOEIC Placement Test — Standard')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@toeicCert, N'TOEIC Placement Test — Standard', 'published', 45, 'placement',
          N'Comprehensive diagnostic covering Listening and Reading. Calibrates your starting CEFR band and personalizes your study plan.',
          N'ETS Diagnostic 2024', 4);

DECLARE @toeicStdPlace INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'TOEIC Placement Test — Standard');

IF NOT EXISTS (SELECT 1 FROM ExamSectionAudio WHERE ExamId = @toeicStdPlace AND SectionId = @secP1)
  INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
  VALUES (@toeicStdPlace, @secP1, @toeicCert, '/audio/toeic/placement-p1.mp3', 120, 1920000);
IF NOT EXISTS (SELECT 1 FROM ExamSectionAudio WHERE ExamId = @toeicStdPlace AND SectionId = @secP2)
  INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
  VALUES (@toeicStdPlace, @secP2, @toeicCert, '/audio/toeic/placement-p2.mp3', 180, 2880000);

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicStdPlace AND SectionId = @secP1)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath, ImagePath, ImageAlt)
  VALUES (@toeicStdPlace, @toeicCert, @secP1, 1, '/audio/toeic/placement-p1.mp3', '/img/part1-sample.svg', N'Office workers in meeting');
  DECLARE @gStd1 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gStd1, @toeicStdPlace, @secP1, @toeicList, N'Look at the picture and select the best statement describing what you see.',
          2, 'mcq', N'Notice the people sitting around the table.', N'Option A correctly describes people seated around a conference table.', 1);
  DECLARE @qStd1 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qStd1, 'mcq', 'A', N'People are seated around a conference table.', 1),
    (@qStd1, 'mcq', 'B', N'They are unpacking boxes in a warehouse.', 0),
    (@qStd1, 'mcq', 'C', N'A presenter is leaving the room.', 0),
    (@qStd1, 'mcq', 'D', N'All the chairs are unoccupied.', 0);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
  VALUES (@toeicStdPlace, @toeicCert, @secP2, 2, '/audio/toeic/placement-p2.mp3');
  DECLARE @gStd2 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
  VALUES (@gStd2, @toeicStdPlace, @secP2, @toeicList, N'Where is the quarterly performance report?', 2, 'mcq',
          N'Option B directly answers the question about the report location.', 2);
  DECLARE @qStd2 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qStd2, 'mcq', 'A', N'Yes, every three months.', 0),
    (@qStd2, 'mcq', 'B', N'It is on Ms. Chen''s desk.', 1),
    (@qStd2, 'mcq', 'C', N'No, I haven''t seen him.', 0);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
  VALUES (@toeicStdPlace, @toeicCert, @secP5, 3);
  DECLARE @gStd3 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
  VALUES (@gStd3, @toeicStdPlace, @secP5, @toeicRead,
          N'Customer satisfaction surveys must be submitted ------- Friday afternoon to be included in the quarterly review.',
          2, 'mcq', N'Preposition of time "before" indicates the deadline.', 3);
  DECLARE @qStd3 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qStd3, 'mcq', 'A', N'before', 1),
    (@qStd3, 'mcq', 'B', N'during', 0),
    (@qStd3, 'mcq', 'C', N'between', 0),
    (@qStd3, 'mcq', 'D', N'among', 0);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@toeicStdPlace, @toeicCert, @secP6, 4,
          N'Thank you for contacting Apex Support. We have received your inquiry regarding account renewal. A representative will review your request and ------- within 24 business hours.');
  DECLARE @gStd4 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
  VALUES (@gStd4, @toeicStdPlace, @secP6, @toeicRead, N'Choose the correct verb form to complete the sentence.',
          2, 'mcq', N'The modal auxiliary "will" requires the base form of the verb "respond".', 4);
  DECLARE @qStd4 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qStd4, 'mcq', 'A', N'respond', 1),
    (@qStd4, 'mcq', 'B', N'response', 0),
    (@qStd4, 'mcq', 'C', N'responded', 0),
    (@qStd4, 'mcq', 'D', N'responding', 0);

  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
    (@toeicCert, @toeicList, @toeicStdPlace, 0, 100),
    (@toeicCert, @toeicList, @toeicStdPlace, 1, 250),
    (@toeicCert, @toeicList, @toeicStdPlace, 2, 450),
    (@toeicCert, @toeicRead, @toeicStdPlace, 0, 100),
    (@toeicCert, @toeicRead, @toeicStdPlace, 1, 200),
    (@toeicCert, @toeicRead, @toeicStdPlace, 2, 440);
END;

-- Quick placement
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @toeicCert AND Name = N'TOEIC Placement Test — Quick')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@toeicCert, N'TOEIC Placement Test — Quick', 'published', 20, 'placement',
          N'Fast 20-minute diagnostic to quickly estimate your starting level across core listening and reading skills.',
          N'ETS Diagnostic 2024', 2);
  DECLARE @qExam INT = SCOPE_IDENTITY();

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
  VALUES (@qExam, @toeicCert, @secP2, 1, '/audio/toeic/placement-quick.mp3');
  DECLARE @gQ1 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
  VALUES (@gQ1, @qExam, @secP2, @toeicList, N'Who is responsible for booking the meeting room?', 1, 'mcq',
          N'Option C identifies the person responsible.', 1);
  DECLARE @qQ1 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qQ1, 'mcq', 'A', N'At 2:00 PM.', 0),
    (@qQ1, 'mcq', 'B', N'In Conference Room B.', 0),
    (@qQ1, 'mcq', 'C', N'Ms. Tanaka from HR.', 1);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
  VALUES (@qExam, @toeicCert, @secP5, 2);
  DECLARE @gQ2 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
  VALUES (@gQ2, @qExam, @secP5, @toeicRead,
          N'The new software update will ------- improve database query performance across all branch offices.',
          2, 'mcq', N'Adverb "significantly" modifies the verb "improve".', 2);
  DECLARE @qQ2 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qQ2, 'mcq', 'A', N'significant', 0),
    (@qQ2, 'mcq', 'B', N'significantly', 1),
    (@qQ2, 'mcq', 'C', N'significance', 0),
    (@qQ2, 'mcq', 'D', N'signified', 0);

  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
    (@toeicCert, @toeicList, @qExam, 0, 150),
    (@toeicCert, @toeicList, @qExam, 1, 350),
    (@toeicCert, @toeicRead, @qExam, 0, 150),
    (@toeicCert, @toeicRead, @qExam, 1, 350);
END;

-- 3. TOEIC Speaking Practice: ETS TOEIC Speaking Practice Exam 1
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS TOEIC Speaking Practice Exam 1')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@toeicCert, N'ETS TOEIC Speaking Practice Exam 1', 'published', 20, 'speaking_practice',
          N'Workplace English speaking practice with read-aloud, image description, and question response.',
          N'ETS Official 2024', 3);
  DECLARE @speaExam INT = SCOPE_IDENTITY();

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@speaExam, @toeicCert, @secS1, 1,
          N'Attention all shoppers. The annual summer clearance sale will end today at 9:00 PM. Please bring all selected items to the front registers.');
  DECLARE @gS1 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
  VALUES (@gS1, @speaExam, @secS1, @toeicSpea, N'Read the announcement aloud as clearly and naturally as possible.', 2, 'free_response', 45, 45, 1);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, ImagePath, ImageAlt)
  VALUES (@speaExam, @toeicCert, @secS2, 2, '/img/part1-sample.svg', N'Office workers in a team meeting');
  DECLARE @gS2 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
  VALUES (@gS2, @speaExam, @secS2, @toeicSpea, N'Look at the picture and describe it in as much detail as you can.', 2, 'free_response', 45, 30, 2);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
  VALUES (@speaExam, @toeicCert, @secS3, 3);
  DECLARE @gS3 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
  VALUES (@gS3, @speaExam, @secS3, @toeicSpea, N'How often do you go shopping for clothes, and what is your favorite clothing store?', 3, 'free_response', 3, 15, 3);
END;

-- 4. TOEIC Writing Practice: ETS TOEIC Writing Practice Test 1
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS TOEIC Writing Practice Test 1')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@toeicCert, N'ETS TOEIC Writing Practice Test 1', 'published', 60, 'writing_practice',
          N'Workplace English writing practice: sentence writing and opinion essay.',
          N'ETS Official 2024', 2);
  DECLARE @writExam INT = SCOPE_IDENTITY();

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, ImagePath, ImageAlt)
  VALUES (@writExam, @toeicCert, @secW1, 1, '/img/part1-sample.svg', N'Meeting in conference room');
  DECLARE @gW1 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
  VALUES (@gW1, @writExam, @secW1, @toeicWrit,
          N'Write ONE sentence based on the picture using two given words: "discuss" and "proposal".', 2, 'free_response', 8, 35, 1);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@writExam, @toeicCert, @secW2, 2,
          N'Some companies offer employees the option of flexible working hours. In your opinion, what are the advantages and disadvantages of flexible working schedules?');
  DECLARE @gW2 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
  VALUES (@gW2, @writExam, @secW2, @toeicWrit,
          N'Write an opinion essay of at least 250 words explaining your viewpoint with specific reasons and examples.', 4, 'free_response', 250, 450, 2);
END;

-------------------------------------------------------------------------------
-- 8.2 IELTS Exams (Mock, Placement, Speaking, Writing)
-------------------------------------------------------------------------------

-- 1. IELTS Mock: Cambridge IELTS 18 — Academic Test 1
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @ieltsCert AND Name = N'Cambridge IELTS 18 — Academic Test 1')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@ieltsCert, N'Cambridge IELTS 18 — Academic Test 1', 'published', 170, 'mock',
          N'Academic Listening, Reading, Writing and Speaking form.', N'Cambridge 18', 40);

DECLARE @ieltsMock INT = (SELECT ExamId FROM Exams WHERE CertId = @ieltsCert AND Name = N'Cambridge IELTS 18 — Academic Test 1');

IF NOT EXISTS (SELECT 1 FROM ExamSectionAudio WHERE ExamId = @ieltsMock AND SectionId = @secRec)
  INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
  VALUES (@ieltsMock, @secRec, @ieltsCert, '/audio/ielts/cambridge18-t1-listening.mp3', 1800, 28800000);

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @ieltsMock AND SectionId = @secRec)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath, Transcript)
  VALUES (@ieltsMock, @ieltsCert, @secRec, 1, '/audio/ielts/cambridge18-t1-listening.mp3',
          N'Guide: Welcome to the University Library induction. Daily guided tours depart every half hour from the information desk starting at 9:30 AM.');
  DECLARE @gRec INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gRec, @ieltsMock, @secRec, @ieltsList, N'At what time does the first library induction tour depart?', 2, 'mcq',
          N'Listen for the starting time mentioned by the guide.', N'The guide states that the tours start at 9:30 AM.', 1);
  DECLARE @qRec INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qRec, 'mcq', 'A', N'9:00 AM', 0),
    (@qRec, 'mcq', 'B', N'9:30 AM', 1),
    (@qRec, 'mcq', 'C', N'10:00 AM', 0),
    (@qRec, 'mcq', 'D', N'10:30 AM', 0);
END;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @ieltsMock AND SectionId = @secPas)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@ieltsMock, @ieltsCert, @secPas, 2,
          N'Urban reforestation initiatives have demonstrated measurable cooling effects in metropolitan centres. Studies in Singapore and Seoul indicate that rooftop gardens and street canopy trees reduce localized surface temperatures by up to 2.8 degrees Celsius during peak summer months.');
  DECLARE @gPas INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gPas, @ieltsMock, @secPas, @ieltsRead,
          N'According to the passage, by how much can urban surface temperatures be reduced through green canopies?',
          3, 'mcq', N'Scan the passage for specific numbers and degrees Celsius.',
          N'The text explicitly mentions a reduction of up to 2.8 degrees Celsius.', 1);
  DECLARE @qPas INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qPas, 'mcq', 'A', N'Up to 1.5 °C', 0),
    (@qPas, 'mcq', 'B', N'Up to 2.8 °C', 1),
    (@qPas, 'mcq', 'C', N'Up to 3.5 °C', 0),
    (@qPas, 'mcq', 'D', N'Up to 4.2 °C', 0);
END;

IF NOT EXISTS (SELECT 1 FROM ScoreScales WHERE CertId = @ieltsCert AND ExamId = @ieltsMock)
BEGIN
  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
    (@ieltsCert, @ieltsList, @ieltsMock, 0, 20),
    (@ieltsCert, @ieltsList, @ieltsMock, 1, 40),
    (@ieltsCert, @ieltsRead, @ieltsMock, 0, 20),
    (@ieltsCert, @ieltsRead, @ieltsMock, 1, 40);
END;

-- 2. IELTS Placement: Cambridge IELTS — Placement Diagnostic
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @ieltsCert AND Name = N'Cambridge IELTS — Placement Diagnostic')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@ieltsCert, N'Cambridge IELTS — Placement Diagnostic', 'published', 45, 'placement',
          N'Diagnostic assessment to calibrate your baseline IELTS band across listening and reading.',
          N'Cambridge Assessment', 4);
  DECLARE @ieltsPlace INT = SCOPE_IDENTITY();

  INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
  VALUES (@ieltsPlace, @secRec, @ieltsCert, '/audio/ielts/placement-diagnostic.mp3', 600, 9600000);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath, Transcript)
  VALUES (@ieltsPlace, @ieltsCert, @secRec, 1, '/audio/ielts/placement-diagnostic.mp3',
          N'Officer: The student housing office is open Monday to Thursday from 9 AM to 4 PM. On Fridays we close at 1 PM for staff training.');
  DECLARE @gIP1 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
  VALUES (@gIP1, @ieltsPlace, @secRec, @ieltsList, N'What time does the student housing office close on Fridays?', 2, 'mcq',
          N'The officer says: "On Fridays we close at 1 PM".', 1);
  DECLARE @qIP1 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qIP1, 'mcq', 'A', N'12:00 PM', 0),
    (@qIP1, 'mcq', 'B', N'1:00 PM', 1),
    (@qIP1, 'mcq', 'C', N'4:00 PM', 0),
    (@qIP1, 'mcq', 'D', N'5:00 PM', 0);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@ieltsPlace, @ieltsCert, @secPas, 2,
          N'Renewable energy capacity expanded by 50% in 2023 worldwide, with solar photovoltaics accounting for three-quarters of new installations globally.');
  DECLARE @gIP2 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
  VALUES (@gIP2, @ieltsPlace, @secPas, @ieltsRead, N'What energy source accounted for the majority of new capacity?', 2, 'mcq',
          N'The passage states solar photovoltaics accounted for three-quarters of installations.', 2);
  DECLARE @qIP2 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qIP2, 'mcq', 'A', N'Wind power', 0),
    (@qIP2, 'mcq', 'B', N'Solar photovoltaics', 1),
    (@qIP2, 'mcq', 'C', N'Hydroelectricity', 0),
    (@qIP2, 'mcq', 'D', N'Geothermal energy', 0);

  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
    (@ieltsCert, @ieltsList, @ieltsPlace, 0, 30),
    (@ieltsCert, @ieltsList, @ieltsPlace, 1, 60),
    (@ieltsCert, @ieltsRead, @ieltsPlace, 0, 30),
    (@ieltsCert, @ieltsRead, @ieltsPlace, 1, 60);
END;

-- 3. IELTS Speaking Practice: Cambridge IELTS 18 — Speaking Practice Test 1
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @ieltsCert AND Name = N'Cambridge IELTS 18 — Speaking Practice Test 1')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@ieltsCert, N'Cambridge IELTS 18 — Speaking Practice Test 1', 'published', 15, 'speaking_practice',
          N'Complete Academic Speaking test with Part 1 interview questions and Part 2 long-turn cue card topic.',
          N'Cambridge 18', 2);
  DECLARE @ieltsSpeaExam INT = SCOPE_IDENTITY();

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
  VALUES (@ieltsSpeaExam, @ieltsCert, @secInt, 1);
  DECLARE @gIS1 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
  VALUES (@gIS1, @ieltsSpeaExam, @secInt, @ieltsSpea,
          N'Part 1: Do you work or are you a student? What do you enjoy most about your studies or job?', 2, 'free_response', 0, 60, 1);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@ieltsSpeaExam, @ieltsCert, @secInt, 2,
          N'Describe a piece of technology you find difficult to use.\nYou should say:\n- What it is\n- When you got it\n- What you use it for\nAnd explain why you find it difficult to use.');
  DECLARE @gIS2 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
  VALUES (@gIS2, @ieltsSpeaExam, @secInt, @ieltsSpea,
          N'Part 2: Speak for 1 to 2 minutes on the cue card topic. You have 1 minute to prepare.', 4, 'free_response', 60, 120, 2);
END;

-- 4. IELTS Writing Practice: Cambridge IELTS 18 — Writing Practice Test 1
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @ieltsCert AND Name = N'Cambridge IELTS 18 — Writing Practice Test 1')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@ieltsCert, N'Cambridge IELTS 18 — Writing Practice Test 1', 'published', 60, 'writing_practice',
          N'Complete Academic Writing test with Task 1 (visual chart report) and Task 2 (argumentative essay). Scored with AI against official IELTS criteria.',
          N'Cambridge 18', 2);
  DECLARE @writingExam INT = SCOPE_IDENTITY();

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@writingExam, @ieltsCert, @secT1, 1,
          N'The chart below shows the percentage of households with internet access in three European countries between 2010 and 2020.' + CHAR(10) +
          N'Summarise the information by selecting and reporting the main features, and make comparisons where relevant.');
  DECLARE @grpT1 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
  VALUES (@grpT1, @writingExam, @secT1, @ieltsWrit,
          N'Write at least 150 words summarizing the main features and comparisons in the chart.',
          3, 'free_response', 150, NULL, 1);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@writingExam, @ieltsCert, @secT2, 2,
          N'Some people believe that unpaid community service should be a compulsory part of high school programmes (such as working for a charity, improving the neighborhood, or coaching younger children).' + CHAR(10) +
          N'To what extent do you agree or disagree? Give reasons for your answer and include any relevant examples from your own knowledge or experience.');
  DECLARE @grpT2 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
  VALUES (@grpT2, @writingExam, @secT2, @ieltsWrit,
          N'Write an essay of at least 250 words discussing your opinion with supporting arguments.',
          4, 'free_response', 250, NULL, 2);
END;

-------------------------------------------------------------------------------
-- 8.3 VSTEP Exams (Mock, Placement, Speaking, Writing)
-------------------------------------------------------------------------------

-- 1. VSTEP Mock: VSTEP Sample Test 1
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @vstepCert AND Name = N'VSTEP Sample Test 1')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@vstepCert, N'VSTEP Sample Test 1', 'published', 180, 'mock',
          N'Four-skill national examination form.', N'VSTEP 2024', 75);

DECLARE @vstepMock INT = (SELECT ExamId FROM Exams WHERE CertId = @vstepCert AND Name = N'VSTEP Sample Test 1');

IF NOT EXISTS (SELECT 1 FROM ExamSectionAudio WHERE ExamId = @vstepMock AND SectionId = @secL3)
  INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
  VALUES (@vstepMock, @secL3, @vstepCert, '/audio/vstep/sample1-listening.mp3', 2400, 38400000);

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @vstepMock AND SectionId = @secL3)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath, Transcript)
  VALUES (@vstepMock, @vstepCert, @secL3, 1, '/audio/vstep/sample1-listening.mp3',
          N'Speaker: Good afternoon listeners. The annual Da Nang International Fireworks Festival will open next month on June 8th along the Han River.');
  DECLARE @gL3 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gL3, @vstepMock, @secL3, @vstepList, N'On what date will the festival open?', 2, 'mcq',
          N'Listen for the exact month and date mentioned by the speaker.', N'The speaker explicitly states June 8th.', 1);
  DECLARE @qL3 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qL3, 'mcq', 'A', N'June 1st', 0),
    (@qL3, 'mcq', 'B', N'June 8th', 1),
    (@qL3, 'mcq', 'C', N'June 15th', 0),
    (@qL3, 'mcq', 'D', N'July 8th', 0);
END;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @vstepMock AND SectionId = @secR4)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@vstepMock, @vstepCert, @secR4, 2,
          N'Community-based tourism has expanded significantly in the northern mountainous provinces of Vietnam. Local homestays in Ha Giang and Yen Bai allow visitors to experience ethnic cultural traditions while generating sustainable income for rural families.');
  DECLARE @gR4 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@gR4, @vstepMock, @secR4, @vstepRead,
          N'What is a primary benefit of community-based tourism described in the text?', 2, 'mcq',
          N'Look at the final sentence describing the effect for rural families.',
          N'The passage notes it provides sustainable income for local families while sharing cultural traditions.', 1);
  DECLARE @qR4 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qR4, 'mcq', 'A', N'Rapid industrialization of rural areas', 0),
    (@qR4, 'mcq', 'B', N'Sustainable income for local families', 1),
    (@qR4, 'mcq', 'C', N'Construction of new international airports', 0),
    (@qR4, 'mcq', 'D', N'Government subsidies for tour operators', 0);
END;

IF NOT EXISTS (SELECT 1 FROM ScoreScales WHERE CertId = @vstepCert AND ExamId = @vstepMock)
BEGIN
  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
    (@vstepCert, @vstepList, @vstepMock, 0, 20),
    (@vstepCert, @vstepList, @vstepMock, 1, 35),
    (@vstepCert, @vstepRead, @vstepMock, 0, 20),
    (@vstepCert, @vstepRead, @vstepMock, 1, 35);
END;

-- 2. VSTEP Placement: VSTEP Placement Test — Diagnostic
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @vstepCert AND Name = N'VSTEP Placement Test — Diagnostic')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@vstepCert, N'VSTEP Placement Test — Diagnostic', 'published', 45, 'placement',
          N'Diagnostic assessment to determine your baseline VSTEP proficiency level (B1, B2, or C1).',
          N'VSTEP Center', 4);
  DECLARE @vstepPlace INT = SCOPE_IDENTITY();

  INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
  VALUES (@vstepPlace, @secL3, @vstepCert, '/audio/vstep/placement-diagnostic.mp3', 600, 9600000);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath, Transcript)
  VALUES (@vstepPlace, @vstepCert, @secL3, 1, '/audio/vstep/placement-diagnostic.mp3',
          N'Lecturer: Please submit your research proposals by Friday 5 PM through the faculty portal.');
  DECLARE @gVP1 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
  VALUES (@gVP1, @vstepPlace, @secL3, @vstepList, N'How should students submit their research proposals?', 2, 'mcq',
          N'The lecturer specifies "through the faculty portal".', 1);
  DECLARE @qVP1 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qVP1, 'mcq', 'A', N'Printed copy to the office', 0),
    (@qVP1, 'mcq', 'B', N'Through the faculty portal', 1),
    (@qVP1, 'mcq', 'C', N'By email to the teaching assistant', 0),
    (@qVP1, 'mcq', 'D', N'During Monday class', 0);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@vstepPlace, @vstepCert, @secR4, 2,
          N'Artificial intelligence is being integrated into medical imaging across hospital networks, providing diagnostic assistance for radiology scans.');
  DECLARE @gVP2 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
  VALUES (@gVP2, @vstepPlace, @secR4, @vstepRead, N'What role does artificial intelligence play in medical imaging according to the passage?', 2, 'mcq',
          N'The passage states AI provides diagnostic assistance for radiology scans.', 2);
  DECLARE @qVP2 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qVP2, 'mcq', 'A', N'Replacing hospital doctors completely', 0),
    (@qVP2, 'mcq', 'B', N'Providing diagnostic assistance', 1),
    (@qVP2, 'mcq', 'C', N'Manufacturing hospital equipment', 0),
    (@qVP2, 'mcq', 'D', N'Managing billing departments', 0);

  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
    (@vstepCert, @vstepList, @vstepPlace, 0, 30),
    (@vstepCert, @vstepList, @vstepPlace, 1, 60),
    (@vstepCert, @vstepRead, @vstepPlace, 0, 30),
    (@vstepCert, @vstepRead, @vstepPlace, 1, 60);
END;

-- 3. VSTEP Speaking Practice: VSTEP Speaking Practice Test 1
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @vstepCert AND Name = N'VSTEP Speaking Practice Test 1')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@vstepCert, N'VSTEP Speaking Practice Test 1', 'published', 15, 'speaking_practice',
          N'Complete VSTEP Speaking practice with social interaction and topic presentation.',
          N'VSTEP Center', 2);
  DECLARE @vstepSpeaExam INT = SCOPE_IDENTITY();

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
  VALUES (@vstepSpeaExam, @vstepCert, @secVS3, 1);
  DECLARE @gVS1 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
  VALUES (@gVS1, @vstepSpeaExam, @secVS3, @vstepSpea,
          N'Part 1: Social Interaction: Talk about your favorite leisure activity. Why do you enjoy it?', 2, 'free_response', 0, 60, 1);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@vstepSpeaExam, @vstepCert, @secVS3, 2,
          N'Topic: Should universities make physical education courses compulsory for all undergraduate students?\n- Health benefits\n- Academic stress relief\n- Student freedom of choice');
  DECLARE @gVS2 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
  VALUES (@gVS2, @vstepSpeaExam, @secVS3, @vstepSpea,
          N'Part 2: Topic Development: Present your opinion in 3 minutes based on the prompt.', 3, 'free_response', 60, 180, 2);
END;

-- 4. VSTEP Writing Practice: VSTEP Writing Practice Test 1
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @vstepCert AND Name = N'VSTEP Writing Practice Test 1')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@vstepCert, N'VSTEP Writing Practice Test 1', 'published', 60, 'writing_practice',
          N'Complete VSTEP Writing practice with Task 1 (informal/formal letter) and Task 2 (essay).',
          N'VSTEP Center', 2);
  DECLARE @vstepWritExam INT = SCOPE_IDENTITY();

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@vstepWritExam, @vstepCert, @secVW1, 1,
          N'You received an email from an international friend asking for recommendations on historical places to visit in Hanoi.\nWrite an email of at least 120 words suggesting two places, explaining why they are interesting, and giving advice on when to visit.');
  DECLARE @gVW1 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
  VALUES (@gVW1, @vstepWritExam, @secVW1, @vstepWrit,
          N'Write an email responding to your friend (at least 120 words).', 3, 'free_response', 120, 250, 1);

  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@vstepWritExam, @vstepCert, @secVW2, 2,
          N'Some people think that online learning will eventually replace traditional classrooms. To what extent do you agree or disagree?');
  DECLARE @gVW2 INT = SCOPE_IDENTITY();
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
  VALUES (@gVW2, @vstepWritExam, @secVW2, @vstepWrit,
          N'Write an essay of at least 250 words discussing both sides and giving your opinion with reasons.', 4, 'free_response', 250, 450, 2);
END;
GO

PRINT '8. Exams of every purpose across all certificates seeded';
GO

-- ============================================================================
-- 9. Placement Test & Target Band for learner@demo.local
-- ============================================================================
DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @stdExam INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'TOEIC Placement Test — Standard');

-- Target band B2 (785)
IF NOT EXISTS (SELECT 1 FROM TargetBands WHERE UserId = @learner AND CertId = @toeicCert)
  INSERT INTO TargetBands (UserId, CertId, TargetBandCode)
  VALUES (@learner, @toeicCert, 'B2');

-- Completed Placement Test (Total score 650, Band B1)
IF NOT EXISTS (SELECT 1 FROM PlacementTests WHERE UserId = @learner AND CertId = @toeicCert AND Status = 'done')
BEGIN
  INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
  VALUES (@learner, @stdExam, @toeicCert, 'graded',
          DATEADD(DAY, -22, SYSDATETIMEOFFSET()), DATEADD(DAY, -22, DATEADD(HOUR, 1, SYSDATETIMEOFFSET())),
          DATEADD(DAY, -22, DATEADD(MINUTE, 38, SYSDATETIMEOFFSET())), 650, 'B1');
  DECLARE @attPlace INT = SCOPE_IDENTITY();

  INSERT INTO PlacementTests (UserId, CertId, ExamId, Status, ResultBandCode, AttemptId, TotalScore, TakenAt)
  VALUES (@learner, @toeicCert, @stdExam, 'done', 'B1', @attPlace, 650, DATEADD(DAY, -22, SYSDATETIMEOFFSET()));
END;
GO

PRINT '9. Placement test and target band seeded';
GO

-- ============================================================================
-- 10. Learning Path Templates & Active Learner Path
-- ============================================================================
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @toeicList INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'LIST');
DECLARE @toeicRead INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'READ');
DECLARE @secP3 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P3');
DECLARE @secP5 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P5');
DECLARE @secP6 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P6');
DECLARE @speaExam INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS TOEIC Speaking Practice Exam 1');
DECLARE @catTense INT = (SELECT CategoryId FROM QuestionCategories WHERE CertId = @toeicCert AND Code = 'grammar_tense');

IF NOT EXISTS (SELECT 1 FROM PathTemplates WHERE CertId = @toeicCert AND FromBandCode = 'B1' AND ToBandCode = 'B2')
BEGIN
  INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
  VALUES (@toeicCert, 'B1', 'B2', N'TOEIC B1 → B2 Intermediate to Upper-Intermediate Path');
  DECLARE @tplId INT = SCOPE_IDENTITY();

  -- Module 1: Incomplete Sentences Grammar Mastery
  INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, GroupName, Description, EstimatedMinutes)
  VALUES (@tplId, @toeicRead, @toeicCert, 1, N'Module 1: Incomplete Sentences Grammar Mastery',
          N'Grammar foundations · modules 1–2',
          N'Master high-frequency Part 5 patterns: verb tenses, voice, and agreement.', 20);
  DECLARE @m1 INT = SCOPE_IDENTITY();
  INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
  VALUES (@m1, @toeicCert, @tplId, 'practice_section', @secP5, 20, 1);
  INSERT INTO PathModuleTopics (ModuleId, Title, CategoryId, QuestionCount, DisplayOrder) VALUES
    (@m1, N'Subject-verb agreement and tenses', @catTense, 10, 1);

  -- Module 2: Sentence Structure & AI Speaking Drill
  INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, GroupName, Description, EstimatedMinutes)
  VALUES (@tplId, @toeicRead, @toeicCert, 2, N'Module 2: Sentence Structure & AI Speaking Drill',
          N'Grammar foundations · modules 1–2',
          N'Apply grammar structures in spoken English and written context.', 25);
  DECLARE @m2 INT = SCOPE_IDENTITY();
  INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
  VALUES (@m2, @toeicCert, @tplId, 'practice_section', @secP5, 25, 1);
  IF @speaExam IS NOT NULL
  BEGIN
    INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefExamId, QuestionCount, IsPremiumOnly, DisplayOrder)
    VALUES (@m2, @toeicCert, @tplId, 'ai_task', @speaExam, 3, 1, 2);
  END;

  -- Module 3: Workplace Reading & Text Completion
  INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, GroupName, Description, EstimatedMinutes)
  VALUES (@tplId, @toeicRead, @toeicCert, 3, N'Module 3: Workplace Reading & Text Completion',
          N'Reading & listening · modules 3–4',
          N'Master Part 6 text completion and business email vocabulary.', 30);
  DECLARE @m3 INT = SCOPE_IDENTITY();
  INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
  VALUES (@m3, @toeicCert, @tplId, 'practice_section', @secP6, 20, 1);

  -- Module 4: Listening Tactics — Conversations
  INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, GroupName, Description, EstimatedMinutes)
  VALUES (@tplId, @toeicList, @toeicCert, 4, N'Module 4: Listening Tactics — Conversations',
          N'Reading & listening · modules 3–4',
          N'Develop rapid inference skills for Part 3 conversations.', 25);
  DECLARE @m4 INT = SCOPE_IDENTITY();
  INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
  VALUES (@m4, @toeicCert, @tplId, 'practice_section', @secP3, 15, 1);
END;
GO

-- Active Learning Path for learner@demo.local (B1 -> B2)
DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @tplB1B2 INT = (SELECT TOP 1 TemplateId FROM PathTemplates WHERE CertId = @toeicCert AND FromBandCode = 'B1' AND ToBandCode = 'B2');

IF NOT EXISTS (SELECT 1 FROM LearnerPaths WHERE UserId = @learner AND CertId = @toeicCert AND Status = 'active')
BEGIN
  DECLARE @placeId INT = (SELECT TOP 1 PlacementId FROM PlacementTests WHERE UserId = @learner AND CertId = @toeicCert AND Status = 'done' ORDER BY TakenAt DESC);

  INSERT INTO LearnerPaths (UserId, CertId, TemplateId, CurrentBandCode, PlacementId, Status, StartedAt)
  VALUES (@learner, @toeicCert, @tplB1B2, 'B1', @placeId, 'active', DATEADD(DAY, -14, SYSDATETIMEOFFSET()));
  DECLARE @lpId INT = SCOPE_IDENTITY();

  -- LearnerPathSteps: Module 1 done (85%), Module 2 in_progress (80% on item 1, in_progress on item 2), Module 3 available, Module 4 locked
  ;WITH NumberedItems AS (
    SELECT i.ItemId, i.TemplateId,
           ROW_NUMBER() OVER (ORDER BY m.DisplayOrder, i.DisplayOrder) AS StepOrder
    FROM PathModuleItems i
    JOIN PathModules m ON m.ModuleId = i.ModuleId
    WHERE i.TemplateId = @tplB1B2
  )
  INSERT INTO LearnerPathSteps (LearnerPathId, PathModuleItemId, TemplateId, Status, Score, StartedAt, CompletedAt)
  SELECT @lpId, ItemId, TemplateId,
         CASE StepOrder
           WHEN 1 THEN 'done'
           WHEN 2 THEN 'done'
           WHEN 3 THEN 'in_progress'
           WHEN 4 THEN 'available'
           ELSE 'locked'
         END,
         CASE StepOrder
           WHEN 1 THEN 85.00
           WHEN 2 THEN 80.00
           ELSE NULL
         END,
         CASE WHEN StepOrder IN (1, 2, 3) THEN DATEADD(DAY, -12, SYSDATETIMEOFFSET()) ELSE NULL END,
         CASE
           WHEN StepOrder = 1 THEN DATEADD(DAY, -10, SYSDATETIMEOFFSET())
           WHEN StepOrder = 2 THEN DATEADD(DAY, -5, SYSDATETIMEOFFSET())
           ELSE NULL
         END
  FROM NumberedItems
  ORDER BY StepOrder;

  -- Premium Path unlock transaction (-5 credits)
  INSERT INTO CreditTransactions (UserId, Reason, Delta, LearnerPathId, CreatedAt)
  VALUES (@learner, 'premium_path', -5, @lpId, DATEADD(DAY, -14, SYSDATETIMEOFFSET()));
END;
GO

PRINT '10. Learning path templates and active learner path seeded';
GO

-- ============================================================================
-- 11. Practice Sessions & Weakness Analysis
-- ============================================================================
-- Completed standalone practice sessions across multiple sections (P1, P2, P5)
-- with real questions and answers so vw_UserSectionAccuracyAll and vw_UserCategoryAccuracy populate.

DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @toeicMock INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS 2024 — Test 1');

DECLARE @secP1 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P1');
DECLARE @secP2 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P2');
DECLARE @secP5 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P5');

DECLARE @qP1 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = @secP1);
DECLARE @optP1_C INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP1 AND Label = 'C');

DECLARE @qP2 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = @secP2);
DECLARE @optP2_B INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP2 AND Label = 'B');

DECLARE @qP5_1 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = @secP5 AND DisplayOrder = 1);
DECLARE @optP5_1_C INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP5_1 AND Label = 'C');

DECLARE @qP5_2 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = @secP5 AND DisplayOrder = 2);
DECLARE @optP5_2_D INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP5_2 AND Label = 'D');

DECLARE @qP5_3 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = @secP5 AND DisplayOrder = 3);
DECLARE @optP5_3_A INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP5_3 AND Label = 'A'); -- incorrect option

-- Session 1: Section P1
IF NOT EXISTS (SELECT 1 FROM PracticeSessions WHERE UserId = @learner AND SectionId = @secP1)
BEGIN
  INSERT INTO PracticeSessions (UserId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt)
  VALUES (@learner, @secP1, 'listening', 1, 'finished', 1,
          DATEADD(DAY, -8, SYSDATETIMEOFFSET()), DATEADD(DAY, -8, DATEADD(MINUTE, 5, SYSDATETIMEOFFSET())));
  DECLARE @s1 INT = SCOPE_IDENTITY();
  IF @qP1 IS NOT NULL
    INSERT INTO PracticeAnswers (SessionId, QuestionId, SectionId, SelectedOptionId, IsCorrect, HintUsed, DisplayOrder)
    VALUES (@s1, @qP1, @secP1, @optP1_C, 1, 0, 1);
END;

-- Session 2: Section P2
IF NOT EXISTS (SELECT 1 FROM PracticeSessions WHERE UserId = @learner AND SectionId = @secP2)
BEGIN
  INSERT INTO PracticeSessions (UserId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt)
  VALUES (@learner, @secP2, 'listening', 1, 'finished', 1,
          DATEADD(DAY, -6, SYSDATETIMEOFFSET()), DATEADD(DAY, -6, DATEADD(MINUTE, 6, SYSDATETIMEOFFSET())));
  DECLARE @s2 INT = SCOPE_IDENTITY();
  IF @qP2 IS NOT NULL
    INSERT INTO PracticeAnswers (SessionId, QuestionId, SectionId, SelectedOptionId, IsCorrect, HintUsed, DisplayOrder)
    VALUES (@s2, @qP2, @secP2, @optP2_B, 1, 0, 1);
END;

-- Session 3: Section P5 (3 questions: 2 correct, 1 incorrect)
IF NOT EXISTS (SELECT 1 FROM PracticeSessions WHERE UserId = @learner AND SectionId = @secP5)
BEGIN
  INSERT INTO PracticeSessions (UserId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt)
  VALUES (@learner, @secP5, 'reading', 3, 'finished', 2,
          DATEADD(DAY, -4, SYSDATETIMEOFFSET()), DATEADD(DAY, -4, DATEADD(MINUTE, 12, SYSDATETIMEOFFSET())));
  DECLARE @s3 INT = SCOPE_IDENTITY();
  IF @qP5_1 IS NOT NULL
    INSERT INTO PracticeAnswers (SessionId, QuestionId, SectionId, SelectedOptionId, IsCorrect, HintUsed, DisplayOrder)
    VALUES (@s3, @qP5_1, @secP5, @optP5_1_C, 1, 0, 1);
  IF @qP5_2 IS NOT NULL
    INSERT INTO PracticeAnswers (SessionId, QuestionId, SectionId, SelectedOptionId, IsCorrect, HintUsed, DisplayOrder)
    VALUES (@s3, @qP5_2, @secP5, @optP5_2_D, 1, 1, 2);
  IF @qP5_3 IS NOT NULL
    INSERT INTO PracticeAnswers (SessionId, QuestionId, SectionId, SelectedOptionId, IsCorrect, HintUsed, DisplayOrder)
    VALUES (@s3, @qP5_3, @secP5, @optP5_3_A, 0, 0, 3);
END;
GO

PRINT '11. Practice sessions and answers seeded';
GO

-- ============================================================================
-- 12. Mock Exam Attempt on TOEIC Test 1 (Score 720, Band B1)
-- ============================================================================
DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @toeicMock INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS 2024 — Test 1');
DECLARE @toeicList INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'LIST');
DECLARE @toeicRead INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'READ');

IF NOT EXISTS (SELECT 1 FROM ExamAttempts WHERE UserId = @learner AND ExamId = @toeicMock AND TotalScore = 720)
BEGIN
  INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
  VALUES (@learner, @toeicMock, @toeicCert, 'graded',
          DATEADD(DAY, -3, SYSDATETIMEOFFSET()), DATEADD(DAY, -3, DATEADD(HOUR, 2, SYSDATETIMEOFFSET())),
          DATEADD(DAY, -3, DATEADD(MINUTE, 105, SYSDATETIMEOFFSET())), 720, 'B1');
  DECLARE @mockAtt INT = SCOPE_IDENTITY();

  -- Charge 1 credit for mock exam start
  INSERT INTO CreditTransactions (UserId, Reason, Delta, AttemptId, CreatedAt)
  VALUES (@learner, 'mock_exam_start', -1, @mockAtt, DATEADD(DAY, -3, SYSDATETIMEOFFSET()));

  -- AttemptSkillScores: Listening = 380, Reading = 340 (Total = 720)
  INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore) VALUES
    (@mockAtt, @toeicList, @toeicCert, 3, 380),
    (@mockAtt, @toeicRead, @toeicCert, 2, 340);

  -- AttemptAnswers
  DECLARE @qP1 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P1'));
  DECLARE @optP1_C INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP1 AND Label = 'C');
  IF @qP1 IS NOT NULL
    INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, SelectedOptionId, IsCorrect, IsFlagged)
    VALUES (@mockAtt, @qP1, @toeicMock, @optP1_C, 1, 0);

  DECLARE @qP2 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P2'));
  DECLARE @optP2_B INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP2 AND Label = 'B');
  IF @qP2 IS NOT NULL
    INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, SelectedOptionId, IsCorrect, IsFlagged)
    VALUES (@mockAtt, @qP2, @toeicMock, @optP2_B, 1, 0);

  DECLARE @qP3 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P3'));
  DECLARE @optP3_B INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP3 AND Label = 'B');
  IF @qP3 IS NOT NULL
    INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, SelectedOptionId, IsCorrect, IsFlagged)
    VALUES (@mockAtt, @qP3, @toeicMock, @optP3_B, 1, 0);

  DECLARE @qP4 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P4'));
  DECLARE @optP4_A INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP4 AND Label = 'A'); -- incorrect
  IF @qP4 IS NOT NULL
    INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, SelectedOptionId, IsCorrect, IsFlagged)
    VALUES (@mockAtt, @qP4, @toeicMock, @optP4_A, 0, 0);

  DECLARE @secP5 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P5');
  DECLARE @qP5_1 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = @secP5 AND DisplayOrder = 1);
  DECLARE @optP5_1_C INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP5_1 AND Label = 'C');
  IF @qP5_1 IS NOT NULL
    INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, SelectedOptionId, IsCorrect, IsFlagged)
    VALUES (@mockAtt, @qP5_1, @toeicMock, @optP5_1_C, 1, 0);

  DECLARE @qP5_2 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = @secP5 AND DisplayOrder = 2);
  DECLARE @optP5_2_D INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP5_2 AND Label = 'D');
  IF @qP5_2 IS NOT NULL
    INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, SelectedOptionId, IsCorrect, IsFlagged)
    VALUES (@mockAtt, @qP5_2, @toeicMock, @optP5_2_D, 1, 1); -- flagged for review

  DECLARE @qP5_3 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = @secP5 AND DisplayOrder = 3);
  DECLARE @optP5_3_C INT = (SELECT TOP 1 OptionId FROM QuestionOptions WHERE QuestionId = @qP5_3 AND Label = 'C'); -- incorrect
  IF @qP5_3 IS NOT NULL
    INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, SelectedOptionId, IsCorrect, IsFlagged)
    VALUES (@mockAtt, @qP5_3, @toeicMock, @optP5_3_C, 0, 0);
END;
GO

PRINT '12. Mock exam attempt with answers seeded';
GO

-- ============================================================================
-- 13. Mentors & Bookings
-- ============================================================================
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @toeicListSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'LIST');
DECLARE @khoaUserId INT = (SELECT UserId FROM Users WHERE Email = 'mentor.khoa@demo.local');
DECLARE @demoLearnerId INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');

-- Human Mentor: Dr. Trần Minh Khoa
IF NOT EXISTS (SELECT 1 FROM MentorProfiles WHERE DisplayName = N'Dr. Trần Minh Khoa')
BEGIN
  INSERT INTO MentorProfiles (UserId, DisplayName, Bio, CertId, SkillId, Type, PricePerSlot, PriceCredits, Headline, Expertise, SessionMinutes, IsActive)
  VALUES (
    @khoaUserId, N'Dr. Trần Minh Khoa',
    N'10 years teaching TOEIC at major language centers in Ho Chi Minh City. Specializes in listening strategies and accent training for Vietnamese learners.',
    @toeicCert, @toeicListSkill, 'human', 200000.00, 1,
    N'10 years teaching TOEIC. Specializes in listening strategies and accent training for Vietnamese learners.',
    N'Listening strategies, pronunciation, test-taking techniques', 60, 1
  );
END;

-- AI Mentor: AI Mentor — TOEIC Coach
IF NOT EXISTS (SELECT 1 FROM MentorProfiles WHERE DisplayName = N'AI Mentor — TOEIC Coach')
BEGIN
  INSERT INTO MentorProfiles (UserId, DisplayName, Bio, CertId, SkillId, Type, PricePerSlot, PriceCredits, Headline, Expertise, SessionMinutes, IsActive)
  VALUES (
    NULL, N'AI Mentor — TOEIC Coach',
    N'AI-powered speaking practice. Available 24/7 with instant feedback and pronunciation analysis.',
    @toeicCert, NULL, 'ai', 0.00, 1,
    N'AI-powered speaking practice. Available 24/7 with instant feedback and pronunciation analysis.',
    N'Instant speaking feedback, AI pronunciation diagnosis, 24/7 practice', 60, 1
  );
END;

DECLARE @khoaMentorId INT = (SELECT MentorId FROM MentorProfiles WHERE DisplayName = N'Dr. Trần Minh Khoa');

-- Future open slots for Dr. Trần Minh Khoa
IF NOT EXISTS (SELECT 1 FROM MentorSlots WHERE MentorId = @khoaMentorId AND Status = 'open')
BEGIN
  INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
  VALUES
    (@khoaMentorId, DATEADD(hour, 24, SYSDATETIMEOFFSET()), DATEADD(hour, 25, SYSDATETIMEOFFSET()), 'open'),
    (@khoaMentorId, DATEADD(hour, 48, SYSDATETIMEOFFSET()), DATEADD(hour, 49, SYSDATETIMEOFFSET()), 'open'),
    (@khoaMentorId, DATEADD(hour, 72, SYSDATETIMEOFFSET()), DATEADD(hour, 73, SYSDATETIMEOFFSET()), 'open'),
    (@khoaMentorId, DATEADD(hour, 120, SYSDATETIMEOFFSET()), DATEADD(hour, 121, SYSDATETIMEOFFSET()), 'open');
END;

-- Past completed booking with review
IF NOT EXISTS (SELECT 1 FROM Bookings WHERE MentorId = @khoaMentorId AND Status = 'done')
BEGIN
  INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
  VALUES (@khoaMentorId, DATEADD(day, -5, SYSDATETIMEOFFSET()), DATEADD(day, -5, DATEADD(hour, 1, SYSDATETIMEOFFSET())), 'open');
  DECLARE @pastSlot INT = SCOPE_IDENTITY();

  INSERT INTO Bookings (UserId, MentorId, SlotId, MentorType, Status, FocusArea, CreatedAt)
  VALUES (@demoLearnerId, @khoaMentorId, @pastSlot, 'human', 'done', N'Listening strategies Part 3 & 4', DATEADD(day, -5, SYSDATETIMEOFFSET()));
  DECLARE @bDone INT = SCOPE_IDENTITY();

  INSERT INTO Reviews (BookingId, UserId, Rating, Text, CreatedAt)
  VALUES (@bDone, @demoLearnerId, 5, N'Very patient and explains listening strategies clearly. Score improved significantly in 2 months.', DATEADD(day, -4, SYSDATETIMEOFFSET()));

  INSERT INTO CreditTransactions (UserId, Reason, Delta, BookingId, CreatedAt)
  VALUES (@demoLearnerId, 'mentor_booking', -1, @bDone, DATEADD(day, -5, SYSDATETIMEOFFSET()));
END;

-- Confirmed upcoming booking with Google Meet link
IF NOT EXISTS (SELECT 1 FROM Bookings WHERE MentorId = @khoaMentorId AND Status = 'confirmed')
BEGIN
  INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
  VALUES (@khoaMentorId, DATEADD(day, 2, SYSDATETIMEOFFSET()), DATEADD(day, 2, DATEADD(hour, 1, SYSDATETIMEOFFSET())), 'open');
  DECLARE @futureSlot INT = SCOPE_IDENTITY();

  INSERT INTO Bookings (UserId, MentorId, SlotId, MentorType, Status, MeetLink, FocusArea, CreatedAt)
  VALUES (@demoLearnerId, @khoaMentorId, @futureSlot, 'human', 'confirmed',
          N'https://meet.google.com/abc-defg-hij', N'Exam strategy & timing for Reading Part 7', DATEADD(day, -1, SYSDATETIMEOFFSET()));
  DECLARE @bConfirmed INT = SCOPE_IDENTITY();

  INSERT INTO CreditTransactions (UserId, Reason, Delta, BookingId, CreatedAt)
  VALUES (@demoLearnerId, 'mentor_booking', -1, @bConfirmed, DATEADD(day, -1, SYSDATETIMEOFFSET()));
END;
GO

PRINT '13. Mentors and bookings seeded';
GO

-- ============================================================================
-- 14. Complaints & Make-Good Grant
-- ============================================================================
DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @adminUser INT = (SELECT UserId FROM Users WHERE Email = 'admin@demo.local');
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @mockAtt INT = (SELECT TOP 1 AttemptId FROM ExamAttempts WHERE UserId = @learner AND CertId = @toeicCert AND TotalScore = 720);
DECLARE @pastBooking INT = (SELECT TOP 1 BookingId FROM Bookings WHERE UserId = @learner AND Status = 'done');

-- Make-good admin grant (+1 credit)
IF NOT EXISTS (SELECT 1 FROM CreditTransactions WHERE UserId = @learner AND Reason = 'admin_grant' AND Note LIKE N'%audio stream%')
BEGIN
  INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note, CreatedAt)
  VALUES (@learner, 'admin_grant', 1, @adminUser, N'Complaint make-good: audio stream stutter refund', DATEADD(DAY, -2, SYSDATETIMEOFFSET()));
END;

DECLARE @grantTxn INT = (SELECT TOP 1 TxnId FROM CreditTransactions WHERE UserId = @learner AND Reason = 'admin_grant' AND Note LIKE N'%audio stream%');

-- Resolved complaint: technical error on mock attempt
IF NOT EXISTS (SELECT 1 FROM Complaints WHERE UserId = @learner AND Status = 'resolved')
BEGIN
  INSERT INTO Complaints (AttemptId, UserId, Type, Reason, Status, Resolution, InternalNotes, ResolvedByUserId, ResolvedAt, ResolvedByGrantTxnId, CreatedAt)
  VALUES (@mockAtt, @learner, 'technical_error',
          N'Audio stream froze momentarily during listening Part 4, causing a missed question.',
          'resolved',
          N'Audio stream logs confirmed a 4-second network stutter. Granted 1 make-good credit to student wallet.',
          N'Verified CDN latency spike at 14:22. Compensatory credit issued.',
          @adminUser, DATEADD(DAY, -2, SYSDATETIMEOFFSET()), @grantTxn, DATEADD(DAY, -2, SYSDATETIMEOFFSET()));
END;

-- Reviewing / Open complaint: mentor quality
IF NOT EXISTS (SELECT 1 FROM Complaints WHERE UserId = @learner AND Status = 'reviewing')
BEGIN
  INSERT INTO Complaints (BookingId, UserId, Type, Reason, Status, InternalNotes, CreatedAt)
  VALUES (@pastBooking, @learner, 'mentor_quality',
          N'Session started 5 minutes late due to mentor connection troubles.',
          'reviewing',
          N'Discussed with Dr. Khoa; mentor will provide 15 extra minutes on the upcoming booking.',
          DATEADD(DAY, -1, SYSDATETIMEOFFSET()));
END;
GO

PRINT '14. Complaints and make-good grant seeded';
GO

-- ============================================================================
-- 15. AI Insights & Recommendations (Dashboard, Practice Session, Attempt, Path)
-- ============================================================================
DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @secP5 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P5');
DECLARE @sessionP5 INT = (SELECT TOP 1 SessionId FROM PracticeSessions WHERE UserId = @learner AND SectionId = @secP5);
DECLARE @mockAtt INT = (SELECT TOP 1 AttemptId FROM ExamAttempts WHERE UserId = @learner AND CertId = @toeicCert AND TotalScore = 720);
DECLARE @lpId INT = (SELECT TOP 1 LearnerPathId FROM LearnerPaths WHERE UserId = @learner AND CertId = @toeicCert AND Status = 'active');

-- 1. Dashboard Insight
IF NOT EXISTS (SELECT 1 FROM AiInsights WHERE UserId = @learner AND Scope = 'dashboard')
BEGIN
  INSERT INTO AiInsights (UserId, CertId, Scope, Narrative, Trend, Status, Model, PromptTokens, CompletionTokens, SourceFingerprint, CreatedAt, CompletedAt)
  VALUES (@learner, @toeicCert, 'dashboard',
          N'Strong progression across listening skills, with high accuracy in Part 1 and Part 2. Grammar fundamentals in Part 5 are improving, but vocabulary and fast inference in Part 6 and 7 require targeted practice to reach the target B2 band (785+).',
          'improving', 'done', 'fake-ai-insight-v1', 140, 85,
          '4a6b2c8e1f0d3e5a7b9c1d3e5f7a9b1c3d5e7f9a1b3c5d7e9f1a3b5c7d9e1f3a',
          DATEADD(DAY, -1, SYSDATETIMEOFFSET()), DATEADD(DAY, -1, SYSDATETIMEOFFSET()));
  DECLARE @inDash INT = SCOPE_IDENTITY();

  INSERT INTO Recommendations (UserId, CertId, InsightId, Text, Severity, TargetBandCode, IsResolved, CreatedAt)
  VALUES (@learner, @toeicCert, @inDash,
          N'Complete the Part 5 verb tense modules in your learning path to eliminate grammatical errors.',
          'minor', 'B2', 0, DATEADD(DAY, -1, SYSDATETIMEOFFSET()));
END;

-- 2. Practice Session Insight
IF @sessionP5 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AiInsights WHERE UserId = @learner AND Scope = 'practice_session' AND PracticeSessionId = @sessionP5)
BEGIN
  INSERT INTO AiInsights (UserId, CertId, Scope, PracticeSessionId, Narrative, Trend, Status, Model, PromptTokens, CompletionTokens, SourceFingerprint, CreatedAt, CompletedAt)
  VALUES (@learner, @toeicCert, 'practice_session', @sessionP5,
          N'Solid accuracy on verb tenses and prepositions. One question missed on part of speech (noun vs adjective forms).',
          'flat', 'done', 'fake-ai-insight-v1', 110, 60,
          '5b7c3d9f2a1e4f6b8c0d2e4f6a8b2c4d6e8f0a2b4c6d8e0f2a4b6c8d0e2f4a6b',
          DATEADD(DAY, -4, SYSDATETIMEOFFSET()), DATEADD(DAY, -4, SYSDATETIMEOFFSET()));
  DECLARE @inSess INT = SCOPE_IDENTITY();

  INSERT INTO Recommendations (UserId, CertId, InsightId, PracticeSessionId, Text, Severity, TargetBandCode, IsResolved, CreatedAt)
  VALUES (@learner, @toeicCert, @inSess, @sessionP5,
          N'Review suffix rules for adjectives (-ive, -al, -ous) to improve speed in Part 5.',
          'minor', 'B2', 0, DATEADD(DAY, -4, SYSDATETIMEOFFSET()));
END;

-- 3. Attempt Insight
IF @mockAtt IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AiInsights WHERE UserId = @learner AND Scope = 'attempt' AND AttemptId = @mockAtt)
BEGIN
  INSERT INTO AiInsights (UserId, CertId, Scope, AttemptId, Narrative, Trend, Status, Model, PromptTokens, CompletionTokens, SourceFingerprint, CreatedAt, CompletedAt)
  VALUES (@learner, @toeicCert, 'attempt', @mockAtt,
          N'Excellent mock result: 720 Total Score (B1), placing you within reach of B2. Listening achieved 380 scaled score; reading reached 340. Time management was consistent throughout.',
          'improving', 'done', 'fake-ai-insight-v1', 160, 95,
          '6c8d4e0a3b2f5a7c9d1e3f5a7b9c3d5e7f9a1b3c5d7e9f1a3b5c7d9e1f3a5b7c',
          DATEADD(DAY, -3, SYSDATETIMEOFFSET()), DATEADD(DAY, -3, SYSDATETIMEOFFSET()));
  DECLARE @inAtt INT = SCOPE_IDENTITY();

  INSERT INTO Recommendations (UserId, CertId, InsightId, AttemptId, Text, Severity, TargetBandCode, IsResolved, CreatedAt)
  VALUES (@learner, @toeicCert, @inAtt, @mockAtt,
          N'Review your flagged question on prepositions and analyze the explanation.',
          'minor', 'B2', 0, DATEADD(DAY, -3, SYSDATETIMEOFFSET()));
END;

-- 4. Path Insight
IF @lpId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AiInsights WHERE UserId = @learner AND Scope = 'path' AND LearnerPathId = @lpId)
BEGIN
  INSERT INTO AiInsights (UserId, CertId, Scope, LearnerPathId, Narrative, Trend, Status, Model, PromptTokens, CompletionTokens, SourceFingerprint, CreatedAt, CompletedAt)
  VALUES (@learner, @toeicCert, 'path', @lpId,
          N'Module 1 completed with 85% mastery. You are currently working on Module 2 sentence structures and speaking tasks.',
          'improving', 'done', 'fake-ai-insight-v1', 130, 75,
          '7d9e5f1b4c3a6b8d0e2f4a6b8c0d4e6f8a0b2c4d6e8f0a2b4c6d8e0f2a4b6c8d',
          DATEADD(DAY, -2, SYSDATETIMEOFFSET()), DATEADD(DAY, -2, SYSDATETIMEOFFSET()));
  DECLARE @inPath INT = SCOPE_IDENTITY();

  INSERT INTO Recommendations (UserId, CertId, InsightId, Text, Severity, TargetBandCode, IsResolved, CreatedAt)
  VALUES (@learner, @toeicCert, @inPath,
          N'Complete the speaking AI task in Module 2 before proceeding to business vocabulary.',
          'info', 'B2', 0, DATEADD(DAY, -2, SYSDATETIMEOFFSET()));
END;
GO

PRINT '15. AI insights and recommendations seeded';
GO

-- ============================================================================
-- 16. Saved Questions
-- ============================================================================
DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @toeicMock INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS 2024 — Test 1');
DECLARE @secP5 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P5');

DECLARE @qP5_2 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = @secP5 AND DisplayOrder = 2);
DECLARE @qP5_3 INT = (SELECT TOP 1 QuestionId FROM Questions WHERE ExamId = @toeicMock AND SectionId = @secP5 AND DisplayOrder = 3);

IF @qP5_3 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM SavedQuestions WHERE UserId = @learner AND QuestionId = @qP5_3)
  INSERT INTO SavedQuestions (UserId, QuestionId, Source, SavedAt)
  VALUES (@learner, @qP5_3, 'incorrect', DATEADD(DAY, -4, SYSDATETIMEOFFSET()));

IF @qP5_2 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM SavedQuestions WHERE UserId = @learner AND QuestionId = @qP5_2)
  INSERT INTO SavedQuestions (UserId, QuestionId, Source, SavedAt)
  VALUES (@learner, @qP5_2, 'manual', DATEADD(DAY, -3, SYSDATETIMEOFFSET()));
GO

PRINT '16. Saved questions seeded';
GO

PRINT 'All demo data seeded successfully.';
GO
"""

with open("docs/seed-demo.sql", "w", encoding="utf-8") as f:
    f.write(sql)
print("Wrote docs/seed-demo.sql successfully")
