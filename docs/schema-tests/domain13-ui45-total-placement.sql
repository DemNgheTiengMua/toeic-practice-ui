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
  UPDATE ExamAttempts SET Status='graded', SubmittedAt=SYSDATETIMEOFFSET(), TotalScore=180 WHERE AttemptId=@a;
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
UPDATE ExamAttempts SET TotalScore=385 WHERE AttemptId=@pa;
INSERT INTO PlacementTests (UserId,CertId,ExamId,AttemptId,ResultBandCode,TotalScore)
  VALUES (@u,@c,@ps,@pa,'A2',385);
PRINT 'accepted placement snapshot';
BEGIN TRY
  -- a second attempt, so the refusal under test is the snapshot guard and not UX_PlacementTests_Attempt
  INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt,TotalScore)
    VALUES (@u,@ps,@c,'submitted',DATEADD(HOUR,-1,SYSDATETIMEOFFSET()),200);
  DECLARE @pa2 INT=SCOPE_IDENTITY();
  INSERT INTO PlacementTests (UserId,CertId,ExamId,AttemptId,TotalScore)
    VALUES (@u,@c,@ps,@pa2,999);
  THROW 50000,'EXPECT_REJECT FAILED: placement total disagrees with attempt accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  -- #191: the refusal must be the snapshot-agrees-with-attempt guard, not the writer guard
  IF ERROR_MESSAGE() NOT LIKE '%snapshot of the attempt%' THROW;
  PRINT 'rejected placement total that disagrees with the attempt'; END CATCH

-- UI-44, carried across #232: a mock_section step may open a mock exam and nothing else. Before
-- #232 this rule lived on PathModuleItems' ai_task rows, which D10 drops; the surviving shape is the
-- only column that can still open a paper.
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal) VALUES (@c,'B1',N'B1',550,784);
INSERT INTO Topics (Modality,Code,Name) VALUES ('writing','W-CORR',N'Correspondence');
DECLARE @tWr13 INT=(SELECT TopicId FROM Topics WHERE Modality='writing' AND Code='W-CORR');
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Writing mock 13',N'published',60,'mock');
DECLARE @wxMock13 INT=SCOPE_IDENTITY();
INSERT INTO LearnerPaths (UserId,CertId,Status) VALUES (@u,@c,'reset'); DECLARE @u44lp13 INT=SCOPE_IDENTITY();
INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,RefExamId)
  VALUES (@u44lp13,@c,@wr,'writing',@tWr13,'B1','mock_section','developing',@wxMock13);
PRINT 'accepted mock_section pointing at a mock exam';
BEGIN TRY
  INSERT INTO LearnerPathSteps (LearnerPathId,CertId,SkillId,Modality,TopicId,TargetCefr,StepType,SizingTier,RefExamId)
    VALUES (@u44lp13,@c,@wr,'writing',@tWr13,'B1','mock_section','developing',@ps);
  THROW 50000,'EXPECT_REJECT FAILED: mock_section opening a placement exam accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected mock_section opening a placement exam'; END CATCH
