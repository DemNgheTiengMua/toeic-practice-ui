SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- docs/schema-tests/domain06-mockexam.sql
--
-- #269: an answer names a roster POSITION (ExamItemId) and the version it pinned, not a Questions row.
-- Two of the H-5 refusals this file used to make went with the composite foreign keys that carried them,
-- and they are named here rather than quietly rewritten:
--
--   * "a TOEIC attempt cannot answer an IELTS QUESTION" was FK_AttemptAnswers_QuestionExam
--     (QuestionId, ExamId) -> Questions(QuestionId, ExamId). With QuestionId gone nothing ties an
--     answer's ExamItemId to its ExamId, so an answer can name a roster position of another paper.
--     THIS IS A LOST RULE, not a passing one - reported with #269, not asserted here, because a test
--     for it would fail and a test written around it would be the defect.
--   * "a cross-cert FREE RESPONSE is refused" was FK_FreeResp_QuestionExam + FK_FreeResp_QuestionSkill.
--     The exam half SURVIVES as FK_FreeResp_AttemptExam and is asserted below; the skill half is lost
--     for the same reason and reported with it.
--
-- What survives, and is proved here: an attempt cannot be created on a draft exam, cannot be moved onto
-- a draft exam after creation, its total must match the skill sum, and every child row it carries must
-- name the attempt's OWN exam (FK_*_AttemptExam, one composite foreign key each).
DECLARE @msg NVARCHAR(2048);
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
  INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt) VALUES (@u,@draft,@c,'in_progress',SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: draft attempt accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected draft-exam attempt as expected';
END CATCH
-- valid: attempt on published exam, two skill scores, total = 250+250
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt,TotalScore,WrittenByService,ProjectionVersion)
  VALUES (@u,@pub,@c,'submitted',SYSDATETIMEOFFSET(),500,'AttemptEngine',1);
DECLARE @a INT=SCOPE_IDENTITY();
INSERT INTO AttemptSkillScores (AttemptId,SkillId,CertId,RawScore,ScaledScore)
  VALUES (@a,@sL,@c,40,250),(@a,@sR,@c,42,250);
PRINT 'valid published attempt accepted';
-- reject (Review Focus #2): TotalScore that disagrees with the skill sum
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore=999, WrittenByService='AttemptEngine', ProjectionVersion=2 WHERE AttemptId=@a;
  THROW 50000,'EXPECT_REJECT FAILED: total mismatch accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  -- #191: the single-writer guard is on this table too, so the refusal must name the aggregation
  -- rule. Without this the test would still pass if the writer guard fired first and the invariant
  -- it is actually about had stopped being enforced.
  IF ERROR_MESSAGE() NOT LIKE '%TotalScore does not match%' THROW;
  PRINT 'rejected total/skill mismatch as expected';
END CATCH
-- M-5: attempt created against published exam then moved to a draft exam by UPDATE
INSERT INTO ExamAttempts (UserId,ExamId,CertId,Status,ExpiresAt)
  VALUES (@u,@pub,@c,'in_progress',SYSDATETIMEOFFSET());
DECLARE @a2 INT=SCOPE_IDENTITY();
BEGIN TRY
  UPDATE ExamAttempts SET ExamId=@draft WHERE AttemptId=@a2;
  THROW 50000,'EXPECT_REJECT FAILED: attempt moved to draft exam accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected attempt moved onto draft exam as expected';
END CATCH

-- H-5: a second certificate, with its own exam holding one roster position. The position is what the
-- legacy cross-cert fixtures were reaching for; the child rows below must name the TOEIC attempt's own
-- exam, which is the half of H-5 that survives as a foreign key.
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('IELTS',N'I',1); DECLARE @c2 INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c2,'SPK',N'Spk','speaking',1);
DECLARE @s2 INT=SCOPE_IDENTITY();
-- OptionCount is NOT NULL (2026-10-26), 2..6; 4 is what the certificate wizard supplies.
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount,DisplayOrder) VALUES (@s2,@c2,'speaking','S1',N'Interview',4,3,1);
DECLARE @sec2 INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c2,N'IELTS Exam','published',60);
DECLARE @exam2 INT=SCOPE_IDENTITY();
INSERT INTO Topics (Modality,Code,Name) VALUES ('speaking','S-DESC',N'Informational description and personal framing');
DECLARE @t2 INT=(SELECT TopicId FROM Topics WHERE Code='S-DESC');
INSERT INTO Items (Modality,TopicId,CefrLevel,ItemType,RenderFormat)
  VALUES ('speaking',@t2,'B2','descriptive_monologue','free_response');
DECLARE @item2 INT=SCOPE_IDENTITY();
INSERT INTO ItemVersions (ItemId,VersionNumber,Stem,ResponseSeconds)
  VALUES (@item2,1,N'Describe the picture.',90);
DECLARE @v2 INT=SCOPE_IDENTITY();
INSERT INTO ExamItems (ExamId,SectionId,CertId,ItemVersionId,ItemId,Modality,DisplayOrder)
  VALUES (@exam2,@sec2,@c2,@v2,@item2,'speaking',1);
DECLARE @ei2 INT=SCOPE_IDENTITY();

-- H-5: reject a TOEIC attempt's answer claiming the IELTS exam. FK_AttemptAnswers_AttemptExam pins the
-- carried-down ExamId to the attempt's own, so the answer cannot claim another certificate's paper.
BEGIN TRY
  INSERT INTO AttemptAnswers (AttemptId,ExamId,ItemId,ItemVersionId,ExamItemId)
    VALUES (@a,@exam2,@item2,@v2,@ei2);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert attempt answer accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_AttemptAnswers_AttemptExam%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the cross-cert answer was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected cross-cert attempt answer, by FK_AttemptAnswers_AttemptExam';
END CATCH
-- H-5: reject a per-skill score for a skill outside the attempt's certificate
BEGIN TRY
  INSERT INTO AttemptSkillScores (AttemptId,SkillId,CertId,RawScore,ScaledScore) VALUES (@a,@s2,@c,1,1);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert skill score accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert skill score as expected';
END CATCH
-- H-5: reject a free response on a TOEIC attempt claiming the IELTS exam
BEGIN TRY
  INSERT INTO FreeResponses (AttemptId,ExamItemId,ExamId,SectionId,SkillId,QuestionType,ResponseText)
    VALUES (@a,@ei2,@exam2,@sec2,@s2,'free_response',N'x');
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert free response accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_FreeResp_AttemptExam%'
  BEGIN
    SET @msg = N'EXPECT_REJECT FAILED: the cross-cert response was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @msg, 1;
  END
  PRINT 'rejected cross-cert free response, by FK_FreeResp_AttemptExam';
END CATCH
