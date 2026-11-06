SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #202 / S-01: the coverage matrix.
--
-- The screen's whole job is to make an empty cell something you look at rather than something you
-- query, so the view has to produce a row for a cell that holds nothing. That is the assertion this
-- file exists for: an aggregation grouped over Items cannot produce such a row, because a cell with
-- no items has nothing to group. Sections 3 to 5 fail if the view was built that way.
--
--   1. ContentQuotas is the content budget: one row per (skill, level), and no CHECK names a pool
--      size. The numbers live in rows so a later content ticket re-sizes a cell without a schema
--      edit (D0.5).
--   2. the matrix has 120 rows for the canonical twenty topics (20 x 6), never 20 and never 0.
--   3. an empty cell is present, holds zero, and still carries its target and its deficit.
--   4. only active items are stock: a retired, quarantined or draft item buys no coverage.
--   5. the deficit never goes negative when a cell is over-supplied.
--
-- Every count is read from the view. Where a number is not asserted by name it is compared against
-- the quota row itself, so this file does not become a second copy of the volume tiers.

DECLARE @guardWrong NVARCHAR(2048);
DECLARE @msg NVARCHAR(2048);

-- ---------------------------------------------------------------------------------------------
-- 1. the quota table is the budget: one row per (skill, level), all four skills, all six levels
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID('ContentQuotas') IS NULL
  THROW 50000, 'EXPECT_REJECT FAILED: ContentQuotas does not exist, so the quota is not data', 1;

IF (SELECT COUNT(*) FROM ContentQuotas) <> 24
  THROW 50000, 'EXPECT_REJECT FAILED: ContentQuotas does not hold 24 rows (4 skills x 6 levels)', 1;

IF EXISTS (
  SELECT Modality, COUNT(*) AS n FROM ContentQuotas GROUP BY Modality HAVING COUNT(*) <> 6
)
  THROW 50000, 'EXPECT_REJECT FAILED: a skill does not hold exactly six quota rows', 1;

IF EXISTS (SELECT 1 FROM ContentQuotas WHERE TargetItemCount <= 0)
  THROW 50000, 'EXPECT_REJECT FAILED: a quota row carries a target of zero or less', 1;

-- the six levels are the authored ladder, and every one of them has a row for every skill
IF EXISTS (
  SELECT 1 FROM ContentQuotas WHERE CefrLevel NOT IN ('A1','A2','B1','B2','C1','C2')
)
  THROW 50000, 'EXPECT_REJECT FAILED: ContentQuotas carries a level outside A1-C2', 1;
PRINT 'ContentQuotas holds one budget row per (skill, level)';

BEGIN TRY
  INSERT INTO ContentQuotas (Modality, CefrLevel, TargetItemCount) VALUES ('grammar','B1',30);
  THROW 50000, 'EXPECT_REJECT FAILED: a quota row for a skill that does not exist was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  IF ERROR_MESSAGE() NOT LIKE '%CK_ContentQuotas_Modality%'
  BEGIN
    SET @guardWrong = N'EXPECT_REJECT FAILED: the invented skill was refused by the wrong guard: ' + ERROR_MESSAGE();
    THROW 50000, @guardWrong, 1;
  END
  PRINT 'rejected a quota row for an invented skill, by CK_ContentQuotas_Modality';
END CATCH

-- ---------------------------------------------------------------------------------------------
-- 2. the canonical twenty topics, and the 120-cell shape
-- ---------------------------------------------------------------------------------------------
INSERT INTO Topics (Modality, Code, Name) VALUES
  ('listening','L-GIST',N'Global gist, purpose, macro-topic'),
  ('listening','L-DET', N'Explicit factual detail and extraction'),
  ('listening','L-INF', N'Pragmatic implication and logical inference'),
  ('listening','L-ATT', N'Speaker stance, attitude, prosodic emotion'),
  ('listening','L-FUNC',N'Communicative function and speech acts'),
  ('listening','L-ORG', N'Discourse structure, signposting, synthesis'),
  ('reading',  'R-GIST',N'Global thesis, purpose, paragraph main idea'),
  ('reading',  'R-DET', N'Local factual retrieval and paraphrase matching'),
  ('reading',  'R-INF', N'Bridging inference and logical deduction'),
  ('reading',  'R-ATT', N'Authorial stance, rhetorical purpose, tone'),
  ('reading',  'R-COH', N'Discourse cohesion, reference, text insertion'),
  ('reading',  'R-GRAM',N'Morphosyntactic structure and lexical precision'),
  ('speaking', 'S-DESC',N'Informational description and personal framing'),
  ('speaking', 'S-INT', N'Transactional interaction and option negotiation'),
  ('speaking', 'S-ARG', N'Sustained monologic argumentation and justification'),
  ('speaking', 'S-SYN', N'Integrated summary and visual/data interpretation'),
  ('writing',  'W-CORR',N'Transactional correspondence and register control'),
  ('writing',  'W-DATA',N'Visual data, process, and informational synthesis'),
  ('writing',  'W-ARG', N'Thesis-driven persuasive and opinion argumentation'),
  ('writing',  'W-DISC',N'Multi-perspective problem-solution and evaluation');

DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Code = 'R-DET');

-- nothing is authored yet: every one of the 120 cells is empty, and every one must still be a row
IF (SELECT COUNT(*) FROM vw_CoverageMatrix) <> 120
BEGIN
  SET @msg = N'EXPECT_REJECT FAILED: the matrix holds '
    + CONVERT(NVARCHAR(10), (SELECT COUNT(*) FROM vw_CoverageMatrix))
    + N' cells for twenty topics x six levels, not 120';
  THROW 50000, @msg, 1;
END
IF EXISTS (SELECT 1 FROM vw_CoverageMatrix WHERE ActiveItemCount <> 0)
  THROW 50000, 'EXPECT_REJECT FAILED: a cell reports stock in a database with no items', 1;
PRINT 'the matrix is 120 rows with no item authored at all';

-- ---------------------------------------------------------------------------------------------
-- 3. the empty cell: present, zero, and carrying a deficit. This is the ticket's fourth criterion
--    at the data layer - a grouped-over-Items view simply has no row here.
-- ---------------------------------------------------------------------------------------------
IF NOT EXISTS (
  SELECT 1 FROM vw_CoverageMatrix
  WHERE TopicCode = 'R-DET' AND CefrLevel = 'A2'
)
  THROW 50000, 'EXPECT_REJECT FAILED: an empty cell is absent from the matrix instead of reporting a deficit', 1;

IF EXISTS (
  SELECT 1 FROM vw_CoverageMatrix
  WHERE TopicCode = 'R-DET' AND CefrLevel = 'A2'
    AND (ActiveItemCount <> 0 OR Deficit IS NULL OR Deficit <> TargetItemCount OR TargetItemCount <= 0)
)
  THROW 50000, 'EXPECT_REJECT FAILED: an empty cell does not report zero stock against its own target and deficit', 1;
PRINT 'an empty cell is a row reporting zero stock and a deficit';

-- every cell's target is its modality's quota row, never a number repeated in the view
IF EXISTS (
  SELECT 1 FROM vw_CoverageMatrix m
  JOIN Topics t ON t.TopicId = m.TopicId
  JOIN ContentQuotas q ON q.Modality = t.Modality AND q.CefrLevel = m.CefrLevel
  WHERE m.TargetItemCount <> q.TargetItemCount
)
  THROW 50000, 'EXPECT_REJECT FAILED: a cell''s target does not match its ContentQuotas row', 1;
PRINT 'every target is read from its quota row';

-- ---------------------------------------------------------------------------------------------
-- 4. only active items are stock. Two active items are authored here, plus one retired and one
--    draft - and the retired one is placement-eligible on purpose, so a view that counts
--    PlacementEligible without the lifecycle filter reports 2 instead of 1.
-- ---------------------------------------------------------------------------------------------
INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus)
VALUES ('reading', @topic, 'B1', 'passage_comprehension', 1, 'production', 'active'),
       ('reading', @topic, 'B1', 'passage_comprehension', 0, 'production', 'active'),
       ('reading', @topic, 'B1', 'passage_comprehension', 1, 'production', 'retired'),
       ('reading', @topic, 'B1', 'passage_comprehension', 1, 'production', 'draft');

IF (SELECT ActiveItemCount FROM vw_CoverageMatrix WHERE TopicCode = 'R-DET' AND CefrLevel = 'B1') <> 2
  THROW 50000, 'EXPECT_REJECT FAILED: the B1 cell does not count exactly the two active items', 1;

IF (SELECT PlacementEligibleItemCount FROM vw_CoverageMatrix WHERE TopicCode = 'R-DET' AND CefrLevel = 'B1') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: the B1 cell counts a retired or draft item as placement-eligible stock', 1;

IF (SELECT Deficit FROM vw_CoverageMatrix WHERE TopicCode = 'R-DET' AND CefrLevel = 'B1')
   <> (SELECT TargetItemCount - 2 FROM ContentQuotas WHERE Modality = 'reading' AND CefrLevel = 'B1')
  THROW 50000, 'EXPECT_REJECT FAILED: the deficit is not the target less the active items', 1;

-- ...and the four items did not disturb a single other cell
IF (SELECT COUNT(*) FROM vw_CoverageMatrix WHERE ActiveItemCount <> 0) <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: authoring two items changed more than one cell', 1;
PRINT 'only active items buy coverage, and only in their own cell';

-- ---------------------------------------------------------------------------------------------
-- 5. a satisfied cell reports a deficit of zero, not a negative number. The quota is data, so this
--    is exercised by shrinking one row rather than by authoring thirty items.
-- ---------------------------------------------------------------------------------------------
UPDATE ContentQuotas SET TargetItemCount = 1 WHERE Modality = 'reading' AND CefrLevel = 'B1';

IF (SELECT Deficit FROM vw_CoverageMatrix WHERE TopicCode = 'R-DET' AND CefrLevel = 'B1') <> 0
  THROW 50000, 'EXPECT_REJECT FAILED: an over-supplied cell reports a negative deficit', 1;
IF EXISTS (SELECT 1 FROM vw_CoverageMatrix WHERE Deficit < 0)
  THROW 50000, 'EXPECT_REJECT FAILED: the matrix holds a negative deficit', 1;
PRINT 'an over-supplied cell reports a deficit of zero';

-- the view is read-only by construction: it is a view, so there is nothing to declare a writer on
IF OBJECTPROPERTY(OBJECT_ID('vw_CoverageMatrix'), 'IsView') <> 1
  THROW 50000, 'EXPECT_REJECT FAILED: vw_CoverageMatrix is not a view', 1;
PRINT 'the matrix is one read-only view, fed by a data-held quota';
