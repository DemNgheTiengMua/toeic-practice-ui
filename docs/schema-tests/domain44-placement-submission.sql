SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #217 / PLC-18: a partial submission's two facts at the database grain.
--
-- What this file pins, and the input that would pass a weaker version while being wrong:
--   1. An UNANSWERED objective item is not counted as an answered item by the pool accuracy views.
--      The views count `COUNT(IsCorrect)`, so an unanswered item stored with `IsCorrect = 0` would be
--      counted - inflating the denominator of every weakness percentage the learner ever sees. That is
--      the wrong-but-passing input: "mark blanks incorrect" at the column, asserted only by the total
--      on the result page, passes every service test and corrupts vw_UserSectionAccuracyAll.
--      The correct shape is `IsCorrect IS NULL` for a blank: it is not correct (the raw score counts
--      only `= 1` over every delivered item) and it is not answered.
--   2. A `done` placement with NO measured row and NO recorded failure is still refused in BOTH
--      directions (#212's two triggers). PLC-19's "nothing measurable is abandoned, not done" rides on
--      that guard rather than replacing it: this file re-asserts both arms so a later change that
--      relaxed them for a partial result would fail here.

INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('SUB217',N'Submit cert',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder,MinScaledScore,MaxScaledScore,ScoreStep)
  VALUES (@c,'R',N'Reading','reading',1,0,100,1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount,DisplayOrder)
  VALUES (@sk,@c,'reading','R1',N'Reading',2,1); DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO QuestionCategories (CertId,Code,Name) VALUES (@c,'R217',N'Detail'); DECLARE @cat INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Placement 217','published',30,'placement');
DECLARE @e INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@e,@c,@sec,1); DECLARE @g INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,CefrLevel,QuestionType,CategoryId,DisplayOrder)
  VALUES (@g,@e,@sec,@sk,N'Answered','B1','mcq',@cat,1), (@g,@e,@sec,@sk,N'Blank','B1','mcq',@cat,2);
DECLARE @q1 INT = (SELECT QuestionId FROM Questions WHERE ExamId=@e AND Stem=N'Answered');
DECLARE @q2 INT = (SELECT QuestionId FROM Questions WHERE ExamId=@e AND Stem=N'Blank');
INSERT INTO QuestionOptions (QuestionId,QuestionType,Label,Text,IsCorrect)
  VALUES (@q1,'mcq','A',N'Right',1), (@q1,'mcq','B',N'Wrong',0),
         (@q2,'mcq','A',N'Right',1), (@q2,'mcq','B',N'Wrong',0);
DECLARE @right1 INT = (SELECT OptionId FROM QuestionOptions WHERE QuestionId=@q1 AND IsCorrect=1);
INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'submit217@x.com',0x00,@c); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt) VALUES (@u,@e,@c,SYSDATETIMEOFFSET());
DECLARE @a INT=SCOPE_IDENTITY();

-- the shape submission writes: one answered-and-correct row, one blank left UNJUDGED
INSERT INTO AttemptAnswers (AttemptId,QuestionId,ExamId,SelectedOptionId,IsCorrect)
  VALUES (@a,@q1,@e,@right1,1), (@a,@q2,@e,NULL,NULL);
UPDATE ExamAttempts SET Status='graded' WHERE AttemptId=@a;

-- 1. the blank is NOT in the answered denominator, and the answered one IS
DECLARE @answered INT = (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId=@u AND SectionId=@sec);
IF @answered <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the accuracy view counted an unanswered item as answered, so every weakness percentage is diluted', 1;
DECLARE @correct INT = (SELECT Correct FROM vw_UserSectionAccuracyAll WHERE UserId=@u AND SectionId=@sec);
IF @correct <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the accuracy view lost the one answered item''s correct mark', 1;
-- and the topic view agrees, so the two axes cannot disagree about one sitting. #271: the category
-- view this used to read is gone; a legacy answer has no cell, and this fixture's questions are
-- legacy, so what is asserted here is the SECTION axis plus the topic view's silence.
IF EXISTS (SELECT 1 FROM vw_UserTopicAccuracy WHERE UserId=@u)
  THROW 50000, 'EXPECT_REJECT FAILED: a legacy answer produced a topic cell, which no answer supports', 1;
PRINT 'a blank is not counted as an answered item; the answered one is';

-- the wrong-but-passing input, stated as a refusal: a blank written as INCORRECT inflates the denominator
UPDATE AttemptAnswers SET IsCorrect = 0 WHERE AttemptId=@a AND QuestionId=@q2;
IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId=@u AND SectionId=@sec) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the view no longer reflects IsCorrect = 0, so this test no longer proves the blank is excluded', 1;
PRINT 'marking a blank incorrect demonstrably inflates the denominator - which is why submission leaves it NULL';
UPDATE AttemptAnswers SET IsCorrect = NULL WHERE AttemptId=@a AND QuestionId=@q2;

-- 2. #217 / PLC-19 rides on #212's completion guard: `done` with no measurement and no failure is refused
INSERT INTO PlacementTests (UserId,CertId,ExamId,AttemptId,Status)
  VALUES (@u,@c,@e,@a,'in_progress'); DECLARE @p INT=SCOPE_IDENTITY();
BEGIN TRY
  UPDATE PlacementTests SET Status='done' WHERE PlacementId=@p;
  THROW 50000, 'EXPECT_REJECT FAILED: a done placement with no measured skill and no failure reason was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'refused a done placement with no measurement';
END CATCH

-- the abandoned shape PLC-19 asks for: same row, no result, and the attempt carries no total either
INSERT INTO AttemptSkillScores (AttemptId,SkillId,CertId,RawScore,ScaledScore) VALUES (@a,@sk,@c,1,100);
UPDATE PlacementTests SET Status='abandoned', CompletionFailureReason='candidate_skipped' WHERE PlacementId=@p;
IF EXISTS (SELECT 1 FROM PlacementTests WHERE PlacementId=@p AND (TotalScore IS NOT NULL OR ResultBandCode IS NOT NULL))
  THROW 50000, 'EXPECT_REJECT FAILED: an abandoned sitting claims a result', 1;
PRINT 'an abandoned sitting is storable with no total and no band';
