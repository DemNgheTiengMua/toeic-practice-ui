-- docs/seed-demo.sql
-- Demo seed: full demo data set for black-box grading (issue #32).
-- Single source of truth for demo data across all certificates and screens.
-- Can be run cleanly and idempotently with:
--   sqlcmd -S "(localdb)\MSSQLLocalDB" -d CompanionSchemaTest -I -b -i docs/seed-demo.sql
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
          N'Woman: Good morning, Mr. Evans. Did the catering team confirm the order for Friday''s banquet?
Man: Yes, they did, but they said they need our final head count by tomorrow noon.
Woman: I will email the department heads right away to get their RSVPs.');
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
          N'MEMORANDUM
To: All Branch Employees
From: Facilities Management
Date: October 14
Subject: Elevator Maintenance
Please note that the main elevators will undergo safety upgrades this Saturday between 8:00 A.M. and 2:00 P.M. The service elevator near the loading dock will remain accessible throughout this period.');
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
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @ieltsCert AND Name IN (N'Cambridge IELTS — Placement Diagnostic', N'IELTS-DIAG-01'))
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
          N'Describe a piece of technology you find difficult to use.
You should say:
- What it is
- When you got it
- What you use it for
And explain why you find it difficult to use.');
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
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @vstepCert AND Name IN (N'VSTEP Placement Test — Diagnostic', N'VSTEP-DIAG-01'))
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
          N'Topic: Should universities make physical education courses compulsory for all undergraduate students?
- Health benefits
- Academic stress relief
- Student freedom of choice');
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
          N'You received an email from an international friend asking for recommendations on historical places to visit in Hanoi.
Write an email of at least 120 words suggesting two places, explaining why they are interesting, and giving advice on when to visit.');
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

-- Seed Guided Learning Lesson: Present Continuous Tense
IF NOT EXISTS (SELECT 1 FROM Lessons WHERE CertId = @toeicCert AND Title = N'Present Continuous Tense')
BEGIN
  INSERT INTO Lessons (CertId, SkillId, CategoryId, Title, Summary, EstimatedMinutes, DisplayOrder)
  VALUES (@toeicCert, @toeicRead, @catTense, N'Present Continuous Tense',
          N'Master the present continuous tense for temporary and ongoing actions in TOEIC Part 5.', 5, 1);
  DECLARE @lessonId INT = SCOPE_IDENTITY();

  INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder) VALUES
    (@lessonId, @toeicCert, 'rule', N'Rule: Present Continuous for Ongoing Actions',
     N'The present continuous describes actions happening right now, at the moment of speaking, or around the current time period.' + CHAR(10) + CHAR(10) +
     N'**Formula:** `Subject + is / am / are + Verb-ing`' + CHAR(10) + CHAR(10) +
     N'Signals in TOEIC Part 5: **now, currently, presently, at the moment, this week**.', 0),

    (@lessonId, @toeicCert, 'pitfall', N'Common Pitfalls & Exam Traps',
     N'Watch out for stative verbs that NEVER appear in the continuous aspect on the TOEIC:' + CHAR(10) + CHAR(10) +
     N'- **TRAP 1: Stative Verbs of Perception & Cognition**' + CHAR(10) +
     N'WRONG: "He is knowing the answer." → CORRECT: "He knows the answer." (know, understand, believe, remember).' + CHAR(10) + CHAR(10) +
     N'- **TRAP 2: Possession Verbs**' + CHAR(10) +
     N'WRONG: "The firm is owning five branches." → CORRECT: "The firm owns five branches." (own, have, belong).' + CHAR(10) + CHAR(10) +
     N'- **TRAP 3: Missing Auxiliary ''Be''**' + CHAR(10) +
     N'WRONG: "The committee reviewing the draft now." → CORRECT: "The committee is reviewing the draft now."', 1),

    (@lessonId, @toeicCert, 'example', N'Recognizing the Aspect in Context',
     N'**Part 5 Worked Question:**' + CHAR(10) + CHAR(10) +
     N'"Mr. Tanaka currently ------- the quarterly budget with regional supervisors in Osaka."' + CHAR(10) + CHAR(10) +
     N'- **(A)** reviews — *Wrong: simple present does not match temporary ''currently'' modifier*' + CHAR(10) +
     N'- **(B)** is reviewing — **Correct: singular subject ''Mr. Tanaka'' + ''currently'' signals action in progress**' + CHAR(10) +
     N'- **(C)** reviewed — *Wrong: past tense conflicts with ongoing signal*' + CHAR(10) +
     N'- **(D)** reviewing — *Wrong: missing auxiliary ''is''*' + CHAR(10) + CHAR(10) +
     N'**Key Takeaway:** Whenever you spot **currently** or **at present** in Part 5, test for `is/are + -ing` first.', 2),

    (@lessonId, @toeicCert, 'micro_check', N'Check Your Understanding',
     N'Choose the correct option to complete the sentence.', 3);

  DECLARE @mcCardId INT = (SELECT CardId FROM LessonCards WHERE LessonId = @lessonId AND CardType = 'micro_check');
  UPDATE LessonCards SET
    QuestionPrompt = N'The marketing team ------- new client proposals at the moment, so please do not disturb them.',
    OptionA = N'evaluates',
    OptionB = N'is evaluating',
    OptionC = N'evaluated',
    OptionD = N'evaluating',
    CorrectOption = 'B',
    Explanation = N'The time adverbial "at the moment" indicates an action in progress right now, requiring the present continuous. The collective subject "team" takes the singular verb "is evaluating".',
    Rationale = N'(A) is simple present, (C) is simple past, and (D) is a participle lacking the required auxiliary "is".'
  WHERE CardId = @mcCardId;
END;

IF NOT EXISTS (SELECT 1 FROM PathTemplates WHERE CertId = @toeicCert AND FromBandCode = 'B1' AND ToBandCode = 'B2')
BEGIN
  INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
  VALUES (@toeicCert, 'B1', 'B2', N'TOEIC B1 → B2 Intermediate to Upper-Intermediate Path');
  DECLARE @tplId INT = SCOPE_IDENTITY();
  DECLARE @presentContinuousLesson INT = (SELECT TOP 1 LessonId FROM Lessons WHERE CertId = @toeicCert AND Title = N'Present Continuous Tense');

  -- Module 1: Incomplete Sentences Grammar Mastery
  INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, GroupName, Description, EstimatedMinutes)
  VALUES (@tplId, @toeicRead, @toeicCert, 1, N'Module 1: Incomplete Sentences Grammar Mastery',
          N'Grammar foundations · modules 1–2',
          N'Master high-frequency Part 5 patterns: verb tenses, voice, and agreement.', 25);
  DECLARE @m1 INT = SCOPE_IDENTITY();
  IF @presentContinuousLesson IS NOT NULL
  BEGIN
    INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefLessonId, DisplayOrder)
    VALUES (@m1, @toeicCert, @tplId, 'lesson', @presentContinuousLesson, 1);
  END;
  INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
  VALUES (@m1, @toeicCert, @tplId, 'practice_section', @secP5, 20, 2);
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

  -- LearnerPathSteps: Module 1 Lesson done (100%), Module 1 Practice done (85%), Module 2 Practice done (80%), Module 2 AI Task in_progress, Module 3 available, Module 4 locked
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
           WHEN 3 THEN 'done'
           WHEN 4 THEN 'in_progress'
           WHEN 5 THEN 'available'
           ELSE 'locked'
         END,
         CASE StepOrder
           WHEN 1 THEN 100.00
           WHEN 2 THEN 85.00
           WHEN 3 THEN 80.00
           ELSE NULL
         END,
         CASE WHEN StepOrder IN (1, 2, 3, 4) THEN DATEADD(DAY, -12, SYSDATETIMEOFFSET()) ELSE NULL END,
         CASE
           WHEN StepOrder = 1 THEN DATEADD(DAY, -11, SYSDATETIMEOFFSET())
           WHEN StepOrder = 2 THEN DATEADD(DAY, -10, SYSDATETIMEOFFSET())
           WHEN StepOrder = 3 THEN DATEADD(DAY, -5, SYSDATETIMEOFFSET())
           ELSE NULL
         END
  FROM NumberedItems
  ORDER BY StepOrder;

  -- Seed LearnerLessonProgress for learner@demo.local
  DECLARE @demoLesson INT = (SELECT TOP 1 LessonId FROM Lessons WHERE CertId = @toeicCert AND Title = N'Present Continuous Tense');
  IF @demoLesson IS NOT NULL AND NOT EXISTS (SELECT 1 FROM LearnerLessonProgress WHERE UserId = @learner AND LessonId = @demoLesson)
  BEGIN
    DECLARE @lastCard INT = (SELECT TOP 1 CardId FROM LessonCards WHERE LessonId = @demoLesson ORDER BY DisplayOrder DESC);
    INSERT INTO LearnerLessonProgress (UserId, LessonId, CertId, LastViewedCardId, CardsCompleted, TotalCards, IsCompleted, FirstOpenedAt, CompletedAt, UpdatedAt)
    VALUES (@learner, @demoLesson, @toeicCert, @lastCard, 4, 4, 1, DATEADD(DAY, -12, SYSDATETIMEOFFSET()), DATEADD(DAY, -11, SYSDATETIMEOFFSET()), DATEADD(DAY, -11, SYSDATETIMEOFFSET()));
  END;

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


-- ============================================================================
-- 17. 3-Skill Diagnostic Placement Benchmark ("Diagnostic Benchmark 45M") & Dynamic Learning Path
-- ============================================================================
/* ==========================================================================================
   FILE: seed-diagnostic-benchmark.sql
   TARGET: SQL Server / LocalDB (T-SQL)
   PURPOSE: Seed 100% Production-Ready Data for TOEIC 3-Skill Diagnostic Placement Exam 
            ("Diagnostic Benchmark 45M") & Dynamic Learning Path ("TOEIC Diagnostic Sprint")
   AUTHOR: Principal Database Engineer & ETS Assessment Specialist
   DATE: 2026
   ========================================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;


    

    /* ======================================================================================
       0. TRA CỨU & ĐẢM BẢO DỮ LIỆU NỀN TẢNG (CERTIFICATES, SKILLS, SECTIONS)
       ====================================================================================== */
    DECLARE @toeicCert INT;
    DECLARE @toeicList INT, @toeicRead INT, @toeicWrit INT;
    DECLARE @secP1 INT, @secP2 INT, @secP3 INT, @secP4 INT;
    DECLARE @secP5 INT, @secP6 INT, @secP7 INT;
    DECLARE @secW2 INT, @secW3 INT;

    -- 0.1. Tra cứu Certificate TOEIC
    SELECT @toeicCert = CertId FROM dbo.Certificates WHERE Code = N'TOEIC';
    IF @toeicCert IS NULL
    BEGIN
        RAISERROR(N'Không tìm thấy chứng chỉ TOEIC (Code = ''TOEIC'') trong bảng Certificates.', 16, 1);
    END

    -- 0.2. Tra cứu Skills của TOEIC
    SELECT @toeicList = SkillId FROM dbo.Skills WHERE CertId = @toeicCert AND Code = 'LIST';
    SELECT @toeicRead = SkillId FROM dbo.Skills WHERE CertId = @toeicCert AND Code = 'READ';
    SELECT @toeicWrit = SkillId FROM dbo.Skills WHERE CertId = @toeicCert AND Code = 'WRIT';

    -- 0.3. Tra cứu các Sections của TOEIC
    SELECT @secP1 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P1';
    SELECT @secP2 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P2';
    SELECT @secP3 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P3';
    SELECT @secP4 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P4';
    SELECT @secP5 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P5';
    SELECT @secP6 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P6';
    SELECT @secP7 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'P7';
    SELECT @secW2 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'W2';
    SELECT @secW3 = SectionId FROM dbo.Sections WHERE CertId = @toeicCert AND Code = N'W3';

    IF @secP1 IS NULL OR @secP2 IS NULL OR @secP3 IS NULL OR @secP4 IS NULL 
       OR @secP5 IS NULL OR @secP6 IS NULL OR @secP7 IS NULL 
       OR @secW2 IS NULL OR @secW3 IS NULL
    BEGIN
        RAISERROR(N'Thiếu một hoặc nhiều Section (P1..P7, W2, W3) của chứng chỉ TOEIC trong bảng Sections.', 16, 1);
    END

    /* ======================================================================================
       PHẦN 1: BÀI THI CHẨN ĐOÁN 3 KỸ NĂNG ("Diagnostic Benchmark 45M")
       ====================================================================================== */
    DECLARE @examId INT;
    DECLARE @examName NVARCHAR(200) = N'Diagnostic Benchmark 45M';

    IF NOT EXISTS (SELECT 1 FROM dbo.Exams WHERE CertId = @toeicCert AND Name = @examName)
    BEGIN
        INSERT INTO dbo.Exams (
            CertId, Name, Purpose, Status, DurationMinutes, QuestionCount, Description, SourceName
        )
        VALUES (
            @toeicCert,
            @examName,
            'placement',
            'published',
            45,
            32,
            N'Bài thi chẩn đoán toàn diện 3 kỹ năng (Nghe, Đọc, Viết) được thiết kế để phân tích lỗ hổng kiến thức, đo lường năng lực CEFR ban đầu và tự động kiến tạo lộ trình học tập tối ưu.',
            N'ETS Benchmark Suite 2026'
        );
    END

    SELECT @examId = ExamId FROM dbo.Exams WHERE CertId = @toeicCert AND Name = @examName;

    /* --------------------------------------------------------------------------------------
       1.1. EXAM SECTION AUDIO (LISTENING P1, P2, P3, P4)
       -------------------------------------------------------------------------------------- */
    IF NOT EXISTS (SELECT 1 FROM dbo.ExamSectionAudio WHERE CertId = @toeicCert AND ExamId = @examId AND SectionId = @secP1)
        INSERT INTO dbo.ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
        VALUES (@examId, @secP1, @toeicCert, N'/audio/toeic/placement-p1.mp3', 180, 2880000);

    IF NOT EXISTS (SELECT 1 FROM dbo.ExamSectionAudio WHERE CertId = @toeicCert AND ExamId = @examId AND SectionId = @secP2)
        INSERT INTO dbo.ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
        VALUES (@examId, @secP2, @toeicCert, N'/audio/toeic/placement-p2.mp3', 300, 4800000);

    IF NOT EXISTS (SELECT 1 FROM dbo.ExamSectionAudio WHERE CertId = @toeicCert AND ExamId = @examId AND SectionId = @secP3)
        INSERT INTO dbo.ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
        VALUES (@examId, @secP3, @toeicCert, N'/audio/toeic/test1-p3.mp3', 600, 9600000);

    IF NOT EXISTS (SELECT 1 FROM dbo.ExamSectionAudio WHERE CertId = @toeicCert AND ExamId = @examId AND SectionId = @secP4)
        INSERT INTO dbo.ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
        VALUES (@examId, @secP4, @toeicCert, N'/audio/toeic/test1-p4.mp3', 600, 9600000);

    DECLARE @qId INT;
    /* --------------------------------------------------------------------------------------
       1.2. LISTENING - PART 1: PHOTOGRAPHS (2 CÂU: Q1 - Q2, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP1_1 INT, @grpP1_2 INT;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP1 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript, 
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP1, 1, NULL,
            N'(A) They are boarding a commuter train. (B) They are examining a blueprint on the desk. (C) They are hanging a picture on the wall. (D) The projector screen is being rolled up.',
            N'/img/part1-sample.svg',
            N'Two engineers standing near an office desk looking closely at an architectural blueprint',
            N'/audio/toeic/placement-p1.mp3', 0, 18000
        );
    END
    SELECT @grpP1_1 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP1 AND DisplayOrder = 1;

    -- Câu 1 (P1)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP1_1, @examId, @secP1, @toeicList,
            N'Look at the picture marked Number 1 in your test book and select the statement that best describes what you see.',
            2, 'mcq',
            N'Hai nhân vật trong ảnh đang cúi nhìn vào bản thiết kế trải trên bàn làm việc (examining a blueprint on the desk).',
            1
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) They are boarding a commuter train.', 0, N'Bẫy đồng âm (Homophone trap): Từ "boarding" dễ gây nhầm lẫn âm thanh với "board" (bảng/bàn họp) trong phòng.'),
        (@qId, 'mcq', 'B', N'(B) They are examining a blueprint on the desk.', 1, N'Đáp án đúng: Mô tả chính xác hành động của hai người đang xem xét bản thiết kế trên mặt bàn.'),
        (@qId, 'mcq', 'C', N'(C) They are hanging a picture on the wall.', 0, N'Bẫy hành động sai (Wrong action trap): Có bức tranh/biểu đồ trên tường nhưng đã được treo sẵn, không có ai đang treo.'),
        (@qId, 'mcq', 'D', N'(D) The projector screen is being rolled up.', 0, N'Bẫy bị động hiện tại tiếp diễn (being + V-ed): Màn chiếu đang ở trạng thái tĩnh, không có tác động cuộn lên.');

    -- QuestionGroup cho P1 Câu 2
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP1 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript, 
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP1, 2, NULL,
            N'(A) Some chairs are stacked in the corner. (B) A technician is repairing the light fixture. (C) Documents are being distributed to attendees. (D) Monitor screens are arranged in a row on the table.',
            N'/img/part1-sample.svg',
            N'An empty modern conference room with computer monitors lined up neatly along the table',
            N'/audio/toeic/placement-p1.mp3', 18000, 36000
        );
    END
    SELECT @grpP1_2 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP1 AND DisplayOrder = 2;

    -- Câu 2 (P1)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP1_2, @examId, @secP1, @toeicList,
            N'Look at the picture marked Number 2 in your test book and select the statement that best describes what you see.',
            2, 'mcq',
            N'Tranh tả vật trong phòng họp trống: các màn hình máy tính được xếp thành hàng trên bàn (arranged in a row).',
            2
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 2;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) Some chairs are stacked in the corner.', 0, N'Bẫy trạng thái sai: Ghế được xếp quanh bàn làm việc chứ không chồng lên nhau (stacked) ở góc phòng.'),
        (@qId, 'mcq', 'B', N'(B) A technician is repairing the light fixture.', 0, N'Bẫy suy diễn chủ thể không tồn tại: Trong hình hoàn toàn không có kỹ thuật viên nào.'),
        (@qId, 'mcq', 'C', N'(C) Documents are being distributed to attendees.', 0, N'Bẫy "being + V3/ed" cho tranh tĩnh: Không có người tham dự nên không thể có hành động đang phát tài liệu.'),
        (@qId, 'mcq', 'D', N'(D) Monitor screens are arranged in a row on the table.', 1, N'Đáp án đúng: Diễn tả chính xác trạng thái tĩnh của các màn hình máy tính xếp dọc theo bàn.');
    /* --------------------------------------------------------------------------------------
       1.3. LISTENING - PART 2: QUESTION-RESPONSE (4 CÂU: Q3 - Q6, CHỈ 3 OPTIONS A-C)
       -------------------------------------------------------------------------------------- */
    -- Câu 3 (P2)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
        VALUES (@examId, @toeicCert, @secP2, 3, N'/audio/toeic/placement-p2.mp3');
    END
    DECLARE @grpQ3 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 3);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ3, @examId, @secP2, @toeicList,
            N'When is the quarterly budget proposal due? — Listen and choose the best response (A, B, or C).',
            2, 'mcq',
            N'Câu hỏi "When" nhưng được trả lời gián tiếp bằng cách đẩy trách nhiệm kiểm tra sang lịch trình mà giám đốc tài chính đã gửi.',
            3
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 3;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) Yes, the proposal was approved.', 0, N'Bẫy Yes/No cho câu hỏi Wh- (When) kết hợp bẫy lặp từ "proposal" (Same-word trap).'),
        (@qId, 'mcq', 'B', N'(B) Check the schedule Ms. Lin e-mailed this morning.', 1, N'Đáp án đúng: Phản hồi gián tiếp (Indirect response) chuẩn ETS — chỉ dẫn người hỏi nơi tra cứu thời hạn.'),
        (@qId, 'mcq', 'C', N'(C) About twenty thousand dollars.', 0, N'Bẫy liên tưởng từ vựng (Associative trap): Nghe thấy "budget" nên đưa ra con số tiền tệ, nhưng câu hỏi hỏi về thời gian "When".');

    -- Câu 4 (P2)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 4)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
        VALUES (@examId, @toeicCert, @secP2, 4, N'/audio/toeic/placement-p2.mp3');
    END
    DECLARE @grpQ4 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 4);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 4)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ4, @examId, @secP2, @toeicList,
            N'Who is leading the new client orientation session tomorrow? — Listen and choose the best response (A, B, or C).',
            2, 'mcq',
            N'Câu hỏi "Who" yêu cầu xác định nhân sự hoặc sự thay đổi phân công công việc.',
            4
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 4;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) In the main conference room on the third floor.', 0, N'Bẫy sai từ để hỏi: Trả lời cho câu hỏi "Where" thay vì "Who".'),
        (@qId, 'mcq', 'B', N'(B) Mr. Takahashi volunteered to take it over.', 1, N'Đáp án đúng: Nêu tên người phụ trách buổi định hướng khách hàng mới.'),
        (@qId, 'mcq', 'C', N'(C) It was a very informative session.', 0, N'Bẫy sai thì (Wrong tense trap) và lặp từ "session": Sự kiện diễn ra vào ngày mai (tomorrow) nhưng đáp án lại dùng thì quá khứ "was".');

    -- Câu 5 (P2)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 5)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
        VALUES (@examId, @toeicCert, @secP2, 5, N'/audio/toeic/placement-p2.mp3');
    END
    DECLARE @grpQ5 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 5);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 5)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ5, @examId, @secP2, @toeicList,
            N'Why don''t we postpone the product launch until the packaging is finalized? — Listen and choose the best response (A, B, or C).',
            2, 'mcq',
            N'"Why don''t we..." là câu đề xuất (suggestion), không phải câu hỏi nguyên nhân "Why" thông thường.',
            5
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 5;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) Because the factory is located in Osaka.', 0, N'Bẫy "Because" kinh điển cho câu đề xuất "Why don''t we...": Nội dung không ăn nhập với đề xuất lùi ngày ra mắt.'),
        (@qId, 'mcq', 'B', N'(B) A light lunch at the cafeteria.', 0, N'Bẫy phát âm gần giống (Near-homophone trap): Từ "lunch" (bữa trưa) phát âm gần giống với "launch" (ra mắt sản phẩm).'),
        (@qId, 'mcq', 'C', N'(C) That makes sense, given the printing delays.', 1, N'Đáp án đúng: Đồng tình với lời đề xuất và bổ sung lý do thực tế về việc chậm trễ in ấn bao bì.');

    -- Câu 6 (P2)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 6)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, AudioPath)
        VALUES (@examId, @toeicCert, @secP2, 6, N'/audio/toeic/placement-p2.mp3');
    END
    DECLARE @grpQ6 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP2 AND DisplayOrder = 6);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 6)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, 
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ6, @examId, @secP2, @toeicList,
            N'You''ve already updated the inventory database, haven''t you? — Listen and choose the best response (A, B, or C).',
            2, 'mcq',
            N'Câu hỏi đuôi (Tag question) kiểm tra trạng thái hoàn thành công việc, thường được trả lời bằng lý do chưa thể thực hiện.',
            6
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 6;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'(A) The server was down all afternoon, so I couldn''t.', 1, N'Đáp án đúng: Phản hồi gián tiếp giải thích nguyên nhân chưa thể cập nhật cơ sở dữ liệu.'),
        (@qId, 'mcq', 'B', N'(B) In the warehouse next to the loading dock.', 0, N'Bẫy liên tưởng từ vựng: Nghe thấy "inventory" (hàng tồn kho) nên gán địa điểm nhà kho (warehouse).'),
        (@qId, 'mcq', 'C', N'(C) Yes, the software update is free of charge.', 0, N'Bẫy lặp từ "update": Đánh tráo khái niệm giữa cập nhật dữ liệu kho và bản cập nhật phần mềm miễn phí.');
    /* --------------------------------------------------------------------------------------
       1.4. LISTENING - PART 3: CONVERSATIONS (1 GROUP, 3 CÂU: Q7 - Q9, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP3 INT;
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP3 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript,
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP3, 1,
            N'Questions 7 through 9 refer to the following conversation.',
            N'Man: Hi, Clara. I just received a call from Apex Logistics regarding our shipment of ergonomic office chairs for the new Seattle branch. Unfortunately, two of the delivery trucks are stuck in Denver due to severe weather.
Woman: Oh no! Our interior setup team is scheduled to assemble the workstations this Thursday morning. Did they say when the trucks will arrive?
Man: They estimate a forty-eight-hour delay, which means the chairs won''t get there until Friday afternoon.
Woman: In that case, I''d better contact the setup agency right away to reschedule their technicians for Saturday so we don''t get charged a standby fee.',
            NULL, NULL, N'/audio/toeic/test1-p3.mp3', 0, 48000
        );
    END
    SELECT @grpP3 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP3 AND DisplayOrder = 1;

    -- Câu 7 (P3)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 7)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP3, @examId, @secP3, @toeicList,
            N'What problem does the man mention?',
            2, 'mcq',
            N'Người đàn ông thông báo lô hàng ghế văn phòng bị kẹt ở Denver do thời tiết xấu dẫn đến giao hàng trễ.',
            7
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 7;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'Some office furniture was damaged in transit.', 0, N'Bẫy suy diễn quá đà: Lô hàng bị kẹt do thời tiết chứ không hề bị hư hỏng.'),
        (@qId, 'mcq', 'B', N'A delivery of office chairs has been delayed.', 1, N'Đáp án đúng: Paraphrase từ "shipment of ergonomic office chairs... stuck in Denver... forty-eight-hour delay".'),
        (@qId, 'mcq', 'C', N'A branch office failed a safety inspection.', 0, N'Bẫy thông tin nhiễu không có trong bài nghe.'),
        (@qId, 'mcq', 'D', N'An invoice contains an incorrect standby fee.', 0, N'Bẫy ghép từ (Word-matching trap): Có nhắc tới "standby fee" ở cuối bài nhưng đó là khoản phí người phụ nữ muốn tránh, không phải lỗi hóa đơn.');

    -- Câu 8 (P3)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 8)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP3, @examId, @secP3, @toeicList,
            N'When is the shipment now expected to arrive?',
            2, 'mcq',
            N'Ban đầu dự kiến lắp đặt sáng thứ Năm, nhưng bị trễ 48 giờ nên chiều thứ Sáu hàng mới tới.',
            8
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 8;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'On Thursday morning', 0, N'Bẫy mốc thời gian cũ (Original schedule trap): Thứ Năm là lịch lắp đặt ban đầu của đội kỹ thuật.'),
        (@qId, 'mcq', 'B', N'On Thursday afternoon', 0, N'Bẫy trộn lẫn thông tin giữa "Thursday morning" và "Friday afternoon".'),
        (@qId, 'mcq', 'C', N'On Friday afternoon', 1, N'Đáp án đúng: Người đàn ông nói rõ "the chairs won''t get there until Friday afternoon".'),
        (@qId, 'mcq', 'D', N'On Saturday morning', 0, N'Bẫy mốc thời gian của hành động khác: Thứ Bảy là ngày dời lịch cho đội kỹ thuật viên lắp đặt.');

    -- Câu 9 (P3)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 9)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP3, @examId, @secP3, @toeicList,
            N'What will the woman most likely do next?',
            2, 'mcq',
            N'Người phụ nữ quyết định liên hệ ngay với bên lắp đặt (setup agency) để đổi lịch sang thứ Bảy.',
            9
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 9;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'Order chairs from a local supplier in Seattle', 0, N'Bẫy suy diễn không có căn cứ: Hai người chỉ chờ hàng tới chứ không hủy đơn để mua nhà cung cấp khác.'),
        (@qId, 'mcq', 'B', N'Travel to Denver to inspect the merchandise', 0, N'Bẫy địa danh: Denver chỉ là nơi xe tải bị kẹt lại do thời tiết.'),
        (@qId, 'mcq', 'C', N'Reschedule an appointment with a service provider', 1, N'Đáp án đúng: Paraphrase từ "contact the setup agency right away to reschedule their technicians".'),
        (@qId, 'mcq', 'D', N'Approve an overtime payment for the drivers', 0, N'Bẫy suy diễn từ ngữ cảnh vận tải và chi phí.');
    /* --------------------------------------------------------------------------------------
       1.5. LISTENING - PART 4: TALKS (1 GROUP, 3 CÂU: Q10 - Q12, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP4 INT;
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP4 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript,
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP4, 1,
            N'Questions 10 through 12 refer to the following announcement.',
            N'Attention, attendees of the Global FinTech Summit. Due to an unexpected audio-visual malfunction in Ballroom A, this morning''s keynote address by Dr. Elena Rostova on cybersecurity trends has been relocated to the Sapphire Auditorium on the mezzanine level. Because the Sapphire Auditorium has a slightly smaller seating capacity, overflow seating with a live video feed will be available in Room 204. Also, please remember to scan your digital conference badge at the entrance of the auditorium if you wish to receive Continuing Professional Education credits for attending this session. Thank you for your cooperation.',
            NULL, NULL, N'/audio/toeic/test1-p4.mp3', 0, 52000
        );
    END
    SELECT @grpP4 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP4 AND DisplayOrder = 1;

    -- Câu 10 (P4)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 10)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP4, @examId, @secP4, @toeicList,
            N'Why is the keynote address being moved to a different location?',
            2, 'mcq',
            N'Bài phát biểu chính phải chuyển phòng vì sự cố thiết bị nghe nhìn (audio-visual malfunction) tại Ballroom A.',
            10
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 10;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'More people registered than originally anticipated.', 0, N'Bẫy đảo ngược nguyên nhân: Bài nói có nhắc tới "smaller seating capacity" ở phòng mới, dễ làm thí sinh tưởng nhầm là đổi phòng vì quá đông người.'),
        (@qId, 'mcq', 'B', N'There is a technical problem with the room''s equipment.', 1, N'Đáp án đúng: Paraphrase chuẩn xác cụm "unexpected audio-visual malfunction in Ballroom A".'),
        (@qId, 'mcq', 'C', N'The keynote speaker requested a smaller stage.', 0, N'Bẫy thông tin sai lệch: Diễn giả Dr. Elena Rostova không hề yêu cầu đổi sân khấu.'),
        (@qId, 'mcq', 'D', N'Renovation work is taking place on the mezzanine level.', 0, N'Bẫy ghép từ: "mezzanine level" là tầng của hội trường mới (Sapphire Auditorium), không phải nơi đang sửa chữa.');

    -- Câu 11 (P4)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 11)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP4, @examId, @secP4, @toeicList,
            N'What is available in Room 204?',
            2, 'mcq',
            N'Phòng 204 được bố trí làm phòng dự phòng (overflow seating) có truyền hình trực tiếp (live video feed).',
            11
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 11;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'Complimentary refreshments', 0, N'Bẫy kịch bản hội thảo quen thuộc nhưng không được đề cập trong bài.'),
        (@qId, 'mcq', 'B', N'Printed copies of the presentation slides', 0, N'Bẫy suy diễn không có trong transcript.'),
        (@qId, 'mcq', 'C', N'A live broadcast of the speech', 1, N'Đáp án đúng: "A live broadcast" tương đương với "a live video feed" trong bài thông báo.'),
        (@qId, 'mcq', 'D', N'Technical support for digital badges', 0, N'Bẫy trộn từ: "digital badge" xuất hiện ở câu sau nhưng dùng để quét tại cửa hội trường.');

    -- Câu 12 (P4)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 12)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP4, @examId, @secP4, @toeicList,
            N'How can attendees earn professional credits?',
            2, 'mcq',
            N'Người tham dự cần quét thẻ hội nghị điện tử (scan digital conference badge) tại lối vào hội trường.',
            12
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 12;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'By completing an online feedback survey', 0, N'Bẫy thông lệ thực tế nhưng không có trong bài nói.'),
        (@qId, 'mcq', 'B', N'By signing a registration sheet at the front desk', 0, N'Bẫy phương thức điểm danh truyền thống (signing a sheet) thay vì quét thẻ kỹ thuật số.'),
        (@qId, 'mcq', 'C', N'By scanning their badges at the auditorium entrance', 1, N'Đáp án đúng: Trùng khớp trực tiếp với chỉ dẫn "scan your digital conference badge at the entrance of the auditorium".'),
        (@qId, 'mcq', 'D', N'By submitting a summary to Dr. Rostova', 0, N'Bẫy ghép tên nhân vật trong bài vào hành động sai.');
    /* --------------------------------------------------------------------------------------
       1.6. READING - PART 5: INCOMPLETE SENTENCES (8 CÂU: Q13 - Q20, 8 CHỦ ĐIỂM NGỮ PHÁP)
       -------------------------------------------------------------------------------------- */
    -- Câu 13 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 13)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 13);
    END
    DECLARE @grpQ13 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 13);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 13)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ13, @examId, @secP5, @toeicRead,
            N'All reimbursement requests must be submitted to the accounting department ------- the fifteenth of each month.',
            2, 'mcq',
            N'Phân biệt giới từ chỉ hạn chót "by" (trước hoặc muộn nhất là thời điểm nào) và "until" (kéo dài liên tục đến thời điểm nào). Động từ "submit" là hành động điểm nên bắt buộc đi với "by".',
            13
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 13;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'by', 1, N'Đáp án đúng: Giới từ "by + mốc thời gian" diễn tả hạn chót (deadline) cho một hành động hoàn tất như "submitted".'),
        (@qId, 'mcq', 'B', N'until', 0, N'Bẫy giới từ kinh điển: "until" chỉ hành động kéo dài liên tục, không dùng cho động từ hoàn tất một lần như "submit".'),
        (@qId, 'mcq', 'C', N'during', 0, N'Bẫy giới từ: "during" phải đi với một khoảng thời gian danh từ (e.g., during the month), không đi với ngày cụ thể "the fifteenth".'),
        (@qId, 'mcq', 'D', N'among', 0, N'Bẫy nghĩa giới từ: "among" mang nghĩa "trong số (từ 3 đối tượng trở lên)", không dùng để chỉ mốc thời gian.');
    -- Câu 14 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 14)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 14);
    END
    DECLARE @grpQ14 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 14);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 14)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ14, @examId, @secP5, @toeicRead,
            N'By the time the chief executive officer arrives at the Tokyo headquarters tomorrow, the legal team ------- the merger contract.',
            2, 'mcq',
            N'Cấu trúc "By the time + S + V(hiện tại đơn), S + will have + V3/ed (tương lai hoàn thành)" diễn tả hành động sẽ hoàn tất trước một thời điểm hoặc hành động khác trong tương lai.',
            14
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 14;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'finalized', 0, N'Bẫy sai thì: Thì quá khứ đơn mâu thuẫn với trạng từ chỉ tương lai "tomorrow" và mệnh đề "arrives".'),
        (@qId, 'mcq', 'B', N'will have finalized', 1, N'Đáp án đúng: Thì tương lai hoàn thành (will have + V3) đi chuẩn với mệnh đề trạng ngữ "By the time + Hiện tại đơn".'),
        (@qId, 'mcq', 'C', N'had finalized', 0, N'Bẫy công thức máy móc: Nhiều thí sinh học vẹt "By the time" đi với "had V3" mà quên nhìn động từ "arrives ... tomorrow".'),
        (@qId, 'mcq', 'D', N'is finalizing', 0, N'Bẫy thì hiện tại tiếp diễn: Không phản ánh được tính hoàn tất trước một mốc tương lai của cấu trúc "By the time".');
    -- Câu 15 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 15)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 15);
    END
    DECLARE @grpQ15 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 15);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 15)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ15, @examId, @secP5, @toeicRead,
            N'The newly installed inventory tracking software has made warehouse operations ------- more efficient.',
            2, 'mcq',
            N'Trước tính từ so sánh hơn "more efficient" cần một trạng từ (adverb) để nhấn mạnh mức độ.',
            15
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 15;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'consider', 0, N'Bẫy từ loại: Động từ nguyên mẫu không thể đứng bổ nghĩa cho cụm tính từ so sánh "more efficient".'),
        (@qId, 'mcq', 'B', N'considerable', 0, N'Bẫy cấu trúc make + O + Adj: Thí sinh nhìn thấy "made warehouse operations" nên vội chọn tính từ mà bỏ sót cụm tính từ chính "more efficient" ở phía sau.'),
        (@qId, 'mcq', 'C', N'considerably', 1, N'Đáp án đúng: Trạng từ "considerably" bổ nghĩa mức độ cho tính từ so sánh hơn "more efficient".'),
        (@qId, 'mcq', 'D', N'consideration', 0, N'Bẫy từ loại: Danh từ không thể đứng giữa tân ngữ "operations" và cụm tính từ "more efficient".');
    -- Câu 16 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 16)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 16);
    END
    DECLARE @grpQ16 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 16);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 16)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ16, @examId, @secP5, @toeicRead,
            N'Although the marketing interns were given guidance, they designed the social media campaign entirely by -------.',
            2, 'mcq',
            N'Cụm cố định "by + đại từ phản thân (oneself)" mang nghĩa "tự lực / một mình không ai giúp đỡ". Chủ ngữ là "they" nên dùng "themselves".',
            16
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 16;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'they', 0, N'Bẫy sai hình thái đại từ: Đại từ nhân xưng chủ ngữ không thể đứng sau giới từ "by".'),
        (@qId, 'mcq', 'B', N'them', 0, N'Bẫy đại từ tân ngữ: Khi chủ ngữ và tân ngữ cùng chỉ một đối tượng ("they") và đi sau giới từ "by" để chỉ sự tự lực, phải dùng đại từ phản thân.'),
        (@qId, 'mcq', 'C', N'their', 0, N'Bẫy tính từ sở hữu: "their" không thể đứng độc lập ở cuối câu mà không có danh từ theo sau.'),
        (@qId, 'mcq', 'D', N'themselves', 1, N'Đáp án đúng: Cấu trúc "by themselves" = "on their own" (tự chính họ thực hiện).');
    -- Câu 17 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 17)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 17);
    END
    DECLARE @grpQ17 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 17);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 17)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ17, @examId, @secP5, @toeicRead,
            N'------- the rising cost of raw materials, Falcon Manufacturing managed to keep its retail prices stable this year.',
            2, 'mcq',
            N'Phía sau chỗ trống là một cụm danh từ ("the rising cost of raw materials", không có động từ chia thì) và mang nghĩa nhượng bộ tương phản, do đó cần giới từ "Despite".',
            17
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 17;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'Although', 0, N'Bẫy liên từ vs giới từ: "Although" cùng nghĩa tương phản nhưng là liên từ phụ thuộc, bắt buộc theo sau bởi một mệnh đề (S + V).'),
        (@qId, 'mcq', 'B', N'Despite', 1, N'Đáp án đúng: Giới từ chỉ sự nhượng bộ "Despite + Cụm danh từ (Noun Phrase)".'),
        (@qId, 'mcq', 'C', N'Because of', 0, N'Bẫy ngữ nghĩa: Đúng ngữ pháp giới từ nhưng sai logic nhân quả.'),
        (@qId, 'mcq', 'D', N'Unless', 0, N'Bẫy liên từ điều kiện: "Unless" phải đi với mệnh đề (S + V) và không hợp nghĩa câu.');
    -- Câu 18 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 18)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 18);
    END
    DECLARE @grpQ18 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 18);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 18)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ18, @examId, @secP5, @toeicRead,
            N'Any employee ------- to attend the leadership seminar in Chicago must obtain written approval from a department head.',
            2, 'mcq',
            N'Rút gọn mệnh đề quan hệ: Câu gốc là "Any employee who wishes to attend...". Động từ chính của câu đã là "must obtain", nên chỗ trống cần hiện tại phân từ chủ động "wishing".',
            18
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 18;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'wishes', 0, N'Bẫy hai động từ chia thì trong một mệnh đề đơn: Câu đã có động từ chính "must obtain".'),
        (@qId, 'mcq', 'B', N'wished', 0, N'Bẫy phân từ bị động: Động từ "wish" ở đây mang nghĩa chủ động, không thể rút gọn dạng V-ed bị động.'),
        (@qId, 'mcq', 'C', N'wishing', 1, N'Đáp án đúng: Rút gọn mệnh đề quan hệ chủ động (who wishes -> wishing).'),
        (@qId, 'mcq', 'D', N'to wish', 0, N'Bẫy động từ nguyên mẫu có "to": Không dùng "to V" để rút gọn mệnh đề quan hệ thông thường khi không có số thứ tự.');
    -- Câu 19 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 19)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 19);
    END
    DECLARE @grpQ19 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 19);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 19)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ19, @examId, @secP5, @toeicRead,
            N'A comprehensive review of the company''s cybersecurity protocols and backup systems ------- conducted every six months.',
            2, 'mcq',
            N'Sự hòa hợp Chủ ngữ - Vị ngữ: Chủ ngữ chính là danh từ số ít "A comprehensive review", không phải cụm danh từ số nhiều đứng ngay trước chỗ trống ("protocols and backup systems").',
            19
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 19;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'are', 0, N'Bẫy danh từ gần nhất (Proximity trap): Thí sinh nhìn thấy "protocols and backup systems" số nhiều nên chọn "are".'),
        (@qId, 'mcq', 'B', N'is', 1, N'Đáp án đúng: Động từ to-be số ít "is" hòa hợp với chủ ngữ chính "A comprehensive review" và tạo thể bị động "is conducted".'),
        (@qId, 'mcq', 'C', N'have', 0, N'Bẫy sai cả số lẫn thể.'),
        (@qId, 'mcq', 'D', N'were', 0, N'Bẫy sai số và sai thì (câu có "every six months" chỉ thói quen định kỳ ở hiện tại).');
    -- Câu 20 (P5)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 20)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
        VALUES (@examId, @toeicCert, @secP5, 20);
    END
    DECLARE @grpQ20 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP5 AND DisplayOrder = 20);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 20)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpQ20, @examId, @secP5, @toeicRead,
            N'The new biodegradable packaging is ------- durable enough for international shipping and cost-effective to produce.',
            2, 'mcq',
            N'Cặp liên từ tương quan: Phía sau có liên từ "and" nối hai tính từ, do đó vị trí phía trước bắt buộc là "both".',
            20
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 20;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'either', 0, N'Bẫy cặp liên từ: "either" phải đi với "or", không đi với "and".'),
        (@qId, 'mcq', 'B', N'neither', 0, N'Bẫy cặp liên từ: "neither" phải đi với "nor", không đi với "and".'),
        (@qId, 'mcq', 'C', N'whether', 0, N'Bẫy cặp liên từ: "whether" đi với "or".'),
        (@qId, 'mcq', 'D', N'both', 1, N'Đáp án đúng: Cặp liên từ tương quan chuẩn "both A and B".');
    /* --------------------------------------------------------------------------------------
       1.7. READING - PART 6: TEXT COMPLETION (1 GROUP, 4 CÂU: Q21 - Q24, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP6 INT;
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP6 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript,
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP6, 1,
            N'Questions 21 through 24 refer to the following memorandum.

To: All Regional Sales Representatives
From: Marcus Vance, Director of Operations
Date: October 12
Subject: Transition to CloudSync Expense Portal

Effective November 1, our company will retire the legacy spreadsheet system used for travel expense reporting and transition fully to the CloudSync Expense Portal. This upgrade is designed to [21] ------- the reimbursement process so that employees receive their funds within three business days rather than two weeks.

To ensure a smooth transition, all sales staff are required to complete a 30-minute interactive tutorial before October 25. [22] -------. Once you have finished the module, your login credentials will be activated automatically.

Please note that any paper receipts dated prior to November 1 must still be processed through the old system. [23] -------, expenses incurred on or after that date will only be accepted via the CloudSync mobile app or web dashboard. Should you encounter any technical difficulties during registration, please contact the IT Help Desk [24] ------- extension 4402.',
            NULL, NULL, NULL, NULL, NULL, NULL
        );
    END
    SELECT @grpP6 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP6 AND DisplayOrder = 1;

    -- Câu 21 (P6)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 21)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP6, @examId, @secP6, @toeicRead,
            N'Select the best word to fill in blank [21].',
            2, 'mcq',
            N'Ngữ cảnh phía sau giải thích nhân viên sẽ nhận được tiền hoàn lại trong 3 ngày thay vì 2 tuần, tức là quy trình được đẩy nhanh / tinh gọn ("streamline").',
            21
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 21;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'streamline', 1, N'Đáp án đúng: "streamline" (hợp lý hóa, tinh gọn) khớp hoàn hảo với việc rút ngắn thời gian từ 2 tuần xuống 3 ngày.'),
        (@qId, 'mcq', 'B', N'postpone', 0, N'Bẫy từ vựng ngược nghĩa: "postpone" (trì hoãn) mâu thuẫn với mục đích nâng cấp hệ thống.'),
        (@qId, 'mcq', 'C', N'complicate', 0, N'Bẫy ngữ nghĩa: Không ai nâng cấp phần mềm với mục đích làm phức tạp hóa ("complicate") quy trình.'),
        (@qId, 'mcq', 'D', N'reimburse', 0, N'Bẫy lặp từ trong ngữ cảnh: Phía sau đã có danh từ "reimbursement process".');

    -- Câu 22 (P6)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 22)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP6, @examId, @secP6, @toeicRead,
            N'Select the best sentence to fill in blank [22].',
            2, 'mcq',
            N'Câu trước nhắc tới "a 30-minute interactive tutorial", câu sau nhắc tới "Once you have finished the module...". Do đó câu ở vị trí [22] phải kết nối cách truy cập bài hướng dẫn.',
            22
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 22;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'The quarterly sales targets have been adjusted accordingly.', 0, N'Bẫy lạc đề dựa trên đối tượng người nhận: Đoạn văn đang nói về phần mềm hoàn ứng chi phí, không phải chỉ tiêu doanh số.'),
        (@qId, 'mcq', 'B', N'A link to the training video has been sent to your corporate e-mail address.', 1, N'Đáp án đúng: Liên kết mạch lạc giữa "complete a 30-minute interactive tutorial" ở câu trước và "finished the module" ở câu sau.'),
        (@qId, 'mcq', 'C', N'Several clients have requested digital copies of their contracts.', 0, N'Bẫy từ vựng "digital": Không liên quan đến quy trình đào tạo nội bộ.'),
        (@qId, 'mcq', 'D', N'Travel allowances will be reduced by ten percent next fiscal year.', 0, N'Bẫy liên tưởng từ "travel expense": Đưa thông tin cắt giảm ngân sách không ăn nhập với mạch hướng dẫn kích hoạt tài khoản.');

    -- Câu 23 (P6)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 23)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP6, @examId, @secP6, @toeicRead,
            N'Select the best transition word to fill in blank [23].',
            2, 'mcq',
            N'Câu trước nói hóa đơn trước ngày 1/11 vẫn xử lý qua hệ thống cũ; câu sau nói chi phí từ ngày 1/11 trở đi chỉ được nhận qua ứng dụng mới. Hai mệnh đề đối lập nhau nên dùng "However".',
            23
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 23;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'For instance', 0, N'Bẫy trạng từ liên kết: Câu sau không phải là ví dụ minh họa mà là quy định ngược lại.'),
        (@qId, 'mcq', 'B', N'Similarly', 0, N'Bẫy quan hệ tương đồng: Đây là sự tương phản, không phải tương đồng.'),
        (@qId, 'mcq', 'C', N'However', 1, N'Đáp án đúng: Thể hiện rõ sự đối lập quy trình giữa các hóa đơn trước ngày 1/11 và từ ngày 1/11 trở đi.'),
        (@qId, 'mcq', 'D', N'Therefore', 0, N'Bẫy quan hệ nhân quả: Quy định sau không phải là hệ quả suy ra từ quy định trước.');

    -- Câu 24 (P6)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 24)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP6, @examId, @secP6, @toeicRead,
            N'Select the best word to fill in blank [24].',
            2, 'mcq',
            N'Trong tiếng Anh thương mại, khi liên hệ qua số máy lẻ nội bộ (extension + số), giới từ chuẩn luôn là "at" (contact someone at extension 4402).',
            24
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 24;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'at', 1, N'Đáp án đúng: Collocation chuẩn "reach/contact someone at extension + [number]".'),
        (@qId, 'mcq', 'B', N'to', 0, N'Bẫy dịch từng từ: Động từ "contact" là ngoại động từ không đi với "to".'),
        (@qId, 'mcq', 'C', N'with', 0, N'Bẫy giới từ: "contact with" là lỗi sai phổ biến khi "contact" đóng vai trò là động từ.'),
        (@qId, 'mcq', 'D', N'for', 0, N'Bẫy giới từ chỉ mục đích.');
    /* --------------------------------------------------------------------------------------
       1.8. READING - PART 7: SINGLE PASSAGE (GROUP 1, 3 CÂU: Q25 - Q27, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP7_1 INT;
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP7 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript,
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP7, 1,
            N'Questions 25 through 27 refer to the following workshop schedule.

====================================================================
PACIFIC NORTHWEST SMALL BUSINESS SYMPOSIUM
Date: Friday, November 14 | Venue: Grand Harbor Convention Center
====================================================================

09:00 A.M. – 10:15 A.M. | Session 1: Mastering Supply Chain Resilience
Speaker: Marcus Thorne (COO, Cascade Freight Solutions)
Room: Olympic Hall
Overview: Learn practical strategies for diversifying suppliers and reducing shipping bottlenecks for regional retailers.

10:30 A.M. – 11:45 A.M. | Session 2: Digital Marketing on a Shoestring Budget
Speaker: Priya Patel (Founder, Spark Media Agency)
Room: Rainier Suite
Overview: Discover how to maximize customer engagement using targeted social media campaigns without hiring an external agency. *Note: Attendees should bring a fully charged laptop for the hands-on analytics exercise.*

01:00 P.M. – 02:15 P.M. | Session 3: Navigating Commercial Tax Reforms
Speaker: Evelyn Cho, CPA (Senior Partner, Cho & Associates)
Room: Columbia Room
Overview: A detailed breakdown of upcoming state tax incentives for businesses with fewer than 50 full-time employees.

*Registration includes complimentary morning coffee and a buffet lunch served in the Emerald Atrium from 11:45 A.M. to 1:00 P.M. Seating for Session 2 is limited to 40 participants; advance sign-up via the symposium mobile app is mandatory.*',
            NULL, NULL, NULL, NULL, NULL, NULL
        );
    END
    SELECT @grpP7_1 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP7 AND DisplayOrder = 1;

    -- Câu 25 (P7 Single)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 25)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_1, @examId, @secP7, @toeicRead,
            N'Who would most likely benefit from attending Session 3?',
            2, 'mcq',
            N'Session 3 trình bày về ưu đãi thuế bang dành cho các doanh nghiệp có dưới 50 nhân viên toàn thời gian (fewer than 50 full-time employees).',
            25
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 25;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'International freight forwarders', 0, N'Bẫy thông tin của phần khác: Liên quan tới nội dung chuỗi cung ứng ở Session 1.'),
        (@qId, 'mcq', 'B', N'Freelance graphic designers seeking agency jobs', 0, N'Bẫy từ khóa "agency" lấy từ Session 2.'),
        (@qId, 'mcq', 'C', N'Owners of companies with a small workforce', 1, N'Đáp án đúng: Paraphrase cụm "businesses with fewer than 50 full-time employees" thành "companies with a small workforce".'),
        (@qId, 'mcq', 'D', N'Software developers building mobile applications', 0, N'Bẫy từ khóa "mobile app" xuất hiện ở dòng chú thích cuối bài.');

    -- Câu 26 (P7 Single)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 26)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_1, @examId, @secP7, @toeicRead,
            N'What is indicated about Ms. Patel''s session?',
            2, 'mcq',
            N'Ms. Patel phụ trách Session 2. Chú thích trong Session 2 yêu cầu mang laptop, và chú thích cuối bài ghi rõ Session 2 giới hạn 40 chỗ ngồi và bắt buộc đăng ký trước qua ứng dụng.',
            26
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 26;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'It will take place in the Emerald Atrium.', 0, N'Bẫy địa điểm: Emerald Atrium là nơi phục vụ bữa trưa buffet, còn phiên của Ms. Patel diễn ra tại Rainier Suite.'),
        (@qId, 'mcq', 'B', N'It requires participants to register in advance.', 1, N'Đáp án đúng: Kết nối thông tin "Ms. Patel" -> "Session 2" -> "advance sign-up via the symposium mobile app is mandatory".'),
        (@qId, 'mcq', 'C', N'It is the first session of the morning.', 0, N'Bẫy thứ tự: Phiên đầu tiên lúc 9:00 sáng là của Mr. Marcus Thorne.'),
        (@qId, 'mcq', 'D', N'It charges an additional fee for laptop rentals.', 0, N'Bẫy suy diễn sai: Người tham gia được nhắc tự mang theo laptop đã sạc đầy, không có dịch vụ cho thuê thu phí.');

    -- Câu 27 (P7 Single)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 27)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_1, @examId, @secP7, @toeicRead,
            N'What is included with the symposium registration?',
            2, 'mcq',
            N'Dòng cuối cùng ghi rõ: "Registration includes complimentary morning coffee and a buffet lunch".',
            27
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 27;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'A private consultation with a tax accountant', 0, N'Bẫy suy diễn từ diễn giả Evelyn Cho, CPA ở Session 3.'),
        (@qId, 'mcq', 'B', N'Free parking at the Grand Harbor Convention Center', 0, N'Bẫy thông tin không được đề cập (Not mentioned trap).'),
        (@qId, 'mcq', 'C', N'A complimentary midday meal', 1, N'Đáp án đúng: Paraphrase chuẩn từ "complimentary ... buffet lunch" thành "complimentary midday meal".'),
        (@qId, 'mcq', 'D', N'A one-year subscription to analytics software', 0, N'Bẫy từ khóa "analytics" lấy từ bài tập thực hành ở Session 2.');

    /* --------------------------------------------------------------------------------------
       1.9. READING - PART 7: DOUBLE PASSAGE (GROUP 2, 3 CÂU: Q28 - Q30, 4 OPTIONS A-D)
       -------------------------------------------------------------------------------------- */
    DECLARE @grpP7_2 INT;
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP7 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.QuestionGroups (
            ExamId, CertId, SectionId, DisplayOrder, Passage, Transcript,
            ImagePath, ImageAlt, AudioPath, AudioStartMs, AudioEndMs
        )
        VALUES (
            @examId, @toeicCert, @secP7, 2,
            N'Questions 28 through 30 refer to the following invoice and e-mail.

--------------------------------------------------------------------
 PASSAGE 1: INVOICE #INV-8841
 Vendor: Lumina Botanicals Wholesale
 Client: Serenity Day Spa (Attn: Hannah Abbott)
 Order Date: March 3 | Delivery Date: March 6
--------------------------------------------------------------------
 Item Code | Description                        | Qty | Unit Price | Total
 LB-102    | Organic Lavender Essential Oil 1L  |  5  | $40.00     | $200.00
 LB-205    | Eucalyptus Massage Lotion 5L       |  4  | $55.00     | $220.00
 LB-310    | Bamboo Facial Towels (Pack of 50)  |  6  | $30.00     | $180.00
 LB-408    | Chamomile Soothing Serum 500ml     |  2  | $65.00     | $130.00
--------------------------------------------------------------------
 Subtotal: $730.00
 Shipping (Express Courier): $0.00 (Waived for orders over $500)
 Total Charged: $730.00
 *Return Policy: Claims for damaged or incorrect items must be submitted within 5 business days of delivery to qualify for a full refund or free replacement.*

--------------------------------------------------------------------
 PASSAGE 2: E-MAIL
 To: billing@luminabotanicals.com
 From: h.abbott@serenitydayspa.com
 Date: March 7
 Subject: Issue with Order #INV-8841
--------------------------------------------------------------------
 Dear Customer Support,

 Thank you for the prompt delivery of our wholesale order yesterday. Everything arrived intact except for one item. While the invoice correctly lists the Bamboo Facial Towels we ordered, the boxes we actually received contained standard cotton hand towels instead.

 Because we have a VIP wellness retreat scheduled for next weekend, we urgently need the correct towels sent out via express courier. Alternatively, if that item is currently out of stock, please issue a refund for that portion of the order to our corporate credit card so we can source them locally. We have left the unopened boxes of cotton towels at our reception desk for your driver to pick up.

 Sincerely,
 Hannah Abbott
 Operations Manager, Serenity Day Spa',
            NULL, NULL, NULL, NULL, NULL, NULL
        );
    END
    SELECT @grpP7_2 = GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secP7 AND DisplayOrder = 2;

    -- Câu 28 (P7 Double)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 28)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_2, @examId, @secP7, @toeicRead,
            N'Why did Ms. Abbott send the e-mail?',
            2, 'mcq',
            N'Trong email, bà Abbott báo rằng thay vì nhận được Bamboo Facial Towels như trên hóa đơn, spa lại nhận nhầm sang khăn tay cotton thông thường.',
            28
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 28;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'To complain about an unexpected shipping charge', 0, N'Bẫy thông tin ngược: Hóa đơn ghi rõ phí giao hàng là $0.00 (được miễn phí).'),
        (@qId, 'mcq', 'B', N'To report that some glass bottles were broken', 0, N'Bẫy từ khóa "damaged" ở phần chính sách đổi trả, nhưng email ghi rõ "Everything arrived intact".'),
        (@qId, 'mcq', 'C', N'To notify the vendor that an incorrect product was delivered', 1, N'Đáp án đúng: Spa đặt khăn mặt sợi tre nhưng lại bị giao nhầm khăn tay cotton.'),
        (@qId, 'mcq', 'D', N'To increase the quantity of lotion before the shipment goes out', 0, N'Bẫy sai trình tự thời gian: Đơn hàng đã được giao vào ngày hôm qua (March 6).');

    -- Câu 29 (P7 Double)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 29)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_2, @examId, @secP7, @toeicRead,
            N'If the requested item is out of stock, how much money should be refunded to Serenity Day Spa?',
            3, 'mcq',
            N'Suy luận chéo 2 văn bản: Trong email (Passage 2), mặt hàng bị giao sai cần hoàn tiền nếu hết hàng là "Bamboo Facial Towels". Đối chiếu lên Hóa đơn (Passage 1), mục "LB-310 | Bamboo Facial Towels" có tổng tiền (Total) là $180.00.',
            29
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 29;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'$30.00', 0, N'Bẫy đơn giá (Unit Price trap): $30.00 chỉ là giá của 1 gói khăn, trong khi spa đặt 6 gói (Tổng $180.00).'),
        (@qId, 'mcq', 'B', N'$130.00', 0, N'Bẫy nhầm dòng trên bảng hóa đơn: $130.00 là tổng tiền của mục LB-408 (Chamomile Soothing Serum).'),
        (@qId, 'mcq', 'C', N'$180.00', 1, N'Đáp án đúng: Suy luận chéo từ "Bamboo Facial Towels" trong email sang cột Total của mã LB-310 trên hóa đơn ($30.00 x 6 = $180.00).'),
        (@qId, 'mcq', 'D', N'$220.00', 0, N'Bẫy nhầm dòng trên bảng hóa đơn: $220.00 là tổng tiền của mục LB-205 (Eucalyptus Massage Lotion).');

    -- Câu 30 (P7 Double)
    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 30)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder
        )
        VALUES (
            @grpP7_2, @examId, @secP7, @toeicRead,
            N'Why is Serenity Day Spa eligible for a free replacement?',
            3, 'mcq',
            N'Suy luận chéo: Chính sách cuối hóa đơn (Passage 1) quy định khiếu nại gửi trong vòng 5 ngày làm việc kể từ ngày giao hàng (March 6) sẽ được đổi miễn phí. Email (Passage 2) được gửi ngay ngày March 7 (1 ngày sau khi nhận hàng).',
            30
        );
    END
    SELECT @qId = QuestionId FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 30;

    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionOptions WHERE QuestionId = @qId AND Label = 'A')
        INSERT INTO dbo.QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale) VALUES
        (@qId, 'mcq', 'A', N'Because the total order exceeded $500.00', 0, N'Bẫy nhầm điều kiện khuyến mãi: Đơn trên $500 là điều kiện để được miễn phí vận chuyển ban đầu, không phải điều kiện đổi trả hàng lỗi.'),
        (@qId, 'mcq', 'B', N'Because Ms. Abbott submitted the claim within five business days of delivery', 1, N'Đáp án đúng: Kết hợp ngày giao hàng (March 6) + chính sách 5 ngày trên Hóa đơn với ngày gửi email khiếu nại (March 7).'),
        (@qId, 'mcq', 'C', N'Because Serenity Day Spa is a first-time wholesale customer', 0, N'Bẫy thông tin không có trong cả hai văn bản.'),
        (@qId, 'mcq', 'D', N'Because the spa paid an extra fee for shipping insurance', 0, N'Bẫy suy diễn không có thật: Không có khoản phí bảo hiểm vận chuyển nào trên hóa đơn.');
    /* --------------------------------------------------------------------------------------
       1.10. WRITING - TASK 1 (SECTION W2 - Q31) & TASK 2 (SECTION W3 - Q32)
       LƯU Ý RÀNG BUỘC: QuestionType = 'free_response', TUYỆT ĐỐI KHÔNG TẠO QuestionOptions!
       -------------------------------------------------------------------------------------- */
    -- Câu 31 (Writing W2 - Respond to a Written Request)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secW2 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, ResponseSeconds)
        VALUES (@examId, @toeicCert, @secW2, 1, 600);
    END
    DECLARE @grpQ31 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secW2 AND DisplayOrder = 1);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 31)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder, PrepSeconds, ResponseSeconds, MinWords, MaxWords
        )
        VALUES (
            @grpQ31, @examId, @secW2, @toeicWrit,
            N'Directions: Read the e-mail below. You will have 10 minutes to read the e-mail and write your response. Your response should be between 60 and 100 words.

--------------------------------------------------------------------
From: David Kowalski, Procurement Manager, Apex Retail Group
To: Customer Fulfillment Team, Vanguard Electronics
Subject: Urgent – Delayed Delivery of Order #VE-9042
Sent: October 19, 10:15 A.M.

Dear Fulfillment Team,

We have still not received our shipment of 200 wireless barcode scanners (Order #VE-9042), which was guaranteed to arrive at our Chicago warehouse yesterday. Because our seasonal inventory audit begins this Thursday, this delay is disrupting our operations. Please explain why our shipment has been delayed and let us know what compensation or expedited shipping solution you can offer.

Regards,
David Kowalski
--------------------------------------------------------------------

Directions: Respond to the e-mail as if you are a Customer Fulfillment Representative at Vanguard Electronics. In your e-mail, do the following:
1. Apologize professionally and explain the specific reason for the shipping delay.
2. Confirm the exact new delivery date before Thursday.
3. Offer a clear form of compensation (e.g., partial refund, discount voucher, or waived shipping fee).
(Target length: 60–100 words | Time limit: 10 minutes)',
            3, 'free_response',
            N'Tiêu chí chấm điểm ETS Writing Part 2: (1) Giải quyết trọn vẹn cả 3 yêu cầu trong đề bài; (2) Văn phong email thương mại lịch sự, trang trọng; (3) Sử dụng chính xác các từ nối và cấu trúc câu phức; (4) Đạt độ dài tiêu chuẩn 60–100 từ.',
            31, 0, 600, 60, 100
        );
    END

    -- Câu 32 (Writing W3 - Write an Opinion Essay)
    IF NOT EXISTS (SELECT 1 FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secW3 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, ResponseSeconds)
        VALUES (@examId, @toeicCert, @secW3, 1, 1200);
    END
    DECLARE @grpQ32 INT = (SELECT GroupId FROM dbo.QuestionGroups WHERE ExamId = @examId AND SectionId = @secW3 AND DisplayOrder = 1);

    IF NOT EXISTS (SELECT 1 FROM dbo.Questions WHERE ExamId = @examId AND DisplayOrder = 32)
    BEGIN
        INSERT INTO dbo.Questions (
            GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType,
            Explanation, DisplayOrder, PrepSeconds, ResponseSeconds, MinWords, MaxWords
        )
        VALUES (
            @grpQ32, @examId, @secW3, @toeicWrit,
            N'Directions: Read the question below. You have 20 minutes to plan, write, and revise your essay. Your essay should be between 150 and 220 words.

--------------------------------------------------------------------
PROMPT:
Do you agree or disagree with the following statement?
"Remote work increases employee productivity more than working in a traditional office."
Use specific reasons and examples to support your answer.
--------------------------------------------------------------------

Essay Guidelines:
- State your position clearly in the introductory paragraph.
- Develop at least two well-structured body paragraphs with concrete business examples.
- Include a concise concluding paragraph summarizing your main argument.
(Target length: 150–220 words | Time limit: 20 minutes)',
            4, 'free_response',
            N'Tiêu chí chấm điểm ETS Writing Part 3: (1) Bố cục 3 phần (Introduction - Body Paragraphs - Conclusion) mạch lạc; (2) Lập luận nhất quán kèm ví dụ thực tiễn nơi công sở; (3) Đa dạng hóa cấu trúc ngữ pháp và từ vựng học thuật/thương mại; (4) Đạt độ dài 150–220 từ.',
            32, 0, 1200, 150, 220
        );
    END
    /* ======================================================================================
       PHẦN 2: LỘ TRÌNH ĐIỀU TRỊ ĐỘNG & BÀI HỌC GUIDED LESSONS (SPRINT 2-3 TUẦN)
       ====================================================================================== */
    DECLARE @tplId INT;
    DECLARE @tplName NVARCHAR(200) = N'TOEIC Diagnostic Sprint (Target B1/B2)';

    -- 2.1. Tạo Master PathTemplate
    IF NOT EXISTS (SELECT 1 FROM dbo.PathTemplates WHERE CertId = @toeicCert AND Name = @tplName)
    BEGIN
        INSERT INTO dbo.PathTemplates (
            CertId, Name, FromBandCode, ToBandCode
        )
        VALUES (
            @toeicCert, @tplName, 'A2', 'B1'
        );
    END
    SELECT @tplId = TemplateId FROM dbo.PathTemplates WHERE CertId = @toeicCert AND Name = @tplName;

    -- 2.2. Tạo 4 PathModules tương ứng với 4 điểm yếu điển hình
    DECLARE @mod1 INT, @mod2 INT, @mod3 INT, @mod4 INT;

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, Description, EstimatedMinutes)
        VALUES (
            @tplId, @toeicRead, @toeicCert, 1,
            N'Tense & Aspect Mastery',
            N'Làm chủ hệ thống thì động từ, sự phối hợp thì (By the time / Since) và thể bị động trong ngữ cảnh doanh nghiệp Part 5 & Part 6.',
            25
        );
    END
    SELECT @mod1 = ModuleId FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, Description, EstimatedMinutes)
        VALUES (
            @tplId, @toeicList, @toeicCert, 2,
            N'Auditory Traps & Indirect Responses',
            N'Nhận diện và hóa giải bẫy lặp từ (Same-word trap), bẫy đồng âm và câu trả lời gián tiếp trong TOEIC Listening Part 2.',
            25
        );
    END
    SELECT @mod2 = ModuleId FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 2;

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, Description, EstimatedMinutes)
        VALUES (
            @tplId, @toeicWrit, @toeicCert, 3,
            N'Business Email Structure & Tone',
            N'Chuẩn hóa cấu trúc email phản hồi đối tác, xử lý khiếu nàn và nâng cấp văn phong chuyên nghiệp cho TOEIC Writing Part 2 (Section W2).',
            25
        );
    END
    SELECT @mod3 = ModuleId FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 3;

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 4)
    BEGIN
        INSERT INTO dbo.PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, Description, EstimatedMinutes)
        VALUES (
            @tplId, @toeicRead, @toeicCert, 4,
            N'Cross-Text Inference & Speed Reading',
            N'Chiến thuật đọc lướt (Skimming/Scanning) và kỹ năng móc nối dữ kiện chéo giữa Hóa đơn - Email trong TOEIC Reading Part 7.',
            30
        );
    END
    SELECT @mod4 = ModuleId FROM dbo.PathModules WHERE CertId = @toeicCert AND TemplateId = @tplId AND DisplayOrder = 4;

    /* --------------------------------------------------------------------------------------
       2.3. TẠO 4 GUIDED LESSONS & 16 LESSON CARDS
       -------------------------------------------------------------------------------------- */
    DECLARE @les1 INT, @les2 INT, @les3 INT, @les4 INT;

    -- LESSON 1
    IF NOT EXISTS (SELECT 1 FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Tense & Aspect Mastery in Business Contexts')
    BEGIN
        INSERT INTO dbo.Lessons (CertId, SkillId, Title, Summary, EstimatedMinutes, DisplayOrder)
        VALUES (
            @toeicCert, @toeicRead,
            N'Tense & Aspect Mastery in Business Contexts',
            N'Phương pháp 3 giây nhận diện dấu hiệu phối hợp thì (Time Markers) và phân biệt chủ động - bị động trong Part 5.',
            15, 1
        );
    END
    SELECT @les1 = LessonId FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Tense & Aspect Mastery in Business Contexts';

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les1 AND DisplayOrder = 0)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les1, @toeicCert, 'rule',
            N'Quy tắc cốt lõi: Sự phối hợp thì theo trục thời gian (Timeline Alignment)',
            N'### 1. Công thức "By the time" kinh điển trong TOEIC' + CHAR(10) +
            N'- **Tương lai hoàn thành**: `By the time + S + V(hiện tại đơn), S + will have + V3/ed`' + CHAR(10) +
            N'  *(Diễn tả một dự án/báo cáo sẽ hoàn tất trước khi một sự kiện tương lai khác diễn ra)*.' + CHAR(10) +
            N'- **Quá khứ hoàn thành**: `By the time + S + V(quá khứ đơn), S + had + V3/ed`' + CHAR(10) +
            N'  *(Diễn tả hành động đã xong trước một mốc trong quá khứ)*.' + CHAR(10) + CHAR(10) +
            N'### 2. Kiểm tra Thể Bị Động (Passive Voice Check)' + CHAR(10) +
            N'- Nếu sau chỗ trống **KHÔNG có cụm danh từ làm tân ngữ** (mà là giới từ `by`, `in`, `at`, `to` hoặc dấu chấm câu) và động từ cần điền là ngoại động từ (`submit`, `approve`, `conduct`, `finalize`), hãy ưu tiên **Thể bị động (`be + V3/ed`)**.',
            0
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les1 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les1, @toeicCert, 'pitfall',
            N'Bẫy kinh điển: Học vẹt dấu hiệu mà bỏ quên động từ mệnh đề phụ',
            N'Thí sinh thường phản xạ máy móc khi thấy `By the time` là chọn ngay `had + V3` mà không kiểm tra thì của mệnh đề đi kèm.' + CHAR(10) + CHAR(10) +
            N'- ❌ **Sai lầm phổ biến**: *By the time the auditor **arrives** next Monday, we **had prepared** all tax documents.*' + CHAR(10) +
            N'  *(Lỗi lệch pha thời gian: `arrives` là hiện tại chỉ tương lai nhưng mệnh đề chính lại lùi về quá khứ hoàn thành)*.' + CHAR(10) + CHAR(10) +
            N'- ✔️ **Chuẩn ETS**: *By the time the auditor **arrives** next Monday, we **will have prepared** all tax documents.*' + CHAR(10) +
            N'  *(Mệnh đề phụ chia Hiện tại đơn `arrives` -> Mệnh đề chính bắt buộc chia Tương lai hoàn thành `will have V3`)*.',
            1
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les1 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les1, @toeicCert, 'example',
            N'Phân tích ngữ cảnh thực tế trong đề thi TOEIC',
            N'> *"Ms. Gomez **has overseen** the regional logistics department since the company **expanded** its operations to Southeast Asia in 2022."*' + CHAR(10) + CHAR(10) +
            N'**Giải phẫu câu:**' + CHAR(10) +
            N'1. Mệnh đề sau `since` chỉ một mốc sự kiện đã xảy ra và khép lại vào năm 2022 -> Dùng **Quá khứ đơn (`expanded`)**.' + CHAR(10) +
            N'2. Mệnh đề chính diễn tả trách nhiệm quản lý bắt đầu từ năm 2022 và kéo dài liên tục đến hiện tại -> Dùng **Hiện tại hoàn thành (`has overseen`)**.',
            2
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les1 AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.LessonCards (
            LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder,
            QuestionPrompt, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation, Rationale
        )
        VALUES (
            @les1, @toeicCert, 'micro_check',
            N'Kiểm tra nhanh: Phối hợp thì & Thể bị động',
            N'Áp dụng quy tắc trục thời gian và kiểm tra tân ngữ phía sau chỗ trống để chọn đáp án đúng.',
            3,
            N'By the time the board of directors convenes at 3:00 P.M. today, the revised financial report ------- to all shareholders.',
            N'distributed',
            N'had been distributed',
            N'will have been distributed',
            N'is distributing',
            'C',
            N'Mệnh đề "By the time + convenes (hiện tại đơn) ... today" đòi hỏi thì Tương lai hoàn thành ở mệnh đề chính. Đồng thời chủ ngữ "the revised financial report" chịu tác động và sau chỗ trống là giới từ "to" (không có tân ngữ) nên phải chia ở thể bị động: "will have been distributed".',
            N'(A) Sai thì và thiếu bị động; (B) Bẫy quá khứ hoàn thành với "By the time"; (C) Đúng cả thì tương lai hoàn thành lẫn thể bị động; (D) Sai thì và sai thể chủ động.'
        );
    END

    -- LESSON 2
    IF NOT EXISTS (SELECT 1 FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Eliminating Auditory Traps & Mastering Indirect Responses')
    BEGIN
        INSERT INTO dbo.Lessons (CertId, SkillId, Title, Summary, EstimatedMinutes, DisplayOrder)
        VALUES (
            @toeicCert, @toeicList,
            N'Eliminating Auditory Traps & Mastering Indirect Responses',
            N'Kỹ thuật loại trừ đáp án lặp âm/lặp từ và nhận diện các mẫu câu trả lời gián tiếp đạt điểm tuyệt đối Part 2.',
            15, 2
        );
    END
    SELECT @les2 = LessonId FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Eliminating Auditory Traps & Mastering Indirect Responses';

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les2 AND DisplayOrder = 0)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les2, @toeicCert, 'rule',
            N'Quy tắc cốt lõi: 3 Dạng trả lời gián tiếp (Indirect Responses) phổ biến nhất',
            N'Trong đề thi TOEIC hiện đại, hơn **45% câu hỏi Part 2** không trả lời trực tiếp vào từ để hỏi mà sử dụng phản hồi gián tiếp:' + CHAR(10) + CHAR(10) +
            N'1. **Báo không biết / Chưa quyết định**: *"I haven''t checked the schedule yet"*, *"It hasn''t been finalized"*.' + CHAR(10) +
            N'2. **Chuyển hướng sang người hoặc tài liệu khác**: *"Ask Ms. Carter in Accounting"*, *"Check the memo on the bulletin board"*.' + CHAR(10) +
            N'3. **Đưa ra lý do cản trở thay vì nói Yes/No**: Khi được hỏi *"Have you submitted the report?"*, người nghe trả lời *"The network has been down all morning"* (ngầm hiểu là *Chưa nộp*).',
            0
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les2 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les2, @toeicCert, 'pitfall',
            N'Bẫy kinh điển: Bẫy lặp từ (Same-Word Trap) & Bẫy đồng âm (Sound-Alike Trap)',
            N'Nguyên tắc vàng của ETS: **Đáp án nào lặp lại nguyên xi danh từ/động từ của câu hỏi hoặc dùng từ phát âm na ná thì 90% là bẫy!**' + CHAR(10) + CHAR(10) +
            N'- **Câu hỏi**: *"Where did you leave the **client contract**?"*' + CHAR(10) +
            N'  - ❌ **Bẫy lặp từ & đồng âm**: *"Yes, my **contact** lens is on the desk."* (Lặp âm `contract` -> `contact` và dùng `Yes` cho câu hỏi `Where`).' + CHAR(10) +
            N'  - ✔️ **Đáp án chuẩn**: *"I gave it to Mr. Evans before lunch."* (Trả lời gián tiếp vị trí của hợp đồng bằng hành động đã đưa cho ông Evans).',
            1
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les2 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les2, @toeicCert, 'example',
            N'Phân tích câu hỏi đề xuất "Why don''t we..." trong Part 2',
            N'> **Question**: *"Why don''t we interview the final candidate via video conference?"*' + CHAR(10) + CHAR(10) +
            N'**Phân tích phản xạ:**' + CHAR(10) +
            N'- Mặc dù bắt đầu bằng `Why`, đây là **lời đề nghị (Suggestion)** tương đương với `Let''s...`.' + CHAR(10) +
            N'- Loại bỏ ngay các phương án bắt đầu bằng `Because...`.' + CHAR(10) +
            N'- **Đáp án đúng**: *"That would save travel expenses."* (Đồng tình gián tiếp bằng cách nêu lợi ích của đề xuất).',
            2
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les2 AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.LessonCards (
            LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder,
            QuestionPrompt, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation, Rationale
        )
        VALUES (
            @les2, @toeicCert, 'micro_check',
            N'Kiểm tra nhanh: Nhận diện phản hồi gián tiếp',
            N'Đọc câu hỏi dưới đây và loại bỏ các bẫy âm thanh/lặp từ để chọn phản hồi tự nhiên nhất.',
            3,
            N'Question: "How much will it cost to upgrade our server capacity?" — Which response is best?',
            N'Yes, the server is at full capacity.',
            N'I''m still waiting for a quote from the vendor.',
            N'Along the coast of California.',
            N'To improve our data processing speed.',
            'B',
            N'Câu hỏi "How much" hỏi về chi phí, nhưng người trả lời chưa có con số chính xác vì vẫn đang đợi bảng báo giá (waiting for a quote) từ nhà cung cấp — đây là phản hồi gián tiếp cực kỳ phổ biến trong TOEIC.',
            N'(A) Bẫy Yes/No cho câu hỏi Wh- kèm lặp từ "server" và "capacity"; (B) Đáp án đúng dạng gián tiếp; (C) Bẫy đồng âm "cost" và "coast"; (D) Bẫy trả lời cho câu hỏi mục đích "Why" thay vì "How much".'
        );
    END

    -- LESSON 3
    IF NOT EXISTS (SELECT 1 FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'High-Scoring Business Email Architecture (Writing W2)')
    BEGIN
        INSERT INTO dbo.Lessons (CertId, SkillId, Title, Summary, EstimatedMinutes, DisplayOrder)
        VALUES (
            @toeicCert, @toeicWrit,
            N'High-Scoring Business Email Architecture (Writing W2)',
            N'Khung sườn 4 bước viết email giải quyết sự cố thương mại trong 10 phút với văn phong trang trọng chuẩn ETS.',
            20, 3
        );
    END
    SELECT @les3 = LessonId FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'High-Scoring Business Email Architecture (Writing W2)';

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les3 AND DisplayOrder = 0)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les3, @toeicCert, 'rule',
            N'Quy tắc cốt lõi: Khung 4 đoạn chuẩn mực cho Business Email',
            N'Để đạt điểm tối đa (Level 5) trong TOEIC Writing Task 1, email phải tuân thủ nghiêm ngặt 4 khối nội dung:' + CHAR(10) + CHAR(10) +
            N'1. **Salutation & Purpose (Chào hỏi & Nêu mục đích)**: *"Thank you for contacting us regarding..."*, *"I am writing to apologize for the delay in..."*' + CHAR(10) +
            N'2. **Cause & Details (Giải trình nguyên nhân)**: Trình bày lý do khách quan bằng cấu trúc bị động hoặc danh từ hóa (*"Due to an unexpected supply chain disruption..."*).' + CHAR(10) +
            N'3. **Action & Compensation (Giải pháp & Đền bù)**: Đưa ra mốc thời gian giải quyết cụ thể và phương án bồi thường thỏa đáng (*"We have dispatched a replacement order via express air freight..."*).' + CHAR(10) +
            N'4. **Professional Closing (Lời chúc & Ký tên)**: *"Please accept our sincere apologies for any inconvenience caused. Sincerely, [Name]"*.',
            0
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les3 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les3, @toeicCert, 'pitfall',
            N'Bẫy kinh điển: Thiếu một trong các yêu cầu của đề bài (Missing Task Element)',
            N'ETS luôn đưa ra **từ 2 đến 3 chỉ dẫn bắt buộc** trong phần Directions (ví dụ: giải thích lý do, hẹn ngày mới, và đề xuất bồi thường).' + CHAR(10) + CHAR(10) +
            N'- ❌ **Lỗi mất điểm nặng nhất**: Dù tiếng Anh của bạn hoàn hảo, ngữ pháp không sai một lỗi nào, nhưng nếu bạn **quên đề xuất phương án bồi thường**, bài viết sẽ bị giới hạn ở mức **điểm 3/5**!' + CHAR(10) +
            N'- ✔️ **Kỹ thuật Check-off**: Trước khi bấm nộp bài, hãy dành 60 giây đối chiếu từng gạch đầu dòng của đề bài xem đã có trong bài viết chưa.',
            1
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les3 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les3, @toeicCert, 'example',
            N'Mẫu câu ghi điểm tuyệt đối cho phần Đền bù (Compensation)',
            N'- *"To compensate for this inconvenience, we would like to offer a 15% discount on your next wholesale order."*' + CHAR(10) +
            N'- *"In addition, we have waived all freight charges for this delivery and credited your corporate account accordingly."*' + CHAR(10) +
            N'- *"As a gesture of goodwill, please accept a complimentary one-year warranty extension on all delivered equipment."*',
            2
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les3 AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.LessonCards (
            LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder,
            QuestionPrompt, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation, Rationale
        )
        VALUES (
            @les3, @toeicCert, 'micro_check',
            N'Kiểm tra nhanh: Lựa chọn câu đền bù chuẩn văn phong thương mại',
            N'Chọn câu văn thể hiện thái độ đền bù khách hàng chuyên nghiệp và trang trọng nhất.',
            3,
            N'Which sentence best demonstrates appropriate tone and professional vocabulary when offering compensation?',
            N'We feel really bad, so you don''t have to pay for shipping this time.',
            N'As a gesture of goodwill, we have waived the shipping fee and credited a 10% discount to your account.',
            N'You should take a 10% discount because our delivery driver was late.',
            N'If you want your money back, tell our accounting team right now.',
            'B',
            N'Phương án (B) sử dụng cụm từ thương mại chuẩn mực "As a gesture of goodwill", động từ học thuật "waived" (miễn phí) và "credited" (hoàn/cộng vào tài khoản), thể hiện sự chủ động và tôn trọng khách hàng.',
            N'(A) Quá suồng sã ("feel really bad"); (B) Chuẩn văn phong Business English; (C) Thiếu lịch sự và đổ lỗi trực tiếp; (D) Giọng điệu mệnh lệnh gây phản cảm cho đối tác.'
        );
    END

    -- LESSON 4
    IF NOT EXISTS (SELECT 1 FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Cross-Text Inference in Multi-Passage Reading (Part 7)')
    BEGIN
        INSERT INTO dbo.Lessons (CertId, SkillId, Title, Summary, EstimatedMinutes, DisplayOrder)
        VALUES (
            @toeicCert, @toeicRead,
            N'Cross-Text Inference in Multi-Passage Reading (Part 7)',
            N'Phương pháp tìm "Từ khóa cầu nối" (Bridge Keywords) để giải quyết câu hỏi suy luận chéo giữa Hóa đơn, Lịch trình và Email.',
            20, 4
        );
    END
    SELECT @les4 = LessonId FROM dbo.Lessons WHERE CertId = @toeicCert AND Title = N'Cross-Text Inference in Multi-Passage Reading (Part 7)';

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les4 AND DisplayOrder = 0)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les4, @toeicCert, 'rule',
            N'Quy tắc cốt lõi: Kỹ thuật "Cầu nối thực thể" (Entity Bridging)',
            N'Trong bài đọc ghép (Double/Triple Passages), câu hỏi suy luận chéo **không bao giờ** nêu trực tiếp đáp án ở một văn bản duy nhất. Hãy thực hiện 3 bước:' + CHAR(10) + CHAR(10) +
            N'1. **Xác định Clue A ở Văn bản 2 (Email/Review)**: Tìm mô tả gián tiếp (ví dụ: *"the item that arrived damaged"*, *"the session conducted by the CPA"*).' + CHAR(10) +
            N'2. **Tìm Điểm neo (Anchor/Bridge)**: Lấy tên món hàng, mã đơn, hoặc ngày tháng làm cầu nối.' + CHAR(10) +
            N'3. **Tra cứu Clue B ở Văn bản 1 (Invoice/Schedule)**: Đối chiếu sang cột Giá tổng (`Total`), Mã sản phẩm (`Item Code`), hoặc Điều khoản (`Policy Note`) ở cuối bảng.',
            0
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les4 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les4, @toeicCert, 'pitfall',
            N'Bẫy kinh điển: Bẫy Đơn giá vs Tổng tiền & Bỏ qua dòng chú thích (*Asterisk Note*)',
            N'Hai cái bẫy lấy đi nhiều điểm nhất của thí sinh ở Part 7:' + CHAR(10) + CHAR(10) +
            N'- ❌ **Bẫy cột Unit Price (Đơn giá)**: Khi câu hỏi hỏi *"How much will be refunded for the towels?"*, thí sinh dò đúng dòng Khăn nhưng lại lấy con số ở cột `Unit Price ($30.00)` thay vì cột `Total ($180.00 cho 6 gói)`.' + CHAR(10) +
            N'- ❌ **Bỏ qua dòng chữ nhỏ có dấu hoa thị (`*`) ở đáy bảng**: hơn **70% đáp án suy luận** về điều kiện miễn phí, hạn chót đổi trả hoặc yêu cầu đăng ký trước đều nằm ở dòng `*Note:` cuối bảng hoặc cuối hóa đơn!',
            1
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les4 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
        VALUES (
            @les4, @toeicCert, 'example',
            N'Minh họa chuỗi suy luận chéo thực tế',
            N'- **Dữ kiện 1 (Trên Hóa đơn)**:' + CHAR(10) +
            N'  - `Delivery Date: March 6`' + CHAR(10) +
            N'  - `*Note: Claims submitted within 5 business days of delivery qualify for a free replacement.`' + CHAR(10) +
            N'- **Dữ kiện 2 (Trên Email)**:' + CHAR(10) +
            N'  - `Sent Date: March 7` (Tức là chỉ **1 ngày** sau khi giao hàng).' + CHAR(10) +
            N'- **Kết luận suy luận**: Khách hàng hoàn toàn đủ điều kiện nhận hàng thay thế miễn phí vì gửi yêu cầu trong hạn 5 ngày làm việc.',
            2
        );
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.LessonCards WHERE CertId = @toeicCert AND LessonId = @les4 AND DisplayOrder = 3)
    BEGIN
        INSERT INTO dbo.LessonCards (
            LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder,
            QuestionPrompt, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation, Rationale
        )
        VALUES (
            @les4, @toeicCert, 'micro_check',
            N'Kiểm tra nhanh: Thực hành suy luận chéo (Cross-Text Inference)',
            N'Đọc nhanh 2 trích đoạn ngắn dưới đây và trả lời câu hỏi suy luận.',
            3,
            N'[Menu]: "Package A ($15/person): Sandwiches & Salad | Package B ($22/person): Hot Pasta Buffet. *Orders for 20 or more people receive free delivery."
[Email]: "We would like to order the Hot Pasta Buffet for our 25-member department meeting." — How much will the delivery fee be?',
            N'$0.00',
            N'$15.00',
            N'$22.00',
            N'$25.00',
            'A',
            N'Email đặt suất ăn cho 25 người ("25-member department"). Đối chiếu với chú thích có dấu hoa thị (*) trên Menu: đơn hàng từ 20 người trở lên được miễn phí giao hàng ("free delivery"), do đó phí giao hàng là $0.00.',
            N'(A) Đúng nhờ kết nối số lượng 25 người với điều kiện >= 20 người ở dòng chú thích; (B) Bẫy giá của Package A; (C) Bẫy đơn giá của Package B; (D) Bẫy con số 25 thành viên trong email.'
        );
    END

    /* --------------------------------------------------------------------------------------
       2.4. LIÊN KẾT PATH MODULE ITEMS & TOPICS
       -------------------------------------------------------------------------------------- */
    -- Module 1 Items
    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod1 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefLessonId, DisplayOrder)
        VALUES (@mod1, @toeicCert, @tplId, 'lesson', @les1, 1);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod1 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
        VALUES (@mod1, @toeicCert, @tplId, 'practice_section', @secP5, 20, 2);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleTopics WHERE ModuleId = @mod1 AND Title = N'Tense & Aspect Foundations')
    BEGIN
        INSERT INTO dbo.PathModuleTopics (ModuleId, Title, QuestionCount, DisplayOrder)
        VALUES (@mod1, N'Tense & Aspect Foundations', 15, 1);
    END

    -- Module 2 Items
    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod2 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefLessonId, DisplayOrder)
        VALUES (@mod2, @toeicCert, @tplId, 'lesson', @les2, 1);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod2 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
        VALUES (@mod2, @toeicCert, @tplId, 'practice_section', @secP2, 25, 2);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleTopics WHERE ModuleId = @mod2 AND Title = N'Auditory Distractors & Traps')
    BEGIN
        INSERT INTO dbo.PathModuleTopics (ModuleId, Title, QuestionCount, DisplayOrder)
        VALUES (@mod2, N'Auditory Distractors & Traps', 25, 1);
    END

    -- Module 3 Items
    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod3 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefLessonId, DisplayOrder)
        VALUES (@mod3, @toeicCert, @tplId, 'lesson', @les3, 1);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod3 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
        VALUES (@mod3, @toeicCert, @tplId, 'practice_section', @secW2, 2, 2);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleTopics WHERE ModuleId = @mod3 AND Title = N'Business Email Formatting')
    BEGIN
        INSERT INTO dbo.PathModuleTopics (ModuleId, Title, QuestionCount, DisplayOrder)
        VALUES (@mod3, N'Business Email Formatting', 5, 1);
    END

    -- Module 4 Items
    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod4 AND DisplayOrder = 1)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefLessonId, DisplayOrder)
        VALUES (@mod4, @toeicCert, @tplId, 'lesson', @les4, 1);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleItems WHERE CertId = @toeicCert AND ModuleId = @mod4 AND DisplayOrder = 2)
    BEGIN
        INSERT INTO dbo.PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
        VALUES (@mod4, @toeicCert, @tplId, 'practice_section', @secP7, 20, 2);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PathModuleTopics WHERE ModuleId = @mod4 AND Title = N'Double Passage Cross-Inference')
    BEGIN
        INSERT INTO dbo.PathModuleTopics (ModuleId, Title, QuestionCount, DisplayOrder)
        VALUES (@mod4, N'Double Passage Cross-Inference', 15, 1);
    END
GO

PRINT '17. 3-Skill Diagnostic Benchmark and dynamic path template seeded';
GO

PRINT 'All demo data seeded successfully.';
GO

-- ============================================================================
-- 18. IELTS and VSTEP diagnostic benchmarks, free writing extensions, and paths
-- ============================================================================
DECLARE @diagIeltsCert INT = (SELECT CertId FROM Certificates WHERE Code = 'IELTS');
DECLARE @diagVstepCert INT = (SELECT CertId FROM Certificates WHERE Code = 'VSTEP');

DECLARE @diagIeltsList INT = (SELECT SkillId FROM Skills WHERE CertId = @diagIeltsCert AND Code = 'LIST');
DECLARE @diagIeltsRead INT = (SELECT SkillId FROM Skills WHERE CertId = @diagIeltsCert AND Code = 'READ');
DECLARE @diagIeltsWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @diagIeltsCert AND Code = 'WRIT');
DECLARE @diagVstepList INT = (SELECT SkillId FROM Skills WHERE CertId = @diagVstepCert AND Code = 'LIST');
DECLARE @diagVstepRead INT = (SELECT SkillId FROM Skills WHERE CertId = @diagVstepCert AND Code = 'READ');
DECLARE @diagVstepWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @diagVstepCert AND Code = 'WRIT');

DECLARE @diagIeltsListeningSection INT = (SELECT SectionId FROM Sections WHERE CertId = @diagIeltsCert AND Code = 'REC');
DECLARE @diagIeltsReadingSection INT = (SELECT SectionId FROM Sections WHERE CertId = @diagIeltsCert AND Code = 'PAS');
DECLARE @diagIeltsTask1Section INT = (SELECT SectionId FROM Sections WHERE CertId = @diagIeltsCert AND Code = 'T1');
DECLARE @diagIeltsTask2Section INT = (SELECT SectionId FROM Sections WHERE CertId = @diagIeltsCert AND Code = 'T2');
DECLARE @diagVstepListeningSection INT = (SELECT SectionId FROM Sections WHERE CertId = @diagVstepCert AND Code = 'L3');
DECLARE @diagVstepReadingSection INT = (SELECT SectionId FROM Sections WHERE CertId = @diagVstepCert AND Code = 'R4');
DECLARE @diagVstepEmailSection INT = (SELECT SectionId FROM Sections WHERE CertId = @diagVstepCert AND Code = 'W1');
DECLARE @diagVstepEssaySection INT = (SELECT SectionId FROM Sections WHERE CertId = @diagVstepCert AND Code = 'W2');

IF @diagIeltsCert IS NULL OR @diagVstepCert IS NULL
   OR @diagIeltsList IS NULL OR @diagIeltsRead IS NULL OR @diagIeltsWrit IS NULL
   OR @diagVstepList IS NULL OR @diagVstepRead IS NULL OR @diagVstepWrit IS NULL
   OR @diagIeltsListeningSection IS NULL OR @diagIeltsReadingSection IS NULL
   OR @diagIeltsTask1Section IS NULL OR @diagIeltsTask2Section IS NULL
   OR @diagVstepListeningSection IS NULL OR @diagVstepReadingSection IS NULL
   OR @diagVstepEmailSection IS NULL OR @diagVstepEssaySection IS NULL
  THROW 50050, 'IELTS/VSTEP diagnostic seed requires the complete certificate catalog.', 1;

-- Upgrade the earlier two-question placeholders in place; certificate-scoped lookup keeps
-- existing exam IDs and all related references stable without assuming identity values.
UPDATE Exams
SET Name = N'IELTS-DIAG-01',
    DurationMinutes = 60,
    QuestionCount = 10,
    Description = N'Free IELTS baseline benchmark: Listening and Academic Reading, followed by a separately graded free diagnostic Writing extension.'
WHERE CertId = @diagIeltsCert
  AND Name IN (N'Cambridge IELTS — Placement Diagnostic', N'IELTS-DIAG-01');

IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @diagIeltsCert AND Name = N'IELTS-DIAG-01')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@diagIeltsCert, N'IELTS-DIAG-01', 'published', 60, 'placement',
          N'Free IELTS baseline benchmark: Listening and Academic Reading, followed by a separately graded free diagnostic Writing extension.',
          N'IELTS diagnostic benchmark', 10);
DECLARE @diagIeltsExam INT = (SELECT ExamId FROM Exams WHERE CertId = @diagIeltsCert AND Name = N'IELTS-DIAG-01');

UPDATE Exams
SET Name = N'VSTEP-DIAG-01',
    DurationMinutes = 45,
    QuestionCount = 10,
    Description = N'Free VSTEP baseline benchmark: Listening and Reading, followed by a separately graded free diagnostic Writing extension.'
WHERE CertId = @diagVstepCert
  AND Name IN (N'VSTEP Placement Test — Diagnostic', N'VSTEP-DIAG-01');

IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @diagVstepCert AND Name = N'VSTEP-DIAG-01')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@diagVstepCert, N'VSTEP-DIAG-01', 'published', 45, 'placement',
          N'Free VSTEP baseline benchmark: Listening and Reading, followed by a separately graded free diagnostic Writing extension.',
          N'VSTEP diagnostic benchmark', 10);
DECLARE @diagVstepExam INT = (SELECT ExamId FROM Exams WHERE CertId = @diagVstepCert AND Name = N'VSTEP-DIAG-01');

-- Separate writing_practice exams use the existing free-response lifecycle and IELTS/VSTEP
-- GradingCriteria. The source marker makes these diagnostic attempts free without changing schema.
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @diagIeltsCert AND Name = N'IELTS-DIAG-01 Writing Extension')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@diagIeltsCert, N'IELTS-DIAG-01 Writing Extension', 'published', 60, 'writing_practice',
          N'Free diagnostic extension: IELTS Academic Task 1 trend report and Task 2 opinion essay, graded against the four IELTS writing criteria.',
          N'Free diagnostic benchmark', 2);
DECLARE @diagIeltsWritingExam INT =
    (SELECT ExamId FROM Exams WHERE CertId = @diagIeltsCert AND Name = N'IELTS-DIAG-01 Writing Extension');

IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @diagVstepCert AND Name = N'VSTEP-DIAG-01 Writing Extension')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@diagVstepCert, N'VSTEP-DIAG-01 Writing Extension', 'published', 45, 'writing_practice',
          N'Free diagnostic extension: formal email response and a developed opinion essay, graded against the four VSTEP writing criteria.',
          N'Free diagnostic benchmark', 2);
DECLARE @diagVstepWritingExam INT =
    (SELECT ExamId FROM Exams WHERE CertId = @diagVstepCert AND Name = N'VSTEP-DIAG-01 Writing Extension');

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @diagIeltsWritingExam AND SectionId = @diagIeltsTask1Section AND DisplayOrder = 1)
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@diagIeltsWritingExam, @diagIeltsCert, @diagIeltsTask1Section, 1,
          N'The line chart shows the percentage of households with access to three energy sources in a European country from 2010 to 2020: solar rose from 10% to 50%, wind from 20% to 35%, and coal fell from 60% to 25%.');
IF NOT EXISTS (SELECT 1 FROM Questions WHERE ExamId = @diagIeltsWritingExam AND DisplayOrder = 1)
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, MinWords, MaxWords, DisplayOrder)
  SELECT g.GroupId, @diagIeltsWritingExam, @diagIeltsTask1Section, @diagIeltsWrit,
         N'Summarise the main trends, select the most important features, and make relevant comparisons. Write at least 150 words.',
         3, 'free_response',
         N'Assessment focus: a clear overview, accurate comparisons, selective data, and a coherent progression from 2010 to 2020.',
         150, 220, 1
  FROM QuestionGroups g
  WHERE g.ExamId = @diagIeltsWritingExam AND g.SectionId = @diagIeltsTask1Section AND g.DisplayOrder = 1;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @diagIeltsWritingExam AND SectionId = @diagIeltsTask2Section AND DisplayOrder = 2)
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@diagIeltsWritingExam, @diagIeltsCert, @diagIeltsTask2Section, 2,
          N'Some people believe that universities should focus on practical job skills, while others think academic knowledge is more important. Discuss both views and give your own opinion.');
IF NOT EXISTS (SELECT 1 FROM Questions WHERE ExamId = @diagIeltsWritingExam AND DisplayOrder = 2)
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, MinWords, MaxWords, DisplayOrder)
  SELECT g.GroupId, @diagIeltsWritingExam, @diagIeltsTask2Section, @diagIeltsWrit,
         N'Discuss both views and give your own opinion. Support your position with reasons and relevant examples. Write at least 250 words.',
         4, 'free_response',
         N'Assessment focus: address both views, state a consistent position, develop supported ideas, and control paragraph cohesion.',
         250, 350, 2
  FROM QuestionGroups g
  WHERE g.ExamId = @diagIeltsWritingExam AND g.SectionId = @diagIeltsTask2Section AND g.DisplayOrder = 2;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @diagVstepWritingExam AND SectionId = @diagVstepEmailSection AND DisplayOrder = 1)
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@diagVstepWritingExam, @diagVstepCert, @diagVstepEmailSection, 1,
          N'You have received an email from the coordinator of a local community project. Reply to explain which weekend activity you can join, ask for the meeting details, and suggest one way to prepare.');
IF NOT EXISTS (SELECT 1 FROM Questions WHERE ExamId = @diagVstepWritingExam AND DisplayOrder = 1)
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, MinWords, MaxWords, DisplayOrder)
  SELECT g.GroupId, @diagVstepWritingExam, @diagVstepEmailSection, @diagVstepWrit,
         N'Write a polite email reply covering all three points. Use an appropriate greeting, clear paragraphs, and a suitable closing. Write 120–150 words.',
         3, 'free_response',
         N'Assessment focus: fulfil every communicative purpose, maintain an appropriate formal register, and organize a clear email.',
         120, 150, 1
  FROM QuestionGroups g
  WHERE g.ExamId = @diagVstepWritingExam AND g.SectionId = @diagVstepEmailSection AND g.DisplayOrder = 1;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @diagVstepWritingExam AND SectionId = @diagVstepEssaySection AND DisplayOrder = 2)
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@diagVstepWritingExam, @diagVstepCert, @diagVstepEssaySection, 2,
          N'Some people think that cities should invest more in public parks than in shopping centres. Do you agree or disagree? Give reasons and examples.');
IF NOT EXISTS (SELECT 1 FROM Questions WHERE ExamId = @diagVstepWritingExam AND DisplayOrder = 2)
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, MinWords, MaxWords, DisplayOrder)
  SELECT g.GroupId, @diagVstepWritingExam, @diagVstepEssaySection, @diagVstepWrit,
         N'Write an opinion essay with a clear position, two developed reasons, and relevant examples. Write 250–300 words.',
         4, 'free_response',
         N'Assessment focus: build a logical argument, explain examples, use cohesive paragraphs, and maintain accurate language.',
         250, 300, 2
  FROM QuestionGroups g
  WHERE g.ExamId = @diagVstepWritingExam AND g.SectionId = @diagVstepEssaySection AND g.DisplayOrder = 2;

-- Each scored benchmark has five Listening and five Reading questions. The existing two seed
-- questions remain; eight additional questions add listening slices, academic reading, and a
-- matching-style item. Every option, including each distractor, has a review rationale.
DECLARE @diagQuestions TABLE (
  SeedId INT IDENTITY(1,1) PRIMARY KEY,
  CertId INT NOT NULL, ExamId INT NOT NULL, SectionId INT NOT NULL, SkillId INT NOT NULL,
  GroupOrder INT NOT NULL, QuestionOrder INT NOT NULL,
  AudioPath NVARCHAR(400) NULL, AudioStartMs INT NULL, AudioEndMs INT NULL,
  Transcript NVARCHAR(MAX) NULL, Passage NVARCHAR(MAX) NULL,
  Stem NVARCHAR(MAX) NOT NULL, Explanation NVARCHAR(MAX) NOT NULL, CorrectLabel VARCHAR(4) NOT NULL,
  AText NVARCHAR(MAX) NOT NULL, ARationale NVARCHAR(MAX) NOT NULL,
  BText NVARCHAR(MAX) NOT NULL, BRationale NVARCHAR(MAX) NOT NULL,
  CText NVARCHAR(MAX) NOT NULL, CRationale NVARCHAR(MAX) NOT NULL,
  DText NVARCHAR(MAX) NOT NULL, DRationale NVARCHAR(MAX) NOT NULL
);

INSERT INTO @diagQuestions
  (CertId, ExamId, SectionId, SkillId, GroupOrder, QuestionOrder, AudioPath, AudioStartMs, AudioEndMs,
   Transcript, Passage, Stem, Explanation, CorrectLabel,
   AText, ARationale, BText, BRationale, CText, CRationale, DText, DRationale)
VALUES
(@diagIeltsCert,@diagIeltsExam,@diagIeltsListeningSection,@diagIeltsList,2,3,'/audio/ielts/placement-diagnostic.mp3',30000,60000,
 N'Applicant: I can attend the laboratory induction on Tuesday. Is the 10 o''clock session still available?
Coordinator: It is full, but there are two places in the 2:30 session. Please bring your student card.',
 NULL,N'Which induction session can the applicant attend?',
 N'The 10 o''clock session is full; the coordinator offers two places in the 2:30 session.', 'B',
 N'The 10 o''clock session',N'This repeats the applicant''s first choice, but the coordinator says it is full.',
 N'The 2:30 session',N'This is the alternative session with two available places.',
 N'The Tuesday evening session',N'No evening session is mentioned in the recording.',
 N'Any session without a student card',N'The card is something to bring, not a condition for choosing a session.'),
(@diagIeltsCert,@diagIeltsExam,@diagIeltsListeningSection,@diagIeltsList,3,4,'/audio/ielts/placement-diagnostic.mp3',60000,90000,
 N'Tutor: Your draft is well researched. Before Friday, reorganise the second section around the comparison rather than the dates.
Student: Should I add another source?
Tutor: Not yet. Make the contrast clearer first.',
 NULL,N'What does the tutor advise the student to do first?',
 N'The tutor explicitly prioritizes reorganising the section to make the comparison clearer.', 'C',
 N'Add another source',N'The tutor says not to add a source yet.',
 N'Correct every date',N'The advice is about the comparison, not date accuracy.',
 N'Reorganise the section around the comparison',N'This paraphrases the tutor''s instruction to make the contrast clearer.',
 N'Submit the draft on Thursday',N'The tutor gives a Friday deadline and no Thursday submission instruction.'),
(@diagIeltsCert,@diagIeltsExam,@diagIeltsListeningSection,@diagIeltsList,4,5,'/audio/ielts/placement-diagnostic.mp3',90000,120000,
 N'Guide: The north gallery is closed for lighting work this morning. The sculpture collection has moved upstairs, beside the reading room.
Visitor: Is the café affected?
Guide: No, it opens at the usual time.',
 NULL,N'Where can visitors now find the sculpture collection?',
 N'The guide says the collection has moved upstairs beside the reading room.', 'A',
 N'Upstairs beside the reading room',N'This is the new location stated by the guide.',
 N'In the closed north gallery',N'The gallery is closed and no longer holds the collection.',
 N'Inside the café',N'The café is unaffected and is not the collection location.',
 N'Outside the building',N'No outdoor relocation is mentioned.'),
(@diagIeltsCert,@diagIeltsExam,@diagIeltsListeningSection,@diagIeltsList,5,6,'/audio/ielts/placement-diagnostic.mp3',120000,150000,
 N'Researcher: We expected the survey to finish in May, but the final interviews were delayed.
Manager: Does that change the publication date?
Researcher: By only one week. The report should be ready in early July, not late June.',
 NULL,N'What changed because of the delayed interviews?',
 N'The report is now expected in early July rather than late June, a one-week shift.', 'D',
 N'The survey will be cancelled',N'The researcher says only the timeline changed.',
 N'The interviews ended one month early',N'The interviews were delayed, not completed early.',
 N'The report will be ready in May',N'May was the expected survey completion, not the report date.',
 N'The report is expected one week later',N'This captures the stated shift to early July.'),
(@diagIeltsCert,@diagIeltsExam,@diagIeltsReadingSection,@diagIeltsRead,3,7,NULL,NULL,NULL,NULL,
 N'Urban tree cover can reduce surface temperatures during heatwaves. A city study found the greatest cooling in streets where mature trees shaded continuous sections of pavement; isolated trees produced smaller local effects.',
 N'Which condition produced the greatest cooling in the study?',
 N'The passage contrasts continuous shade from mature trees with the smaller effects of isolated trees.', 'A',
 N'Mature trees shading continuous pavement',N'This matches the reported condition with the greatest cooling.',
 N'Newly planted trees in parks',N'Neither newly planted trees nor parks are identified as the strongest condition.',
 N'Isolated trees beside buildings',N'The passage says isolated trees produced smaller effects.',
 N'Any street with a wide road',N'Road width is not identified as a factor.'),
(@diagIeltsCert,@diagIeltsExam,@diagIeltsReadingSection,@diagIeltsRead,4,8,NULL,NULL,NULL,NULL,
 N'In a survey of 1,200 commuters, 48% travelled by bus in 2018, compared with 30% by rail and 22% by bicycle. By 2024, rail had risen to 41%, cycling to 25%, while bus use had fallen to 34%.',
 N'Which option correctly matches the 2024 transport mode with its share? (Matching)',
 N'Rail is matched with 41%; the other options pair the wrong mode and percentage.', 'C',
 N'Bus — 41%',N'41% is the rail figure; bus use fell to 34%.',
 N'Bicycle — 34%',N'Cycling is reported at 25%, not 34%.',
 N'Rail — 41%',N'This correctly matches the mode to the 2024 percentage.',
 N'Bus — 25%',N'25% is the bicycle share, while bus use was 34%.'),
(@diagIeltsCert,@diagIeltsExam,@diagIeltsReadingSection,@diagIeltsRead,5,9,NULL,NULL,NULL,NULL,
 N'The archive digitised its fragile maps first, not because they were the most requested items, but because repeated handling was causing irreversible damage. Popularity rankings were therefore set aside in the initial phase.',
 N'Why were the maps digitised first?',
 N'The passage says preservation needs, rather than popularity, determined the order.', 'D',
 N'They had the highest online readership',N'The passage explicitly distinguishes preservation from popularity.',
 N'They were inexpensive to replace',N'The maps are described as fragile and at risk of irreversible damage.',
 N'They contained no historical information',N'The passage does not say this.',
 N'To prevent damage from repeated handling',N'This is the stated preservation reason.'),
(@diagIeltsCert,@diagIeltsExam,@diagIeltsReadingSection,@diagIeltsRead,6,10,NULL,NULL,NULL,NULL,
 N'A field experiment compared two restoration sites. Site A received more rain but recovered slowly because compacted soil restricted drainage. Site B received less rain; after its soil was loosened, native plants spread quickly.',
 N'What can be inferred about the restoration at Site B?',
 N'The comparison suggests soil condition supported recovery even with less rainfall.', 'B',
 N'More rainfall alone guarantees rapid recovery',N'Site A had more rainfall but recovered slowly.',
 N'Improved soil drainage may support plant recovery',N'Site B recovered quickly after the compacted soil problem was addressed.',
 N'Native plants cannot grow in dry conditions',N'Native plants spread at the site receiving less rain.',
 N'Site A had no compacted soil',N'The passage identifies compacted soil at Site A.'),
(@diagVstepCert,@diagVstepExam,@diagVstepListeningSection,@diagVstepList,2,3,'/audio/vstep/placement-diagnostic.mp3',30000,60000,
 N'Lecturer: The field trip leaves from the main gate at 7:15 on Saturday. Please arrive fifteen minutes early because the bus cannot wait.
Student: Do we need to bring lunch?
Lecturer: Yes, and wear shoes suitable for walking.',
 NULL,N'When should students arrive at the main gate?',
 N'The trip leaves at 7:15 and students must arrive fifteen minutes earlier, at 7:00.', 'A',
 N'7:00 on Saturday',N'Arriving fifteen minutes before the 7:15 departure means 7:00.',
 N'7:15 on Sunday',N'The trip is on Saturday, not Sunday.',
 N'7:30 on Saturday',N'This is after departure and contradicts the instruction.',
 N'8:15 on Saturday',N'The departure is at 7:15, so this is an hour too late.'),
(@diagVstepCert,@diagVstepExam,@diagVstepListeningSection,@diagVstepList,3,4,'/audio/vstep/placement-diagnostic.mp3',60000,90000,
 N'Office staff: The language club meeting has moved from Room 204 to Room 118 because the projector in 204 is being repaired.
Student: Is the time still 3 p.m.?
Office staff: Yes, the schedule is unchanged.',
 NULL,N'Why has the language club changed rooms?',
 N'The projector in Room 204 is being repaired; the meeting time remains unchanged.', 'D',
 N'The club meeting has been cancelled',N'The staff confirms that only the room changed.',
 N'Room 118 has a better projector',N'The reason given is repair work in Room 204.',
 N'The meeting time has changed',N'The time remains 3 p.m.',
 N'The projector in Room 204 needs repair',N'This is the reason stated by the office staff.'),
(@diagVstepCert,@diagVstepExam,@diagVstepListeningSection,@diagVstepList,4,5,'/audio/vstep/placement-diagnostic.mp3',90000,120000,
 N'Announcer: The community clean-up will take place on 18 June, not 16 June as printed on the first notice. Volunteers should register at the library desk by 5 p.m. on Friday.',
 NULL,N'What should volunteers do by Friday?',
 N'The announcer asks volunteers to register at the library desk by 5 p.m.', 'B',
 N'Clean the library on 16 June',N'The event date was corrected to 18 June; the task is registration.',
 N'Register at the library desk',N'This is the action and location stated in the announcement.',
 N'Bring the first notice to the event',N'No instruction to bring a notice is given.',
 N'Arrive at the clean-up by 5 p.m.',N'5 p.m. Friday is the registration deadline, not the event time.'),
(@diagVstepCert,@diagVstepExam,@diagVstepListeningSection,@diagVstepList,5,6,'/audio/vstep/placement-diagnostic.mp3',120000,150000,
 N'Student: I cannot attend the computer workshop on Wednesday.
Advisor: The Friday group still has spaces. You can transfer online before noon tomorrow; there is no extra fee.
Student: I will do that tonight.',
 NULL,N'How can the student change workshop groups?',
 N'The advisor says to transfer online before noon tomorrow; the student plans to do it tonight.', 'C',
 N'Pay for a new registration at the office',N'The advisor says there is no extra fee.',
 N'Ask the student group to change its date',N'The advisor offers a different group, not a date change.',
 N'Transfer online before noon tomorrow',N'This is the required process and deadline.',
 N'Wait until the Friday workshop begins',N'The transfer must be made before noon tomorrow.'),
(@diagVstepCert,@diagVstepExam,@diagVstepReadingSection,@diagVstepRead,3,7,NULL,NULL,NULL,NULL,
 N'A town library introduced a book-delivery service for older residents who cannot travel easily. Volunteers collect requested titles twice a week and leave them at each resident''s home. The service is free, but readers must register by phone.',
 N'Who is the delivery service mainly designed to help?',
 N'The first sentence identifies older residents with limited ability to travel.', 'A',
 N'Older residents who cannot travel easily',N'This restates the group named in the passage.',
 N'Volunteers who want free books',N'Volunteers deliver the books; they are not the intended readers.',
 N'Visitors who have not joined the library',N'The passage says users need to register, but does not identify visitors as the target group.',
 N'People who live outside the town',N'The service is described for residents of the town.'),
(@diagVstepCert,@diagVstepExam,@diagVstepReadingSection,@diagVstepRead,4,8,NULL,NULL,NULL,NULL,
 N'The college offers evening classes in basic accounting, workplace communication, and computer skills. Learners may enrol in one course at a time. Applications close on 30 September, and classes begin two weeks later.',
 N'Which option correctly matches a course with its start date? (Matching)',
 N'Classes begin two weeks after applications close on 30 September, so the start is in October.', 'C',
 N'Computer skills — 30 September',N'30 September is the application deadline, not the class start date.',
 N'Accounting — one week after applications close',N'The passage states a two-week interval.',
 N'Workplace communication — two weeks after 30 September',N'This correctly matches the course schedule stated in the notice.',
 N'All courses — the application deadline',N'The deadline is for applying; classes begin later.'),
(@diagVstepCert,@diagVstepExam,@diagVstepReadingSection,@diagVstepRead,5,9,NULL,NULL,NULL,NULL,
 N'Although the sports centre extended its opening hours, attendance did not rise during the first month. Managers later learned that the last bus left before the new closing time. They are discussing a revised shuttle schedule with the transport office.',
 N'Why did attendance probably fail to increase?',
 N'The last bus left before the centre closed, making the extended hours difficult to use.', 'D',
 N'The sports centre reduced its opening hours',N'The centre extended, rather than reduced, its hours.',
 N'The transport office closed permanently',N'The passage says a schedule is being discussed, not that the office closed.',
 N'There were no activities at the centre',N'No information about activities is provided.',
 N'Public transport was unavailable at closing time',N'The last bus left before the new closing time.'),
(@diagVstepCert,@diagVstepExam,@diagVstepReadingSection,@diagVstepRead,6,10,NULL,NULL,NULL,NULL,
 N'An adult-learning programme originally met every Saturday. After many learners reported shift-work conflicts, the organiser added a Wednesday evening class. The Saturday class remains available for those who prefer it.',
 N'What was the main reason for adding a Wednesday class?',
 N'The additional class responds to learners'' reported work schedules while keeping the original option.', 'B',
 N'The Saturday class was too expensive',N'No course fee is mentioned.',
 N'To accommodate learners with shift-work conflicts',N'This is the stated reason for adding an evening alternative.',
 N'The organiser wanted to remove the Saturday class',N'The passage says Saturday remains available.',
 N'The programme changed to online teaching',N'Both options are described as classes; online delivery is not mentioned.');

DECLARE @diagSeedId INT = 1;
DECLARE @diagSeedCount INT = (SELECT COUNT(*) FROM @diagQuestions);
DECLARE @diagQCert INT, @diagQExam INT, @diagQSection INT, @diagQSkill INT;
DECLARE @diagQGroupOrder INT, @diagQOrder INT, @diagQAudio NVARCHAR(400);
DECLARE @diagQStart INT, @diagQEnd INT, @diagQTranscript NVARCHAR(MAX), @diagQPassage NVARCHAR(MAX);
DECLARE @diagQStem NVARCHAR(MAX), @diagQExplanation NVARCHAR(MAX), @diagQCorrect VARCHAR(4);
DECLARE @diagQA NVARCHAR(MAX), @diagQAR NVARCHAR(MAX), @diagQB NVARCHAR(MAX), @diagQBR NVARCHAR(MAX);
DECLARE @diagQC NVARCHAR(MAX), @diagQCR NVARCHAR(MAX), @diagQD NVARCHAR(MAX), @diagQDR NVARCHAR(MAX);
DECLARE @diagQGroupId INT, @diagQId INT;

WHILE @diagSeedId <= @diagSeedCount
BEGIN
  SELECT @diagQCert=CertId, @diagQExam=ExamId, @diagQSection=SectionId, @diagQSkill=SkillId,
         @diagQGroupOrder=GroupOrder, @diagQOrder=QuestionOrder, @diagQAudio=AudioPath,
         @diagQStart=AudioStartMs, @diagQEnd=AudioEndMs, @diagQTranscript=Transcript, @diagQPassage=Passage,
         @diagQStem=Stem, @diagQExplanation=Explanation, @diagQCorrect=CorrectLabel,
         @diagQA=AText, @diagQAR=ARationale, @diagQB=BText, @diagQBR=BRationale,
         @diagQC=CText, @diagQCR=CRationale, @diagQD=DText, @diagQDR=DRationale
  FROM @diagQuestions WHERE SeedId = @diagSeedId;

  IF NOT EXISTS (SELECT 1 FROM Questions WHERE ExamId = @diagQExam AND DisplayOrder = @diagQOrder)
    INSERT INTO QuestionGroups
      (ExamId, CertId, SectionId, DisplayOrder, AudioPath, AudioStartMs, AudioEndMs, Transcript, Passage)
    VALUES
      (@diagQExam, @diagQCert, @diagQSection, @diagQGroupOrder, @diagQAudio, @diagQStart, @diagQEnd, @diagQTranscript, @diagQPassage);

  SELECT @diagQGroupId = GroupId
  FROM QuestionGroups
  WHERE ExamId = @diagQExam AND SectionId = @diagQSection AND DisplayOrder = @diagQGroupOrder;

  IF NOT EXISTS (SELECT 1 FROM Questions WHERE ExamId = @diagQExam AND DisplayOrder = @diagQOrder)
    INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Explanation, DisplayOrder)
    VALUES (@diagQGroupId, @diagQExam, @diagQSection, @diagQSkill, @diagQStem, 3, 'mcq', @diagQExplanation, @diagQOrder);

  SELECT @diagQId = QuestionId FROM Questions WHERE ExamId = @diagQExam AND DisplayOrder = @diagQOrder;
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect, Rationale)
  SELECT @diagQId, 'mcq', o.Label, o.OptionText, IIF(o.Label = @diagQCorrect, 1, 0), o.Rationale
  FROM (VALUES ('A',@diagQA,@diagQAR),('B',@diagQB,@diagQBR),('C',@diagQC,@diagQCR),('D',@diagQD,@diagQDR))
       o(Label, OptionText, Rationale)
  WHERE NOT EXISTS (SELECT 1 FROM QuestionOptions existing WHERE existing.QuestionId = @diagQId AND existing.Label = o.Label);

  UPDATE existing
  SET Rationale = o.Rationale,
      IsCorrect = IIF(o.Label = @diagQCorrect, 1, 0)
  FROM QuestionOptions existing
  JOIN (VALUES ('A',@diagQAR),('B',@diagQBR),('C',@diagQCR),('D',@diagQDR)) o(Label, Rationale)
    ON o.Label = existing.Label
  WHERE existing.QuestionId = @diagQId;

  SET @diagSeedId += 1;
END;

-- Complete rationales for the two questions that were part of the original placeholders.
UPDATE o SET Rationale = CASE o.Label
  WHEN 'A' THEN N'12:00 PM is not the Friday closing time; the recording specifies 1:00 PM.'
  WHEN 'B' THEN N'Correct: the housing office closes at 1:00 PM on Fridays.'
  WHEN 'C' THEN N'4:00 PM applies Monday through Thursday, not Friday.'
  WHEN 'D' THEN N'5:00 PM is never given as a closing time.'
END
FROM QuestionOptions o JOIN Questions q ON q.QuestionId=o.QuestionId
WHERE q.ExamId=@diagIeltsExam AND q.DisplayOrder=1;
UPDATE o SET Rationale = CASE o.Label
  WHEN 'A' THEN N'Wind is not the source identified as the majority of new capacity.'
  WHEN 'B' THEN N'Correct: solar photovoltaics accounted for three-quarters of new installations.'
  WHEN 'C' THEN N'Hydroelectricity is not named in the passage.'
  WHEN 'D' THEN N'Geothermal energy is not named in the passage.'
END
FROM QuestionOptions o JOIN Questions q ON q.QuestionId=o.QuestionId
WHERE q.ExamId=@diagIeltsExam AND q.DisplayOrder=2;
UPDATE o SET Rationale = CASE o.Label
  WHEN 'A' THEN N'The lecturer asks students to submit through the faculty portal, not on paper.'
  WHEN 'B' THEN N'Correct: students should submit their proposals through the faculty portal.'
  WHEN 'C' THEN N'Emailing the teaching assistant is not the stated submission route.'
  WHEN 'D' THEN N'The deadline is Friday at 5 PM, not during Monday class.'
END
FROM QuestionOptions o JOIN Questions q ON q.QuestionId=o.QuestionId
WHERE q.ExamId=@diagVstepExam AND q.DisplayOrder=1;
UPDATE o SET Rationale = CASE o.Label
  WHEN 'A' THEN N'The passage describes support for radiology, not replacing doctors.'
  WHEN 'B' THEN N'Correct: AI provides diagnostic assistance for radiology scans.'
  WHEN 'C' THEN N'The passage discusses interpreting scans, not manufacturing equipment.'
  WHEN 'D' THEN N'Billing is not mentioned as an AI function.'
END
FROM QuestionOptions o JOIN Questions q ON q.QuestionId=o.QuestionId
WHERE q.ExamId=@diagVstepExam AND q.DisplayOrder=2;

UPDATE ExamSectionAudio
SET AudioPath = '/audio/ielts/placement-diagnostic.mp3', DurationSec = 600, SizeBytes = 9600000
WHERE ExamId=@diagIeltsExam AND SectionId=@diagIeltsListeningSection;
IF NOT EXISTS (SELECT 1 FROM ExamSectionAudio WHERE ExamId=@diagIeltsExam AND SectionId=@diagIeltsListeningSection)
  INSERT INTO ExamSectionAudio (ExamId,SectionId,CertId,AudioPath,DurationSec,SizeBytes)
  VALUES (@diagIeltsExam,@diagIeltsListeningSection,@diagIeltsCert,'/audio/ielts/placement-diagnostic.mp3',600,9600000);
UPDATE ExamSectionAudio
SET AudioPath = '/audio/vstep/placement-diagnostic.mp3', DurationSec = 600, SizeBytes = 9600000
WHERE ExamId=@diagVstepExam AND SectionId=@diagVstepListeningSection;
IF NOT EXISTS (SELECT 1 FROM ExamSectionAudio WHERE ExamId=@diagVstepExam AND SectionId=@diagVstepListeningSection)
  INSERT INTO ExamSectionAudio (ExamId,SectionId,CertId,AudioPath,DurationSec,SizeBytes)
  VALUES (@diagVstepExam,@diagVstepListeningSection,@diagVstepCert,'/audio/vstep/placement-diagnostic.mp3',600,9600000);

-- Make the listening and reading raw-score conversions cover every attainable score.
UPDATE ScoreScales SET ScaledScore = CASE RawScore WHEN 0 THEN 40 WHEN 1 THEN 50 END
WHERE CertId=@diagIeltsCert AND ExamId=@diagIeltsExam AND RawScore IN (0,1);
UPDATE ScoreScales SET ScaledScore = CASE RawScore WHEN 0 THEN 40 WHEN 1 THEN 50 END
WHERE CertId=@diagVstepCert AND ExamId=@diagVstepExam AND RawScore IN (0,1);
INSERT INTO ScoreScales (CertId,SkillId,ExamId,RawScore,ScaledScore)
SELECT @diagIeltsCert,s.SkillId,@diagIeltsExam,r.RawScore,40+(r.RawScore*10)
FROM (VALUES (@diagIeltsList),(@diagIeltsRead)) s(SkillId)
CROSS JOIN (VALUES (2),(3),(4),(5)) r(RawScore)
WHERE NOT EXISTS (SELECT 1 FROM ScoreScales x WHERE x.CertId=@diagIeltsCert AND x.SkillId=s.SkillId AND x.ExamId=@diagIeltsExam AND x.RawScore=r.RawScore);
INSERT INTO ScoreScales (CertId,SkillId,ExamId,RawScore,ScaledScore)
SELECT @diagVstepCert,s.SkillId,@diagVstepExam,r.RawScore,40+(r.RawScore*10)
FROM (VALUES (@diagVstepList),(@diagVstepRead)) s(SkillId)
CROSS JOIN (VALUES (2),(3),(4),(5)) r(RawScore)
WHERE NOT EXISTS (SELECT 1 FROM ScoreScales x WHERE x.CertId=@diagVstepCert AND x.SkillId=s.SkillId AND x.ExamId=@diagVstepExam AND x.RawScore=r.RawScore);

DECLARE @diagLessons TABLE (
  SeedId INT IDENTITY(1,1) PRIMARY KEY,
  CertId INT NOT NULL, SkillId INT NOT NULL, Title NVARCHAR(160) NOT NULL,
  Summary NVARCHAR(500) NOT NULL, ModuleOrder INT NOT NULL, PracticeSectionId INT NOT NULL,
  RuleContent NVARCHAR(MAX) NOT NULL, PitfallContent NVARCHAR(MAX) NOT NULL, ExampleContent NVARCHAR(MAX) NOT NULL,
  QuizPrompt NVARCHAR(MAX) NOT NULL, OptionA NVARCHAR(250) NOT NULL, OptionB NVARCHAR(250) NOT NULL,
  OptionC NVARCHAR(250) NOT NULL, OptionD NVARCHAR(250) NOT NULL, CorrectOption CHAR(1) NOT NULL,
  QuizExplanation NVARCHAR(MAX) NOT NULL
);
INSERT INTO @diagLessons
  (CertId,SkillId,Title,Summary,ModuleOrder,PracticeSectionId,RuleContent,PitfallContent,ExampleContent,
   QuizPrompt,OptionA,OptionB,OptionC,OptionD,CorrectOption,QuizExplanation)
VALUES
(@diagIeltsCert,@diagIeltsList,N'IELTS Listening: Follow Signposts and Corrections',N'Track the speaker''s purpose, changes, and final answer rather than matching isolated words.',1,@diagIeltsListeningSection,
 N'Listen for discourse markers such as “actually”, “instead”, and “not ... but ...”. The corrected or qualified detail often carries the answer.',
 N'A distractor may repeat a word from the question while giving an outdated time, location, or choice. Confirm the relationship, not just the keyword.',
 N'“We planned to meet at ten; actually, the lab is free at half past two” makes 2:30 the available time.',
 N'A speaker says, “The first room is full; instead, meet us in Room 12.” Which room is available?',
 N'The first room',N'Room 12',N'Both rooms',N'No room is available','B',
 N'“Instead” introduces the replacement location; the first room is explicitly full.'),
(@diagIeltsCert,@diagIeltsRead,N'IELTS Reading: Paraphrase Claims and Match Evidence',N'Connect question wording to synonyms, references, and evidence across academic passages.',2,@diagIeltsReadingSection,
 N'Underline the idea in the question, predict a synonym, then locate evidence before choosing. For matching, verify both sides of the pair.',
 N'A familiar noun can appear in a sentence that contradicts the question. A shared keyword alone does not prove a match.',
 N'“The intervention reduced delays” can support “the measure improved punctuality” when the surrounding evidence confirms the same result.',
 N'The passage says “use fell from 48% to 34%”. Which paraphrase is supported?',
 N'Use increased to 48%',N'Use declined to 34%',N'Use remained unchanged',N'Use stopped completely','B',
 N'A fall to 34% is a decline; the other options change the direction or magnitude.'),
(@diagIeltsCert,@diagIeltsWrit,N'IELTS Task 1: Select and Describe Trends',N'Write a concise overview, group key features, and compare data without listing every figure.',3,@diagIeltsTask1Section,
 N'After the introduction, state the two or three dominant trends in an overview. Support them with selected, accurate figures and comparisons.',
 N'Do not describe every year separately or add causes and opinions that the chart does not provide.',
 N'“Solar rose from 10% to 50%, while coal fell from 60% to 25%” highlights the principal contrast with start and end values.',
 N'What should the Task 1 overview primarily do?',
 N'Explain why each change happened',N'List every data point',N'Summarise the main patterns and comparisons',N'Give a personal opinion','C',
 N'The overview reports prominent patterns; causes and opinions are not inferred from chart data.'),
(@diagIeltsCert,@diagIeltsWrit,N'IELTS Writing: Coherence, Cohesion, and Position',N'Build a clear Task 2 position with purposeful paragraphs and controlled linking.',4,@diagIeltsTask2Section,
 N'Answer every part of the prompt, state a consistent position, and give each body paragraph one main claim supported by explanation or an example.',
 N'Linking words cannot repair paragraphs that contain unrelated ideas. Avoid memorised phrases that do not express a real relationship.',
 N'A paragraph can progress from claim → reason → specific example → link back to the question.',
 N'Which paragraph plan best supports a clear Task 2 argument?',
 N'One paragraph with several unrelated claims',N'Claim, explanation, and relevant example',N'Only a list of linking words',N'A conclusion with new arguments','B',
 N'A developed paragraph connects one claim to its reasoning and evidence.'),
(@diagVstepCert,@diagVstepList,N'VSTEP Listening: Catch Purpose, Time, and Changes',N'Identify the speaker''s main purpose and distinguish confirmed details from corrected plans.',1,@diagVstepListeningSection,
 N'Note who is speaking, why, and what action is required. Listen for dates, deadlines, and changes signalled by “because”, “but”, or “not”.',
 N'Do not choose the first date or room you hear if the speaker later corrects it.',
 N'“It was planned for the 16th, but it will take place on the 18th” confirms 18 June.',
 N'A notice corrects an event from 16 June to 18 June. Which date should volunteers use?',
 N'16 June',N'18 June',N'Both dates',N'The date is cancelled','B',
 N'The later correction replaces the earlier printed date.'),
(@diagVstepCert,@diagVstepRead,N'VSTEP Reading: Locate Paraphrases and References',N'Scan for names, dates, and synonyms, then check pronouns and nearby evidence.',2,@diagVstepReadingSection,
 N'Scan for a distinctive detail, read its surrounding sentence, and connect paraphrases such as “cannot travel easily” with “limited mobility”.',
 N'A nearby pronoun can refer to a different person or object than the closest noun. Check meaning across the sentence.',
 N'“The last bus left before closing” supports the inference that late visitors lacked transport home.',
 N'“Applicants must register by phone” means applicants should ...',
 N'Call to complete registration',N'Visit without registering',N'Pay at the library',N'Wait until classes begin','A',
 N'Registering by phone is equivalent to calling to complete registration.'),
(@diagVstepCert,@diagVstepWrit,N'VSTEP Writing: Organise a Formal Email',N'Use an appropriate greeting, answer every point, request or suggest details, and close politely.',3,@diagVstepEmailSection,
 N'Plan the email in four moves: greeting and purpose, response to each required point, any request or suggestion, and an appropriate closing.',
 N'A friendly tone does not mean omitting the requested information or using chat abbreviations in a formal email.',
 N'Dear Project Coordinator, I can join the Saturday clean-up. Could you confirm the meeting point? I will bring gloves. Kind regards, ...',
 N'What should a complete reply do first after a suitable greeting?',
 N'Answer the prompt''s required points clearly',N'Add an unrelated story',N'Use informal abbreviations',N'End without a closing','A',
 N'Task fulfilment requires responding to the requested information before adding unrelated content.'),
(@diagVstepCert,@diagVstepWrit,N'VSTEP Writing: Build and Support an Argument',N'State a position, develop reasons, and connect each example to the argument.',4,@diagVstepEssaySection,
 N'Use an introduction with a clear position, body paragraphs with one reason each, and a conclusion that reinforces the argument.',
 N'A list of examples without explaining their relevance does not develop an argument.',
 N'Claim: parks improve public health. Reason: they provide safe exercise space. Example: residents can walk daily without travelling far.',
 N'Which sequence best develops one body-paragraph idea?',
 N'Example, unrelated claim, conclusion',N'Claim, explanation, relevant example',N'Three linking words only',N'New position, then copied prompt','B',
 N'A claim supported by an explanation and relevant example forms a developed argument.');

DECLARE @diagLessonIndex INT=1, @diagLessonCount INT=(SELECT COUNT(*) FROM @diagLessons);
DECLARE @diagLessonCert INT, @diagLessonSkill INT, @diagLessonTitle NVARCHAR(160), @diagLessonSummary NVARCHAR(500);
DECLARE @diagModuleOrder INT, @diagPracticeSection INT, @diagRule NVARCHAR(MAX), @diagPitfall NVARCHAR(MAX), @diagExample NVARCHAR(MAX);
DECLARE @diagQuiz NVARCHAR(MAX), @diagOptA NVARCHAR(250), @diagOptB NVARCHAR(250), @diagOptC NVARCHAR(250), @diagOptD NVARCHAR(250);
DECLARE @diagCorrect CHAR(1), @diagQuizExplanation NVARCHAR(MAX), @diagLessonId INT, @diagTemplateId INT, @diagModuleId INT;
DECLARE @diagWritingExam INT;

WHILE @diagLessonIndex <= @diagLessonCount
BEGIN
  SELECT @diagLessonCert=CertId,@diagLessonSkill=SkillId,@diagLessonTitle=Title,@diagLessonSummary=Summary,
         @diagModuleOrder=ModuleOrder,@diagPracticeSection=PracticeSectionId,@diagRule=RuleContent,
         @diagPitfall=PitfallContent,@diagExample=ExampleContent,@diagQuiz=QuizPrompt,
         @diagOptA=OptionA,@diagOptB=OptionB,@diagOptC=OptionC,@diagOptD=OptionD,
         @diagCorrect=CorrectOption,@diagQuizExplanation=QuizExplanation
  FROM @diagLessons WHERE SeedId=@diagLessonIndex;

  IF NOT EXISTS (SELECT 1 FROM Lessons WHERE CertId=@diagLessonCert AND Title=@diagLessonTitle)
    INSERT INTO Lessons (CertId,SkillId,Title,Summary,EstimatedMinutes,DisplayOrder)
    VALUES (@diagLessonCert,@diagLessonSkill,@diagLessonTitle,@diagLessonSummary,12,@diagModuleOrder);
  SELECT @diagLessonId=LessonId FROM Lessons WHERE CertId=@diagLessonCert AND Title=@diagLessonTitle;

  INSERT INTO LessonCards
    (LessonId,CertId,CardType,Title,ContentMarkdown,DisplayOrder,QuestionPrompt,OptionA,OptionB,OptionC,OptionD,CorrectOption,Explanation)
  SELECT @diagLessonId,@diagLessonCert,c.CardType,c.Title,c.ContentMarkdown,c.DisplayOrder,
         c.QuestionPrompt,c.OptionA,c.OptionB,c.OptionC,c.OptionD,c.CorrectOption,c.Explanation
  FROM (VALUES
    ('rule',N'Rule',@diagRule,0,CAST(NULL AS NVARCHAR(MAX)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS CHAR(1)),CAST(NULL AS NVARCHAR(MAX))),
    ('pitfall',N'Pitfall',@diagPitfall,1,CAST(NULL AS NVARCHAR(MAX)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS CHAR(1)),CAST(NULL AS NVARCHAR(MAX))),
    ('example',N'Worked example',@diagExample,2,CAST(NULL AS NVARCHAR(MAX)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS NVARCHAR(250)),CAST(NULL AS CHAR(1)),CAST(NULL AS NVARCHAR(MAX))),
    ('micro_check',N'Micro-check',@diagQuiz,3,@diagQuiz,@diagOptA,@diagOptB,@diagOptC,@diagOptD,@diagCorrect,@diagQuizExplanation)
  ) c(CardType,Title,ContentMarkdown,DisplayOrder,QuestionPrompt,OptionA,OptionB,OptionC,OptionD,CorrectOption,Explanation)
  WHERE NOT EXISTS (SELECT 1 FROM LessonCards old WHERE old.LessonId=@diagLessonId AND old.DisplayOrder=c.DisplayOrder);

  IF @diagLessonCert=@diagIeltsCert
  BEGIN
    IF NOT EXISTS (SELECT 1 FROM PathTemplates WHERE CertId=@diagLessonCert AND Name=N'IELTS Diagnostic Remedial Sprint')
      INSERT INTO PathTemplates (CertId,FromBandCode,ToBandCode,Name)
      VALUES (@diagLessonCert,'B1','B2',N'IELTS Diagnostic Remedial Sprint');
    SELECT @diagTemplateId=TemplateId FROM PathTemplates WHERE CertId=@diagLessonCert AND Name=N'IELTS Diagnostic Remedial Sprint';
    SET @diagWritingExam=@diagIeltsWritingExam;
  END
  ELSE
  BEGIN
    IF NOT EXISTS (SELECT 1 FROM PathTemplates WHERE CertId=@diagLessonCert AND Name=N'VSTEP Diagnostic Remedial Sprint')
      INSERT INTO PathTemplates (CertId,FromBandCode,ToBandCode,Name)
      VALUES (@diagLessonCert,'B1','B2',N'VSTEP Diagnostic Remedial Sprint');
    SELECT @diagTemplateId=TemplateId FROM PathTemplates WHERE CertId=@diagLessonCert AND Name=N'VSTEP Diagnostic Remedial Sprint';
    SET @diagWritingExam=@diagVstepWritingExam;
  END;

  IF NOT EXISTS (SELECT 1 FROM PathModules WHERE CertId=@diagLessonCert AND TemplateId=@diagTemplateId AND DisplayOrder=@diagModuleOrder)
    INSERT INTO PathModules (TemplateId,SkillId,CertId,DisplayOrder,Title,Description,EstimatedMinutes)
    VALUES (@diagTemplateId,@diagLessonSkill,@diagLessonCert,@diagModuleOrder,@diagLessonTitle,@diagLessonSummary,25);
  SELECT @diagModuleId=ModuleId FROM PathModules WHERE CertId=@diagLessonCert AND TemplateId=@diagTemplateId AND DisplayOrder=@diagModuleOrder;

  IF NOT EXISTS (SELECT 1 FROM PathModuleItems WHERE ModuleId=@diagModuleId AND ItemType='lesson' AND DisplayOrder=1)
    INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefLessonId,DisplayOrder)
    VALUES (@diagModuleId,@diagLessonCert,@diagTemplateId,'lesson',@diagLessonId,1);
  IF NOT EXISTS (SELECT 1 FROM PathModuleItems WHERE ModuleId=@diagModuleId AND ItemType='practice_section' AND DisplayOrder=2)
    INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefSectionId,QuestionCount,DisplayOrder)
    VALUES (@diagModuleId,@diagLessonCert,@diagTemplateId,'practice_section',@diagPracticeSection,5,2);
  IF @diagLessonSkill=@diagIeltsWrit OR @diagLessonSkill=@diagVstepWrit
    IF NOT EXISTS (SELECT 1 FROM PathModuleItems WHERE ModuleId=@diagModuleId AND ItemType='ai_task' AND DisplayOrder=3)
      INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefExamId,IsPremiumOnly,DisplayOrder)
      VALUES (@diagModuleId,@diagLessonCert,@diagTemplateId,'ai_task',@diagWritingExam,0,3);

  SET @diagLessonIndex+=1;
END;

PRINT '18. IELTS/VSTEP diagnostic benchmarks, free writing extensions, and remedial paths seeded';
GO
