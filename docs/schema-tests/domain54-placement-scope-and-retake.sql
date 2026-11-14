SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #289: placement scope and paid retake persistence invariants:
--   1. ExamAttempts.PlacementScope accepts NULL, 'all_skills', 'listening_and_reading',
--      and refuses anything else via CK_Attempts_PlacementScope.
--   2. PlacementTests.Scope defaults to 'all_skills', accepts 'listening_and_reading',
--      and refuses anything else via CK_Placement_Scope.
--   3. PlacementTests.CreditTxnId is nullable (first placement is free), pins ownership
--      to the same user via FK_Placement_CreditTxn, and refuses linking two placements to
--      the same ledger row via UX_PlacementTests_CreditTxn.
--   4. PlacementTests.IdempotencyKey allows multiple NULLs per user, rejects duplicate
--      non-NULL (UserId, IdempotencyKey) via UX_PlacementTests_Idempotency, and allows
--      two different users to supply the same key.

INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
DECLARE @rLearner INT = (SELECT RoleId FROM Roles WHERE Code = 'learner');

INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
VALUES (@rLearner, 'scope-u1@test.local', 0x01, N'Learner One');
DECLARE @u1 INT = SCOPE_IDENTITY();

INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
VALUES (@rLearner, 'scope-u2@test.local', 0x01, N'Learner Two');
DECLARE @u2 INT = SCOPE_IDENTITY();

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC_289', N'TOEIC', 1);
DECLARE @c INT = SCOPE_IDENTITY();
INSERT INTO Exams (CertId, Purpose, Name, Status, DurationMinutes)
VALUES (@c, 'placement', N'Placement Exam 289', 'published', 45);
DECLARE @exam INT = SCOPE_IDENTITY();

INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway)
VALUES (@u1, 10, 'paid', 100000, 'DH-289-001', NEXT VALUE FOR SEQ_PaymentNo, 'mock');
DECLARE @o1 INT = SCOPE_IDENTITY();
INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway)
VALUES (@u2, 10, 'paid', 100000, 'DH-289-002', NEXT VALUE FOR SEQ_PaymentNo, 'mock');
DECLARE @o2 INT = SCOPE_IDENTITY();

INSERT INTO CreditTransactions (UserId, Reason, Delta, OrderId)
VALUES (@u1, 'purchase', 10, @o1), (@u2, 'purchase', 10, @o2);

INSERT INTO CreditTransactions (UserId, Reason, Delta, Note)
VALUES (@u1, 'placement_retake', -3, N'Placement retake: listening and reading');
DECLARE @u1Txn INT = SCOPE_IDENTITY();

INSERT INTO CreditTransactions (UserId, Reason, Delta, Note)
VALUES (@u2, 'placement_retake', -5, N'Placement retake: all 4 skills');
DECLARE @u2Txn INT = SCOPE_IDENTITY();

-- ===== 1. ExamAttempts.PlacementScope ========================================================
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@u1, @exam, @c, 'in_progress', DATEADD(hour, 72, SYSDATETIMEOFFSET()));
DECLARE @attNull INT = SCOPE_IDENTITY();

INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, PlacementScope)
VALUES (@u1, @exam, @c, 'in_progress', DATEADD(hour, 72, SYSDATETIMEOFFSET()), 'all_skills');
DECLARE @attAll INT = SCOPE_IDENTITY();

INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, PlacementScope)
VALUES (@u1, @exam, @c, 'in_progress', DATEADD(hour, 72, SYSDATETIMEOFFSET()), 'listening_and_reading');
DECLARE @attLR INT = SCOPE_IDENTITY();

BEGIN TRY
  INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, PlacementScope)
  VALUES (@u1, @exam, @c, 'in_progress', DATEADD(hour, 72, SYSDATETIMEOFFSET()), 'speaking_only');
  THROW 50000, 'EXPECT_REJECT FAILED: invalid ExamAttempts.PlacementScope was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Attempts_PlacementScope%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by wrong guard, not CK_Attempts_PlacementScope', 1;
  PRINT 'rejected invalid ExamAttempts.PlacementScope via CK_Attempts_PlacementScope';
END CATCH

-- ===== 2. PlacementTests.Scope default + valid + invalid =====================================
INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId)
VALUES (@u1, @c, @exam, @attNull);
DECLARE @pFirst INT = SCOPE_IDENTITY();

IF (SELECT Scope FROM PlacementTests WHERE PlacementId = @pFirst) <> 'all_skills'
  THROW 50000, 'EXPECT_REJECT FAILED: PlacementTests.Scope did not default to all_skills', 1;

BEGIN TRY
  INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Scope)
  VALUES (@u1, @c, @exam, @attAll, 'reading_only');
  THROW 50000, 'EXPECT_REJECT FAILED: invalid PlacementTests.Scope was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Placement_Scope%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by wrong guard, not CK_Placement_Scope', 1;
  PRINT 'rejected invalid PlacementTests.Scope via CK_Placement_Scope';
END CATCH

-- ===== 3. CreditTxnId ownership & uniqueness =================================================
-- Another user's ledger row is rejected by FK_Placement_CreditTxn
BEGIN TRY
  INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Scope, CreditTxnId)
  VALUES (@u1, @c, @exam, @attAll, 'listening_and_reading', @u2Txn);
  THROW 50000, 'EXPECT_REJECT FAILED: cross-user CreditTxnId was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_Placement_CreditTxn%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by wrong guard, not FK_Placement_CreditTxn', 1;
  PRINT 'rejected cross-user CreditTxnId via FK_Placement_CreditTxn';
END CATCH

-- Valid retake row with own CreditTxnId and IdempotencyKey
INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Scope, CreditTxnId, IdempotencyKey)
VALUES (@u1, @c, @exam, @attLR, 'listening_and_reading', @u1Txn, 'idem-key-1');

-- Reusing the same CreditTxnId on another placement is rejected by UX_PlacementTests_CreditTxn
BEGIN TRY
  INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Scope, CreditTxnId, IdempotencyKey)
  VALUES (@u1, @c, @exam, @attAll, 'all_skills', @u1Txn, 'idem-key-2');
  THROW 50000, 'EXPECT_REJECT FAILED: duplicate CreditTxnId was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UX_PlacementTests_CreditTxn%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by wrong guard, not UX_PlacementTests_CreditTxn', 1;
  PRINT 'rejected duplicate CreditTxnId via UX_PlacementTests_CreditTxn';
END CATCH

-- ===== 4. IdempotencyKey NULLs & per-user uniqueness =========================================
-- Multiple NULL IdempotencyKeys for the same user are allowed (already @pFirst had NULL)
INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Scope, IdempotencyKey)
VALUES (@u1, @c, @exam, @attAll, 'all_skills', NULL);

-- Duplicate non-NULL (UserId, IdempotencyKey) is rejected by UX_PlacementTests_Idempotency
BEGIN TRY
  INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Scope, IdempotencyKey)
  VALUES (@u1, @c, @exam, NULL, 'listening_and_reading', 'idem-key-1');
  THROW 50000, 'EXPECT_REJECT FAILED: duplicate (UserId, IdempotencyKey) was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%UX_PlacementTests_Idempotency%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by wrong guard, not UX_PlacementTests_Idempotency', 1;
  PRINT 'rejected duplicate (UserId, IdempotencyKey) via UX_PlacementTests_Idempotency';
END CATCH

-- Another user CAN reuse the same IdempotencyKey
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, PlacementScope)
VALUES (@u2, @exam, @c, 'in_progress', DATEADD(hour, 72, SYSDATETIMEOFFSET()), 'all_skills');
DECLARE @u2Att INT = SCOPE_IDENTITY();

INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Scope, CreditTxnId, IdempotencyKey)
VALUES (@u2, @c, @exam, @u2Att, 'all_skills', @u2Txn, 'idem-key-1');
PRINT 'accepted same IdempotencyKey for distinct user';
