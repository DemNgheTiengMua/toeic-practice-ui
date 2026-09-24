-- docs/schema-tests/domain02-identity.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code, Name) VALUES ('student', N'Student');
DECLARE @r INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC', N'TOEIC', 1);
DECLARE @c INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash, ActiveCertId)
  VALUES (@r, 'a@b.com', 0x00, @c);
PRINT 'valid user accepted';
-- reject: duplicate email (one account per email)
BEGIN TRY
  INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@r,'a@b.com',0x00);
  THROW 50000,'EXPECT_REJECT FAILED: dup email accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected dup email as expected';
END CATCH
