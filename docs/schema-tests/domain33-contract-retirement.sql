SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- docs/schema-tests/domain33-contract-retirement.sql
-- #231 / D1: the difficulty scale is gone from the schema, and a re-run cannot resurrect it.
--
-- The 1-5 difficulty was a second level model beside CEFR. D1 forbids both the scale and any mapping
-- between them, so its absence is the invariant worth guarding: a column that came back would let a
-- writer put a rank where a level belongs, and every authoring path would have two answers again.
--
-- Both assertions are made against the catalog, not against a sample row. A test that inserted a row
-- and asserted "the difficulty is null" passes on a table with no such column at all - which is why
-- the #228 guard had to be replaced rather than kept, and why this one names sys.columns directly.
--
-- The scan is proved non-empty first: a lookup for a column name that no table has would report
-- "absent" for every table and pass without touching anything (lib.sql records that exact failure
-- mode from domain04, where a CHECK that was in fact rejecting 9 was reported as accepting it).
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
GO

-- 1. the scan itself works: a column that does exist is found by the same lookup the assertions use.
IF NOT EXISTS (
  SELECT 1 FROM sys.columns c
  JOIN sys.tables t ON t.object_id = c.object_id
  WHERE t.name = 'Questions' AND c.name = 'CefrLevel'
)
  THROW 50000, 'EXPECT_REJECT FAILED: the column lookup cannot see Questions.CefrLevel, so its absence checks prove nothing', 1;
PRINT 'the column lookup finds a column that exists';

-- 2. Questions carries no difficulty, and its 1-5 CHECK is gone with the column.
IF EXISTS (
  SELECT 1 FROM sys.columns c
  JOIN sys.tables t ON t.object_id = c.object_id
  WHERE t.name = 'Questions' AND c.name = 'DifficultyLevel'
)
  THROW 50000, 'EXPECT_REJECT FAILED: Questions.DifficultyLevel still exists; #231 dropped it', 1;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Questions_Difficulty')
  THROW 50000, 'EXPECT_REJECT FAILED: CK_Questions_Difficulty still exists; it went with the column', 1;
PRINT 'Questions carries no difficulty and no 1-5 CHECK';

-- 3. PathModuleItems likewise. Its level requirement is unexpressed here by decision: the column was
--    removed rather than replaced, and #232 carries the level on LearnerPathSteps.
IF EXISTS (
  SELECT 1 FROM sys.columns c
  JOIN sys.tables t ON t.object_id = c.object_id
  WHERE t.name = 'PathModuleItems' AND c.name = 'DifficultyLevel'
)
  THROW 50000, 'EXPECT_REJECT FAILED: PathModuleItems.DifficultyLevel still exists; #231 dropped it', 1;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PMI_Difficulty')
  THROW 50000, 'EXPECT_REJECT FAILED: CK_PMI_Difficulty still exists; it went with the column', 1;
PRINT 'PathModuleItems carries no difficulty and no 1-5 CHECK';

-- 4. the level column the difficulty was replaced by is still the authored ladder, unchanged. A
--    retirement that widened this CHECK would let 'pre_a1' content in through the back door.
IF NOT EXISTS (
  SELECT 1 FROM sys.check_constraints
  WHERE name = 'CK_Questions_Level' AND definition LIKE '%''A1''%' AND definition LIKE '%''C2''%'
)
  THROW 50000, 'EXPECT_REJECT FAILED: CK_Questions_Level is missing or no longer names A1..C2', 1;
PRINT 'the authored-level CHECK still names A1..C2';

PRINT 'the difficulty scale is retired from the schema; the level is the only authored fact';
