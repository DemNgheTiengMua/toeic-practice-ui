SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #288: the credit ledger accepts a placement_retake charge as its own reason, with one legal
-- shape: negative delta, no admin actor, no source row (no order, no attempt, no booking), and a
-- mandatory note naming what the learner paid for.
--
-- What this file proves, all of it at the database level:
--   1. a valid placement_retake (negative delta, AdminUserId NULL, no source, Note present) inserts;
--   2. vw_UserCreditBalance sums it alongside other ledger rows;
--   3. a positive delta is refused by CK_CreditTxn_DeltaSign;
--   4. a zero delta is refused by CK_CreditTxn_NonZero / CK_CreditTxn_DeltaSign;
--   5. naming an OrderId, AttemptId, or BookingId is refused by CK_CreditTxn_Source;
--   6. naming an AdminUserId is refused by CK_CreditTxn_AdminActor;
--   7. omitting Note is refused by CK_CreditTxn_AdminActor;
--   8. overdrawing the wallet is refused by trg_CreditTransactions_NoNegativeBalance (50020);
--   9. deleting or rewriting a placement_retake row is refused by the append-only / no-rewrite triggers.
--
-- Every refusal asserts the named guard, not merely that an error occurred, so a fixture broken on
-- an unrelated FK cannot pass as proof of the rule under test.

INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner'), ('admin', N'Admin');
DECLARE @rLearner INT = (SELECT RoleId FROM Roles WHERE Code = 'learner');
DECLARE @rAdmin   INT = (SELECT RoleId FROM Roles WHERE Code = 'admin');

INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
VALUES (@rLearner, 'retake-learner@test.local', 0x01, N'Retake Learner');
DECLARE @u INT = SCOPE_IDENTITY();

INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
VALUES (@rAdmin, 'retake-admin@test.local', 0x01, N'Retake Admin');
DECLARE @admin INT = SCOPE_IDENTITY();

-- Source fixtures belonging to @u so source-refusal tests hit CK_CreditTxn_Source, not an FK.
INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway)
VALUES (@u, 10, 'paid', 100000, 'DH-PR-0001', NEXT VALUE FOR SEQ_PaymentNo, 'mock');
DECLARE @o INT = SCOPE_IDENTITY();
INSERT INTO Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway)
VALUES (@u, 10, 'paid', 100000, 'DH-PR-0002', NEXT VALUE FOR SEQ_PaymentNo, 'mock');
DECLARE @oUnused INT = SCOPE_IDENTITY();

INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC_PR', N'TOEIC', 1);
DECLARE @c INT = SCOPE_IDENTITY();
INSERT INTO Exams (CertId, Purpose, Name, Status, DurationMinutes)
VALUES (@c, 'placement', N'Placement Exam', 'published', 45);
DECLARE @exam INT = SCOPE_IDENTITY();
INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt)
VALUES (@u, @exam, @c, 'in_progress', DATEADD(hour, 72, SYSDATETIMEOFFSET()));
DECLARE @attempt INT = SCOPE_IDENTITY();

INSERT INTO MentorProfiles (DisplayName, CertId, MeetLink, PriceCredits, IsActive)
VALUES (N'Coach', @c, N'https://meet.google.com/abc-defg-hij', 1, 1);
DECLARE @mentor INT = SCOPE_IDENTITY();
INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
VALUES (@mentor, '2026-10-01T09:00:00+07:00', '2026-10-01T10:00:00+07:00', 'booked');
DECLARE @mslot INT = SCOPE_IDENTITY();
INSERT INTO Bookings (UserId, MentorId, SlotId, PriceCredits, Status)
VALUES (@u, @mentor, @mslot, 1, 'done');
DECLARE @booking INT = SCOPE_IDENTITY();

-- Seed 10 credits so a -5 placement_retake leaves balance = 5.
INSERT INTO CreditTransactions (UserId, Reason, Delta, OrderId)
VALUES (@u, 'purchase', 10, @o);

-- ===== 1. accept: valid placement_retake =====================================================
INSERT INTO CreditTransactions (UserId, Reason, Delta, Note)
VALUES (@u, 'placement_retake', -5, N'Placement retake: all 4 skills');
DECLARE @retakeTxn INT = SCOPE_IDENTITY();
PRINT 'accepted: negative placement_retake with note, no admin actor, and no source row';

-- ===== 2. balance view equals the ledger sum =================================================
DECLARE @bal INT = (SELECT Balance FROM vw_UserCreditBalance WHERE UserId = @u);
IF @bal <> 5
  THROW 50000, 'EXPECT_REJECT FAILED: vw_UserCreditBalance did not reflect placement_retake debit (expected 5)', 1;
PRINT 'accepted: vw_UserCreditBalance equals ledger sum (5)';

-- ===== 3. reject: positive delta =============================================================
BEGIN TRY
  INSERT INTO CreditTransactions (UserId, Reason, Delta, Note)
  VALUES (@u, 'placement_retake', 5, N'positive retake');
  THROW 50000, 'EXPECT_REJECT FAILED: a positive placement_retake was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_DeltaSign%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_CreditTxn_DeltaSign', 1;
  PRINT 'rejected positive placement_retake via CK_CreditTxn_DeltaSign';
END CATCH

-- ===== 4. reject: zero delta =================================================================
BEGIN TRY
  INSERT INTO CreditTransactions (UserId, Reason, Delta, Note)
  VALUES (@u, 'placement_retake', 0, N'zero retake');
  THROW 50000, 'EXPECT_REJECT FAILED: a zero placement_retake was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_NonZero%'
     AND ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_DeltaSign%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_CreditTxn_NonZero/DeltaSign', 1;
  PRINT 'rejected zero placement_retake';
END CATCH

-- ===== 5a. reject: naming an OrderId =========================================================
BEGIN TRY
  INSERT INTO CreditTransactions (UserId, Reason, Delta, OrderId, Note)
  VALUES (@u, 'placement_retake', -3, @oUnused, N'with order');
  THROW 50000, 'EXPECT_REJECT FAILED: placement_retake with OrderId was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_Source%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by the wrong guard for OrderId, not CK_CreditTxn_Source', 1;
  PRINT 'rejected placement_retake with OrderId via CK_CreditTxn_Source';
END CATCH

-- ===== 5b. reject: naming an AttemptId =======================================================
BEGIN TRY
  INSERT INTO CreditTransactions (UserId, Reason, Delta, AttemptId, Note)
  VALUES (@u, 'placement_retake', -3, @attempt, N'with attempt');
  THROW 50000, 'EXPECT_REJECT FAILED: placement_retake with AttemptId was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_Source%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by the wrong guard for AttemptId, not CK_CreditTxn_Source', 1;
  PRINT 'rejected placement_retake with AttemptId via CK_CreditTxn_Source';
END CATCH

-- ===== 5c. reject: naming a BookingId ========================================================
BEGIN TRY
  INSERT INTO CreditTransactions (UserId, Reason, Delta, BookingId, Note)
  VALUES (@u, 'placement_retake', -3, @booking, N'with booking');
  THROW 50000, 'EXPECT_REJECT FAILED: placement_retake with BookingId was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_Source%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by the wrong guard for BookingId, not CK_CreditTxn_Source', 1;
  PRINT 'rejected placement_retake with BookingId via CK_CreditTxn_Source';
END CATCH

-- ===== 6. reject: naming an AdminUserId ======================================================
BEGIN TRY
  INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
  VALUES (@u, 'placement_retake', -3, @admin, N'with admin');
  THROW 50000, 'EXPECT_REJECT FAILED: placement_retake with AdminUserId was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_AdminActor%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by the wrong guard for AdminUserId, not CK_CreditTxn_AdminActor', 1;
  PRINT 'rejected placement_retake with AdminUserId via CK_CreditTxn_AdminActor';
END CATCH

-- ===== 7. reject: missing Note ===============================================================
BEGIN TRY
  INSERT INTO CreditTransactions (UserId, Reason, Delta)
  VALUES (@u, 'placement_retake', -3);
  THROW 50000, 'EXPECT_REJECT FAILED: placement_retake without Note was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_AdminActor%'
    THROW 50000, 'EXPECT_REJECT FAILED: refused by the wrong guard for NULL Note, not CK_CreditTxn_AdminActor', 1;
  PRINT 'rejected placement_retake without Note via CK_CreditTxn_AdminActor';
END CATCH

-- ===== 8. reject: overdrawing balance ========================================================
BEGIN TRY
  INSERT INTO CreditTransactions (UserId, Reason, Delta, Note)
  VALUES (@u, 'placement_retake', -99, N'overdraw');
  THROW 50000, 'EXPECT_REJECT FAILED: overdrawing placement_retake was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50020
    THROW 50000, 'EXPECT_REJECT FAILED: refused by the wrong guard, not trg_CreditTransactions_NoNegativeBalance', 1;
  PRINT 'rejected overdrawing placement_retake via trg_CreditTransactions_NoNegativeBalance';
END CATCH

-- ===== 9. reject: deleting or rewriting a placement_retake row ===============================
BEGIN TRY
  DELETE FROM CreditTransactions WHERE TxnId = @retakeTxn;
  THROW 50000, 'EXPECT_REJECT FAILED: placement_retake row was deleted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50021
    THROW 50000, 'EXPECT_REJECT FAILED: refused by the wrong guard, not trg_CreditTransactions_AppendOnly', 1;
  PRINT 'rejected deleting placement_retake row via trg_CreditTransactions_AppendOnly';
END CATCH

BEGIN TRY
  UPDATE CreditTransactions SET Delta = -1 WHERE TxnId = @retakeTxn;
  THROW 50000, 'EXPECT_REJECT FAILED: placement_retake row was rewritten', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50028
    THROW 50000, 'EXPECT_REJECT FAILED: refused by the wrong guard, not trg_CreditTransactions_NoRewrite', 1;
  PRINT 'rejected rewriting placement_retake row via trg_CreditTransactions_NoRewrite';
END CATCH
