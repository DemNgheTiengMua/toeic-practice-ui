-- UI-45: a writing-practice attempt cannot carry a band (UI-38) or a total score (writing-review
-- shows rubric scores only, never "out of 990")
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'s@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality) VALUES (@c,'LS',N'Listening','listening'); DECLARE @ls INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality) VALUES (@c,'RD',N'Reading','reading'); DECLARE @rd INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality) VALUES (@c,'WR',N'Writing','writing'); DECLARE @wr INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount) VALUES (@wr,@c,'writing','W8',N'Q8 essay',1); DECLARE @w8 INT=SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal) VALUES (@c,'A2',N'A2',225,549);
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Writing 1','published',60,'writing_practice'); DECLARE @wx INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt) VALUES (@u,@wx,@c,DATEADD(HOUR,1,SYSDATETIMEOFFSET())); DECLARE @a INT=SCOPE_IDENTITY();
BEGIN TRY
  UPDATE ExamAttempts SET Status='graded', SubmittedAt=SYSDATETIMEOFFSET(), TotalScore=180,
    WrittenByService='AttemptEngine', ProjectionVersion=1 WHERE AttemptId=@a;
  THROW 50000,'EXPECT_REJECT FAILED: total score on writing practice accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  -- #191: the refusal must be the total-score ban, not the single-writer guard firing first
  IF ERROR_MESSAGE() NOT LIKE '%cannot carry a total score%' THROW;
  PRINT 'rejected TotalScore on a writing-practice attempt'; END CATCH

-- UI-45: a placement exam can be the standard (40-question) or the quick (20-question) variant
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose,QuestionCount) VALUES (@c,N'Placement quick','published',15,'placement',20);
PRINT 'accepted placement variants';
BEGIN TRY
  INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose,QuestionCount) VALUES (@c,N'Bad','published',30,'placement',0);
  THROW 50000,'EXPECT_REJECT FAILED: zero-question exam accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected zero QuestionCount'; END CATCH

-- UI-46: a placement result is a snapshot of the attempt's skill scores
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose,QuestionCount) VALUES (@c,N'Placement standard','published',30,'placement',40); DECLARE @ps INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt) VALUES (@u,@ps,@c,'submitted',DATEADD(HOUR,-1,SYSDATETIMEOFFSET())); DECLARE @pa INT=SCOPE_IDENTITY();
INSERT INTO AttemptSkillScores (AttemptId,SkillId,CertId,RawScore,ScaledScore) VALUES (@pa,@ls,@c,13,215), (@pa,@rd,@c,10,170);
UPDATE ExamAttempts SET TotalScore=385, WrittenByService='AttemptEngine', ProjectionVersion=1 WHERE AttemptId=@pa;
INSERT INTO PlacementTests (UserId,CertId,ExamId,AttemptId,ResultBandCode,TotalScore,WrittenByService,ProjectionVersion)
  VALUES (@u,@c,@ps,@pa,'A2',385,'AttemptEngine',1);
PRINT 'accepted placement snapshot';
BEGIN TRY
  -- a second attempt, so the refusal under test is the snapshot guard and not UX_PlacementTests_Attempt
  INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt,TotalScore,WrittenByService,ProjectionVersion)
    VALUES (@u,@ps,@c,'submitted',DATEADD(HOUR,-1,SYSDATETIMEOFFSET()),200,'AttemptEngine',1);
  DECLARE @pa2 INT=SCOPE_IDENTITY();
  INSERT INTO PlacementTests (UserId,CertId,ExamId,AttemptId,TotalScore,WrittenByService,ProjectionVersion)
    VALUES (@u,@c,@ps,@pa2,999,'AttemptEngine',1);
  THROW 50000,'EXPECT_REJECT FAILED: placement total disagrees with attempt accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  -- #191: the refusal must be the snapshot-agrees-with-attempt guard, not the writer guard
  IF ERROR_MESSAGE() NOT LIKE '%snapshot of the attempt%' THROW;
  PRINT 'rejected placement total that disagrees with the attempt'; END CATCH
BEGIN TRY
  -- likewise a third attempt, so the refusal is the done-needs-a-result constraint
  INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt)
    VALUES (@u,@ps,@c,'submitted',DATEADD(HOUR,-1,SYSDATETIMEOFFSET())); DECLARE @pa3 INT=SCOPE_IDENTITY();
  INSERT INTO PlacementTests (UserId,CertId,ExamId,AttemptId,Status,WrittenByService,ProjectionVersion)
    VALUES (@u,@c,@ps,@pa3,'done','AttemptEngine',1);
  THROW 50000,'EXPECT_REJECT FAILED: done placement without a result accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Placement_DoneHasResult%' THROW;
  PRINT 'rejected done placement without band/total'; END CATCH

-- UI-44: an ai_task path item may open a writing/speaking practice exam, but never a mock or a placement
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal) VALUES (@c,'B1',N'B1',550,784);
INSERT INTO PathTemplates (CertId,FromBandCode,ToBandCode,Name) VALUES (@c,'A2','B1',N'Starter'); DECLARE @tpl INT=SCOPE_IDENTITY();
INSERT INTO PathModules (TemplateId,SkillId,CertId,Title) VALUES (@tpl,@wr,@c,N'Writing'); DECLARE @mod INT=SCOPE_IDENTITY();
INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefExamId) VALUES (@mod,@c,@tpl,'ai_task',@wx);
PRINT 'accepted ai_task opening a writing-practice exam';
BEGIN TRY
  INSERT INTO PathModuleItems (ModuleId,CertId,TemplateId,ItemType,RefExamId) VALUES (@mod,@c,@tpl,'ai_task',@ps);
  THROW 50000,'EXPECT_REJECT FAILED: ai_task opening a placement exam accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected ai_task opening a placement exam'; END CATCH
