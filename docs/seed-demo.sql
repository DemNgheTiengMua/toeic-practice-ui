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
  (@toeic, 'below_A1', N'Below A1', 10, 119, 0),
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
-- TermsAcceptedAt is set because registration requires it. EmailVerifiedAt is left NULL so
-- the demo learner shows the dashboard's verification reminder; "Resend email" there drops a
-- fresh activation link in src/WebAPI/mail-drop/ (issue #5).
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

-- ============ Demo MCQ content across all certificates (issue #8) ============
-- Practice drills draw MCQ questions from listening and reading sections.
-- Seed categories, exams, and MCQ questions with options for TOEIC, IELTS, and VSTEP.

-- 1. Published mock exams for IELTS and VSTEP
DECLARE @ieltsCert INT = (SELECT CertId FROM Certificates WHERE Code = 'IELTS');
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @ieltsCert AND Name = N'Cambridge IELTS 18 — Academic Test 1')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@ieltsCert, N'Cambridge IELTS 18 — Academic Test 1', 'published', 170, 'mock',
          N'Academic Listening, Reading, Writing and Speaking form.', N'Cambridge 18', 40);

DECLARE @vstepCert INT = (SELECT CertId FROM Certificates WHERE Code = 'VSTEP');
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @vstepCert AND Name = N'VSTEP Sample Test 1')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@vstepCert, N'VSTEP Sample Test 1', 'published', 180, 'mock',
          N'Four-skill national examination form.', N'VSTEP 2024', 75);
GO

-- 2. QuestionCategories
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

-- 3. TOEIC Listening Sections (P1, P2, P3, P4)
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @toeicExam INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS 2024 — Test 1');
DECLARE @toeicList INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'LIST');
DECLARE @toeicRead INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'READ');

-- Part 1: Photographs
DECLARE @secP1 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P1');
IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicExam AND SectionId = @secP1)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, ImagePath, ImageAlt)
  VALUES (@toeicExam, @toeicCert, @secP1, 1, '/img/demo-p1-meeting.jpg', N'Office workers gathered around a whiteboard');
  DECLARE @grpP1 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpP1, @toeicExam, @secP1, @toeicList,
          N'Look at the photograph and choose the statement that best describes what you see in the image.',
          2, 'mcq', N'Focus on what the person in the centre is holding and doing.',
          N'Option C correctly describes the action in the photo: the woman is writing notes on the whiteboard.', 1);
  DECLARE @qP1 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP1, 'mcq', 'A', N'They are leaving the meeting room.', 0),
    (@qP1, 'mcq', 'B', N'A laptop is being repaired on the desk.', 0),
    (@qP1, 'mcq', 'C', N'A woman is writing on a whiteboard.', 1),
    (@qP1, 'mcq', 'D', N'Some chairs are stacked against the wall.', 0);
END;

-- Part 2: Question-Response (3 options: A, B, C)
DECLARE @secP2 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P2');
IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicExam AND SectionId = @secP2)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
  VALUES (@toeicExam, @toeicCert, @secP2, 2);
  DECLARE @grpP2 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpP2, @toeicExam, @secP2, @toeicList,
          N'Where did you leave the project folders?',
          2, 'mcq', N'Listen for a location word that answers "Where".',
          N'Option B directly answers where the folders are placed.', 1);
  DECLARE @qP2 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP2, 'mcq', 'A', N'Yes, I folded them neatly.', 0),
    (@qP2, 'mcq', 'B', N'On the conference room table.', 1),
    (@qP2, 'mcq', 'C', N'No later than five o''clock.', 0);
END;

-- Part 3: Short Conversations
DECLARE @secP3 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P3');
IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicExam AND SectionId = @secP3)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Transcript)
  VALUES (@toeicExam, @toeicCert, @secP3, 3,
          N'Woman: Good morning, Mr. Evans. Did the catering team confirm the order for Friday''s banquet?\nMan: Yes, they did, but they said they need our final head count by tomorrow noon.\nWoman: I will email the department heads right away to get their RSVPs.');
  DECLARE @grpP3 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpP3, @toeicExam, @secP3, @toeicList,
          N'What deadline does the catering team have for the banquet?',
          3, 'mcq', N'Pay attention to what the man says about when they need the count.',
          N'The man says they need the final head count by tomorrow noon.', 1);
  DECLARE @qP3 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP3, 'mcq', 'A', N'By the end of today', 0),
    (@qP3, 'mcq', 'B', N'By tomorrow noon', 1),
    (@qP3, 'mcq', 'C', N'On Friday morning', 0),
    (@qP3, 'mcq', 'D', N'Next Monday afternoon', 0);
END;

-- Part 4: Short Talks
DECLARE @secP4 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P4');
IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicExam AND SectionId = @secP4)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Transcript)
  VALUES (@toeicExam, @toeicCert, @secP4, 4,
          N'Attention all passengers on flight VN 214 to Tokyo. Due to routine aircraft inspection, boarding will be delayed by approximately twenty minutes. Please remain seated near Gate 12 until boarding begins.');
  DECLARE @grpP4 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpP4, @toeicExam, @secP4, @toeicList,
          N'What is the reason for the flight delay?',
          2, 'mcq', N'Listen for the phrase following "Due to".',
          N'The speaker explicitly announces that the delay is due to routine aircraft inspection.', 1);
  DECLARE @qP4 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP4, 'mcq', 'A', N'Adverse weather conditions', 0),
    (@qP4, 'mcq', 'B', N'Routine aircraft inspection', 1),
    (@qP4, 'mcq', 'C', N'A gate reassignment', 0),
    (@qP4, 'mcq', 'D', N'Crew scheduling changes', 0);
END;
GO

-- 4. TOEIC Reading Sections (P5, P6, P7)
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @toeicExam INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS 2024 — Test 1');
DECLARE @toeicRead INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'READ');
DECLARE @catTense INT = (SELECT CategoryId FROM QuestionCategories WHERE CertId = @toeicCert AND Code = 'grammar_tense');
DECLARE @catPrep INT = (SELECT CategoryId FROM QuestionCategories WHERE CertId = @toeicCert AND Code = 'grammar_prep');
DECLARE @catPos INT = (SELECT CategoryId FROM QuestionCategories WHERE CertId = @toeicCert AND Code = 'grammar_pos');

-- Part 5: Incomplete Sentences
DECLARE @secP5 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P5');
IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicExam AND SectionId = @secP5)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
  VALUES (@toeicExam, @toeicCert, @secP5, 5);
  DECLARE @grpP5 INT = SCOPE_IDENTITY();

  -- Q1: Verb tense
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpP5, @toeicExam, @secP5, @toeicRead, @catTense,
          N'The regional manager ------- the quarterly figures before the board meeting.',
          3, 'mcq',
          N'Does the sentence already have a main verb? Then look at the time marker at the end.',
          N'The sentence lacks a conjugated main verb. "Before the board meeting" indicates a future event, so "will review" is required.', 1);
  DECLARE @qP5_1 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP5_1, 'mcq', 'A', N'review', 0),
    (@qP5_1, 'mcq', 'B', N'reviewing', 0),
    (@qP5_1, 'mcq', 'C', N'will review', 1),
    (@qP5_1, 'mcq', 'D', N'to review', 0);

  -- Q2: Preposition
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpP5, @toeicExam, @secP5, @toeicRead, @catPrep,
          N'All client inquiries must be resolved ------- forty-eight hours of submission.',
          2, 'mcq',
          N'Which preposition means "no later than the end of a given period"?',
          N'"within" + time duration indicates that the action must take place before that span elapses.', 2);
  DECLARE @qP5_2 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP5_2, 'mcq', 'A', N'in', 0),
    (@qP5_2, 'mcq', 'B', N'during', 0),
    (@qP5_2, 'mcq', 'C', N'along', 0),
    (@qP5_2, 'mcq', 'D', N'within', 1);

  -- Q3: Part of speech
  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpP5, @toeicExam, @secP5, @toeicRead, @catPos,
          N'The design studio provides ------- solutions for modern office spaces.',
          3, 'mcq',
          N'The blank modifies the plural noun "solutions", so an adjective is needed.',
          N'"innovative" is the adjective modifying "solutions". "innovation" is a noun and "innovatively" is an adverb.', 3);
  DECLARE @qP5_3 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP5_3, 'mcq', 'A', N'innovation', 0),
    (@qP5_3, 'mcq', 'B', N'innovative', 1),
    (@qP5_3, 'mcq', 'C', N'innovatively', 0),
    (@qP5_3, 'mcq', 'D', N'innovate', 0);
END;

-- Part 6: Text Completion
DECLARE @secP6 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P6');
IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicExam AND SectionId = @secP6)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@toeicExam, @toeicCert, @secP6, 6,
          N'Dear Valued Customer, We ------- your recent order and your package is being prepared for shipment.');
  DECLARE @grpP6 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpP6, @toeicExam, @secP6, @toeicRead, @catTense,
          N'Choose the word or phrase that best completes the sentence in the customer letter.',
          2, 'mcq', N'The action happened in the recent past with continuing relevance to the shipment.',
          N'The present perfect "have received" describes an action that occurred recently with present impact.', 1);
  DECLARE @qP6 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP6, 'mcq', 'A', N'have received', 1),
    (@qP6, 'mcq', 'B', N'will receive', 0),
    (@qP6, 'mcq', 'C', N'are receiving', 0),
    (@qP6, 'mcq', 'D', N'to receive', 0);
END;

-- Part 7: Reading Comprehension
DECLARE @secP7 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'P7');
IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @toeicExam AND SectionId = @secP7)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@toeicExam, @toeicCert, @secP7, 7,
          N'MEMORANDUM\nTo: All Branch Employees\nFrom: Facilities Management\nDate: October 14\nSubject: Elevator Maintenance\nPlease note that the main elevators will undergo safety upgrades this Saturday between 8:00 A.M. and 2:00 P.M. The service elevator near the loading dock will remain accessible throughout this period.');
  DECLARE @grpP7 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpP7, @toeicExam, @secP7, @toeicRead,
          N'What is the primary purpose of the memorandum?',
          2, 'mcq', N'Look at the subject line and the main announcement.',
          N'The memo announces temporary elevator upgrades and directs staff to an alternate service elevator.', 1);
  DECLARE @qP7 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qP7, 'mcq', 'A', N'To announce staff promotions', 0),
    (@qP7, 'mcq', 'B', N'To notify employees of maintenance work', 1),
    (@qP7, 'mcq', 'C', N'To schedule an annual fire drill', 0),
    (@qP7, 'mcq', 'D', N'To request feedback on office amenities', 0);
END;
GO

-- 5. IELTS Listening and Reading Sections (REC, PAS)
DECLARE @ieltsCert INT = (SELECT CertId FROM Certificates WHERE Code = 'IELTS');
DECLARE @ieltsExam INT = (SELECT ExamId FROM Exams WHERE CertId = @ieltsCert AND Name = N'Cambridge IELTS 18 — Academic Test 1');
DECLARE @ieltsList INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'LIST');
DECLARE @ieltsRead INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'READ');
DECLARE @secRec INT = (SELECT SectionId FROM Sections WHERE CertId = @ieltsCert AND Code = 'REC');
DECLARE @secPas INT = (SELECT SectionId FROM Sections WHERE CertId = @ieltsCert AND Code = 'PAS');

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @ieltsExam AND SectionId = @secRec)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Transcript)
  VALUES (@ieltsExam, @ieltsCert, @secRec, 1,
          N'Guide: Welcome to the University Library induction. Daily guided tours depart every half hour from the information desk starting at 9:30 AM.');
  DECLARE @grpRec INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpRec, @ieltsExam, @secRec, @ieltsList,
          N'At what time does the first library induction tour depart?',
          2, 'mcq', N'Listen for the starting time mentioned by the guide.',
          N'The guide states that the tours start at 9:30 AM.', 1);
  DECLARE @qRec INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qRec, 'mcq', 'A', N'9:00 AM', 0),
    (@qRec, 'mcq', 'B', N'9:30 AM', 1),
    (@qRec, 'mcq', 'C', N'10:00 AM', 0),
    (@qRec, 'mcq', 'D', N'10:30 AM', 0);
END;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @ieltsExam AND SectionId = @secPas)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@ieltsExam, @ieltsCert, @secPas, 2,
          N'Recent breakthroughs in geothermal heat pump installations have reduced residential heating expenses by up to forty percent in northern climates.');
  DECLARE @grpPas INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpPas, @ieltsExam, @secPas, @ieltsRead,
          N'What is highlighted as the primary benefit of modern geothermal pumps?',
          3, 'mcq', N'Check the percentage reduction described in the excerpt.',
          N'The passage specifies a reduction of residential heating expenses by up to forty percent.', 1);
  DECLARE @qPas INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qPas, 'mcq', 'A', N'Decreased water consumption', 0),
    (@qPas, 'mcq', 'B', N'Reduced residential heating expenses', 1),
    (@qPas, 'mcq', 'C', N'Zero installation footprint', 0),
    (@qPas, 'mcq', 'D', N'Elimination of electricity grids', 0);
END;
GO

-- 6. VSTEP Listening and Reading Sections (L3, R4)
DECLARE @vstepCert INT = (SELECT CertId FROM Certificates WHERE Code = 'VSTEP');
DECLARE @vstepExam INT = (SELECT ExamId FROM Exams WHERE CertId = @vstepCert AND Name = N'VSTEP Sample Test 1');
DECLARE @vstepList INT = (SELECT SkillId FROM Skills WHERE CertId = @vstepCert AND Code = 'LIST');
DECLARE @vstepRead INT = (SELECT SkillId FROM Skills WHERE CertId = @vstepCert AND Code = 'READ');
DECLARE @secL3 INT = (SELECT SectionId FROM Sections WHERE CertId = @vstepCert AND Code = 'L3');
DECLARE @secR4 INT = (SELECT SectionId FROM Sections WHERE CertId = @vstepCert AND Code = 'R4');

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @vstepExam AND SectionId = @secL3)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Transcript)
  VALUES (@vstepExam, @vstepCert, @secL3, 1,
          N'Speaker: Good afternoon listeners. The municipal transport department announced today that metro line 1 will extend operating hours until midnight starting this weekend.');
  DECLARE @grpL3 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpL3, @vstepExam, @secL3, @vstepList,
          N'What change in metro services was announced by the transport department?',
          2, 'mcq', N'Listen for the detail about operating schedule.',
          N'Operating hours will extend until midnight starting this weekend.', 1);
  DECLARE @qL3 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qL3, 'mcq', 'A', N'Fare reductions during rush hours', 0),
    (@qL3, 'mcq', 'B', N'Extended operating hours until midnight', 1),
    (@qL3, 'mcq', 'C', N'New stations opened along line 2', 0),
    (@qL3, 'mcq', 'D', N'Weekend maintenance closures', 0);
END;

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @vstepExam AND SectionId = @secR4)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@vstepExam, @vstepCert, @secR4, 2,
          N'Ecotourism in the Mekong Delta region has gained rapid popularity as visitors seek authentic cultural experiences in floating markets and fruit orchards.');
  DECLARE @grpR4 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, Hint, Explanation, DisplayOrder)
  VALUES (@grpR4, @vstepExam, @secR4, @vstepRead,
          N'Why has ecotourism in the Mekong Delta grown rapidly according to the text?',
          2, 'mcq', N'Notice the reason given after "as visitors seek...".',
          N'Travelers are seeking authentic cultural experiences such as floating markets and fruit orchards.', 1);
  DECLARE @qR4 INT = SCOPE_IDENTITY();
  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
    (@qR4, 'mcq', 'A', N'Lower hotel prices during summer', 0),
    (@qR4, 'mcq', 'B', N'Demand for authentic cultural experiences', 1),
    (@qR4, 'mcq', 'C', N'Construction of new international airports', 0),
    (@qR4, 'mcq', 'D', N'Government subsidies for tour operators', 0);
END;
GO

PRINT 'demo practice questions seeded for all certificates';
GO

-- ============ Demo Credit Packages (issue #11) ============
IF NOT EXISTS (SELECT 1 FROM Packages WHERE Name = N'Pay as you go')
  INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
  VALUES (N'Pay as you go', 1, 49000, 1, 0, N'1 full-exam credit' + CHAR(10) + N'Review answers and explanations' + CHAR(10) + N'Never expires', 1);

IF NOT EXISTS (SELECT 1 FROM Packages WHERE Name = N'Exam Prep')
  INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
  VALUES (N'Exam Prep', 5, 199000, 1, 1, N'5 full-exam credits' + CHAR(10) + N'Strengths and weaknesses by section' + CHAR(10) + N'Never expires', 2);

IF NOT EXISTS (SELECT 1 FROM Packages WHERE Name = N'Intensive')
  INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
  VALUES (N'Intensive', 10, 349000, 1, 0, N'10 full-exam credits' + CHAR(10) + N'Strengths and weaknesses by section' + CHAR(10) + N'Never expires', 3);
GO

PRINT 'demo credit packages seeded';
GO

-- ============ Demo ScoreScales (issue #15) ============
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
DECLARE @toeicExam INT = (SELECT ExamId FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS 2024 — Test 1');
DECLARE @toeicList INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'LIST');
DECLARE @toeicRead INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'READ');

IF NOT EXISTS (SELECT 1 FROM ScoreScales WHERE CertId = @toeicCert AND ExamId = @toeicExam)
BEGIN
  -- TOEIC ETS 2024 Test 1 has 4 listening questions and 3 reading questions seeded
  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
    (@toeicCert, @toeicList, @toeicExam, 0, 5),
    (@toeicCert, @toeicList, @toeicExam, 1, 150),
    (@toeicCert, @toeicList, @toeicExam, 2, 260),
    (@toeicCert, @toeicList, @toeicExam, 3, 380),
    (@toeicCert, @toeicList, @toeicExam, 4, 495),
    (@toeicCert, @toeicRead, @toeicExam, 0, 5),
    (@toeicCert, @toeicRead, @toeicExam, 1, 175),
    (@toeicCert, @toeicRead, @toeicExam, 2, 340),
    (@toeicCert, @toeicRead, @toeicExam, 3, 495);
END

DECLARE @ieltsCert INT = (SELECT CertId FROM Certificates WHERE Code = 'IELTS');
DECLARE @ieltsExam INT = (SELECT ExamId FROM Exams WHERE CertId = @ieltsCert AND Name = N'Cambridge IELTS 18 — Academic Test 1');
DECLARE @ieltsList INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'LIST');
DECLARE @ieltsRead INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'READ');

IF NOT EXISTS (SELECT 1 FROM ScoreScales WHERE CertId = @ieltsCert AND ExamId = @ieltsExam)
BEGIN
  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
    (@ieltsCert, @ieltsList, @ieltsExam, 0, 20),
    (@ieltsCert, @ieltsList, @ieltsExam, 1, 40),
    (@ieltsCert, @ieltsRead, @ieltsExam, 0, 20),
    (@ieltsCert, @ieltsRead, @ieltsExam, 1, 40);
END

DECLARE @vstepCert INT = (SELECT CertId FROM Certificates WHERE Code = 'VSTEP');
DECLARE @vstepExam INT = (SELECT ExamId FROM Exams WHERE CertId = @vstepCert AND Name = N'VSTEP Sample Test 1');
DECLARE @vstepList INT = (SELECT SkillId FROM Skills WHERE CertId = @vstepCert AND Code = 'LIST');
DECLARE @vstepRead INT = (SELECT SkillId FROM Skills WHERE CertId = @vstepCert AND Code = 'READ');

IF NOT EXISTS (SELECT 1 FROM ScoreScales WHERE CertId = @vstepCert AND ExamId = @vstepExam)
BEGIN
  INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
    (@vstepCert, @vstepList, @vstepExam, 0, 20),
    (@vstepCert, @vstepList, @vstepExam, 1, 35),
    (@vstepCert, @vstepRead, @vstepExam, 0, 20),
    (@vstepCert, @vstepRead, @vstepExam, 1, 35);
END
GO

PRINT 'demo score scales seeded';
GO

-- ============ Demo Writing Practice Exam & Rubric (issue #18) ============
DECLARE @ieltsCert INT = (SELECT CertId FROM Certificates WHERE Code = 'IELTS');
DECLARE @ieltsWrit INT = (SELECT SkillId FROM Skills WHERE CertId = @ieltsCert AND Code = 'WRIT');
DECLARE @secT1 INT = (SELECT SectionId FROM Sections WHERE CertId = @ieltsCert AND Code = 'T1');
DECLARE @secT2 INT = (SELECT SectionId FROM Sections WHERE CertId = @ieltsCert AND Code = 'T2');

-- Seed IELTS Writing Rubric Criteria (TR, CC, LR, GRA - MaxScore 9.0)
IF NOT EXISTS (SELECT 1 FROM GradingCriteria WHERE CertId = @ieltsCert AND SkillId = @ieltsWrit AND Code = 'TR')
  INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
    (@ieltsCert, @ieltsWrit, 'TR', N'Task Response', 9.0, 1.0, 1),
    (@ieltsCert, @ieltsWrit, 'CC', N'Coherence & Cohesion', 9.0, 1.0, 2),
    (@ieltsCert, @ieltsWrit, 'LR', N'Lexical Resource', 9.0, 1.0, 3),
    (@ieltsCert, @ieltsWrit, 'GRA', N'Grammatical Range & Accuracy', 9.0, 1.0, 4);

-- Published Writing Practice Exam
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @ieltsCert AND Name = N'Cambridge IELTS 18 — Writing Practice Test 1')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@ieltsCert, N'Cambridge IELTS 18 — Writing Practice Test 1', 'published', 60, 'writing_practice',
          N'Complete Academic Writing test with Task 1 (visual chart report) and Task 2 (argumentative essay). Scored with AI against official IELTS criteria.',
          N'Cambridge 18', 2);
  DECLARE @writingExam INT = SCOPE_IDENTITY();

  -- Task 1 Group & Question
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@writingExam, @ieltsCert, @secT1, 1,
          N'The chart below shows the percentage of households with internet access in three European countries between 2010 and 2020.' + CHAR(10) +
          N'Summarise the information by selecting and reporting the main features, and make comparisons where relevant.');
  DECLARE @grpT1 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
  VALUES (@grpT1, @writingExam, @secT1, @ieltsWrit,
          N'Write at least 150 words summarizing the main features and comparisons in the chart.',
          3, 'free_response', 150, NULL, 1);

  -- Task 2 Group & Question
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
GO

PRINT 'demo writing practice exam seeded';
GO

-- ============ Demo Speaking Practice Exam & Rubric (issue #19) ============
DECLARE @toeicCert INT = (SELECT CertId FROM Certificates WHERE Code = 'TOEIC');
IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @toeicCert AND Code = 'SPEA')
BEGIN
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
  VALUES (@toeicCert, 'SPEA', N'Speaking', 'speaking', 3, 20);
END;

DECLARE @toeicSpea INT = (SELECT SkillId FROM Skills WHERE CertId = @toeicCert AND Code = 'SPEA');

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @toeicCert AND Code = 'S1')
BEGIN
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
    (@toeicSpea, @toeicCert, 'speaking', 'S1', N'Read a text aloud', NULL, 2, 1),
    (@toeicSpea, @toeicCert, 'speaking', 'S2', N'Describe a picture', NULL, 2, 2),
    (@toeicSpea, @toeicCert, 'speaking', 'S3', N'Respond to questions', NULL, 3, 3),
    (@toeicSpea, @toeicCert, 'speaking', 'S4', N'Respond using information provided', NULL, 3, 4),
    (@toeicSpea, @toeicCert, 'speaking', 'S5', N'Express an opinion', NULL, 1, 5);
END;

IF NOT EXISTS (SELECT 1 FROM GradingCriteria WHERE CertId = @toeicCert AND SkillId = @toeicSpea AND Code = 'PRON')
BEGIN
  INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
    (@toeicCert, @toeicSpea, 'PRON', N'Pronunciation', 3.0, 1.0, 1),
    (@toeicCert, @toeicSpea, 'INT', N'Intonation and stress', 3.0, 1.0, 2),
    (@toeicCert, @toeicSpea, 'GRAM', N'Grammar, vocabulary and cohesion', 3.0, 1.0, 3),
    (@toeicCert, @toeicSpea, 'REL', N'Relevance and completeness', 3.0, 1.0, 4);
END;

DECLARE @secS1 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'S1');
DECLARE @secS2 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'S2');
DECLARE @secS3 INT = (SELECT SectionId FROM Sections WHERE CertId = @toeicCert AND Code = 'S3');

IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @toeicCert AND Name = N'ETS TOEIC Speaking Practice Exam 1')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, SourceName, QuestionCount)
  VALUES (@toeicCert, N'ETS TOEIC Speaking Practice Exam 1', 'published', 20, 'speaking_practice',
          N'Complete workplace English speaking practice exam with 11 questions across 5 task types. Scored with AI against official pronunciation and fluency rubric criteria.',
          N'ETS Official 2024', 3);
  DECLARE @speakingExam INT = SCOPE_IDENTITY();

  -- Question 1: Read aloud
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@speakingExam, @toeicCert, @secS1, 1,
          N'Attention all shoppers. The annual summer clearance sale will end today at 9:00 PM. Please bring all selected items to the front registers.');
  DECLARE @grpS1 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
  VALUES (@grpS1, @speakingExam, @secS1, @toeicSpea,
          N'Read the announcement aloud as clearly and naturally as possible.',
          2, 'free_response', 45, 45, 1);

  -- Question 2: Describe picture
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, ImagePath, ImageAlt)
  VALUES (@speakingExam, @toeicCert, @secS2, 2,
          N'/assets/img/part1-sample.svg', N'Office workers in a team meeting');
  DECLARE @grpS2 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
  VALUES (@grpS2, @speakingExam, @secS2, @toeicSpea,
          N'Look at the picture and describe it in as much detail as you can.',
          2, 'free_response', 45, 30, 2);

  -- Question 3: Respond to question
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
  VALUES (@speakingExam, @toeicCert, @secS3, 3);
  DECLARE @grpS3 INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
  VALUES (@grpS3, @speakingExam, @secS3, @toeicSpea,
          N'How often do you go shopping for clothes, and what is your favorite clothing store?',
          3, 'free_response', 3, 15, 3);
END;
GO

PRINT 'demo speaking practice exam seeded';
GO
