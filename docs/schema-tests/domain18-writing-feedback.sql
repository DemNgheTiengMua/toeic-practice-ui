-- docs/schema-tests/domain18-writing-feedback.sql — E2.1 (#151): actionable feedback belongs to a
-- finished grading, and nowhere else. A pending row has not been read and a failed one was never
-- scored, so neither may claim that a learner has something to fix.
--
-- #269: the free response names a roster POSITION (ExamItemId) instead of a Questions row. Nothing
-- else in this chain changed; MinWords is asserted where it now lives, on the version (the under-length
-- penalty reads it, so an in-place edit is refused by trg_ItemVersions_ScoredFieldsImmutable).
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'f151@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'WR',N'Writing','writing',1); DECLARE @wr INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount) VALUES (@wr,@c,'writing','W1',N'Essay',1); DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,RubricDescription,MaxScore,Weight,DisplayOrder)
  VALUES (@c,@wr,'GRAM',N'Grammar',N'5: accurate. 3: frequent errors.',5,1,1); DECLARE @crit INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'W151','published',60,'writing_practice'); DECLARE @ex INT=SCOPE_IDENTITY();
INSERT INTO Topics (Modality,Code,Name) VALUES ('writing','W-ARG',N'Thesis-driven persuasive and opinion argumentation');
DECLARE @topic INT=(SELECT TopicId FROM Topics WHERE Code='W-ARG');
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES ('writing',@topic,'B2','persuasive_essay','free_response'); DECLARE @item INT=SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,MinWords,ResponseSeconds)
  VALUES (@item,1,N'Essay',250,1800); DECLARE @v INT=SCOPE_IDENTITY();
IF (SELECT MinWords FROM ItemVersions WHERE ItemVersionId=@v) <> 250
  THROW 50000,'EXPECT_REJECT FAILED: the version did not keep its 250-word minimum',1;
INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
  VALUES (@ex,@sec,@c,@v,@item,'writing',1); DECLARE @pos INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt) VALUES (@u,@ex,@c,DATEADD(HOUR,1,SYSDATETIMEOFFSET())); DECLARE @a INT=SCOPE_IDENTITY();
INSERT INTO FreeResponses (AttemptId,ExamItemId,ExamId,SectionId,SkillId,QuestionType,ResponseText,Status,SubmittedAt)
  VALUES (@a,@pos,@ex,@sec,@wr,'free_response',N'Dog run fast.','pending_ai',SYSDATETIMEOFFSET()); DECLARE @fr INT=SCOPE_IDENTITY();
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
