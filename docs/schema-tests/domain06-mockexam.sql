SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'L',N'L','listening',1),(@c,'R',N'R','reading',2);
DECLARE @sL INT=(SELECT SkillId FROM Skills WHERE CertId=@c AND Code='L');
DECLARE @sR INT=(SELECT SkillId FROM Skills WHERE CertId=@c AND Code='R');
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'Draft','draft',120);
DECLARE @draft INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'Pub','published',120);
DECLARE @pub INT=SCOPE_IDENTITY();
-- reject: attempt on a draft exam
BEGIN TRY
  INSERT INTO ExamAttempts (UserId,ExamId,Status,ExpiresAt) VALUES (@u,@draft,'in_progress',SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: draft attempt accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected draft-exam attempt as expected';
END CATCH
-- valid: attempt on published exam, two skill scores, total = 250+250
INSERT INTO ExamAttempts (UserId,ExamId,Status,ExpiresAt,TotalScore)
  VALUES (@u,@pub,'submitted',SYSDATETIMEOFFSET(),500);
DECLARE @a INT=SCOPE_IDENTITY();
INSERT INTO AttemptSkillScores (AttemptId,SkillId,RawScore,ScaledScore)
  VALUES (@a,@sL,40,250),(@a,@sR,42,250);
PRINT 'valid published attempt accepted';
-- reject (Review Focus #2): TotalScore that disagrees with the skill sum
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore=999 WHERE AttemptId=@a;
  THROW 50000,'EXPECT_REJECT FAILED: total mismatch accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected total/skill mismatch as expected';
END CATCH
