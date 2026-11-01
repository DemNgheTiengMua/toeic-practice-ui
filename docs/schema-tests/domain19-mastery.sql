-- docs/schema-tests/domain19-mastery.sql
-- #155: the mastery and question-history tables, and the constraints that keep them honest.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('IELTS',N'I',1); DECLARE @c2 INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'READ',N'R','reading',1);
DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Modality,Code,Name,OptionCount,ExpectedQuestionCount,DisplayOrder)
  VALUES (@sk,@c,'reading','P5',N'IC',4,30,1); DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'m@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO QuestionCategories (CertId,Code,Name) VALUES (@c,'tense',N'Verb tense');
DECLARE @cat INT=SCOPE_IDENTITY();
INSERT INTO QuestionCategories (CertId,Code,Name) VALUES (@c2,'ielts_detail',N'Detail');
DECLARE @cat2 INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'Mastery Src','published',60);
DECLARE @ex INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@ex,@c,@sec,1);
DECLARE @grp INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,CategoryId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp,@ex,@sec,@sk,@cat,N'q1',3,'mcq',1); DECLARE @q1 INT=SCOPE_IDENTITY();
INSERT INTO Questions (GroupId,ExamId,SectionId,SkillId,CategoryId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  VALUES (@grp,@ex,@sec,@sk,@cat,N'q2',3,'mcq',2); DECLARE @q2 INT=SCOPE_IDENTITY();

-- a valid mastery row is accepted, and starts Untested by default
INSERT INTO UserCategoryMasteries (UserId,CategoryId,CertId) VALUES (@u,@cat,@c);
PRINT 'valid mastery row accepted';
IF (SELECT State FROM UserCategoryMasteries WHERE UserId=@u AND CategoryId=@cat) <> 'untested'
  THROW 50000,'EXPECT_FAIL: a new mastery row is not untested',1;

-- the fourth state is the ceiling: an invented one is refused
BEGIN TRY
  INSERT INTO UserCategoryMasteries (UserId,CategoryId,CertId,State) VALUES (@u,@cat2,@c2,'fluent');
  THROW 50000,'EXPECT_REJECT FAILED: invented mastery state accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an invented mastery state as expected';
END CATCH

-- a mastery row cannot straddle two certificates: the category belongs to IELTS, the row claims TOEIC
BEGIN TRY
  INSERT INTO UserCategoryMasteries (UserId,CategoryId,CertId) VALUES (@u,@cat2,@c);
  THROW 50000,'EXPECT_REJECT FAILED: mastery row straddled two certificates',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_UCM_CategoryCert%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by something other than FK_UCM_CategoryCert',1;
  PRINT 'rejected a mastery row whose category belongs to another certificate (FK_UCM_CategoryCert)';
END CATCH

-- the counters must agree: more correct answers than answers is a bug, not a rounding
BEGIN TRY
  UPDATE UserCategoryMasteries SET AnsweredCount=2, CorrectCount=3 WHERE UserId=@u AND CategoryId=@cat;
  THROW 50000,'EXPECT_REJECT FAILED: correct answers exceeded total answers',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected correct answers exceeding total answers';
END CATCH

-- a valid question-history row is accepted, and a wrong answer's cooldown is a real moment
INSERT INTO UserQuestionHistories (UserId,QuestionId,LastIsCorrect,CooldownUntil)
  VALUES (@u,@q1,0,SYSDATETIMEOFFSET());
PRINT 'valid question history accepted';
-- the same learner/question pair cannot be filed twice: it is the cooldown row, not a log
BEGIN TRY
  INSERT INTO UserQuestionHistories (UserId,QuestionId,LastIsCorrect) VALUES (@u,@q1,1);
  THROW 50000,'EXPECT_REJECT FAILED: duplicate question history accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a duplicate question history row as expected';
END CATCH

-- a right answer writes NULL, which is what lets the picker serve the question again
UPDATE UserQuestionHistories SET LastIsCorrect=1, CooldownUntil=NULL WHERE UserId=@u AND QuestionId=@q1;
IF EXISTS (SELECT 1 FROM UserQuestionHistories WHERE UserId=@u AND QuestionId=@q1 AND CooldownUntil IS NOT NULL)
  THROW 50000,'EXPECT_FAIL: clearing a cooldown did not take',1;
PRINT 'cleared cooldown accepted';

-- SELECT * never sees this schema; assert the two columns the picker reads are nullable
IF (SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS
      WHERE TABLE_NAME='UserQuestionHistories' AND COLUMN_NAME='CooldownUntil') <> 'YES'
  THROW 50000,'EXPECT_FAIL: CooldownUntil is not nullable',1;
IF (SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS
      WHERE TABLE_NAME='UserCategoryMasteries' AND COLUMN_NAME='NextReviewDueAt') <> 'YES'
  THROW 50000,'EXPECT_FAIL: NextReviewDueAt is not nullable',1;
PRINT 'both scheduler dates are nullable as designed';
