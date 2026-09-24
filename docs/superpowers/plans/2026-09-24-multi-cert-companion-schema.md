# Multi-Certificate Companion Schema Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite `docs/database-schema.sql` from the TOEIC-L&R-only schema into the 10-domain multi-certificate mock-exam companion schema, verified by live execution on SQL Server LocalDB.

**Architecture:** One canonical DDL file, built domain-by-domain in dependency order (Catalog first, Payment last because its ledger references attempts/gradings/bookings/paths). Each domain is a task: write a T-SQL verification script that fails against the not-yet-created objects, add the domain's DDL, re-run against a freshly reset database until valid data is accepted and every claimed constraint rejects bad data. No EF, no app code — this plan produces the schema only.

**Tech Stack:** SQL Server (LocalDB `(localdb)\MSSQLLocalDB`), T-SQL DDL, `sqlcmd` for execution. Bash shell on Windows.

**Spec:** `docs/superpowers/specs/2026-09-24-multi-cert-companion-design.md`

## Global Constraints

Copied verbatim from the spec's cross-cutting conventions and hard constraints. Every task implicitly includes these.

- Every batch runs with `SET QUOTED_IDENTIFIER ON;` and `SET ANSI_NULLS ON;` (required for filtered indexes). `sqlcmd` must pass `-I`.
- Timestamps are `DATETIMEOFFSET`. Enum-like states are `VARCHAR` + a `CHECK` constraint (never a lookup-less magic int).
- Soft-delete via a `IsDeleted BIT NOT NULL DEFAULT 0` column where content must survive references (Users, Exams).
- Snapshot pattern: scores/bands are copied into result rows at grading time (`BandCode`, `ResultBandCode`) so history is stable against later catalog edits.
- Aggregates (wallet balance, mentor average rating) are always a VIEW over rows (SUM/AVG), never a stored counter column.
- Certificate isolation: a Skill belongs to one Certificate, a Section to one Skill, an Exam to one Certificate. No row may mix certificates. Enforced in the schema (composite FKs / triggers), not in backend policy.
- `scaled→band` (`ScoreBands`) is fixed standard data: admin cannot edit it. `raw→scaled` (`ScoreScales`) is admin-editable per exam.
- Wallet balance = SUM over `CreditTransactions`; no stored balance column. Practice, weakness analysis, and placement never write a `CreditTransactions` row.
- Learner-scoped data is keyed by `(UserId, CertId)` and persists independently across certificate switches; destroyed only on explicit reset.
- No KYC / CCCD anywhere. No real/high-stakes exam.

## Review Focus

Input classes the spec implies but a naive per-table test would miss — most likely to bite first. Each has its test pinned to the owning task below.

- **Cross-certificate leakage (the spec's whole premise).** A simple FK lets an Exam of cert A hold a QuestionGroup whose Section belongs to cert B — `QuestionGroups.SectionId → Sections` and `Exams.CertId → Certificates` are unrelated FKs. Owned by Task 4 (composite-FK carry-down of `CertId`); tested there with a cross-cert insert that must be rejected.
- **`TotalScore` drifting from its skill parts.** The spec says total = SUM of scaled skill scores, but nothing enforces it unless the schema does. Owned by Task 6 as a trigger; tested with an attempt whose `TotalScore` disagrees with `AttemptSkillScores`.
- **`PathModuleItems` ref column mismatching `ItemType`.** `practice_section` with a null `RefSectionId`, or a `mock_exam` carrying a `RefSectionId`. Owned by Task 8; tested with each wrong combination.
- **Negative wallet balance under a debit that overdraws.** Owned by Task 10 (no-negative-balance trigger carried forward); tested with a debit larger than the ledger sum.
- **Duplicate "single active" rows.** Two active `LearnerPaths` for one `(UserId, CertId)`, two pending orders for one user, two bookings on one slot. Owned by Tasks 8, 10, 9 respectively via filtered unique indexes; each tested with a duplicate insert that must be rejected.

---

## File structure

- `docs/database-schema.sql` — the canonical schema, rewritten. Built by appending one domain section per task, in the order below. Each task's verification re-runs the **whole** file against a fresh database, so the file is always internally consistent.
- `docs/schema-tests/reset-and-load.sh` — drops + recreates `CompanionSchemaTest`, runs `database-schema.sql` into it. One helper, reused by every task.
- `docs/schema-tests/domainNN-*.sql` — one verification script per domain: inserts one valid row-set (must succeed) and the constraint-violating rows the spec's guarantees imply (must each be rejected).

Dependency order (why Payment is last): Catalog → {Identity, Content, Scoring} → {Practice, MockExam} → AI Grading → LearningPath → Mentor → Payment. Payment's `CreditTransactions` source columns FK into Orders, ExamAttempts, AiGradings, Bookings, LearnerPaths, and its complaint make-good closes a cycle back to Complaints — so it lands after every table it references exists.

---

### Task 0: Test harness

**Files:**
- Create: `docs/schema-tests/reset-and-load.sh`
- Create: `docs/schema-tests/lib.sql` (reusable assert helper)

**Interfaces:**
- Produces: `reset-and-load.sh` (drops/creates DB `CompanionSchemaTest`, loads `docs/database-schema.sql`); an `EXPECT_REJECT` T-SQL pattern later scripts copy.

- [ ] **Step 1: Write the reset+load helper**

```bash
#!/usr/bin/env bash
# docs/schema-tests/reset-and-load.sh — fresh DB, then load the schema.
set -euo pipefail
S='(localdb)\MSSQLLocalDB'
DB='CompanionSchemaTest'
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
sqlcmd -S "$S" -I -b -Q "IF DB_ID('$DB') IS NOT NULL BEGIN ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DB]; END; CREATE DATABASE [$DB];"
sqlcmd -S "$S" -d "$DB" -I -b -i "$ROOT/docs/database-schema.sql"
echo "LOADED OK"
```

- [ ] **Step 2: Record the reject-assertion pattern in lib.sql**

```sql
-- docs/schema-tests/lib.sql — reference only. Wrap an insert that MUST fail:
-- BEGIN TRY
--   <insert that violates a constraint>;
--   THROW 50000, 'EXPECT_REJECT FAILED: insert was accepted', 1;
-- END TRY
-- BEGIN CATCH
--   IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
--   PRINT 'rejected as expected: ' + ERROR_MESSAGE();
-- END CATCH
```

- [ ] **Step 3: Verify the harness fails cleanly on an empty schema file**

Run: `touch docs/database-schema.sql.bak && : > docs/database-schema.sql && bash docs/schema-tests/reset-and-load.sh; echo "exit=$?"`
Expected: `LOADED OK` (empty file loads), then restore: `mv docs/database-schema.sql.bak docs/database-schema.sql` — this only proves the harness runs. (If `database-schema.sql` still holds the old schema, skip the emptying; just run the reset once to confirm `LOADED OK`.)

- [ ] **Step 4: Commit**

```bash
git add docs/schema-tests/reset-and-load.sh docs/schema-tests/lib.sql
git commit -m "test(schema): add LocalDB reset-and-load harness"
```

### Task 1: Catalog domain (Certificates → Skills → Sections)

This task **replaces** the old `docs/database-schema.sql` contents with a new file header + the Catalog tables. All later tasks append below.

**Files:**
- Modify (replace whole file): `docs/database-schema.sql`
- Test: `docs/schema-tests/domain01-catalog.sql`

**Interfaces:**
- Produces: `Certificates(CertId PK, Code UQ)`, `Skills(SkillId PK, CertId FK, Modality)`, `Sections(SectionId PK, SkillId FK, OptionCount NULL)`. Later tasks FK to `CertId`, `SkillId`, `SectionId`.

- [ ] **Step 1: Write the failing test**

```sql
-- docs/schema-tests/domain01-catalog.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- valid: TOEIC with two skills, one section
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC', N'TOEIC L&R', 1);
DECLARE @cert INT = SCOPE_IDENTITY();
INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
  VALUES (@cert, 'LIST', N'Listening', 'listening', 1), (@cert, 'READ', N'Reading', 'reading', 2);
DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId=@cert AND Code='LIST');
INSERT INTO Sections (SkillId, CertId, Code, Name, OptionCount, DisplayOrder)
  VALUES (@skill, @cert, 'P2', N'Question-Response', 3, 2);
PRINT 'valid catalog accepted';
-- reject: Modality outside the allowed set
BEGIN TRY
  INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@cert,'X',N'X','singing',9);
  THROW 50000,'EXPECT_REJECT FAILED: bad modality accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad modality as expected';
END CATCH
```

- [ ] **Step 2: Run it, verify failure**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain01-catalog.sql`
Expected: FAIL — `Invalid object name 'Certificates'` (tables not created yet).

- [ ] **Step 3: Write the DDL (replace `docs/database-schema.sql`)**

```sql
-- docs/database-schema.sql
-- Multi-certificate mock-exam companion schema.
-- Spec: docs/superpowers/specs/2026-09-24-multi-cert-companion-design.md
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- ============ Domain 2: Catalog ============
CREATE TABLE Certificates (
  CertId       INT IDENTITY(1,1) PRIMARY KEY,
  Code         VARCHAR(20)  NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  Description  NVARCHAR(500) NULL,
  IsActive     BIT NOT NULL DEFAULT 1,
  CONSTRAINT UQ_Certificates_Code UNIQUE (Code)
);
GO
CREATE TABLE Skills (
  SkillId      INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  Modality     VARCHAR(10) NOT NULL
    CONSTRAINT CK_Skills_Modality CHECK (Modality IN ('listening','reading','speaking','writing')),
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Skills_Cert_Code UNIQUE (CertId, Code),
  -- composite target so Content can carry CertId down and pin isolation:
  CONSTRAINT UQ_Skills_Id_Cert UNIQUE (SkillId, CertId)
);
GO
CREATE TABLE Sections (
  SectionId    INT IDENTITY(1,1) PRIMARY KEY,
  SkillId      INT NOT NULL,
  CertId       INT NOT NULL,
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  OptionCount  INT NULL CONSTRAINT CK_Sections_OptionCount CHECK (OptionCount IS NULL OR OptionCount BETWEEN 2 AND 6),
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Sections_Skill_Code UNIQUE (SkillId, Code),
  -- composite FK pins the section's cert to its skill's cert (isolation, enforced because CertId is NOT NULL):
  CONSTRAINT FK_Sections_SkillCert FOREIGN KEY (SkillId, CertId) REFERENCES Skills(SkillId, CertId),
  -- composite target so QuestionGroups can require exam-cert = section-cert:
  CONSTRAINT UQ_Sections_Id_Cert UNIQUE (SectionId, CertId)
);
GO
```

- [ ] **Step 4: Run it, verify pass**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain01-catalog.sql`
Expected: PASS — prints `valid catalog accepted` and `rejected bad modality as expected`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/domain01-catalog.sql
git commit -m "feat(schema): data-driven Catalog (Certificates, Skills, Sections)"
```

### Task 2: Identity domain (login kept, KYC dropped)

**Files:**
- Modify (append): `docs/database-schema.sql`
- Test: `docs/schema-tests/domain02-identity.sql`

**Interfaces:**
- Consumes: `Certificates(CertId)` (Task 1).
- Produces: `Roles(RoleId PK)`, `Users(UserId PK, RoleId FK, ActiveCertId FK NULL, IsDeleted)`, `PasswordResetTokens`. Later tasks FK to `UserId`.

- [ ] **Step 1: Write the failing test**

```sql
-- docs/schema-tests/domain02-identity.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code, Name) VALUES ('student', N'Student');
DECLARE @r INT = SCOPE_IDENTITY();
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC', N'TOEIC', 1);
DECLARE @c INT = SCOPE_IDENTITY();
INSERT INTO Users (RoleId, Email, PasswordHash, ActiveCertId)
  VALUES (@r, 'a@b.com', 0x00, @c);
PRINT 'valid user accepted';
-- reject: duplicate email (one account per email)
BEGIN TRY
  INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@r,'a@b.com',0x00);
  THROW 50000,'EXPECT_REJECT FAILED: dup email accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected dup email as expected';
END CATCH
```

- [ ] **Step 2: Run it, verify failure**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain02-identity.sql`
Expected: FAIL — `Invalid object name 'Roles'`.

- [ ] **Step 3: Append the DDL**

```sql
-- ============ Domain 1: Identity ============
CREATE TABLE Roles (
  RoleId INT IDENTITY(1,1) PRIMARY KEY,
  Code   VARCHAR(20) NOT NULL CONSTRAINT UQ_Roles_Code UNIQUE,
  Name   NVARCHAR(60) NOT NULL
);
GO
CREATE TABLE Users (
  UserId       INT IDENTITY(1,1) PRIMARY KEY,
  RoleId       INT NOT NULL REFERENCES Roles(RoleId),
  Email        VARCHAR(255) NOT NULL,
  PasswordHash VARBINARY(256) NOT NULL,
  ActiveCertId INT NULL REFERENCES Certificates(CertId),
  IsDeleted    BIT NOT NULL DEFAULT 0,
  CreatedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
-- one account per live email (filtered so soft-deleted emails can be reused)
CREATE UNIQUE INDEX UX_Users_Email ON Users(Email) WHERE IsDeleted = 0;
GO
CREATE TABLE PasswordResetTokens (
  TokenId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId     INT NOT NULL REFERENCES Users(UserId),
  TokenHash  VARBINARY(256) NOT NULL,
  ExpiresAt  DATETIMEOFFSET NOT NULL,
  UsedAt     DATETIMEOFFSET NULL,
  CreatedAt  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
```

- [ ] **Step 4: Run it, verify pass**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain02-identity.sql`
Expected: PASS — `valid user accepted`, `rejected dup email as expected`.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/domain02-identity.sql
git commit -m "feat(schema): Identity (login kept, KYC removed)"
```

### Task 3: Scoring domain (per-certificate scales + bands)

**Files:**
- Modify (append): `docs/database-schema.sql`
- Test: `docs/schema-tests/domain03-scoring.sql`

**Interfaces:**
- Consumes: `Certificates(CertId)`, `Skills(SkillId)`.
- Produces: `ScoreScales`, `ScoreBands(BandId PK, UNIQUE(CertId,Code))`. `Exams`/attempts/targets reference bands by `(CertId, Code)`. Note: `ScoreScales.ExamId` FK is added in Task 4 after `Exams` exists.

- [ ] **Step 1: Write the failing test**

```sql
-- docs/schema-tests/domain03-scoring.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Certificates (Code, Name, IsActive) VALUES ('TOEIC', N'TOEIC', 1);
DECLARE @c INT = SCOPE_IDENTITY();
INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
  VALUES (@c,'below_A1',N'Below A1',10,119,1), (@c,'A1',N'A1',120,224,2);
INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
  VALUES (@c, NULL, NULL, 50, 495);
PRINT 'valid scoring accepted';
-- reject: duplicate band code within a certificate (breaks reference-by-code)
BEGIN TRY
  INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
    VALUES (@c,'A1',N'dup',1,2,9);
  THROW 50000,'EXPECT_REJECT FAILED: dup band code accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected dup band code as expected';
END CATCH
```

- [ ] **Step 2: Run it, verify failure**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain03-scoring.sql`
Expected: FAIL — `Invalid object name 'ScoreBands'`.

- [ ] **Step 3: Append the DDL**

```sql
-- ============ Domain 4: Scoring ============
CREATE TABLE ScoreBands (
  BandId       INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(60) NOT NULL,
  MinTotal     INT NOT NULL,
  MaxTotal     INT NOT NULL,
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT CK_ScoreBands_Range CHECK (MaxTotal >= MinTotal),
  CONSTRAINT UQ_ScoreBands_Cert_Code UNIQUE (CertId, Code)
);
GO
CREATE TABLE ScoreScales (
  ScaleId     INT IDENTITY(1,1) PRIMARY KEY,
  CertId      INT NOT NULL REFERENCES Certificates(CertId),
  SkillId     INT NULL REFERENCES Skills(SkillId),   -- NULL = total
  ExamId      INT NULL,                              -- NULL = default; FK added in Task 4
  RawScore    INT NOT NULL,
  ScaledScore INT NOT NULL
);
GO
```

- [ ] **Step 4: Run it, verify pass**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain03-scoring.sql`
Expected: PASS — `valid scoring accepted`, `rejected dup band code as expected`.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/domain03-scoring.sql
git commit -m "feat(schema): per-certificate Scoring (ScoreScales, ScoreBands)"
```

### Task 4: Content domain (Exams, Questions, Options, Import) + cross-cert isolation

Implements Review Focus #1. `Exams` carries `CertId`; `Sections` gains a `CertId` mirror validated against its skill; `QuestionGroups` uses a **composite FK** `(ExamId, CertId)` and `(SectionId, CertId)` so the exam's certificate and the section's certificate must be the same value — a cross-cert group cannot be inserted.

**Files:**
- Modify (append + one ALTER to `Sections` and `ScoreScales`): `docs/database-schema.sql`
- Test: `docs/schema-tests/domain04-content.sql`

**Interfaces:**
- Consumes: `Certificates`, `Skills(SkillId,CertId)` (composite UQ from Task 1), `Sections(SectionId,CertId)` (composite UQ from Task 1), `ScoreScales`.
- Produces: `Exams(ExamId PK, CertId, UNIQUE(ExamId,CertId))`, `QuestionGroups`, `Questions(QuestionId PK, DifficultyLevel 1-5, QuestionType)`, `QuestionOptions`, `ImportBatches`, `ImportLog`. `ScoreScales.ExamId` gains its FK.

- [ ] **Step 1: Write the failing test (valid + cross-cert reject)**

```sql
-- docs/schema-tests/domain04-content.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- two certs, each one skill + one section
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'TOEIC',1),('IELTS',N'IELTS',1);
DECLARE @toeic INT=(SELECT CertId FROM Certificates WHERE Code='TOEIC');
DECLARE @ielts INT=(SELECT CertId FROM Certificates WHERE Code='IELTS');
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES
  (@toeic,'READ',N'Reading','reading',1),(@ielts,'SPK',N'Speaking','speaking',1);
DECLARE @tRead INT=(SELECT SkillId FROM Skills WHERE CertId=@toeic AND Code='READ');
DECLARE @iSpk  INT=(SELECT SkillId FROM Skills WHERE CertId=@ielts AND Code='SPK');
INSERT INTO Sections (SkillId,CertId,Code,Name,OptionCount,DisplayOrder) VALUES
  (@tRead,@toeic,'P7',N'Reading Comp',4,1),(@iSpk,@ielts,'S1',N'Interview',NULL,1);
DECLARE @tSec INT=(SELECT SectionId FROM Sections WHERE Code='P7');
DECLARE @iSec INT=(SELECT SectionId FROM Sections WHERE Code='S1');
-- valid TOEIC exam + group on TOEIC section
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@toeic,N'ETS 1','published',120);
DECLARE @exam INT=SCOPE_IDENTITY();
INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@exam,@toeic,@tSec,1);
PRINT 'valid same-cert group accepted';
-- REJECT (Review Focus #1): TOEIC exam group pointing at an IELTS section
BEGIN TRY
  INSERT INTO QuestionGroups (ExamId,CertId,SectionId,DisplayOrder) VALUES (@exam,@toeic,@iSec,2);
  THROW 50000,'EXPECT_REJECT FAILED: cross-cert group accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected cross-cert group as expected';
END CATCH
-- reject: DifficultyLevel outside 1-5
INSERT INTO Questions (GroupId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
  SELECT GroupId,N'ok',3,'mcq',1 FROM QuestionGroups WHERE ExamId=@exam AND SectionId=@tSec;
BEGIN TRY
  INSERT INTO Questions (GroupId,Stem,DifficultyLevel,QuestionType,DisplayOrder)
    SELECT GroupId,N'bad',9,'mcq',2 FROM QuestionGroups WHERE ExamId=@exam;
  THROW 50000,'EXPECT_REJECT FAILED: difficulty 9 accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected out-of-range difficulty as expected';
END CATCH
```

- [ ] **Step 2: Run it, verify failure**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain04-content.sql`
Expected: FAIL — `Invalid object name 'Exams'`.

- [ ] **Step 3: Append the DDL (and backfill the two deferred FKs)**

```sql
-- ============ Domain 3: Content ============
-- (Sections already carries CertId + composite FK from Task 1)
CREATE TABLE Exams (
  ExamId          INT IDENTITY(1,1) PRIMARY KEY,
  CertId          INT NOT NULL REFERENCES Certificates(CertId),
  Name            NVARCHAR(160) NOT NULL,
  Status          VARCHAR(12) NOT NULL DEFAULT 'draft'
    CONSTRAINT CK_Exams_Status CHECK (Status IN ('draft','published','archived')),
  DurationMinutes INT NOT NULL CONSTRAINT CK_Exams_Duration CHECK (DurationMinutes > 0),
  IsDeleted       BIT NOT NULL DEFAULT 0,
  CreatedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT UQ_Exams_Id_Cert UNIQUE (ExamId, CertId)   -- composite target for isolation
);
GO
-- now that Exams exists, pin ScoreScales.ExamId
ALTER TABLE ScoreScales ADD CONSTRAINT FK_ScoreScales_Exam FOREIGN KEY (ExamId) REFERENCES Exams(ExamId);
GO
CREATE TABLE QuestionGroups (
  GroupId      INT IDENTITY(1,1) PRIMARY KEY,
  ExamId       INT NOT NULL,
  CertId       INT NOT NULL,
  SectionId    INT NOT NULL,
  Passage      NVARCHAR(MAX) NULL,
  AudioPath    NVARCHAR(400) NULL,
  DisplayOrder INT NOT NULL DEFAULT 0,
  -- both composite FKs share CertId, so exam-cert and section-cert must agree:
  CONSTRAINT FK_QGroups_ExamCert    FOREIGN KEY (ExamId, CertId)    REFERENCES Exams(ExamId, CertId),
  CONSTRAINT FK_QGroups_SectionCert FOREIGN KEY (SectionId, CertId) REFERENCES Sections(SectionId, CertId)
);
GO
CREATE TABLE Questions (
  QuestionId      INT IDENTITY(1,1) PRIMARY KEY,
  GroupId         INT NOT NULL REFERENCES QuestionGroups(GroupId),
  Stem            NVARCHAR(MAX) NOT NULL,
  DifficultyLevel INT NOT NULL CONSTRAINT CK_Questions_Difficulty CHECK (DifficultyLevel BETWEEN 1 AND 5),
  QuestionType    VARCHAR(14) NOT NULL
    CONSTRAINT CK_Questions_Type CHECK (QuestionType IN ('mcq','free_response')),
  Explanation     NVARCHAR(MAX) NULL,
  DisplayOrder    INT NOT NULL DEFAULT 0
);
GO
CREATE TABLE QuestionOptions (
  OptionId   INT IDENTITY(1,1) PRIMARY KEY,
  QuestionId INT NOT NULL REFERENCES Questions(QuestionId),
  Label      VARCHAR(4) NOT NULL,
  Text       NVARCHAR(MAX) NOT NULL,
  IsCorrect  BIT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Options_Question_Label UNIQUE (QuestionId, Label)
);
GO
CREATE TABLE ImportBatches (
  BatchId     INT IDENTITY(1,1) PRIMARY KEY,
  CertId      INT NOT NULL REFERENCES Certificates(CertId),
  SourceName  NVARCHAR(260) NOT NULL,
  Status      VARCHAR(10) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_ImportBatches_Status CHECK (Status IN ('pending','done','failed')),
  UploadedBy  INT NOT NULL REFERENCES Users(UserId),
  CreatedAt   DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CompletedAt DATETIMEOFFSET NULL
);
GO
CREATE TABLE ImportLog (
  LogId   INT IDENTITY(1,1) PRIMARY KEY,
  BatchId INT NOT NULL REFERENCES ImportBatches(BatchId),
  RowNo   INT NULL,
  Level   VARCHAR(5) NOT NULL CONSTRAINT CK_ImportLog_Level CHECK (Level IN ('info','warn','error')),
  Message NVARCHAR(1000) NOT NULL,
  LoggedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
```

- [ ] **Step 4: Run it, verify pass**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain04-content.sql`
Expected: PASS — `valid same-cert group accepted`, `rejected cross-cert group as expected`, `rejected out-of-range difficulty as expected`.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/domain04-content.sql
git commit -m "feat(schema): Content + composite-FK certificate isolation"
```

### Task 5: Practice domain (by Section, credit-free)

**Files:**
- Modify (append): `docs/database-schema.sql`
- Test: `docs/schema-tests/domain05-practice.sql`

**Interfaces:**
- Consumes: `Users`, `Sections`, `QuestionOptions`.
- Produces: `PracticeSessions`, `PracticeAnswers`. No ledger interaction (free by construction).

- [ ] **Step 1: Write the failing test**

```sql
-- docs/schema-tests/domain05-practice.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'READ',N'R','reading',1);
DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Code,Name,OptionCount,DisplayOrder) VALUES (@sk,@c,'P5',N'IC',4,1);
DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO PracticeSessions (UserId,SectionId,QuestionCount,Status,CorrectCount)
  VALUES (@u,@sec,10,'in_progress',0);
PRINT 'valid practice session accepted';
-- reject: bad status
BEGIN TRY
  INSERT INTO PracticeSessions (UserId,SectionId,QuestionCount,Status) VALUES (@u,@sec,1,'exploding');
  THROW 50000,'EXPECT_REJECT FAILED: bad status accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad status as expected';
END CATCH
```

- [ ] **Step 2: Run it, verify failure**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain05-practice.sql`
Expected: FAIL — `Invalid object name 'PracticeSessions'`.

- [ ] **Step 3: Append the DDL**

```sql
-- ============ Domain 6: Practice (free) ============
CREATE TABLE PracticeSessions (
  SessionId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId       INT NOT NULL REFERENCES Users(UserId),
  SectionId    INT NOT NULL REFERENCES Sections(SectionId),
  QuestionCount INT NOT NULL CONSTRAINT CK_Practice_Count CHECK (QuestionCount > 0),
  Status       VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Practice_Status CHECK (Status IN ('in_progress','finished','abandoned')),
  CorrectCount INT NOT NULL DEFAULT 0,
  StartedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  FinishedAt   DATETIMEOFFSET NULL
);
GO
CREATE TABLE PracticeAnswers (
  AnswerId         INT IDENTITY(1,1) PRIMARY KEY,
  SessionId        INT NOT NULL REFERENCES PracticeSessions(SessionId),
  QuestionId       INT NOT NULL REFERENCES Questions(QuestionId),
  SelectedOptionId INT NULL REFERENCES QuestionOptions(OptionId),
  IsCorrect        BIT NULL,
  DisplayOrder     INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_PracticeAnswers UNIQUE (SessionId, QuestionId)
);
GO
```

- [ ] **Step 4: Run it, verify pass**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain05-practice.sql`
Expected: PASS — `valid practice session accepted`, `rejected bad status as expected`.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/domain05-practice.sql
git commit -m "feat(schema): Practice by Section (credit-free)"
```

### Task 6: MockExam domain (dynamic per-skill scores, FreeResponses, TotalScore trigger)

Implements Review Focus #2 (`TotalScore` = SUM of scaled skill scores) via a trigger, and carries forward the "published exams only" trigger from the old schema.

**Files:**
- Modify (append): `docs/database-schema.sql`
- Test: `docs/schema-tests/domain06-mockexam.sql`

**Interfaces:**
- Consumes: `Users`, `Exams`, `Skills`, `Questions`, `QuestionOptions`, `ScoreBands`.
- Produces: `ExamAttempts(AttemptId PK, TotalScore, BandCode)`, `AttemptSkillScores`, `AttemptAnswers`, `FreeResponses(FreeResponseId PK, Status)`. Later: AI Grading FKs `FreeResponseId`; Payment FKs `AttemptId`.

- [ ] **Step 1: Write the failing test (valid + published-only + TotalScore trigger)**

```sql
-- docs/schema-tests/domain06-mockexam.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'L',N'L','listening',1),(@c,'R',N'R','reading',2);
DECLARE @sL INT=(SELECT SkillId FROM Skills WHERE CertId=@c AND Code='L');
DECLARE @sR INT=(SELECT SkillId FROM Skills WHERE CertId=@c AND Code='R');
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'Draft','draft',120);
DECLARE @draft INT=SCOPE_IDENTITY();
INSERT INTO Exams (CertId,Name,Status,DurationMinutes) VALUES (@c,N'Pub','published',120);
DECLARE @pub INT=SCOPE_IDENTITY();
-- reject: attempt on a draft exam
BEGIN TRY
  INSERT INTO ExamAttempts (UserId,ExamId,Status,ExpiresAt) VALUES (@u,@draft,'in_progress',SYSDATETIMEOFFSET());
  THROW 50000,'EXPECT_REJECT FAILED: draft attempt accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected draft-exam attempt as expected';
END CATCH
-- valid: attempt on published exam, two skill scores, total = 250+250
INSERT INTO ExamAttempts (UserId,ExamId,Status,ExpiresAt,TotalScore)
  VALUES (@u,@pub,'submitted',SYSDATETIMEOFFSET(),500);
DECLARE @a INT=SCOPE_IDENTITY();
INSERT INTO AttemptSkillScores (AttemptId,SkillId,RawScore,ScaledScore)
  VALUES (@a,@sL,40,250),(@a,@sR,42,250);
PRINT 'valid published attempt accepted';
-- reject (Review Focus #2): TotalScore that disagrees with the skill sum
BEGIN TRY
  UPDATE ExamAttempts SET TotalScore=999 WHERE AttemptId=@a;
  THROW 50000,'EXPECT_REJECT FAILED: total mismatch accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected total/skill mismatch as expected';
END CATCH
```

- [ ] **Step 2: Run it, verify failure**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain06-mockexam.sql`
Expected: FAIL — `Invalid object name 'ExamAttempts'`.

- [ ] **Step 3: Append the DDL (tables + two triggers)**

```sql
-- ============ Domain 7: MockExam ============
CREATE TABLE ExamAttempts (
  AttemptId       INT IDENTITY(1,1) PRIMARY KEY,
  UserId          INT NOT NULL REFERENCES Users(UserId),
  ExamId          INT NOT NULL REFERENCES Exams(ExamId),
  Status          VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Attempts_Status CHECK (Status IN ('in_progress','submitted','graded','abandoned')),
  StartedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  ExpiresAt       DATETIMEOFFSET NOT NULL,          -- server-authoritative
  IsAutoSubmitted BIT NOT NULL DEFAULT 0,
  TotalScore      INT NULL,
  BandCode        VARCHAR(20) NULL                  -- snapshot at grading
);
GO
CREATE TABLE AttemptSkillScores (
  AttemptId   INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  SkillId     INT NOT NULL REFERENCES Skills(SkillId),
  RawScore    INT NOT NULL,
  ScaledScore INT NOT NULL,
  CONSTRAINT PK_AttemptSkillScores PRIMARY KEY (AttemptId, SkillId)
);
GO
CREATE TABLE AttemptAnswers (
  AnswerId         INT IDENTITY(1,1) PRIMARY KEY,
  AttemptId        INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  QuestionId       INT NOT NULL REFERENCES Questions(QuestionId),
  SelectedOptionId INT NULL REFERENCES QuestionOptions(OptionId),
  IsCorrect        BIT NULL,
  CONSTRAINT UQ_AttemptAnswers UNIQUE (AttemptId, QuestionId)
);
GO
CREATE TABLE FreeResponses (
  FreeResponseId INT IDENTITY(1,1) PRIMARY KEY,
  AttemptId      INT NOT NULL REFERENCES ExamAttempts(AttemptId),
  QuestionId     INT NOT NULL REFERENCES Questions(QuestionId),
  ResponseText   NVARCHAR(MAX) NULL,
  AudioPath      NVARCHAR(400) NULL,
  Status         VARCHAR(12) NOT NULL DEFAULT 'pending_ai'
    CONSTRAINT CK_FreeResp_Status CHECK (Status IN ('pending_ai','graded')),
  SubmittedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT UQ_FreeResponses UNIQUE (AttemptId, QuestionId),
  CONSTRAINT CK_FreeResp_HasContent CHECK (ResponseText IS NOT NULL OR AudioPath IS NOT NULL)
);
GO
-- attempts may only target published exams (carried from old schema)
CREATE TRIGGER trg_ExamAttempts_PublishedExamOnly ON ExamAttempts AFTER INSERT AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM inserted i JOIN Exams e ON e.ExamId=i.ExamId WHERE e.Status <> 'published')
  BEGIN
    THROW 50010, 'Attempt must target a published exam.', 1;
  END
END;
GO
-- Review Focus #2: TotalScore must equal SUM of scaled skill scores (when both present)
CREATE TRIGGER trg_Attempts_TotalMatchesSkills
ON AttemptSkillScores AFTER INSERT, UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT a.AttemptId
    FROM ExamAttempts a
    WHERE a.TotalScore IS NOT NULL
      AND a.AttemptId IN (SELECT AttemptId FROM inserted UNION SELECT AttemptId FROM deleted)
      AND a.TotalScore <> (SELECT ISNULL(SUM(ScaledScore),0) FROM AttemptSkillScores s WHERE s.AttemptId=a.AttemptId)
  )
    THROW 50011, 'TotalScore must equal the sum of scaled skill scores.', 1;
END;
GO
-- same check when TotalScore itself changes
CREATE TRIGGER trg_Attempts_TotalMatchesSkills_OnAttempt
ON ExamAttempts AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF UPDATE(TotalScore) AND EXISTS (
    SELECT i.AttemptId FROM inserted i
    WHERE i.TotalScore IS NOT NULL
      AND EXISTS (SELECT 1 FROM AttemptSkillScores s WHERE s.AttemptId=i.AttemptId)
      AND i.TotalScore <> (SELECT ISNULL(SUM(ScaledScore),0) FROM AttemptSkillScores s WHERE s.AttemptId=i.AttemptId)
  )
    THROW 50011, 'TotalScore must equal the sum of scaled skill scores.', 1;
END;
GO
```

- [ ] **Step 4: Run it, verify pass**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain06-mockexam.sql`
Expected: PASS — `rejected draft-exam attempt`, `valid published attempt accepted`, `rejected total/skill mismatch as expected`.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/domain06-mockexam.sql
git commit -m "feat(schema): MockExam with per-skill scores + TotalScore trigger"
```

### Task 7: AI Grading domain (rubric, scores, recommendations)

**Files:**
- Modify (append): `docs/database-schema.sql`
- Test: `docs/schema-tests/domain07-aigrading.sql`

**Interfaces:**
- Consumes: `FreeResponses`, `Skills`, `Certificates`, `ScoreBands`, `Users`.
- Produces: `GradingCriteria`, `AiGradings(GradingId PK)`, `AiGradingScores`, `Recommendations`. Charging is per-attempt in Payment (Task 12), not here.

- [ ] **Step 1: Write the failing test**

```sql
-- docs/schema-tests/domain07-aigrading.sql — abbreviated setup via the harness state
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- minimal chain: cert, skill(speaking), rubric criterion
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('IELTS',N'I',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'S','speaking',1);
DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO GradingCriteria (CertId,SkillId,Code,Name,MaxScore,Weight,DisplayOrder)
  VALUES (@c,@sk,'FLU',N'Fluency',9,1.0,1);
DECLARE @crit INT=SCOPE_IDENTITY();
PRINT 'valid rubric criterion accepted';
-- reject: grading status outside the set
BEGIN TRY
  INSERT INTO AiGradings (FreeResponseId,SkillId,Status) VALUES (1,@sk,'thinking');
  THROW 50000,'EXPECT_REJECT FAILED: bad grading status accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad grading status as expected';
END CATCH
```

- [ ] **Step 2: Run it, verify failure**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain07-aigrading.sql`
Expected: FAIL — `Invalid object name 'GradingCriteria'`.

- [ ] **Step 3: Append the DDL**

```sql
-- ============ Domain 8: AI Grading ============
CREATE TABLE GradingCriteria (
  CriterionId  INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  SkillId      INT NOT NULL REFERENCES Skills(SkillId),
  Code         VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  MaxScore     DECIMAL(5,2) NOT NULL,
  Weight       DECIMAL(5,2) NOT NULL DEFAULT 1.0,
  DisplayOrder INT NOT NULL DEFAULT 0,
  CONSTRAINT UQ_Criteria_Skill_Code UNIQUE (SkillId, Code)
);
GO
CREATE TABLE AiGradings (
  GradingId      INT IDENTITY(1,1) PRIMARY KEY,
  FreeResponseId INT NOT NULL REFERENCES FreeResponses(FreeResponseId),
  SkillId        INT NOT NULL REFERENCES Skills(SkillId),
  Status         VARCHAR(8) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_AiGradings_Status CHECK (Status IN ('pending','done','failed')),
  OverallScaled  DECIMAL(6,2) NULL,
  BandCode       VARCHAR(20) NULL,             -- snapshot
  Model          NVARCHAR(80) NULL,
  RequestedAt    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CompletedAt    DATETIMEOFFSET NULL,
  CONSTRAINT UQ_AiGradings_FreeResponse UNIQUE (FreeResponseId)
);
GO
CREATE TABLE AiGradingScores (
  GradingId   INT NOT NULL REFERENCES AiGradings(GradingId),
  CriterionId INT NOT NULL REFERENCES GradingCriteria(CriterionId),
  Score       DECIMAL(5,2) NOT NULL,
  Comment     NVARCHAR(MAX) NULL,
  CONSTRAINT PK_AiGradingScores PRIMARY KEY (GradingId, CriterionId)
);
GO
CREATE TABLE Recommendations (
  RecId          INT IDENTITY(1,1) PRIMARY KEY,
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  GradingId      INT NULL REFERENCES AiGradings(GradingId),   -- NULL = aggregate
  SkillId        INT NULL REFERENCES Skills(SkillId),
  Text           NVARCHAR(MAX) NOT NULL,
  Severity       VARCHAR(8) NOT NULL DEFAULT 'info'
    CONSTRAINT CK_Reco_Severity CHECK (Severity IN ('info','minor','major')),
  TargetBandCode VARCHAR(20) NULL,
  IsResolved     BIT NOT NULL DEFAULT 0,
  CreatedAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
```

- [ ] **Step 4: Run it, verify pass**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain07-aigrading.sql`
Expected: PASS — `valid rubric criterion accepted`, `rejected bad grading status as expected`.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/domain07-aigrading.sql
git commit -m "feat(schema): AI Grading (rubric, scores, recommendations)"
```

### Task 8: LearningPath domain (placement → path, per (UserId,CertId))

Implements Review Focus #3 (`PathModuleItems` ref/type match) and the active-path half of Review Focus #5.

**Files:**
- Modify (append): `docs/database-schema.sql`
- Test: `docs/schema-tests/domain08-learningpath.sql`

**Interfaces:**
- Consumes: `Users`, `Certificates`, `Exams`, `Sections`, `ScoreBands(CertId,Code)`.
- Produces: `PlacementTests`, `TargetBands`, `PathTemplates`, `PathModules`, `PathModuleItems`, `LearnerPaths(LearnerPathId PK)`, `LearnerPathSteps`. Payment (Task 10) FKs `LearnerPathId` for `premium_path`.

- [ ] **Step 1: Write the failing test**

```sql
-- docs/schema-tests/domain08-learningpath.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'R',N'R','reading',1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Sections (SkillId,CertId,Code,Name,OptionCount,DisplayOrder) VALUES (@sk,@c,'P5',N'IC',4,1); DECLARE @sec INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash,ActiveCertId) VALUES (@r,'u@x.com',0x00,@c); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO PathTemplates (CertId,FromBandCode,ToBandCode,Name) VALUES (@c,'below_A1','A2',N'Starter'); DECLARE @tpl INT=SCOPE_IDENTITY();
INSERT INTO PathModules (TemplateId,SkillId,DisplayOrder,Title) VALUES (@tpl,@sk,1,N'Reading'); DECLARE @mod INT=SCOPE_IDENTITY();
-- valid practice_section item (RefSectionId set, RefExamId null)
INSERT INTO PathModuleItems (ModuleId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
  VALUES (@mod,'practice_section',@sec,NULL,2,1);
INSERT INTO LearnerPaths (UserId,CertId,TemplateId,CurrentBandCode,Status) VALUES (@u,@c,@tpl,'below_A1','active');
PRINT 'valid path accepted';
-- reject (Review Focus #3): practice_section with NULL RefSectionId
BEGIN TRY
  INSERT INTO PathModuleItems (ModuleId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
    VALUES (@mod,'practice_section',NULL,NULL,2,2);
  THROW 50000,'EXPECT_REJECT FAILED: practice_section without section accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected practice_section w/o section as expected';
END CATCH
-- reject (Review Focus #3): mock_exam carrying a RefSectionId
BEGIN TRY
  INSERT INTO PathModuleItems (ModuleId,ItemType,RefSectionId,RefExamId,DifficultyLevel,DisplayOrder)
    VALUES (@mod,'mock_exam',@sec,NULL,2,3);
  THROW 50000,'EXPECT_REJECT FAILED: mock_exam with section accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected mock_exam with section as expected';
END CATCH
-- reject (Review Focus #5): a second active path for the same (UserId,CertId)
BEGIN TRY
  INSERT INTO LearnerPaths (UserId,CertId,TemplateId,CurrentBandCode,Status) VALUES (@u,@c,@tpl,'below_A1','active');
  THROW 50000,'EXPECT_REJECT FAILED: second active path accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected second active path as expected';
END CATCH
```

- [ ] **Step 2: Run it, verify failure**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain08-learningpath.sql`
Expected: FAIL — `Invalid object name 'PathTemplates'`.

- [ ] **Step 3: Append the DDL**

```sql
-- ============ Domain 9: LearningPath ============
CREATE TABLE PlacementTests (
  PlacementId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  ExamId         INT NULL REFERENCES Exams(ExamId),
  Status         VARCHAR(12) NOT NULL DEFAULT 'in_progress'
    CONSTRAINT CK_Placement_Status CHECK (Status IN ('in_progress','done','abandoned')),
  ResultBandCode VARCHAR(20) NULL,        -- snapshot
  TakenAt        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
CREATE TABLE TargetBands (
  UserId         INT NOT NULL REFERENCES Users(UserId),
  CertId         INT NOT NULL REFERENCES Certificates(CertId),
  TargetBandCode VARCHAR(20) NOT NULL,
  SetAt          DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT PK_TargetBands PRIMARY KEY (UserId, CertId),
  CONSTRAINT FK_TargetBands_Band FOREIGN KEY (CertId, TargetBandCode) REFERENCES ScoreBands(CertId, Code)
);
GO
CREATE TABLE PathTemplates (
  TemplateId   INT IDENTITY(1,1) PRIMARY KEY,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  FromBandCode VARCHAR(20) NOT NULL,
  ToBandCode   VARCHAR(20) NOT NULL,
  Name         NVARCHAR(120) NOT NULL,
  CONSTRAINT FK_PathTpl_FromBand FOREIGN KEY (CertId, FromBandCode) REFERENCES ScoreBands(CertId, Code),
  CONSTRAINT FK_PathTpl_ToBand   FOREIGN KEY (CertId, ToBandCode)   REFERENCES ScoreBands(CertId, Code)
);
GO
CREATE TABLE PathModules (
  ModuleId     INT IDENTITY(1,1) PRIMARY KEY,
  TemplateId   INT NOT NULL REFERENCES PathTemplates(TemplateId),
  SkillId      INT NOT NULL REFERENCES Skills(SkillId),
  DisplayOrder INT NOT NULL DEFAULT 0,
  Title        NVARCHAR(120) NOT NULL
);
GO
CREATE TABLE PathModuleItems (
  ItemId          INT IDENTITY(1,1) PRIMARY KEY,
  ModuleId        INT NOT NULL REFERENCES PathModules(ModuleId),
  ItemType        VARCHAR(16) NOT NULL
    CONSTRAINT CK_PMI_Type CHECK (ItemType IN ('practice_section','mock_exam','ai_task','mentor')),
  RefSectionId    INT NULL REFERENCES Sections(SectionId),
  RefExamId       INT NULL REFERENCES Exams(ExamId),
  DifficultyLevel INT NULL CONSTRAINT CK_PMI_Difficulty CHECK (DifficultyLevel IS NULL OR DifficultyLevel BETWEEN 1 AND 5),
  DisplayOrder    INT NOT NULL DEFAULT 0,
  -- Review Focus #3: the ref column must match ItemType
  CONSTRAINT CK_PMI_RefMatchesType CHECK (
       (ItemType='practice_section' AND RefSectionId IS NOT NULL AND RefExamId IS NULL)
    OR (ItemType='mock_exam'        AND RefExamId    IS NOT NULL AND RefSectionId IS NULL)
    OR (ItemType IN ('ai_task','mentor') AND RefSectionId IS NULL AND RefExamId IS NULL)
  )
);
GO
CREATE TABLE LearnerPaths (
  LearnerPathId   INT IDENTITY(1,1) PRIMARY KEY,
  UserId          INT NOT NULL REFERENCES Users(UserId),
  CertId          INT NOT NULL REFERENCES Certificates(CertId),
  TemplateId      INT NOT NULL REFERENCES PathTemplates(TemplateId),
  CurrentBandCode VARCHAR(20) NULL,
  Status          VARCHAR(10) NOT NULL DEFAULT 'active'
    CONSTRAINT CK_LearnerPaths_Status CHECK (Status IN ('active','completed','reset')),
  IsPremium       BIT NOT NULL DEFAULT 0,
  StartedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
-- Review Focus #5: at most one active path per (UserId,CertId)
CREATE UNIQUE INDEX UX_LearnerPaths_OneActive
  ON LearnerPaths(UserId, CertId) WHERE Status = 'active';
GO
CREATE TABLE LearnerPathSteps (
  StepId          INT IDENTITY(1,1) PRIMARY KEY,
  LearnerPathId   INT NOT NULL REFERENCES LearnerPaths(LearnerPathId),
  PathModuleItemId INT NOT NULL REFERENCES PathModuleItems(ItemId),
  Status          VARCHAR(10) NOT NULL DEFAULT 'locked'
    CONSTRAINT CK_Steps_Status CHECK (Status IN ('locked','available','done')),
  Score           DECIMAL(6,2) NULL,
  CompletedAt     DATETIMEOFFSET NULL
);
GO
```

- [ ] **Step 4: Run it, verify pass**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain08-learningpath.sql`
Expected: PASS — `valid path accepted`, both `rejected ...` path-item lines, `rejected second active path as expected`.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/domain08-learningpath.sql
git commit -m "feat(schema): LearningPath (placement, templates, per-cert active path)"
```

### Task 9: Mentor domain (profiles, slots, bookings, reviews, complaints, payouts)

Implements the slot-double-booking half of Review Focus #5. `Complaints.ResolvedByGrantTxnId` FK is deferred to Task 10 (needs `CreditTransactions`).

**Files:**
- Modify (append): `docs/database-schema.sql`
- Test: `docs/schema-tests/domain09-mentor.sql`

**Interfaces:**
- Consumes: `Users`, `Certificates`, `Skills`.
- Produces: `MentorProfiles(MentorId PK)`, `MentorSlots(SlotId PK)`, `Bookings(BookingId PK)`, `Reviews`, `Complaints(ComplaintId PK)`, `MentorPayouts`, `MentorPayoutItems`. Payment (Task 10) FKs `BookingId`; adds `Complaints.ResolvedByGrantTxnId`.

- [ ] **Step 1: Write the failing test**

```sql
-- docs/schema-tests/domain09-mentor.sql
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
INSERT INTO Roles (Code,Name) VALUES ('student',N'S'); DECLARE @r INT=SCOPE_IDENTITY();
INSERT INTO Certificates (Code,Name,IsActive) VALUES ('TOEIC',N'T',1); DECLARE @c INT=SCOPE_IDENTITY();
INSERT INTO Skills (CertId,Code,Name,Modality,DisplayOrder) VALUES (@c,'SPK',N'S','speaking',1); DECLARE @sk INT=SCOPE_IDENTITY();
INSERT INTO Users (RoleId,Email,PasswordHash) VALUES (@r,'u@x.com',0x00); DECLARE @u INT=SCOPE_IDENTITY();
INSERT INTO MentorProfiles (DisplayName,CertId,SkillId,Type,PricePerSlot,IsActive)
  VALUES (N'Coach',@c,@sk,'human',5,1); DECLARE @m INT=SCOPE_IDENTITY();
INSERT INTO MentorSlots (MentorId,StartAt,EndAt,Status)
  VALUES (@m,SYSDATETIMEOFFSET(),DATEADD(hour,1,SYSDATETIMEOFFSET()),'booked'); DECLARE @slot INT=SCOPE_IDENTITY();
INSERT INTO Bookings (UserId,MentorId,SlotId,Status,MeetLink) VALUES (@u,@m,@slot,'confirmed',N'https://meet');
PRINT 'valid booking accepted';
-- reject (Review Focus #5): a second booking on the same booked slot
BEGIN TRY
  INSERT INTO Bookings (UserId,MentorId,SlotId,Status) VALUES (@u,@m,@slot,'pending');
  THROW 50000,'EXPECT_REJECT FAILED: double-booked slot accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected double-booked slot as expected';
END CATCH
-- reject: review rating out of 1-5
BEGIN TRY
  INSERT INTO Reviews (BookingId,UserId,Rating,Text)
    SELECT TOP 1 BookingId,@u,7,N'x' FROM Bookings;
  THROW 50000,'EXPECT_REJECT FAILED: rating 7 accepted',1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected bad rating as expected';
END CATCH
```

- [ ] **Step 2: Run it, verify failure**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain09-mentor.sql`
Expected: FAIL — `Invalid object name 'MentorProfiles'`.

- [ ] **Step 3: Append the DDL**

```sql
-- ============ Domain 10: Mentor ============
CREATE TABLE MentorProfiles (
  MentorId     INT IDENTITY(1,1) PRIMARY KEY,
  UserId       INT NULL REFERENCES Users(UserId),   -- NULL for AI mentors
  DisplayName  NVARCHAR(120) NOT NULL,
  Bio          NVARCHAR(MAX) NULL,
  CertId       INT NOT NULL REFERENCES Certificates(CertId),
  SkillId      INT NULL REFERENCES Skills(SkillId),
  Type         VARCHAR(6) NOT NULL CONSTRAINT CK_Mentor_Type CHECK (Type IN ('human','ai')),
  PricePerSlot DECIMAL(10,2) NOT NULL CONSTRAINT CK_Mentor_Price CHECK (PricePerSlot >= 0),
  IsActive     BIT NOT NULL DEFAULT 1
);
GO
CREATE TABLE MentorSlots (
  SlotId   INT IDENTITY(1,1) PRIMARY KEY,
  MentorId INT NOT NULL REFERENCES MentorProfiles(MentorId),
  StartAt  DATETIMEOFFSET NOT NULL,
  EndAt    DATETIMEOFFSET NOT NULL,
  Status   VARCHAR(6) NOT NULL DEFAULT 'open'
    CONSTRAINT CK_Slots_Status CHECK (Status IN ('open','booked','closed')),
  CONSTRAINT CK_Slots_Range CHECK (EndAt > StartAt)
);
GO
CREATE TABLE Bookings (
  BookingId INT IDENTITY(1,1) PRIMARY KEY,
  UserId    INT NOT NULL REFERENCES Users(UserId),
  MentorId  INT NOT NULL REFERENCES MentorProfiles(MentorId),
  SlotId    INT NULL REFERENCES MentorSlots(SlotId),   -- NULL for AI
  Status    VARCHAR(10) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_Bookings_Status CHECK (Status IN ('pending','confirmed','done','cancelled')),
  MeetLink  NVARCHAR(400) NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
-- Review Focus #5: one live booking per slot (cancelled frees it)
CREATE UNIQUE INDEX UX_Bookings_OnePerSlot
  ON Bookings(SlotId) WHERE SlotId IS NOT NULL AND Status <> 'cancelled';
GO
CREATE TABLE Reviews (
  ReviewId  INT IDENTITY(1,1) PRIMARY KEY,
  BookingId INT NOT NULL REFERENCES Bookings(BookingId),
  UserId    INT NOT NULL REFERENCES Users(UserId),
  Rating    INT NOT NULL CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5),
  Text      NVARCHAR(MAX) NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT UQ_Reviews_OnePerBooking UNIQUE (BookingId)
);
GO
CREATE TABLE Complaints (
  ComplaintId          INT IDENTITY(1,1) PRIMARY KEY,
  BookingId            INT NOT NULL REFERENCES Bookings(BookingId),
  UserId               INT NOT NULL REFERENCES Users(UserId),
  Reason               NVARCHAR(MAX) NOT NULL,
  Status               VARCHAR(10) NOT NULL DEFAULT 'open'
    CONSTRAINT CK_Complaints_Status CHECK (Status IN ('open','reviewing','resolved')),
  Resolution           NVARCHAR(MAX) NULL,
  ResolvedByGrantTxnId INT NULL,      -- FK added in Task 10
  CreatedAt            DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
CREATE TABLE MentorPayouts (
  PayoutId     INT IDENTITY(1,1) PRIMARY KEY,
  MentorId     INT NOT NULL REFERENCES MentorProfiles(MentorId),
  PeriodStart  DATETIMEOFFSET NOT NULL,
  PeriodEnd    DATETIMEOFFSET NOT NULL,
  BookingCount INT NOT NULL DEFAULT 0,
  GrossAmount  DECIMAL(12,2) NOT NULL DEFAULT 0,
  Status       VARCHAR(7) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_Payouts_Status CHECK (Status IN ('pending','paid')),
  PaidAt       DATETIMEOFFSET NULL,
  CONSTRAINT CK_Payouts_Range CHECK (PeriodEnd > PeriodStart)
);
GO
CREATE TABLE MentorPayoutItems (
  PayoutId  INT NOT NULL REFERENCES MentorPayouts(PayoutId),
  BookingId INT NOT NULL REFERENCES Bookings(BookingId),
  CONSTRAINT PK_PayoutItems PRIMARY KEY (PayoutId, BookingId),
  CONSTRAINT UQ_PayoutItems_Booking UNIQUE (BookingId)   -- a booking settles once
);
GO
```

- [ ] **Step 4: Run it, verify pass**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain09-mentor.sql`
Expected: PASS — `valid booking accepted`, `rejected double-booked slot as expected`, `rejected bad rating as expected`.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/domain09-mentor.sql
git commit -m "feat(schema): Mentor (profiles, slots, bookings, reviews, complaints, payouts)"
```

### Task 10: Payment domain (ledger, spend reasons, no-negative-balance, expiry, complaint make-good)

Implements Review Focus #4 (no-negative-balance trigger) and the pending-order half of Review Focus #5. Lands last because its source columns FK into `Orders`, `ExamAttempts`, `Bookings`, and `LearnerPaths`, and it backfills `Complaints.ResolvedByGrantTxnId` — so every table it references already exists.

**Files:**
- Modify (append + one ALTER to `Complaints`): `docs/database-schema.sql`
- Test: `docs/schema-tests/domain10-payment.sql`

**Interfaces:**
- Consumes: `Users`, `ExamAttempts(AttemptId)`, `Bookings(BookingId)`, `LearnerPaths(LearnerPathId)`, `Complaints`.
- Produces: `Packages`, `Orders`, `CreditTransactions`, `vw_UserCreditBalance`, `usp_ExpireStalePendingOrders`. Backfills `Complaints.ResolvedByGrantTxnId` FK.

- [ ] **Step 1: Write the failing test**

```sql
-- docs/schema-tests/domain10-payment.sql
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
```

- [ ] **Step 2: Run it, verify failure**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain10-payment.sql`
Expected: FAIL — `Invalid object name 'Packages'`.

- [ ] **Step 3: Append the DDL**

```sql
-- ============ Domain 5: Payment ============
CREATE TABLE Packages (
  PackageId INT IDENTITY(1,1) PRIMARY KEY,
  Name      NVARCHAR(120) NOT NULL,
  Credits   INT NOT NULL CONSTRAINT CK_Packages_Credits CHECK (Credits > 0),
  Price     DECIMAL(12,2) NOT NULL CONSTRAINT CK_Packages_Price CHECK (Price >= 0),
  IsActive  BIT NOT NULL DEFAULT 1
);
GO
CREATE TABLE Orders (
  OrderId   INT IDENTITY(1,1) PRIMARY KEY,
  UserId    INT NOT NULL REFERENCES Users(UserId),
  PackageId INT NOT NULL REFERENCES Packages(PackageId),
  Status    VARCHAR(8) NOT NULL DEFAULT 'pending'
    CONSTRAINT CK_Orders_Status CHECK (Status IN ('pending','paid','failed','expired')),
  Amount    DECIMAL(12,2) NOT NULL,
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  PaidAt    DATETIMEOFFSET NULL
);
GO
-- Review Focus #5: one pending order per user
CREATE UNIQUE INDEX UX_Orders_OnePendingPerUser
  ON Orders(UserId) WHERE Status = 'pending';
GO
CREATE TABLE CreditTransactions (
  TxnId    INT IDENTITY(1,1) PRIMARY KEY,
  UserId   INT NOT NULL REFERENCES Users(UserId),
  Reason   VARCHAR(16) NOT NULL
    CONSTRAINT CK_CreditTxn_Reason CHECK (Reason IN
      ('purchase','admin_grant','mock_exam_start','ai_grading','mentor_booking','premium_path','admin_revoke')),
  Delta    INT NOT NULL CONSTRAINT CK_CreditTxn_NonZero CHECK (Delta <> 0),
  OrderId       INT NULL REFERENCES Orders(OrderId),
  AttemptId     INT NULL REFERENCES ExamAttempts(AttemptId),
  BookingId     INT NULL REFERENCES Bookings(BookingId),
  LearnerPathId INT NULL REFERENCES LearnerPaths(LearnerPathId),
  CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
  -- sign per reason
  CONSTRAINT CK_CreditTxn_DeltaSign CHECK (
       (Reason IN ('purchase','admin_grant') AND Delta > 0)
    OR (Reason IN ('mock_exam_start','ai_grading','mentor_booking','premium_path','admin_revoke') AND Delta < 0)
  ),
  -- exactly the source column matching the reason is set (admin_* carry none)
  CONSTRAINT CK_CreditTxn_Source CHECK (
       (Reason='purchase'        AND OrderId IS NOT NULL AND AttemptId IS NULL AND BookingId IS NULL AND LearnerPathId IS NULL)
    OR (Reason='mock_exam_start' AND AttemptId IS NOT NULL AND OrderId IS NULL AND BookingId IS NULL AND LearnerPathId IS NULL)
    OR (Reason='ai_grading'      AND AttemptId IS NOT NULL AND OrderId IS NULL AND BookingId IS NULL AND LearnerPathId IS NULL)
    OR (Reason='mentor_booking'  AND BookingId IS NOT NULL AND OrderId IS NULL AND AttemptId IS NULL AND LearnerPathId IS NULL)
    OR (Reason='premium_path'    AND LearnerPathId IS NOT NULL AND OrderId IS NULL AND AttemptId IS NULL AND BookingId IS NULL)
    OR (Reason IN ('admin_grant','admin_revoke') AND OrderId IS NULL AND AttemptId IS NULL AND BookingId IS NULL AND LearnerPathId IS NULL)
  )
);
GO
CREATE VIEW vw_UserCreditBalance AS
  SELECT UserId, SUM(Delta) AS Balance
  FROM CreditTransactions GROUP BY UserId;
GO
-- Review Focus #4: no debit may drive the running balance below zero
CREATE TRIGGER trg_CreditTransactions_NoNegativeBalance
ON CreditTransactions AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (
    SELECT 1 FROM (SELECT DISTINCT UserId FROM inserted) u
    WHERE (SELECT SUM(Delta) FROM CreditTransactions t WHERE t.UserId=u.UserId) < 0
  )
    THROW 50020, 'Transaction would drive wallet balance negative.', 1;
END;
GO
-- backfill the complaint make-good FK now that CreditTransactions exists
ALTER TABLE Complaints
  ADD CONSTRAINT FK_Complaints_GrantTxn FOREIGN KEY (ResolvedByGrantTxnId) REFERENCES CreditTransactions(TxnId);
GO
-- expire stale pending orders (carried from old schema; frees UX_Orders_OnePendingPerUser)
CREATE PROCEDURE usp_ExpireStalePendingOrders @OlderThanMinutes INT = 15 AS
BEGIN
  SET NOCOUNT ON;
  UPDATE Orders SET Status='expired'
  WHERE Status='pending'
    AND CreatedAt < DATEADD(minute, -@OlderThanMinutes, SYSDATETIMEOFFSET());
END;
GO
```

- [ ] **Step 4: Run it, verify pass**

Run: `bash docs/schema-tests/reset-and-load.sh && sqlcmd -S '(localdb)\MSSQLLocalDB' -d CompanionSchemaTest -I -b -i docs/schema-tests/domain10-payment.sql`
Expected: PASS — `rejected second pending order`, `wallet balance = 10 as expected`, `rejected overdrawing debit`, `rejected reason/source mismatch as expected`.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/domain10-payment.sql
git commit -m "feat(schema): Payment ledger with spend reasons + guards"
```

### Task 11: Finalize — accuracy view + full-suite run

**Files:**
- Modify (append): `docs/database-schema.sql`
- Create: `docs/schema-tests/run-all.sh`
- Test: all `docs/schema-tests/domain*.sql` in sequence

**Interfaces:**
- Consumes: everything.
- Produces: `vw_UserSectionAccuracy` (the renamed `vw_UserPartAccuracy`), `run-all.sh`.

- [ ] **Step 1: Append the accuracy view (Part → Section)**

```sql
-- ============ Views ============
CREATE VIEW vw_UserSectionAccuracy AS
  SELECT ps.UserId, s.SectionId, s.Name AS SectionName,
         COUNT(*) AS Answered,
         SUM(CASE WHEN pa.IsCorrect=1 THEN 1 ELSE 0 END) AS Correct
  FROM PracticeAnswers pa
  JOIN PracticeSessions ps ON ps.SessionId = pa.SessionId
  JOIN Sections s ON s.SectionId = ps.SectionId
  GROUP BY ps.UserId, s.SectionId, s.Name;
GO
```

- [ ] **Step 2: Write the full-suite runner**

```bash
#!/usr/bin/env bash
# docs/schema-tests/run-all.sh — load schema once, run every domain test on its own fresh DB.
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S='(localdb)\MSSQLLocalDB'; DB='CompanionSchemaTest'
for t in "$DIR"/domain*.sql; do
  echo "=== $t ==="
  bash "$DIR/reset-and-load.sh" >/dev/null
  sqlcmd -S "$S" -d "$DB" -I -b -i "$t"
done
echo "ALL DOMAIN TESTS PASSED"
```

- [ ] **Step 3: Run the whole suite**

Run: `bash docs/schema-tests/run-all.sh`
Expected: each domain prints its `accepted`/`rejected ... as expected` lines, then `ALL DOMAIN TESTS PASSED`, exit 0.

- [ ] **Step 4: Confirm the full schema loads clean from scratch one final time**

Run: `bash docs/schema-tests/reset-and-load.sh`
Expected: `LOADED OK`.

- [ ] **Step 5: Commit**

```bash
git add docs/database-schema.sql docs/schema-tests/run-all.sh
git commit -m "feat(schema): section-accuracy view + full-suite runner"
```







