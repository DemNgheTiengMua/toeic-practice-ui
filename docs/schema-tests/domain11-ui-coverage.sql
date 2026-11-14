-- docs/schema-tests/domain11-ui-coverage.sql — the UI-n additions made by the screen-vs-schema audit
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #269: the legacy content tables are gone, so each UI rule below is asserted where its fact now lives
-- and the rules that went with the tables are NAMED rather than quietly dropped:
--
--   * UI-5 "image without alt text" survives verbatim, on Stimuli — the table that carries an image
--     now (CK_Stimuli_ImageAlt). QuestionGroups.ImagePath/ImageAlt were its old home.
--   * UI-9 "an MCQ cannot carry speaking timings" survives, on ItemVersions — PrepSeconds /
--     ResponseSeconds there, and CK_Items_Modality_RenderFormat/ItemType keep an objective item from
--     being a speaking task at all. The specific old CHECK (Questions.CK_Questions_Timing) went with
--     the table; the pool's version is that an mcq item cannot hold a free-response version (proved in
--     domain46) and a reading item cannot be rendered as one (CK_Items_Modality_RenderFormat).
--   * "a per-option rationale" survives as ItemVersions.RationaleA..D, with
--     CK_ItemVersions_RationalesNeedOptions refusing one where its option is absent (domain46).
--   * "the category a question belongs to" no longer relates to an answer at all: CategoryId lived on
--     Questions, and an item names a TOPIC. UI-21's "recommendation names the learner's own attempt"
--     and every payment/mentor/complaint rule below are untouched.
--   * UI-15/16 "the remembered position is in this attempt's exam" is kept: the position is a roster id
--     now (CurrentExamItemId), and an answer records the roster position it was written against.
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash,FullName,Phone) VALUES (@r,'u@x.com',0x00,N'Nguyễn Văn A','0901234567'); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'admin@x.com',0x00); DECLARE @adm INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder,DurationMinutes) VALUES (@c,'RD',N'Reading','reading',1,75); DECLARE @rd INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'Speaking','speaking',2); DECLARE @spk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount) VALUES (@rd,@c,'reading','P5',N'Part 5',4,30); DECLARE @p5 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount) VALUES (@spk,@c,'speaking','S1',N'Q1-10',10); DECLARE @s1 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount) VALUES (@spk,@c,'speaking','S11',N'Q11',1); DECLARE @s11 INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'ETS 1','published',120); DECLARE @ex INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Placement Std','published',30,'placement'); DECLARE @pl INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Speaking 1','published',20,'speaking_practice'); DECLARE @spx INT=SCOPE_IDENTITY();

-- UI-4: duplicate exam name within a certificate
BEGIN TRY
  INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'ETS 1','draft',120);
  THROW 50000,'EXPECT_REJECT FAILED: duplicate exam name accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected duplicate exam name'; END CATCH
-- UI-5: an image without alt text
BEGIN TRY
  INSERT INTO Stimuli (CefrLevel,Passage,ImagePath) VALUES ('B1',N'A notice.',N'p1.jpg');
  THROW 50000,'EXPECT_REJECT FAILED: image without alt accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected image without alt'; END CATCH
INSERT INTO Stimuli (CefrLevel,Passage,ImagePath,ImageAlt) VALUES ('B1',N'A notice.',N'p1.jpg',N'Two people');
DECLARE @stim INT=SCOPE_IDENTITY();
-- UI-9: an MCQ cannot carry speaking timings. The pool states it as a shape: a reading item's version
-- is the four-option arm of CK_ItemVersions_OptionsComplete and its item cannot be a free response.
INSERT INTO Topics (Modality,Code,Name) VALUES ('reading','R-GRAM',N'Morphosyntactic structure and lexical precision');
DECLARE @tRead INT=(SELECT TopicId FROM Topics WHERE Modality='reading' AND Code='R-GRAM');
BEGIN TRY
  INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
    VALUES ('reading',@tRead,'B1','sentence_cloze','free_response');
  THROW 50000,'EXPECT_REJECT FAILED: an MCQ item rendered as a free response was accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Items_Modality_RenderFormat%' THROW;
  PRINT 'rejected an objective item on the productive widget, by CK_Items_Modality_RenderFormat'; END CATCH
INSERT INTO Items (StimulusId,Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES (@stim,'reading',@tRead,'B1','sentence_cloze','mcq');
DECLARE @item INT=SCOPE_IDENTITY();
-- UI-11 / #194: a per-option rationale, versioned with the option it explains
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,OptionA,OptionB,OptionC,OptionD,KeyLabel,RationaleB)
  VALUES (@item,1,N'The clerk ___ the form.',N'reviewing',N'will review',N'reviewed',N'review',N'B',
          N'V-ing cannot be the main verb');
PRINT 'accepted per-option rationale and category';
IF (SELECT RationaleB FROM ItemVersions WHERE ItemId=@item) IS NULL
  THROW 50000,'EXPECT_REJECT FAILED: the rationale was not stored with its option',1;
-- a rationale with no option to explain is refused: the same CK_ItemVersions_RationalesNeedOptions
BEGIN TRY
  INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,OptionA,OptionB,KeyLabel,RationaleC)
    VALUES (@item,2,N'Two options.',N'a',N'b',N'A',N'C is absent.');
  THROW 50000,'EXPECT_REJECT FAILED: a rationale for an absent option was accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ItemVersions_RationalesNeedOptions%' THROW;
  PRINT 'rejected a rationale with no option to explain, by CK_ItemVersions_RationalesNeedOptions'; END CATCH
DECLARE @v INT=(SELECT ItemVersionId FROM ItemVersions WHERE ItemId=@item AND VersionNumber=1);
INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
  VALUES (@ex,@p5,@c,@v,@item,'reading',1);
DECLARE @pos INT=SCOPE_IDENTITY();

-- UI-15/16: resume position must be in the attempt's exam; flags persist
-- #269: the remembered position is a ROSTER id (CurrentExamItemId), not a Questions row.
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt) VALUES (@u,@ex,@c,SYSDATETIMEOFFSET()); DECLARE @a INT=SCOPE_IDENTITY();
INSERT INTO AttemptAnswers (AttemptId,ExamId,ItemId,ItemVersionId,ExamItemId,SelectedLabel,IsCorrect,IsFlagged)
  VALUES (@a,@ex,@item,@v,@pos,'A',0,1);
IF (SELECT IsFlagged FROM AttemptAnswers WHERE AttemptId=@a AND ExamItemId=@pos) <> 1
  THROW 50000,'EXPECT_REJECT FAILED: the answer did not keep its flag',1;
-- the resume pointer is a position of this attempt's own paper, so the exam can find it
UPDATE ExamAttempts SET CurrentExamItemId=@pos WHERE AttemptId=@a;
IF (SELECT CurrentExamItemId FROM ExamAttempts WHERE AttemptId=@a) <> @pos
  THROW 50000,'EXPECT_REJECT FAILED: the resume position was not stored',1;
BEGIN TRY
  UPDATE ExamAttempts SET SubmittedAt=SYSDATETIMEOFFSET() WHERE AttemptId=@a;  -- still in_progress
  THROW 50000,'EXPECT_REJECT FAILED: SubmittedAt on in-progress attempt accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected SubmittedAt on in-progress attempt'; END CATCH
UPDATE ExamAttempts SET Status='graded', SubmittedAt=SYSDATETIMEOFFSET() WHERE AttemptId=@a;
IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId=@u AND SectionId=@p5) <> 1 THROW 50000,'exam answers missing from combined accuracy',1;
-- #271: the topic view is where a POOL answer's accuracy is reported. This answer names a pool item,
-- so the topic view must give the learner a cell for it - the mirror of what the retired category view
-- could never do. (The legacy "no cell for a paper answer" assertion went with Questions.)
IF NOT EXISTS (SELECT 1 FROM vw_UserTopicAccuracy WHERE UserId=@u AND TopicId=@tRead AND CefrLevel='B1')
  THROW 50000,'a pool attempt answer produced no topic cell',1;
PRINT 'combined accuracy views include graded exam answers';

-- UI-3: placement must point at a placement-purpose exam
BEGIN TRY
  INSERT INTO PlacementTests (UserId,CertId,ExamId) VALUES (@u,@c,@ex);
  THROW 50000,'EXPECT_REJECT FAILED: mock exam used as placement',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected mock exam as placement'; END CATCH
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt) VALUES (@u,@pl,@c,SYSDATETIMEOFFSET()); DECLARE @pa INT=SCOPE_IDENTITY();
INSERT INTO PlacementTests (UserId,CertId,ExamId,AttemptId) VALUES (@u,@c,@pl,@pa);
PRINT 'accepted placement linked to its attempt';

-- E1.3 (#148): the band arm of this trigger is gone, so a speaking-practice attempt's BandCode is no
-- longer constrained here. That is deliberate, not an oversight: the estimate belongs on
-- AiGradings.BandCode and the app leaves ExamAttempts.BandCode NULL, but the schema does not police
-- which of the two a future caller writes. TotalScore stays forbidden - one rubric is not "out of 990".
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal) VALUES (@c,'A1',N'A1',120,224);
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt,BandCode)
  VALUES (@u,@spx,@c,SYSDATETIMEOFFSET(),'A1'); DECLARE @spa INT=SCOPE_IDENTITY();
PRINT 'accepted a band on a speaking-practice attempt: the ban was dropped in #148';
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore=180 WHERE AttemptId=@spa;
  THROW 50000,'EXPECT_REJECT FAILED: total score on speaking practice accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  -- #191: this table now has a single-writer guard as well, so the refusal must be the total-score
  -- ban and not the writer guard standing in for it.
  IF ERROR_MESSAGE() NOT LIKE '%cannot carry a total score%' THROW;
  PRINT 'rejected total score on speaking practice'; END CATCH

-- UI-20: section criteria + per-section ceiling
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,MaxScore) VALUES (@c,@spk,'PRON',N'Pronunciation',5); DECLARE @pron INT=SCOPE_IDENTITY();
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,MaxScore) VALUES (@c,@spk,'COH',N'Cohesion',5); DECLARE @coh INT=SCOPE_IDENTITY();
INSERT INTO SectionCriteria (SectionId,CriterionId,SkillId,MaxScore) VALUES (@s1,@pron,@spk,3);
-- UI-36: a speaking-practice exam holds speaking sections only. #269 moved the guard from
-- QuestionGroups to the roster, which is the table the sitting actually renders.
INSERT INTO Topics (Modality,Code,Name) VALUES ('speaking','S-DESC',N'Informational description and personal framing');
DECLARE @tSpk INT=(SELECT TopicId FROM Topics WHERE Modality='speaking' AND Code='S-DESC');
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES ('speaking',@tSpk,'B1','descriptive_monologue','free_response');
DECLARE @spkItem INT=SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,PrepSeconds,ResponseSeconds)
  VALUES (@spkItem,1,N'read aloud',45,45);
DECLARE @spkV INT=SCOPE_IDENTITY();
INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
  VALUES (@spx,@s1,@c,@spkV,@spkItem,'speaking',1);
DECLARE @spos INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt,Status) VALUES (@u,@spx,@c,SYSDATETIMEOFFSET(),'submitted'); DECLARE @sa INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,ExamItemId,ExamId,SectionId,SkillId,QuestionType,AudioPath,DurationMs)
  VALUES (@sa,@spos,@spx,@s1,@spk,'free_response',N'q1.webm',27000); DECLARE @fr INT=SCOPE_IDENTITY();
INSERT INTO AiGradings (FreeResponseId,SkillId) VALUES (@fr,@spk); DECLARE @gr INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO AiGradingScores (GradingId,CriterionId,SkillId,Score) VALUES (@gr,@pron,@spk,4);
  THROW 50000,'EXPECT_REJECT FAILED: score above section ceiling accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected score above section ceiling'; END CATCH
BEGIN TRY
  INSERT INTO AiGradingScores (GradingId,CriterionId,SkillId,Score) VALUES (@gr,@coh,@spk,1);
  THROW 50000,'EXPECT_REJECT FAILED: non-applicable criterion accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected non-applicable criterion'; END CATCH
INSERT INTO AiGradingScores (GradingId,CriterionId,SkillId,Score) VALUES (@gr,@pron,@spk,3);
BEGIN TRY
  UPDATE SectionCriteria SET MaxScore=2 WHERE SectionId=@s1 AND CriterionId=@pron;
  THROW 50000,'EXPECT_REJECT FAILED: section ceiling lowered below stored score',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected lowering section ceiling'; END CATCH
PRINT 'accepted score within section ceiling';
-- UI-35: failure reason only on a failed grading; retry reuses the row
BEGIN TRY
  UPDATE AiGradings SET FailureReason=N'timeout' WHERE GradingId=@gr;   -- still pending
  THROW 50000,'EXPECT_REJECT FAILED: failure reason on pending grading accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected failure reason on non-failed grading'; END CATCH
UPDATE AiGradings SET Status='failed', FailureReason=N'provider timeout', CompletedAt=SYSDATETIMEOFFSET() WHERE GradingId=@gr;
UPDATE AiGradings SET Status='pending', FailureReason=NULL, CompletedAt=NULL, AttemptCount=AttemptCount+1 WHERE GradingId=@gr;
PRINT 'accepted failed -> retried grading';
-- UI-36: a speaking-practice exam cannot hold a reading section. The guard is on the roster now, so the
-- wrong input is a reading POSITION on the speaking-practice paper.
BEGIN TRY
  INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
    VALUES (@spx,@p5,@c,@v,@item,'reading',2);
  THROW 50000,'EXPECT_REJECT FAILED: reading position in speaking exam accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%only hold sections of that skill%' THROW;
  PRINT 'rejected reading position in speaking-practice exam'; END CATCH
BEGIN TRY
  UPDATE Exams SET Purpose='speaking_practice' WHERE ExamId=@ex;   -- @ex holds a reading position
  THROW 50000,'EXPECT_REJECT FAILED: mock with reading turned into speaking practice',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected re-purposing a mixed exam to speaking practice'; END CATCH
-- UI-21: recommendation tied to an attempt of another user
BEGIN TRY
  INSERT INTO Recommendations (UserId,CertId,AttemptId,Text) VALUES (@adm,@c,@a,N'x');
  THROW 50000,'EXPECT_REJECT FAILED: cross-user recommendation accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected cross-user recommendation'; END CATCH
INSERT INTO Recommendations (UserId,CertId,AttemptId,Text) VALUES (@u,@c,@a,N'Expand business vocabulary');

-- ===== payment / mentor / complaints =====
INSERT INTO Packages (Name,Credits,Price,IsFeatured) VALUES (N'Exam Prep',5,199000,1); DECLARE @pk INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO Packages (Name,Credits,Price,IsFeatured) VALUES (N'Intensive',10,349000,1);
  THROW 50000,'EXPECT_REJECT FAILED: second featured package accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected second featured package'; END CATCH
INSERT INTO Orders (UserId,PackageId,Status,Amount,OrderCode,PaymentNo,Gateway) VALUES (@u,@pk,'reconciling',199000,'DH-1',NEXT VALUE FOR SEQ_PaymentNo,'payos'); DECLARE @o1 INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO Orders (UserId,PackageId,Status,Amount,OrderCode,PaymentNo,Gateway) VALUES (@u,@pk,'pending',199000,'DH-2',NEXT VALUE FOR SEQ_PaymentNo,'payos');
  THROW 50000,'EXPECT_REJECT FAILED: new order while reconciling accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected new order while one is reconciling'; END CATCH
UPDATE Orders SET Status='paid', PaidAt=SYSDATETIMEOFFSET(), GatewayTxnRef='14320988' WHERE OrderId=@o1;
INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u,'purchase',5,@o1);
-- UI-37: charges must fit the exam purpose
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,AttemptId) VALUES (@u,'mock_exam_start',-1,@sa);  -- speaking practice
  THROW 50000,'EXPECT_REJECT FAILED: mock_exam_start on speaking practice accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected mock_exam_start on speaking practice'; END CATCH
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,AttemptId) VALUES (@u,'ai_grading',-1,@pa);  -- placement
  THROW 50000,'EXPECT_REJECT FAILED: charge on placement accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected charge on placement attempt'; END CATCH
INSERT INTO CreditTransactions (UserId,Reason,Delta,AttemptId) VALUES (@u,'ai_grading',-1,@sa);
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,AttemptId) VALUES (@u,'ai_grading',-1,@sa);
  THROW 50000,'EXPECT_REJECT FAILED: second ai_grading charge (retry) accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected second ai_grading charge: retry is free'; END CATCH
-- UI-32: an admin adjustment needs an actor and a note
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta) VALUES (@u,'admin_grant',1);
  THROW 50000,'EXPECT_REJECT FAILED: anonymous admin grant accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected admin grant without actor/note'; END CATCH

INSERT INTO MentorProfiles (DisplayName,CertId,MeetLink,PriceCredits) VALUES (N'Dr. Khoa',@c,N'https://meet.google.com/abc-defg-hij',2); DECLARE @m INT=SCOPE_IDENTITY();
INSERT INTO MentorSlots (MentorId,StartAt,EndAt) VALUES (@m,'2026-09-30T09:00:00+07:00','2026-09-30T10:00:00+07:00'); DECLARE @slot INT=SCOPE_IDENTITY();
INSERT INTO Bookings (UserId,MentorId,SlotId,PriceCredits,Status,FocusArea) VALUES (@u,@m,@slot,2,'pending',N'Listening'); DECLARE @bk INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,BookingId) VALUES (@u,'mentor_booking',-1,@bk);
  THROW 50000,'EXPECT_REJECT FAILED: booking charge below price accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected booking charge not matching PriceCredits'; END CATCH
INSERT INTO CreditTransactions (UserId,Reason,Delta,BookingId) VALUES (@u,'mentor_booking',-2,@bk);
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,BookingId) VALUES (@u,'booking_cancel',2,@bk);
  THROW 50000,'EXPECT_REJECT FAILED: refund of a live booking accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected refund while booking is live'; END CATCH
UPDATE Bookings SET Status='cancelled', CancelledAt=SYSDATETIMEOFFSET() WHERE BookingId=@bk;
INSERT INTO CreditTransactions (UserId,Reason,Delta,BookingId) VALUES (@u,'booking_cancel',2,@bk);
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,BookingId) VALUES (@u,'booking_cancel',2,@bk);
  THROW 50000,'EXPECT_REJECT FAILED: double refund accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected double refund'; END CATCH
DECLARE @bal INT, @bought INT, @used INT;
SELECT @bal=Balance, @bought=CreditsPurchased, @used=CreditsUsed FROM vw_UserCreditSummary WHERE UserId=@u;
-- 5 bought; 1 spent on speaking grading; the 2-credit booking was refunded so it does not count as used
IF @bal<>4 OR @bought<>5 OR @used<>1 THROW 50000,'credit summary wrong after refund',1;
PRINT 'credit summary: 5 bought, 1 used, refunded booking not counted';

-- UI-28: complaints about an exam and an order; subject must match type and owner
INSERT INTO Complaints (AttemptId,UserId,Type,Reason) VALUES (@a,@u,'grading_issue',N'Q137 wrong key');
INSERT INTO Complaints (OrderId,UserId,Type,Reason) VALUES (@o1,@u,'payment_issue',N'charged twice');
BEGIN TRY
  INSERT INTO Complaints (OrderId,UserId,Type,Reason) VALUES (@o1,@u,'mentor_no_show',N'x');
  THROW 50000,'EXPECT_REJECT FAILED: type/subject mismatch accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected complaint type/subject mismatch'; END CATCH
BEGIN TRY
  INSERT INTO Complaints (OrderId,AttemptId,UserId,Type,Reason) VALUES (@o1,@a,@u,'payment_issue',N'x');
  THROW 50000,'EXPECT_REJECT FAILED: two subjects accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected complaint with two subjects'; END CATCH
BEGIN TRY
  INSERT INTO Complaints (OrderId,UserId,Type,Reason) VALUES (@o1,@adm,'payment_issue',N'not mine');
  THROW 50000,'EXPECT_REJECT FAILED: complaint on another user''s order accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected complaint on another user''s order'; END CATCH
-- UI-25 / UI-12: an in-progress path step, with a practice session running against it.
-- #232: the step is a coordinate - it names its skill, topic, level, type and size, and carries no
-- module item. The practice session links to the step by id, which is all UI-12 ever needed.
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal) VALUES (@c,'B2',N'B2',785,944);
INSERT INTO Topics (Modality,Code,Name) VALUES ('reading','R-DET',N'Detail');
DECLARE @tTime INT=(SELECT TopicId FROM Topics WHERE Modality='reading' AND Code='R-DET');
DECLARE @plc INT=(SELECT PlacementId FROM PlacementTests WHERE AttemptId=@pa);
INSERT INTO LearnerPaths (UserId,CertId,PlacementId) VALUES (@u,@c,@plc); DECLARE @lp INT=SCOPE_IDENTITY();
INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,Status,StartedAt,WeekNumber,WeekSequence)
  VALUES (@lp,@c,@rd,'reading',@tTime,'B1','focus_practice','developing','in_progress',SYSDATETIMEOFFSET(),1,1); DECLARE @st INT=SCOPE_IDENTITY();
INSERT INTO PracticeSessions (UserId,CertId,SectionId,Modality,QuestionCount,QuestionSource,LearnerPathStepId) VALUES (@u,@c,@p5,'reading',25,'wrong_before',@st);
-- UI-13: a saved link names the item and the version it was on screen at. #269 dropped the legacy
-- question shape with the table, so the bank's only shape is the pool one.
INSERT INTO SavedQuestions (UserId,CertId,ItemId,ItemVersionId,Source) VALUES (@u,@c,@item,@v,'incorrect');
PRINT 'accepted in-progress module step with linked practice session';
PRINT 'DOMAIN 11 OK';
