-- docs/audit/161-demo-certificate.sql
-- #161 acceptance walk: a certificate the code has never heard of.
--
-- Two skills whose names are not Listening/Reading, and four bands whose ranges are not
-- anything the CEFR ladder covers. If any page still carries a certificate literal or a
-- six-column band strip, it shows here and nowhere else - the three seeded certificates
-- happen to match every literal the templates used to hold.
--
-- Applied on top of docs/seed-demo.sql in the crawl environment only. Not part of the seed
-- a real deployment loads: a demo certificate belongs in a test, not in the catalogue.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM Certificates WHERE Code = 'DEMO')
BEGIN
  INSERT INTO Certificates (Code, Name, Description, IsActive, ScoreAggregationRule, PresetCode, SectionNoun)
  VALUES ('DEMO', N'Demo Certificate',
          N'A certificate that exists only to prove nothing is hardcoded.', 1, 'sum', NULL, N'Unit');
END;
GO

DECLARE @demo INT = (SELECT CertId FROM Certificates WHERE Code = 'DEMO');

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @demo AND Code = 'COMP')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep)
  VALUES (@demo, 'COMP', N'Comprehension', 'listening', 1, 45, 0, 250, 5);

IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @demo AND Code = 'EXPR')
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep)
  VALUES (@demo, 'EXPR', N'Expression', 'writing', 2, 60, 0, 250, 5);

DECLARE @comp INT = (SELECT SkillId FROM Skills WHERE CertId = @demo AND Code = 'COMP');
DECLARE @expr INT = (SELECT SkillId FROM Skills WHERE CertId = @demo AND Code = 'EXPR');

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @demo AND Code = 'C1')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, NavigationMode)
  VALUES (@comp, @demo, 'listening', 'C1', N'First passage', 4, 10, 1, 'FULL_SANDBOX');

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @demo AND Code = 'C2')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, NavigationMode)
  VALUES (@comp, @demo, 'listening', 'C2', N'Second passage', 4, 10, 2, 'FULL_SANDBOX');

IF NOT EXISTS (SELECT 1 FROM Sections WHERE CertId = @demo AND Code = 'E1')
  INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder, NavigationMode, AllowedComponentTypesJson)
  VALUES (@expr, @demo, 'writing', 'E1', N'First task', 4, 1, 1, 'FULL_SANDBOX', '["free_response"]');

-- Four bands on a 0-500 scale. The overlap trigger allows only strict gaps, so each band ends one
-- point before the next begins: 0-99, 100-199, 200-399, 400-500. Equal quarters would put D3's right
-- edge at 375 instead of 399, so the proportions are visible at a glance.
IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @demo AND Code = 'D1')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@demo, 'D1', N'Starting', 0, 99, 1);
IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @demo AND Code = 'D2')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@demo, 'D2', N'Developing', 100, 199, 2);
IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @demo AND Code = 'D3')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@demo, 'D3', N'Strong', 200, 399, 3);
IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = @demo AND Code = 'D4')
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@demo, 'D4', N'Mastery', 400, 500, 4);

-- One published exam with a keyed question per comprehension section, so the practice page has a
-- drillable card and the history page has something the learner can sit.
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @demo AND Name = N'Demo mock exam')
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, DeliveryMode, ExamFamily, Description, QuestionCount)
  VALUES (@demo, N'Demo mock exam', 'published', 30, 'mock', 'strict_exam', 'DEMO',
          N'Two comprehension sections and one writing task.', 2);

DECLARE @demoExam INT = (SELECT ExamId FROM Exams WHERE CertId = @demo AND Name = N'Demo mock exam');
DECLARE @demoSecC1 INT = (SELECT SectionId FROM Sections WHERE CertId = @demo AND Code = 'C1');

IF NOT EXISTS (SELECT 1 FROM QuestionGroups WHERE ExamId = @demoExam AND SectionId = @demoSecC1 AND DisplayOrder = 1)
BEGIN
  INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, Passage)
  VALUES (@demoExam, @demo, @demoSecC1, 1, N'The demo passage has one question.');

  DECLARE @demoGroup INT = SCOPE_IDENTITY();

  INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, QuestionType, DifficultyLevel, DisplayOrder)
  VALUES (@demoGroup, @demoExam, @demoSecC1, @comp,
          N'Pick the answer that matches the passage.', 'mcq', 3, 1);

  DECLARE @demoQuestion INT = SCOPE_IDENTITY();

  INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect)
  VALUES (@demoQuestion, 'mcq', 'A', N'The one in the passage.', 1),
         (@demoQuestion, 'mcq', 'B', N'A distractor.', 0),
         (@demoQuestion, 'mcq', 'C', N'Another distractor.', 0),
         (@demoQuestion, 'mcq', 'D', N'A fourth option.', 0);
END;
GO

-- Put the demo learner on the DEMO certificate so the walk starts there, and give them one finished
-- placement attempt and one graded mock attempt, so the two result-bearing pages have data to render.
DECLARE @learner INT = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local');
DECLARE @demoCert INT = (SELECT CertId FROM Certificates WHERE Code = 'DEMO');
DECLARE @comp INT = (SELECT SkillId FROM Skills WHERE CertId = @demoCert AND Code = 'COMP');
DECLARE @expr INT = (SELECT SkillId FROM Skills WHERE CertId = @demoCert AND Code = 'EXPR');
IF @learner IS NOT NULL
  UPDATE Users SET ActiveCertId = @demoCert WHERE UserId = @learner;

DECLARE @placementExam INT;
IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @demoCert AND Purpose = 'placement')
BEGIN
  INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, DeliveryMode, ExamFamily, Description, QuestionCount)
  VALUES (@demoCert, N'Demo placement test', 'published', 20, 'placement', 'strict_exam', 'DEMO',
          N'Two comprehension sections and one writing task.', 20);
  SET @placementExam = SCOPE_IDENTITY();
END
ELSE
  SET @placementExam = (SELECT ExamId FROM Exams WHERE CertId = @demoCert AND Purpose = 'placement');

IF @learner IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM PlacementTests WHERE UserId = @learner AND CertId = @demoCert)
BEGIN
  INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
  VALUES (@learner, @placementExam, @demoCert, 'graded',
          DATEADD(DAY, -2, SYSDATETIMEOFFSET()),
          DATEADD(DAY, -2, DATEADD(HOUR, 1, SYSDATETIMEOFFSET())),
          DATEADD(DAY, -2, DATEADD(MINUTE, 34, SYSDATETIMEOFFSET())), 150, 'D2');
  DECLARE @demoPlacementAttempt INT = SCOPE_IDENTITY();

  INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore) VALUES
    (@demoPlacementAttempt, @comp, @demoCert, 8, 80),
    (@demoPlacementAttempt, @expr, @demoCert, 7, 70);

  INSERT INTO PlacementTests (UserId, CertId, ExamId, Status, ResultBandCode, AttemptId, TotalScore, TakenAt)
  VALUES (@learner, @demoCert, @placementExam, 'done', 'D2', @demoPlacementAttempt, 150,
          DATEADD(DAY, -2, SYSDATETIMEOFFSET()));

  DECLARE @demoMockExam INT = (SELECT ExamId FROM Exams WHERE CertId = @demoCert AND Name = N'Demo mock exam');
  INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
  VALUES (@learner, @demoMockExam, @demoCert, 'graded',
          DATEADD(DAY, -1, SYSDATETIMEOFFSET()),
          DATEADD(DAY, -1, DATEADD(HOUR, 1, SYSDATETIMEOFFSET())),
          DATEADD(DAY, -1, DATEADD(MINUTE, 28, SYSDATETIMEOFFSET())), 150, 'D2');
  DECLARE @demoMockAttempt INT = SCOPE_IDENTITY();

  INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore) VALUES
    (@demoMockAttempt, @comp, @demoCert, 8, 80),
    (@demoMockAttempt, @expr, @demoCert, 7, 70);
END;
GO

PRINT 'DEMO certificate ready: COMP/EXPR skills, four bands on 0-500, sections C1/C2/E1';
GO
