-- docs/schema-tests/domain05-practice.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'READ',N'R','reading',1);
DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Code,Name,OptionCount,DisplayOrder) VALUES (@sk,@c,'P5',N'IC',4,1);
DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO PracticeSessions (UserId,SectionId,QuestionCount,Status,CorrectCount)
  VALUES (@u,@sec,10,'in_progress',0);
PRINT 'valid practice session accepted';
-- reject: bad status
BEGIN TRY
  INSERT INTO PracticeSessions (UserId,SectionId,QuestionCount,Status) VALUES (@u,@sec,1,'exploding');
  THROW 50000,'EXPECT_REJECT FAILED: bad status accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad status as expected';
END CATCH
