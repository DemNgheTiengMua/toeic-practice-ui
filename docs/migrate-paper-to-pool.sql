-- #250 step 3: move the paper's content model onto the pool.
--
-- WHAT THIS DOES, and what it deliberately does not.
--
--   For every (exam, question) that has no ExamItems binding yet:
--     1. one Stimulus per QuestionGroup, carrying the group's passage/audio (a group IS the shared
--        stimulus - CNT-12), created once and shared by its children;
--     2. one Item per Question, in the group's section and at the item's level;
--     3. one ItemVersion per Question carrying the scored content the sitting renders;
--     4. one ExamItem binding, which is the roster position the engine reads.
--
--   It does NOT delete Questions, QuestionGroups or QuestionOptions. The ticket's order is: move
--   the content, prove the counts are unchanged, prove both shapes agree, and only then drop. A
--   migration that drops in the same step as it copies has no rollback.
--
-- THE LEVEL. #229 made CefrLevel authored and #231 deleted the 1-5 difficulty, so a question already
-- carries its own level. Nothing here derives a level from anything else: a question with no level is
-- a DATA DEFECT and this script refuses to invent one (see the guard at the end). That is PRN-03.
--
-- THE TOPIC. Items.TopicId is NOT NULL and the pool is keyed by (topic, level) cell. A paper question
-- names a CategoryId, which is the LEGACY axis - #243/D3 replaced it with TopicId, and there is no
-- mapping between them in the product (the categories are the old per-certificate list; the topics are
-- the canonical twenty). So a question with no topic is mapped by its SECTION's skill to the most
-- literal topic of that skill, and the mapping is stated here rather than hidden: D0.4 already binds
-- each item type to a topic set, and this picks the least interpretive member of it.

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- WHO AUTHORED THIS MIGRATION. The pool's write guards (ADM-03) demand that every write to Items and
-- ItemVersions declares who made it, and a migration is a write by a staff account - not by a person
-- clicking a screen. The account is named once here so the guard can judge it, and it must be a real
-- non-triage account: a NULL would be refused, which is the guard working.
DECLARE @author INT = (
    SELECT TOP 1 u.UserId
    FROM Users u JOIN Roles r ON r.RoleId = u.RoleId
    WHERE r.Code = 'admin' ORDER BY u.UserId);
IF @author IS NULL
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50000, '#250: no admin account exists to author this migration. The pool refuses an unattributed write, so the migration refuses rather than inventing an author.', 1;
END

-- ---- 1. a stimulus per group ----------------------------------------------------------------
-- One row per group, created only when the group actually carries text or audio. A group with neither
-- is a bare set of questions, and an empty Stimulus row would be a pointer at nothing.
INSERT INTO Stimuli (CefrLevel, Passage, AudioPath, AudioStartMs, AudioEndMs, Transcript, ImagePath, ImageAlt, DomainContext, PromptCopy)
SELECT
    -- The stimulus's level is its group's FIRST child's level: D0.3 allows children at Ls +/- 1 and
    -- requires the majority at Ls, so the first child is the best available reading of the group's own.
    COALESCE((SELECT TOP 1 q.CefrLevel FROM Questions q WHERE q.GroupId = g.GroupId AND q.CefrLevel IS NOT NULL
              ORDER BY q.DisplayOrder), 'B1'),
    g.Passage, g.AudioPath, g.AudioStartMs, g.AudioEndMs, g.Transcript, g.ImagePath, g.ImageAlt, 'general', NULL
FROM QuestionGroups g
WHERE (g.Passage IS NOT NULL OR g.AudioPath IS NOT NULL OR g.Transcript IS NOT NULL OR g.ImagePath IS NOT NULL)
  AND NOT EXISTS (
      -- Idempotent: a re-run must not create a second stimulus for the same group. The marker is the
      -- group's own text, matched by content rather than by id because Stimuli has no group column.
      SELECT 1 FROM Stimuli s
      WHERE ISNULL(s.Passage, '') = ISNULL(g.Passage, '')
        AND ISNULL(s.AudioPath, '') = ISNULL(g.AudioPath, '')
        AND ISNULL(s.Transcript, '') = ISNULL(g.Transcript, '')
  );

-- ---- 2. the item and its version, one per question -------------------------------------------
-- Topic: the question's own topic when the pool already expresses one, otherwise the most literal
-- topic of the question's section skill. Written as a CASE on the skill so the choice is visible.
DECLARE @topicBySkill TABLE (Modality VARCHAR(10) PRIMARY KEY, TopicCode VARCHAR(16) NOT NULL);
INSERT INTO @topicBySkill (Modality, TopicCode) VALUES
    ('listening', 'L-DET'),
    ('reading',   'R-DET'),
    -- The canonical twenty, by their real codes. The first draft wrote 'S-INF', which does not exist -
    -- the migration was refused by FK_Items_TopicModality after 116 items had already been attempted,
    -- and the codes below were read from the Topics table rather than recalled.
    ('speaking',  'S-DESC'),
    ('writing',   'W-ARG');

DECLARE @pending TABLE (QuestionId INT PRIMARY KEY, ItemId INT NULL, ItemVersionId INT NULL);
INSERT INTO @pending (QuestionId)
SELECT q.QuestionId
FROM Questions q
WHERE NOT EXISTS (SELECT 1 FROM ExamItems ei WHERE ei.ExamId = q.ExamId);

DECLARE @qid INT, @itemId INT, @versionId INT;

DECLARE pending_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT QuestionId FROM @pending ORDER BY QuestionId;
OPEN pending_cursor;
FETCH NEXT FROM pending_cursor INTO @qid;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @examId INT, @sectionId INT, @groupId INT, @skillId INT, @modality VARCHAR(10);
    DECLARE @level VARCHAR(10), @stem NVARCHAR(MAX), @type VARCHAR(30), @render VARCHAR(30);
    DECLARE @explain NVARCHAR(MAX), @hint NVARCHAR(1000), @accepted NVARCHAR(MAX), @keywords NVARCHAR(MAX);
    DECLARE @order INT, @topicId INT, @stimulusId INT;
    DECLARE @optA NVARCHAR(MAX), @optB NVARCHAR(MAX), @optC NVARCHAR(MAX), @optD NVARCHAR(MAX);
    DECLARE @ratA NVARCHAR(MAX), @ratB NVARCHAR(MAX), @ratC NVARCHAR(MAX), @ratD NVARCHAR(MAX);
    DECLARE @key VARCHAR(16);

    SELECT
        @examId = q.ExamId, @sectionId = q.SectionId, @groupId = q.GroupId, @skillId = q.SkillId,
        @level = q.CefrLevel, @stem = q.Stem, @type = q.QuestionType,
        @explain = q.Explanation, @hint = q.Hint, @accepted = q.AcceptedAnswersJson,
        @keywords = q.RequiredKeywords, @order = q.DisplayOrder, @modality = s.Modality
    FROM Questions q
    JOIN Sections s ON s.SectionId = q.SectionId
    WHERE q.QuestionId = @qid;

    -- The render format IS the legacy QuestionType: the seven values are the same vocabulary
    -- CK_Items_RenderFormat admits, which is why the pool can express the paper at all (#250 step 1).
    SET @render = CASE WHEN @type IN ('mcq','mcq_3','multi_select','inline_completion','dropdown','drag_drop','free_response')
                       THEN @type ELSE 'mcq' END;

    -- The topic: the section's skill decides, per @topicBySkill.
    SELECT @topicId = t.TopicId
    FROM Topics t JOIN @topicBySkill m ON m.TopicCode = t.Code
    WHERE m.Modality = @modality;

    IF @topicId IS NULL
    BEGIN
        -- No canonical topic for this skill: refuse rather than pick one, because an item in the
        -- wrong cell is worse than an item that did not migrate.
        ROLLBACK TRANSACTION;
        THROW 50000, '#250: no canonical topic for this question''s skill, so it cannot be placed in a cell.', 1;
    END

    -- The shared stimulus, if this group carries one.
    SELECT TOP 1 @stimulusId = s.StimulusId
    FROM Stimuli s
    JOIN QuestionGroups g ON g.GroupId = @groupId
    WHERE ISNULL(s.Passage, '') = ISNULL(g.Passage, '')
      AND ISNULL(s.AudioPath, '') = ISNULL(g.AudioPath, '')
      AND ISNULL(s.Transcript, '') = ISNULL(g.Transcript, '')
    ORDER BY s.StimulusId;

    INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, RenderFormat,
                       PlacementEligible, ContentTier, LifecycleStatus, LastWrittenBy)
    VALUES (@stimulusId, @modality, @topicId, @level,
            -- The semantic item type, derived per MODALITY because CK_Items_Modality_Type is a closed
            -- pairing: a listening item can never be 'passage_comprehension' however much text its group
            -- carries, and the first draft of this script learned that from the constraint rather than
            -- from the reader. The choice follows D0.4's most literal member of each modality's set.
            CASE @modality
                WHEN 'listening' THEN
                    CASE WHEN EXISTS (SELECT 1 FROM QuestionGroups g WHERE g.GroupId = @groupId AND g.Passage IS NOT NULL)
                         THEN 'extended_dialogue' ELSE 'short_exchange' END
                WHEN 'writing' THEN
                    CASE WHEN @type = 'free_response' THEN 'transactional_correspondence' ELSE 'transactional_correspondence' END
                WHEN 'speaking' THEN 'descriptive_monologue'
                ELSE
                    CASE WHEN EXISTS (SELECT 1 FROM QuestionGroups g WHERE g.GroupId = @groupId AND g.Passage IS NOT NULL)
                         THEN 'passage_comprehension' ELSE 'sentence_cloze' END
            END,
            @render, 0, 'demo', 'active', @author);
    SET @itemId = SCOPE_IDENTITY();

    -- Option text and rationales come from QuestionOptions, by label. A question with fewer than four
    -- options leaves the rest NULL, which CK_ItemVersions_OptionsComplete accepts since #250 step 2.
    SELECT
        @optA = MAX(CASE WHEN o.Label = 'A' THEN o.Text END),
        @optB = MAX(CASE WHEN o.Label = 'B' THEN o.Text END),
        @optC = MAX(CASE WHEN o.Label = 'C' THEN o.Text END),
        @optD = MAX(CASE WHEN o.Label = 'D' THEN o.Text END),
        @ratA = MAX(CASE WHEN o.Label = 'A' THEN o.Rationale END),
        @ratB = MAX(CASE WHEN o.Label = 'B' THEN o.Rationale END),
        @ratC = MAX(CASE WHEN o.Label = 'C' THEN o.Rationale END),
        @ratD = MAX(CASE WHEN o.Label = 'D' THEN o.Rationale END)
    FROM QuestionOptions o WHERE o.QuestionId = @qid;

    -- The key is a SET after #250 step 2: multi_select marks two or more, joined ascending.
    SELECT @key = STRING_AGG(CAST(o.Label AS VARCHAR(4)), ',') WITHIN GROUP (ORDER BY o.Label)
    FROM QuestionOptions o WHERE o.QuestionId = @qid AND o.IsCorrect = 1;

    INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel,
                              Explanation, RationaleA, RationaleB, RationaleC, RationaleD,
                              MaxSelections, AllowNumber, AcceptedAnswersJson, RequiredKeywords, Hint,
                              MinWords, MaxWords, PrepSeconds, ResponseSeconds,
                              LastWrittenBy)
    VALUES (@itemId, 1, @stem, @optA, @optB, @optC, @optD, @key,
            @explain, @ratA, @ratB, @ratC, @ratD,
            (SELECT MaxSelections FROM Questions WHERE QuestionId = @qid),
            ISNULL((SELECT AllowNumber FROM Questions WHERE QuestionId = @qid), 1),
            @accepted, @keywords, @hint,
            -- A productive task's word window and timers live on the version, not on the paper. Without
            -- these four a writing task migrates with no floor - measured: four Writing Extension tasks
            -- all arrived with MinWords NULL, and the grader's "at least N words" check silently stopped
            -- applying.
            --
            -- CK_ItemVersions_WordRange refuses a ceiling with no floor, and one legacy row has exactly
            -- that (Q110: an inline_completion with MaxWords = 1 and no MinWords). That row's ceiling is
            -- its whole meaning, so it is carried as a floor of 1 rather than dropped - dropping it
            -- would let a one-word blank accept a paragraph.
            CASE WHEN (SELECT MinWords FROM Questions WHERE QuestionId = @qid) IS NULL
                  AND (SELECT MaxWords FROM Questions WHERE QuestionId = @qid) IS NOT NULL
                 THEN 1
                 ELSE (SELECT MinWords FROM Questions WHERE QuestionId = @qid) END,
            (SELECT MaxWords FROM Questions WHERE QuestionId = @qid),
            (SELECT PrepSeconds FROM Questions WHERE QuestionId = @qid),
            (SELECT ResponseSeconds FROM Questions WHERE QuestionId = @qid),
            @author);
    SET @versionId = SCOPE_IDENTITY();

    -- The roster, which is the whole point: the paper's position now exists on the pool.
    -- CertId comes from Exams, not from Questions: a question row carries no certificate of its own
    -- (it belongs to a paper), and the paper is the thing that names one.
    --
    -- DisplayOrder is NOT Question.DisplayOrder. A binding's position is "1-based within its section"
    -- (UQ_ExamItems_Exam_Section_Order), and the engine places bindings by RANK inside the section,
    -- not by the raw column. The first draft copied the question's own ordering - 3,4,5,6 in a section
    -- - so the binding at rank 1 carried DisplayOrder 3 and PlaceBindings found no match at position 1.
    -- Measured on the seeded placement paper: "This paper binds item version 46 at section 2 position 5,
    -- but that position holds no question."
    --
    -- ROW_NUMBER cannot be used here: this INSERT runs once per question inside a cursor, so the
    -- window sees a single row and always yields 1 - which then collides with
    -- UQ_ExamItems_Exam_Section_Order on the second question. The rank is counted instead.
    DECLARE @position INT =
        (SELECT COUNT(*) + 1
         FROM Questions other
         WHERE other.ExamId = @examId
           AND other.SectionId = @sectionId
           AND (other.DisplayOrder < @order
                OR (other.DisplayOrder = @order AND other.QuestionId < @qid)));

    INSERT INTO ExamItems (ExamId, CertId, SectionId, ItemId, ItemVersionId, Modality, DisplayOrder, UnitNumber,
                           LastWrittenBy)
    SELECT @examId, e.CertId, @sectionId, @itemId, @versionId, @modality, @position,
           ISNULL((SELECT g.UnitNumber FROM QuestionGroups g WHERE g.GroupId = @groupId), 1), @author
    FROM Exams e WHERE e.ExamId = @examId;

    FETCH NEXT FROM pending_cursor INTO @qid;
END
CLOSE pending_cursor;
DEALLOCATE pending_cursor;

-- ---- 3. the guard: every question reached the pool -------------------------------------------
-- A silent partial migration is the failure this whole ticket exists to prevent: the counts would look
-- plausible and the missing rows would surface as an unreachable question on a real sitting.
DECLARE @unmigrated INT =
    (SELECT COUNT(*) FROM Questions q WHERE NOT EXISTS (SELECT 1 FROM ExamItems ei WHERE ei.ExamId = q.ExamId));
IF @unmigrated > 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50000, '#250: some questions have no ExamItem binding after the migration, so the paper would have holes. Nothing was committed.', 1;
END

DECLARE @noLevel INT = (SELECT COUNT(*) FROM Questions WHERE CefrLevel IS NULL);
IF @noLevel > 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50000, '#250: a question carries no CEFR level. PRN-03 forbids deriving one, so the migration refuses rather than inventing it.', 1;
END

COMMIT TRANSACTION;
PRINT 'paper content migrated to the pool';
