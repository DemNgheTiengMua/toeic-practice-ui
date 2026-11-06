-- docs/schema-tests/domain18-writing-feedback.sql — E2.1 (#151): actionable feedback belongs to a
-- finished grading, and nowhere else. A pending row has not been read and a failed one was never
-- scored, so neither may claim that a learner has something to fix.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'f151@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'WR',N'Writing','writing',1); DECLARE @wr INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount) VALUES (@wr,@c,'writing','W1',N'Essay',1); DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,RubricDescription,MaxScore,Weight,DisplayOrder)
  VALUES (@c,@wr,'GRAM',N'Grammar',N'5: accurate. 3: frequent errors.',5,1,1); DECLARE @crit INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'W151','published',60,'writing_practice'); DECLARE @ex INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId) VALUES (@ex,@c,@sec); DECLARE @g INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,MinWords,DisplayOrder)
  VALUES (@g,@ex,@sec,@wr,N'Essay','B2','free_response',250,1); DECLARE @q INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt) VALUES (@u,@ex,@c,DATEADD(HOUR,1,SYSDATETIMEOFFSET())); DECLARE @a INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,QuestionId,ExamId,SkillId,QuestionType,ResponseText,Status,SubmittedAt)
  VALUES (@a,@q,@ex,@wr,'free_response',N'Dog run fast.','pending_ai',SYSDATETIMEOFFSET()); DECLARE @fr INT=SCOPE_IDENTITY();
INSERT INTO AiGradings (FreeResponseId,SkillId) VALUES (@fr,@wr); DECLARE @gr INT=SCOPE_IDENTITY();

-- reject: a pending grading carrying feedback - nothing has read the answer yet
BEGIN TRY
  UPDATE AiGradings SET PriorityFix=N'Use the present continuous.', ModelRewrite=N'Dogs run fast.' WHERE GradingId=@gr;
  THROW 50000,'EXPECT_REJECT FAILED: feedback on a pending grading accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected feedback on a pending grading'; END CATCH

-- reject: a failed grading carrying feedback - a failure is not a bad mark
UPDATE AiGradings SET Status='failed', FailureReason=N'no usable answer', CompletedAt=SYSDATETIMEOFFSET() WHERE GradingId=@gr;
BEGIN TRY
  UPDATE AiGradings SET ErrorSpansJson=N'[{"originalSnippet":"Dog run fast."}]' WHERE GradingId=@gr;
  THROW 50000,'EXPECT_REJECT FAILED: feedback on a failed grading accepted',1;
END TRY BEGIN CATCH IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW; PRINT 'rejected feedback on a failed grading'; END CATCH

-- accept: the same feedback on the row once it is done
UPDATE AiGradings SET Status='pending', FailureReason=NULL, CompletedAt=NULL WHERE GradingId=@gr;
UPDATE AiGradings
   SET Status='done', CompletedAt=SYSDATETIMEOFFSET(), OverallScaled=3.00, BandCode=NULL,
       PriorityFix=N'Use the present continuous.',
       ModelRewrite=N'Dogs run fast.',
       ErrorSpansJson=N'[{"originalSnippet":"Dog run fast.","correctedSnippet":"Dogs run fast.","category":"grammar","explanationVi":"Chia động từ theo chủ ngữ số nhiều."}]'
 WHERE GradingId=@gr;
PRINT 'accepted feedback on a finished grading';

-- accept: a perfect answer may have nothing to fix, and the columns stay NULL
UPDATE AiGradings SET PriorityFix=NULL, ModelRewrite=NULL, ErrorSpansJson=NULL WHERE GradingId=@gr;
PRINT 'accepted a grading with no feedback at all';
