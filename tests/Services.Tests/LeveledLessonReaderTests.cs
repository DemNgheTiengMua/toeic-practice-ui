using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #240: the leveled lesson reader. Five criteria, each with the input that would pass a weaker
/// implementation while being wrong:
/// <list type="number">
/// <item>A lesson is addressed by (skill, topic, level) and carries its level. The wrong input: a
/// lesson whose level is inferred from the cell's stocked items, which reads right until the stock
/// moves and then silently changes the lesson's level.</item>
/// <item>The primary and remedial modules are separate. The wrong input: one card set plus a flag, so
/// the remedial visit re-reads identical instruction under a different title.</item>
/// <item>A micro-check is graded on the server and writes the evidence a drill writes. The wrong
/// input: a private counter in <c>LearnerLessonMicroChecks</c> that no view and no cell read sees.</item>
/// <item>Progress resumes at the exact card. The wrong input: "restart at card one", which looks like
/// a working reader until someone reads half a lesson and comes back.</item>
/// <item>A lesson step closes only when the lesson is complete. The wrong input: closing on the first
/// card view, which marks done a lesson nobody read.</item>
/// </list>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LeveledLessonReaderTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..16];

    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    private static LessonService Lessons(CompanionDbContext db) => new(db);

    /// <summary>
    /// One reading skill with a canonical R-DET cell at B1 holding a primary module of three cards and
    /// a remedial module of two DIFFERENT cards. The content differs on purpose: the separation this
    /// asserts is content, not a flag.
    /// </summary>
    private async Task<Seed> SeedAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Topics (Modality, Code, Name)
            SELECT v.Modality, v.Code, v.Name
            FROM (VALUES
              ('listening','L-GIST',N'Global gist'), ('listening','L-DET',N'Detail'),
              ('listening','L-INF',N'Inference'), ('listening','L-ATT',N'Attitude'),
              ('listening','L-FUNC',N'Function'), ('listening','L-ORG',N'Organisation'),
              ('reading','R-GIST',N'Global thesis'), ('reading','R-DET',N'Local detail'),
              ('reading','R-INF',N'Bridging inference'), ('reading','R-ATT',N'Authorial stance'),
              ('reading','R-COH',N'Cohesion'), ('reading','R-GRAM',N'Grammar'),
              ('speaking','S-DESC',N'Description'), ('speaking','S-INT',N'Interaction'),
              ('speaking','S-ARG',N'Argument'), ('speaking','S-SYN',N'Synthesis'),
              ('writing','W-CORR',N'Correspondence'), ('writing','W-DATA',N'Data'),
              ('writing','W-ARG',N'Argument'), ('writing','W-DISC',N'Discourse')
            ) v(Modality, Code, Name)
            WHERE NOT EXISTS (SELECT 1 FROM Topics t WHERE t.Modality = v.Modality AND t.Code = v.Code);

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Reader cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO QuestionCategories (CertId, Code, Name)
            VALUES (@cert, 'R-DETAIL', N'Reading detail');
            DECLARE @cat INT = SCOPE_IDENTITY();

            DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET');

            INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, CategoryId, Title, Summary, EstimatedMinutes, DisplayOrder)
            VALUES (@cert, @skill, 'reading', @topic, 'B1', @cat, N'Detail at B1', N'Primary rule', 8, 1);
            DECLARE @lesson INT = SCOPE_IDENTITY();

            -- A second lesson in the SAME cell at a DIFFERENT level, so the cell lookup is real rather
            -- than a table with one lesson in it.
            INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, Summary, EstimatedMinutes, DisplayOrder)
            VALUES (@cert, @skill, 'reading', @topic, 'B2', N'Detail at B2', N'Another level', 8, 2);

            INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder, Module) VALUES
              (@lesson, @cert, 'rule',    N'Primary 1', N'PRIMARY-RULE-CONTENT',    0, 'primary'),
              (@lesson, @cert, 'pitfall', N'Primary 2', N'PRIMARY-PITFALL-CONTENT', 1, 'primary'),
              (@lesson, @cert, 'example', N'Primary 3', N'PRIMARY-EXAMPLE-CONTENT', 2, 'primary'),
              (@lesson, @cert, 'rule',    N'Remedial 1', N'REMEDIAL-DISTRACTOR-ANATOMY', 0, 'remedial'),
              (@lesson, @cert, 'pitfall', N'Remedial 2', N'REMEDIAL-UPGRADE-COMPARISON', 1, 'remedial');

            DECLARE @mc INT;
            INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder, Module,
                                     QuestionPrompt, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation)
            VALUES (@lesson, @cert, 'micro_check', N'Check', N'', 3, 'primary',
                    N'Which is right?', N'Right', N'Wrong', N'Also wrong', N'Wrong too', 'A', N'Because.');
            SET @mc = SCOPE_IDENTITY();

            INSERT INTO Roles (Code, Name) SELECT 'learner', N'Learner'
              WHERE NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{certCode}@reader.local', 0x01, N'Reader', @cert);
            """);

        await using var db = Db();
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var skill = await db.Skills.SingleAsync(s => s.CertId == cert.CertId);
        var topic = await db.Topics.SingleAsync(t => t.Modality == "reading" && t.Code == "R-DET");
        var b1 = await db.Lessons.SingleAsync(l => l.CertId == cert.CertId && l.CefrLevel == CefrLevel.B1);
        var b2 = await db.Lessons.SingleAsync(l => l.CertId == cert.CertId && l.CefrLevel == CefrLevel.B2);
        var primary = await db.LessonCards
            .Where(c => c.LessonId == b1.LessonId && c.Module == LessonModule.Primary)
            .OrderBy(c => c.DisplayOrder).ToListAsync();        var remedial = await db.LessonCards
            .Where(c => c.LessonId == b1.LessonId && c.Module == LessonModule.Remedial)
            .OrderBy(c => c.DisplayOrder).ToListAsync();
        var userId = await db.Users.Where(u => u.Email == $"{certCode}@reader.local").Select(u => u.UserId).SingleAsync();

        return new Seed(cert.CertId, skill.SkillId, topic.TopicId, b1.LessonId, b2.LessonId,
            userId, primary.Select(c => c.CardId).ToList(), remedial.Select(c => c.CardId).ToList(),
            primary.Single(c => c.CardType == "micro_check").CardId);
    }

    private sealed record Seed(
        int CertId, int SkillId, int TopicId, int LessonId, int OtherLevelLessonId, int UserId,
        IReadOnlyList<int> PrimaryCardIds, IReadOnlyList<int> RemedialCardIds, int MicroCheckCardId);

    // -------------------------------------------------------------------------------------------
    // 1. addressed by (skill, topic, level), carrying its own level
    // -------------------------------------------------------------------------------------------
    [Fact]
    public async Task A_lesson_is_addressed_by_skill_topic_and_level_and_carries_that_level()
    {
        var seed = await SeedAsync(Unique("L1"));

        await using var db = Db();
        var lesson = await Lessons(db).GetLessonForCellAsync(
            seed.CertId, seed.SkillId, seed.TopicId, "B1", LessonModule.Primary);

        Assert.NotNull(lesson);
        Assert.Equal(seed.LessonId, lesson.LessonId);

        // The wrong input this catches: a lookup that ignored the level and took "whichever lesson of
        // the skill sorted first" would return the B1 lesson for a B2 request too, because B1 sorts
        // first. The B2 cell must resolve to the OTHER lesson, not to that one.
        var b2 = await Lessons(db).GetLessonForCellAsync(
            seed.CertId, seed.SkillId, seed.TopicId, "B2", LessonModule.Primary);
        Assert.NotNull(b2);
        Assert.Equal(seed.OtherLevelLessonId, b2.LessonId);
        Assert.NotEqual(lesson.LessonId, b2.LessonId);

        // And the level is the row's own, not the caller's claim about it.
        Assert.Equal("B1", lesson.CefrLevel);
        Assert.Equal("B2", b2.CefrLevel);
        Assert.Equal("R-DET", lesson.TopicCode);
        Assert.Equal(seed.TopicId, lesson.TopicId);
    }

    // -------------------------------------------------------------------------------------------
    // 2. the primary and remedial modules are separate, and a remedial visit shows the remedial one
    // -------------------------------------------------------------------------------------------
    [Fact]
    public async Task A_remedial_visit_shows_the_remedial_module_and_its_content_is_different()
    {
        var seed = await SeedAsync(Unique("L2"));

        await using var db = Db();
        var service = Lessons(db);
        var primary = await service.GetLessonAsync(seed.LessonId, seed.CertId, LessonModule.Primary);
        var remedial = await service.GetLessonAsync(seed.LessonId, seed.CertId, LessonModule.Remedial);

        Assert.NotNull(primary);
        Assert.NotNull(remedial);
        Assert.True(primary.HasRemedialModule);
        Assert.True(remedial.HasRemedialModule);

        Assert.Equal(LessonModule.Primary, primary.Module);
        Assert.Equal(LessonModule.Remedial, remedial.Module);

        // The wrong input this catches: one card set plus a `Module` flag, or the same module with a
        // different title. Both would give the two visits the SAME card ids and the SAME content.
        var primaryIds = primary.Cards.Select(c => c.CardId).ToHashSet();
        var remedialIds = remedial.Cards.Select(c => c.CardId).ToHashSet();

        Assert.NotEqual(primaryIds, remedialIds);
        Assert.Empty(primaryIds.Intersect(remedialIds));
        Assert.All(primary.Cards, c => Assert.Equal(LessonModule.Primary, c.Module));
        Assert.All(remedial.Cards, c => Assert.Equal(LessonModule.Remedial, c.Module));

        var primaryContent = primary.Cards.Select(c => c.ContentMarkdown).ToList();
        var remedialContent = remedial.Cards.Select(c => c.ContentMarkdown).ToList();
        Assert.NotEqual(primaryContent, remedialContent);
        Assert.Contains(remedialContent, text => text == "REMEDIAL-DISTRACTOR-ANATOMY");
        Assert.DoesNotContain("REMEDIAL-DISTRACTOR-ANATOMY", primaryContent);

        // The key answer never leaves the page: the card's CorrectOption is blanked in the read model.
        Assert.All(primary.Cards, c => Assert.Null(c.CorrectOption));
        Assert.All(remedial.Cards, c => Assert.Null(c.CorrectOption));
    }

    // -------------------------------------------------------------------------------------------
    // 3. a micro-check is graded on the server and writes the evidence a drill writes
    // -------------------------------------------------------------------------------------------
    [Fact]
    public async Task A_micro_check_is_graded_on_the_server_and_writes_a_PracticeAnswers_row()
    {
        var seed = await SeedAsync(Unique("L3"));

        await using var db = Db();
        var learners = new LearnerService(db);
        var practice = new PracticeService(db, learners);

        var result = await practice.SubmitLessonMicroCheckAsync(
            seed.UserId, seed.LessonId, seed.MicroCheckCardId, "B");

        // Graded on the server: the reply says wrong and names the right option, and the page did not
        // carry it.
        Assert.False(result.IsCorrect);
        Assert.Equal("A", result.CorrectOption);

        await using var verify = Db();
        var answers = await verify.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.CardId == seed.MicroCheckCardId)
            .ToListAsync();

        // The wrong input this catches: a private counter, graded in the browser or recorded only in
        // LearnerLessonMicroChecks. Either leaves this count at zero and no view ever sees the answer.
        var answer = Assert.Single(answers);
        Assert.False(answer.IsCorrect);
        Assert.Equal("B", answer.SelectedLabel);
        Assert.NotNull(answer.AnsweredAt);

        // The sitting is an ordinary pool-shaped one, so the answer is attributable to a cell.
        var session = await verify.PracticeSessions.AsNoTracking()
            .SingleAsync(s => s.SessionId == answer.SessionId);
        Assert.Equal(seed.LessonId, session.LessonId);
        Assert.Equal(seed.TopicId, session.TopicId);
        Assert.Equal(CefrLevel.B1, session.CefrLevel);
        Assert.Equal("reading", session.Modality);
        Assert.Null(session.SectionId);
    }

    [Fact]
    public async Task The_micro_check_evidence_reaches_the_cell_mastery_read_the_path_sizes_from()
    {
        var seed = await SeedAsync(Unique("L4"));

        await using var db = Db();
        var learners = new LearnerService(db);
        var practice = new PracticeService(db, learners);
        await practice.SubmitLessonMicroCheckAsync(
            seed.UserId, seed.LessonId, seed.MicroCheckCardId, "A");

        // The read is the path's own: it counts graded answers for the cell. A micro-check that wrote
        // only a private counter leaves this at zero and the cell stays Unmeasured forever.
        var counts = await db.PracticeAnswers
            .AsNoTracking()
            .CountAsync(pa => pa.Session.UserId == seed.UserId
                && pa.CardId != null
                && pa.Session.TopicId == seed.TopicId
                && pa.Session.CefrLevel == CefrLevel.B1);

        Assert.Equal(1, counts);
    }

    [Fact]
    public async Task Re_posting_the_same_micro_check_writes_no_second_evidence_row()
    {
        var seed = await SeedAsync(Unique("L5"));

        await using var db = Db();
        var learners = new LearnerService(db);
        var practice = new PracticeService(db, learners);

        await practice.SubmitLessonMicroCheckAsync(seed.UserId, seed.LessonId, seed.MicroCheckCardId, "A");
        await practice.SubmitLessonMicroCheckAsync(seed.UserId, seed.LessonId, seed.MicroCheckCardId, "A");
        await practice.SubmitLessonMicroCheckAsync(seed.UserId, seed.LessonId, seed.MicroCheckCardId, "A");

        await using var verify = Db();
        var rows = await verify.PracticeAnswers
            .AsNoTracking()
            .CountAsync(pa => pa.CardId == seed.MicroCheckCardId);

        // The wrong input: a grade per post, which lets one known answer farm a cell by repetition.
        Assert.Equal(1, rows);
    }

    // -------------------------------------------------------------------------------------------
    // 4. progress resumes at the exact card
    // -------------------------------------------------------------------------------------------
    [Fact]
    public async Task A_partially_read_lesson_resumes_at_the_exact_card_it_stopped_at()
    {
        var seed = await SeedAsync(Unique("L6"));

        await using var db = Db();
        var service = Lessons(db);

        // Read the primary module's first two cards and stop.
        await service.RecordCardViewAsync(seed.UserId, seed.LessonId, seed.PrimaryCardIds[0]);
        await service.RecordCardViewAsync(seed.UserId, seed.LessonId, seed.PrimaryCardIds[1]);

        var progress = await service.GetProgressAsync(seed.UserId, seed.LessonId, LessonModule.Primary);

        // The wrong input this catches: "resume = restart at card one" reports an index of 1, and the
        // learner re-reads what they already read.
        Assert.Equal(seed.PrimaryCardIds[1], progress.LastViewedCardId);
        // Index is 1-based into THIS module's own list, and points at the last card viewed.
        Assert.Equal(2, progress.ResumeCardIndex);
        // The primary module holds three content cards and the micro-check.
        Assert.Equal(seed.PrimaryCardIds.Count, progress.TotalCards);
        Assert.Equal(4, progress.TotalCards);
        Assert.False(progress.IsCompleted);

        // Re-reading the first card moves the pointer back but does not un-complete anything or
        // double-count the cards already read.
        await service.RecordCardViewAsync(seed.UserId, seed.LessonId, seed.PrimaryCardIds[0]);
        var after = await service.GetProgressAsync(seed.UserId, seed.LessonId, LessonModule.Primary);
        Assert.Equal(1, after.ResumeCardIndex);
        Assert.Equal(2, after.CardsCompleted);
    }

    [Fact]
    public async Task The_remedial_module_resumes_independently_of_the_finished_primary_one()
    {
        var seed = await SeedAsync(Unique("L7"));

        await using var db = Db();
        var service = Lessons(db);

        // Finish the primary module entirely.
        foreach (var cardId in seed.PrimaryCardIds)
        {
            await service.RecordCardViewAsync(seed.UserId, seed.LessonId, cardId);
        }

        var primary = await service.GetProgressAsync(seed.UserId, seed.LessonId, LessonModule.Primary);
        Assert.True(primary.IsCompleted);

        // The remedial module nobody has opened must be untouched: not completed, and at card one.
        // The wrong input this catches: one progress row per lesson, which would carry IsCompleted
        // and the last viewed card across to a module the learner has not read.
        var remedial = await service.GetProgressAsync(seed.UserId, seed.LessonId, LessonModule.Remedial);
        Assert.False(remedial.IsCompleted);
        Assert.Null(remedial.LastViewedCardId);
        Assert.Equal(0, remedial.CardsCompleted);
        Assert.Equal(1, remedial.ResumeCardIndex);
        Assert.Equal(2, remedial.TotalCards);

        // Reading one remedial card leaves the primary module's own position alone.
        await service.RecordCardViewAsync(seed.UserId, seed.LessonId, seed.RemedialCardIds[0]);
        var remedialAfter = await service.GetProgressAsync(seed.UserId, seed.LessonId, LessonModule.Remedial);
        Assert.Equal(1, remedialAfter.ResumeCardIndex);
        Assert.False(remedialAfter.IsCompleted);
        Assert.True((await service.GetProgressAsync(seed.UserId, seed.LessonId, LessonModule.Primary)).IsCompleted);
    }

    // -------------------------------------------------------------------------------------------
    // 5. a lesson step closes only when the lesson is complete
    // -------------------------------------------------------------------------------------------
    [Fact]
    public async Task A_lesson_step_is_not_closed_by_the_first_card_view()
    {
        var seed = await SeedAsync(Unique("L8"));

        // A concept_lesson step naming this lesson, on an active path, and available.
        await database.SqlAsync($"""
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              ({seed.CertId}, 'B1', N'Band', 0, 500, 1);
            INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status)
            VALUES ({seed.UserId}, {seed.CertId}, 'B1', 'active');
            DECLARE @lp INT = SCOPE_IDENTITY();
            INSERT INTO LearnerPathSteps
              (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr, StepType, SizingTier, RefLessonId, Status)
            VALUES (@lp, {seed.CertId}, {seed.SkillId}, 'reading', {seed.TopicId}, 'B1',
                    'concept_lesson', 'developing', {seed.LessonId}, 'available');
            """);

        await using var db = Db();
        var service = Lessons(db);

        // One card is not a lesson.
        await service.RecordCardViewAsync(seed.UserId, seed.LessonId, seed.PrimaryCardIds[0]);

        await using var verify = Db();
        var afterOne = await verify.LearnerPathSteps.AsNoTracking()
            .SingleAsync(s => s.RefLessonId == seed.LessonId);
        Assert.NotEqual("done", afterOne.Status);

        // Read the rest of the primary module.
        foreach (var cardId in seed.PrimaryCardIds.Skip(1))
        {
            await service.RecordCardViewAsync(seed.UserId, seed.LessonId, cardId);
        }

        await using var verify2 = Db();
        var afterAll = await verify2.LearnerPathSteps.AsNoTracking()
            .SingleAsync(s => s.RefLessonId == seed.LessonId);
        Assert.Equal("done", afterAll.Status);
        Assert.NotNull(afterAll.CompletedAt);
    }

    [Fact]
    public async Task Finishing_the_primary_module_does_not_close_an_expanded_remedial_step()
    {
        var seed = await SeedAsync(Unique("L9"));

        // A remedial step naming this lesson, beside a concept_lesson step on the same lesson.
        await database.SqlAsync($"""
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              ({seed.CertId}, 'B1', N'Band', 0, 500, 1);
            INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status)
            VALUES ({seed.UserId}, {seed.CertId}, 'B1', 'active');
            DECLARE @lp INT = SCOPE_IDENTITY();
            INSERT INTO LearnerPathSteps
              (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr, StepType, SizingTier, RefLessonId, Status, WeekNumber, WeekSequence)
            VALUES (@lp, {seed.CertId}, {seed.SkillId}, 'reading', {seed.TopicId}, 'B1',
                    'concept_lesson', 'developing', {seed.LessonId}, 'available', 1, 1);
            INSERT INTO LearnerPathSteps
              (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr, StepType, SizingTier, RefLessonId, Status, WeekNumber, WeekSequence)
            VALUES (@lp, {seed.CertId}, {seed.SkillId}, 'reading', {seed.TopicId}, 'B1',
                    'expanded_remedial', 'developing', {seed.LessonId}, 'available', 1, 2);
            """);

        await using var db = Db();
        var service = Lessons(db);

        foreach (var cardId in seed.PrimaryCardIds)
        {
            await service.RecordCardViewAsync(seed.UserId, seed.LessonId, cardId);
        }

        await using var verify = Db();
        var concept = await verify.LearnerPathSteps.AsNoTracking()
            .SingleAsync(s => s.RefLessonId == seed.LessonId && s.StepType == "concept_lesson");
        var remedialStep = await verify.LearnerPathSteps.AsNoTracking()
            .SingleAsync(s => s.RefLessonId == seed.LessonId && s.StepType == "expanded_remedial");

        // The wrong input this catches: closing "the lesson's step" without asking which module. Both
        // steps would go done on the primary module's completion, and the remedial one would be
        // ticked off without the learner reading it.
        Assert.Equal("done", concept.Status);
        Assert.NotEqual("done", remedialStep.Status);

        // Reading the remedial module closes it, and only then.
        foreach (var cardId in seed.RemedialCardIds)
        {
            await service.RecordCardViewAsync(seed.UserId, seed.LessonId, cardId);
        }

        await using var verify2 = Db();
        var remedialClosed = await verify2.LearnerPathSteps.AsNoTracking()
            .SingleAsync(s => s.RefLessonId == seed.LessonId && s.StepType == "expanded_remedial");
        Assert.Equal("done", remedialClosed.Status);
    }

    [Fact]
    public async Task A_lesson_step_opens_the_reader_at_the_module_its_type_names()
    {
        var seed = await SeedAsync(Unique("LA"));

        await database.SqlAsync($"""
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              ({seed.CertId}, 'B1', N'Band', 0, 500, 1);
            INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status)
            VALUES ({seed.UserId}, {seed.CertId}, 'B1', 'active');
            DECLARE @lp INT = SCOPE_IDENTITY();
            INSERT INTO LearnerPathSteps
              (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr, StepType, SizingTier, RefLessonId, Status, WeekNumber, WeekSequence)
            VALUES (@lp, {seed.CertId}, {seed.SkillId}, 'reading', {seed.TopicId}, 'B1',
                    'expanded_remedial', 'developing', {seed.LessonId}, 'available', 1, 1);
            """);

        await using var db = Db();
        var paths = new LearningPathService(db, new LearnerService(db));
        var stepId = await db.LearnerPathSteps
            .Where(s => s.RefLessonId == seed.LessonId && s.StepType == "expanded_remedial")
            .Select(s => s.StepId)
            .SingleAsync();

        var start = await paths.StartStepAsync(seed.UserId, stepId);

        // The wrong input: one URL for both types, which makes the step type a label rather than a
        // binding, and a second attempt re-reads the primary module.
        Assert.Equal(StepRoute.LessonReader, start.Route);
        Assert.Contains($"module={LessonModule.Remedial}", start.RedirectUrl);
    }
}
