SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- #183: the topic vocabulary is one shared axis of the warehouse, not a per-certificate label.
--
-- A topic belongs to the language: it is addressed by (modality, code) and carries no CertId.
-- The matrix is 20 rows exactly - six listening, six reading, four speaking, four writing - and
-- this file proves four things:
--   1. the canonical twenty exist and nothing else does;
--   2. the per-modality split is 6/6/4/4, so a listening topic cannot be authored under writing;
--   3. a topic can exist with no certificate in the database at all;
--   4. a modality outside the four is refused.
--
-- A twenty-first topic is a defect, not a feature: coverage scales by depth (how much content a
-- cell holds), never by breadth (how many topics a skill has). Twelve empty categories out of
-- fifteen is what breadth-first coverage produced.

-- 1. exactly twenty, and each one canonical
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

IF (SELECT COUNT(*) FROM Topics) <> 20
  THROW 50000, 'EXPECT_REJECT FAILED: the topic vocabulary is not exactly twenty rows', 1;
PRINT 'accepted the canonical twenty topics';

-- 2. per-modality split 6/6/4/4
IF EXISTS (
  SELECT Modality, COUNT(*) AS n FROM Topics GROUP BY Modality
  HAVING COUNT(*) NOT IN (6, 4)
)
  THROW 50000, 'EXPECT_REJECT FAILED: a modality holds a topic count that is neither six nor four', 1;

IF (SELECT COUNT(*) FROM Topics WHERE Modality = 'listening') <> 6
  THROW 50000, 'EXPECT_REJECT FAILED: listening does not hold exactly six topics', 1;
IF (SELECT COUNT(*) FROM Topics WHERE Modality = 'reading') <> 6
  THROW 50000, 'EXPECT_REJECT FAILED: reading does not hold exactly six topics', 1;
IF (SELECT COUNT(*) FROM Topics WHERE Modality = 'speaking') <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: speaking does not hold exactly four topics', 1;
IF (SELECT COUNT(*) FROM Topics WHERE Modality = 'writing') <> 4
  THROW 50000, 'EXPECT_REJECT FAILED: writing does not hold exactly four topics', 1;
PRINT 'accepted the 6/6/4/4 split';

-- 3. no Certificates row exists here, so the rows above prove a topic needs no certificate
IF EXISTS (SELECT 1 FROM Certificates)
  THROW 50000, 'EXPECT_REJECT FAILED: this file must run with no certificate, or it proves nothing', 1;
PRINT 'topics exist with no certificate in the database';

-- 4. a twenty-first topic is refused. The vocabulary is a closed set, and the guard is what
--    stops a later ticket adding a convenient sixth listening topic and silently changing the
--    denominator every mastery figure divides by.
BEGIN TRY
  INSERT INTO Topics (Modality, Code, Name) VALUES ('listening','L-EXTRA',N'An extra topic nobody asked for');
  THROW 50000, 'EXPECT_REJECT FAILED: a twenty-first topic was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a twenty-first topic as expected';
END CATCH

-- 5. modality is one of the four
BEGIN TRY
  INSERT INTO Topics (Modality, Code, Name) VALUES ('grammar','X-GRAM',N'A modality that does not exist');
  THROW 50000, 'EXPECT_REJECT FAILED: an unknown modality was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected an unknown modality as expected';
END CATCH

-- 6. duplicate (modality, code) is refused
BEGIN TRY
  INSERT INTO Topics (Modality, Code, Name) VALUES ('reading','R-DET',N'A duplicate detail topic');
  THROW 50000, 'EXPECT_REJECT FAILED: a duplicate (modality, code) was accepted', 1;
END TRY BEGIN CATCH
  IF ERROR_MESSAGE() LIKE 'EXPECT_REJECT FAILED%' THROW;
  PRINT 'rejected a duplicate (modality, code) as expected';
END CATCH
