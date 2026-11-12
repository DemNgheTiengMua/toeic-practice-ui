SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- docs/schema-tests/domain33-contract-retirement.sql
-- #231 / D1: the difficulty scale is gone from the schema, and a re-run cannot resurrect it.
-- #269: and so is the legacy content model — QuestionGroups, Questions, QuestionOptions and
-- UserQuestionHistories. This file is one of the legitimate places that NAMES a removed table on
-- purpose: its subject is that the object is ABSENT, and it cannot prove an absence without naming
-- the thing.
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
--    #269: the authored level now lives on Items, so that is the column the control arm looks for.
IF NOT EXISTS (
  SELECT 1 FROM sys.columns c
  JOIN sys.tables t ON t.object_id = c.object_id
  WHERE t.name = 'Items' AND c.name = 'CefrLevel'
)
  THROW 50000, 'EXPECT_REJECT FAILED: the column lookup cannot see Items.CefrLevel, so its absence checks prove nothing', 1;
PRINT 'the column lookup finds a column that exists';

-- 2. the pool carries no difficulty, and the 1-5 CHECK is gone with it. #231 dropped the column from
--    Questions; #269 dropped Questions itself, so the rule that survives is "the table that replaced it
--    did not bring the scale back".
IF EXISTS (
  SELECT 1 FROM sys.columns c
  JOIN sys.tables t ON t.object_id = c.object_id
  WHERE t.name = 'Items' AND c.name = 'DifficultyLevel'
)
  THROW 50000, 'EXPECT_REJECT FAILED: Items.DifficultyLevel exists; #231 retired the 1-5 scale', 1;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Questions_Difficulty')
  THROW 50000, 'EXPECT_REJECT FAILED: CK_Questions_Difficulty still exists; it went with the column and the table', 1;
IF EXISTS (
  SELECT 1 FROM sys.columns c
  JOIN sys.tables t ON t.object_id = c.object_id
  WHERE c.name = 'DifficultyLevel'
)
  THROW 50000, 'EXPECT_REJECT FAILED: a DifficultyLevel column is back on some table; D1 forbids the scale anywhere', 1;
PRINT 'no table carries a difficulty and no 1-5 CHECK survives';

-- 3. #269: the four legacy content objects are gone. Each is named, and the absence is asserted on the
--    catalog - the same way the path scaffolding below is.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'QuestionGroups')
  THROW 50000, 'EXPECT_REJECT FAILED: QuestionGroups still exists; a paper is a roster of ExamItems', 1;
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Questions')
  THROW 50000, 'EXPECT_REJECT FAILED: Questions still exists; item content lives on Stimuli/Items/ItemVersions', 1;
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'QuestionOptions')
  THROW 50000, 'EXPECT_REJECT FAILED: QuestionOptions still exists; a version stores OptionA..D and one KeyLabel', 1;
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'UserQuestionHistories')
  THROW 50000, 'EXPECT_REJECT FAILED: UserQuestionHistories still exists; mastery is a fact about a cell', 1;
PRINT 'QuestionGroups, Questions, QuestionOptions and UserQuestionHistories are all gone';

-- and the columns those tables carried are not hiding on a survivor. Each was a second way to name a
-- question on a table that now names an ITEM, so a leftover would be a column nothing can fill.
IF EXISTS (
  SELECT 1 FROM sys.columns
  WHERE name = 'QuestionId'
)
  THROW 50000, 'EXPECT_REJECT FAILED: a QuestionId column survives on some table; #269 removed every one', 1;
PRINT 'no surviving table carries a QuestionId column';

-- 4. #232 / #236: the template scaffolding is gone, so a column lookup against it would find nothing
--    and report the table clean while it is absent for a different reason. Assert each drop itself,
--    and that the level requirement has a home: a step's own TargetCefr, with its authored-level
--    CHECK. A lookup that found nothing would pass this check on a database with no path steps at
--    all, so the CHECK is required to exist by name rather than merely not to be contradicted.
--
--    All four tables are named, not one. Asserting only PathModuleItems would let PathTemplates come
--    back and "no code reads a template" would still pass, because the code stays deleted while the
--    table waits for a new reader.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PathTemplates')
  THROW 50000, 'EXPECT_REJECT FAILED: PathTemplates still exists; D10 dropped the template scaffolding', 1;
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PathModules')
  THROW 50000, 'EXPECT_REJECT FAILED: PathModules still exists; D10 dropped the template scaffolding', 1;
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PathModuleItems')
  THROW 50000, 'EXPECT_REJECT FAILED: PathModuleItems still exists; D10 dropped the template scaffolding', 1;
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PathModuleTopics')
  THROW 50000, 'EXPECT_REJECT FAILED: PathModuleTopics still exists; D10 dropped the template scaffolding', 1;
IF EXISTS (
  SELECT 1 FROM sys.columns c
  JOIN sys.tables t ON t.object_id = c.object_id
  WHERE t.name = 'LearnerPaths' AND c.name = 'TemplateId'
)
  THROW 50000, 'EXPECT_REJECT FAILED: LearnerPaths.TemplateId still exists; a path is synthesised, not looked up', 1;
PRINT 'the four template tables and LearnerPaths.TemplateId are dropped';

IF NOT EXISTS (
  SELECT 1 FROM sys.columns c
  JOIN sys.tables t ON t.object_id = c.object_id
  WHERE t.name = 'LearnerPathSteps' AND c.name = 'TargetCefr'
)
  THROW 50000, 'EXPECT_REJECT FAILED: LearnerPathSteps.TargetCefr is missing; #232 carries the step level here', 1;
IF NOT EXISTS (
  SELECT 1 FROM sys.check_constraints
  WHERE name = 'CK_Steps_TargetCefr' AND definition LIKE '%''A1''%' AND definition LIKE '%''C2''%'
)
  THROW 50000, 'EXPECT_REJECT FAILED: CK_Steps_TargetCefr is missing or does not name A1..C2', 1;
PRINT 'the step carries its own level with an authored-level CHECK';

-- 5. the level column the difficulty was replaced by is still the authored ladder, unchanged. A
--    retirement that widened this CHECK would let 'pre_a1' content in through the back door.
--    #269: the surviving CHECK is Items.CK_Items_Level (CK_Questions_Level died with its table).
IF NOT EXISTS (
  SELECT 1 FROM sys.check_constraints
  WHERE name = 'CK_Items_Level' AND definition LIKE '%''A1''%' AND definition LIKE '%''C2''%'
)
  THROW 50000, 'EXPECT_REJECT FAILED: CK_Items_Level is missing or no longer names A1..C2', 1;
PRINT 'the authored-level CHECK still names A1..C2';

PRINT 'the difficulty scale is retired from the schema; the level is the only authored fact';
