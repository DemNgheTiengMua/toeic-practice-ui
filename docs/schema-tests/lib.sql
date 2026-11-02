-- docs/schema-tests/lib.sql — helpers shared by the domain tests. reset-and-load.sh installs these
-- into the test database, so a domain file calls them without defining them.
--
-- A test earns the right to say "the constraint rejected it" only if its fixture matched a row.
-- Call ExpectFixtureRows immediately after such an INSERT ... SELECT, before anything else resets
-- @@ROWCOUNT:
--
--   INSERT INTO Reviews (BookingId,UserId,Rating,Text)
--     SELECT TOP 1 BookingId,@u,7,N'x' FROM Bookings;
--   EXEC ExpectFixtureRows N'domain09-mentor.sql: Reviews from Bookings (rating 7 fixture)';
--
-- Without it a fixture that matches nothing falls through to the test's own
-- THROW 'EXPECT_REJECT FAILED: ... accepted', which is the right verdict for the wrong reason: no
-- constraint was exercised. That is how domain04 reported "difficulty 9 accepted" while
-- CK_Questions_Difficulty was in fact rejecting 9.
--
-- Sections.OptionCount is NOT NULL (2026-10-26) and stays that way: a speaking section declares a
-- real count, which the certificate wizard defaults to 4. A NULL there fails the section insert,
-- the variables declared after it go NULL, and every later statement quietly matches zero rows —
-- the failure mode the guard below names instead of hiding.
--
-- Wrap an insert that MUST fail:
-- BEGIN TRY
--   <insert that violates a constraint>;
--   THROW 50000, 'EXPECT_REJECT FAILED: insert was accepted', 1;
-- END TRY
-- BEGIN CATCH
--   IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
--   PRINT 'rejected as expected: ' + ERROR_MESSAGE();
-- END CATCH
--
-- The message keeps that prefix so the existing CATCH blocks re-raise it rather than printing
-- "rejected as expected" over a test that voted nobody.
CREATE OR ALTER PROCEDURE ExpectFixtureRows @Statement NVARCHAR(400) AS
BEGIN
  -- Read first: every statement after this one resets @@ROWCOUNT, including SET NOCOUNT ON.
  DECLARE @rows INT = @@ROWCOUNT;
  IF @rows = 0
  BEGIN
    -- THROW takes a variable, not an expression, so the message is built first.
    DECLARE @msg NVARCHAR(2048) =
      N'EXPECT_REJECT FAILED: fixture matched 0 rows, so nothing was tested - ' + @Statement;
    THROW 50000, @msg, 1;
  END
END
