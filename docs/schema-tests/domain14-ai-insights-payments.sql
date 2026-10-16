SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- Domain 14: ADR-0003 (payOS payment events, PaymentNo) and ADR-0004 (AiInsights)
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'ai@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'other@x.com',0x00); DECLARE @u2 INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name) VALUES ('TOEIC',N'TOEIC'); DECLARE @c INT=SCOPE_IDENTITY();

-- UI-52: every user has a time zone, defaulting to Vietnam
IF (SELECT TimeZoneId FROM Users WHERE UserId=@u) <> 'Asia/Ho_Chi_Minh'
  THROW 50000,'TimeZoneId default expected Asia/Ho_Chi_Minh',1;
PRINT 'TimeZoneId defaults to Asia/Ho_Chi_Minh as expected';

-- ============ ADR-0003: PaymentNo + PaymentEvents ============
INSERT INTO Packages (Name,Credits,Price,IsActive) VALUES (N'10 credits',10,100000,1); DECLARE @pk INT=SCOPE_IDENTITY();
DECLARE @pn BIGINT = NEXT VALUE FOR SEQ_PaymentNo;
INSERT INTO Orders (UserId,PackageId,Status,Amount,OrderCode,PaymentNo,Gateway)
  VALUES (@u,@pk,'pending',100000,'DH-AI-0001',@pn,'payos'); DECLARE @o INT=SCOPE_IDENTITY();

-- reject: a gateway the app has no implementation for
BEGIN TRY
  INSERT INTO Orders (UserId,PackageId,Status,Amount,OrderCode,PaymentNo,Gateway)
    VALUES (@u2,@pk,'pending',100000,'DH-AI-0002',NEXT VALUE FOR SEQ_PaymentNo,'vnpay');
  THROW 50000,'EXPECT_REJECT FAILED: unknown gateway accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected unknown gateway as expected';
END CATCH

-- reject: two orders sharing one merchant payment number
BEGIN TRY
  INSERT INTO Orders (UserId,PackageId,Status,Amount,OrderCode,PaymentNo,Gateway)
    VALUES (@u2,@pk,'pending',100000,'DH-AI-0003',@pn,'payos');
  THROW 50000,'EXPECT_REJECT FAILED: duplicate PaymentNo accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected duplicate PaymentNo as expected';
END CATCH

-- the return redirect is recorded but may never be the event that grants credit
INSERT INTO PaymentEvents (OrderId,Source,ReportedStatus,Note)
  VALUES (@o,'return_redirect','paid',N'learner came back from the gateway');
PRINT 'logged an untrusted return_redirect as expected';
BEGIN TRY
  INSERT INTO PaymentEvents (OrderId,Source,ReportedStatus,WasApplied) VALUES (@o,'return_redirect','paid',1);
  THROW 50000,'EXPECT_REJECT FAILED: return_redirect granted credit',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected applying a return_redirect as expected';
END CATCH

-- reject: an unverified webhook may not grant credit either
BEGIN TRY
  INSERT INTO PaymentEvents (OrderId,Source,ReportedStatus,SignatureValid,WasApplied)
    VALUES (@o,'webhook','paid',0,1);
  THROW 50000,'EXPECT_REJECT FAILED: unverified webhook granted credit',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected applying an unverified webhook as expected';
END CATCH

-- reject: only a webhook can carry a verified signature
BEGIN TRY
  INSERT INTO PaymentEvents (OrderId,Source,ReportedStatus,SignatureValid)
    VALUES (@o,'status_query','paid',1);
  THROW 50000,'EXPECT_REJECT FAILED: non-webhook claimed a valid signature',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a signature on a non-webhook source as expected';
END CATCH

-- accept: the verified webhook that actually pays the order, then the repeat that changes nothing
INSERT INTO PaymentEvents (OrderId,Source,ReportedStatus,SignatureValid,WasApplied)
  VALUES (@o,'webhook','paid',1,1);
UPDATE Orders SET Status='paid', PaidAt=SYSDATETIMEOFFSET() WHERE OrderId=@o;
INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u,'purchase',10,@o);
INSERT INTO PaymentEvents (OrderId,Source,ReportedStatus,SignatureValid,WasApplied)
  VALUES (@o,'webhook','paid',1,0);
IF (SELECT COUNT(*) FROM PaymentEvents WHERE OrderId=@o AND WasApplied=1) <> 1
  THROW 50000,'exactly one applied payment event expected',1;
PRINT 'verified webhook applied once, repeat logged as expected';

-- the ledger still refuses a second purchase credit for the same order
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId) VALUES (@u,'purchase',10,@o);
  THROW 50000,'EXPECT_REJECT FAILED: order credited twice',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected crediting the same order twice as expected';
END CATCH

-- ============ ADR-0004: AiInsights ============
-- accept: a dashboard insight names no source
INSERT INTO AiInsights (UserId,CertId,Scope,Status,Narrative,Trend,Model,SourceFingerprint,CompletedAt)
  VALUES (@u,@c,'dashboard','done',N'Reading is up; Part 5 slipped.','improving','claude-sonnet-3.7',
          REPLICATE('a',64),SYSDATETIMEOFFSET());
DECLARE @ins INT=SCOPE_IDENTITY();
PRINT 'stored a dashboard insight as expected';

-- reject: reusing one fingerprint for the same user and scope (the no-repeat-call guard)
BEGIN TRY
  INSERT INTO AiInsights (UserId,CertId,Scope,SourceFingerprint) VALUES (@u,@c,'dashboard',REPLICATE('a',64));
  THROW 50000,'EXPECT_REJECT FAILED: duplicate fingerprint accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a duplicate insight fingerprint as expected';
END CATCH

-- reject: a dashboard insight that names a source anyway
BEGIN TRY
  INSERT INTO AiInsights (UserId,CertId,Scope,AttemptId,SourceFingerprint)
    VALUES (@u,@c,'dashboard',1,REPLICATE('b',64));
  THROW 50000,'EXPECT_REJECT FAILED: dashboard insight named a source',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a dashboard insight naming a source as expected';
END CATCH

-- reject: a session-scoped insight with no session
BEGIN TRY
  INSERT INTO AiInsights (UserId,CertId,Scope,SourceFingerprint)
    VALUES (@u,@c,'practice_session',REPLICATE('c',64));
  THROW 50000,'EXPECT_REJECT FAILED: scoped insight with no source',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a scoped insight with no source as expected';
END CATCH

-- reject: done without the prose the learner is supposed to read
BEGIN TRY
  INSERT INTO AiInsights (UserId,CertId,Scope,Status,SourceFingerprint,CompletedAt)
    VALUES (@u,@c,'dashboard','done',REPLICATE('d',64),SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: done insight with no narrative',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a done insight with no narrative as expected';
END CATCH

-- accept: a recommendation produced by that insight
INSERT INTO Recommendations (UserId,CertId,InsightId,Text,Severity)
  VALUES (@u,@c,@ins,N'Drill Part 5 for ten minutes a day.','minor');
PRINT 'linked a recommendation to its insight as expected';

-- reject: a recommendation claiming both producers at once
DECLARE @badIns INT=@ins;
BEGIN TRY
  INSERT INTO Recommendations (UserId,CertId,InsightId,GradingId,Text)
    VALUES (@u,@c,@badIns,1,N'both producers');
  THROW 50000,'EXPECT_REJECT FAILED: recommendation had two producers',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a recommendation with two producers as expected';
END CATCH

-- reject (H-1): a recommendation attaching another learner's insight
BEGIN TRY
  INSERT INTO Recommendations (UserId,CertId,InsightId,Text) VALUES (@u2,@c,@badIns,N'not your insight');
  THROW 50000,'EXPECT_REJECT FAILED: cross-user insight accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected another learner''s insight as expected';
END CATCH
GO
PRINT 'DOMAIN 14 AI INSIGHTS + PAYMENTS PASSED';
