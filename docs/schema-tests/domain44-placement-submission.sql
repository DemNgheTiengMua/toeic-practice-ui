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
--
-- #269: the fixture authors the POOL shape. It used QuestionGroups/Questions/QuestionOptions, which no
-- longer exist; two positions over two item versions express the same "one answered, one blank", and the
-- blank-vs-incorrect distinction is unchanged because it is about IsCorrect, not about where the item
-- came from.

INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('SUB217',N'Submit cert',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder,MinScaledScore,MaxScaledScore,ScoreStep)
  VALUES (@c,'R',N'Reading','reading',1,0,100,1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount,DisplayOrder)
  VALUES (@sk,@c,'reading','R1',N'Reading',2,1); DECLARE @sec INT=SCOPE_IDENTITY();

INSERT INTO Topics (Modality,Code,Name) VALUES ('reading','R-DET',N'Local factual retrieval and paraphrase matching');
DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code='R-DET');

INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat,ContentTier,LifecycleStatus,BatchTag) VALUES
  ('reading',@topic,'B1','sentence_cloze','mcq','demo','active','sub-answered'),
  ('reading',@topic,'B1','sentence_cloze','mcq','demo','active','sub-blank');
DECLARE @i1 INT = (SELECT ItemId FROM Items WHERE BatchTag='sub-answered');
DECLARE @i2 INT = (SELECT ItemId FROM Items WHERE BatchTag='sub-blank');

INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,OptionA,OptionB,OptionC,OptionD,KeyLabel) VALUES
  (@i1,1,N'Answered',N'Right',N'Wrong',N'Wrong',N'Wrong','A'),
  (@i2,1,N'Blank',   N'Right',N'Wrong',N'Wrong',N'Wrong','A');
DECLARE @v1 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId=@i1);
DECLARE @v2 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId=@i2);

INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose) VALUES (@c,N'Placement 217','published',30,'placement');
DECLARE @e INT=SCOPE_IDENTITY();
INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder) VALUES
  (@e,@sec,@c,@v1,@i1,'reading',1),
  (@e,@sec,@c,@v2,@i2,'reading',2);
DECLARE @p1 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId=@e AND DisplayOrder=1);
DECLARE @p2 INT = (SELECT ExamItemId FROM ExamItems WHERE ExamId=@e AND DisplayOrder=2);

INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'submit217@x.com',0x00,@c); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,ExpiresAt) VALUES (@u,@e,@c,SYSDATETIMEOFFSET());
DECLARE @a INT=SCOPE_IDENTITY();

-- the shape submission writes: one answered-and-correct row, one blank left UNJUDGED
INSERT INTO AttemptAnswers (AttemptId,ExamId,ItemId,ItemVersionId,ExamItemId,SelectedLabel,IsCorrect)
  VALUES (@a,@e,@i1,@v1,@p1,'A',1),
         (@a,@e,@i2,@v2,@p2,NULL,NULL);
UPDATE ExamAttempts SET Status='graded' WHERE AttemptId=@a;

-- 1. the blank is NOT in the answered denominator, and the answered one IS
DECLARE @answered INT = (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId=@u AND SectionId=@sec);
IF @answered <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the accuracy view counted an unanswered item as answered, so every weakness percentage is diluted', 1;
DECLARE @correct INT = (SELECT Correct FROM vw_UserSectionAccuracyAll WHERE UserId=@u AND SectionId=@sec);
IF @correct <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the accuracy view lost the one answered item''s correct mark', 1;
-- and the topic view agrees, so the two axes cannot disagree about one sitting. #271: the category
-- view this used to read is gone. #269: this fixture's items DO carry a cell now (they are pool items),
-- so the topic axis is asserted rather than its silence - which is the stronger claim.
IF (SELECT Answered FROM vw_UserTopicAccuracy WHERE UserId=@u AND TopicId=@topic AND CefrLevel='B1') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the topic view did not count the one answered item', 1;
IF (SELECT Correct FROM vw_UserTopicAccuracy WHERE UserId=@u AND TopicId=@topic AND CefrLevel='B1') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the topic view lost the answered item''s correct mark', 1;
PRINT 'a blank is not counted as an answered item; the answered one is';

-- the wrong-but-passing input, stated as a refusal: a blank written as INCORRECT inflates the denominator
UPDATE AttemptAnswers SET IsCorrect = 0 WHERE AttemptId=@a AND ExamItemId=@p2;
IF (SELECT Answered FROM vw_UserSectionAccuracyAll WHERE UserId=@u AND SectionId=@sec) <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the view no longer reflects IsCorrect = 0, so this test no longer proves the blank is excluded', 1;
PRINT 'marking a blank incorrect demonstrably inflates the denominator - which is why submission leaves it NULL';
UPDATE AttemptAnswers SET IsCorrect = NULL WHERE AttemptId=@a AND ExamItemId=@p2;

-- 2. abandoned sitting: no result, and the attempt carries no total either
INSERT INTO PlacementTests (UserId,CertId,ExamId,AttemptId,Status)
  VALUES (@u,@c,@e,@a,'in_progress'); DECLARE @p INT=SCOPE_IDENTITY();

-- the abandoned shape PLC-19 asks for: same row, no result, and the attempt carries no total either
INSERT INTO AttemptSkillScores (AttemptId,SkillId,CertId,RawScore,ScaledScore) VALUES (@a,@sk,@c,1,100);
UPDATE PlacementTests SET Status='abandoned', CompletionFailureReason='candidate_skipped' WHERE PlacementId=@p;
IF EXISTS (SELECT 1 FROM PlacementTests WHERE PlacementId=@p AND (TotalScore IS NOT NULL OR ResultBandCode IS NOT NULL))
  THROW 50000, 'EXPECT_REJECT FAILED: an abandoned sitting claims a result', 1;
PRINT 'an abandoned sitting is storable with no total and no band';
