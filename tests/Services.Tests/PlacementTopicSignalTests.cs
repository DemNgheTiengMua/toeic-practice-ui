using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>
/// #219 / D5: the placement's topic and criterion signals — what the sitting SAW, as a hint for the
/// first week's ordering and <b>explicitly not mastery</b>.
///
/// <b>Each test names the input that would pass a weaker one.</b> The three defects this ticket is
/// most likely to ship are: a hint counted over unanswered rows (every topic reads "seen, none
/// correct"), a band copied from the skill's measured level instead of the item actually sat, and a
/// hint that leaks into mastery. Each is driven with the fixture that would slip through it.
///
/// Isolation: this class owns a database created for it and dropped afterwards — seeding the shared
/// fixture catalog has broken the full suite three times in this rebuild.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementTopicSignalTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EE219_{Guid.NewGuid():N}"[..24];
    private string _connectionString = null!;

    public PlacementTopicSignalTests(DatabaseFixture database) => _database = database;

    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private Task SqlAsync(string batch) =>
        _database.SqlCmdFileToDatabaseAsync(_dbName, WriteTemp(batch));

    private static string WriteTemp(string batch)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ee219-{Guid.NewGuid():N}.sql");
        File.WriteAllText(path, batch);
        return path;
    }

    // ---------------------------------------------------------------------------------------------
    // fixtures
    // ---------------------------------------------------------------------------------------------

    private sealed record Fixture(
        int CertId,
        int ExamId,
        int ReadingSkillId,
        int WritingSkillId,
        int ListeningSkillId,
        IReadOnlyDictionary<string, int> TopicIds,
        IReadOnlyList<BoundQuestion> ReadingQuestions,
        IReadOnlyList<BoundQuestion> WritingQuestions,
        IReadOnlyDictionary<string, int> CriterionIds);

    /// <summary>One paper position, with the pool item and version the answer will pin.</summary>
    private sealed record BoundQuestion(
        int QuestionId, int ItemId, int ItemVersionId, string TopicCode, string Band, char KeyLabel);

    /// <summary>
    /// A certificate with a reading skill (three paper positions across two topics at two bands, so
    /// "which band did this topic sit at" is detectable), a writing skill with two free-response tasks
    /// and three rubric criteria, and a placement exam binding every position to a real pool item.
    /// <para>
    /// The pool binding is what makes a topic nameable at all: an answer with no <c>ItemId</c> has no
    /// topic, and the writer records nothing rather than inventing one.
    /// </para>
    /// </summary>
    private async Task<Fixture> SeedAsync(string code)
    {
        await SqlAsync($$"""
            INSERT INTO Topics (Modality, Code, Name) VALUES
              ('reading', 'R-INF', N'Bridging inference'),
              ('reading', 'R-ATT', N'Authorial stance'),
              ('writing', 'W-ARG', N'Argument');
            DECLARE @rInf INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-INF');
            DECLARE @rAtt INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-ATT');
            DECLARE @wArg INT = (SELECT TopicId FROM Topics WHERE Modality = 'writing' AND Code = 'W-ARG');

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{{code}}', N'Cert {{code}}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
              VALUES (@cert, 'READ', N'Reading', 'reading', 1, 0, 100, 1);
            DECLARE @read INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
              VALUES (@cert, 'WRIT', N'Writing', 'writing', 2, 0, 100, 1);
            DECLARE @writ INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
              VALUES (@cert, 'LIST', N'Listening', 'listening', 3, 0, 100, 1);
            DECLARE @list INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder)
              VALUES (@read, @cert, 'reading', 'S1', N'Reading', 3, 1);
            DECLARE @secR INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder)
              VALUES (@writ, @cert, 'writing', 'S2', N'Writing', 2, 2);
            DECLARE @secW INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
              VALUES (@cert, N'Placement', 'published', 30, 'placement', 90);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A2', N'Elementary', 0, 200, 1),
              (@cert, 'B1', N'Intermediate', 201, 500, 2),
              (@cert, 'B2', N'Upper intermediate', 501, 900, 3);
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              VALUES (@cert, @read, NULL, 0, 0), (@cert, @read, NULL, 3, 300),
                     (@cert, @writ, NULL, 0, 0), (@cert, @writ, NULL, 2, 300),
                     (@cert, @list, NULL, 0, 0), (@cert, @list, NULL, 1, 300);

            -- A cut-score table, so the per-skill level is READ from the certificate's own scale
            -- rather than falling back to the attempt's band. Reading lands at B1 even though the
            -- attempt's total lands at B2, which is the "one band cannot say four things" case.
            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
              VALUES (@cert, 1, 0, N'#219 fixture', '2026-01-01');
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled) VALUES
              (@cert, 1, @read, 'A2', 'cut', 0, 99),
              (@cert, 1, @read, 'B1', 'cut', 100, 400),
              (@cert, 1, @read, 'B2', 'cut', 401, 700),
              (@cert, 1, @writ, 'B1', 'cut', 0, 700);

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secR, 1);
            DECLARE @grpR INT = SCOPE_IDENTITY();
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secW, 2);
            DECLARE @grpW INT = SCOPE_IDENTITY();

            -- Reading: position 1 and 2 are R-INF at B1; position 3 is R-ATT at B2. Two bands, two
            -- topics, so "the band this topic sat at" cannot be read off the skill.
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
              VALUES (@grpR, @exam, @secR, @read, N'R1', 'B1', 'mcq', 1),
                     (@grpR, @exam, @secR, @read, N'R2', 'B1', 'mcq', 2),
                     (@grpR, @exam, @secR, @read, N'R3', 'B2', 'mcq', 3);
            DECLARE @q1 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 1);
            DECLARE @q2 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 2);
            DECLARE @q3 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 3);

            -- Writing: two tasks, as D6 requires, so a grading failure is representable.
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
              VALUES (@grpW, @exam, @secW, @writ, N'W1', 'B1', 'free_response', 1),
                     (@grpW, @exam, @secW, @writ, N'W2', 'B1', 'free_response', 2);
            DECLARE @q4 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 1 AND SectionId = @secW);
            DECLARE @q5 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 2 AND SectionId = @secW);

            -- Three rubric criteria, 0-6. GRAM is scored lowest BY RATIO below, not by raw score.
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder, RubricDescription)
              VALUES (@cert, @writ, 'GRAM', N'Grammar', 6, 1, 1, N'Descriptor'),
                     (@cert, @writ, 'LEX', N'Lexis', 6, 1, 2, N'Descriptor'),
                     (@cert, @writ, 'COH', N'Cohesion', 6, 1, 3, N'Descriptor');

            -- Pool items: each paper position binds one, so the answer pins a real topic and band.
            INSERT INTO Stimuli (CefrLevel, Passage) VALUES ('B1', N'S'), ('B1', N'S'), ('B2', N'S'), ('B1', N'S'), ('B1', N'S');
            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, RenderFormat) VALUES
              ('reading', @rInf, 'B1', 'passage_comprehension', 1, 'demo', 'active', 'mcq'),
              ('reading', @rInf, 'B1', 'passage_comprehension', 1, 'demo', 'active', 'mcq'),
              ('reading', @rAtt, 'B2', 'passage_comprehension', 1, 'demo', 'active', 'mcq'),
              ('writing', @wArg, 'B1', 'persuasive_essay', 1, 'demo', 'active', 'free_response'),
              ('writing', @wArg, 'B1', 'persuasive_essay', 1, 'demo', 'active', 'free_response');
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
              SELECT i.ItemId, 1, N'Stem', N'A', N'B', N'C', N'D', 'A'
              FROM Items i WHERE i.ItemType = 'passage_comprehension';
            -- the two productive tasks carry no options (CNT-06's other legal shape)
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem)
              SELECT ItemId, 1, N'Write about it.'
              FROM Items WHERE ItemType = 'persuasive_essay';

            -- Drillable stock at B1 for BOTH reading topics. A path drill never draws placement stock
            -- (PlacementEligible = 0), and the hint may only permute topics the block can actually
            -- serve — so without these, a hint naming R-ATT would be dropped and prove nothing.
            INSERT INTO Stimuli (CefrLevel, Passage) VALUES ('B1', N'D'), ('B1', N'D'), ('B1', N'D'), ('B1', N'D');
            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus) VALUES
              ('reading', @rInf, 'B1', 'passage_comprehension', 0, 'demo', 'active'),
              ('reading', @rInf, 'B1', 'passage_comprehension', 0, 'demo', 'active'),
              ('reading', @rAtt, 'B1', 'passage_comprehension', 0, 'demo', 'active'),
              ('reading', @rAtt, 'B1', 'passage_comprehension', 0, 'demo', 'active');
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
              SELECT i.ItemId, 1, N'Drill', N'A', N'B', N'C', N'D', 'A'
              FROM Items i WHERE i.PlacementEligible = 0;

            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
              SELECT @exam, @secR, @cert, v.ItemVersionId, v.ItemId, 'reading', 1
              FROM ItemVersions v JOIN Items i ON i.ItemId = v.ItemId
              WHERE i.TopicId = @rInf AND i.CefrLevel = 'B1' AND i.PlacementEligible = 1 AND v.VersionNumber = 1
              ORDER BY v.ItemVersionId OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;
            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
              SELECT @exam, @secR, @cert, v.ItemVersionId, v.ItemId, 'reading', 2
              FROM ItemVersions v JOIN Items i ON i.ItemId = v.ItemId
              WHERE i.TopicId = @rInf AND i.CefrLevel = 'B1' AND i.PlacementEligible = 1 AND v.VersionNumber = 1
              ORDER BY v.ItemVersionId OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY;
            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
              SELECT @exam, @secR, @cert, v.ItemVersionId, v.ItemId, 'reading', 3
              FROM ItemVersions v JOIN Items i ON i.ItemId = v.ItemId
              WHERE i.TopicId = @rAtt AND i.CefrLevel = 'B2' AND i.PlacementEligible = 1 AND v.VersionNumber = 1;
            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
              SELECT @exam, @secW, @cert, v.ItemVersionId, v.ItemId, 'writing', 1
              FROM ItemVersions v JOIN Items i ON i.ItemId = v.ItemId
              WHERE i.TopicId = @wArg AND i.PlacementEligible = 1 AND v.VersionNumber = 1
              ORDER BY v.ItemVersionId OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;
            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
              SELECT @exam, @secW, @cert, v.ItemVersionId, v.ItemId, 'writing', 2
              FROM ItemVersions v JOIN Items i ON i.ItemId = v.ItemId
              WHERE i.TopicId = @wArg AND i.PlacementEligible = 1 AND v.VersionNumber = 1
              ORDER BY v.ItemVersionId OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY;
            IF (SELECT COUNT(*) FROM ExamItems WHERE ExamId = @exam) <> 5
              THROW 50000, 'fixture: the paper did not bind five positions, so this test would prove nothing', 1;
            """);

        await using var db = Db();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync();
        var skillIds = await db.Skills.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code, s => s.SkillId);
        var topics = await db.Topics
            .Where(t => t.Code == "R-INF" || t.Code == "R-ATT" || t.Code == "W-ARG")
            .ToDictionaryAsync(t => t.Code, t => t.TopicId);

        var bound = await db.Questions
            .Where(q => q.ExamId == examId)
            .OrderBy(q => q.SectionId).ThenBy(q => q.DisplayOrder)
            .Select(q => new { q.QuestionId, q.SectionId, q.DisplayOrder, Skill = q.Section.SkillId })
            .ToListAsync();

        var bindings = await db.ExamItems
            .Where(b => b.ExamId == examId)
            .Join(db.ItemVersions, b => b.ItemVersionId, v => v.ItemVersionId, (b, v) => new { b.SectionId, b.DisplayOrder, b.ItemId, b.ItemVersionId, v.KeyLabel })
            .Join(db.Items, x => x.ItemId, i => i.ItemId, (x, i) => new
            {
                x.SectionId, x.DisplayOrder, x.ItemId, x.ItemVersionId, i.TopicId, Band = i.CefrLevel.Code, Key = x.KeyLabel,
            })
            .ToListAsync();

        var topicsById = topics.ToDictionary(t => t.Value, t => t.Key);
        var reading = new List<BoundQuestion>();
        var writing = new List<BoundQuestion>();
        foreach (var question in bound)
        {
            var binding = bindings.Single(b => b.SectionId == question.SectionId && b.DisplayOrder == question.DisplayOrder);
            var row = new BoundQuestion(
                question.QuestionId, binding.ItemId, binding.ItemVersionId,
                topicsById[binding.TopicId], binding.Band, binding.Key?[0] ?? 'A');
            (question.Skill == skillIds["READ"] ? reading : writing).Add(row);
        }

        var criteria = await db.GradingCriteria
            .Where(c => c.CertId == certId).ToDictionaryAsync(c => c.Code, c => c.CriterionId);

        return new Fixture(
            certId, examId, skillIds["READ"], skillIds["WRIT"], skillIds["LIST"], topics, reading, writing, criteria);
    }

    private async Task<int> SeedLearnerAsync(int certId)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await SqlAsync($$"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, ActiveCertId)
              VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{{email}}', 0x01, {{certId}});
            """);
        await using var db = Db();
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>The option id the sitting projection renders for a pinned version (AttemptEngine.PoolOptionId).</summary>
    private static int OptionId(int itemVersionId, char label) => itemVersionId * 10 + (label - 'A') + 1;

    private async Task AdvanceToSkillAsync(int attemptId, int questionId) =>
        await SqlAsync($$"""
            UPDATE a
            SET a.ActiveSkillId = (SELECT q.SkillId FROM Questions q WHERE q.QuestionId = {{questionId}}),
                a.PhaseDeadlineAt = DATEADD(minute, 30, GETUTCDATE())
            FROM ExamAttempts a
            WHERE a.AttemptId = {{attemptId}};
            """);

    /// <summary>
    /// Sits the whole placement: answers the reading positions (with <paramref name="wrongReadingPositions"/>
    /// deliberately wrong), drafts both writing tasks, submits. Returns the placement and attempt ids.
    /// The writing grading is left pending, which is the state a real sitting is in on submit.
    /// </summary>
    private async Task<(int PlacementId, int AttemptId)> SitAsync(
        int userId, Fixture fixture, params int[] wrongReadingPositions)
    {
        int placementId;
        int attemptId;
        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            var info = await service.StartPlacementTestAsync(userId, fixture.ExamId);
            placementId = info.PlacementId;
            attemptId = info.AttemptId;
        }

        for (var i = 0; i < fixture.ReadingQuestions.Count; i++)
        {
            var question = fixture.ReadingQuestions[i];
            if (i > 0)
            {
                await AdvanceToSkillAsync(attemptId, question.QuestionId);
            }

            // 'A' is the key; 'B' is wrong, so the fixture's wrong answer is genuinely wrong.
            var label = wrongReadingPositions.Contains(i) ? 'B' : question.KeyLabel;
            await using var db = Db();
            var service = new PlacementService(db, new LearnerService(db));
            await service.SaveAnswerAsync(
                userId, placementId, question.QuestionId, OptionId(question.ItemVersionId, label), isFlagged: false);
        }

        await AdvanceToSkillAsync(attemptId, fixture.WritingQuestions[0].QuestionId);
        foreach (var question in fixture.WritingQuestions)
        {
            await using var db = Db();
            var service = new PlacementService(db, new LearnerService(db));
            await service.SaveDraftAsync(userId, placementId, question.QuestionId,
                "A written response that is long enough to grade.");
        }

        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        return (placementId, attemptId);
    }

    /// <summary>Settles both writing tasks' grading with the given criterion scores and finalises.</summary>
    private async Task SettleWritingAsync(
        int attemptId, Fixture fixture, IReadOnlyDictionary<string, decimal> scores,
        string? priorityFix = null)
    {
        var values = string.Join(", ",
            scores.Select(s => $"({fixture.CriterionIds[s.Key]}, {fixture.WritingSkillId}, {s.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)})"));

        await SqlAsync($$"""
            DECLARE @fix NVARCHAR(400) = {{Literal(priorityFix)}};
            UPDATE AiGradings
              SET Status = 'done', CompletedAt = SYSDATETIMEOFFSET(), OverallScaled = 4.5, PriorityFix = @fix
              WHERE FreeResponseId IN (SELECT FreeResponseId FROM FreeResponses WHERE AttemptId = {{attemptId}});
            UPDATE FreeResponses SET Status = 'graded' WHERE AttemptId = {{attemptId}};
            INSERT INTO AiGradingScores (GradingId, CriterionId, SkillId, Score)
              SELECT g.GradingId, v.CriterionId, v.SkillId, v.Score
              FROM AiGradings g
              CROSS JOIN (VALUES {{values}}) v(CriterionId, SkillId, Score)
              WHERE g.FreeResponseId IN (SELECT FreeResponseId FROM FreeResponses WHERE AttemptId = {{attemptId}});
            """);

        await using var db = Db();
        await new AttemptEngine(db).FinalizeAiGradedAttemptAsync(attemptId);
    }

    private static string Literal(string? value) =>
        value is null ? "NULL" : "N'" + value.Replace("'", "''") + "'";

    private async Task<PlacementSkillResult> RowAsync(int placementId, int skillId)
    {
        await using var db = Db();
        return await db.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == skillId);
    }

    /// <summary>
    /// A skill carried over from an earlier sitting records no topic hint: this paper did not see it,
    /// so presenting last month's observation as this sitting's would be the re-stamping defect the
    /// MeasuredAt badge already exists to make visible.
    ///
    /// <b>The input that would pass a weaker test:</b> asserting only "a carried row has no priors"
    /// passes for a writer that never writes any priors at all. The same sitting's <b>measured</b>
    /// skill is asserted to carry one, so the test proves the clearing is selective.
    /// </summary>
    [Fact]
    public async Task A_carried_skill_records_no_topic_hint_while_the_measured_one_does()
    {
        var fixture = await SeedAsync(Unique("T219G"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        // A stored reading state inside the 90-day window, which the next sitting may carry over.
        await SqlAsync($$"""
            INSERT INTO LearnerSkillStates (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source, MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
              VALUES ({{userId}}, {{fixture.CertId}}, {{fixture.ReadingSkillId}}, 'B1', 40, 3.40, 'placement',
                      DATEADD(DAY, -5, SYSDATETIMEOFFSET()), DATEADD(DAY, 85, SYSDATETIMEOFFSET()), 'SkillStateWriter', 1);
            """);

        int placementId;
        int attemptId;
        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            var info = await service.StartPlacementTestAsync(
                userId, fixture.ExamId, PlacementReuseChoice.ReuseValid);
            placementId = info.PlacementId;
            attemptId = info.AttemptId;
        }

        // Answer only the writing half: the reading positions are untouched, so the only reason a
        // reading row exists at all is the carry-over.
        await AdvanceToSkillAsync(attemptId, fixture.WritingQuestions[0].QuestionId);
        foreach (var question in fixture.WritingQuestions)
        {
            await using var db = Db();
            var service = new PlacementService(db, new LearnerService(db));
            await service.SaveDraftAsync(userId, placementId, question.QuestionId,
                "A written response that is long enough to grade.");
        }

        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        var reading = await RowAsync(placementId, fixture.ReadingSkillId);
        Assert.True(reading.ReusedFromPrior, "the reading skill was not carried, so this test proves nothing");
        Assert.Null(reading.TopicPriorsJson);
        Assert.Null(reading.CriterionProfileJson);
    }

    /// <summary>
    /// An answered-but-unjudged answer is not an item seen. A productive answer is stored with
    /// <c>IsCorrect</c> null by design (#253), so a tally that counted every answered row would report
    /// its topic as "seen, none correct" and mark it weak on evidence the item never produced. The
    /// rubric profile is the productive half's evidence; this tally is the objective half's.
    ///
    /// <b>The input that would pass a weaker test:</b> asserting only "the number is not negative" —
    /// or asserting on a topic with no answer at all. The row here is answered (a response text is
    /// stored) and unjudged, which is exactly the row a naive count admits and a judged-only count
    /// rejects; the same sitting's judged topic is asserted to be counted, so the exclusion is
    /// selective rather than a tally that never runs.
    /// </summary>
    [Fact]
    public async Task An_answered_but_unjudged_item_is_not_counted_as_a_topic_seen()
    {
        var fixture = await SeedAsync(Unique("T219H"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        var (placementId, attemptId) = await SitAsync(userId, fixture, wrongReadingPositions: []);

        // A writing position that carries a response text but was never judged — the row shape #253
        // describes. It is bound to the W-ARG item, so a naive tally would name that topic.
        var writing = fixture.WritingQuestions[0];
        await SqlAsync($$"""
            INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, ItemId, ItemVersionId, ResponseText, IsCorrect, IsFlagged)
              SELECT {{attemptId}}, {{writing.QuestionId}}, ExamId, {{writing.ItemId}}, {{writing.ItemVersionId}},
                     N'A written response that was never judged.', NULL, 0
              FROM ExamAttempts WHERE AttemptId = {{attemptId}};
            """);

        // Settle the writing grading so the skill is measured and its row actually reaches the signal
        // path; the appended unjudged answer row is what the tally must still refuse to count.
        await SqlAsync($$"""
            UPDATE AiGradings SET Status = 'done', CompletedAt = SYSDATETIMEOFFSET(), OverallScaled = 4.0
              WHERE FreeResponseId IN (SELECT FreeResponseId FROM FreeResponses WHERE AttemptId = {{attemptId}});
            UPDATE FreeResponses SET Status = 'graded' WHERE AttemptId = {{attemptId}};
            INSERT INTO AiGradingScores (GradingId, CriterionId, SkillId, Score)
              SELECT g.GradingId, {{fixture.CriterionIds["GRAM"]}}, {{fixture.WritingSkillId}}, 4
              FROM AiGradings g
              WHERE g.FreeResponseId IN (SELECT FreeResponseId FROM FreeResponses WHERE AttemptId = {{attemptId}});
            """);

        await using (var db = Db())
        {
            await new AttemptEngine(db).FinalizeAiGradedAttemptAsync(attemptId);
        }

        await using (var db = Db())
        {
            await new PlacementResultWriter(db).WriteAsync(placementId);
        }

        var reading = await RowAsync(placementId, fixture.ReadingSkillId);
        Assert.NotNull(reading.TopicPriorsJson);
        var priors = PlacementTopicPrior.Deserialize(reading.TopicPriorsJson).ToList();
        Assert.Equal(2, priors.Count);

        // the judged reading topics are counted, and each counted at its real size
        Assert.All(priors, p => Assert.InRange(p.ItemsSeen, 1, 3));

        // the writing skill's only answer is the unjudged one, so it saw NOTHING: a tally that
        // admitted unjudged rows would name W-ARG here and call it weak.
        var writingRow = await RowAsync(placementId, fixture.WritingSkillId);
        Assert.Equal(PlacementResultStatus.Measured, writingRow.ResultStatus);
        Assert.Null(writingRow.TopicPriorsJson);
    }

    // ---------------------------------------------------------------------------------------------
    // criterion 1: each touched topic, with seen / correct / band
    // ---------------------------------------------------------------------------------------------
    /// <summary>
    /// Three reading positions across two topics at two bands: R-INF at B1 (one of two wrong) and
    /// R-ATT at B2 (right). Each topic is recorded with its own counts and <b>its own band</b>, and the
    /// weak one carries <c>weak</c>.
    ///
    /// <b>The input that would pass a weaker test:</b>
    /// <list type="bullet">
    /// <item>A writer that recorded the skill's measured level as every topic's band passes "a band
    /// was stored". R-ATT sat at B2 while the skill reports B1, so this one catches it.</item>
    /// <item>A writer that counted the engine's pre-created empty answer rows as items seen would
    /// report 3/3 on R-INF; only two positions there were answered.</item>
    /// <item>A writer that set <c>itemsCorrect = itemsSeen</c> would call R-INF <c>on_level</c>.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Each_touched_topic_records_its_own_seen_correct_and_band()
    {
        var fixture = await SeedAsync(Unique("T219A"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        var (placementId, _) = await SitAsync(userId, fixture, wrongReadingPositions: [0]);

        var row = await RowAsync(placementId, fixture.ReadingSkillId);
        Assert.Equal(PlacementResultStatus.Measured, row.ResultStatus);
        Assert.Equal("B1", row.CefrLevel!.Value.Code);

        var priors = PlacementTopicPrior.Deserialize(row.TopicPriorsJson)
            .ToDictionary(p => p.TopicId);

        Assert.Equal(2, priors.Count);

        var inference = priors[fixture.TopicIds["R-INF"]];
        Assert.Equal(2, inference.ItemsSeen);
        Assert.Equal(1, inference.ItemsCorrect);
        Assert.Equal("B1", inference.EvaluatedBand);
        Assert.Equal(PlacementTopicPrior.Weak, inference.RoutingSignal);

        var stance = priors[fixture.TopicIds["R-ATT"]];
        Assert.Equal(1, stance.ItemsSeen);
        Assert.Equal(1, stance.ItemsCorrect);
        Assert.Equal("B2", stance.EvaluatedBand);
        Assert.Equal(PlacementTopicPrior.OnLevel, stance.RoutingSignal);
    }

    /// <summary>
    /// An unanswered skill has no topic priors, and an <c>itemsSeen</c> count of three items cannot be
    /// claimed by a skill the sitting never reached. The column's CHECK refuses a hint on a
    /// <c>not_measured</c> row outright; this asserts the writer does not even try.
    /// </summary>
    [Fact]
    public async Task A_skill_the_sitting_never_reached_carries_no_topic_hint()
    {
        var fixture = await SeedAsync(Unique("T219B"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        // Answer only the reading half; the writing tasks stay untouched.
        int placementId;
        int attemptId;
        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            var info = await service.StartPlacementTestAsync(userId, fixture.ExamId);
            placementId = info.PlacementId;
            attemptId = info.AttemptId;
        }

        for (var i = 0; i < fixture.ReadingQuestions.Count; i++)
        {
            var question = fixture.ReadingQuestions[i];
            if (i > 0) await AdvanceToSkillAsync(attemptId, question.QuestionId);
            await using var db = Db();
            var service = new PlacementService(db, new LearnerService(db));
            await service.SaveAnswerAsync(
                userId, placementId, question.QuestionId,
                OptionId(question.ItemVersionId, question.KeyLabel), isFlagged: false);
        }

        // The listening skill has no positions on this paper at all: the sitting is submitted with the
        // writing half untouched, and the writer records nothing for a skill it never reached.
        await using (var db = Db())
        {
            var service = new PlacementService(db, new LearnerService(db));
            await service.SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        var skipped = await RowAsync(placementId, fixture.ListeningSkillId);
        Assert.Equal(PlacementResultStatus.NotMeasured, skipped.ResultStatus);
        Assert.Null(skipped.TopicPriorsJson);
        Assert.Null(skipped.CriterionProfileJson);
    }

    // ---------------------------------------------------------------------------------------------
    // criterion 2: the productive half's criterion scores and the weakest one
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Three criteria, and the weakest is the one with the lowest <b>proportion of its ceiling</b>.
    /// COH is scored lowest while GRAM is scored FIRST (lowest id), so a writer that named the first
    /// criterion in the list would name the wrong one.
    ///
    /// <b>The input that would pass a weaker test:</b> a writer that took <c>Scores.First()</c> passes
    /// "a weakest criterion was stored" whenever the weakest happens to sort first. Here the weakest is
    /// the last, so the assertion compares to the computed minimum and to the lowest id at once.
    /// </summary>
    [Fact]
    public async Task The_productive_half_records_each_criterion_and_names_the_lowest_ratio_as_weakest()
    {
        var fixture = await SeedAsync(Unique("T219C"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        // GRAM is the lowest criterion ID and the second-lowest score; COH is the weakest.
        var scores = new Dictionary<string, decimal> { ["GRAM"] = 5m, ["LEX"] = 6m, ["COH"] = 4m };
        var (placementId, attemptId) = await SitAsync(userId, fixture);
        await SettleWritingAsync(attemptId, fixture, scores, priorityFix: "Vary your complex clauses.");
        var row = await RowAsync(placementId, fixture.WritingSkillId);
        Assert.Equal(PlacementResultStatus.Measured, row.ResultStatus);

        var profile = PlacementCriterionProfile.Deserialize(row.CriterionProfileJson);
        Assert.NotNull(profile);
        Assert.Equal(3, profile!.Scores.Count);

        // every criterion, with the score and the ceiling it was marked out of
        foreach (var (code, expected) in scores)
        {
            var scored = profile.Scores.Single(s => s.CriterionId == fixture.CriterionIds[code]);
            Assert.Equal(code, scored.Code);
            Assert.Equal(expected, scored.Score);
            Assert.Equal(6m, scored.MaxScore);
        }

        var lowest = profile.Scores.OrderBy(s => s.Score / s.MaxScore).First();
        Assert.Equal("COH", profile.WeakestCriterion);
        Assert.Equal(lowest.Code, profile.WeakestCriterion);

        // the grader's own advice travels with the profile: it is the only row that knows what to do
        Assert.Equal("Vary your complex clauses.", profile.PriorityFix);

        // #219: both writing tasks were ANSWERED but never judged (#253 stores a productive answer
        // with IsCorrect null), so they are not items seen at all. Counting them would report the
        // W-ARG topic as 0 of 2 and mark it weak on the strength of a rubric that has its own column.
        Assert.Null(row.TopicPriorsJson);
    }

    /// <summary>
    /// A section may cap a criterion below its table ceiling (UI-20: TOEIC Speaking scores 0–3 on
    /// criteria IELTS scores 0–6). The weakest must then be read by <b>ratio</b>, not raw score:
    /// LEX at 2 of 3 is 67% and GRAM at 3 of 6 is 50%, so GRAM is weakest although LEX's raw score is
    /// lower.
    ///
    /// <b>The input that would pass a weaker test:</b> ordering by raw <c>Score</c>. It names LEX,
    /// which this asserts against.
    /// </summary>
    [Fact]
    public async Task The_weakest_criterion_is_the_lowest_ratio_not_the_lowest_raw_score()
    {
        var fixture = await SeedAsync(Unique("T219D"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        // LEX is capped at 3 for this writing section, the way a TOEIC speaking part is capped at 0-3.
        // A section that lists criteria accepts ONLY those (UI-20), so all three are listed and only
        // LEX's ceiling moves.
        await SqlAsync($$"""
            INSERT INTO SectionCriteria (SectionId, CriterionId, SkillId, MaxScore)
            SELECT s.SectionId, c.CriterionId, {{fixture.WritingSkillId}},
                   CASE WHEN c.Code = 'LEX' THEN 3 ELSE NULL END
            FROM Sections s
            CROSS JOIN GradingCriteria c
            WHERE s.SkillId = {{fixture.WritingSkillId}}
              AND c.SkillId = {{fixture.WritingSkillId}};
            """);

        var scores = new Dictionary<string, decimal> { ["GRAM"] = 3m, ["LEX"] = 2m, ["COH"] = 6m };
        var (placementId, attemptId) = await SitAsync(userId, fixture);
        await SettleWritingAsync(attemptId, fixture, scores);

        var profile = PlacementCriterionProfile.Deserialize((await RowAsync(placementId, fixture.WritingSkillId)).CriterionProfileJson);
        Assert.NotNull(profile);

        Assert.Equal(3m, profile!.Scores.Single(s => s.Code == "LEX").MaxScore);
        Assert.Equal("GRAM", profile.WeakestCriterion);
        Assert.NotEqual("LEX", profile.WeakestCriterion);
    }

    // ---------------------------------------------------------------------------------------------
    // criterion 3: nothing in the mastery path reads these signals
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A full placement creates no mastery row and moves no existing one. The recorded signal is a
    /// sequencing hint; mastery needs twelve items and the plan is explicit that three cannot serve.
    ///
    /// <b>The input that would pass a weaker test:</b> counting rows on a learner with no mastery row
    /// at all passes for a writer that inserts and then deletes. The fixture seeds an existing mastery
    /// row with known counters and asserts it is byte-for-byte unchanged, so a "helpful" sync of the
    /// placement's topics into mastery is caught in both directions — created and moved.
    /// </summary>
    [Fact]
    public async Task A_placement_creates_no_mastery_record_and_moves_none()
    {
        var fixture = await SeedAsync(Unique("T219E"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        // an existing mastery row, with counters a placement must not touch
        await SqlAsync($$"""
            INSERT INTO QuestionCategories (CertId, Code, Name)
              VALUES ({{fixture.CertId}}, 'R-INF', N'Inference');
            DECLARE @cat INT = SCOPE_IDENTITY();
            INSERT INTO UserCategoryMasteries (UserId, CategoryId, CertId, State, AnsweredCount, CorrectCount, Streak, ConsecutiveWrong, SessionDays)
              VALUES ({{userId}}, @cat, {{fixture.CertId}}, 'reviewing', 7, 5, 2, 1, 2);
            """);

        await using var before = Db();
        var seeded = await before.UserCategoryMasteries.AsNoTracking()
            .Where(m => m.UserId == userId).ToListAsync();
        Assert.Single(seeded);

        var (_, attemptId) = await SitAsync(userId, fixture);
        await SettleWritingAsync(attemptId, fixture,
            new Dictionary<string, decimal> { ["GRAM"] = 2m, ["LEX"] = 2m, ["COH"] = 2m });

        await using var after = Db();
        var rows = await after.UserCategoryMasteries.AsNoTracking()
            .Where(m => m.UserId == userId).ToListAsync();

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(seeded[0].CategoryId, row.CategoryId);
        Assert.Equal("reviewing", row.State);
        Assert.Equal(7, row.AnsweredCount);
        Assert.Equal(5, row.CorrectCount);
        Assert.Equal(2, row.Streak);
        Assert.Equal(1, row.ConsecutiveWrong);
        Assert.Equal(2, row.SessionDays);
        Assert.Equal(seeded[0].MasteredAt, row.MasteredAt);
        Assert.Equal(seeded[0].UpdatedAt, row.UpdatedAt);
    }

    // ---------------------------------------------------------------------------------------------
    // criterion 4: the first week's ordering reads them, as hints only
    // ---------------------------------------------------------------------------------------------

    private static SkillPlanBlock Block(params int[] topics) =>
        new(1, Modality.Reading, CefrLevel.B1, MasteryState.Reviewing, topics, LessonId: null, MockExamId: null);

    /// <summary>
    /// The hint moves a topic to the front of week one and leaves every later week on the block's own
    /// order. A placement said R-ATT went badly; the plan starts there.
    ///
    /// <b>The input that would pass a weaker test:</b> a planner that reordered <b>every</b> week, or
    /// one that ignored hints entirely. Both are asserted: week one's first drill is the hinted topic,
    /// and week two's first drill is the unhinted order's.
    /// </summary>
    [Fact]
    public void The_first_week_starts_on_the_topic_the_placement_saw_go_badly()
    {
        var plain = Block(10, 11, 12);
        var hinted = plain with { PlacementWeakTopicIds = [12] };

        var order = LearningPathPlanner.FirstWeekTopicOrder(hinted);
        Assert.Equal([12, 10, 11], order);

        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, [hinted]);
        var weekOne = plan.Where(p => p.WeekNumber == 1 && p.Route == StepRoute.ItemSelector).ToList();
        Assert.NotEmpty(weekOne);
        Assert.Equal(12, weekOne[0].TopicId);

        // a later week is not guided by a hint about where to START
        var weekTwo = plan.Where(p => p.WeekNumber == 2 && p.Route == StepRoute.ItemSelector).ToList();
        Assert.NotEmpty(weekTwo);
        Assert.Equal(11, weekTwo[0].TopicId);
    }

    /// <summary>
    /// A hint can only permute the topics the block already holds. Naming a topic from another level,
    /// or one the pool has no stock in, must not add it — a placement is not a licence to plan an empty
    /// cell — and must not change any step's size or type.
    ///
    /// <b>The input that would pass a weaker test:</b> a planner that unioned the hint into the topic
    /// list passes "the hinted topic is drilled", which is why this asserts the drilled set is a subset
    /// of the block's own and the sizing tier still follows mastery.
    /// </summary>
    [Fact]
    public void A_hint_never_adds_a_topic_or_changes_a_step_s_size_or_type()
    {
        var block = Block(10, 11) with { PlacementWeakTopicIds = [99, 10] };
        var plan = LearningPathPlanner.Plan(weeklyStudyHours: 6m, [block]);

        Assert.DoesNotContain(plan, p => p.TopicId == 99);

        var supplied = block.TopicIds.ToHashSet();
        Assert.All(plan, p => Assert.Contains(p.TopicId, supplied));

        // sizing still follows the cell's own mastery, not the hint
        var expected = LearningPathPlanner.SizingTierFor(block.Mastery);
        Assert.All(plan.Where(p => p.Route == StepRoute.ItemSelector), p => Assert.Equal(expected, p.SizingTier));

        // and an empty hint list is the block's order, not an empty week
        Assert.Equal(block.TopicIds, LearningPathPlanner.FirstWeekTopicOrder(block with { PlacementWeakTopicIds = [] }));
        Assert.Equal(block.TopicIds, LearningPathPlanner.FirstWeekTopicOrder(block with { PlacementWeakTopicIds = null }));
    }

    /// <summary>
    /// The wiring: a generated path reads the latest placement's hint for the skill and level it
    /// plans at, and week one drills the topic the placement saw go badly.
    ///
    /// <b>The input that would pass a weaker test:</b> a service that loaded the hints but keyed them
    /// by skill only — the fixture stores the skill's level at B1 while a <b>second</b>, higher-level
    /// placement row exists from a different band, and a skill-keyed reader would order week one from
    /// the wrong band's evidence. Here the hint is written directly at the band the path plans at, so
    /// the test fails if the service drops the hint on the floor between the row and the plan.
    /// </summary>
    [Fact]
    public async Task A_generated_path_drills_the_topic_the_placement_saw_go_badly_in_week_one()
    {
        var fixture = await SeedAsync(Unique("T219F"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        // The path plans at B1: the learner's own state says so.
        await SqlAsync($$"""
            INSERT INTO LearnerSkillStates (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate, Source, MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
              VALUES ({{userId}}, {{fixture.CertId}}, {{fixture.ReadingSkillId}}, 'B1', 50, 3.50, 'placement',
                      SYSDATETIMEOFFSET(), DATEADD(DAY, 90, SYSDATETIMEOFFSET()), 'SkillStateWriter', 1);
            -- The certificate's own B1 row already exists from the fixture; only the hours and the
            -- target band have to be added for a plan to be buildable.
            UPDATE ScoreBands SET CumulativeStudyHours = 400 WHERE CertId = {{fixture.CertId}} AND Code = 'B1';
            IF NOT EXISTS (SELECT 1 FROM ScoreBands WHERE CertId = {{fixture.CertId}} AND Code = 'B2')
              INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours)
                VALUES ({{fixture.CertId}}, 'B2', N'Upper', 501, 900, 3, 800);
            INSERT INTO TargetBands (UserId, CertId, CurrentBandCode, TargetBandCode, TargetDate, WeeklyStudyHours, FeasibilityStatus)
              VALUES ({{userId}}, {{fixture.CertId}}, 'B1', 'B2',
                      DATEADD(DAY, 200, CAST(SYSDATETIMEOFFSET() AS DATE)), 6, 'feasible');
            """);

        // A done placement whose reading row carries a weak hint on R-ATT, at the band the path plans.
        await SqlAsync($$"""
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
              VALUES ({{fixture.CertId}}, N'Placement 219F', 'published', 30, 'placement');
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
              VALUES ({{userId}}, @exam, {{fixture.CertId}}, 'graded', DATEADD(HOUR, 1, SYSDATETIMEOFFSET()), 350, 'B1', 'AttemptEngine', 1);
            DECLARE @attempt INT = SCOPE_IDENTITY();
            INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, ResultBandCode, TotalScore, TakenAt,
                                        WrittenByService, ProjectionVersion)
              VALUES ({{userId}}, {{fixture.CertId}}, @exam, @attempt, 'in_progress', 'B1', 350,
                      DATEADD(DAY, -1, SYSDATETIMEOFFSET()), 'AttemptEngine', 1);
            DECLARE @plc INT = SCOPE_IDENTITY();
            DECLARE @measured DATETIMEOFFSET = DATEADD(DAY, -1, SYSDATETIMEOFFSET());
            INSERT INTO PlacementSkillResults
              (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
               TopicPriorsJson, MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
              VALUES (@plc, {{fixture.ReadingSkillId}}, {{fixture.CertId}}, 'measured', 'B1', 50, 3.50,
                      '[{"topicId":{{fixture.TopicIds["R-ATT"]}},"evaluatedBand":"B1","itemsSeen":2,"itemsCorrect":0,"routingSignal":"weak"}]',
                      @measured, DATEADD(DAY, 90, @measured), 'AttemptEngine', 1);
            UPDATE PlacementTests SET Status = 'done', WrittenByService = 'AttemptEngine', ProjectionVersion = 2
              WHERE PlacementId = @plc;
            """);

        var certId = fixture.CertId;
        var certCode = await SelectCertCodeAsync(certId);

        await using var db = Db();
        var paths = new LearningPathService(db, new LearnerService(db));
        var summary = await paths.GeneratePathAsync(userId);

        var weekOne = await db.LearnerPathSteps.AsNoTracking()
            .Where(s => s.LearnerPathId == summary.LearnerPathId
                && s.WeekNumber == 1
                && (s.StepType == "focus_practice" || s.StepType == "review_spaced"))
            .OrderBy(s => s.WeekSequence)
            .Select(s => s.TopicId)
            .ToListAsync();

        Assert.NotEmpty(weekOne);
        Assert.Equal(fixture.TopicIds["R-ATT"], weekOne[0]);
        Assert.DoesNotContain(weekOne, t => t == fixture.TopicIds["W-ARG"]);
        Assert.NotEmpty(certCode);
    }

    private async Task<string> SelectCertCodeAsync(int certId)
    {
        await using var db = Db();
        return await db.Certificates.Where(c => c.CertId == certId).Select(c => c.Code).SingleAsync();
    }
}
