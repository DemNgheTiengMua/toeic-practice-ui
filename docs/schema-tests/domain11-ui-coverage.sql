-- docs/schema-tests/domain11-ui-coverage.sql — the UI-n additions made by the screen-vs-schema audit
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash,FullName,Phone) VALUES (@r,'u@x.com',0x00,N'Nguyễn Văn A','0901234567'); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'admin@x.com',0x00); DECLARE @adm INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder,DurationMinutes) VALUES (@c,'RD',N'Reading','reading',1,75); DECLARE @rd INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'Speaking','speaking',2); DECLARE @spk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount) VALUES (@rd,@c,'reading','P5',N'Part 5',4,30); DECLARE @p5 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name) VALUES (@spk,@c,'speaking','S1',N'Q1-10'); DECLARE @s1 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name) VALUES (@spk,@c,'speaking','S11',N'Q11'); DECLARE @s11 INT=SCOPE_IDENTITY();
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
  INSERT INTO QuestionGroups (ExamId,CertId,SectionId,ImagePath) VALUES (@ex,@c,@p5,N'p1.jpg');
  THROW 50000,'EXPECT_REJECT FAILED: image without alt accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected image without alt'; END CATCH
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,ImagePath,ImageAlt) VALUES (@ex,@c,@p5,N'p1.jpg',N'Two people'); DECLARE @g INT=SCOPE_IDENTITY();
INSERT INTO QuestionCategories (CertId,Code,Name) VALUES (@c,'verb_tense',N'Verb tense'); DECLARE @cat INT=SCOPE_IDENTITY();
-- UI-9: an MCQ cannot carry speaking timings
BEGIN TRY
  INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,PrepSeconds) VALUES (@g,@ex,@p5,@rd,N'x',2,'mcq',45);
  THROW 50000,'EXPECT_REJECT FAILED: MCQ with timing accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected MCQ with timing'; END CATCH
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,CategoryId) VALUES (@g,@ex,@p5,@rd,N'q7',2,'mcq',@cat); DECLARE @q INT=SCOPE_IDENTITY();
INSERT INTO QuestionOptions (QuestionId,QuestionType,Label,Text,IsCorrect,Rationale) VALUES (@q,'mcq','B',N'reviewing',0,N'V-ing cannot be the main verb'); DECLARE @optB INT=SCOPE_IDENTITY();
INSERT INTO QuestionOptions (QuestionId,QuestionType,Label,Text,IsCorrect) VALUES (@q,'mcq','C',N'will review',1);
PRINT 'accepted per-option rationale and category';

-- UI-15/16: resume position must be in the attempt's exam; flags persist
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt,LastQuestionId) VALUES (@u,@ex,@c,SYSDATETIMEOFFSET(),@q); DECLARE @a INT=SCOPE_IDENTITY();
INSERT INTO AttemptAnswers (AttemptId,QuestionId,ExamId,SelectedOptionId,IsCorrect,IsFlagged) VALUES (@a,@q,@ex,@optB,0,1);
BEGIN TRY
  UPDATE ExamAttempts SET SubmittedAt=SYSDATETIMEOFFSET() WHERE AttemptId=@a;  -- still in_progress
  THROW 50000,'EXPECT_REJECT FAILED: SubmittedAt on in-progress attempt accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected SubmittedAt on in-progress attempt'; END CATCH
UPDATE ExamAttempts SET Status='graded', SubmittedAt=SYSDATETIMEOFFSET() WHERE AttemptId=@a;
IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId=@u AND SectionId=@p5) <> 1 THROW 50000,'exam answers missing from combined accuracy',1;
IF (SELECT Answered FROM vw_UserCategoryAccuracy WHERE UserId=@u AND CategoryId=@cat) <> 1 THROW 50000,'category accuracy wrong',1;
PRINT 'combined accuracy views include graded exam answers';

-- UI-3: placement must point at a placement-purpose exam
BEGIN TRY
  INSERT INTO PlacementTests (UserId,CertId,ExamId) VALUES (@u,@c,@ex);
  THROW 50000,'EXPECT_REJECT FAILED: mock exam used as placement',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected mock exam as placement'; END CATCH
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt) VALUES (@u,@pl,@c,SYSDATETIMEOFFSET()); DECLARE @pa INT=SCOPE_IDENTITY();
INSERT INTO PlacementTests (UserId,CertId,ExamId,AttemptId) VALUES (@u,@c,@pl,@pa);
PRINT 'accepted placement linked to its attempt';

-- UI-19: no band on a speaking-practice attempt
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal) VALUES (@c,'A1',N'A1',120,224);
BEGIN TRY
  INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt,BandCode) VALUES (@u,@spx,@c,SYSDATETIMEOFFSET(),'A1');
  THROW 50000,'EXPECT_REJECT FAILED: band on speaking practice accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected band on speaking practice'; END CATCH

-- UI-20: section criteria + per-section ceiling
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,MaxScore) VALUES (@c,@spk,'PRON',N'Pronunciation',5); DECLARE @pron INT=SCOPE_IDENTITY();
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,MaxScore) VALUES (@c,@spk,'COH',N'Cohesion',5); DECLARE @coh INT=SCOPE_IDENTITY();
INSERT INTO SectionCriteria (SectionId,CriterionId,SkillId,MaxScore) VALUES (@s1,@pron,@spk,3);
INSERT INTO QuestionGroups (ExamId,CertId,SectionId) VALUES (@spx,@c,@s1); DECLARE @sg INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,PrepSeconds,ResponseSeconds) VALUES (@sg,@spx,@s1,@spk,N'read aloud',2,'free_response',45,45); DECLARE @sq INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt,Status) VALUES (@u,@spx,@c,SYSDATETIMEOFFSET(),'submitted'); DECLARE @sa INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,SkillId,QuestionType,AudioPath,DurationMs) VALUES (@sa,@sq,@spx,@spk,'free_response',N'q1.webm',27000); DECLARE @fr INT=SCOPE_IDENTITY();
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
-- UI-36: a speaking-practice exam cannot hold a reading section
BEGIN TRY
  INSERT INTO QuestionGroups (ExamId,CertId,SectionId) VALUES (@spx,@c,@p5);
  THROW 50000,'EXPECT_REJECT FAILED: reading group in speaking exam accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected reading section in speaking-practice exam'; END CATCH
BEGIN TRY
  UPDATE Exams SET Purpose='speaking_practice' WHERE ExamId=@ex;   -- @ex holds a reading group
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
INSERT INTO Orders (UserId,PackageId,Status,Amount,OrderCode,Gateway) VALUES (@u,@pk,'reconciling',199000,'DH-1','momo'); DECLARE @o1 INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO Orders (UserId,PackageId,Status,Amount,OrderCode,Gateway) VALUES (@u,@pk,'pending',199000,'DH-2','vnpay');
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

INSERT INTO MentorProfiles (UserId,DisplayName,CertId,Type,PricePerSlot,PriceCredits) VALUES (@adm,N'Dr. Khoa',@c,'human',50000,2); DECLARE @m INT=SCOPE_IDENTITY();
INSERT INTO MentorSlots (MentorId,StartAt,EndAt) VALUES (@m,'2026-09-30T09:00:00+07:00','2026-09-30T10:00:00+07:00'); DECLARE @slot INT=SCOPE_IDENTITY();
INSERT INTO Bookings (UserId,MentorId,SlotId,MentorType,Status,FocusArea) VALUES (@u,@m,@slot,'human','pending',N'Listening'); DECLARE @bk INT=SCOPE_IDENTITY();
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
-- UI-25 / UI-12: an in-progress module step, with a practice session running against it
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal) VALUES (@c,'B2',N'B2',785,944);
INSERT INTO PathTemplates (CertId,FromBandCode,ToBandCode,Name) VALUES (@c,'A1','B2',N'A1→B2'); DECLARE @tpl INT=SCOPE_IDENTITY();
INSERT INTO PathModules (TemplateId,SkillId,CertId,Title,GroupName,EstimatedMinutes) VALUES (@tpl,@rd,@c,N'Present Continuous',N'Grammar foundations',20); DECLARE @mod INT=SCOPE_IDENTITY();
INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefSectionId,QuestionCount) VALUES (@mod,@c,@tpl,'practice_section',@p5,25); DECLARE @it INT=SCOPE_IDENTITY();
INSERT INTO PathModuleTopics (ModuleId,Title,CategoryId,QuestionCount) VALUES (@mod,N'Time expressions',@cat,5);
DECLARE @plc INT=(SELECT PlacementId FROM PlacementTests WHERE AttemptId=@pa);
INSERT INTO LearnerPaths (UserId,CertId,TemplateId,PlacementId) VALUES (@u,@c,@tpl,@plc); DECLARE @lp INT=SCOPE_IDENTITY();
INSERT INTO LearnerPathSteps (LearnerPathId,PathModuleItemId,TemplateId,Status,StartedAt) VALUES (@lp,@it,@tpl,'in_progress',SYSDATETIMEOFFSET()); DECLARE @st INT=SCOPE_IDENTITY();
INSERT INTO PracticeSessions (UserId,SectionId,Modality,QuestionCount,QuestionSource,FeedbackMode,LearnerPathStepId) VALUES (@u,@p5,'reading',25,'wrong_before','end',@st);
INSERT INTO SavedQuestions (UserId,QuestionId,Source) VALUES (@u,@q,'incorrect');
PRINT 'accepted in-progress module step with linked practice session';
PRINT 'DOMAIN 11 OK';
