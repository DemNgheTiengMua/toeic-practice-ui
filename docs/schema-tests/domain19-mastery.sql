-- docs/schema-tests/domain19-mastery.sql
-- #155 / #237 / #269: the mastery tables, and the constraints that keep them honest.
--
-- WHAT WENT WITH UserQuestionHistories. That table held one row per (learner, question) - a cooldown
-- clock and a last-answer flag - and it was DROPPED with Questions in #269, because a cooldown over one
-- question is "spaced repetition over one question", which #155 itself replaced with UserCellMasteries
-- (a fact about a cell, not a question). So these four assertions are gone, named:
--
--   * "a valid (learner, question) history row is accepted"
--   * "the same learner/question pair cannot be filed twice"      (PK_UserQuestionHistories)
--   * "a right answer clears the cooldown"
--   * "UserQuestionHistories.CooldownUntil is nullable"           (#155's scheduler date)
--
-- Their replacement is already asserted below and in domain45: the cooldown axis is
-- UserCellMasteries.NextReviewDueAt, whose nullability this file still checks, and the "one row per
-- learner per cell" rule is that table's primary key. Nothing about the retired per-question clock is
-- silently re-expressed: a cell *can* be served again, and the four-state ceiling is what governs it.
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

-- The per-cell record is the cooldown axis now; domain45 owns it in full (its writer declaration, its
-- four-state transition table and its evidence CHECK). What this file still owns is the category
-- mastery's own scheduler date: "no next review due" is a real answer, so the column is nullable.
IF (SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS
      WHERE TABLE_NAME='UserCategoryMasteries' AND COLUMN_NAME='NextReviewDueAt') <> 'YES'
  THROW 50000,'EXPECT_FAIL: NextReviewDueAt is not nullable',1;
PRINT 'the category mastery scheduler date is nullable as designed';
