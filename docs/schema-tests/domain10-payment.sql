SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Packages (Name,Credits,Price,IsActive) VALUES (N'10 credits',10,100000,1); DECLARE @pk INT=SCOPE_IDENTITY();
INSERT INTO Orders (UserId,PackageId,Status,Amount) VALUES (@u,@pk,'pending',100000); DECLARE @o INT=SCOPE_IDENTITY();
-- reject (Review Focus #5): a second pending order for the same user
BEGIN TRY
  INSERT INTO Orders (UserId,PackageId,Status,Amount) VALUES (@u,@pk,'pending',100000);
  THROW 50000,'EXPECT_REJECT FAILED: second pending order accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected second pending order as expected';
END CATCH
-- credit the wallet via a paid order, then check the balance view
UPDATE Orders SET Status='paid' WHERE OrderId=@o;
INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u,'purchase',10,@o);
DECLARE @bal INT=(SELECT Balance FROM vw_UserCreditBalance WHERE UserId=@u);
IF @bal <> 10 THROW 50000,'balance expected 10',1;
PRINT 'wallet balance = 10 as expected';
-- reject (Review Focus #4): a debit that overdraws the wallet
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u,'admin_revoke',-999,NULL);
  THROW 50000,'EXPECT_REJECT FAILED: negative balance accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected overdrawing debit as expected';
END CATCH
-- reject: reason/source mismatch (purchase must carry OrderId, not AttemptId)
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,AttemptId) VALUES (@u,'purchase',5,1);
  THROW 50000,'EXPECT_REJECT FAILED: purchase w/ AttemptId accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected reason/source mismatch as expected';
END CATCH
