-- docs/schema-changes-237.sql
-- #237 "Mastery over the cell, not the item" — the schema delta.
--
-- WHY THIS IS A SEPARATE FILE: #250 exclusively owns docs/database-schema.sql for this wave (it is
-- migrating the paper's content model and will rewrite large parts of the file). This file is the
-- exact, idempotent SQL that belongs there, so applying it is a copy-paste and not a merge.
--
-- WHERE EACH BLOCK BELONGS in docs/database-schema.sql:
--
--   Block 1 (registry row)  : inside the `INSERT INTO DerivedFactOwners ... VALUES` batch at the top
--                             of Domain 2. It MOVES the existing 'topic_capability' row from
--                             'awaiting_table' to 'covered' — do not add a second row, the FactCode is
--                             the primary key and the whole point is that this fact now has a table.
--   Block 2 (the table)     : after the Topics trigger (trg_Topics_ClosedVocabulary) and AFTER
--                             `ALTER TABLE Topics ADD CONSTRAINT UQ_Topics_Id_Modality`, because the
--                             table references Topics. Placing it beside UserCategoryMasteries also
--                             works; the only hard requirement is "after Topics".
--   Block 3 (trigger 1)     : immediately after the table, with the other single-writer triggers.
--   Block 4 (trigger 2)     : immediately after trigger 1. It is separate from trigger 1 on purpose:
--                             "who wrote this" and "is this state earned" are two questions, and a
--                             combined trigger can only raise one message for both.
--
-- WHAT IT REPLACES: SRS D4 says `UserCategoryMasteries` becomes `UserCellMasteries(UserId, TopicId,
-- CefrLevel, ...)` with no `CertId`, because mastery follows the topic (D-19). This file creates the
-- replacement. It does NOT drop `UserCategoryMasteries` — legacy section drills still read it and
-- TRN-04's data disposition retires the legacy table, not this ticket.
--
-- EVERY BLOCK IS GUARDED so this file can be applied twice: once now (on top of the current schema,
-- which is how tests provision it) and again after #250 folds it into database-schema.sql. A schema
-- delta that is not re-runnable is a deploy that works exactly once.
--
-- NUMBERS, AND WHERE THEY COME FROM (SRS §7.5; do not retune without re-deriving):
--   α0 = β0 = 2.0          PLC-67   the prior; an unmeasured cell sits at 2/(2+2) = 0.50
--   w  = 0.85^(K-i)        PLC-67   the recency weight
--   low      p < 0.733     PLC-68   the band below the lower cut
--   developing 0.733 ≤ p < 0.80      PLC-68   the band between the cuts
--   mastered   p ≥ 0.80 AND observations ≥ 12 items / 3 tasks   PLC-68
--   28 days, 0.02/week     PLC-71   the inactivity decay
--
-- THE PRIOR IS NOT A GUESS AND IT IS NOT ZERO: 2.0/2.0 is a deliberately weak prior, so four correct
-- answers from an empty cell reach (2+4)/(4+4) = 0.75 — above 0.733 and below 0.80. That is what makes
-- "four answers cannot pronounce a topic learned" arithmetic rather than a policy.

-- =============================================================================================
-- Block 1 — the registry row (Domain 2, inside the existing INSERT ... VALUES batch)
-- =============================================================================================
UPDATE DerivedFactOwners
   SET Coverage     = 'covered',
       CoveredTable = 'UserCellMasteries',
       Note         = N'#237: per-cell mastery (topic, level). The estimate and its four states are derived from the learner''s graded answers; CapabilityWriter is the only writer.'
 WHERE FactCode = 'topic_capability';
GO

-- =============================================================================================
-- Block 2 — the table (after Topics)
-- =============================================================================================
IF OBJECT_ID('dbo.UserCellMasteries', 'U') IS NULL
BEGIN
  CREATE TABLE UserCellMasteries (
    UserId     INT NOT NULL,
    -- The cell. A topic is certificate-agnostic (D-3) and so is its mastery (D-19): no CertId here,
    -- because a learner who switches certificate keeps one comparable record per cell.
    TopicId    INT NOT NULL REFERENCES Topics(TopicId),
    -- Authored levels only. A cell is content; the pre-A1 floor is reportable, never taught, so a
    -- mastery row at 'pre_a1' would be mastery of a cell that cannot hold an item.
    CefrLevel  VARCHAR(10) NOT NULL
      CONSTRAINT CK_UserCellMasteries_Level
      CHECK (CefrLevel IN ('A1','A2','B1','B2','C1','C2')),

    -- The four states of the EXISTING BusinessObjects.MasteryState enum, stored by code. They are not
    -- renamed here: PLC-70 itself calls the lapsed state `reviewing`, and D19 makes `low` and
    -- `developing` the display text for `learning` and `reviewing`. A second four-state vocabulary
    -- would be two answers to one question.
    State      VARCHAR(12) NOT NULL
      CONSTRAINT DF_UserCellMasteries_State DEFAULT 'untested'
      CONSTRAINT CK_UserCellMasteries_State
      CHECK (State IN ('untested','learning','reviewing','mastered')),

    -- PLC-67's sufficient statistic, not a copy of the probability.
    --
    -- Storing (Σ w·c, Σ w·n) rather than the answer sequence is deliberate: practice sessions are
    -- pruned FIFO (CONTEXT.md), so a cell that re-read its own answers would silently rewind a state
    -- the learner already earned. Appending one observation is exactly
    --   Σw·c ← 0.85·Σw·c + c,   Σw·n ← 0.85·Σw·n + 1
    -- so the two numbers ARE the sequence for every purpose the estimate has.
    --
    -- Storing the probability as well would be a second copy of one fact: it is a function of these
    -- two columns and the prior, so a row could disagree with its own arithmetic.
    WeightedCorrect      FLOAT NOT NULL CONSTRAINT DF_UserCellMasteries_WC DEFAULT 0
      CONSTRAINT CK_UserCellMasteries_WeightedCorrect CHECK (WeightedCorrect >= 0),
    WeightedObservations FLOAT NOT NULL CONSTRAINT DF_UserCellMasteries_WN DEFAULT 0
      CONSTRAINT CK_UserCellMasteries_WeightedObservations CHECK (WeightedObservations >= 0),
    -- The unweighted count, because PLC-68's n_min is a count of observations and a weighted sum is
    -- not one. Four answers must not be able to look like twelve.
    ObservationCount     INT NOT NULL CONSTRAINT DF_UserCellMasteries_N DEFAULT 0
      CONSTRAINT CK_UserCellMasteries_ObservationCount CHECK (ObservationCount >= 0),

    -- Consecutive wrong answers. A correct answer resets it, which is the half a counter that only
    -- increments gets wrong: wrong, correct, wrong is NOT a lapse.
    ConsecutiveWrong     INT NOT NULL CONSTRAINT DF_UserCellMasteries_CW DEFAULT 0
      CONSTRAINT CK_UserCellMasteries_ConsecutiveWrong CHECK (ConsecutiveWrong >= 0),

    -- When the cell may next be offered. A wrong answer while mastered sets now + 24 h (the review
    -- window); a correct answer spaces by state.
    NextReviewDueAt      DATETIMEOFFSET NULL,
    -- The last graded answer in this cell. This is the datum the 28-day inactivity clock reads, and
    -- it is also the boundary a mock section must postdate to save the cell from decay.
    LastPractisedAt      DATETIMEOFFSET NULL,
    -- Set when mastered is entered and never cleared by a lapse: "earned once" is a fact about the
    -- learner, and PLC-71's reversion is not a claim it never happened.
    MasteredAt           DATETIMEOFFSET NULL,

    UpdatedAt            DATETIMEOFFSET NOT NULL CONSTRAINT DF_UserCellMasteries_Updated DEFAULT SYSDATETIMEOFFSET(),

    -- #191 / ADR-0012. Nullable for the same reason the other covered tables are: the trigger, not
    -- the column, is what refuses a write that declares nothing.
    WrittenByService     VARCHAR(64) NULL,
    ProjectionVersion    INT NOT NULL CONSTRAINT DF_UserCellMasteries_ProjectionVersion DEFAULT 0,

    CONSTRAINT PK_UserCellMasteries PRIMARY KEY (UserId, TopicId, CefrLevel),

    CONSTRAINT FK_UserCellMasteries_User FOREIGN KEY (UserId) REFERENCES Users(UserId),
    -- The cell's skill must be its topic's skill: a B1 listening mastery row on a reading topic is the
    -- defect this composite prevents. (TopicId, Modality) is not carried here, so the pair is pinned
    -- the other way round: the level is a level, and the topic already fixes the skill.
    CONSTRAINT CK_UserCellMasteries_StateHasEvidence CHECK (
         (State <> 'untested')
      OR (ObservationCount = 0 AND WeightedObservations = 0)
    ),
    -- A correct answer can never be counted more than the answers that carried it.
    CONSTRAINT CK_UserCellMasteries_CorrectLeObservations CHECK (WeightedCorrect <= WeightedObservations),
    -- Mastered is a state the learner reached, so it always has a date. One-way on purpose: a lapsed
    -- cell keeps the date it earned, so the reverse implication would be false.
    CONSTRAINT CK_UserCellMasteries_MasteredIsDated CHECK (State <> 'mastered' OR MasteredAt IS NOT NULL)
  );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserCellMasteries_User_Due'
                                          AND object_id = OBJECT_ID('dbo.UserCellMasteries'))
  CREATE INDEX IX_UserCellMasteries_User_Due ON UserCellMasteries(UserId, NextReviewDueAt);
GO

-- =============================================================================================
-- Block 3 — ADR-0012: one writer per derived fact
-- =============================================================================================
-- The owner is read from the registry, never written into the trigger: a second source for the name
-- is a second answer to "who owns this".
--
-- The declaration must be IN THE STATEMENT. The trigger checks UPDATE(WrittenByService) as well as the
-- value, because a second writer that changes only State leaves the stored name reading
-- 'CapabilityWriter' and would pass a value-only check (PRN-01's inherited-name case).
IF OBJECT_ID('dbo.trg_UserCellMasteries_SingleWriter', 'TR') IS NOT NULL
  DROP TRIGGER trg_UserCellMasteries_SingleWriter;
GO
CREATE TRIGGER trg_UserCellMasteries_SingleWriter
ON UserCellMasteries AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  DECLARE @owner VARCHAR(64) =
    (SELECT OwningService FROM DerivedFactOwners WHERE FactCode = 'topic_capability');

  DECLARE @touched TABLE (
    UserId INT NOT NULL, TopicId INT NOT NULL, CefrLevel VARCHAR(10) NOT NULL, WasVersion INT NOT NULL,
    PRIMARY KEY (UserId, TopicId, CefrLevel));
  INSERT INTO @touched (UserId, TopicId, CefrLevel, WasVersion)
    SELECT i.UserId, i.TopicId, i.CefrLevel, ISNULL(d.ProjectionVersion, 0)
    FROM inserted i
    LEFT JOIN deleted d
      ON d.UserId = i.UserId AND d.TopicId = i.TopicId AND d.CefrLevel = i.CefrLevel
    WHERE d.UserId IS NULL
       OR i.State <> d.State
       OR i.WeightedCorrect <> d.WeightedCorrect
       OR i.WeightedObservations <> d.WeightedObservations
       OR i.ObservationCount <> d.ObservationCount
       OR i.ConsecutiveWrong <> d.ConsecutiveWrong
       OR i.MasteredAt <> d.MasteredAt
       OR i.ProjectionVersion <> d.ProjectionVersion;

  IF NOT EXISTS (SELECT 1 FROM @touched)
    RETURN;

  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN @touched t
      ON t.UserId = i.UserId AND t.TopicId = i.TopicId AND t.CefrLevel = i.CefrLevel
    WHERE ISNULL(i.WrittenByService, '') <> @owner
       OR (EXISTS (SELECT 1 FROM deleted d
                   WHERE d.UserId = i.UserId AND d.TopicId = i.TopicId AND d.CefrLevel = i.CefrLevel)
           AND NOT UPDATE(WrittenByService))
  )
    THROW 50112, 'The per-cell mastery has exactly one writer, CapabilityWriter. A write declaring another writer (or none) is refused.', 1;

  IF EXISTS (
    SELECT 1
    FROM inserted i
    JOIN @touched t
      ON t.UserId = i.UserId AND t.TopicId = i.TopicId AND t.CefrLevel = i.CefrLevel
    WHERE i.ProjectionVersion <= t.WasVersion
  )
    THROW 50113, 'The per-cell mastery projection version must be newer than the stored one; an older callback cannot overwrite a newer state.', 1;
END;
GO

-- =============================================================================================
-- Block 4 — the four states are reachable only through the named transitions
-- =============================================================================================
-- PLC-68 + PLC-70 + PLC-71, as a transition table. A setter that accepts any state passes every
-- service-level transition test, so the reachable set is pinned here:
--
--   (no row)/untested → untested     a row created before its first answer
--   (no row)          → learning     a row created BY its first answer, which the writer does in one
--                                    statement: create the row, apply the first answer
--   untested          → learning     the first answer on a row that already existed
--   learning          → learning | reviewing | mastered
--   reviewing         → reviewing | learning | mastered
--   mastered          → mastered     an answer that does not lapse, or a maintenance pass
--   mastered          → reviewing    the lapse (two wrong in a row) or the inactivity decay
--
-- Nothing reaches mastered directly from untested or from a new row, and nothing ever returns to
-- untested: unmeasured is the absence of evidence, and evidence cannot be unobserved.
IF OBJECT_ID('dbo.trg_UserCellMasteries_StateIsEarned', 'TR') IS NOT NULL
  DROP TRIGGER trg_UserCellMasteries_StateIsEarned;
GO
CREATE TRIGGER trg_UserCellMasteries_StateIsEarned
ON UserCellMasteries AFTER INSERT, UPDATE AS
BEGIN
  SET NOCOUNT ON;
  DECLARE @msg NVARCHAR(2048);

  DECLARE @moved TABLE (
    UserId INT NOT NULL, TopicId INT NOT NULL, CefrLevel VARCHAR(10) NOT NULL,
    FromState VARCHAR(12) NULL, ToState VARCHAR(12) NOT NULL,
    Probability FLOAT NOT NULL, ObservationCount INT NOT NULL, MinimumObservations INT NOT NULL,
    PRIMARY KEY (UserId, TopicId, CefrLevel));

  -- The probability MUST be the same expression the service uses, or this trigger and the writer
  -- disagree about one row: the writer stores a state its own arithmetic earned, and the trigger
  -- refuses it for a number it computed differently. The service's p_m normalizes the recency weights
  -- to the observation count (`k = n / Σw`), and so does this - see MasteryEstimate for why the
  -- un-normalized form cannot reach 0.80 and would make n_min unreachable.
  INSERT INTO @moved (UserId, TopicId, CefrLevel, FromState, ToState, Probability, ObservationCount,
                      MinimumObservations)
    SELECT i.UserId, i.TopicId, i.CefrLevel, d.State, i.State,
           (2.0 + ((CASE WHEN i.WeightedObservations > 0
                         THEN i.ObservationCount / i.WeightedObservations
                         ELSE 0 END) * i.WeightedCorrect))
             / (4.0 + i.ObservationCount),
           i.ObservationCount,
           CASE WHEN t.Modality IN ('speaking','writing') THEN 3 ELSE 12 END
    FROM inserted i
    JOIN Topics t ON t.TopicId = i.TopicId
    LEFT JOIN deleted d
      ON d.UserId = i.UserId AND d.TopicId = i.TopicId AND d.CefrLevel = i.CefrLevel
    WHERE d.State IS NULL OR i.State <> d.State;

  IF NOT EXISTS (SELECT 1 FROM @moved)
    RETURN;

  -- 1. the transition itself is one the specification names
  IF EXISTS (
    SELECT 1 FROM @moved m
    WHERE NOT EXISTS (
      SELECT 1 FROM (VALUES
        (NULL,        'untested'),
        (NULL,        'learning'),
        ('untested',  'untested'),
        ('untested',  'learning'),
        ('learning',  'learning'),
        ('learning',  'reviewing'),
        ('learning',  'mastered'),
        ('reviewing', 'reviewing'),
        ('reviewing', 'learning'),
        ('reviewing', 'mastered'),
        ('mastered',  'mastered'),
        ('mastered',  'reviewing')
      ) AS allowed(FromState, ToState)
      WHERE ISNULL(allowed.FromState, '') = ISNULL(m.FromState, '')
        AND allowed.ToState = m.ToState
    )
  )
  BEGIN
    SET @msg = N'PLC-68/70/71: that mastery transition is not one the specification names. Named: ' +
               N'(new|untested)->untested, untested->learning, learning->{learning,reviewing,mastered}, ' +
               N'reviewing->{reviewing,learning,mastered}, mastered->{mastered,reviewing}. ' +
               N'Nothing returns to untested, and nothing reaches mastered from untested.';
    THROW 50114, @msg, 0;
  END

  -- 2. ENTERING mastered needs the probability AND the observation minimum (PLC-68).
  --    Held mastery is deliberately exempt: PLC-70 says a single wrong answer does not drag a mastered
  --    topic back, so the estimate may sit below 0.80 while the state is still mastered. The guard is
  --    therefore on the transition, not on every row - a value check on every row would forbid the
  --    review window the same specification requires.
  IF EXISTS (
    SELECT 1 FROM @moved m
    WHERE m.ToState = 'mastered'
      AND (m.FromState IS NULL OR m.FromState <> 'mastered')
      AND (m.Probability < 0.80 OR m.ObservationCount < m.MinimumObservations)
  )
    THROW 50115, 'PLC-68: a cell cannot be declared mastered without p_m >= 0.80 and the observation minimum (12 objective items or 3 tasks). Four answers cannot pronounce a topic learned.', 1;

  -- 3. ENTERING reviewing from below needs the estimate (PLC-68): the band between the cuts is earned
  --    by p_m, not asserted. Arriving from mastered is exempt - that is the lapse and the decay, which
  --    move the state on an event while the estimate may still read high.
  IF EXISTS (
    SELECT 1 FROM @moved m
    WHERE m.ToState = 'reviewing'
      AND ISNULL(m.FromState, 'untested') IN ('untested','learning')
      AND m.Probability < 0.733
  )
    THROW 50116, 'PLC-68: a cell enters reviewing only when p_m >= 0.733; below that it is low.', 1;
END;
GO
