-- -------------------------------------------------------------------------------------------
-- GENERATED FILE - do not hand-edit.
--
-- Dumped from a database holding the correct end state by scripts/dump-seed.ps1.
-- The pool (Stimuli/Items/ItemVersions/ExamItems) is the only content model: this seed writes
-- no Questions, QuestionGroups or QuestionOptions rows, because those tables are gone (#250).
--
-- Every row is its own TRY/CATCH filtered to duplicate-key errors (2627, 2601), so re-running
-- changes no count while any OTHER error still throws loudly.
--
-- Regenerate with:  ./scripts/dump-seed.ps1 -Database <a migrated database>
-- -------------------------------------------------------------------------------------------
SET NOCOUNT ON;
GO

-- The guards ask who is writing (ADR-0012). A seed is a fixture catalog: declare it, and restore
-- the previous value at the end so a run against a real catalog leaves it as it found it.
IF OBJECT_ID('tempdb..#ee_seed_prior_fixture') IS NOT NULL DROP TABLE #ee_seed_prior_fixture;
CREATE TABLE #ee_seed_prior_fixture (Was BIT NOT NULL);
INSERT INTO #ee_seed_prior_fixture (Was)
  SELECT CASE WHEN OBJECT_ID('CatalogMode', 'U') IS NULL THEN 0 ELSE dbo.fn_IsFixtureCatalog() END;
IF OBJECT_ID('CatalogMode', 'U') IS NOT NULL
BEGIN
  IF EXISTS (SELECT 1 FROM CatalogMode WHERE ModeId = 1)
    UPDATE CatalogMode SET IsFixtureCatalog = 1 WHERE ModeId = 1;
  ELSE
    INSERT INTO CatalogMode (ModeId, IsFixtureCatalog) VALUES (1, 1);
END;
GO

-- ---- CatalogMode (1 rows)
BEGIN TRY
  INSERT INTO [CatalogMode] ([ModeId], [IsFixtureCatalog]) VALUES (1, 0);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH

-- ---- Certificates (5 rows)
SET IDENTITY_INSERT [Certificates] ON;
BEGIN TRY
  INSERT INTO [Certificates] ([CertId], [Code], [Name], [Description], [IsActive], [ScoreAggregationRule], [PresetCode], [SectionNoun]) VALUES (1, N'TOEIC', N'TOEIC Listening & Reading', N'The standard workplace English test. 200 multiple-choice questions, about 2 hours, scored 10–990. Practice speaking and writing separately with AI-graded tasks.', 1, N'sum', N'toeic_lr_std', N'Part');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Certificates] ([CertId], [Code], [Name], [Description], [IsActive], [ScoreAggregationRule], [PresetCode], [SectionNoun]) VALUES (2, N'IELTS', N'IELTS Academic & General', N'The four-skill test for study, migration and work abroad. Scored in 0.5 steps from 0.0 to 9.0, with a separate band for each skill.', 1, N'avg_half_step', N'ielts_academic', N'Section');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Certificates] OFF;



-- ---- Packages (3 rows)
SET IDENTITY_INSERT [Packages] ON;
BEGIN TRY
  INSERT INTO [Packages] ([PackageId], [Name], [Credits], [Price], [IsActive], [IsFeatured], [Features], [DisplayOrder]) VALUES (1, N'Intensive Pack', 10, 190000.00, 1, 1, N'10 full-exam / grading credits' + CHAR(10) + N'Detailed weakness diagnostic' + CHAR(10) + N'Priority AI mentor evaluation' + CHAR(10) + N'Never expires', 2);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Packages] ([PackageId], [Name], [Credits], [Price], [IsActive], [IsFeatured], [Features], [DisplayOrder]) VALUES (2, N'Starter Pack', 5, 100000.00, 1, 0, N'5 full-exam / grading credits' + CHAR(10) + N'Access to AI mentor feedback' + CHAR(10) + N'Never expires', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Packages] ([PackageId], [Name], [Credits], [Price], [IsActive], [IsFeatured], [Features], [DisplayOrder]) VALUES (3, N'Master Pack', 20, 360000.00, 1, 0, N'20 full-exam / grading credits' + CHAR(10) + N'Complete path unlocking' + CHAR(10) + N'1-on-1 human mentor booking' + CHAR(10) + N'Never expires', 3);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Packages] OFF;

-- ---- Permissions (5 rows)
BEGIN TRY
  INSERT INTO [Permissions] ([Code]) VALUES (N'assessment.blueprint.design');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Permissions] ([Code]) VALUES (N'operations.support.triage');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Permissions] ([Code]) VALUES (N'pool.content.author');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Permissions] ([Code]) VALUES (N'pool.content.review');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Permissions] ([Code]) VALUES (N'system.governance.admin');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH

-- ---- QuestionCategories (15 rows)
SET IDENTITY_INSERT [QuestionCategories] ON;
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (1, 1, N'grammar_tense', N'Verb tense');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (2, 1, N'grammar_prep', N'Preposition');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (3, 1, N'grammar_pos', N'Part of speech');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (4, 1, N'grammar_rel', N'Relative pronoun');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (5, 1, N'grammar_conj', N'Conjunction');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (6, 1, N'listening_photo', N'Photographs');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (7, 1, N'listening_detail', N'Detail & facts');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (8, 1, N'reading_vocab', N'Vocabulary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (9, 1, N'reading_inference', N'Inference');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (10, 2, N'ielts_detail', N'Factual detail');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (11, 2, N'ielts_inference', N'Inference');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [QuestionCategories] ([CategoryId], [CertId], [Code], [Name]) VALUES (12, 2, N'ielts_main_idea', N'Main idea');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [QuestionCategories] OFF;

-- ---- Roles (2 rows)
SET IDENTITY_INSERT [Roles] ON;
BEGIN TRY
  INSERT INTO [Roles] ([RoleId], [Code], [Name]) VALUES (1, N'learner', N'Learner');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Roles] ([RoleId], [Code], [Name]) VALUES (2, N'admin', N'Administrator');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Roles] OFF;

-- ---- ScoreBands (18 rows)
SET IDENTITY_INSERT [ScoreBands] ON;
BEGIN TRY
  INSERT INTO [ScoreBands] ([BandId], [CertId], [Code], [Name], [MinTotal], [MaxTotal], [DisplayOrder], [CumulativeStudyHours]) VALUES (1, 1, N'below_A1', N'Below A1', 10, 119, 0, 0);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreBands] ([BandId], [CertId], [Code], [Name], [MinTotal], [MaxTotal], [DisplayOrder], [CumulativeStudyHours]) VALUES (2, 1, N'A1', N'Beginner', 120, 220, 1, 100);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreBands] ([BandId], [CertId], [Code], [Name], [MinTotal], [MaxTotal], [DisplayOrder], [CumulativeStudyHours]) VALUES (3, 1, N'A2', N'Elementary', 225, 545, 2, 250);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreBands] ([BandId], [CertId], [Code], [Name], [MinTotal], [MaxTotal], [DisplayOrder], [CumulativeStudyHours]) VALUES (4, 1, N'B1', N'Intermediate', 550, 780, 3, 500);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreBands] ([BandId], [CertId], [Code], [Name], [MinTotal], [MaxTotal], [DisplayOrder], [CumulativeStudyHours]) VALUES (5, 1, N'B2', N'Upper intermediate', 785, 940, 4, 800);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreBands] ([BandId], [CertId], [Code], [Name], [MinTotal], [MaxTotal], [DisplayOrder], [CumulativeStudyHours]) VALUES (6, 1, N'C1', N'Advanced', 945, 990, 5, 1150);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreBands] ([BandId], [CertId], [Code], [Name], [MinTotal], [MaxTotal], [DisplayOrder], [CumulativeStudyHours]) VALUES (7, 2, N'B1', N'Intermediate (4.0–5.0)', 40, 50, 1, 500);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreBands] ([BandId], [CertId], [Code], [Name], [MinTotal], [MaxTotal], [DisplayOrder], [CumulativeStudyHours]) VALUES (8, 2, N'B2', N'Upper intermediate (5.5–6.5)', 55, 65, 2, 800);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreBands] ([BandId], [CertId], [Code], [Name], [MinTotal], [MaxTotal], [DisplayOrder], [CumulativeStudyHours]) VALUES (9, 2, N'C1', N'Advanced (7.0–8.0)', 70, 80, 3, 1150);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreBands] ([BandId], [CertId], [Code], [Name], [MinTotal], [MaxTotal], [DisplayOrder], [CumulativeStudyHours]) VALUES (10, 2, N'C2', N'Expert (8.5–9.0)', 85, 90, 4, 1550);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [ScoreBands] OFF;

-- ---- Skills (16 rows)
SET IDENTITY_INSERT [Skills] ON;
BEGIN TRY
  INSERT INTO [Skills] ([SkillId], [CertId], [Code], [Name], [Modality], [DisplayOrder], [DurationMinutes], [MinScaledScore], [MaxScaledScore], [ScoreStep], [LockNavigationOneWay]) VALUES (1, 1, N'LIST', N'Listening', N'listening', 1, 45, 5, 495, 5, 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Skills] ([SkillId], [CertId], [Code], [Name], [Modality], [DisplayOrder], [DurationMinutes], [MinScaledScore], [MaxScaledScore], [ScoreStep], [LockNavigationOneWay]) VALUES (2, 1, N'READ', N'Reading', N'reading', 2, 75, 5, 495, 5, 0);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Skills] ([SkillId], [CertId], [Code], [Name], [Modality], [DisplayOrder], [DurationMinutes], [MinScaledScore], [MaxScaledScore], [ScoreStep], [LockNavigationOneWay]) VALUES (5, 2, N'LIST', N'Listening', N'listening', 1, 30, 20, 90, 10, 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Skills] ([SkillId], [CertId], [Code], [Name], [Modality], [DisplayOrder], [DurationMinutes], [MinScaledScore], [MaxScaledScore], [ScoreStep], [LockNavigationOneWay]) VALUES (6, 2, N'READ', N'Reading', N'reading', 2, 60, 20, 90, 10, 0);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Skills] ([SkillId], [CertId], [Code], [Name], [Modality], [DisplayOrder], [DurationMinutes], [MinScaledScore], [MaxScaledScore], [ScoreStep], [LockNavigationOneWay]) VALUES (7, 2, N'WRIT', N'Writing', N'writing', 3, 60, NULL, NULL, NULL, 0);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Skills] ([SkillId], [CertId], [Code], [Name], [Modality], [DisplayOrder], [DurationMinutes], [MinScaledScore], [MaxScaledScore], [ScoreStep], [LockNavigationOneWay]) VALUES (8, 2, N'SPEA', N'Speaking', N'speaking', 4, 14, NULL, NULL, NULL, 0);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Skills] OFF;

-- ---- Stimuli (78 rows)
SET IDENTITY_INSERT [Stimuli] ON;
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (1, N'A2', NULL, N'/audio/vstep/sample1-listening.mp3', NULL, NULL, N'Speaker: Good afternoon listeners. The annual Da Nang International Fireworks Festival will open next month on June 8th along the Han River.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:02.8064239+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (2, N'B1', N'Community-based tourism has expanded significantly in the northern mountainous provinces of Vietnam. Local homestays in Ha Giang and Yen Bai allow visitors to experience ethnic cultural traditions while generating sustainable income for rural families.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:02.8146559+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (3, N'B1', NULL, N'/audio/toeic/test1-p1.mp3', NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers gathered around a whiteboard', NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (4, N'B1', NULL, N'/audio/toeic/test1-p2.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (5, N'B2', NULL, N'/audio/toeic/test1-p3.mp3', NULL, NULL, N'Woman: Good morning, Mr. Evans. Did the catering team confirm the order for Friday''s banquet?' + CHAR(10) + N'Man: Yes, they did, but they said they need our final head count by tomorrow noon.' + CHAR(10) + N'Woman: I will email the department heads right away to get their RSVPs.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (6, N'B1', NULL, N'/audio/toeic/test1-p4.mp3', NULL, NULL, N'Attention all passengers on flight VN 214 to Tokyo. Due to routine aircraft inspection, boarding will be delayed by approximately twenty minutes. Please remain seated near Gate 12 until boarding begins.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (7, N'B1', N'Dear Valued Customer, We ------- your recent order and your package is being prepared for shipment.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (8, N'B1', N'MEMORANDUM' + CHAR(10) + N'To: All Branch Employees' + CHAR(10) + N'From: Facilities Management' + CHAR(10) + N'Date: October 14' + CHAR(10) + N'Subject: Elevator Maintenance' + CHAR(10) + N'Please note that the main elevators will undergo safety upgrades this Saturday between 8:00 A.M. and 2:00 P.M. The service elevator near the loading dock will remain accessible throughout this period.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (9, N'A2', NULL, N'/audio/toeic/placement-p1.mp3', NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a meeting room', NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (10, N'B1', NULL, N'/audio/toeic/placement-p1.mp3', NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a meeting room', NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (11, N'A2', NULL, N'/audio/toeic/placement-p2.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (12, N'B1', NULL, N'/audio/toeic/placement-p2.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (13, N'B1', NULL, N'/audio/toeic/placement-p2.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (14, N'B2', NULL, N'/audio/toeic/placement-p2.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (15, N'B1', NULL, N'/audio/toeic/test1-p3.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (16, N'B2', NULL, N'/audio/toeic/test1-p3.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (17, N'B1', NULL, N'/audio/toeic/test1-p4.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (18, N'B2', NULL, N'/audio/toeic/test1-p4.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (19, N'B1', N'Attention all shoppers. The annual summer clearance sale will end today at 9:00 PM. Please bring all selected items to the front registers.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (20, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (21, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Meeting in conference room', NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (22, N'C1', N'Some companies offer employees the option of flexible working hours. In your opinion, what are the advantages and disadvantages of flexible working schedules?', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (23, N'B1', NULL, N'/audio/ielts/cambridge18-t1-listening.mp3', NULL, NULL, N'Guide: Welcome to the University Library induction. Daily guided tours depart every half hour from the information desk starting at 9:30 AM.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (24, N'B2', N'Urban reforestation initiatives have demonstrated measurable cooling effects in metropolitan centres. Studies in Singapore and Seoul indicate that rooftop gardens and street canopy trees reduce localized surface temperatures by up to 2.8 degrees Celsius during peak summer months.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (25, N'B1', NULL, N'/audio/ielts/placement-diagnostic.mp3', NULL, NULL, N'Officer: The student housing office is open Monday to Thursday from 9 AM to 4 PM. On Fridays we close at 1 PM for staff training.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (26, N'B1', N'Renewable energy capacity expanded by 50% in 2023 worldwide, with solar photovoltaics accounting for three-quarters of new installations globally.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (27, N'C1', N'Describe a piece of technology you find difficult to use.' + CHAR(10) + N'You should say:' + CHAR(10) + N'- What it is' + CHAR(10) + N'- When you got it' + CHAR(10) + N'- What you use it for' + CHAR(10) + N'And explain why you find it difficult to use.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (28, N'B2', N'The chart below shows the percentage of households with internet access in three European countries between 2010 and 2020.' + CHAR(10) + N'Summarise the information by selecting and reporting the main features, and make comparisons where relevant.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (29, N'C1', N'Some people believe that unpaid community service should be a compulsory part of high school programmes (such as working for a charity, improving the neighborhood, or coaching younger children).' + CHAR(10) + N'To what extent do you agree or disagree? Give reasons for your answer and include any relevant examples from your own knowledge or experience.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (30, N'B1', NULL, N'/audio/vstep/placement-diagnostic.mp3', NULL, NULL, N'Lecturer: Please submit your research proposals by Friday 5 PM through the faculty portal.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (31, N'B1', N'Artificial intelligence is being integrated into medical imaging across hospital networks, providing diagnostic assistance for radiology scans.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (32, N'B2', N'Topic: Should universities make physical education courses compulsory for all undergraduate students?' + CHAR(10) + N'- Health benefits' + CHAR(10) + N'- Academic stress relief' + CHAR(10) + N'- Student freedom of choice', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (33, N'B2', N'You received an email from an international friend asking for recommendations on historical places to visit in Hanoi.' + CHAR(10) + N'Write an email of at least 120 words suggesting two places, explaining why they are interesting, and giving advice on when to visit.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (34, N'C1', N'Some people think that online learning will eventually replace traditional classrooms. To what extent do you agree or disagree?', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (35, N'B1', NULL, N'/audio/toeic/placement-p1.mp3', 0, 18000, N'(A) They are boarding a commuter train. (B) They are examining a blueprint on the desk. (C) They are hanging a picture on the wall. (D) The projector screen is being rolled up.', N'general', NULL, N'/img/part1-sample.svg', N'Two engineers standing near an office desk looking closely at an architectural blueprint', NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (36, N'B1', NULL, N'/audio/toeic/placement-p1.mp3', 18000, 36000, N'(A) Some chairs are stacked in the corner. (B) A technician is repairing the light fixture. (C) Documents are being distributed to attendees. (D) Monitor screens are arranged in a row on the table.', N'general', NULL, N'/img/part1-sample.svg', N'An empty modern conference room with computer monitors lined up neatly along the table', NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (37, N'B1', NULL, N'/audio/toeic/placement-p2.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (38, N'B1', NULL, N'/audio/toeic/placement-p2.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (39, N'B1', NULL, N'/audio/toeic/placement-p2.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (40, N'B1', NULL, N'/audio/toeic/placement-p2.mp3', NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (41, N'B1', N'Questions 7 through 9 refer to the following conversation.', N'/audio/toeic/test1-p3.mp3', 0, 48000, N'Man: Hi, Clara. I just received a call from Apex Logistics regarding our shipment of ergonomic office chairs for the new Seattle branch. Unfortunately, two of the delivery trucks are stuck in Denver due to severe weather.' + CHAR(10) + N'Woman: Oh no! Our interior setup team is scheduled to assemble the workstations this Thursday morning. Did they say when the trucks will arrive?' + CHAR(10) + N'Man: They estimate a forty-eight-hour delay, which means the chairs won''t get there until Friday afternoon.' + CHAR(10) + N'Woman: In that case, I''d better contact the setup agency right away to reschedule their technicians for Saturday so we don''t get charged a standby fee.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (42, N'B1', N'Questions 10 through 12 refer to the following announcement.', N'/audio/toeic/test1-p4.mp3', 0, 52000, N'Attention, attendees of the Global FinTech Summit. Due to an unexpected audio-visual malfunction in Ballroom A, this morning''s keynote address by Dr. Elena Rostova on cybersecurity trends has been relocated to the Sapphire Auditorium on the mezzanine level. Because the Sapphire Auditorium has a slightly smaller seating capacity, overflow seating with a live video feed will be available in Room 204. Also, please remember to scan your digital conference badge at the entrance of the auditorium if you wish to receive Continuing Professional Education credits for attending this session. Thank you for your cooperation.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (43, N'B1', N'Questions 21 through 24 refer to the following memorandum.' + CHAR(10) + N'' + CHAR(10) + N'To: All Regional Sales Representatives' + CHAR(10) + N'From: Marcus Vance, Director of Operations' + CHAR(10) + N'Date: October 12' + CHAR(10) + N'Subject: Transition to CloudSync Expense Portal' + CHAR(10) + N'' + CHAR(10) + N'Effective November 1, our company will retire the legacy spreadsheet system used for travel expense reporting and transition fully to the CloudSync Expense Portal. This upgrade is designed to [21] ------- the reimbursement process so that employees receive their funds within three business days rather than two weeks.' + CHAR(10) + N'' + CHAR(10) + N'To ensure a smooth transition, all sales staff are required to complete a 30-minute interactive tutorial before October 25. [22] -------. Once you have finished the module, your login credentials will be activated automatically.' + CHAR(10) + N'' + CHAR(10) + N'Please note that any paper receipts dated prior to November 1 must still be processed through the old system. [23] -------, expenses incurred on or after that date will only be accepted via the CloudSync mobile app or web dashboard. Should you encounter any technical difficulties during registration, please contact the IT Help Desk [24] ------- extension 4402.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (44, N'B1', N'Questions 25 through 27 refer to the following workshop schedule.' + CHAR(10) + N'' + CHAR(10) + N'====================================================================' + CHAR(10) + N'PACIFIC NORTHWEST SMALL BUSINESS SYMPOSIUM' + CHAR(10) + N'Date: Friday, November 14 | Venue: Grand Harbor Convention Center' + CHAR(10) + N'====================================================================' + CHAR(10) + N'' + CHAR(10) + N'09:00 A.M. – 10:15 A.M. | Session 1: Mastering Supply Chain Resilience' + CHAR(10) + N'Speaker: Marcus Thorne (COO, Cascade Freight Solutions)' + CHAR(10) + N'Room: Olympic Hall' + CHAR(10) + N'Overview: Learn practical strategies for diversifying suppliers and reducing shipping bottlenecks for regional retailers.' + CHAR(10) + N'' + CHAR(10) + N'10:30 A.M. – 11:45 A.M. | Session 2: Digital Marketing on a Shoestring Budget' + CHAR(10) + N'Speaker: Priya Patel (Founder, Spark Media Agency)' + CHAR(10) + N'Room: Rainier Suite' + CHAR(10) + N'Overview: Discover how to maximize customer engagement using targeted social media campaigns without hiring an external agency. *Note: Attendees should bring a fully charged laptop for the hands-on analytics exercise.*' + CHAR(10) + N'' + CHAR(10) + N'01:00 P.M. – 02:15 P.M. | Session 3: Navigating Commercial Tax Reforms' + CHAR(10) + N'Speaker: Evelyn Cho, CPA (Senior Partner, Cho & Associates)' + CHAR(10) + N'Room: Columbia Room' + CHAR(10) + N'Overview: A detailed breakdown of upcoming state tax incentives for businesses with fewer than 50 full-time employees.' + CHAR(10) + N'' + CHAR(10) + N'*Registration includes complimentary morning coffee and a buffet lunch served in the Emerald Atrium from 11:45 A.M. to 1:00 P.M. Seating for Session 2 is limited to 40 participants; advance sign-up via the symposium mobile app is mandatory.*', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (45, N'B1', N'Questions 28 through 30 refer to the following invoice and e-mail.' + CHAR(10) + N'' + CHAR(10) + N'--------------------------------------------------------------------' + CHAR(10) + N' PASSAGE 1: INVOICE #INV-8841' + CHAR(10) + N' Vendor: Lumina Botanicals Wholesale' + CHAR(10) + N' Client: Serenity Day Spa (Attn: Hannah Abbott)' + CHAR(10) + N' Order Date: March 3 | Delivery Date: March 6' + CHAR(10) + N'--------------------------------------------------------------------' + CHAR(10) + N' Item Code | Description                        | Qty | Unit Price | Total' + CHAR(10) + N' LB-102    | Organic Lavender Essential Oil 1L  |  5  | $40.00     | $200.00' + CHAR(10) + N' LB-205    | Eucalyptus Massage Lotion 5L       |  4  | $55.00     | $220.00' + CHAR(10) + N' LB-310    | Bamboo Facial Towels (Pack of 50)  |  6  | $30.00     | $180.00' + CHAR(10) + N' LB-408    | Chamomile Soothing Serum 500ml     |  2  | $65.00     | $130.00' + CHAR(10) + N'--------------------------------------------------------------------' + CHAR(10) + N' Subtotal: $730.00' + CHAR(10) + N' Shipping (Express Courier): $0.00 (Waived for orders over $500)' + CHAR(10) + N' Total Charged: $730.00' + CHAR(10) + N' *Return Policy: Claims for damaged or incorrect items must be submitted within 5 business days of delivery to qualify for a full refund or free replacement.*' + CHAR(10) + N'' + CHAR(10) + N'--------------------------------------------------------------------' + CHAR(10) + N' PASSAGE 2: E-MAIL' + CHAR(10) + N' To: billing@luminabotanicals.com' + CHAR(10) + N' From: h.abbott@serenitydayspa.com' + CHAR(10) + N' Date: March 7' + CHAR(10) + N' Subject: Issue with Order #INV-8841' + CHAR(10) + N'--------------------------------------------------------------------' + CHAR(10) + N' Dear Customer Support,' + CHAR(10) + N'' + CHAR(10) + N' Thank you for the prompt delivery of our wholesale order yesterday. Everything arrived intact except for one item. While the invoice correctly lists the Bamboo Facial Towels we ordered, the boxes we actually received contained standard cotton hand towels instead.' + CHAR(10) + N'' + CHAR(10) + N' Because we have a VIP wellness retreat scheduled for next weekend, we urgently need the correct towels sent out via express courier. Alternatively, if that item is currently out of stock, please issue a refund for that portion of the order to our corporate credit card so we can source them locally. We have left the unopened boxes of cotton towels at our reception desk for your driver to pick up.' + CHAR(10) + N'' + CHAR(10) + N' Sincerely,' + CHAR(10) + N' Hannah Abbott' + CHAR(10) + N' Operations Manager, Serenity Day Spa', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (46, N'B2', N'The line chart shows the percentage of households with access to three energy sources in a European country from 2010 to 2020: solar rose from 10% to 50%, wind from 20% to 35%, and coal fell from 60% to 25%.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (47, N'C1', N'Some people believe that universities should focus on practical job skills, while others think academic knowledge is more important. Discuss both views and give your own opinion.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (48, N'B2', N'You have received an email from the coordinator of a local community project. Reply to explain which weekend activity you can join, ask for the meeting details, and suggest one way to prepare.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (49, N'C1', N'Some people think that cities should invest more in public parks than in shopping centres. Do you agree or disagree? Give reasons and examples.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (50, N'B2', NULL, N'/audio/ielts/placement-diagnostic.mp3', 30000, 60000, N'Applicant: I can attend the laboratory induction on Tuesday. Is the 10 o''clock session still available?' + CHAR(10) + N'Coordinator: It is full, but there are two places in the 2:30 session. Please bring your student card.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (51, N'B2', NULL, N'/audio/ielts/placement-diagnostic.mp3', 60000, 90000, N'Tutor: Your draft is well researched. Before Friday, reorganise the second section around the comparison rather than the dates.' + CHAR(10) + N'Student: Should I add another source?' + CHAR(10) + N'Tutor: Not yet. Make the contrast clearer first.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (52, N'B2', NULL, N'/audio/ielts/placement-diagnostic.mp3', 90000, 120000, N'Guide: The north gallery is closed for lighting work this morning. The sculpture collection has moved upstairs, beside the reading room.' + CHAR(10) + N'Visitor: Is the café affected?' + CHAR(10) + N'Guide: No, it opens at the usual time.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (53, N'B2', NULL, N'/audio/ielts/placement-diagnostic.mp3', 120000, 150000, N'Researcher: We expected the survey to finish in May, but the final interviews were delayed.' + CHAR(10) + N'Manager: Does that change the publication date?' + CHAR(10) + N'Researcher: By only one week. The report should be ready in early July, not late June.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (54, N'B2', N'Urban tree cover can reduce surface temperatures during heatwaves. A city study found the greatest cooling in streets where mature trees shaded continuous sections of pavement; isolated trees produced smaller local effects.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (55, N'B2', N'In a survey of 1,200 commuters, 48% travelled by bus in 2018, compared with 30% by rail and 22% by bicycle. By 2024, rail had risen to 41%, cycling to 25%, while bus use had fallen to 34%.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (56, N'B2', N'The archive digitised its fragile maps first, not because they were the most requested items, but because repeated handling was causing irreversible damage. Popularity rankings were therefore set aside in the initial phase.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (57, N'B2', N'A field experiment compared two restoration sites. Site A received more rain but recovered slowly because compacted soil restricted drainage. Site B received less rain; after its soil was loosened, native plants spread quickly.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (58, N'B2', NULL, N'/audio/vstep/placement-diagnostic.mp3', 30000, 60000, N'Lecturer: The field trip leaves from the main gate at 7:15 on Saturday. Please arrive fifteen minutes early because the bus cannot wait.' + CHAR(10) + N'Student: Do we need to bring lunch?' + CHAR(10) + N'Lecturer: Yes, and wear shoes suitable for walking.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (59, N'B2', NULL, N'/audio/vstep/placement-diagnostic.mp3', 60000, 90000, N'Office staff: The language club meeting has moved from Room 204 to Room 118 because the projector in 204 is being repaired.' + CHAR(10) + N'Student: Is the time still 3 p.m.?' + CHAR(10) + N'Office staff: Yes, the schedule is unchanged.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (60, N'B2', NULL, N'/audio/vstep/placement-diagnostic.mp3', 90000, 120000, N'Announcer: The community clean-up will take place on 18 June, not 16 June as printed on the first notice. Volunteers should register at the library desk by 5 p.m. on Friday.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (61, N'B2', NULL, N'/audio/vstep/placement-diagnostic.mp3', 120000, 150000, N'Student: I cannot attend the computer workshop on Wednesday.' + CHAR(10) + N'Advisor: The Friday group still has spaces. You can transfer online before noon tomorrow; there is no extra fee.' + CHAR(10) + N'Student: I will do that tonight.', N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (62, N'B2', N'A town library introduced a book-delivery service for older residents who cannot travel easily. Volunteers collect requested titles twice a week and leave them at each resident''s home. The service is free, but readers must register by phone.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (63, N'B2', N'The college offers evening classes in basic accounting, workplace communication, and computer skills. Learners may enrol in one course at a time. Applications close on 30 September, and classes begin two weeks later.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (64, N'B2', N'Although the sports centre extended its opening hours, attendance did not rise during the first month. Managers later learned that the last bus left before the new closing time. They are discussing a revised shuttle schedule with the transport office.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (65, N'B2', N'An adult-learning programme originally met every Saturday. After many learners reported shift-work conflicts, the organiser added a Wednesday evening class. The Saturday class remains available for those who prefer it.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (66, N'B1', N'Unit 1 foundational stage for incomplete sentences and completion items.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (67, N'B2', N'The corporate merger announced yesterday between Apex Technologies and Horizon Logistics will create the region''s largest supply chain network. Analysts project a 15% reduction in freight overhead over the upcoming fiscal quarters.', NULL, NULL, NULL, NULL, N'general', NULL, NULL, NULL, NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (68, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/images/toeic/test1-p1-q1.jpg', N'Conference meeting room', NULL, NULL, 0, '2026-11-11T01:44:03.0840572+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (70, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-12T01:46:30.3110559+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (71, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-12T01:46:30.3231864+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (72, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-12T01:46:30.3231864+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (73, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-12T01:46:30.3231864+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (74, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-12T01:46:30.3231864+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (75, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-12T01:46:30.3231864+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (76, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-12T01:46:30.3231864+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (77, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-12T01:46:30.3267986+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (78, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-12T01:46:30.3267986+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Stimuli] ([StimulusId], [CefrLevel], [Passage], [AudioPath], [AudioStartMs], [AudioEndMs], [Transcript], [DomainContext], [PromptCopy], [ImagePath], [ImageAlt], [PrepSeconds], [ResponseSeconds], [DisplayOrder], [CreatedAt]) VALUES (79, N'B1', NULL, NULL, NULL, NULL, NULL, N'general', NULL, N'/img/part1-sample.svg', N'Office workers in a team meeting', NULL, NULL, 0, '2026-11-12T01:46:30.3267986+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Stimuli] OFF;

-- ---- Topics (20 rows)
SET IDENTITY_INSERT [Topics] ON;
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (1, N'listening', N'L-GIST', N'Global gist, purpose, macro-topic');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (2, N'listening', N'L-DET', N'Explicit factual detail and extraction');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (3, N'listening', N'L-INF', N'Pragmatic implication and logical inference');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (4, N'listening', N'L-ATT', N'Speaker stance, attitude, prosodic emotion');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (5, N'listening', N'L-FUNC', N'Communicative function and speech acts');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (6, N'listening', N'L-ORG', N'Discourse structure, signposting, synthesis');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (7, N'reading', N'R-GIST', N'Global thesis, purpose, paragraph main idea');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (8, N'reading', N'R-DET', N'Local factual retrieval and paraphrase matching');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (9, N'reading', N'R-INF', N'Bridging inference and logical deduction');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (10, N'reading', N'R-ATT', N'Authorial stance, rhetorical purpose, tone');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (11, N'reading', N'R-COH', N'Discourse cohesion, reference, text insertion');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (12, N'reading', N'R-GRAM', N'Morphosyntactic structure and lexical precision');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (13, N'speaking', N'S-DESC', N'Informational description and personal framing');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (14, N'speaking', N'S-INT', N'Transactional interaction and option negotiation');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (15, N'speaking', N'S-ARG', N'Sustained monologic argumentation and justification');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (16, N'speaking', N'S-SYN', N'Integrated summary and visual/data interpretation');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (17, N'writing', N'W-CORR', N'Transactional correspondence and register control');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (18, N'writing', N'W-DATA', N'Visual data, process, and informational synthesis');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (19, N'writing', N'W-ARG', N'Thesis-driven persuasive and opinion argumentation');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Topics] ([TopicId], [Modality], [Code], [Name]) VALUES (20, N'writing', N'W-DISC', N'Multi-perspective problem-solution and evaluation');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Topics] OFF;

-- ---- Users (4 rows)
SET IDENTITY_INSERT [Users] ON;
BEGIN TRY
  INSERT INTO [Users] ([UserId], [RoleId], [Email], [PasswordHash], [ActiveCertId], [FullName], [Phone], [IsLocked], [SecurityStamp], [FailedAttempts], [LockoutEnd], [EmailVerifiedAt], [LastLoginAt], [TermsAcceptedAt], [TimeZoneId], [IsDeleted], [CreatedAt]) VALUES (1, 2, N'admin@demo.local', 0x0100000002000186A0000000103C900F405E0EA707A8C20B5D134127259871478D350B03660E6C246E724D8C436A9004C3BA672FE5A2481FAB2C3223E9, NULL, N'Admin User', NULL, 0, N'06DCBA49-B30A-4DB9-979D-BE3805E3B1A4', 0, NULL, '2026-11-11T01:44:00.6628359+07:00', NULL, '2026-11-11T01:44:00.6628359+07:00', N'Asia/Ho_Chi_Minh', 0, '2026-11-11T01:44:00.6628359+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Users] ([UserId], [RoleId], [Email], [PasswordHash], [ActiveCertId], [FullName], [Phone], [IsLocked], [SecurityStamp], [FailedAttempts], [LockoutEnd], [EmailVerifiedAt], [LastLoginAt], [TermsAcceptedAt], [TimeZoneId], [IsDeleted], [CreatedAt]) VALUES (2, 1, N'learner@demo.local', 0x0100000002000186A0000000103C900F405E0EA707A8C20B5D134127259871478D350B03660E6C246E724D8C436A9004C3BA672FE5A2481FAB2C3223E9, 1, N'Demo Learner', N'+84 901 000 001', 0, N'E8EA41FD-32FC-4868-A9BF-B9A57A17D4F8', 0, NULL, '2026-11-11T01:44:00.6628359+07:00', NULL, '2026-11-11T01:44:00.6628359+07:00', N'Asia/Ho_Chi_Minh', 0, '2026-11-11T01:44:00.6653393+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Users] ([UserId], [RoleId], [Email], [PasswordHash], [ActiveCertId], [FullName], [Phone], [IsLocked], [SecurityStamp], [FailedAttempts], [LockoutEnd], [EmailVerifiedAt], [LastLoginAt], [TermsAcceptedAt], [TimeZoneId], [IsDeleted], [CreatedAt]) VALUES (3, 1, N'learner.fresh@demo.local', 0x0100000002000186A0000000103C900F405E0EA707A8C20B5D134127259871478D350B03660E6C246E724D8C436A9004C3BA672FE5A2481FAB2C3223E9, NULL, N'Fresh Student', NULL, 0, N'8B038155-2ACA-4151-B11A-1CE51EAD44B6', 0, NULL, '2026-11-11T01:44:00.6628359+07:00', NULL, '2026-11-11T01:44:00.6628359+07:00', N'Asia/Ho_Chi_Minh', 0, '2026-11-11T01:44:00.6663443+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Users] ([UserId], [RoleId], [Email], [PasswordHash], [ActiveCertId], [FullName], [Phone], [IsLocked], [SecurityStamp], [FailedAttempts], [LockoutEnd], [EmailVerifiedAt], [LastLoginAt], [TermsAcceptedAt], [TimeZoneId], [IsDeleted], [CreatedAt]) VALUES (4, 1, N'mentor.khoa@demo.local', 0x0100000002000186A0000000103C900F405E0EA707A8C20B5D134127259871478D350B03660E6C246E724D8C436A9004C3BA672FE5A2481FAB2C3223E9, NULL, N'Dr. Trần Minh Khoa', N'+84 901 000 002', 0, N'2DABEAE8-1AF3-4421-BDDE-232D9EBD3C47', 0, NULL, '2026-11-11T01:44:00.6628359+07:00', NULL, '2026-11-11T01:44:00.6628359+07:00', N'Asia/Ho_Chi_Minh', 0, '2026-11-11T01:44:00.6678821+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Users] OFF;


-- ---- Exams (17 rows)
SET IDENTITY_INSERT [Exams] ON;
BEGIN TRY
  INSERT INTO [Exams] ([ExamId], [CertId], [Name], [Status], [DurationMinutes], [Purpose], [DeliveryMode], [ExamFamily], [Description], [SourceName], [InternalNote], [QuestionCount], [ContentTier], [EditionNumber], [SupersedesExamId], [IsDeleted], [CreatedAt], [UpdatedAt], [LastWrittenBy]) VALUES (1, 1, N'ETS 2024 — Test 1', N'published', 120, N'mock', N'strict_exam', NULL, N'A full TOEIC Listening & Reading form.', N'ETS 2024', NULL, 200, N'demo', 1, NULL, 0, '2026-11-11T01:44:01.1857920+07:00', NULL, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Exams] ([ExamId], [CertId], [Name], [Status], [DurationMinutes], [Purpose], [DeliveryMode], [ExamFamily], [Description], [SourceName], [InternalNote], [QuestionCount], [ContentTier], [EditionNumber], [SupersedesExamId], [IsDeleted], [CreatedAt], [UpdatedAt], [LastWrittenBy]) VALUES (5, 2, N'Cambridge IELTS 18 — Academic Test 1', N'published', 170, N'mock', N'strict_exam', NULL, N'Academic Listening, Reading, Writing and Speaking form.', N'Cambridge 18', NULL, 40, N'demo', 1, NULL, 0, '2026-11-11T01:44:01.4857774+07:00', NULL, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Exams] ([ExamId], [CertId], [Name], [Status], [DurationMinutes], [Purpose], [DeliveryMode], [ExamFamily], [Description], [SourceName], [InternalNote], [QuestionCount], [ContentTier], [EditionNumber], [SupersedesExamId], [IsDeleted], [CreatedAt], [UpdatedAt], [LastWrittenBy]) VALUES (6, 2, N'Universal Placement Exam', N'published', 60, N'placement', N'strict_exam', NULL, N'Single universal 4-skill diagnostic placement exam (Listening, Reading, Speaking, Writing) supporting 2-skill or 4-skill OOBE scope.', N'Universal Placement', NULL, 12, N'demo', 1, NULL, 0, '2026-11-11T01:44:01.4937789+07:00', NULL, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Exams] OFF;

-- ---- GradingCriteria (28 rows)
SET IDENTITY_INSERT [GradingCriteria] ON;
BEGIN TRY
  INSERT INTO [GradingCriteria] ([CriterionId], [CertId], [SkillId], [Code], [Name], [RubricDescription], [MaxScore], [Weight], [DisplayOrder], [LastWrittenBy]) VALUES (9, 2, 7, N'TR', N'Task Response', N'9: fully addresses all parts of the task, fully developed position. 8: sufficiently addresses all parts, well-developed position. 7: addresses all parts, clear position throughout. 6: addresses all parts though some more fully than others, position relevant. 5: addresses the task only partially, format may be inappropriate. 4: responds to the task only in a minimal way, position unclear. 3: does not adequately address the task, may be unrelated. 2: barely responds to the task. 1: answer is completely unrelated. 0: does not attempt the task.', 9.00, 1.00, 1, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [GradingCriteria] ([CriterionId], [CertId], [SkillId], [Code], [Name], [RubricDescription], [MaxScore], [Weight], [DisplayOrder], [LastWrittenBy]) VALUES (10, 2, 7, N'CC', N'Coherence & Cohesion', N'9: cohesion attracts no attention, skilful paragraphing. 8: sequences information logically, manages cohesion well. 7: logically organises information, clear progression, range of cohesion. 6: arranges information coherently, cohesive devices used but sometimes faulty. 5: some organisation but lack of overall progression, inadequate or mechanical cohesion. 4: information not arranged coherently, cohesion faulty or missing. 3: does not organise ideas logically. 2: little or no use of cohesive features. 1: no discernible organisation. 0: no assessable text.', 9.00, 1.00, 2, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [GradingCriteria] ([CriterionId], [CertId], [SkillId], [Code], [Name], [RubricDescription], [MaxScore], [Weight], [DisplayOrder], [LastWrittenBy]) VALUES (11, 2, 7, N'LR', N'Lexical Resource', N'9: full flexibility and precise use, rare minor slips. 8: wide resource used fluently and flexibly. 7: sufficient range to allow some flexibility and precision. 6: adequate range, some inappropriate choices that do not impede communication. 5: limited range, noticeable errors in word choice and spelling. 4: basic vocabulary, frequent errors that may distort the message. 3: inadequate range, errors may severely distort meaning. 2: very limited range. 1: only isolated words. 0: no assessable language.', 9.00, 1.00, 3, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [GradingCriteria] ([CriterionId], [CertId], [SkillId], [Code], [Name], [RubricDescription], [MaxScore], [Weight], [DisplayOrder], [LastWrittenBy]) VALUES (12, 2, 7, N'GRA', N'Grammatical Range & Accuracy', N'9: wide range of structures, rare minor errors. 8: wide range, majority of sentences error-free. 7: variety of complex structures, frequent error-free sentences, few errors. 6: mix of simple and complex forms, some errors that rarely impede communication. 5: limited range, complex sentences noticeably faulty. 4: very limited range, frequent grammatical errors that can seriously impede communication. 3: range of structures very limited, errors predominate. 2: little control of sentence forms. 1: cannot produce a sentence. 0: no assessable language.', 9.00, 1.00, 4, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [GradingCriteria] ([CriterionId], [CertId], [SkillId], [Code], [Name], [RubricDescription], [MaxScore], [Weight], [DisplayOrder], [LastWrittenBy]) VALUES (13, 2, 8, N'FC', N'Fluency and Coherence', NULL, 9.00, 1.00, 1, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [GradingCriteria] ([CriterionId], [CertId], [SkillId], [Code], [Name], [RubricDescription], [MaxScore], [Weight], [DisplayOrder], [LastWrittenBy]) VALUES (14, 2, 8, N'LR', N'Lexical Resource', NULL, 9.00, 1.00, 2, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [GradingCriteria] ([CriterionId], [CertId], [SkillId], [Code], [Name], [RubricDescription], [MaxScore], [Weight], [DisplayOrder], [LastWrittenBy]) VALUES (15, 2, 8, N'GRA', N'Grammatical Range and Accuracy', NULL, 9.00, 1.00, 3, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [GradingCriteria] ([CriterionId], [CertId], [SkillId], [Code], [Name], [RubricDescription], [MaxScore], [Weight], [DisplayOrder], [LastWrittenBy]) VALUES (16, 2, 8, N'PRON', N'Pronunciation', NULL, 9.00, 1.00, 4, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [GradingCriteria] OFF;

-- ---- Items (140 rows)
SET IDENTITY_INSERT [Items] ON;
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (1, 1, N'listening', 2, N'A2', N'short_exchange', N'mcq', 0, N'demo', N'active', N'seed-vstep-sample-1', '2026-11-11T01:44:02.8064239+07:00', NULL, N'A2,B1');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (2, 2, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', N'seed-vstep-sample-1', '2026-11-11T01:44:02.8156557+07:00', NULL, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (3, 3, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0905658+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (4, 4, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0925664+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (5, 5, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0935658+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (6, 6, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0945657+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (7, 20, N'reading', 8, N'B2', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0945657+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (8, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0955657+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (9, 20, N'reading', 8, N'B2', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0960710+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (10, 7, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0970728+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (11, 8, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0970728+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (12, 9, N'listening', 2, N'A2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0985774+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (13, 9, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0985774+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (14, 11, N'listening', 2, N'A2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.0995808+07:00', 1, N'A2,B1');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (15, 11, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1005852+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (16, 11, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1005852+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (17, 11, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1015853+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (18, 15, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1025851+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (19, 15, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1035852+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (20, 17, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1035852+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (21, 17, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1045851+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (22, 20, N'reading', 8, N'A2', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1055851+07:00', 1, N'A2,B1');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (23, 20, N'reading', 8, N'A2', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1060901+07:00', 1, N'A2,B1');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (24, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1070918+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (25, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1070918+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (26, 20, N'reading', 8, N'B2', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1080931+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (27, 20, N'reading', 8, N'B2', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1085966+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (28, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1096028+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (29, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1106079+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (30, 20, N'reading', 8, N'B2', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1106079+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (31, 20, N'reading', 8, N'B2', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1116072+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (32, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1126103+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (33, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1153611+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (34, 20, N'reading', 8, N'B2', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1168502+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (35, 20, N'reading', 8, N'B2', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1178519+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (36, 19, N'speaking', 13, N'B1', N'descriptive_monologue', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1178519+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (37, 70, N'speaking', 13, N'B1', N'descriptive_monologue', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1188603+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (38, 71, N'speaking', 13, N'B2', N'descriptive_monologue', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1188603+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (39, 72, N'writing', 19, N'B1', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1198611+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (40, 22, N'writing', 19, N'C1', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1208609+07:00', 1, N'7.5,8.0,8.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (41, 23, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1208609+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (42, 24, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1218611+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (43, 25, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1228609+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (44, 26, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1238641+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (45, 73, N'speaking', 13, N'B1', N'descriptive_monologue', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1253700+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (46, 27, N'speaking', 13, N'C1', N'descriptive_monologue', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1253700+07:00', 1, N'7.5,8.0,8.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (47, 28, N'writing', 19, N'B2', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1263764+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (48, 29, N'writing', 19, N'C1', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1273762+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (49, 30, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1273762+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (50, 31, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1283811+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (51, 74, N'speaking', 13, N'B1', N'descriptive_monologue', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1293815+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (52, 32, N'speaking', 13, N'B2', N'descriptive_monologue', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1293815+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (53, 33, N'writing', 19, N'B2', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1303815+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (54, 34, N'writing', 19, N'C1', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1313813+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (55, 35, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1313813+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (56, 36, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1323818+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (57, 11, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1333816+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (58, 11, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1343816+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (59, 11, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1343816+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (60, 11, N'listening', 2, N'B1', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1358864+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (61, 41, N'listening', 2, N'B1', N'extended_dialogue', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1358864+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (62, 41, N'listening', 2, N'B1', N'extended_dialogue', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1368924+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (63, 41, N'listening', 2, N'B1', N'extended_dialogue', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1378978+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (64, 42, N'listening', 2, N'B1', N'extended_dialogue', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1388980+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (65, 42, N'listening', 2, N'B1', N'extended_dialogue', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1388980+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (66, 42, N'listening', 2, N'B1', N'extended_dialogue', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1398978+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (67, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1408973+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (68, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1418972+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (69, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1428977+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (70, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1428977+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (71, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1438977+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (72, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1448973+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (73, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1454026+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (74, 20, N'reading', 8, N'B1', N'sentence_cloze', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1464088+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (75, 43, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1469138+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (76, 43, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1479187+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (77, 43, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1479187+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (78, 43, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1494661+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (79, 44, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1504726+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (80, 44, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1514729+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (81, 44, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1514729+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (82, 45, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1524730+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (83, 45, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1534728+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (84, 45, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1544730+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (85, 75, N'writing', 19, N'B2', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1559794+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (86, 76, N'writing', 19, N'C1', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1564819+07:00', 1, N'7.5,8.0,8.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (87, 46, N'writing', 19, N'B2', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1575543+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (88, 47, N'writing', 19, N'C1', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1575543+07:00', 1, N'7.5,8.0,8.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (89, 48, N'writing', 19, N'B2', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1589292+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (90, 49, N'writing', 19, N'C1', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1589292+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (91, 50, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1602111+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (92, 51, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1612089+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (93, 52, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1612089+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (94, 53, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1622086+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (95, 54, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1632085+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (96, 55, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1642086+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (97, 56, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1642086+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (98, 57, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1657118+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (99, 58, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1667175+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (100, 59, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1667175+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (101, 60, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1677166+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (102, 61, N'listening', 2, N'B2', N'short_exchange', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1687165+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (103, 62, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1697180+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (104, 63, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1707167+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (105, 64, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1707167+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (106, 65, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1717165+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (107, 66, N'reading', 8, N'B1', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1727164+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (108, 66, N'reading', 8, N'B1', N'passage_comprehension', N'mcq_3', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1737164+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (109, 66, N'reading', 8, N'B2', N'passage_comprehension', N'multi_select', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1747166+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (110, 66, N'reading', 8, N'B1', N'passage_comprehension', N'inline_completion', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1747166+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (111, 66, N'reading', 8, N'B2', N'passage_comprehension', N'dropdown', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1762225+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (112, 66, N'reading', 8, N'B2', N'passage_comprehension', N'drag_drop', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1762225+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (113, 67, N'reading', 8, N'B2', N'passage_comprehension', N'mcq', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1772244+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (114, 77, N'speaking', 13, N'B1', N'descriptive_monologue', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1782242+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (115, 78, N'writing', 19, N'B1', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1792242+07:00', 1, N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (116, 79, N'writing', 19, N'B2', N'transactional_correspondence', N'free_response', 0, N'demo', N'active', NULL, '2026-11-11T01:44:03.1792242+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (118, NULL, N'listening', 2, N'A1', N'short_exchange', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9218259+07:00', 1, N'A2,B1');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (119, NULL, N'listening', 2, N'A1', N'short_exchange', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9387647+07:00', 1, N'A2,B1');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (120, NULL, N'listening', 2, N'A2', N'short_exchange', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9397649+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (121, NULL, N'listening', 2, N'A2', N'short_exchange', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9397649+07:00', 1, N'A2,B1');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (122, NULL, N'listening', 3, N'B1', N'extended_dialogue', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9407647+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (123, NULL, N'listening', 3, N'B1', N'extended_dialogue', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9417644+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (124, NULL, N'listening', 3, N'B2', N'extended_dialogue', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9422700+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (125, NULL, N'listening', 3, N'B2', N'extended_dialogue', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9422700+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (126, NULL, N'listening', 6, N'C1', N'extended_dialogue', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9422700+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (127, NULL, N'listening', 6, N'C1', N'extended_dialogue', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9432717+07:00', 1, N'7.5,8.0,8.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (128, NULL, N'listening', 6, N'C2', N'extended_dialogue', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9432717+07:00', 1, N'7.5,8.0,8.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (129, NULL, N'listening', 6, N'C2', N'extended_dialogue', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9442715+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (130, NULL, N'reading', 8, N'A1', N'sentence_cloze', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9442715+07:00', 1, N'A2,B1');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (131, NULL, N'reading', 8, N'A1', N'sentence_cloze', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9452714+07:00', 1, N'A2,B1');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (132, NULL, N'reading', 8, N'A2', N'sentence_cloze', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9452714+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (133, NULL, N'reading', 8, N'A2', N'sentence_cloze', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9452714+07:00', 1, N'A2,B1');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (134, NULL, N'reading', 9, N'B1', N'passage_comprehension', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9462717+07:00', 1, N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (135, NULL, N'reading', 9, N'B1', N'passage_comprehension', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9462717+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (136, NULL, N'reading', 9, N'B2', N'passage_comprehension', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9472713+07:00', 1, N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (137, NULL, N'reading', 9, N'B2', N'passage_comprehension', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9477761+07:00', 1, N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (138, NULL, N'reading', 7, N'C1', N'passage_comprehension', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9477761+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (139, NULL, N'reading', 7, N'C1', N'passage_comprehension', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9487794+07:00', 1, N'7.5,8.0,8.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (140, NULL, N'reading', 7, N'C2', N'passage_comprehension', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9492906+07:00', 1, N'7.5,8.0,8.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Items] ([ItemId], [StimulusId], [Modality], [TopicId], [CefrLevel], [ItemType], [RenderFormat], [PlacementEligible], [ContentTier], [LifecycleStatus], [BatchTag], [CreatedAt], [LastWrittenBy], [TargetBands]) VALUES (141, NULL, N'reading', 7, N'C2', N'passage_comprehension', N'mcq', 1, N'demo', N'active', NULL, '2026-11-11T02:12:01.9492906+07:00', 1, N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Items] OFF;

-- ---- ItemVersions (140 rows)
SET IDENTITY_INSERT [ItemVersions] ON;
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (1, 1, 1, N'On what date will the festival open?', N'June 1st', N'June 8th', N'June 15th', N'July 8th', N'B', N'The speaker explicitly states June 8th.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:02.8106557+07:00', NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (2, 2, 1, N'What is a primary benefit of community-based tourism described in the text?', N'Rapid industrialization of rural areas', N'Sustainable income for local families', N'Construction of new international airports', N'Government subsidies for tour operators', N'B', N'The passage notes it provides sustainable income for local families while sharing cultural traditions.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:02.8161623+07:00', NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (3, 3, 1, N'Look at the photograph and choose the statement that best describes what you see in the image.', N'They are leaving the meeting room.', N'A laptop is being repaired on the desk.', N'A woman is writing on a whiteboard.', N'Some chairs are stacked against the wall.', N'C', N'Option C correctly describes the action: the woman is writing notes on the whiteboard.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'Focus on what the person in the centre is holding and doing.', '2026-11-11T01:44:03.0925664+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (4, 4, 1, N'Where did you leave the project folders?', N'Yes, I folded them neatly.', N'On the conference room table.', N'No later than five o''clock.', NULL, N'B', N'Option B directly answers where the folders are placed.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'Listen for a location word that answers "Where".', '2026-11-11T01:44:03.0935658+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (5, 5, 1, N'What deadline does the catering team have for the banquet?', N'By the end of today', N'By tomorrow noon', N'On Friday morning', N'Next Monday afternoon', N'B', N'The man says they need the final head count by tomorrow noon.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'Pay attention to what the man says about when they need the count.', '2026-11-11T01:44:03.0935658+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (6, 6, 1, N'What is the reason for the flight delay?', N'Adverse weather conditions', N'Routine aircraft inspection', N'A gate reassignment', N'Crew scheduling changes', N'B', N'The speaker explicitly announces that the delay is due to routine aircraft inspection.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'Listen for the phrase following "Due to".', '2026-11-11T01:44:03.0945657+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (7, 7, 1, N'The regional manager ------- the quarterly figures before the board meeting.', N'review', N'reviewing', N'will review', N'to review', N'C', N'The sentence lacks a main verb. "will review" expresses future scheduled action.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'Look at the time marker and sentence structure.', '2026-11-11T01:44:03.0955657+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (8, 8, 1, N'All client inquiries must be resolved ------- forty-eight hours of submission.', N'in', N'during', N'along', N'within', N'D', N'"within" + time duration indicates action must take place before that span elapses.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'Which preposition indicates within a time span?', '2026-11-11T01:44:03.0960710+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (9, 9, 1, N'The design studio provides ------- solutions for modern office spaces.', N'innovation', N'innovative', N'innovatively', N'innovate', N'B', N'"innovative" is the adjective modifying "solutions".', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'The blank modifies the plural noun "solutions".', '2026-11-11T01:44:03.0960710+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (10, 10, 1, N'Choose the word or phrase that best completes the sentence in the customer letter.', N'have received', N'will receive', N'are receiving', N'to receive', N'A', N'The present perfect "have received" describes an action that occurred recently with present impact.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'The action happened recently with present relevance.', '2026-11-11T01:44:03.0970728+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (11, 11, 1, N'What is the primary purpose of the memorandum?', N'To announce staff promotions', N'To notify employees of maintenance work', N'To schedule an annual fire drill', N'To request feedback on office amenities', N'B', N'The memo announces temporary elevator upgrades and directs staff to an alternate service elevator.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'Look at the subject line and the main announcement.', '2026-11-11T01:44:03.0980725+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (12, 12, 1, N'Look at the photograph and choose the statement that best describes what you see.', N'A woman is writing on a whiteboard.', N'Some chairs are stacked against the wall.', N'A laptop is being repaired on the desk.', N'The room has been emptied for cleaning.', N'A', N'Only option A describes what the photograph shows.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.0985774+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (13, 13, 1, N'Look at the photograph and choose the statement that best describes what you see.', N'Passengers are waiting on a platform.', N'A train is leaving the station.', N'Two people are buying tickets.', N'Luggage is lined up beside the counter.', N'D', N'The photograph shows luggage beside the counter; the other options describe actions that are not visible.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.0995808+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (14, 14, 1, N'Where did you leave the project folders?', N'Yes, I folded them neatly.', N'On the conference room table.', N'No later than five o''clock.', NULL, N'B', N'The question asks for a place, and only B names one.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.0995808+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (15, 15, 1, N'Why was the shipment delayed?', N'In the loading bay.', N'It arrived on Tuesday.', N'Because the courier misread the postcode.', NULL, N'C', N'The question asks for a reason; only C gives one.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1005852+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (16, 16, 1, N'Should we book the larger room for the workshop?', N'That would be a good idea.', N'About twenty people.', N'It finished early.', NULL, N'A', N'The question asks for a recommendation, and A answers it.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1015853+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (17, 17, 1, N'How long has the client been with the firm?', N'At the downtown office.', N'Since 2019.', N'Yes, she renewed last month.', NULL, N'B', N'The question asks for a duration, and only B provides one.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1015853+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (18, 18, 1, N'Woman: Did the catering team confirm the order for Friday''s banquet? Man: Yes, but they need our final head count by tomorrow noon. Woman: I will email the department heads right away.' + CHAR(10) + N'' + CHAR(10) + N'What deadline does the catering team have?', N'By the end of today.', N'By tomorrow noon.', N'On Friday morning.', NULL, N'B', N'The man states the head count is needed by tomorrow noon.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1025851+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (19, 19, 1, N'Man: I''m afraid the 3 p.m. train has been cancelled. Woman: Then I''ll take the 3:40 instead. Can I change my ticket at the counter? Man: Yes, at no extra charge.' + CHAR(10) + N'' + CHAR(10) + N'What will the woman do?', N'Ask for a refund.', N'Wait for the 3 p.m. train.', N'Change her ticket at the counter.', NULL, N'C', N'She asks whether she can change the ticket at the counter and the man confirms it is free.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1035852+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (20, 20, 1, N'Attention, passengers. The north gallery is closed for lighting work this morning. The sculpture collection has moved upstairs, beside the reading room. The café is unaffected.' + CHAR(10) + N'' + CHAR(10) + N'Where can visitors find the sculpture collection?', N'Upstairs beside the reading room.', N'In the closed north gallery.', N'Behind the café counter.', NULL, N'A', N'The announcement says the collection has moved upstairs beside the reading room.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1045851+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (21, 21, 1, N'A memorandum to all staff: from next month, expense claims must be submitted through the finance portal rather than by email. Claims received by the tenth of the month will be processed in that month''s payroll.' + CHAR(10) + N'' + CHAR(10) + N'How should staff submit expense claims from next month?', N'By email to their manager.', N'Through the finance portal.', N'In person at payroll.', NULL, N'B', N'The memorandum replaces email with the finance portal from next month.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1045851+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (22, 22, 1, N'Customer satisfaction surveys must be submitted ------- Friday afternoon to be included in the quarterly review.', N'before', N'during', N'between', N'among', N'A', N'Only ''before'' marks a deadline relative to a point in time.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1055851+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (23, 23, 1, N'All employees are required to wear their identification badges ------- visiting the research floor.', N'despite', N'although', N'while', N'unless', N'C', N'The badge is worn during the visit, so ''while'' fits.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1060901+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (24, 24, 1, N'The marketing team will ------- the campaign results at Thursday''s meeting.', N'presentation', N'present', N'presented', N'presenting', N'B', N'After the modal ''will'', the base form of the verb is required.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1070918+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (25, 25, 1, N'Ms. Alvarez has asked that the revised budget ------- to her by the end of the week.', N'be sent', N'sends', N'sending', N'will send', N'A', N'''Ask that'' takes the subjunctive, so the base form ''be sent'' is correct.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1080931+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (26, 26, 1, N'The delay was caused not by the supplier ------- by the customs inspection.', N'and', N'or', N'but', N'so', N'C', N'''Not by ... but by ...'' is the parallel construction the sentence needs.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1085966+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (27, 27, 1, N'------- the branch had reported record sales, the regional office remained cautious about the next quarter.', N'Because', N'Since', N'Unless', N'Although', N'D', N'The two clauses contrast, which is what ''Although'' signals.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1096028+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (28, 28, 1, N'Thank you for contacting Apex Support. We have received your inquiry regarding account renewal. A representative will review your request and ------- within 24 business hours.', N'respond', N'response', N'responded', N'responding', N'A', N'The modal ''will'' requires the base verb.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1096028+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (29, 29, 1, N'Please note that the service centre closes at 5 p.m. on weekdays, ------- it remains open until 7 p.m. on Saturdays.', N'so', N'but', N'unless', N'because', N'B', N'The two clauses contrast weekdays with Saturdays.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1106079+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (30, 30, 1, N'Customers who ------- their invoices before the due date are eligible for a two per cent discount.', N'paying', N'are paid', N'pay', N'have paid for', N'C', N'The relative clause needs a finite verb, ''pay'', agreeing with ''Customers''.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1116072+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (31, 31, 1, N'The new policy applies to all contractors ------- their length of service.', N'instead of', N'in place of', N'except for', N'regardless of', N'D', N'The policy covers everyone, so ''regardless of'' is the only fit.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1116072+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (32, 32, 1, N'Notice: The staff car park will be resurfaced from Monday to Wednesday. Employees are asked to use the visitor car park on those days and to display a temporary permit.' + CHAR(10) + N'' + CHAR(10) + N'What are employees asked to do?', N'Resurface the car park themselves.', N'Use the visitor car park with a permit.', N'Work from home for three days.', NULL, N'B', N'The notice redirects staff to the visitor car park and requires a temporary permit.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1138486+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (33, 33, 1, N'Email: I have reviewed the draft proposal and have two comments. First, the timeline should include the client review week. Second, the budget table needs the contingency line. Otherwise the structure is strong.' + CHAR(10) + N'' + CHAR(10) + N'What does the writer want added to the timeline?', N'The client review week.', N'A contingency line.', N'A stronger structure.', NULL, N'A', N'The contingency line belongs to the budget table; the client review week is the timeline comment.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1153611+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (34, 34, 1, N'An article reports that a city''s bicycle-sharing scheme grew from 400 to 5,000 bicycles in four years, and that journeys rose from 900 to 41,000 a day. The operator attributes the growth to docking stations near rail terminals.' + CHAR(10) + N'' + CHAR(10) + N'What does the operator say caused the growth?', N'A fall in the price of bicycles.', N'A reduction in rail services.', N'Docking stations near rail terminals.', NULL, N'C', N'The article attributes growth to the placement of docking stations near rail terminals.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1168502+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (35, 35, 1, N'Memo: Because the audit will occupy the fifth floor all week, the finance team will work from the second-floor meeting rooms. Printers on the fifth floor will be unavailable; please use the shared printer on the second floor.' + CHAR(10) + N'' + CHAR(10) + N'Why is the finance team moving?', N'Their printers have been removed.', N'The second floor is being renovated.', N'They need to meet clients.', N'The audit will occupy their floor.', N'D', N'The audit occupies the fifth floor, which is why the team moves.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1178519+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (36, 36, 1, N'Read the announcement aloud as clearly and naturally as possible.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 45, 45, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1178519+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (37, 37, 1, N'Look at the picture and describe it in as much detail as you can.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 45, 30, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1188603+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (38, 38, 1, N'How often do you go shopping for clothes, and what is your favorite clothing store?', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 3, 15, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1198611+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (39, 39, 1, N'Write ONE sentence based on the picture using two given words: "discuss" and "proposal".', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 8, 35, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1198611+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (40, 40, 1, N'Write an opinion essay of at least 250 words explaining your viewpoint with specific reasons and examples.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 250, 450, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1208609+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (41, 41, 1, N'At what time does the first library induction tour depart?', N'9:00 AM', N'9:30 AM', N'10:00 AM', N'10:30 AM', N'B', N'The guide states that the tours start at 9:30 AM.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'Listen for the starting time mentioned by the guide.', '2026-11-11T01:44:03.1218611+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (42, 42, 1, N'According to the passage, by how much can urban surface temperatures be reduced through green canopies?', N'Up to 1.5 °C', N'Up to 2.8 °C', N'Up to 3.5 °C', N'Up to 4.2 °C', N'B', N'The text explicitly mentions a reduction of up to 2.8 degrees Celsius.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, N'Scan the passage for specific numbers and degrees Celsius.', '2026-11-11T01:44:03.1218611+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (43, 43, 1, N'What time does the student housing office close on Fridays?', N'12:00 PM', N'1:00 PM', N'4:00 PM', N'5:00 PM', N'B', N'The officer says: "On Fridays we close at 1 PM".', N'12:00 PM is not the Friday closing time; the recording specifies 1:00 PM.', N'Correct: the housing office closes at 1:00 PM on Fridays.', N'4:00 PM applies Monday through Thursday, not Friday.', N'5:00 PM is never given as a closing time.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1238641+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (44, 44, 1, N'What energy source accounted for the majority of new capacity?', N'Wind power', N'Solar photovoltaics', N'Hydroelectricity', N'Geothermal energy', N'B', N'The passage states solar photovoltaics accounted for three-quarters of installations.', N'Wind is not the source identified as the majority of new capacity.', N'Correct: solar photovoltaics accounted for three-quarters of new installations.', N'Hydroelectricity is not named in the passage.', N'Geothermal energy is not named in the passage.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1238641+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (45, 45, 1, N'Part 1: Do you work or are you a student? What do you enjoy most about your studies or job?', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 60, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1253700+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (46, 46, 1, N'Part 2: Speak for 1 to 2 minutes on the cue card topic. You have 1 minute to prepare.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 60, 120, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1263764+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (47, 47, 1, N'Write at least 150 words summarizing the main features and comparisons in the chart.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 150, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1263764+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (48, 48, 1, N'Write an essay of at least 250 words discussing your opinion with supporting arguments.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 250, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1273762+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (49, 49, 1, N'How should students submit their research proposals?', N'Printed copy to the office', N'Through the faculty portal', N'By email to the teaching assistant', N'During Monday class', N'B', N'The lecturer specifies "through the faculty portal".', N'The lecturer asks students to submit through the faculty portal, not on paper.', N'Correct: students should submit their proposals through the faculty portal.', N'Emailing the teaching assistant is not the stated submission route.', N'The deadline is Friday at 5 PM, not during Monday class.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1283811+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (50, 50, 1, N'What role does artificial intelligence play in medical imaging according to the passage?', N'Replacing hospital doctors completely', N'Providing diagnostic assistance', N'Manufacturing hospital equipment', N'Managing billing departments', N'B', N'The passage states AI provides diagnostic assistance for radiology scans.', N'The passage describes support for radiology, not replacing doctors.', N'Correct: AI provides diagnostic assistance for radiology scans.', N'The passage discusses interpreting scans, not manufacturing equipment.', N'Billing is not mentioned as an AI function.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1283811+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (51, 51, 1, N'Part 1: Social Interaction: Talk about your favorite leisure activity. Why do you enjoy it?', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 60, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1293815+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (52, 52, 1, N'Part 2: Topic Development: Present your opinion in 3 minutes based on the prompt.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 60, 180, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1303815+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (53, 53, 1, N'Write an email responding to your friend (at least 120 words).', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 120, 250, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1303815+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (54, 54, 1, N'Write an essay of at least 250 words discussing both sides and giving your opinion with reasons.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 250, 450, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1313813+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (55, 55, 1, N'Look at the picture marked Number 1 in your test book and select the statement that best describes what you see.', N'(A) They are boarding a commuter train.', N'(B) They are examining a blueprint on the desk.', N'(C) They are hanging a picture on the wall.', N'(D) The projector screen is being rolled up.', N'B', N'Hai nhân vật trong ảnh đang cúi nhìn vào bản thiết kế trải trên bàn làm việc (examining a blueprint on the desk).', N'Bẫy đồng âm (Homophone trap): Từ "boarding" dễ gây nhầm lẫn âm thanh với "board" (bảng/bàn họp) trong phòng.', N'Đáp án đúng: Mô tả chính xác hành động của hai người đang xem xét bản thiết kế trên mặt bàn.', N'Bẫy hành động sai (Wrong action trap): Có bức tranh/biểu đồ trên tường nhưng đã được treo sẵn, không có ai đang treo.', N'Bẫy bị động hiện tại tiếp diễn (being + V-ed): Màn chiếu đang ở trạng thái tĩnh, không có tác động cuộn lên.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1323818+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (56, 56, 1, N'Look at the picture marked Number 2 in your test book and select the statement that best describes what you see.', N'(A) Some chairs are stacked in the corner.', N'(B) A technician is repairing the light fixture.', N'(C) Documents are being distributed to attendees.', N'(D) Monitor screens are arranged in a row on the table.', N'D', N'Tranh tả vật trong phòng họp trống: các màn hình máy tính được xếp thành hàng trên bàn (arranged in a row).', N'Bẫy trạng thái sai: Ghế được xếp quanh bàn làm việc chứ không chồng lên nhau (stacked) ở góc phòng.', N'Bẫy suy diễn chủ thể không tồn tại: Trong hình hoàn toàn không có kỹ thuật viên nào.', N'Bẫy "being + V3/ed" cho tranh tĩnh: Không có người tham dự nên không thể có hành động đang phát tài liệu.', N'Đáp án đúng: Diễn tả chính xác trạng thái tĩnh của các màn hình máy tính xếp dọc theo bàn.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1333816+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (57, 57, 1, N'When is the quarterly budget proposal due? — Listen and choose the best response (A, B, or C).', N'(A) Yes, the proposal was approved.', N'(B) Check the schedule Ms. Lin e-mailed this morning.', N'(C) About twenty thousand dollars.', NULL, N'B', N'Câu hỏi "When" nhưng được trả lời gián tiếp bằng cách đẩy trách nhiệm kiểm tra sang lịch trình mà giám đốc tài chính đã gửi.', N'Bẫy Yes/No cho câu hỏi Wh- (When) kết hợp bẫy lặp từ "proposal" (Same-word trap).', N'Đáp án đúng: Phản hồi gián tiếp (Indirect response) chuẩn ETS — chỉ dẫn người hỏi nơi tra cứu thời hạn.', N'Bẫy liên tưởng từ vựng (Associative trap): Nghe thấy "budget" nên đưa ra con số tiền tệ, nhưng câu hỏi hỏi về thời gian "When".', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1333816+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (58, 58, 1, N'Who is leading the new client orientation session tomorrow? — Listen and choose the best response (A, B, or C).', N'(A) In the main conference room on the third floor.', N'(B) Mr. Takahashi volunteered to take it over.', N'(C) It was a very informative session.', NULL, N'B', N'Câu hỏi "Who" yêu cầu xác định nhân sự hoặc sự thay đổi phân công công việc.', N'Bẫy sai từ để hỏi: Trả lời cho câu hỏi "Where" thay vì "Who".', N'Đáp án đúng: Nêu tên người phụ trách buổi định hướng khách hàng mới.', N'Bẫy sai thì (Wrong tense trap) và lặp từ "session": Sự kiện diễn ra vào ngày mai (tomorrow) nhưng đáp án lại dùng thì quá khứ "was".', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1343816+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (59, 59, 1, N'Why don''t we postpone the product launch until the packaging is finalized? — Listen and choose the best response (A, B, or C).', N'(A) Because the factory is located in Osaka.', N'(B) A light lunch at the cafeteria.', N'(C) That makes sense, given the printing delays.', NULL, N'C', N'"Why don''t we..." là câu đề xuất (suggestion), không phải câu hỏi nguyên nhân "Why" thông thường.', N'Bẫy "Because" kinh điển cho câu đề xuất "Why don''t we...": Nội dung không ăn nhập với đề xuất lùi ngày ra mắt.', N'Bẫy phát âm gần giống (Near-homophone trap): Từ "lunch" (bữa trưa) phát âm gần giống với "launch" (ra mắt sản phẩm).', N'Đáp án đúng: Đồng tình với lời đề xuất và bổ sung lý do thực tế về việc chậm trễ in ấn bao bì.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1353815+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (60, 60, 1, N'You''ve already updated the inventory database, haven''t you? — Listen and choose the best response (A, B, or C).', N'(A) The server was down all afternoon, so I couldn''t.', N'(B) In the warehouse next to the loading dock.', N'(C) Yes, the software update is free of charge.', NULL, N'A', N'Câu hỏi đuôi (Tag question) kiểm tra trạng thái hoàn thành công việc, thường được trả lời bằng lý do chưa thể thực hiện.', N'Đáp án đúng: Phản hồi gián tiếp giải thích nguyên nhân chưa thể cập nhật cơ sở dữ liệu.', N'Bẫy liên tưởng từ vựng: Nghe thấy "inventory" (hàng tồn kho) nên gán địa điểm nhà kho (warehouse).', N'Bẫy lặp từ "update": Đánh tráo khái niệm giữa cập nhật dữ liệu kho và bản cập nhật phần mềm miễn phí.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1358864+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (61, 61, 1, N'What problem does the man mention?', N'Some office furniture was damaged in transit.', N'A delivery of office chairs has been delayed.', N'A branch office failed a safety inspection.', N'An invoice contains an incorrect standby fee.', N'B', N'Người đàn ông thông báo lô hàng ghế văn phòng bị kẹt ở Denver do thời tiết xấu dẫn đến giao hàng trễ.', N'Bẫy suy diễn quá đà: Lô hàng bị kẹt do thời tiết chứ không hề bị hư hỏng.', N'Đáp án đúng: Paraphrase từ "shipment of ergonomic office chairs... stuck in Denver... forty-eight-hour delay".', N'Bẫy thông tin nhiễu không có trong bài nghe.', N'Bẫy ghép từ (Word-matching trap): Có nhắc tới "standby fee" ở cuối bài nhưng đó là khoản phí người phụ nữ muốn tránh, không phải lỗi hóa đơn.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1368924+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (62, 62, 1, N'When is the shipment now expected to arrive?', N'On Thursday morning', N'On Thursday afternoon', N'On Friday afternoon', N'On Saturday morning', N'C', N'Ban đầu dự kiến lắp đặt sáng thứ Năm, nhưng bị trễ 48 giờ nên chiều thứ Sáu hàng mới tới.', N'Bẫy mốc thời gian cũ (Original schedule trap): Thứ Năm là lịch lắp đặt ban đầu của đội kỹ thuật.', N'Bẫy trộn lẫn thông tin giữa "Thursday morning" và "Friday afternoon".', N'Đáp án đúng: Người đàn ông nói rõ "the chairs won''t get there until Friday afternoon".', N'Bẫy mốc thời gian của hành động khác: Thứ Bảy là ngày dời lịch cho đội kỹ thuật viên lắp đặt.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1368924+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (63, 63, 1, N'What will the woman most likely do next?', N'Order chairs from a local supplier in Seattle', N'Travel to Denver to inspect the merchandise', N'Reschedule an appointment with a service provider', N'Approve an overtime payment for the drivers', N'C', N'Người phụ nữ quyết định liên hệ ngay với bên lắp đặt (setup agency) để đổi lịch sang thứ Bảy.', N'Bẫy suy diễn không có căn cứ: Hai người chỉ chờ hàng tới chứ không hủy đơn để mua nhà cung cấp khác.', N'Bẫy địa danh: Denver chỉ là nơi xe tải bị kẹt lại do thời tiết.', N'Đáp án đúng: Paraphrase từ "contact the setup agency right away to reschedule their technicians".', N'Bẫy suy diễn từ ngữ cảnh vận tải và chi phí.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1378978+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (64, 64, 1, N'Why is the keynote address being moved to a different location?', N'More people registered than originally anticipated.', N'There is a technical problem with the room''s equipment.', N'The keynote speaker requested a smaller stage.', N'Renovation work is taking place on the mezzanine level.', N'B', N'Bài phát biểu chính phải chuyển phòng vì sự cố thiết bị nghe nhìn (audio-visual malfunction) tại Ballroom A.', N'Bẫy đảo ngược nguyên nhân: Bài nói có nhắc tới "smaller seating capacity" ở phòng mới, dễ làm thí sinh tưởng nhầm là đổi phòng vì quá đông người.', N'Đáp án đúng: Paraphrase chuẩn xác cụm "unexpected audio-visual malfunction in Ballroom A".', N'Bẫy thông tin sai lệch: Diễn giả Dr. Elena Rostova không hề yêu cầu đổi sân khấu.', N'Bẫy ghép từ: "mezzanine level" là tầng của hội trường mới (Sapphire Auditorium), không phải nơi đang sửa chữa.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1388980+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (65, 65, 1, N'What is available in Room 204?', N'Complimentary refreshments', N'Printed copies of the presentation slides', N'A live broadcast of the speech', N'Technical support for digital badges', N'C', N'Phòng 204 được bố trí làm phòng dự phòng (overflow seating) có truyền hình trực tiếp (live video feed).', N'Bẫy kịch bản hội thảo quen thuộc nhưng không được đề cập trong bài.', N'Bẫy suy diễn không có trong transcript.', N'Đáp án đúng: "A live broadcast" tương đương với "a live video feed" trong bài thông báo.', N'Bẫy trộn từ: "digital badge" xuất hiện ở câu sau nhưng dùng để quét tại cửa hội trường.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1398978+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (66, 66, 1, N'How can attendees earn professional credits?', N'By completing an online feedback survey', N'By signing a registration sheet at the front desk', N'By scanning their badges at the auditorium entrance', N'By submitting a summary to Dr. Rostova', N'C', N'Người tham dự cần quét thẻ hội nghị điện tử (scan digital conference badge) tại lối vào hội trường.', N'Bẫy thông lệ thực tế nhưng không có trong bài nói.', N'Bẫy phương thức điểm danh truyền thống (signing a sheet) thay vì quét thẻ kỹ thuật số.', N'Đáp án đúng: Trùng khớp trực tiếp với chỉ dẫn "scan your digital conference badge at the entrance of the auditorium".', N'Bẫy ghép tên nhân vật trong bài vào hành động sai.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1408973+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (67, 67, 1, N'All reimbursement requests must be submitted to the accounting department ------- the fifteenth of each month.', N'by', N'until', N'during', N'among', N'A', N'Phân biệt giới từ chỉ hạn chót "by" (trước hoặc muộn nhất là thời điểm nào) và "until" (kéo dài liên tục đến thời điểm nào). Động từ "submit" là hành động điểm nên bắt buộc đi với "by".', N'Đáp án đúng: Giới từ "by + mốc thời gian" diễn tả hạn chót (deadline) cho một hành động hoàn tất như "submitted".', N'Bẫy giới từ kinh điển: "until" chỉ hành động kéo dài liên tục, không dùng cho động từ hoàn tất một lần như "submit".', N'Bẫy giới từ: "during" phải đi với một khoảng thời gian danh từ (e.g., during the month), không đi với ngày cụ thể "the fifteenth".', N'Bẫy nghĩa giới từ: "among" mang nghĩa "trong số (từ 3 đối tượng trở lên)", không dùng để chỉ mốc thời gian.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1418972+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (68, 68, 1, N'By the time the chief executive officer arrives at the Tokyo headquarters tomorrow, the legal team ------- the merger contract.', N'finalized', N'will have finalized', N'had finalized', N'is finalizing', N'B', N'Cấu trúc "By the time + S + V(hiện tại đơn), S + will have + V3/ed (tương lai hoàn thành)" diễn tả hành động sẽ hoàn tất trước một thời điểm hoặc hành động khác trong tương lai.', N'Bẫy sai thì: Thì quá khứ đơn mâu thuẫn với trạng từ chỉ tương lai "tomorrow" và mệnh đề "arrives".', N'Đáp án đúng: Thì tương lai hoàn thành (will have + V3) đi chuẩn với mệnh đề trạng ngữ "By the time + Hiện tại đơn".', N'Bẫy công thức máy móc: Nhiều thí sinh học vẹt "By the time" đi với "had V3" mà quên nhìn động từ "arrives ... tomorrow".', N'Bẫy thì hiện tại tiếp diễn: Không phản ánh được tính hoàn tất trước một mốc tương lai của cấu trúc "By the time".', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1418972+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (69, 69, 1, N'The newly installed inventory tracking software has made warehouse operations ------- more efficient.', N'consider', N'considerable', N'considerably', N'consideration', N'C', N'Trước tính từ so sánh hơn "more efficient" cần một trạng từ (adverb) để nhấn mạnh mức độ.', N'Bẫy từ loại: Động từ nguyên mẫu không thể đứng bổ nghĩa cho cụm tính từ so sánh "more efficient".', N'Bẫy cấu trúc make + O + Adj: Thí sinh nhìn thấy "made warehouse operations" nên vội chọn tính từ mà bỏ sót cụm tính từ chính "more efficient" ở phía sau.', N'Đáp án đúng: Trạng từ "considerably" bổ nghĩa mức độ cho tính từ so sánh hơn "more efficient".', N'Bẫy từ loại: Danh từ không thể đứng giữa tân ngữ "operations" và cụm tính từ "more efficient".', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1428977+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (70, 70, 1, N'Although the marketing interns were given guidance, they designed the social media campaign entirely by -------.', N'they', N'them', N'their', N'themselves', N'D', N'Cụm cố định "by + đại từ phản thân (oneself)" mang nghĩa "tự lực / một mình không ai giúp đỡ". Chủ ngữ là "they" nên dùng "themselves".', N'Bẫy sai hình thái đại từ: Đại từ nhân xưng chủ ngữ không thể đứng sau giới từ "by".', N'Bẫy đại từ tân ngữ: Khi chủ ngữ và tân ngữ cùng chỉ một đối tượng ("they") và đi sau giới từ "by" để chỉ sự tự lực, phải dùng đại từ phản thân.', N'Bẫy tính từ sở hữu: "their" không thể đứng độc lập ở cuối câu mà không có danh từ theo sau.', N'Đáp án đúng: Cấu trúc "by themselves" = "on their own" (tự chính họ thực hiện).', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1438977+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (71, 71, 1, N'------- the rising cost of raw materials, Falcon Manufacturing managed to keep its retail prices stable this year.', N'Although', N'Despite', N'Because of', N'Unless', N'B', N'Phía sau chỗ trống là một cụm danh từ ("the rising cost of raw materials", không có động từ chia thì) và mang nghĩa nhượng bộ tương phản, do đó cần giới từ "Despite".', N'Bẫy liên từ vs giới từ: "Although" cùng nghĩa tương phản nhưng là liên từ phụ thuộc, bắt buộc theo sau bởi một mệnh đề (S + V).', N'Đáp án đúng: Giới từ chỉ sự nhượng bộ "Despite + Cụm danh từ (Noun Phrase)".', N'Bẫy ngữ nghĩa: Đúng ngữ pháp giới từ nhưng sai logic nhân quả.', N'Bẫy liên từ điều kiện: "Unless" phải đi với mệnh đề (S + V) và không hợp nghĩa câu.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1438977+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (72, 72, 1, N'Any employee ------- to attend the leadership seminar in Chicago must obtain written approval from a department head.', N'wishes', N'wished', N'wishing', N'to wish', N'C', N'Rút gọn mệnh đề quan hệ: Câu gốc là "Any employee who wishes to attend...". Động từ chính của câu đã là "must obtain", nên chỗ trống cần hiện tại phân từ chủ động "wishing".', N'Bẫy hai động từ chia thì trong một mệnh đề đơn: Câu đã có động từ chính "must obtain".', N'Bẫy phân từ bị động: Động từ "wish" ở đây mang nghĩa chủ động, không thể rút gọn dạng V-ed bị động.', N'Đáp án đúng: Rút gọn mệnh đề quan hệ chủ động (who wishes -> wishing).', N'Bẫy động từ nguyên mẫu có "to": Không dùng "to V" để rút gọn mệnh đề quan hệ thông thường khi không có số thứ tự.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1454026+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (73, 73, 1, N'A comprehensive review of the company''s cybersecurity protocols and backup systems ------- conducted every six months.', N'are', N'is', N'have', N'were', N'B', N'Sự hòa hợp Chủ ngữ - Vị ngữ: Chủ ngữ chính là danh từ số ít "A comprehensive review", không phải cụm danh từ số nhiều đứng ngay trước chỗ trống ("protocols and backup systems").', N'Bẫy danh từ gần nhất (Proximity trap): Thí sinh nhìn thấy "protocols and backup systems" số nhiều nên chọn "are".', N'Đáp án đúng: Động từ to-be số ít "is" hòa hợp với chủ ngữ chính "A comprehensive review" và tạo thể bị động "is conducted".', N'Bẫy sai cả số lẫn thể.', N'Bẫy sai số và sai thì (câu có "every six months" chỉ thói quen định kỳ ở hiện tại).', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1454026+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (74, 74, 1, N'The new biodegradable packaging is ------- durable enough for international shipping and cost-effective to produce.', N'either', N'neither', N'whether', N'both', N'D', N'Cặp liên từ tương quan: Phía sau có liên từ "and" nối hai tính từ, do đó vị trí phía trước bắt buộc là "both".', N'Bẫy cặp liên từ: "either" phải đi với "or", không đi với "and".', N'Bẫy cặp liên từ: "neither" phải đi với "nor", không đi với "and".', N'Bẫy cặp liên từ: "whether" đi với "or".', N'Đáp án đúng: Cặp liên từ tương quan chuẩn "both A and B".', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1469138+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (75, 75, 1, N'Select the best word to fill in blank [21].', N'streamline', N'postpone', N'complicate', N'reimburse', N'A', N'Ngữ cảnh phía sau giải thích nhân viên sẽ nhận được tiền hoàn lại trong 3 ngày thay vì 2 tuần, tức là quy trình được đẩy nhanh / tinh gọn ("streamline").', N'Đáp án đúng: "streamline" (hợp lý hóa, tinh gọn) khớp hoàn hảo với việc rút ngắn thời gian từ 2 tuần xuống 3 ngày.', N'Bẫy từ vựng ngược nghĩa: "postpone" (trì hoãn) mâu thuẫn với mục đích nâng cấp hệ thống.', N'Bẫy ngữ nghĩa: Không ai nâng cấp phần mềm với mục đích làm phức tạp hóa ("complicate") quy trình.', N'Bẫy lặp từ trong ngữ cảnh: Phía sau đã có danh từ "reimbursement process".', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1469138+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (76, 76, 1, N'Select the best sentence to fill in blank [22].', N'The quarterly sales targets have been adjusted accordingly.', N'A link to the training video has been sent to your corporate e-mail address.', N'Several clients have requested digital copies of their contracts.', N'Travel allowances will be reduced by ten percent next fiscal year.', N'B', N'Câu trước nhắc tới "a 30-minute interactive tutorial", câu sau nhắc tới "Once you have finished the module...". Do đó câu ở vị trí [22] phải kết nối cách truy cập bài hướng dẫn.', N'Bẫy lạc đề dựa trên đối tượng người nhận: Đoạn văn đang nói về phần mềm hoàn ứng chi phí, không phải chỉ tiêu doanh số.', N'Đáp án đúng: Liên kết mạch lạc giữa "complete a 30-minute interactive tutorial" ở câu trước và "finished the module" ở câu sau.', N'Bẫy từ vựng "digital": Không liên quan đến quy trình đào tạo nội bộ.', N'Bẫy liên tưởng từ "travel expense": Đưa thông tin cắt giảm ngân sách không ăn nhập với mạch hướng dẫn kích hoạt tài khoản.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1479187+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (77, 77, 1, N'Select the best transition word to fill in blank [23].', N'For instance', N'Similarly', N'However', N'Therefore', N'C', N'Câu trước nói hóa đơn trước ngày 1/11 vẫn xử lý qua hệ thống cũ; câu sau nói chi phí từ ngày 1/11 trở đi chỉ được nhận qua ứng dụng mới. Hai mệnh đề đối lập nhau nên dùng "However".', N'Bẫy trạng từ liên kết: Câu sau không phải là ví dụ minh họa mà là quy định ngược lại.', N'Bẫy quan hệ tương đồng: Đây là sự tương phản, không phải tương đồng.', N'Đáp án đúng: Thể hiện rõ sự đối lập quy trình giữa các hóa đơn trước ngày 1/11 và từ ngày 1/11 trở đi.', N'Bẫy quan hệ nhân quả: Quy định sau không phải là hệ quả suy ra từ quy định trước.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1494661+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (78, 78, 1, N'Select the best word to fill in blank [24].', N'at', N'to', N'with', N'for', N'A', N'Trong tiếng Anh thương mại, khi liên hệ qua số máy lẻ nội bộ (extension + số), giới từ chuẩn luôn là "at" (contact someone at extension 4402).', N'Đáp án đúng: Collocation chuẩn "reach/contact someone at extension + [number]".', N'Bẫy dịch từng từ: Động từ "contact" là ngoại động từ không đi với "to".', N'Bẫy giới từ: "contact with" là lỗi sai phổ biến khi "contact" đóng vai trò là động từ.', N'Bẫy giới từ chỉ mục đích.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1494661+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (79, 79, 1, N'Who would most likely benefit from attending Session 3?', N'International freight forwarders', N'Freelance graphic designers seeking agency jobs', N'Owners of companies with a small workforce', N'Software developers building mobile applications', N'C', N'Session 3 trình bày về ưu đãi thuế bang dành cho các doanh nghiệp có dưới 50 nhân viên toàn thời gian (fewer than 50 full-time employees).', N'Bẫy thông tin của phần khác: Liên quan tới nội dung chuỗi cung ứng ở Session 1.', N'Bẫy từ khóa "agency" lấy từ Session 2.', N'Đáp án đúng: Paraphrase cụm "businesses with fewer than 50 full-time employees" thành "companies with a small workforce".', N'Bẫy từ khóa "mobile app" xuất hiện ở dòng chú thích cuối bài.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1504726+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (80, 80, 1, N'What is indicated about Ms. Patel''s session?', N'It will take place in the Emerald Atrium.', N'It requires participants to register in advance.', N'It is the first session of the morning.', N'It charges an additional fee for laptop rentals.', N'B', N'Ms. Patel phụ trách Session 2. Chú thích trong Session 2 yêu cầu mang laptop, và chú thích cuối bài ghi rõ Session 2 giới hạn 40 chỗ ngồi và bắt buộc đăng ký trước qua ứng dụng.', N'Bẫy địa điểm: Emerald Atrium là nơi phục vụ bữa trưa buffet, còn phiên của Ms. Patel diễn ra tại Rainier Suite.', N'Đáp án đúng: Kết nối thông tin "Ms. Patel" -> "Session 2" -> "advance sign-up via the symposium mobile app is mandatory".', N'Bẫy thứ tự: Phiên đầu tiên lúc 9:00 sáng là của Mr. Marcus Thorne.', N'Bẫy suy diễn sai: Người tham gia được nhắc tự mang theo laptop đã sạc đầy, không có dịch vụ cho thuê thu phí.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1514729+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (81, 81, 1, N'What is included with the symposium registration?', N'A private consultation with a tax accountant', N'Free parking at the Grand Harbor Convention Center', N'A complimentary midday meal', N'A one-year subscription to analytics software', N'C', N'Dòng cuối cùng ghi rõ: "Registration includes complimentary morning coffee and a buffet lunch".', N'Bẫy suy diễn từ diễn giả Evelyn Cho, CPA ở Session 3.', N'Bẫy thông tin không được đề cập (Not mentioned trap).', N'Đáp án đúng: Paraphrase chuẩn từ "complimentary ... buffet lunch" thành "complimentary midday meal".', N'Bẫy từ khóa "analytics" lấy từ bài tập thực hành ở Session 2.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1524730+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (82, 82, 1, N'Why did Ms. Abbott send the e-mail?', N'To complain about an unexpected shipping charge', N'To report that some glass bottles were broken', N'To notify the vendor that an incorrect product was delivered', N'To increase the quantity of lotion before the shipment goes out', N'C', N'Trong email, bà Abbott báo rằng thay vì nhận được Bamboo Facial Towels như trên hóa đơn, spa lại nhận nhầm sang khăn tay cotton thông thường.', N'Bẫy thông tin ngược: Hóa đơn ghi rõ phí giao hàng là $0.00 (được miễn phí).', N'Bẫy từ khóa "damaged" ở phần chính sách đổi trả, nhưng email ghi rõ "Everything arrived intact".', N'Đáp án đúng: Spa đặt khăn mặt sợi tre nhưng lại bị giao nhầm khăn tay cotton.', N'Bẫy sai trình tự thời gian: Đơn hàng đã được giao vào ngày hôm qua (March 6).', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1534728+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (83, 83, 1, N'If the requested item is out of stock, how much money should be refunded to Serenity Day Spa?', N'$30.00', N'$130.00', N'$180.00', N'$220.00', N'C', N'Suy luận chéo 2 văn bản: Trong email (Passage 2), mặt hàng bị giao sai cần hoàn tiền nếu hết hàng là "Bamboo Facial Towels". Đối chiếu lên Hóa đơn (Passage 1), mục "LB-310 | Bamboo Facial Towels" có tổng tiền (Total) là $180.00.', N'Bẫy đơn giá (Unit Price trap): $30.00 chỉ là giá của 1 gói khăn, trong khi spa đặt 6 gói (Tổng $180.00).', N'Bẫy nhầm dòng trên bảng hóa đơn: $130.00 là tổng tiền của mục LB-408 (Chamomile Soothing Serum).', N'Đáp án đúng: Suy luận chéo từ "Bamboo Facial Towels" trong email sang cột Total của mã LB-310 trên hóa đơn ($30.00 x 6 = $180.00).', N'Bẫy nhầm dòng trên bảng hóa đơn: $220.00 là tổng tiền của mục LB-205 (Eucalyptus Massage Lotion).', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1544730+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (84, 84, 1, N'Why is Serenity Day Spa eligible for a free replacement?', N'Because the total order exceeded $500.00', N'Because Ms. Abbott submitted the claim within five business days of delivery', N'Because Serenity Day Spa is a first-time wholesale customer', N'Because the spa paid an extra fee for shipping insurance', N'B', N'Suy luận chéo: Chính sách cuối hóa đơn (Passage 1) quy định khiếu nại gửi trong vòng 5 ngày làm việc kể từ ngày giao hàng (March 6) sẽ được đổi miễn phí. Email (Passage 2) được gửi ngay ngày March 7 (1 ngày sau khi nhận hàng).', N'Bẫy nhầm điều kiện khuyến mãi: Đơn trên $500 là điều kiện để được miễn phí vận chuyển ban đầu, không phải điều kiện đổi trả hàng lỗi.', N'Đáp án đúng: Kết hợp ngày giao hàng (March 6) + chính sách 5 ngày trên Hóa đơn với ngày gửi email khiếu nại (March 7).', N'Bẫy thông tin không có trong cả hai văn bản.', N'Bẫy suy diễn không có thật: Không có khoản phí bảo hiểm vận chuyển nào trên hóa đơn.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1554736+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (85, 85, 1, N'Directions: Read the e-mail below. You will have 10 minutes to read the e-mail and write your response. Your response should be between 60 and 100 words.' + CHAR(10) + N'' + CHAR(10) + N'--------------------------------------------------------------------' + CHAR(10) + N'From: David Kowalski, Procurement Manager, Apex Retail Group' + CHAR(10) + N'To: Customer Fulfillment Team, Vanguard Electronics' + CHAR(10) + N'Subject: Urgent – Delayed Delivery of Order #VE-9042' + CHAR(10) + N'Sent: October 19, 10:15 A.M.' + CHAR(10) + N'' + CHAR(10) + N'Dear Fulfillment Team,' + CHAR(10) + N'' + CHAR(10) + N'We have still not received our shipment of 200 wireless barcode scanners (Order #VE-9042), which was guaranteed to arrive at our Chicago warehouse yesterday. Because our seasonal inventory audit begins this Thursday, this delay is disrupting our operations. Please explain why our shipment has been delayed and let us know what compensation or expedited shipping solution you can offer.' + CHAR(10) + N'' + CHAR(10) + N'Regards,' + CHAR(10) + N'David Kowalski' + CHAR(10) + N'--------------------------------------------------------------------' + CHAR(10) + N'' + CHAR(10) + N'Directions: Respond to the e-mail as if you are a Customer Fulfillment Representative at Vanguard Electronics. In your e-mail, do the following:' + CHAR(10) + N'1. Apologize professionally and explain the specific reason for the shipping delay.' + CHAR(10) + N'2. Confirm the exact new delivery date before Thursday.' + CHAR(10) + N'3. Offer a clear form of compensation (e.g., partial refund, discount voucher, or waived shipping fee).' + CHAR(10) + N'(Target length: 60–100 words | Time limit: 10 minutes)', NULL, NULL, NULL, NULL, NULL, N'Tiêu chí chấm điểm ETS Writing Part 2: (1) Giải quyết trọn vẹn cả 3 yêu cầu trong đề bài; (2) Văn phong email thương mại lịch sự, trang trọng; (3) Sử dụng chính xác các từ nối và cấu trúc câu phức; (4) Đạt độ dài tiêu chuẩn 60–100 từ.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 60, 100, NULL, 600, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1559794+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (86, 86, 1, N'Directions: Read the question below. You have 20 minutes to plan, write, and revise your essay. Your essay should be between 150 and 220 words.' + CHAR(10) + N'' + CHAR(10) + N'--------------------------------------------------------------------' + CHAR(10) + N'PROMPT:' + CHAR(10) + N'Do you agree or disagree with the following statement?' + CHAR(10) + N'"Remote work increases employee productivity more than working in a traditional office."' + CHAR(10) + N'Use specific reasons and examples to support your answer.' + CHAR(10) + N'--------------------------------------------------------------------' + CHAR(10) + N'' + CHAR(10) + N'Essay Guidelines:' + CHAR(10) + N'- State your position clearly in the introductory paragraph.' + CHAR(10) + N'- Develop at least two well-structured body paragraphs with concrete business examples.' + CHAR(10) + N'- Include a concise concluding paragraph summarizing your main argument.' + CHAR(10) + N'(Target length: 150–220 words | Time limit: 20 minutes)', NULL, NULL, NULL, NULL, NULL, N'Tiêu chí chấm điểm ETS Writing Part 3: (1) Bố cục 3 phần (Introduction - Body Paragraphs - Conclusion) mạch lạc; (2) Lập luận nhất quán kèm ví dụ thực tiễn nơi công sở; (3) Đa dạng hóa cấu trúc ngữ pháp và từ vựng học thuật/thương mại; (4) Đạt độ dài 150–220 từ.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 150, 220, NULL, 1200, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1564819+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (87, 87, 1, N'Summarise the main trends, select the most important features, and make relevant comparisons. Write at least 150 words.', NULL, NULL, NULL, NULL, NULL, N'Assessment focus: a clear overview, accurate comparisons, selective data, and a coherent progression from 2010 to 2020.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 150, 220, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1575543+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (88, 88, 1, N'Discuss both views and give your own opinion. Support your position with reasons and relevant examples. Write at least 250 words.', NULL, NULL, NULL, NULL, NULL, N'Assessment focus: address both views, state a consistent position, develop supported ideas, and control paragraph cohesion.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 250, 350, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1575543+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (89, 89, 1, N'Write a polite email reply covering all three points. Use an appropriate greeting, clear paragraphs, and a suitable closing. Write 120–150 words.', NULL, NULL, NULL, NULL, NULL, N'Assessment focus: fulfil every communicative purpose, maintain an appropriate formal register, and organize a clear email.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 120, 150, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1589292+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (90, 90, 1, N'Write an opinion essay with a clear position, two developed reasons, and relevant examples. Write 250–300 words.', NULL, NULL, NULL, NULL, NULL, N'Assessment focus: build a logical argument, explain examples, use cohesive paragraphs, and maintain accurate language.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 250, 300, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1602111+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (91, 91, 1, N'Which induction session can the applicant attend?', N'The 10 o''clock session', N'The 2:30 session', N'The Tuesday evening session', N'Any session without a student card', N'B', N'The 10 o''clock session is full; the coordinator offers two places in the 2:30 session.', N'This repeats the applicant''s first choice, but the coordinator says it is full.', N'This is the alternative session with two available places.', N'No evening session is mentioned in the recording.', N'The card is something to bring, not a condition for choosing a session.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1602111+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (92, 92, 1, N'What does the tutor advise the student to do first?', N'Add another source', N'Correct every date', N'Reorganise the section around the comparison', N'Submit the draft on Thursday', N'C', N'The tutor explicitly prioritizes reorganising the section to make the comparison clearer.', N'The tutor says not to add a source yet.', N'The advice is about the comparison, not date accuracy.', N'This paraphrases the tutor''s instruction to make the contrast clearer.', N'The tutor gives a Friday deadline and no Thursday submission instruction.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1612089+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (93, 93, 1, N'Where can visitors now find the sculpture collection?', N'Upstairs beside the reading room', N'In the closed north gallery', N'Inside the café', N'Outside the building', N'A', N'The guide says the collection has moved upstairs beside the reading room.', N'This is the new location stated by the guide.', N'The gallery is closed and no longer holds the collection.', N'The café is unaffected and is not the collection location.', N'No outdoor relocation is mentioned.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1622086+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (94, 94, 1, N'What changed because of the delayed interviews?', N'The survey will be cancelled', N'The interviews ended one month early', N'The report will be ready in May', N'The report is expected one week later', N'D', N'The report is now expected in early July rather than late June, a one-week shift.', N'The researcher says only the timeline changed.', N'The interviews were delayed, not completed early.', N'May was the expected survey completion, not the report date.', N'This captures the stated shift to early July.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1632085+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (95, 95, 1, N'Which condition produced the greatest cooling in the study?', N'Mature trees shading continuous pavement', N'Newly planted trees in parks', N'Isolated trees beside buildings', N'Any street with a wide road', N'A', N'The passage contrasts continuous shade from mature trees with the smaller effects of isolated trees.', N'This matches the reported condition with the greatest cooling.', N'Neither newly planted trees nor parks are identified as the strongest condition.', N'The passage says isolated trees produced smaller effects.', N'Road width is not identified as a factor.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1632085+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (96, 96, 1, N'Which option correctly matches the 2024 transport mode with its share? (Matching)', N'Bus — 41%', N'Bicycle — 34%', N'Rail — 41%', N'Bus — 25%', N'C', N'Rail is matched with 41%; the other options pair the wrong mode and percentage.', N'41% is the rail figure; bus use fell to 34%.', N'Cycling is reported at 25%, not 34%.', N'This correctly matches the mode to the 2024 percentage.', N'25% is the bicycle share, while bus use was 34%.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1642086+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (97, 97, 1, N'Why were the maps digitised first?', N'They had the highest online readership', N'They were inexpensive to replace', N'They contained no historical information', N'To prevent damage from repeated handling', N'D', N'The passage says preservation needs, rather than popularity, determined the order.', N'The passage explicitly distinguishes preservation from popularity.', N'The maps are described as fragile and at risk of irreversible damage.', N'The passage does not say this.', N'This is the stated preservation reason.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1642086+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (98, 98, 1, N'What can be inferred about the restoration at Site B?', N'More rainfall alone guarantees rapid recovery', N'Improved soil drainage may support plant recovery', N'Native plants cannot grow in dry conditions', N'Site A had no compacted soil', N'B', N'The comparison suggests soil condition supported recovery even with less rainfall.', N'Site A had more rainfall but recovered slowly.', N'Site B recovered quickly after the compacted soil problem was addressed.', N'Native plants spread at the site receiving less rain.', N'The passage identifies compacted soil at Site A.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1657118+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (99, 99, 1, N'When should students arrive at the main gate?', N'7:00 on Saturday', N'7:15 on Sunday', N'7:30 on Saturday', N'8:15 on Saturday', N'A', N'The trip leaves at 7:15 and students must arrive fifteen minutes earlier, at 7:00.', N'Arriving fifteen minutes before the 7:15 departure means 7:00.', N'The trip is on Saturday, not Sunday.', N'This is after departure and contradicts the instruction.', N'The departure is at 7:15, so this is an hour too late.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1667175+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (100, 100, 1, N'Why has the language club changed rooms?', N'The club meeting has been cancelled', N'Room 118 has a better projector', N'The meeting time has changed', N'The projector in Room 204 needs repair', N'D', N'The projector in Room 204 is being repaired; the meeting time remains unchanged.', N'The staff confirms that only the room changed.', N'The reason given is repair work in Room 204.', N'The time remains 3 p.m.', N'This is the reason stated by the office staff.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1677166+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (101, 101, 1, N'What should volunteers do by Friday?', N'Clean the library on 16 June', N'Register at the library desk', N'Bring the first notice to the event', N'Arrive at the clean-up by 5 p.m.', N'B', N'The announcer asks volunteers to register at the library desk by 5 p.m.', N'The event date was corrected to 18 June; the task is registration.', N'This is the action and location stated in the announcement.', N'No instruction to bring a notice is given.', N'5 p.m. Friday is the registration deadline, not the event time.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1687165+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (102, 102, 1, N'How can the student change workshop groups?', N'Pay for a new registration at the office', N'Ask the student group to change its date', N'Transfer online before noon tomorrow', N'Wait until the Friday workshop begins', N'C', N'The advisor says to transfer online before noon tomorrow; the student plans to do it tonight.', N'The advisor says there is no extra fee.', N'The advisor offers a different group, not a date change.', N'This is the required process and deadline.', N'The transfer must be made before noon tomorrow.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1687165+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (103, 103, 1, N'Who is the delivery service mainly designed to help?', N'Older residents who cannot travel easily', N'Volunteers who want free books', N'Visitors who have not joined the library', N'People who live outside the town', N'A', N'The first sentence identifies older residents with limited ability to travel.', N'This restates the group named in the passage.', N'Volunteers deliver the books; they are not the intended readers.', N'The passage says users need to register, but does not identify visitors as the target group.', N'The service is described for residents of the town.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1697180+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (104, 104, 1, N'Which option correctly matches a course with its start date? (Matching)', N'Computer skills — 30 September', N'Accounting — one week after applications close', N'Workplace communication — two weeks after 30 September', N'All courses — the application deadline', N'C', N'Classes begin two weeks after applications close on 30 September, so the start is in October.', N'30 September is the application deadline, not the class start date.', N'The passage states a two-week interval.', N'This correctly matches the course schedule stated in the notice.', N'The deadline is for applying; classes begin later.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1707167+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (105, 105, 1, N'Why did attendance probably fail to increase?', N'The sports centre reduced its opening hours', N'The transport office closed permanently', N'There were no activities at the centre', N'Public transport was unavailable at closing time', N'D', N'The last bus left before the centre closed, making the extended hours difficult to use.', N'The centre extended, rather than reduced, its hours.', N'The passage says a schedule is being discussed, not that the office closed.', N'No information about activities is provided.', N'The last bus left before the new closing time.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1717165+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (106, 106, 1, N'What was the main reason for adding a Wednesday class?', N'The Saturday class was too expensive', N'To accommodate learners with shift-work conflicts', N'The organiser wanted to remove the Saturday class', N'The programme changed to online teaching', N'B', N'The additional class responds to learners'' reported work schedules while keeping the original option.', N'No course fee is mentioned.', N'This is the stated reason for adding an evening alternative.', N'The passage says Saturday remains available.', N'Both options are described as classes; online delivery is not mentioned.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1717165+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (107, 107, 1, N'The quarterly budget report must be submitted _____ Friday at 5:00 PM.', N'before', N'prior', N'advance', N'ahead', N'A', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1727164+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (108, 108, 1, N'Where did Ms. Chen leave the presentation handouts?', N'On the conference table', N'Yes, yesterday', N'To meet the client', NULL, N'A', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1737164+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (109, 109, 1, N'Select TWO benefits resulting from the newly installed automated inventory system:', N'Decreased fulfillment errors', N'Real-time stock level visibility', N'Immediate relocation to another province', N'Elimination of electricity costs', N'A,B', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 2, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1747166+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (110, 110, 1, N'The express shuttle departs every [blank] minutes from terminal two.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, 1, NULL, NULL, NULL, 1, N'["15","fifteen"]', NULL, NULL, '2026-11-11T01:44:03.1757164+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (111, 111, 1, N'Choose the best connective word to complete the sentence clause.', N'consequently', N'otherwise', N'nevertheless', NULL, N'A', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1762225+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (112, 112, 1, N'Drag the appropriate department heading to the organizational slot.', N'Quality Assurance', N'Investor Relations', N'Facilities Maintenance', NULL, N'A', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1772244+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (113, 113, 1, N'What is mentioned as a key benefit of the Apex-Horizon merger?', N'Reduced shipping overhead costs', N'Higher retail retail shelf prices', N'Cancellation of overseas contracts', N'Downsizing regional warehouses', N'A', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1772244+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (114, 114, 1, N'Read the announcement aloud: Welcome to the annual Global Tech Conference. Please pick up your registration badge at the front desk.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 5, 5, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1782242+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (115, 115, 1, N'Write ONE sentence based on the picture using the two mandatory keywords: conference / yesterday.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 5, 35, NULL, 480, NULL, 1, NULL, N'conference, yesterday', NULL, '2026-11-11T01:44:03.1792242+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (116, 116, 1, N'Write an opinion essay explaining whether remote work improves employee productivity.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 15, 300, NULL, 1800, NULL, 1, NULL, NULL, NULL, '2026-11-11T01:44:03.1792242+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (117, 118, 1, N'Listen: where is the meeting being held?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'A', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9347648+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (118, 119, 1, N'Listen: what time does the shop close?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'B', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9397649+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (119, 120, 1, N'Listen: which platform does the train leave from?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'C', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9397649+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (120, 121, 1, N'Listen: what did the caller ask to change?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'D', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9407647+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (121, 122, 1, N'Listen: why is the shipment delayed?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'A', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9407647+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (122, 123, 1, N'Listen: what will the woman do next?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'B', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9417644+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (123, 124, 1, N'Listen: what does the man imply about the budget?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'C', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9422700+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (124, 125, 1, N'Listen: what concern does the second speaker raise?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'D', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9422700+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (125, 126, 1, N'Listen: what does she imply about the deadline she was given?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'A', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9432717+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (126, 127, 1, N'Listen: which assumption does the negotiation depend on?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'B', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9432717+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (127, 128, 1, N'Listen: the speaker''s tone shifts twice. Which pair of attitudes fits?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'C', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9442715+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (128, 129, 1, N'Listen: what does the concession at the end actually concede?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'D', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9442715+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (129, 130, 1, N'The office opens _____ nine o''clock.', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'A', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9442715+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (130, 131, 1, N'Please sign _____ the bottom of the form.', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'B', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9452714+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (131, 132, 1, N'The delivery arrived _____ Tuesday morning.', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'C', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9452714+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (132, 133, 1, N'She has worked here _____ 2019.', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'D', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9462717+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (133, 134, 1, N'The notice says the car park closes early. What should staff do?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'A', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9462717+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (134, 135, 1, N'The memo changes the meeting room. Where will the meeting be held?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'B', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9472713+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (135, 136, 1, N'The report qualifies its forecast twice. What does it imply about the second half?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'C', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9472713+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (136, 137, 1, N'The author cites two studies that disagree. What does that disagreement support?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'D', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9477761+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (137, 138, 1, N'The author concedes a point in the second paragraph chiefly to what end?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'A', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9477761+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (138, 139, 1, N'Which claim about the reform does the passage leave standing?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'B', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9487794+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (139, 140, 1, N'Which reading of "the proposal was quietly shelved" does the argument require?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'C', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9492906+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ItemVersions] ([ItemVersionId], [ItemId], [VersionNumber], [Stem], [OptionA], [OptionB], [OptionC], [OptionD], [KeyLabel], [Explanation], [RationaleA], [RationaleB], [RationaleC], [RationaleD], [EvidenceStart], [EvidenceEnd], [EvidenceUnit], [EvidenceQuote], [ReferenceText], [ReferenceKind], [MinWords], [MaxWords], [PrepSeconds], [ResponseSeconds], [MaxSelections], [AllowNumber], [AcceptedAnswersJson], [RequiredKeywords], [Hint], [CreatedAt], [LastWrittenBy]) VALUES (140, 141, 1, N'The final sentence reverses the analogy. What does that reversal license?', N'The option marked correct for this item.', N'Second option.', N'Third option.', N'Fourth option.', N'D', N'See the key.', N'Correct.', N'Incorrect: wrong reference.', N'Incorrect: wrong scope.', N'Incorrect: wrong register.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, '2026-11-11T02:12:01.9492906+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [ItemVersions] OFF;

-- ---- Lessons (13 rows)
SET IDENTITY_INSERT [Lessons] ON;
BEGIN TRY
  INSERT INTO [Lessons] ([LessonId], [CertId], [SkillId], [CategoryId], [Title], [Summary], [EstimatedMinutes], [DisplayOrder], [CreatedAt], [Modality], [TopicId], [CefrLevel], [TargetBands]) VALUES (1, 1, 2, 1, N'Present Continuous Tense', N'Master the present continuous tense for temporary and ongoing actions in TOEIC Part 5.', 5, 1, '2026-11-11T01:44:01.6437563+07:00', N'reading', 12, N'B1', N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Lessons] ([LessonId], [CertId], [SkillId], [CategoryId], [Title], [Summary], [EstimatedMinutes], [DisplayOrder], [CreatedAt], [Modality], [TopicId], [CefrLevel], [TargetBands]) VALUES (2, 1, 2, NULL, N'Tense & Aspect Mastery in Business Contexts', N'Phương pháp 3 giây nhận diện dấu hiệu phối hợp thì (Time Markers) và phân biệt chủ động - bị động trong Part 5.', 15, 1, '2026-11-11T01:44:02.3963832+07:00', N'reading', 12, N'B1', N'450,550,650');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Lessons] ([LessonId], [CertId], [SkillId], [CategoryId], [Title], [Summary], [EstimatedMinutes], [DisplayOrder], [CreatedAt], [Modality], [TopicId], [CefrLevel], [TargetBands]) VALUES (3, 1, 1, NULL, N'Eliminating Auditory Traps & Mastering Indirect Responses', N'Kỹ thuật loại trừ đáp án lặp âm/lặp từ và nhận diện các mẫu câu trả lời gián tiếp đạt điểm tuyệt đối Part 2.', 15, 2, '2026-11-11T01:44:02.3983832+07:00', N'listening', 5, N'B1', N'450,550');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Lessons] ([LessonId], [CertId], [SkillId], [CategoryId], [Title], [Summary], [EstimatedMinutes], [DisplayOrder], [CreatedAt], [Modality], [TopicId], [CefrLevel], [TargetBands]) VALUES (5, 1, 2, NULL, N'Cross-Text Inference in Multi-Passage Reading (Part 7)', N'Phương pháp tìm "Từ khóa cầu nối" (Bridge Keywords) để giải quyết câu hỏi suy luận chéo giữa Hóa đơn, Lịch trình và Email.', 20, 4, '2026-11-11T01:44:02.4003832+07:00', N'reading', 9, N'B1', N'650,750,850');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Lessons] ([LessonId], [CertId], [SkillId], [CategoryId], [Title], [Summary], [EstimatedMinutes], [DisplayOrder], [CreatedAt], [Modality], [TopicId], [CefrLevel], [TargetBands]) VALUES (6, 2, 5, NULL, N'IELTS Listening: Follow Signposts and Corrections', N'Track the speaker''s purpose, changes, and final answer rather than matching isolated words.', 12, 1, '2026-11-11T01:44:02.5741463+07:00', N'listening', 1, N'B1', N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Lessons] ([LessonId], [CertId], [SkillId], [CategoryId], [Title], [Summary], [EstimatedMinutes], [DisplayOrder], [CreatedAt], [Modality], [TopicId], [CefrLevel], [TargetBands]) VALUES (7, 2, 6, NULL, N'IELTS Reading: Paraphrase Claims and Match Evidence', N'Connect question wording to synonyms, references, and evidence across academic passages.', 12, 2, '2026-11-11T01:44:02.5741463+07:00', N'reading', 7, N'B1', N'ALL');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Lessons] ([LessonId], [CertId], [SkillId], [CategoryId], [Title], [Summary], [EstimatedMinutes], [DisplayOrder], [CreatedAt], [Modality], [TopicId], [CefrLevel], [TargetBands]) VALUES (8, 2, 7, NULL, N'IELTS Task 1: Select and Describe Trends', N'Write a concise overview, group key features, and compare data without listing every figure.', 12, 3, '2026-11-11T01:44:02.5756495+07:00', N'writing', 17, N'B1', N'5.5,6.0,6.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Lessons] ([LessonId], [CertId], [SkillId], [CategoryId], [Title], [Summary], [EstimatedMinutes], [DisplayOrder], [CreatedAt], [Modality], [TopicId], [CefrLevel], [TargetBands]) VALUES (9, 2, 7, NULL, N'IELTS Writing: Coherence, Cohesion, and Position', N'Build a clear Task 2 position with purposeful paragraphs and controlled linking.', 12, 4, '2026-11-11T01:44:02.5756495+07:00', N'writing', 17, N'B1', N'6.5,7.0,7.5');

END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Lessons] OFF;

-- ---- MentorProfiles (1 rows)
SET IDENTITY_INSERT [MentorProfiles] ON;
BEGIN TRY
  INSERT INTO [MentorProfiles] ([MentorId], [DisplayName], [Bio], [CertId], [SkillId], [MeetLink], [PriceCredits], [Headline], [Expertise], [SessionMinutes], [IsActive]) VALUES (1, N'Dr. Trần Minh Khoa', N'10 years teaching TOEIC at major language centers in Ho Chi Minh City. Specializes in listening strategies and accent training for Vietnamese learners.', 1, 1, N'https://meet.google.com/abc-defg-hij', 1, N'10 years teaching TOEIC. Specializes in listening strategies and accent training for Vietnamese learners.', N'Listening strategies, pronunciation, test-taking techniques', 60, 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [MentorProfiles] OFF;

-- ---- MentorWeeklySlots (3 rows)
SET IDENTITY_INSERT [MentorWeeklySlots] ON;
BEGIN TRY
  INSERT INTO [MentorWeeklySlots] ([WeeklySlotId], [MentorId], [DayOfWeek], [StartTime], [EndTime]) VALUES (1, 1, 2, '19:00:00', '20:00:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [MentorWeeklySlots] ([WeeklySlotId], [MentorId], [DayOfWeek], [StartTime], [EndTime]) VALUES (2, 1, 4, '19:00:00', '20:00:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [MentorWeeklySlots] ([WeeklySlotId], [MentorId], [DayOfWeek], [StartTime], [EndTime]) VALUES (3, 1, 6, '09:00:00', '10:00:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [MentorWeeklySlots] OFF;

-- ---- MentorSlots (6 rows)
SET IDENTITY_INSERT [MentorSlots] ON;
BEGIN TRY
  INSERT INTO [MentorSlots] ([SlotId], [MentorId], [StartAt], [EndAt], [Status]) VALUES (1, 1, '2026-11-12T01:44:01.8487237+07:00', '2026-11-12T02:44:01.8487237+07:00', N'open');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [MentorSlots] ([SlotId], [MentorId], [StartAt], [EndAt], [Status]) VALUES (2, 1, '2026-11-13T01:44:01.8487237+07:00', '2026-11-13T02:44:01.8487237+07:00', N'open');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [MentorSlots] ([SlotId], [MentorId], [StartAt], [EndAt], [Status]) VALUES (3, 1, '2026-11-14T01:44:01.8487237+07:00', '2026-11-14T02:44:01.8487237+07:00', N'open');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [MentorSlots] ([SlotId], [MentorId], [StartAt], [EndAt], [Status]) VALUES (4, 1, '2026-11-16T01:44:01.8487237+07:00', '2026-11-16T02:44:01.8487237+07:00', N'open');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [MentorSlots] ([SlotId], [MentorId], [StartAt], [EndAt], [Status]) VALUES (5, 1, '2026-11-06T01:44:01.8497237+07:00', '2026-11-06T02:44:01.8497237+07:00', N'booked');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [MentorSlots] ([SlotId], [MentorId], [StartAt], [EndAt], [Status]) VALUES (6, 1, '2026-11-13T01:44:01.8702933+07:00', '2026-11-13T02:44:01.8702933+07:00', N'booked');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [MentorSlots] OFF;

-- ---- Orders (2 rows)
SET IDENTITY_INSERT [Orders] ON;
BEGIN TRY
  INSERT INTO [Orders] ([OrderId], [UserId], [PackageId], [Status], [Amount], [OrderCode], [PaymentNo], [Gateway], [GatewayTxnRef], [GatewayErrorCode], [CreatedAt], [PaidAt]) VALUES (1, 2, 1, N'paid', 190000.00, N'DH-20261014-0001', 1, N'mock', N'plink-demo-10', NULL, '2026-10-17T01:44:00.6939290+07:00', '2026-10-17T01:44:00.6939290+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Orders] ([OrderId], [UserId], [PackageId], [Status], [Amount], [OrderCode], [PaymentNo], [Gateway], [GatewayTxnRef], [GatewayErrorCode], [CreatedAt], [PaidAt]) VALUES (2, 2, 2, N'paid', 100000.00, N'DH-20261014-0002', 2, N'mock', N'plink-demo-5', NULL, '2026-10-24T01:44:00.7236021+07:00', '2026-10-24T01:44:00.7236021+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Orders] OFF;

-- ---- PaymentEvents (2 rows)
SET IDENTITY_INSERT [PaymentEvents] ON;
BEGIN TRY
  INSERT INTO [PaymentEvents] ([EventId], [OrderId], [Source], [ReportedStatus], [SignatureValid], [WasApplied], [Payload], [Note], [CreatedAt]) VALUES (1, 1, N'webhook', N'paid', 1, 1, NULL, N'payOS webhook simulation — 10 credits', '2026-10-17T01:44:00.6954322+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [PaymentEvents] ([EventId], [OrderId], [Source], [ReportedStatus], [SignatureValid], [WasApplied], [Payload], [Note], [CreatedAt]) VALUES (2, 2, N'webhook', N'paid', 1, 1, NULL, N'payOS webhook simulation — 5 credits', '2026-10-24T01:44:00.7236021+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [PaymentEvents] OFF;

-- ---- RolePermissions (5 rows)
BEGIN TRY
  INSERT INTO [RolePermissions] ([RoleId], [PermissionCode]) VALUES (2, N'assessment.blueprint.design');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [RolePermissions] ([RoleId], [PermissionCode]) VALUES (2, N'operations.support.triage');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [RolePermissions] ([RoleId], [PermissionCode]) VALUES (2, N'pool.content.author');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [RolePermissions] ([RoleId], [PermissionCode]) VALUES (2, N'pool.content.review');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [RolePermissions] ([RoleId], [PermissionCode]) VALUES (2, N'system.governance.admin');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH

-- ---- SavedQuestions (2 rows)
SET IDENTITY_INSERT [SavedQuestions] ON;
BEGIN TRY
  INSERT INTO [SavedQuestions] ([SavedQuestionId], [UserId], [CertId], [ItemId], [ItemVersionId], [Source], [SavedAt]) VALUES (1, 2, 1, 2, 2, N'manual', '2026-11-08T01:44:02.8616810+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [SavedQuestions] ([SavedQuestionId], [UserId], [CertId], [ItemId], [ItemVersionId], [Source], [SavedAt]) VALUES (2, 2, 1, 1, 1, N'incorrect', '2026-11-07T01:44:02.8671889+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [SavedQuestions] OFF;

-- ---- ScoreScales (89 rows)
SET IDENTITY_INSERT [ScoreScales] ON;
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (1, 1, 1, 1, 0, 5);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (2, 1, 1, 1, 1, 150);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (3, 1, 1, 1, 2, 260);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (4, 1, 1, 1, 3, 380);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (5, 1, 1, 1, 4, 495);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (6, 1, 2, 1, 0, 5);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (7, 1, 2, 1, 1, 175);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (8, 1, 2, 1, 2, 340);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (9, 1, 2, 1, 3, 495);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (10, 1, 1, 2, 0, 5);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (11, 1, 1, 2, 1, 45);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (12, 1, 1, 2, 2, 85);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (13, 1, 1, 2, 3, 130);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (14, 1, 1, 2, 4, 170);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (15, 1, 1, 2, 5, 210);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (16, 1, 1, 2, 6, 250);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (17, 1, 1, 2, 7, 290);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (18, 1, 1, 2, 8, 330);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (19, 1, 1, 2, 9, 370);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (20, 1, 1, 2, 10, 415);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (21, 1, 1, 2, 11, 455);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (22, 1, 1, 2, 12, 495);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (23, 1, 2, 2, 0, 5);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (24, 1, 2, 2, 1, 45);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (25, 1, 2, 2, 2, 85);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (26, 1, 2, 2, 3, 130);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (27, 1, 2, 2, 4, 170);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (28, 1, 2, 2, 5, 210);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (29, 1, 2, 2, 6, 250);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (30, 1, 2, 2, 7, 290);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (31, 1, 2, 2, 8, 330);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (32, 1, 2, 2, 9, 370);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (33, 1, 2, 2, 10, 415);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (34, 1, 2, 2, 11, 455);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (35, 1, 2, 2, 12, 495);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (36, 2, 5, 5, 0, 20);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (37, 2, 5, 5, 1, 40);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (38, 2, 6, 5, 0, 20);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (39, 2, 6, 5, 1, 40);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (40, 2, 5, 6, 0, 40);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (41, 2, 5, 6, 1, 50);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (42, 2, 6, 6, 0, 40);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (43, 2, 6, 6, 1, 50);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (52, 2, 5, 6, 2, 60);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (53, 2, 5, 6, 3, 70);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (54, 2, 5, 6, 4, 80);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (55, 2, 5, 6, 5, 90);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (56, 2, 6, 6, 2, 60);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (57, 2, 6, 6, 3, 70);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (58, 2, 6, 6, 4, 80);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ScoreScales] ([ScaleId], [CertId], [SkillId], [ExamId], [RawScore], [ScaledScore]) VALUES (59, 2, 6, 6, 5, 90);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [ScoreScales] OFF;

-- ---- Sections (33 rows)
SET IDENTITY_INSERT [Sections] ON;
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (1, 1, 1, N'listening', N'P1', N'Photographs', 1.00, N'AUDIO_DRIVEN', NULL, N'STANDARD_VERTICAL', 4, 1, 1, 1, 0, 1, 1, 0, NULL, NULL, 6, 1, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (2, 1, 1, N'listening', N'P2', N'Question-Response', 1.00, N'AUDIO_DRIVEN', NULL, N'STANDARD_VERTICAL', 3, 1, 1, 1, 0, 1, 0, 0, NULL, NULL, 25, 2, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (3, 1, 1, N'listening', N'P3', N'Conversations', 1.00, N'AUDIO_DRIVEN', NULL, N'STANDARD_VERTICAL', 4, 3, 1, 0, 0, 1, 0, 0, NULL, NULL, 39, 3, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (4, 1, 1, N'listening', N'P4', N'Talks', 1.00, N'AUDIO_DRIVEN', NULL, N'STANDARD_VERTICAL', 4, 3, 1, 0, 0, 1, 0, 0, NULL, NULL, 30, 4, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (5, 2, 1, N'reading', N'P5', N'Incomplete Sentences', 1.00, N'FULL_SANDBOX', NULL, N'STANDARD_VERTICAL', 4, 1, 1, 0, 0, 0, 0, 0, NULL, NULL, 30, 1, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (6, 2, 1, N'reading', N'P6', N'Text Completion', 1.00, N'FULL_SANDBOX', NULL, N'STANDARD_VERTICAL', 4, 1, 1, 0, 1, 0, 0, 0, NULL, NULL, 16, 2, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (7, 2, 1, N'reading', N'P7', N'Reading Comprehension', 1.00, N'FULL_SANDBOX', NULL, N'STANDARD_VERTICAL', 4, 1, 1, 0, 1, 0, 0, 0, NULL, N'{"highlighter":true,"notes":true,"allowHighlight":true,"allowNotes":true}', 54, 3, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (16, 5, 2, N'listening', N'REC', N'Four recordings: everyday conversation to academic lecture', 1.00, N'AUDIO_DRIVEN', N'{"timerMode":"AUDIO_PLUS_CHECK","reviewSeconds":120}', N'STANDARD_VERTICAL', 4, 1, 1, 0, 0, 1, 0, 0, NULL, N'{"highlighter":true,"notes":true,"allowHighlight":true,"allowNotes":true}', 40, 1, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (17, 6, 2, N'reading', N'PAS', N'Three long passages with a mix of question types', 1.00, N'FULL_SANDBOX', NULL, N'SPLIT_HORIZONTAL', 4, 1, 1, 0, 1, 0, 0, 0, NULL, N'{"highlighter":true,"notes":true,"allowHighlight":true,"allowNotes":true}', 40, 1, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (18, 7, 2, N'writing', N'T1', N'Task 1 — describe a chart (150 words)', 1.00, N'FULL_SANDBOX', NULL, N'SPLIT_HORIZONTAL', 4, 1, 1, 0, 0, 0, 0, 0, N'["free_response"]', N'{"wordCount":true,"disableSpellcheck":true,"showWordCount":true,"disableSpellCheck":true}', 1, 1, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (19, 7, 2, N'writing', N'T2', N'Task 2 — write an essay (250 words)', 2.00, N'FULL_SANDBOX', NULL, N'SPLIT_HORIZONTAL', 4, 1, 1, 0, 0, 0, 0, 0, N'["free_response"]', N'{"wordCount":true,"disableSpellcheck":true,"showWordCount":true,"disableSpellCheck":true}', 1, 2, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Sections] ([SectionId], [SkillId], [CertId], [Modality], [Code], [Name], [ScoreWeight], [NavigationMode], [TimerPolicyJson], [LayoutType], [OptionCount], [ClusterSize], [UnitCount], [HideOptionText], [RequirePassage], [RequireAudio], [RequireImage], [RequiredKeywordCount], [AllowedComponentTypesJson], [ToolsConfigJson], [ExpectedQuestionCount], [DisplayOrder], [LastWrittenBy]) VALUES (20, 8, 2, N'speaking', N'INT', N'Interview, cue-card talk and discussion', 1.00, N'SYSTEM_PACED_AUTO', NULL, N'STANDARD_VERTICAL', 4, 1, 1, 0, 0, 0, 0, 0, N'["free_response"]', NULL, 3, 1, NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Sections] OFF;

-- ---- Bookings (2 rows)
SET IDENTITY_INSERT [Bookings] ON;
BEGIN TRY
  INSERT INTO [Bookings] ([BookingId], [UserId], [MentorId], [SlotId], [PriceCredits], [Status], [MeetLink], [FocusArea], [CancelledAt], [CreatedAt]) VALUES (1, 2, 1, 5, 1, N'done', N'https://meet.google.com/abc-defg-hij', N'Listening strategies Part 3 & 4', NULL, '2026-11-06T01:44:01.8497237+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Bookings] ([BookingId], [UserId], [MentorId], [SlotId], [PriceCredits], [Status], [MeetLink], [FocusArea], [CancelledAt], [CreatedAt]) VALUES (2, 2, 1, 6, 1, N'confirmed', N'https://meet.google.com/abc-defg-hij', N'Exam strategy & timing for Reading Part 7', NULL, '2026-11-10T01:44:01.8702933+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Bookings] OFF;


-- ---- ExamAttempts (2 rows)
SET IDENTITY_INSERT [ExamAttempts] ON;
BEGIN TRY
  INSERT INTO [ExamAttempts] ([AttemptId], [UserId], [ExamId], [CertId], [ActiveSkillId], [ActiveUnitNumber], [ActiveGroupId], [AuthorizationCode], [PhaseDeadlineAt], [LastAudioOffsetMs], [CandidateNotesJson], [PreflightVerifiedAt], [Status], [StartedAt], [ExpiresAt], [LastActivityAt], [IsAutoSubmitted], [SubmittedAt], [CurrentExamItemId], [TotalScore], [BandCode], [EpochTag]) VALUES (2, 2, 1, 1, NULL, 1, NULL, NULL, NULL, 0, NULL, NULL, N'graded', '2026-11-08T01:44:01.7977460+07:00', '2026-11-08T03:44:01.7977460+07:00', NULL, 0, '2026-11-08T03:29:01.7977460+07:00', NULL, 720, N'B1', NULL);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [ExamAttempts] OFF;


-- ---- ExamItems (116 rows)
SET IDENTITY_INSERT [ExamItems] ON;
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (3, 1, 1, 1, 3, 3, N'listening', 1, 1, '2026-11-11T01:44:03.0925664+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (4, 1, 2, 1, 4, 4, N'listening', 1, 1, '2026-11-11T01:44:03.0935658+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (5, 1, 3, 1, 5, 5, N'listening', 1, 1, '2026-11-11T01:44:03.0935658+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (6, 1, 4, 1, 6, 6, N'listening', 1, 1, '2026-11-11T01:44:03.0945657+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (7, 1, 5, 1, 7, 7, N'reading', 1, 1, '2026-11-11T01:44:03.0955657+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (8, 1, 5, 1, 8, 8, N'reading', 2, 1, '2026-11-11T01:44:03.0960710+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (9, 1, 5, 1, 9, 9, N'reading', 3, 1, '2026-11-11T01:44:03.0970728+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (10, 1, 6, 1, 10, 10, N'reading', 1, 1, '2026-11-11T01:44:03.0970728+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (11, 1, 7, 1, 11, 11, N'reading', 1, 1, '2026-11-11T01:44:03.0980725+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (41, 5, 16, 2, 41, 41, N'listening', 1, 1, '2026-11-11T01:44:03.1218611+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (42, 5, 17, 2, 42, 42, N'reading', 1, 1, '2026-11-11T01:44:03.1228609+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (43, 6, 16, 2, 43, 43, N'listening', 1, 1, '2026-11-11T01:44:03.1238641+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (44, 6, 17, 2, 44, 44, N'reading', 1, 1, '2026-11-11T01:44:03.1238641+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (91, 6, 16, 2, 91, 91, N'listening', 2, 1, '2026-11-11T01:44:03.1602111+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (92, 6, 16, 2, 92, 92, N'listening', 3, 1, '2026-11-11T01:44:03.1612089+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (93, 6, 16, 2, 93, 93, N'listening', 4, 1, '2026-11-11T01:44:03.1622086+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (94, 6, 16, 2, 94, 94, N'listening', 5, 1, '2026-11-11T01:44:03.1632085+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (95, 6, 17, 2, 95, 95, N'reading', 2, 1, '2026-11-11T01:44:03.1632085+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (96, 6, 17, 2, 96, 96, N'reading', 3, 1, '2026-11-11T01:44:03.1642086+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (97, 6, 17, 2, 97, 97, N'reading', 4, 1, '2026-11-11T01:44:03.1657118+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (98, 6, 17, 2, 98, 98, N'reading', 5, 1, '2026-11-11T01:44:03.1657118+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (99, 6, 20, 2, 45, 45, N'speaking', 1, 1, '2026-11-11T01:44:03.1660000+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamItems] ([ExamItemId], [ExamId], [SectionId], [CertId], [ItemVersionId], [ItemId], [Modality], [DisplayOrder], [UnitNumber], [CreatedAt], [LastWrittenBy]) VALUES (100, 6, 18, 2, 47, 47, N'writing', 1, 1, '2026-11-11T01:44:03.1670000+07:00', 1);
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [ExamItems] OFF;

-- ---- ExamSectionAudio (14 rows)
BEGIN TRY
  INSERT INTO [ExamSectionAudio] ([ExamId], [SectionId], [CertId], [AudioPath], [DurationSec], [SizeBytes], [UploadedAt]) VALUES (1, 1, 1, N'/audio/toeic/test1-p1.mp3', 180, 2880000, '2026-11-11T01:44:01.2200944+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamSectionAudio] ([ExamId], [SectionId], [CertId], [AudioPath], [DurationSec], [SizeBytes], [UploadedAt]) VALUES (1, 2, 1, N'/audio/toeic/test1-p2.mp3', 300, 4800000, '2026-11-11T01:44:01.2210944+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamSectionAudio] ([ExamId], [SectionId], [CertId], [AudioPath], [DurationSec], [SizeBytes], [UploadedAt]) VALUES (1, 3, 1, N'/audio/toeic/test1-p3.mp3', 600, 9600000, '2026-11-11T01:44:01.2210944+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamSectionAudio] ([ExamId], [SectionId], [CertId], [AudioPath], [DurationSec], [SizeBytes], [UploadedAt]) VALUES (1, 4, 1, N'/audio/toeic/test1-p4.mp3', 600, 9600000, '2026-11-11T01:44:01.2210944+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamSectionAudio] ([ExamId], [SectionId], [CertId], [AudioPath], [DurationSec], [SizeBytes], [UploadedAt]) VALUES (5, 16, 2, N'/audio/ielts/cambridge18-t1-listening.mp3', 1800, 28800000, '2026-11-11T01:44:01.4867791+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [ExamSectionAudio] ([ExamId], [SectionId], [CertId], [AudioPath], [DurationSec], [SizeBytes], [UploadedAt]) VALUES (6, 16, 2, N'/audio/ielts/placement-diagnostic.mp3', 600, 9600000, '2026-11-11T01:44:01.4937789+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH

-- ---- LessonCards (52 rows)
SET IDENTITY_INSERT [LessonCards] ON;
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (1, 1, 1, N'rule', N'Rule: Present Continuous for Ongoing Actions', N'The present continuous describes actions happening right now, at the moment of speaking, or around the current time period.' + CHAR(10) + N'' + CHAR(10) + N'**Formula:** `Subject + is / am / are + Verb-ing`' + CHAR(10) + N'' + CHAR(10) + N'Signals in TOEIC Part 5: **now, currently, presently, at the moment, this week**.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (2, 1, 1, N'pitfall', N'Common Pitfalls & Exam Traps', N'Watch out for stative verbs that NEVER appear in the continuous aspect on the TOEIC:' + CHAR(10) + N'' + CHAR(10) + N'- **TRAP 1: Stative Verbs of Perception & Cognition**' + CHAR(10) + N'WRONG: "He is knowing the answer." → CORRECT: "He knows the answer." (know, understand, believe, remember).' + CHAR(10) + N'' + CHAR(10) + N'- **TRAP 2: Possession Verbs**' + CHAR(10) + N'WRONG: "The firm is owning five branches." → CORRECT: "The firm owns five branches." (own, have, belong).' + CHAR(10) + N'' + CHAR(10) + N'- **TRAP 3: Missing Auxiliary ''Be''**' + CHAR(10) + N'WRONG: "The committee reviewing the draft now." → CORRECT: "The committee is reviewing the draft now."', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (3, 1, 1, N'example', N'Recognizing the Aspect in Context', N'**Part 5 Worked Question:**' + CHAR(10) + N'' + CHAR(10) + N'"Mr. Tanaka currently ------- the quarterly budget with regional supervisors in Osaka."' + CHAR(10) + N'' + CHAR(10) + N'- **(A)** reviews — *Wrong: simple present does not match temporary ''currently'' modifier*' + CHAR(10) + N'- **(B)** is reviewing — **Correct: singular subject ''Mr. Tanaka'' + ''currently'' signals action in progress**' + CHAR(10) + N'- **(C)** reviewed — *Wrong: past tense conflicts with ongoing signal*' + CHAR(10) + N'- **(D)** reviewing — *Wrong: missing auxiliary ''is''*' + CHAR(10) + N'' + CHAR(10) + N'**Key Takeaway:** Whenever you spot **currently** or **at present** in Part 5, test for `is/are + -ing` first.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 2, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (4, 1, 1, N'micro_check', N'Check Your Understanding', N'Choose the correct option to complete the sentence.', N'The marketing team ------- new client proposals at the moment, so please do not disturb them.', N'evaluates', N'is evaluating', N'evaluated', N'evaluating', N'B', N'The time adverbial "at the moment" indicates an action in progress right now, requiring the present continuous. The collective subject "team" takes the singular verb "is evaluating".', N'(A) is simple present, (C) is simple past, and (D) is a participle lacking the required auxiliary "is".', 3, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (5, 2, 1, N'rule', N'Quy tắc cốt lõi: Sự phối hợp thì theo trục thời gian (Timeline Alignment)', N'### 1. Công thức "By the time" kinh điển trong TOEIC' + CHAR(10) + N'- **Tương lai hoàn thành**: `By the time + S + V(hiện tại đơn), S + will have + V3/ed`' + CHAR(10) + N'  *(Diễn tả một dự án/báo cáo sẽ hoàn tất trước khi một sự kiện tương lai khác diễn ra)*.' + CHAR(10) + N'- **Quá khứ hoàn thành**: `By the time + S + V(quá khứ đơn), S + had + V3/ed`' + CHAR(10) + N'  *(Diễn tả hành động đã xong trước một mốc trong quá khứ)*.' + CHAR(10) + N'' + CHAR(10) + N'### 2. Kiểm tra Thể Bị Động (Passive Voice Check)' + CHAR(10) + N'- Nếu sau chỗ trống **KHÔNG có cụm danh từ làm tân ngữ** (mà là giới từ `by`, `in`, `at`, `to` hoặc dấu chấm câu) và động từ cần điền là ngoại động từ (`submit`, `approve`, `conduct`, `finalize`), hãy ưu tiên **Thể bị động (`be + V3/ed`)**.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (6, 2, 1, N'pitfall', N'Bẫy kinh điển: Học vẹt dấu hiệu mà bỏ quên động từ mệnh đề phụ', N'Thí sinh thường phản xạ máy móc khi thấy `By the time` là chọn ngay `had + V3` mà không kiểm tra thì của mệnh đề đi kèm.' + CHAR(10) + N'' + CHAR(10) + N'- ❌ **Sai lầm phổ biến**: *By the time the auditor **arrives** next Monday, we **had prepared** all tax documents.*' + CHAR(10) + N'  *(Lỗi lệch pha thời gian: `arrives` là hiện tại chỉ tương lai nhưng mệnh đề chính lại lùi về quá khứ hoàn thành)*.' + CHAR(10) + N'' + CHAR(10) + N'- ✔️ **Chuẩn ETS**: *By the time the auditor **arrives** next Monday, we **will have prepared** all tax documents.*' + CHAR(10) + N'  *(Mệnh đề phụ chia Hiện tại đơn `arrives` -> Mệnh đề chính bắt buộc chia Tương lai hoàn thành `will have V3`)*.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (7, 2, 1, N'example', N'Phân tích ngữ cảnh thực tế trong đề thi TOEIC', N'> *"Ms. Gomez **has overseen** the regional logistics department since the company **expanded** its operations to Southeast Asia in 2022."*' + CHAR(10) + N'' + CHAR(10) + N'**Giải phẫu câu:**' + CHAR(10) + N'1. Mệnh đề sau `since` chỉ một mốc sự kiện đã xảy ra và khép lại vào năm 2022 -> Dùng **Quá khứ đơn (`expanded`)**.' + CHAR(10) + N'2. Mệnh đề chính diễn tả trách nhiệm quản lý bắt đầu từ năm 2022 và kéo dài liên tục đến hiện tại -> Dùng **Hiện tại hoàn thành (`has overseen`)**.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 2, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (8, 2, 1, N'micro_check', N'Kiểm tra nhanh: Phối hợp thì & Thể bị động', N'Áp dụng quy tắc trục thời gian và kiểm tra tân ngữ phía sau chỗ trống để chọn đáp án đúng.', N'By the time the board of directors convenes at 3:00 P.M. today, the revised financial report ------- to all shareholders.', N'distributed', N'had been distributed', N'will have been distributed', N'is distributing', N'C', N'Mệnh đề "By the time + convenes (hiện tại đơn) ... today" đòi hỏi thì Tương lai hoàn thành ở mệnh đề chính. Đồng thời chủ ngữ "the revised financial report" chịu tác động và sau chỗ trống là giới từ "to" (không có tân ngữ) nên phải chia ở thể bị động: "will have been distributed".', N'(A) Sai thì và thiếu bị động; (B) Bẫy quá khứ hoàn thành với "By the time"; (C) Đúng cả thì tương lai hoàn thành lẫn thể bị động; (D) Sai thì và sai thể chủ động.', 3, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (9, 3, 1, N'rule', N'Quy tắc cốt lõi: 3 Dạng trả lời gián tiếp (Indirect Responses) phổ biến nhất', N'Trong đề thi TOEIC hiện đại, hơn **45% câu hỏi Part 2** không trả lời trực tiếp vào từ để hỏi mà sử dụng phản hồi gián tiếp:' + CHAR(10) + N'' + CHAR(10) + N'1. **Báo không biết / Chưa quyết định**: *"I haven''t checked the schedule yet"*, *"It hasn''t been finalized"*.' + CHAR(10) + N'2. **Chuyển hướng sang người hoặc tài liệu khác**: *"Ask Ms. Carter in Accounting"*, *"Check the memo on the bulletin board"*.' + CHAR(10) + N'3. **Đưa ra lý do cản trở thay vì nói Yes/No**: Khi được hỏi *"Have you submitted the report?"*, người nghe trả lời *"The network has been down all morning"* (ngầm hiểu là *Chưa nộp*).', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (10, 3, 1, N'pitfall', N'Bẫy kinh điển: Bẫy lặp từ (Same-Word Trap) & Bẫy đồng âm (Sound-Alike Trap)', N'Nguyên tắc vàng của ETS: **Đáp án nào lặp lại nguyên xi danh từ/động từ của câu hỏi hoặc dùng từ phát âm na ná thì 90% là bẫy!**' + CHAR(10) + N'' + CHAR(10) + N'- **Câu hỏi**: *"Where did you leave the **client contract**?"*' + CHAR(10) + N'  - ❌ **Bẫy lặp từ & đồng âm**: *"Yes, my **contact** lens is on the desk."* (Lặp âm `contract` -> `contact` và dùng `Yes` cho câu hỏi `Where`).' + CHAR(10) + N'  - ✔️ **Đáp án chuẩn**: *"I gave it to Mr. Evans before lunch."* (Trả lời gián tiếp vị trí của hợp đồng bằng hành động đã đưa cho ông Evans).', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (11, 3, 1, N'example', N'Phân tích câu hỏi đề xuất "Why don''t we..." trong Part 2', N'> **Question**: *"Why don''t we interview the final candidate via video conference?"*' + CHAR(10) + N'' + CHAR(10) + N'**Phân tích phản xạ:**' + CHAR(10) + N'- Mặc dù bắt đầu bằng `Why`, đây là **lời đề nghị (Suggestion)** tương đương với `Let''s...`.' + CHAR(10) + N'- Loại bỏ ngay các phương án bắt đầu bằng `Because...`.' + CHAR(10) + N'- **Đáp án đúng**: *"That would save travel expenses."* (Đồng tình gián tiếp bằng cách nêu lợi ích của đề xuất).', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 2, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (12, 3, 1, N'micro_check', N'Kiểm tra nhanh: Nhận diện phản hồi gián tiếp', N'Đọc câu hỏi dưới đây và loại bỏ các bẫy âm thanh/lặp từ để chọn phản hồi tự nhiên nhất.', N'Question: "How much will it cost to upgrade our server capacity?" — Which response is best?', N'Yes, the server is at full capacity.', N'I''m still waiting for a quote from the vendor.', N'Along the coast of California.', N'To improve our data processing speed.', N'B', N'Câu hỏi "How much" hỏi về chi phí, nhưng người trả lời chưa có con số chính xác vì vẫn đang đợi bảng báo giá (waiting for a quote) từ nhà cung cấp — đây là phản hồi gián tiếp cực kỳ phổ biến trong TOEIC.', N'(A) Bẫy Yes/No cho câu hỏi Wh- kèm lặp từ "server" và "capacity"; (B) Đáp án đúng dạng gián tiếp; (C) Bẫy đồng âm "cost" và "coast"; (D) Bẫy trả lời cho câu hỏi mục đích "Why" thay vì "How much".', 3, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (17, 5, 1, N'rule', N'Quy tắc cốt lõi: Kỹ thuật "Cầu nối thực thể" (Entity Bridging)', N'Trong bài đọc ghép (Double/Triple Passages), câu hỏi suy luận chéo **không bao giờ** nêu trực tiếp đáp án ở một văn bản duy nhất. Hãy thực hiện 3 bước:' + CHAR(10) + N'' + CHAR(10) + N'1. **Xác định Clue A ở Văn bản 2 (Email/Review)**: Tìm mô tả gián tiếp (ví dụ: *"the item that arrived damaged"*, *"the session conducted by the CPA"*).' + CHAR(10) + N'2. **Tìm Điểm neo (Anchor/Bridge)**: Lấy tên món hàng, mã đơn, hoặc ngày tháng làm cầu nối.' + CHAR(10) + N'3. **Tra cứu Clue B ở Văn bản 1 (Invoice/Schedule)**: Đối chiếu sang cột Giá tổng (`Total`), Mã sản phẩm (`Item Code`), hoặc Điều khoản (`Policy Note`) ở cuối bảng.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (18, 5, 1, N'pitfall', N'Bẫy kinh điển: Bẫy Đơn giá vs Tổng tiền & Bỏ qua dòng chú thích (*Asterisk Note*)', N'Hai cái bẫy lấy đi nhiều điểm nhất của thí sinh ở Part 7:' + CHAR(10) + N'' + CHAR(10) + N'- ❌ **Bẫy cột Unit Price (Đơn giá)**: Khi câu hỏi hỏi *"How much will be refunded for the towels?"*, thí sinh dò đúng dòng Khăn nhưng lại lấy con số ở cột `Unit Price ($30.00)` thay vì cột `Total ($180.00 cho 6 gói)`.' + CHAR(10) + N'- ❌ **Bỏ qua dòng chữ nhỏ có dấu hoa thị (`*`) ở đáy bảng**: hơn **70% đáp án suy luận** về điều kiện miễn phí, hạn chót đổi trả hoặc yêu cầu đăng ký trước đều nằm ở dòng `*Note:` cuối bảng hoặc cuối hóa đơn!', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (19, 5, 1, N'example', N'Minh họa chuỗi suy luận chéo thực tế', N'- **Dữ kiện 1 (Trên Hóa đơn)**:' + CHAR(10) + N'  - `Delivery Date: March 6`' + CHAR(10) + N'  - `*Note: Claims submitted within 5 business days of delivery qualify for a free replacement.`' + CHAR(10) + N'- **Dữ kiện 2 (Trên Email)**:' + CHAR(10) + N'  - `Sent Date: March 7` (Tức là chỉ **1 ngày** sau khi giao hàng).' + CHAR(10) + N'- **Kết luận suy luận**: Khách hàng hoàn toàn đủ điều kiện nhận hàng thay thế miễn phí vì gửi yêu cầu trong hạn 5 ngày làm việc.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 2, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (20, 5, 1, N'micro_check', N'Kiểm tra nhanh: Thực hành suy luận chéo (Cross-Text Inference)', N'Đọc nhanh 2 trích đoạn ngắn dưới đây và trả lời câu hỏi suy luận.', N'[Menu]: "Package A ($15/person): Sandwiches & Salad | Package B ($22/person): Hot Pasta Buffet. *Orders for 20 or more people receive free delivery."' + CHAR(10) + N'[Email]: "We would like to order the Hot Pasta Buffet for our 25-member department meeting." — How much will the delivery fee be?', N'$0.00', N'$15.00', N'$22.00', N'$25.00', N'A', N'Email đặt suất ăn cho 25 người ("25-member department"). Đối chiếu với chú thích có dấu hoa thị (*) trên Menu: đơn hàng từ 20 người trở lên được miễn phí giao hàng ("free delivery"), do đó phí giao hàng là $0.00.', N'(A) Đúng nhờ kết nối số lượng 25 người với điều kiện >= 20 người ở dòng chú thích; (B) Bẫy giá của Package A; (C) Bẫy đơn giá của Package B; (D) Bẫy con số 25 thành viên trong email.', 3, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (21, 6, 2, N'rule', N'Rule', N'Listen for discourse markers such as “actually”, “instead”, and “not ... but ...”. The corrected or qualified detail often carries the answer.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (22, 6, 2, N'pitfall', N'Pitfall', N'A distractor may repeat a word from the question while giving an outdated time, location, or choice. Confirm the relationship, not just the keyword.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (23, 6, 2, N'example', N'Worked example', N'“We planned to meet at ten; actually, the lab is free at half past two” makes 2:30 the available time.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 2, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (24, 6, 2, N'micro_check', N'Micro-check', N'A speaker says, “The first room is full; instead, meet us in Room 12.” Which room is available?', N'A speaker says, “The first room is full; instead, meet us in Room 12.” Which room is available?', N'The first room', N'Room 12', N'Both rooms', N'No room is available', N'B', N'“Instead” introduces the replacement location; the first room is explicitly full.', NULL, 3, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (25, 7, 2, N'rule', N'Rule', N'Underline the idea in the question, predict a synonym, then locate evidence before choosing. For matching, verify both sides of the pair.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (26, 7, 2, N'pitfall', N'Pitfall', N'A familiar noun can appear in a sentence that contradicts the question. A shared keyword alone does not prove a match.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (27, 7, 2, N'example', N'Worked example', N'“The intervention reduced delays” can support “the measure improved punctuality” when the surrounding evidence confirms the same result.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 2, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (28, 7, 2, N'micro_check', N'Micro-check', N'The passage says “use fell from 48% to 34%”. Which paraphrase is supported?', N'The passage says “use fell from 48% to 34%”. Which paraphrase is supported?', N'Use increased to 48%', N'Use declined to 34%', N'Use remained unchanged', N'Use stopped completely', N'B', N'A fall to 34% is a decline; the other options change the direction or magnitude.', NULL, 3, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (29, 8, 2, N'rule', N'Rule', N'After the introduction, state the two or three dominant trends in an overview. Support them with selected, accurate figures and comparisons.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (30, 8, 2, N'pitfall', N'Pitfall', N'Do not describe every year separately or add causes and opinions that the chart does not provide.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (31, 8, 2, N'example', N'Worked example', N'“Solar rose from 10% to 50%, while coal fell from 60% to 25%” highlights the principal contrast with start and end values.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 2, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (32, 8, 2, N'micro_check', N'Micro-check', N'What should the Task 1 overview primarily do?', N'What should the Task 1 overview primarily do?', N'Explain why each change happened', N'List every data point', N'Summarise the main patterns and comparisons', N'Give a personal opinion', N'C', N'The overview reports prominent patterns; causes and opinions are not inferred from chart data.', NULL, 3, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (33, 9, 2, N'rule', N'Rule', N'Answer every part of the prompt, state a consistent position, and give each body paragraph one main claim supported by explanation or an example.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (34, 9, 2, N'pitfall', N'Pitfall', N'Linking words cannot repair paragraphs that contain unrelated ideas. Avoid memorised phrases that do not express a real relationship.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (35, 9, 2, N'example', N'Worked example', N'A paragraph can progress from claim → reason → specific example → link back to the question.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 2, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [LessonCards] ([CardId], [LessonId], [CertId], [CardType], [Title], [ContentMarkdown], [QuestionPrompt], [OptionA], [OptionB], [OptionC], [OptionD], [CorrectOption], [Explanation], [Rationale], [DisplayOrder], [Module]) VALUES (36, 9, 2, N'micro_check', N'Micro-check', N'Which paragraph plan best supports a clear Task 2 argument?', N'Which paragraph plan best supports a clear Task 2 argument?', N'One paragraph with several unrelated claims', N'Claim, explanation, and relevant example', N'Only a list of linking words', N'A conclusion with new arguments', N'B', N'A developed paragraph connects one claim to its reasoning and evidence.', NULL, 3, N'primary');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [LessonCards] OFF;

-- ---- Reviews (1 rows)
SET IDENTITY_INSERT [Reviews] ON;
BEGIN TRY
  INSERT INTO [Reviews] ([ReviewId], [BookingId], [UserId], [Rating], [Text], [CreatedAt]) VALUES (1, 1, 2, 5, N'Very patient and explains listening strategies clearly. Score improved significantly in 2 months.', '2026-11-07T01:44:01.8642311+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Reviews] OFF;

-- ---- CreditTransactions (6 rows)
SET IDENTITY_INSERT [CreditTransactions] ON;
BEGIN TRY
  INSERT INTO [CreditTransactions] ([TxnId], [UserId], [Reason], [Delta], [OrderId], [AttemptId], [BookingId], [AdminUserId], [Note], [CreatedAt]) VALUES (1, 2, N'purchase', 10, 1, NULL, NULL, NULL, NULL, '2026-10-17T01:44:00.6954322+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [CreditTransactions] ([TxnId], [UserId], [Reason], [Delta], [OrderId], [AttemptId], [BookingId], [AdminUserId], [Note], [CreatedAt]) VALUES (2, 2, N'purchase', 5, 2, NULL, NULL, NULL, NULL, '2026-10-24T01:44:00.7246020+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [CreditTransactions] ([TxnId], [UserId], [Reason], [Delta], [OrderId], [AttemptId], [BookingId], [AdminUserId], [Note], [CreatedAt]) VALUES (3, 2, N'mock_exam_start', -1, NULL, 2, NULL, NULL, NULL, '2026-11-08T01:44:01.7988621+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [CreditTransactions] ([TxnId], [UserId], [Reason], [Delta], [OrderId], [AttemptId], [BookingId], [AdminUserId], [Note], [CreatedAt]) VALUES (4, 2, N'mentor_booking', -1, NULL, NULL, 1, NULL, NULL, '2026-11-06T01:44:01.8642311+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [CreditTransactions] ([TxnId], [UserId], [Reason], [Delta], [OrderId], [AttemptId], [BookingId], [AdminUserId], [Note], [CreatedAt]) VALUES (5, 2, N'mentor_booking', -1, NULL, NULL, 2, NULL, NULL, '2026-11-10T01:44:01.8712985+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [CreditTransactions] ([TxnId], [UserId], [Reason], [Delta], [OrderId], [AttemptId], [BookingId], [AdminUserId], [Note], [CreatedAt]) VALUES (6, 2, N'admin_grant', 1, NULL, NULL, NULL, 1, N'Complaint make-good: audio stream stutter refund', '2026-11-09T01:44:01.8868215+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [CreditTransactions] OFF;

-- ---- Complaints (2 rows)
SET IDENTITY_INSERT [Complaints] ON;
BEGIN TRY
  INSERT INTO [Complaints] ([ComplaintId], [BookingId], [AttemptId], [OrderId], [UserId], [Type], [Reason], [Status], [Resolution], [InternalNotes], [ResolvedByUserId], [ResolvedAt], [ResolvedByGrantTxnId], [CreatedAt]) VALUES (1, NULL, 2, NULL, 2, N'technical_error', N'Audio stream froze momentarily during listening Part 4, causing a missed question.', N'resolved', N'Audio stream logs confirmed a 4-second network stutter. Granted 1 make-good credit to student wallet.', N'Verified CDN latency spike at 14:22. Compensatory credit issued.', 1, '2026-11-09T01:44:01.8883268+07:00', 6, '2026-11-09T01:44:01.8883268+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
BEGIN TRY
  INSERT INTO [Complaints] ([ComplaintId], [BookingId], [AttemptId], [OrderId], [UserId], [Type], [Reason], [Status], [Resolution], [InternalNotes], [ResolvedByUserId], [ResolvedAt], [ResolvedByGrantTxnId], [CreatedAt]) VALUES (2, 1, NULL, NULL, 2, N'mentor_quality', N'Session started 5 minutes late due to mentor connection troubles.', N'reviewing', NULL, N'Discussed with Dr. Khoa; mentor will provide 15 extra minutes on the upcoming booking.', NULL, NULL, NULL, '2026-11-10T01:44:01.8953390+07:00');
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN (2627, 2601, 547) THROW;
END CATCH
SET IDENTITY_INSERT [Complaints] OFF;

-- D23 / #224: a legacy saved link was dropped rather than re-pointed. #269 removed the QuestionId
-- column that rule was about, so there is no legacy shape left to delete.
GO

IF OBJECT_ID('CatalogMode', 'U') IS NOT NULL AND OBJECT_ID('tempdb..#ee_seed_prior_fixture') IS NOT NULL
  UPDATE CatalogMode SET IsFixtureCatalog = (SELECT Was FROM #ee_seed_prior_fixture) WHERE ModeId = 1;
GO

