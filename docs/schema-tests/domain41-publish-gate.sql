SET NOCOUNT ON;
-- #226 / ADM-11: publishing is one door.
--
-- What this file proves: a paper cannot be CREATED published, cannot MOVE into published without a
-- recorded readiness pass, cannot ride a pass older than its own last edit, and a pass cannot be
-- declared by a service other than the audit's own writer.
--
-- What it cannot prove: that the six invariants themselves are right. That is the C# gate
-- (tests/Services.Tests/ExamPublishGateTests.cs), because gate four's envelope, span and audio rules
-- are pure functions and a T-SQL trigger that recomputed them would be the second copy AGENTS.md
-- forbids.
--
-- TWO GUARDS FIRE ON THIS TABLE, and this file has to satisfy the other one. trg_Exams_TriageCannotWrite
-- (#201 / ADM-03) refuses an undeclared write in a strict catalog, so every statement below declares
-- LastWrittenBy the way StaffAccountStampInterceptor does. A fixture that tripped the triage guard would
-- go green while this file's own trigger was dead.
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

INSERT INTO Certificates (Code,Name,IsActive) VALUES ('PG',N'Publish Gate',1);
DECLARE @c INT = SCOPE_IDENTITY();
-- A role and an account, so every write below can declare who made it. The triage guard (#201) refuses
-- an undeclared write in a strict catalog, so a fixture that skipped this would trip THAT guard and
-- report its refusal as this file's.
IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'author')
  INSERT INTO Roles (Code, Name) VALUES ('author', N'Content author');
INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
  VALUES ((SELECT RoleId FROM Roles WHERE Code = 'author'), 'pg-author@test.local', 0x01, N'Gate Author');
DECLARE @u INT = SCOPE_IDENTITY();

-- ===== the control: in a fixture catalog the raw publish is allowed ==============================
-- Without this arm, a trigger that refused everything would pass every rejection below.
IF dbo.fn_IsFixtureCatalog() <> 1 THROW 50000, 'EXPECT FAILED: this file expects a fixture catalog', 1;
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose,LastWrittenBy)
  VALUES (@c,N'fixture paper','published',60,'mock',@u);
PRINT 'control: a raw publish is accepted in a fixture catalog, so the refusals below are the trigger''s';

-- ===== a strict catalog from here on ============================================================
UPDATE CatalogMode SET IsFixtureCatalog = 0 WHERE ModeId = 1;

-- reject: INSERT ... 'published'. The first half of "publishing is one door" - a paper is a draft until
-- an audit lets it out. This is the arm that closes a migration or a seed writing a live paper.
BEGIN TRY
  INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose,LastWrittenBy)
    VALUES (@c,N'inserted live','published',60,'mock',@u);
  THROW 50000,'EXPECT_REJECT FAILED: a paper was created already published',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%created as a draft%' THROW 50000,'EXPECT_REJECT FAILED: an insert refused for the wrong reason',1;
  PRINT 'refused INSERT ... published (50100)';
END CATCH

-- A draft the remaining arms act on.
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose,LastWrittenBy)
  VALUES (@c,N'draft paper','draft',60,'mock',@u);
DECLARE @e INT = SCOPE_IDENTITY();

-- reject: the raw UPDATE. This is the bypass the ticket is about - `exam.Status = Published` was a
-- plain column, and the service comment beside CreateExamAsync was the only thing holding it.
BEGIN TRY
  UPDATE Exams SET Status='published', LastWrittenBy=@u WHERE ExamId=@e;
  THROW 50000,'EXPECT_REJECT FAILED: a raw UPDATE published a paper with no audit',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%publishing is one door%' THROW 50000,'EXPECT_REJECT FAILED: an update refused for the wrong reason',1;
  PRINT 'refused UPDATE ... published with no pass (50101)';
END CATCH

-- reject: a STALE pass. The expected-wrong input is the one a lazy guard passes: publish, then edit
-- the paper so an invariant breaks, then set the status again. The first pass still exists, so a guard
-- that only asked "is there a pass" would accept it - which is the same stale-snapshot defect the audit
-- exists to prevent.
INSERT INTO ExamPublishAudits (ExamId,PassedAt,WrittenByService)
  VALUES (@e, DATEADD(MINUTE,-10,SYSDATETIMEOFFSET()), 'AdminExamService');
UPDATE Exams SET UpdatedAt = SYSDATETIMEOFFSET(), LastWrittenBy=@u WHERE ExamId=@e;
BEGIN TRY
  UPDATE Exams SET Status='published', LastWrittenBy=@u WHERE ExamId=@e;
  THROW 50000,'EXPECT_REJECT FAILED: a pass older than the last edit unlocked the door',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%no recorded readiness pass newer than its last edit%' THROW 50000,'EXPECT_REJECT FAILED: a stale pass refused for the wrong reason',1;
  PRINT 'refused a pass older than the paper''s last edit (50101)';
END CATCH

-- accept: a FRESH pass. The other direction, so the refusals above are not a blanket denial.
UPDATE ExamPublishAudits SET PassedAt = DATEADD(MINUTE,10,SYSDATETIMEOFFSET()) WHERE ExamId=@e;
UPDATE Exams SET Status='published', LastWrittenBy=@u WHERE ExamId=@e;
IF (SELECT Status FROM Exams WHERE ExamId=@e) <> 'published'
  THROW 50000,'EXPECT FAILED: a fresh pass did not unlock the door',1;
PRINT 'accepted a publish with a fresh pass';

-- accept: WITHDRAWING a published paper needs no pass. Withdraw only ever removes the exam from the
-- catalog, and requiring a readiness pass to take a paper DOWN would leave a broken one live.
UPDATE Exams SET Status='hidden', LastWrittenBy=@u WHERE ExamId=@e;
UPDATE Exams SET Status='published', LastWrittenBy=@u WHERE ExamId=@e;
PRINT 'accepted hidden -> published on the existing fresh pass, and published -> hidden with none';

-- reject: a pass declared by a service that is not the audit's owner. ADR-0012: the verdict is derived
-- by one writer, and a pass nobody can attribute is a pass nobody took.
INSERT INTO Exams (CertId,Name,Status,DurationMinutes,Purpose,LastWrittenBy)
  VALUES (@c,N'draft two','draft',60,'mock',@u);
DECLARE @e2 INT = SCOPE_IDENTITY();
BEGIN TRY
  INSERT INTO ExamPublishAudits (ExamId,PassedAt,WrittenByService)
    VALUES (@e2, SYSDATETIMEOFFSET(), 'SomeOtherService');
  THROW 50000,'EXPECT_REJECT FAILED: a pass declared by a non-owner was accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%exactly one writer%' THROW 50000,'EXPECT_REJECT FAILED: a wrong-writer pass refused for the wrong reason',1;
  PRINT 'refused a pass declared by a non-owner (50102)';
END CATCH

-- The registry is NOT where this fact is recorded, and that is a decision rather than an omission:
-- domain27 audits every `covered` table for a NOT NULL `ProjectionVersion`, because those facts are
-- projections a stale callback could overwrite. A publish pass has no callback and no race -
-- PassedAt against Exams.UpdatedAt is the currency test - so registering it would add a second
-- monotonic counter beside the one that already answers "is this pass current". #208 made the same
-- call for the rubric lock. The writer is stated by this table's own trigger instead.
IF EXISTS (SELECT 1 FROM DerivedFactOwners WHERE FactCode = 'exam_publish_readiness')
  THROW 50000, 'EXPECT_REJECT FAILED: the publish pass is registered as a derived fact, which would demand a ProjectionVersion it does not need', 1;

IF (SELECT COUNT(*) FROM DerivedFactOwners) <> 11
  THROW 50000, 'EXPECT_REJECT FAILED: the registry no longer names eleven facts', 1;

PRINT 'domain41: publishing is one door, and the door is a constraint';
