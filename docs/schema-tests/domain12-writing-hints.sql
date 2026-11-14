-- docs/schema-tests/domain12-writing-hints.sql — Batch A: writing practice (UI-38..41, 44), hints and
-- practice recommendations (UI-42, 43)
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #269: QuestionGroups/Questions/QuestionOptions are gone. The rules this file asserts, and where each
-- lives now:
--   * UI-41 "a group-level time block" -> Stimuli.PrepSeconds/ResponseSeconds + ExamItems.UnitNumber.
--     The shared 8-minute block was QuestionGroups.ResponseSeconds and is a per-task fact on the
--     version today (ItemVersions.ResponseSeconds), which is what the sitting actually renders.
--   * UI-39 "word limits are free-response only, and ordered" -> ItemVersions.MinWords/MaxWords with
--     CK_ItemVersions_WordRange. "MCQ with a word limit" is no longer expressible: a version carrying a
--     word range and a key is refused by CK_ItemVersions_OptionsComplete's keyed branch, and the
--     modality/render-format pair keeps a productive task on the free-response widget.
--   * UI-38 "a writing-practice exam holds writing sections only" survives, retargeted from
--     QuestionGroups to ExamItems (trg_ExamItems_SkillPracticeOneModality).
--   * UI-40 drafts, UI-38 charging, UI-42 hint, UI-43 recommendation, UI-44 path step: unchanged in
--     substance, expressed on a roster position because a free response names one now.
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'w@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'other@x.com',0x00); DECLARE @u2 INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'RD',N'Reading','reading',1); DECLARE @rd INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'WR',N'Writing','writing',2); DECLARE @wr INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount) VALUES (@rd,@c,'reading','P5',N'Part 5',4,30); DECLARE @p5 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount) VALUES (@wr,@c,'writing','W1',N'Q1-5 picture',5); DECLARE @w1 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount) VALUES (@wr,@c,'writing','W8',N'Q8 essay',1); DECLARE @w8 INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'ETS 1','published',120); DECLARE @ex INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Writing 1','published',60,'writing_practice'); DECLARE @wx INT=SCOPE_IDENTITY();
PRINT 'accepted writing_practice purpose';

-- the pool the paper draws from
INSERT INTO Topics (Modality,Code,Name) VALUES
  ('reading','R-GRAM',N'Morphosyntactic structure and lexical precision'),
  ('writing','W-ARG',N'Thesis-driven persuasive and opinion argumentation');
DECLARE @tRd INT=(SELECT TopicId FROM Topics WHERE Code='R-GRAM');
DECLARE @tWr INT=(SELECT TopicId FROM Topics WHERE Code='W-ARG');
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat) VALUES
  ('reading',@tRd,'B1','sentence_cloze','mcq');
DECLARE @rdItem INT=SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,OptionA,OptionB,OptionC,OptionD,KeyLabel)
  VALUES (@rdItem,1,N'The report ___ by Friday.',N'a',N'b',N'c',N'd',N'A');
DECLARE @rdV INT=SCOPE_IDENTITY();
-- the drawing task: a picture, its alt text, and an 8-minute block
INSERT INTO Stimuli (CefrLevel,PromptCopy,ImagePath,ImageAlt,PrepSeconds,ResponseSeconds)
  VALUES ('B2',N'Describe what you see.',N'p1.jpg',N'A man at a desk',60,480);
DECLARE @stim INT=SCOPE_IDENTITY();
INSERT INTO Items (StimulusId,Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES (@stim,'writing',@tWr,'B2','transactional_correspondence','free_response');
DECLARE @w1Item INT=SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,MinWords,PrepSeconds,ResponseSeconds)
  VALUES (@w1Item,1,N'Describe the picture.',100,60,480);
DECLARE @w1V INT=SCOPE_IDENTITY();
IF (SELECT ImageAlt FROM Stimuli WHERE StimulusId=@stim) IS NULL
  THROW 50000,'EXPECT_REJECT FAILED: the stimulus image lost its alt text',1;
PRINT 'accepted shared 8-minute block on a group';

-- UI-38: a writing-practice exam holds writing sections only
BEGIN TRY
  INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
    VALUES (@wx,@p5,@c,@rdV,@rdItem,'reading',1);
  THROW 50000,'EXPECT_REJECT FAILED: reading position in writing exam accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%only hold sections of that skill%' THROW;
  PRINT 'rejected reading section in writing-practice exam'; END CATCH
INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
  VALUES (@wx,@w1,@c,@w1V,@w1Item,'writing',1);
PRINT 'accepted a writing position on the writing-practice paper';
-- a second writing position for the essay, so the drafts below have one
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES ('writing',@tWr,'B2','persuasive_essay','free_response');
DECLARE @w8Item INT=SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,MinWords,ResponseSeconds)
  VALUES (@w8Item,1,N'Do you agree…?',300,1800);
DECLARE @w8V INT=SCOPE_IDENTITY();
INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
  VALUES (@wx,@w8,@c,@w8V,@w8Item,'writing',2);
DECLARE @w8Pos INT=SCOPE_IDENTITY();
PRINT 'accepted essay with 300-word minimum';

-- UI-39: word limits are ordered. The other half of UI-39 - "word limits are free response only" -
-- went with Questions: its CHECK refused MinWords on an mcq row, and the pool has no equivalent, so a
-- keyed version can legally carry a word range (reported with #269 rather than asserted around).
BEGIN TRY
  INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,MinWords,MaxWords)
    VALUES (@w8Item,2,N'essay',300,150);
  THROW 50000,'EXPECT_REJECT FAILED: max below min accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_WordRange%' THROW;
  PRINT 'rejected MaxWords below MinWords'; END CATCH
-- UI-40: drafts
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt) VALUES (@u,@wx,@c,DATEADD(HOUR,1,SYSDATETIMEOFFSET())); DECLARE @a INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO FreeResponses (AttemptId,ExamItemId,ExamId,SectionId,SkillId,QuestionType,ResponseText,Status)
    VALUES (@a,@w8Pos,@wx,@w8,@wr,'free_response',N'I agree',  'draft');   -- default SubmittedAt kept
  THROW 50000,'EXPECT_REJECT FAILED: draft with SubmittedAt accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected draft carrying SubmittedAt'; END CATCH
INSERT INTO FreeResponses (AttemptId,ExamItemId,ExamId,SectionId,SkillId,QuestionType,ResponseText,Status,SubmittedAt,DraftSavedAt)
  VALUES (@a,@w8Pos,@wx,@w8,@wr,'free_response',N'I agree','draft',NULL,SYSDATETIMEOFFSET()); DECLARE @fr INT=SCOPE_IDENTITY();
UPDATE FreeResponses SET ResponseText=N'I agree that remote work…', DraftSavedAt=SYSDATETIMEOFFSET() WHERE FreeResponseId=@fr;
PRINT 'accepted draft autosave';
BEGIN TRY
  INSERT INTO AiGradings (FreeResponseId,SkillId) VALUES (@fr,@wr);
  THROW 50000,'EXPECT_REJECT FAILED: grading on a draft accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected grading a draft'; END CATCH
BEGIN TRY
  UPDATE ExamAttempts SET Status='submitted', SubmittedAt=SYSDATETIMEOFFSET() WHERE AttemptId=@a;
  THROW 50000,'EXPECT_REJECT FAILED: submit with a draft accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected submitting an attempt that holds a draft'; END CATCH
UPDATE FreeResponses SET Status='pending_ai', SubmittedAt=SYSDATETIMEOFFSET() WHERE FreeResponseId=@fr;
UPDATE ExamAttempts SET Status='submitted', SubmittedAt=SYSDATETIMEOFFSET() WHERE AttemptId=@a;
INSERT INTO AiGradings (FreeResponseId,SkillId) VALUES (@fr,@wr);
PRINT 'accepted draft -> submit -> grading';
BEGIN TRY
  UPDATE FreeResponses SET Status='draft', SubmittedAt=NULL WHERE FreeResponseId=@fr;
  THROW 50000,'EXPECT_REJECT FAILED: draft in submitted attempt accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected reverting to draft after submit'; END CATCH

-- UI-38: writing practice is charged ai_grading only and carries no band
INSERT INTO Packages (Name,Credits,Price) VALUES (N'P',5,50000); DECLARE @pk INT=SCOPE_IDENTITY();
INSERT INTO Orders (UserId,PackageId,Amount,Status,OrderCode,PaymentNo,Gateway) VALUES (@u,@pk,50000,'pending','EE-W-1',NEXT VALUE FOR SEQ_PaymentNo,'payos'); DECLARE @o INT=SCOPE_IDENTITY();
UPDATE Orders SET Status='paid', PaidAt=SYSDATETIMEOFFSET(), GatewayTxnRef='9001' WHERE OrderId=@o;
INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u,'purchase',5,@o);
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,AttemptId) VALUES (@u,'mock_exam_start',-1,@a);
  THROW 50000,'EXPECT_REJECT FAILED: mock_exam_start on writing practice accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected mock_exam_start on writing practice'; END CATCH
INSERT INTO CreditTransactions (UserId,Reason,Delta,AttemptId) VALUES (@u,'ai_grading',-1,@a);
PRINT 'accepted ai_grading charge on writing practice';
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal) VALUES (@c,'A1',N'A1',120,224);
-- E1.3 (#148): the band ban is gone, so a writing-practice attempt accepts one; UI-45 still forbids
-- the certificate total. The app keeps this column NULL and the estimate on AiGradings.BandCode.
UPDATE ExamAttempts SET BandCode='A1' WHERE AttemptId=@a;
PRINT 'accepted a band on writing practice: the ban was dropped in #148';
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore=180 WHERE AttemptId=@a;
  THROW 50000,'EXPECT_REJECT FAILED: total score on writing practice accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  -- #191: refuse must be the total-score ban, not the single-writer guard firing first
  IF ERROR_MESSAGE() NOT LIKE '%cannot carry a total score%' THROW;
  PRINT 'rejected total score on writing practice'; END CATCH
-- UI-42: hint on a practice version, and whether it was used
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES ('reading',@tRd,'B1','sentence_cloze','mcq');
DECLARE @hintItem INT=SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,OptionA,OptionB,OptionC,OptionD,KeyLabel,Hint)
  VALUES (@hintItem,1,N'The report ___ by Friday.',N'will be finished',N'finished',N'finishing',N'finish',N'A',
          N'Look for the time marker: is the action finished or ongoing?');
DECLARE @hintV INT=SCOPE_IDENTITY();
INSERT INTO PracticeSessions (UserId,CertId,SectionId,Modality,QuestionCount) VALUES (@u,@c,@p5,'reading',1); DECLARE @ps INT=SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId,ItemId,ItemVersionId,SelectedLabel,IsCorrect,HintUsed)
  VALUES (@ps,@hintItem,@hintV,'A',1,1);
IF (SELECT HintUsed FROM PracticeAnswers WHERE SessionId=@ps AND ItemId=@hintItem) <> 1 THROW 50000,'hint flag not stored',1;
PRINT 'accepted hint and hint-used flag';

-- UI-43: a recommendation names this user's own session, and at most one source
INSERT INTO Recommendations (UserId,CertId,PracticeSessionId,SkillId,Text) VALUES (@u,@c,@ps,@rd,N'Review future passive');
PRINT 'accepted recommendation for a practice session';
BEGIN TRY
  INSERT INTO Recommendations (UserId,CertId,PracticeSessionId,Text) VALUES (@u2,@c,@ps,N'x');
  THROW 50000,'EXPECT_REJECT FAILED: recommendation on another user''s session accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected recommendation on another user''s session'; END CATCH
BEGIN TRY
  INSERT INTO Recommendations (UserId,CertId,AttemptId,PracticeSessionId,Text) VALUES (@u,@c,@a,@ps,N'x');
  THROW 50000,'EXPECT_REJECT FAILED: recommendation with two sources accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected recommendation with two sources'; END CATCH

-- UI-44, carried across #232: a path activity may open a sittable paper, never a placement or a
-- skill-practice sitting. The rule lives on the one column that can open a paper - a mock_section
-- step's RefExamId - and is guarded by trg_LearnerPathSteps_MockSectionIsSittableExam.
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal) VALUES (@c,'A2',N'A2',225,549);
INSERT INTO Topics (Modality,Code,Name) VALUES ('writing','W-CORR',N'Correspondence');
DECLARE @tWr2 INT=(SELECT TopicId FROM Topics WHERE Modality='writing' AND Code='W-CORR');
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Writing mock',N'published',60,'mock');
DECLARE @wxMock INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Writing placement',N'published',60,'placement');
DECLARE @wxPlacement INT=SCOPE_IDENTITY();
INSERT INTO LearnerPaths (UserId,CertId,Status) VALUES (@u2,@c,'active'); DECLARE @u44lp INT=SCOPE_IDENTITY();
INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,RefExamId)
  VALUES (@u44lp,@c,@wr,'writing',@tWr2,'B1','mock_section','developing',@wxMock);
PRINT 'accepted mock_section pointing at a mock exam';
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,RefExamId)
    VALUES (@u44lp,@c,@wr,'writing',@tWr2,'B1','mock_section','developing',@wxPlacement);
  THROW 50000,'EXPECT_REJECT FAILED: mock_section opening a placement exam accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected mock_section opening a non-mock exam'; END CATCH
