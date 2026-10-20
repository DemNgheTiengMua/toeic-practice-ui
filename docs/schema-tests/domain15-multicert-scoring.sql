-- Verify the score-integrity triggers use each certificate's aggregation rule.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @role INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
VALUES (@role, 'scoring-schema-test@example.local', 0x01, N'Scoring schema test');
DECLARE @user INT = SCOPE_IDENTITY();

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC', N'TOEIC', 1);
DECLARE @toeic INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
  (@toeic, 'LIST', N'Listening', 'listening', 1),
  (@toeic, 'READ', N'Reading', 'reading', 2);
DECLARE @toeicListening INT = (SELECT SkillId FROM Skills WHERE CertId = @toeic AND Code = 'LIST');
DECLARE @toeicReading INT = (SELECT SkillId FROM Skills WHERE CertId = @toeic AND Code = 'READ');
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
VALUES (@toeic, N'TOEIC scoring', 'published', 60, 'mock');
DECLARE @toeicExam INT = SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @toeicExam, @toeic, 'submitted', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @toeicAttempt INT = SCOPE_IDENTITY();
INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore) VALUES
  (@toeicAttempt, @toeicListening, @toeic, 1, 200),
  (@toeicAttempt, @toeicReading, @toeic, 1, 300);
UPDATE ExamAttempts SET TotalScore = 500 WHERE AttemptId = @toeicAttempt;
PRINT 'accepted TOEIC skill-score sum';

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('IELTS', N'IELTS', 1);
DECLARE @ielts INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
  (@ielts, 'LIST', N'Listening', 'listening', 1),
  (@ielts, 'READ', N'Reading', 'reading', 2);
DECLARE @ieltsListening INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'LIST');
DECLARE @ieltsReading INT = (SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'READ');
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
VALUES (@ielts, N'IELTS scoring', 'published', 60, 'mock');
DECLARE @ieltsExam INT = SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @ieltsExam, @ielts, 'submitted', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @ieltsAttempt INT = SCOPE_IDENTITY();
INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore) VALUES
  (@ieltsAttempt, @ieltsListening, @ielts, 1, 60),
  (@ieltsAttempt, @ieltsReading, @ielts, 1, 65);
UPDATE ExamAttempts SET TotalScore = 65 WHERE AttemptId = @ieltsAttempt;
PRINT 'accepted IELTS average rounded to nearest half band';
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore = 62 WHERE AttemptId = @ieltsAttempt;
  THROW 50000, 'EXPECT_REJECT FAILED: invalid IELTS total accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an IELTS total that was not rounded to a half band';
END CATCH
BEGIN TRY
  UPDATE AttemptSkillScores SET ScaledScore = 75
  WHERE AttemptId = @ieltsAttempt AND SkillId = @ieltsReading;
  THROW 50000, 'EXPECT_REJECT FAILED: inconsistent IELTS skill score accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an IELTS skill-score change that invalidated its total';
END CATCH

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('VSTEP', N'VSTEP', 1);
DECLARE @vstep INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
  (@vstep, 'LIST', N'Listening', 'listening', 1),
  (@vstep, 'READ', N'Reading', 'reading', 2);
DECLARE @vstepListening INT = (SELECT SkillId FROM Skills WHERE CertId = @vstep AND Code = 'LIST');
DECLARE @vstepReading INT = (SELECT SkillId FROM Skills WHERE CertId = @vstep AND Code = 'READ');
INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
VALUES (@vstep, N'VSTEP scoring', 'published', 60, 'mock');
DECLARE @vstepExam INT = SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@user, @vstepExam, @vstep, 'submitted', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()));
DECLARE @vstepAttempt INT = SCOPE_IDENTITY();
INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore) VALUES
  (@vstepAttempt, @vstepListening, @vstep, 1, 50),
  (@vstepAttempt, @vstepReading, @vstep, 1, 61);
UPDATE ExamAttempts SET TotalScore = 56 WHERE AttemptId = @vstepAttempt;
PRINT 'accepted VSTEP average rounded to the nearest tenth-point representation';
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore = 55 WHERE AttemptId = @vstepAttempt;
  THROW 50000, 'EXPECT_REJECT FAILED: invalid VSTEP total accepted', 1;
END TRY
BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a VSTEP total rounded incorrectly';
END CATCH
