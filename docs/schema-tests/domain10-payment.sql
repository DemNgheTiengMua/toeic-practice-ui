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

-- ===== Batch 1: ledger & money integrity =====
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'v@x.com',0x00); DECLARE @u2 INT=SCOPE_IDENTITY();

-- reject (C-1): the ledger is append-only, DELETE must never be allowed
BEGIN TRY
  DELETE FROM CreditTransactions WHERE UserId=@u AND Reason='purchase';
  THROW 50000,'EXPECT_REJECT FAILED: ledger row deleted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected ledger row delete as expected: ' + ERROR_MESSAGE();
END CATCH

-- reject (H-1): crediting user B's wallet from user A's order
INSERT INTO Orders (UserId,PackageId,Status,Amount) VALUES (@u,@pk,'paid',100000); DECLARE @o2 INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u2,'purchase',10,@o2);
  THROW 50000,'EXPECT_REJECT FAILED: user B credited from user A''s order',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-user order credit as expected: ' + ERROR_MESSAGE();
END CATCH

-- reject (H-2): a second purchase txn on the same already-funded OrderId
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u,'purchase',500,@o);
  THROW 50000,'EXPECT_REJECT FAILED: second purchase on same order accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected second purchase on same order as expected: ' + ERROR_MESSAGE();
END CATCH

-- reject (H-2): a purchase against a still-pending (unpaid) order
INSERT INTO Orders (UserId,PackageId,Status,Amount) VALUES (@u,@pk,'pending',100000); DECLARE @o3 INT=SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u,'purchase',10,@o3);
  THROW 50000,'EXPECT_REJECT FAILED: purchase against pending order accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected purchase against pending order as expected: ' + ERROR_MESSAGE();
END CATCH

-- reject (H-2): a purchase whose Delta does not equal the package's Credits
UPDATE Orders SET Status='paid' WHERE OrderId=@o3;
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u,'purchase',5,@o3);
  THROW 50000,'EXPECT_REJECT FAILED: purchase delta != package credits accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected purchase delta/credits mismatch as expected: ' + ERROR_MESSAGE();
END CATCH

-- reject (M-16): a complaint resolved by another user's admin_grant txn
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO MentorProfiles (DisplayName,CertId,Type,PricePerSlot,IsActive) VALUES (N'Coach',@c,'ai',0,1); DECLARE @m INT=SCOPE_IDENTITY();
INSERT INTO Bookings (UserId,MentorId,MentorType,Status) VALUES (@u,@m,'ai','done'); DECLARE @bk INT=SCOPE_IDENTITY();
INSERT INTO Complaints (BookingId,UserId,Reason) VALUES (@bk,@u,N'no show'); DECLARE @cp INT=SCOPE_IDENTITY();
INSERT INTO CreditTransactions (UserId,Reason,Delta) VALUES (@u2,'admin_grant',5); DECLARE @grantTxn INT=SCOPE_IDENTITY();
BEGIN TRY
  UPDATE Complaints SET Status='resolved', ResolvedByGrantTxnId=@grantTxn WHERE ComplaintId=@cp;
  THROW 50000,'EXPECT_REJECT FAILED: complaint resolved by another user''s grant accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-user grant resolution as expected: ' + ERROR_MESSAGE();
END CATCH

-- ===== re-review residuals: the ledger is IMMUTABLE, not merely undeletable =====
-- reject (R2): rewriting a posted transaction's Delta
INSERT INTO Orders (UserId,PackageId,Status,Amount) VALUES (@u,@pk,'pending',100000); DECLARE @o4 INT=SCOPE_IDENTITY();
UPDATE Orders SET Status='paid', PaidAt=SYSDATETIMEOFFSET() WHERE OrderId=@o4;
INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u,'purchase',10,@o4); DECLARE @txnR INT=SCOPE_IDENTITY();
BEGIN TRY
  UPDATE CreditTransactions SET Delta=10000 WHERE TxnId=@txnR;
  THROW 50000,'EXPECT_REJECT FAILED: ledger Delta rewritten after posting',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected ledger Delta rewrite as expected';
END CATCH

-- reject (R2b): severing a purchase from its order by rewriting Reason/source.
-- This is the dangerous one: it would free UX_CreditTxn_Order to credit that order AGAIN.
BEGIN TRY
  UPDATE CreditTransactions SET Reason='admin_grant', OrderId=NULL WHERE TxnId=@txnR;
  THROW 50000,'EXPECT_REJECT FAILED: ledger Reason/source rewritten after posting',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected ledger Reason/source rewrite as expected';
END CATCH

-- reject (R3): un-paying an order that has already been credited
BEGIN TRY
  UPDATE Orders SET Status='pending', PaidAt=NULL WHERE OrderId=@o4;
  THROW 50000,'EXPECT_REJECT FAILED: credited order reverted to pending',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected un-paying a credited order as expected';
END CATCH

-- accept: an UNCREDITED pending order can still be expired (the stale-order proc must keep working)
INSERT INTO Orders (UserId,PackageId,Status,Amount) VALUES (@u2,@pk,'pending',100000); DECLARE @o5 INT=SCOPE_IDENTITY();
UPDATE Orders SET Status='expired' WHERE OrderId=@o5;
PRINT 'accepted expiring an uncredited pending order';

-- accept: admin_grant/admin_revoke (all four source columns NULL) stay legal under MATCH SIMPLE,
-- and the ledger triggers are set-based (multi-row insert must work)
INSERT INTO CreditTransactions (UserId,Reason,Delta) VALUES (@u,'admin_grant',1),(@u,'admin_grant',2);
PRINT 'accepted multi-row all-NULL-source admin grants';
