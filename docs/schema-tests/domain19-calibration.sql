-- docs/schema-tests/domain19-calibration.sql — #156: a response time, and a decision the learner made.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('learner',N'Learner'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'c@x.com',0x00,@c); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder,MinScaledScore,MaxScaledScore,ScoreStep)
  VALUES (@c,'LIST',N'Listening','listening',1,5,495,5);
DECLARE @s INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,ExpectedQuestionCount,DisplayOrder)
  VALUES (@s,@c,'listening','P1',N'Photos',1,1);
DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal,DisplayOrder) VALUES
  (@c,'B1',N'Intermediate',550,780,1),
  (@c,'B2',N'Upper intermediate',785,940,2);
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose,QuestionCount)
  VALUES (@c,N'P','published',20,'placement',1);
DECLARE @e INT=SCOPE_IDENTITY();
-- #269: a paper position is a pool item's version on a section. Two positions, so the two answers below
-- are two distinct rows rather than two answers to one.
INSERT INTO Topics (Modality,Code,Name) VALUES ('listening','L-DET',N'Explicit factual detail and extraction');
DECLARE @topic INT=(SELECT TopicId FROM Topics WHERE Code='L-DET');
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat,BatchTag) VALUES
  ('listening',@topic,'B1','short_exchange','mcq','cal-a'),
  ('listening',@topic,'B1','short_exchange','mcq','cal-b');
DECLARE @i1 INT=(SELECT ItemId FROM Items WHERE BatchTag='cal-a');
DECLARE @i2 INT=(SELECT ItemId FROM Items WHERE BatchTag='cal-b');
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,OptionA,OptionB,OptionC,OptionD,KeyLabel) VALUES
  (@i1,1,N'Q', N'a',N'b',N'c',N'd',N'A'),
  (@i2,1,N'Q2',N'a',N'b',N'c',N'd',N'A');
DECLARE @v1 INT=(SELECT ItemVersionId FROM ItemVersions WHERE ItemId=@i1);
DECLARE @v2 INT=(SELECT ItemVersionId FROM ItemVersions WHERE ItemId=@i2);
INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder) VALUES
  (@e,@sec,@c,@v1,@i1,'listening',1),
  (@e,@sec,@c,@v2,@i2,'listening',2);
DECLARE @pos1 INT=(SELECT ExamItemId FROM ExamItems WHERE ExamId=@e AND DisplayOrder=1);
DECLARE @pos2 INT=(SELECT ExamItemId FROM ExamItems WHERE ExamId=@e AND DisplayOrder=2);
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,StartedAt,ExpiresAt)
  VALUES (@u,@e,@c,'in_progress',SYSDATETIMEOFFSET(),DATEADD(hour,1,SYSDATETIMEOFFSET()));
DECLARE @a INT=SCOPE_IDENTITY();

-- #167: AttemptAnswers carries no AnsweredAt any more — nothing read it. The row inserts below stay
-- so the answer table is still exercised; only PracticeAnswers has a response time.
INSERT INTO AttemptAnswers (AttemptId,ExamId,ItemId,ItemVersionId,ExamItemId,IsCorrect)
  VALUES (@a,@e,@i1,@v1,@pos1,1);
INSERT INTO AttemptAnswers (AttemptId,ExamId,ItemId,ItemVersionId,ExamItemId,IsCorrect)
  VALUES (@a,@e,@i2,@v2,@pos2,0);
PRINT 'attempt answers store without a response time';

-- accept: a decision the learner made, with the numbers behind it
INSERT INTO CalibrationDecisions (UserId,CertId,PlacedBandCode,ProposedBandCode,SampleSize,ObservedAccuracyPct,Decision)
  VALUES (@u,@c,'B2','B1',20,15.0,'deferred');
PRINT 'a deferred decision is storable — that is the record of "leave my path alone"';

-- accept: the same learner later choosing to adjust
INSERT INTO CalibrationDecisions (UserId,CertId,PlacedBandCode,ProposedBandCode,SampleSize,ObservedAccuracyPct,Decision)
  VALUES (@u,@c,'B2','B1',25,12.5,'adjusted');
PRINT 'an adjusted decision is storable alongside the deferral';

-- reject: a conclusion on fewer than twenty valid answers
BEGIN TRY
  INSERT INTO CalibrationDecisions (UserId,CertId,PlacedBandCode,ProposedBandCode,SampleSize,ObservedAccuracyPct,Decision)
    VALUES (@u,@c,'B2','B1',19,10.0,'deferred');
  THROW 50000,'EXPECT_REJECT FAILED: a decision on 19 answers was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a conclusion below the 20-answer threshold';
END CATCH

-- reject: an unknown decision word
BEGIN TRY
  INSERT INTO CalibrationDecisions (UserId,CertId,PlacedBandCode,ProposedBandCode,SampleSize,ObservedAccuracyPct,Decision)
    VALUES (@u,@c,'B2','B1',20,10.0,'maybe');
  THROW 50000,'EXPECT_REJECT FAILED: an invented decision was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an invented decision word';
END CATCH

-- reject: a proposal that would raise the band — this detector only ever offers a lower one
BEGIN TRY
  INSERT INTO CalibrationDecisions (UserId,CertId,PlacedBandCode,ProposedBandCode,SampleSize,ObservedAccuracyPct,Decision)
    VALUES (@u,@c,'B1','B1',20,10.0,'deferred');
  THROW 50000,'EXPECT_REJECT FAILED: a band that does not move was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a proposal that changes nothing';
END CATCH

-- reject: an accuracy outside 0..100
BEGIN TRY
  INSERT INTO CalibrationDecisions (UserId,CertId,PlacedBandCode,ProposedBandCode,SampleSize,ObservedAccuracyPct,Decision)
    VALUES (@u,@c,'B2','B1',20,120.0,'deferred');
  THROW 50000,'EXPECT_REJECT FAILED: an impossible accuracy was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an accuracy outside 0..100';
END CATCH

-- reject: a band belonging to another certificate
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('IELTS',N'I',1); DECLARE @c2 INT=SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal,DisplayOrder) VALUES (@c2,'Z1',N'Only band',0,100,1);
BEGIN TRY
  INSERT INTO CalibrationDecisions (UserId,CertId,PlacedBandCode,ProposedBandCode,SampleSize,ObservedAccuracyPct,Decision)
    VALUES (@u,@c2,'Z1','B1',20,10.0,'deferred');
  THROW 50000,'EXPECT_REJECT FAILED: a borrowable band was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a proposed band from another certificate';
END CATCH

-- accept: a practice answer carries its own response time too
INSERT INTO PracticeSessions (UserId,CertId,SectionId,Modality,QuestionCount,Status)
  VALUES (@u,@c,@sec,'listening',1,'in_progress');
DECLARE @ps INT=SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId,ItemId,ItemVersionId,IsCorrect,DisplayOrder,AnsweredAt)
  VALUES (@ps,@i1,@v1,1,1,SYSDATETIMEOFFSET());
PRINT 'practice answer timestamps accepted';

PRINT 'calibration constraints hold';
