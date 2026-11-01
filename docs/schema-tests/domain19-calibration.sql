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
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@e,@c,@sec,1);
DECLARE @g INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@g,@e,@sec,@s,N'Q',2,'mcq',1);
DECLARE @q INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@g,@e,@sec,@s,N'Q2',2,'mcq',2);
DECLARE @q2 INT=SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,StartedAt,ExpiresAt)
  VALUES (@u,@e,@c,'in_progress',SYSDATETIMEOFFSET(),DATEADD(hour,1,SYSDATETIMEOFFSET()));
DECLARE @a INT=SCOPE_IDENTITY();

-- accept: an answer with a response time, and one from before the column with none
INSERT INTO AttemptAnswers (AttemptId,QuestionId,ExamId,IsCorrect,AnsweredAt)
  VALUES (@a,@q,@e,1,SYSDATETIMEOFFSET());
INSERT INTO AttemptAnswers (AttemptId,QuestionId,ExamId,IsCorrect)
  VALUES (@a,@q2,@e,0);
PRINT 'answer timestamps accepted, NULL allowed for rows that predate the column';

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
INSERT INTO PracticeSessions (UserId,SectionId,Modality,QuestionCount,Status)
  VALUES (@u,@sec,'listening',1,'in_progress');
DECLARE @ps INT=SCOPE_IDENTITY();
INSERT INTO PracticeAnswers (SessionId,QuestionId,SectionId,IsCorrect,DisplayOrder,AnsweredAt)
  VALUES (@ps,@q,@sec,1,1,SYSDATETIMEOFFSET());
PRINT 'practice answer timestamps accepted';

PRINT 'calibration constraints hold';
