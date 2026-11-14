SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #280: a refund has two methods, and the schema - not the service - decides which amount column is
-- allowed to hold a value.
--
-- What this file proves, all of it at the level only the database can enforce:
--
--   1. the table exists with the columns the ticket names, and carries NO UpdatedAt and NO rowversion
--      (a refund is append-and-transition only, so there is no mutation timestamp to keep correct);
--   2. method and amount are mutually exclusive AND jointly required: money carries Amount and no
--      CreditCount, credit carries CreditCount and no Amount, and neither may be empty;
--   3. a settled refund is always a money refund;
--   4. a status that records a decision cannot be reached without the deciding actor and timestamp,
--      and the settle stamps belong to 'settled' alone;
--   5. no delete path exists;
--   6. the refund's order and complaint must belong to the refunded learner (the H-1 composite FK);
--   7. the ledger accepts a positive refund_credit carrying a deciding admin and a note, and still
--      refuses a negative one, a missing actor, a missing note, and any source column;
--   8. a credit refund that names a ledger row must name the refund_credit row for exactly its count.
--
-- Every refusal asserts the CONSTRAINT NAME, not merely that something threw. A row refused for the
-- wrong reason stops refusing when the clause it actually hit is edited, and the whole subject of this
-- file is which guard fired. Each refusal's fixture is valid in every other respect, so the guard under
-- test is the only one it can hit.

DECLARE @msg NVARCHAR(2048);

-- ===== the shape, and the two absences ======================================================
DECLARE @required TABLE (ColumnName SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @required (ColumnName) VALUES
  ('UserId'), ('ComplaintId'), ('OrderId'), ('Method'), ('Reason'), ('Status'),
  ('Amount'), ('CreditCount'), ('RequestedByUserId'), ('DecidedByUserId'), ('DecidedAt'),
  ('SettledByUserId'), ('SettledAt'), ('CreditTxnId'), ('CreatedAt');
IF EXISTS (
  SELECT 1 FROM @required r
  WHERE NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS c
                    WHERE c.TABLE_NAME = 'Refunds' AND c.COLUMN_NAME = r.ColumnName))
  THROW 50000, 'EXPECT_REJECT FAILED: a column the ticket names is missing from Refunds', 1;
PRINT 'accepted: Refunds carries every column the ticket names';

-- The absence checks are proved able to SEE a column that exists, or they report "absent" for every
-- name and pass without looking.
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'Refunds' AND COLUMN_NAME = 'Amount')
  THROW 50000, 'EXPECT_REJECT FAILED: the column audit cannot see Refunds.Amount, so its absence checks prove nothing', 1;
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_NAME = 'Refunds' AND COLUMN_NAME = 'UpdatedAt')
  THROW 50000, 'EXPECT_REJECT FAILED: Refunds carries an UpdatedAt, which a table with no update path does not need', 1;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID('Refunds') AND t.name IN ('timestamp','rowversion'))
  THROW 50000, 'EXPECT_REJECT FAILED: Refunds carries a rowversion, which no reader of a refund consumes', 1;
PRINT 'accepted: Refunds carries no UpdatedAt and no rowversion';

-- ===== the fixture ==========================================================================
INSERT INTO Roles (Code,Name) VALUES ('rf280',N'Refund 280'); DECLARE @r INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'learner280@x.com',0x00); DECLARE @u INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'other280@x.com',0x00);   DECLARE @u2 INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'admin280@x.com',0x00);   DECLARE @admin INT = SCOPE_IDENTITY();
INSERT INTO Orders (UserId,Credits,Status,Amount,OrderCode,PaymentNo,Gateway)
  VALUES (@u,10,'paid',100000,'DH-RF-0001',NEXT VALUE FOR SEQ_PaymentNo,'payos'); DECLARE @o INT = SCOPE_IDENTITY();
INSERT INTO Orders (UserId,Credits,Status,Amount,OrderCode,PaymentNo,Gateway)
  VALUES (@u2,10,'paid',100000,'DH-RF-0002',NEXT VALUE FOR SEQ_PaymentNo,'payos'); DECLARE @o2 INT = SCOPE_IDENTITY();
-- payment_issue is the complaint type whose subject is an order (CK_Complaints_TypeMatchesSubject)
INSERT INTO Complaints (OrderId,UserId,Type,Reason) VALUES (@o,@u,'payment_issue',N'charged twice'); DECLARE @cp INT = SCOPE_IDENTITY();

-- ===== 2. method and amount are one decision ==============================================
-- accept: the two legal shapes, each in the only column its method allows
INSERT INTO Refunds (UserId,OrderId,Method,Reason,Status,Amount,RequestedByUserId)
  VALUES (@u,@o,'money',N'duplicate charge','requested',100000.00,@admin);
PRINT 'accepted: a money refund carrying Amount and no CreditCount';

INSERT INTO Refunds (UserId,ComplaintId,Method,Reason,Status,CreditCount,RequestedByUserId)
  VALUES (@u,@cp,'credit',N'goodwill','requested',10,@admin);
PRINT 'accepted: a credit refund carrying CreditCount and no Amount';

-- reject: a money refund that also carries credits
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Amount,CreditCount,RequestedByUserId)
    VALUES (@u,'money',N'both',100000.00,10,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: a money refund carrying CreditCount was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_MethodOwnsAmount%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_MethodOwnsAmount',1;
  PRINT 'rejected a money refund that also carried credits';
END CATCH

-- reject: a credit refund that also carries money
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Amount,CreditCount,RequestedByUserId)
    VALUES (@u,'credit',N'both',100000.00,10,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: a credit refund carrying Amount was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_MethodOwnsAmount%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_MethodOwnsAmount',1;
  PRINT 'rejected a credit refund that also carried money';
END CATCH

-- reject: neither column set - the method names a value and there is none to find
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,RequestedByUserId)
    VALUES (@u,'money',N'empty',@admin);
  THROW 50000,'EXPECT_REJECT FAILED: a money refund with no Amount was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_MethodOwnsAmount%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_MethodOwnsAmount',1;
  PRINT 'rejected a money refund with no amount';
END CATCH

BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,RequestedByUserId)
    VALUES (@u,'credit',N'empty',@admin);
  THROW 50000,'EXPECT_REJECT FAILED: a credit refund with no CreditCount was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_MethodOwnsAmount%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_MethodOwnsAmount',1;
  PRINT 'rejected a credit refund with no credit count';
END CATCH

-- reject: a zero or negative value is not a refund. Same guard, because "> 0" is part of owning an
-- amount: a zero-value row would be a record that says a refund happened when nothing moved.
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Amount,RequestedByUserId)
    VALUES (@u,'money',N'zero',0,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: a zero-amount money refund was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_MethodOwnsAmount%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_MethodOwnsAmount',1;
  PRINT 'rejected a zero-amount money refund';
END CATCH

BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,CreditCount,RequestedByUserId)
    VALUES (@u,'credit',N'zero',0,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: a zero-credit refund was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_MethodOwnsAmount%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_MethodOwnsAmount',1;
  PRINT 'rejected a zero-credit refund';
END CATCH

-- ===== 3. the vocabulary ==================================================================
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Amount,RequestedByUserId)
    VALUES (@u,'bank',N'unknown method',100000.00,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: an unknown refund method was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_Method%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_Method',1;
  PRINT 'rejected an unknown refund method';
END CATCH

BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Status,Amount,RequestedByUserId)
    VALUES (@u,'money',N'unknown status','refunded',100000.00,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: an unknown refund status was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_Status%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_Status',1;
  PRINT 'rejected an unknown refund status';
END CATCH

-- a reason is mandatory on every refund (AC: "no payment leaves without a recorded justification"),
-- and a blank string is not a justification.
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Amount,RequestedByUserId)
    VALUES (@u,'money',N'   ',100000.00,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: a blank refund reason was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_ReasonNotEmpty%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_ReasonNotEmpty',1;
  PRINT 'rejected a blank refund reason';
END CATCH

-- ===== 4. settled is a money-only ending ==================================================
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Status,CreditCount,RequestedByUserId,
                       DecidedByUserId,DecidedAt,SettledByUserId,SettledAt)
    VALUES (@u,'credit',N'settled credit','settled',10,@admin,@admin,SYSDATETIMEOFFSET(),@admin,SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: a credit refund marked settled was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_SettledIsMoney%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_SettledIsMoney',1;
  PRINT 'rejected a settled credit refund';
END CATCH

-- ===== 5. a decision carries its actor and its timestamp ==================================
-- reject: approved with no decider
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Status,Amount,RequestedByUserId)
    VALUES (@u,'money',N'approved undecided','approved',100000.00,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: an approved refund with no deciding actor was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_DecisionStamped%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_DecisionStamped',1;
  PRINT 'rejected an approved refund with no deciding actor';
END CATCH

-- reject: a decider with no timestamp is not an audit trail
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Status,Amount,RequestedByUserId,DecidedByUserId)
    VALUES (@u,'money',N'approved undated','approved',100000.00,@admin,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: an approved refund with no timestamp was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_DecisionStamped%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_DecisionStamped',1;
  PRINT 'rejected a deciding actor with no timestamp';
END CATCH

-- rejected is a decision too: a refusal must name who refused
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Status,Amount,RequestedByUserId)
    VALUES (@u,'money',N'rejected undecided','rejected',100000.00,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: a rejected refund with no deciding actor was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_DecisionStamped%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_DecisionStamped',1;
  PRINT 'rejected a rejected refund with no deciding actor';
END CATCH

-- reject: settle stamps on a row that is not settled
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Status,Amount,RequestedByUserId,
                       DecidedByUserId,DecidedAt,SettledByUserId,SettledAt)
    VALUES (@u,'money',N'approved but settled','approved',100000.00,@admin,
            @admin,SYSDATETIMEOFFSET(),@admin,SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: settle stamps on an approved refund were accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_SettleStamped%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_SettleStamped',1;
  PRINT 'rejected settle stamps on a refund that is not settled';
END CATCH

-- reject: settled without the settler
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Status,Amount,RequestedByUserId,DecidedByUserId,DecidedAt)
    VALUES (@u,'money',N'settled unset','settled',100000.00,@admin,@admin,SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: a settled refund with no settler was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_SettleStamped%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_SettleStamped',1;
  PRINT 'rejected a settled refund with no settler';
END CATCH

-- accept: the full legal money lifecycle, requested -> approved -> settled
INSERT INTO Refunds (UserId,OrderId,Method,Reason,Status,Amount,RequestedByUserId,
                     DecidedByUserId,DecidedAt,SettledByUserId,SettledAt)
  VALUES (@u,@o,'money',N'bank transfer made','settled',100000.00,@admin,
          @admin,SYSDATETIMEOFFSET(),@admin,SYSDATETIMEOFFSET());
DECLARE @settled INT = SCOPE_IDENTITY();
PRINT 'accepted: a fully stamped settled money refund';

-- ===== 6. no delete path ==================================================================
BEGIN TRY
  DELETE FROM Refunds WHERE RefundId = @settled;
  THROW 50000,'EXPECT_REJECT FAILED: a refund was deleted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50041
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not trg_Refunds_AppendOnly',1;
  PRINT 'rejected deleting a refund: ' + ERROR_MESSAGE();
END CATCH

-- ===== 7. the refund target belongs to the refunded learner ==============================
-- reject: a refund against ANOTHER learner's order (FK_Refunds_OrderUser, the H-1 pattern)
BEGIN TRY
  INSERT INTO Refunds (UserId,OrderId,Method,Reason,Amount,RequestedByUserId)
    VALUES (@u,@o2,'money',N'not my order',100000.00,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: a refund against another learner''s order was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%FK_Refunds_OrderUser%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not FK_Refunds_OrderUser',1;
  PRINT 'rejected a refund against another learner''s order';
END CATCH

-- ===== 8. the ledger accepts a refund credit, and cannot be lied to =====================
-- accept: a positive refund_credit with a deciding admin and a note
INSERT INTO CreditTransactions (UserId,Reason,Delta,AdminUserId,Note)
  VALUES (@u,'refund_credit',10,@admin,N'credit refund #1');
DECLARE @txn INT = SCOPE_IDENTITY();
PRINT 'accepted: a positive refund_credit carrying a deciding admin and a note';

-- accept: the credit refund names that ledger row, and the count agrees
INSERT INTO Refunds (UserId,OrderId,Method,Reason,Status,CreditCount,RequestedByUserId,
                     DecidedByUserId,DecidedAt,CreditTxnId)
  VALUES (@u,@o,'credit',N'credited back','approved',10,@admin,@admin,SYSDATETIMEOFFSET(),@txn);
PRINT 'accepted: a credit refund naming the refund_credit row that granted its count';

-- vw_UserCreditBalance is a plain SUM over the ledger, so a refund_credit raises the balance with no
-- view change. Asserted rather than assumed: the number the learner sees is the acceptance criterion.
DECLARE @bal INT = (SELECT Balance FROM vw_UserCreditBalance WHERE UserId=@u);
IF @bal <> 10 THROW 50000,'EXPECT_REJECT FAILED: the balance view did not count the refund_credit row',1;
PRINT 'accepted: vw_UserCreditBalance counts a refund_credit (balance 10)';

-- reject: a negative refund_credit (the sign-per-reason rule, extended)
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,AdminUserId,Note)
    VALUES (@u,'refund_credit',-10,@admin,N'clawback');
  THROW 50000,'EXPECT_REJECT FAILED: a negative refund_credit was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_DeltaSign%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_CreditTxn_DeltaSign',1;
  PRINT 'rejected a negative refund_credit';
END CATCH

-- reject: a refund_credit with no deciding admin (the admin-actor rule, extended)
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,Note)
    VALUES (@u,'refund_credit',5,N'no actor');
  THROW 50000,'EXPECT_REJECT FAILED: a refund_credit with no deciding admin was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_AdminActor%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_CreditTxn_AdminActor',1;
  PRINT 'rejected a refund_credit with no deciding admin';
END CATCH

-- reject: a refund_credit with no note
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,AdminUserId)
    VALUES (@u,'refund_credit',5,@admin);
  THROW 50000,'EXPECT_REJECT FAILED: a refund_credit with no note was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_AdminActor%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_CreditTxn_AdminActor',1;
  PRINT 'rejected a refund_credit with no note';
END CATCH

-- reject: a refund_credit carrying the order. This is the whole reason no source column was added:
-- the purchase for @o already owns that slot under UX_CreditTxn_Order, so the ledger cannot name it.
BEGIN TRY
  INSERT INTO CreditTransactions (UserId,Reason,Delta,OrderId,AdminUserId,Note)
    VALUES (@u,'refund_credit',5,@o,@admin,N'points at the order');
  THROW 50000,'EXPECT_REJECT FAILED: a refund_credit carrying OrderId was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_CreditTxn_Source%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_CreditTxn_Source',1;
  PRINT 'rejected a refund_credit carrying an order reference';
END CATCH

-- reject: a refund claiming to have posted an arbitrary ledger row (e.g. an admin_grant)
INSERT INTO CreditTransactions (UserId,Reason,Delta,AdminUserId,Note)
  VALUES (@u,'admin_grant',5,@admin,N'make-good');
DECLARE @grant INT = SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Status,CreditCount,RequestedByUserId,
                       DecidedByUserId,DecidedAt,CreditTxnId)
    VALUES (@u,'credit',N'wrong row','approved',5,@admin,@admin,SYSDATETIMEOFFSET(),@grant);
  THROW 50000,'EXPECT_REJECT FAILED: a credit refund pointing at an admin_grant row was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50042
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not trg_Refunds_CreditTxnIsRefundCredit',1;
  PRINT 'rejected a credit refund pointing at an admin_grant row';
END CATCH

-- reject: a credit refund whose count disagrees with the row it names
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Status,CreditCount,RequestedByUserId,
                       DecidedByUserId,DecidedAt,CreditTxnId)
    VALUES (@u,'credit',N'count lies','approved',99,@admin,@admin,SYSDATETIMEOFFSET(),@txn);
  THROW 50000,'EXPECT_REJECT FAILED: a credit refund whose count disagrees with its ledger row was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_NUMBER() <> 50042
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not trg_Refunds_CreditTxnIsRefundCredit',1;
  PRINT 'rejected a credit refund whose count disagrees with its ledger row';
END CATCH

-- reject: a MONEY refund naming a ledger row
BEGIN TRY
  INSERT INTO Refunds (UserId,Method,Reason,Status,Amount,RequestedByUserId,
                       DecidedByUserId,DecidedAt,CreditTxnId)
    VALUES (@u,'money',N'money with txn','approved',100000.00,@admin,
            @admin,SYSDATETIMEOFFSET(),@txn);
  THROW 50000,'EXPECT_REJECT FAILED: a money refund carrying a credit txn was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_Refunds_CreditTxnIsCredit%'
    THROW 50000,'EXPECT_REJECT FAILED: refused by the wrong guard, not CK_Refunds_CreditTxnIsCredit',1;
  PRINT 'rejected a money refund carrying a credit txn';
END CATCH
