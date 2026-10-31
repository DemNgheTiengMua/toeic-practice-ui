-- docs/schema-tests/domain16-goal-feasibility.sql — #153: a goal with a deadline, and its answer.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('learner',N'Learner'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'g@x.com',0x00,@c); DECLARE @u INT=SCOPE_IDENTITY();
-- CEFR bands carry cumulative hours; a non-CEFR band carries none.
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal,DisplayOrder,CumulativeStudyHours) VALUES
  (@c,'B1',N'Intermediate',550,780,1,500),
  (@c,'B2',N'Upper intermediate',785,940,2,800),
  (@c,'X9',N'Custom band',941,990,3,NULL);

-- accept: a goal with a deadline, its two numbers and its verdict
INSERT INTO TargetBands (UserId,CertId,TargetBandCode,CurrentBandCode,TargetDate,WeeklyStudyHours,RequiredTotalHours,FeasibilityStatus)
  VALUES (@u,@c,'B2','B1',DATEADD(day,30,CAST(SYSDATETIMEOFFSET() AS DATE)),6.0,300,'feasible');
PRINT 'goal with a deadline accepted';

-- accept: a goal with no deadline — the placement flow writes exactly this row, and "no answer" is legal
UPDATE TargetBands SET TargetDate = NULL, WeeklyStudyHours = NULL, RequiredTotalHours = NULL, FeasibilityStatus = NULL
  WHERE UserId = @u AND CertId = @c;
PRINT 'goal without a deadline accepted, with no verdict';

-- accept: a deadline on a certificate whose bands carry no hours — answer stays NULL, not invented
UPDATE TargetBands SET TargetBandCode = 'X9', CurrentBandCode = 'B1', TargetDate = DATEADD(day,30,CAST(SYSDATETIMEOFFSET() AS DATE)),
  WeeklyStudyHours = 6.0, RequiredTotalHours = NULL, FeasibilityStatus = NULL WHERE UserId = @u AND CertId = @c;
PRINT 'undecidable goal accepted with a NULL verdict';

UPDATE TargetBands SET FeasibilityStatus = 'unrealistic' WHERE UserId = @u AND CertId = @c;
PRINT 'unrealistic goal is storable — the screen has to show it';

-- reject: a pace above the 25 h/week ceiling
BEGIN TRY
  UPDATE TargetBands SET WeeklyStudyHours = 26.0 WHERE UserId = @u AND CertId = @c;
  THROW 50000,'EXPECT_REJECT FAILED: 26 h/week accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a pace above the 25 h ceiling as expected';
END CATCH

-- reject: a zero pace, which would divide by zero in the availability formula
BEGIN TRY
  UPDATE TargetBands SET WeeklyStudyHours = 0 WHERE UserId = @u AND CertId = @c;
  THROW 50000,'EXPECT_REJECT FAILED: 0 h/week accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a zero pace as expected';
END CATCH

-- reject: half a goal — a date with no pace, so the answer could never be computed
BEGIN TRY
  UPDATE TargetBands SET TargetDate = DATEADD(day,30,CAST(SYSDATETIMEOFFSET() AS DATE)), WeeklyStudyHours = NULL
    WHERE UserId = @u AND CertId = @c;
  THROW 50000,'EXPECT_REJECT FAILED: date without a weekly pace accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a deadline without a pace as expected';
END CATCH

-- reject: a verdict with no deadline to justify it
BEGIN TRY
  UPDATE TargetBands SET TargetDate = NULL, WeeklyStudyHours = NULL
    WHERE UserId = @u AND CertId = @c;
  THROW 50000,'EXPECT_REJECT FAILED: verdict without a deadline accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a verdict without a deadline as expected';
END CATCH

-- reject: an invented status outside the three the spec names
UPDATE TargetBands SET TargetDate = DATEADD(day,30,CAST(SYSDATETIMEOFFSET() AS DATE)), WeeklyStudyHours = 6.0
  WHERE UserId = @u AND CertId = @c;
BEGIN TRY
  UPDATE TargetBands SET FeasibilityStatus = 'probably_fine' WHERE UserId = @u AND CertId = @c;
  THROW 50000,'EXPECT_REJECT FAILED: unknown feasibility status accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an unknown feasibility status as expected';
END CATCH

-- reject: a starting band that is not a band of this certificate
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('IELTS',N'I',1); DECLARE @c2 INT=SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId,Code,Name,MinTotal,MaxTotal,DisplayOrder) VALUES (@c2,'Z1',N'Only band',0,100,1);
BEGIN TRY
  INSERT INTO TargetBands (UserId,CertId,TargetBandCode,CurrentBandCode) VALUES (@u,@c2,'Z1','B1');
  THROW 50000,'EXPECT_REJECT FAILED: starting band borrowed from another certificate accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a starting band from another certificate, as expected';
END CATCH

PRINT 'goal feasibility constraints hold';
