SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- The demo seed inserts two Orders carrying explicit PaymentNo 1 and 2, written behind
-- SEQ_PaymentNo's back. Unless the sequence is reseeded past them, the first real checkout draws 1
-- again, violates UQ_Orders_PaymentNo, and PaymentService used to surface that as the misleading 409
-- "Order already open" — for an order the learner did not have.
--
-- This file pins the repair the seed performs: after seeding, SEQ_PaymentNo must sit above every
-- PaymentNo the table holds, so the next draw cannot collide.

INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'seq@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();

-- The state the seed leaves: paid demo orders at 1 and 2, plus the reseed.
INSERT INTO Orders (UserId,Credits,Status,Amount,OrderCode,PaymentNo,Gateway)
  VALUES (@u,10,'paid',190000,'DH-SEQ-0001',1,'mock');
INSERT INTO Orders (UserId,Credits,Status,Amount,OrderCode,PaymentNo,Gateway)
  VALUES (@u,5,'paid',100000,'DH-SEQ-0002',2,'mock');
ALTER SEQUENCE SEQ_PaymentNo RESTART WITH 3;

-- The invariant: the next number the sequence hands out is one no order already holds.
DECLARE @nextDrawn BIGINT = NEXT VALUE FOR SEQ_PaymentNo;
IF @nextDrawn <= ISNULL((SELECT MAX(PaymentNo) FROM Orders WHERE PaymentNo IN (1,2)), 0)
  THROW 50000,'SEQ_PaymentNo did not advance past the seeded PaymentNo values',1;
IF EXISTS (SELECT 1 FROM Orders WHERE PaymentNo = @nextDrawn)
  THROW 50000,'NEXT VALUE FOR SEQ_PaymentNo collides with an existing Order.PaymentNo',1;
PRINT 'after reseeding, SEQ_PaymentNo hands out a PaymentNo no order holds';

-- A checkout can then insert normally with that number.
INSERT INTO Orders (UserId,Credits,Status,Amount,OrderCode,PaymentNo,Gateway)
  VALUES (@u,5,'pending',100000,'DH-SEQ-0003',@nextDrawn,'mock');
PRINT 'checkout insert with the drawn PaymentNo succeeded';

DELETE FROM Orders WHERE UserId = @u;
DELETE FROM Users WHERE UserId = @u;
DELETE FROM Roles WHERE RoleId = @r;
