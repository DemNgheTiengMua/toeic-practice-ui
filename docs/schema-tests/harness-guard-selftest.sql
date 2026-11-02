-- docs/schema-tests/harness-guard-selftest.sql — proves the zero-row fixture guard works.
-- run-all.sh runs this on a fresh database with the same fixture-guard injection the domain files
-- get, so this file tests the harness's own rows and not only ExpectFixtureRows.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

-- 1. Control. A fixture that matched a row must leave the injected guard silent; without this case
--    a guard that threw unconditionally would pass the checks below.
INSERT INTO Certificates (Code,Name,IsActive)
  SELECT 'GUARDCTRL',N'Guard control',1;
PRINT 'guard stayed silent on a fixture that matched a row';

-- 2. The case under test. This fixture SELECT matches no row, so the injected ExpectFixtureRows must
--    abort and name the statement. EXPECT_REJECT is not involved: the point is that the harness
--    notices before the file can fall through to a THROW of its own and report the wrong reason.
BEGIN TRY
  INSERT INTO Certificates (Code,Name,IsActive)
    SELECT Code,Name,IsActive FROM Certificates WHERE Code='GUARD-NO-SUCH-CERT';
  -- run-all.sh injects `EXEC ExpectFixtureRows ...` here. If it did not, the next line aborts with
  -- SELFTEST FAILED, which is the failure this file exists to catch.
  THROW 50000,'SELFTEST FAILED: an empty fixture did not fire the guard',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() NOT LIKE 'EXPECT_REJECT FAILED: fixture matched 0 rows%'
    THROW;
  PRINT 'guard aborted the empty fixture and named it: ' + ERROR_MESSAGE();
END CATCH

-- 3. ExpectFixtureRows reads @@ROWCOUNT on entry, so it must also see a row count that a table
--    trigger has already been through (27 tables in this schema carry one).
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('GUARDTRG',N'Guard trigger',1);
EXEC ExpectFixtureRows N'harness-guard-selftest.sql: insert into a table whose triggers ran';
PRINT 'guard read @@ROWCOUNT correctly after the table triggers ran';
