-- docs/schema-tests/domain12-writing-hints.sql — Batch A: writing practice (UI-38..41, 44), hints and
-- practice recommendations (UI-42, 43)
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'w@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'other@x.com',0x00); DECLARE @u2 INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'RD',N'Reading','reading',1); DECLARE @rd INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'WR',N'Writing','writing',2); DECLARE @wr INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount) VALUES (@rd,@c,'reading','P5',N'Part 5',4); DECLARE @p5 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name) VALUES (@wr,@c,'writing','W1',N'Q1-5 picture'); DECLARE @w1 INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name) VALUES (@wr,@c,'writing','W8',N'Q8 essay'); DECLARE @w8 INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'ETS 1','published',120); DECLARE @ex INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Writing 1','published',60,'writing_practice'); DECLARE @wx INT=SCOPE_IDENTITY();
PRINT 'accepted writing_practice purpose';

-- UI-38: a writing-practice exam holds writing sections only
BEGIN TRY
  INSERT INTO QuestionGroups (ExamId,CertId,SectionId) VALUES (@wx,@c,@p5);
  THROW 50000,'EXPECT_REJECT FAILED: reading group in writing exam accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected reading section in writing-practice exam'; END CATCH
-- UI-41: a group-level time block
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,ImagePath,ImageAlt,ResponseSeconds) VALUES (@wx,@c,@w1,N'p1.jpg',N'A man at a desk',480); DECLARE @g1 INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId) VALUES (@wx,@c,@w8); DECLARE @g8 INT=SCOPE_IDENTITY();
PRINT 'accepted shared 8-minute block on a group';

-- UI-39: word limits are free response only, and ordered
BEGIN TRY
  INSERT INTO QuestionGroups (ExamId,CertId,SectionId) VALUES (@ex,@c,@p5);
  INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,MinWords)
    VALUES (SCOPE_IDENTITY(),@ex,@p5,@rd,N'x',2,'mcq',10);
  THROW 50000,'EXPECT_REJECT FAILED: MCQ with word limit accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected MCQ with word limit'; END CATCH
BEGIN TRY
  INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,MinWords,MaxWords)
    VALUES (@g8,@wx,@w8,@wr,N'essay',3,'free_response',300,150);
  THROW 50000,'EXPECT_REJECT FAILED: max below min accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected MaxWords below MinWords'; END CATCH
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,MinWords,ResponseSeconds)
  VALUES (@g8,@wx,@w8,@wr,N'Do you agree…?',3,'free_response',300,1800); DECLARE @q8 INT=SCOPE_IDENTITY();
PRINT 'accepted essay with 300-word minimum';
-- UI-40: drafts
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt) VALUES (@u,@wx,@c,DATEADD(HOUR,1,SYSDATETIMEOFFSET())); DECLARE @a INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,SkillId,QuestionType,ResponseText,Status)
    VALUES (@a,@q8,@wx,@wr,'free_response',N'I agree',  'draft');   -- default SubmittedAt kept
  THROW 50000,'EXPECT_REJECT FAILED: draft with SubmittedAt accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected draft carrying SubmittedAt'; END CATCH
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,SkillId,QuestionType,ResponseText,Status,SubmittedAt,DraftSavedAt)
  VALUES (@a,@q8,@wx,@wr,'free_response',N'I agree','draft',NULL,SYSDATETIMEOFFSET()); DECLARE @fr INT=SCOPE_IDENTITY();
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
BEGIN TRY
  UPDATE ExamAttempts SET BandCode='A1' WHERE AttemptId=@a;
  THROW 50000,'EXPECT_REJECT FAILED: band on writing practice accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected band on writing practice'; END CATCH
-- UI-42: hint on a practice question, and whether it was used
INSERT INTO QuestionGroups (ExamId,CertId,SectionId) VALUES (@ex,@c,@p5); DECLARE @gp INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,Hint)
  VALUES (@gp,@ex,@p5,@rd,N'The report ___ by Friday.',2,'mcq',N'Look for the time marker: is the action finished or ongoing?'); DECLARE @qh INT=SCOPE_IDENTITY();
INSERT INTO QuestionOptions (QuestionId,QuestionType,Label,Text,IsCorrect) VALUES (@qh,'mcq','A',N'will be finished',1); DECLARE @oa INT=SCOPE_IDENTITY();
INSERT INTO PracticeSessions (UserId,SectionId,Modality,QuestionCount) VALUES (@u,@p5,'reading',1); DECLARE @ps INT=SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId,QuestionId,SectionId,SelectedOptionId,IsCorrect,HintUsed)
  VALUES (@ps,@qh,@p5,@oa,1,1);
IF (SELECT HintUsed FROM PracticeAnswers WHERE SessionId=@ps AND QuestionId=@qh) <> 1 THROW 50000,'hint flag not stored',1;
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

-- UI-44: an AI task on the path may open a writing-practice exam; a mentor item still may not
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal) VALUES (@c,'A2',N'A2',225,549);
INSERT INTO PathTemplates (CertId,FromBandCode,ToBandCode,Name) VALUES (@c,'A1','A2',N'Starter'); DECLARE @tpl INT=SCOPE_IDENTITY();
INSERT INTO PathModules (TemplateId,SkillId,CertId,DisplayOrder,Title) VALUES (@tpl,@wr,@c,1,N'Writing'); DECLARE @mod INT=SCOPE_IDENTITY();
INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefExamId,DisplayOrder) VALUES (@mod,@c,@tpl,'ai_task',@wx,1);
PRINT 'accepted ai_task item opening a writing-practice exam';
BEGIN TRY
  INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefExamId,DisplayOrder) VALUES (@mod,@c,@tpl,'mentor',@wx,2);
  THROW 50000,'EXPECT_REJECT FAILED: mentor item with exam accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected mentor item pointing at an exam'; END CATCH
