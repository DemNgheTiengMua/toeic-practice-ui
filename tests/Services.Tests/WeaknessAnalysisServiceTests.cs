using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class WeaknessAnalysisServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (WeaknessAnalysisService WeaknessService, LearnerService Learner) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learnerService = new LearnerService(db);
        var weaknessService = new WeaknessAnalysisService(db, learnerService);
        return (weaknessService, learnerService);
    }

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// Two pool cells on one paper: <c>R-DET</c> at B1 for the practice drill and <c>R-INF</c> at B2 for
    /// the graded attempt, so the topic view reports two rows with different figures and the service
    /// has something to order.
    /// <para>
    /// <b>#271: this fixture used to author legacy <c>Questions</c> rows with a <c>CategoryId</c>.</b>
    /// The weakness page now reports the topic axis, and <c>vw_UserTopicAccuracy</c> reaches a cell only
    /// through <c>Items</c> - a legacy answer has no cell at all, so a legacy fixture would make every
    /// <c>FrequentlyMissedTopics</c> assertion below an assertion about an empty list.
    /// </para>
    /// </summary>
    private Task<(int CertId, int Sec1Id, int Sec2Id, int ExamId,
        List<PoolPosition> P5, List<PoolPosition> P6)>
        SeedCertificateWithPoolPaperAsync(string certCode) =>
        SeedPoolPaperCoreAsync(certCode);

    private sealed record PoolPosition(int ExamItemId, int ItemId, int ItemVersionId);

    private async Task<(int CertId, int Sec1Id, int Sec2Id, int ExamId,
        List<PoolPosition> P5, List<PoolPosition> P6)> SeedPoolPaperCoreAsync(string certCode)
    {
        int certId, sec1Id, sec2Id, examId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var sec1 = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5", Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1 };
            var sec2 = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P6", Name = "Text Completion", DisplayOrder = 2, OptionCount = 4, ExpectedQuestionCount = 1 };
            db.Sections.AddRange(sec1, sec2);
            await db.SaveChangesAsync();

            var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 120, Purpose = "mock" };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            certId = cert.CertId;
            sec1Id = sec1.SectionId;
            sec2Id = sec2.SectionId;
            examId = exam.ExamId;
        }

        // Both cells exist in the canonical twenty; a database that has not seen them needs the rows.
        await database.SqlAsync("""
            INSERT INTO Topics (Modality, Code, Name)
            SELECT v.Modality, v.Code, v.Name
            FROM (VALUES ('reading','R-DET',N'Local detail'),
                         ('reading','R-INF',N'Bridging inference')) v(Modality, Code, Name)
            WHERE NOT EXISTS (SELECT 1 FROM Topics t WHERE t.Modality = v.Modality AND t.Code = v.Code);
            """);

        var p5 = new List<PoolPosition>();
        var p6 = new List<PoolPosition>();
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            // The pool's own authoring path, so the rows satisfy the real checks and a binding exists
            // for the section view to reach a section through.
            p5.Add(Position(await PoolFixture.AddMcqAsync(db, examId, certId, sec1Id, "reading", "P5-1", displayOrder: 1, level: "B1")));
            p5.Add(Position(await PoolFixture.AddMcqAsync(db, examId, certId, sec1Id, "reading", "P5-2", displayOrder: 2, level: "B1")));
            p6.Add(Position(await PoolFixture.AddMcqAsync(db, examId, certId, sec2Id, "reading", "P6-1", displayOrder: 1, level: "B2")));
            p6.Add(Position(await PoolFixture.AddMcqAsync(db, examId, certId, sec2Id, "reading", "P6-2", displayOrder: 2, level: "B2")));
        }

        return (certId, sec1Id, sec2Id, examId, p5, p6);

        static PoolPosition Position(PoolFixture.Position p) =>
            new(p.ExamItemId, p.ItemId, p.ItemVersionId);
    }

    /// <summary>A learner on a certificate, with no content rows of its own.</summary>
    private async Task<(int CertId, int Sec1Id, int Sec2Id)> SeedCertificateAsync(string certCode)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var sec1 = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5", Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1 };
        var sec2 = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P6", Name = "Text Completion", DisplayOrder = 2, OptionCount = 4, ExpectedQuestionCount = 1 };
        db.Sections.AddRange(sec1, sec2);
        await db.SaveChangesAsync();

        return (cert.CertId, sec1.SectionId, sec2.SectionId);
    }

    [Fact]
    public async Task Empty_state_when_learner_has_no_practice_or_exam_answers()
    {
        var certCode = Unique("EMPTY");
        await SeedCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var view = await service.GetWeaknessAnalysisAsync(userId);

        Assert.False(view.HasData);
        Assert.Equal(0, view.TotalAnswered);
        Assert.Equal(0, view.TotalCorrect);
        Assert.Equal(0, view.GradedExamCount);
        Assert.Equal(0, view.CompletedPracticeCount);
        Assert.Empty(view.FrequentlyMissedTopics);
        Assert.Null(view.PriorityPracticeSection);
        Assert.All(view.Sections, s => Assert.Equal(0, s.Answered));
    }

    /// <summary>
    /// The figures a learner sees must not move. <b>#271 changed the AXIS, not the arithmetic</b>:
    /// <c>vw_UserTopicAccuracy</c> reports the same stored answers the retired category view did, named
    /// by the pool's own <c>(TopicId, CefrLevel)</c>. So this pins both, and the two must agree:
    /// <list type="bullet">
    /// <item>the topic view's Answered/Correct for each cell,</item>
    /// <item>the section view's totals, which are untouched by this ticket,</item>
    /// <item>and the service's own output, which now names cells instead of categories.</item>
    /// </list>
    /// <b>The wrong input this catches:</b> a migration that groups by topic alone (two levels of one
    /// topic collapse into one blended row), or a reader that divides Correct by Answered a second time
    /// and reports a figure the view does not.
    /// </summary>
    [Fact]
    public async Task Numbers_come_from_vw_UserSectionAccuracyAll_and_vw_UserTopicAccuracy()
    {
        var certCode = Unique("ACCNUM");
        var (certId, sec1Id, sec2Id, examId, p5, p6) = await SeedCertificateWithPoolPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            // A graded attempt spanning both sections: 2 items in P5 (1 correct, 1 wrong) and 2 in P6
            // (both correct). That is the same four answers, and the same 3-of-4, the retired fixture
            // recorded - so the figures must not move, only the axis that names them.
            var attempt = new ExamAttempt
            {
                UserId = userId,
                CertId = certId,
                ExamId = examId,
                Status = "graded",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                TotalScore = 90
            };
            db.ExamAttempts.Add(attempt);
            await db.SaveChangesAsync();

            db.AttemptAnswers.AddRange(
                Answer(attempt.AttemptId, p5[0], "A", correct: true),
                Answer(attempt.AttemptId, p5[1], "B", correct: false),
                Answer(attempt.AttemptId, p6[0], "A", correct: true),
                Answer(attempt.AttemptId, p6[1], "A", correct: true));
            await db.SaveChangesAsync();

            AttemptAnswer Answer(int attemptId, PoolPosition position, string label, bool correct) => new()
            {
                AttemptId = attemptId,
                ExamId = examId,
                ExamItemId = position.ExamItemId,
                ItemId = position.ItemId,
                ItemVersionId = position.ItemVersionId,
                SelectedLabel = label,
                IsCorrect = correct,
            };
        }

        // Verify the SQL views directly in the DB
        int b1TopicId, b2TopicId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var secView = await db.vw_UserSectionAccuracyAlls
                .Where(v => v.UserId == userId)
                .ToListAsync();
            Assert.Contains(secView, v => v.SectionId == sec1Id && v.Answered == 2 && v.Correct == 1);
            Assert.Contains(secView, v => v.SectionId == sec2Id && v.Answered == 2 && v.Correct == 2);

            var topicView = await db.vw_UserTopicAccuracies
                .Where(v => v.UserId == userId)
                .ToListAsync();

            // Two cells, not one - and both are the SAME topic (PoolFixture authors one reading topic),
            // so the only thing that separates them is the level. A view that grouped by topic alone
            // would report a single blended 3-of-4 row here and fail this count.
            Assert.Equal(2, topicView.Count);

            // The counts the retired category view reported on the same answers, cell for cell:
            // P5's two items 2 answered / 1 correct, P6's two 2 answered / 2 correct.
            var drillCell = Assert.Single(topicView, v => v.Answered == 2 && v.Correct == 1);
            var attemptCell = Assert.Single(topicView, v => v.Answered == 2 && v.Correct == 2);
            b1TopicId = drillCell.TopicId;
            b2TopicId = attemptCell.TopicId;
            Assert.Equal(b1TopicId, b2TopicId);
            Assert.Equal("B1", drillCell.CefrLevel);
            Assert.Equal("B2", attemptCell.CefrLevel);
            // The percentage is the view's, and it is the same division the retired view would give.
            Assert.Equal(50, drillCell.AccuracyPercent);
            Assert.Equal(100, attemptCell.AccuracyPercent);
        }

        // Call weakness analysis service
        var view = await service.GetWeaknessAnalysisAsync(userId);

        Assert.True(view.HasData);
        Assert.Equal(4, view.TotalAnswered);
        Assert.Equal(3, view.TotalCorrect);
        Assert.Equal(75, view.OverallAccuracyPercentage);
        Assert.Equal(1, view.GradedExamCount);
        Assert.Equal(0, view.CompletedPracticeCount);

        // Section 1: 50% accuracy (weakest)
        var sec1Result = Assert.Single(view.Sections, s => s.SectionId == sec1Id);
        Assert.Equal(2, sec1Result.Answered);
        Assert.Equal(1, sec1Result.Correct);
        Assert.Equal(50, sec1Result.AccuracyPercentage);
        Assert.True(sec1Result.IsWeakest);
        Assert.False(sec1Result.IsStrongest);

        // Section 2: 100% accuracy (strongest)
        var sec2Result = Assert.Single(view.Sections, s => s.SectionId == sec2Id);
        Assert.Equal(2, sec2Result.Answered);
        Assert.Equal(2, sec2Result.Correct);
        Assert.Equal(100, sec2Result.AccuracyPercentage);
        Assert.False(sec2Result.IsWeakest);
        Assert.True(sec2Result.IsStrongest);

        // Priority practice is the weakest section
        Assert.NotNull(view.PriorityPracticeSection);
        Assert.Equal(sec1Id, view.PriorityPracticeSection!.SectionId);

        // Frequently missed topics: the B1 cell is 50%, the B2 cell is 100% - weakest first, and the
        // numbers are the ones the view reported above rather than a second division.
        Assert.Equal(2, view.FrequentlyMissedTopics.Count);
        var weakestMissed = view.FrequentlyMissedTopics[0];
        Assert.Equal(b1TopicId, weakestMissed.TopicId);
        Assert.Equal(CefrLevel.B1, weakestMissed.CefrLevel);
        Assert.Equal(50, weakestMissed.AccuracyPercent);
        Assert.Equal(2, weakestMissed.Answered);
        Assert.Equal(1, weakestMissed.Correct);

        var strongestMissed = view.FrequentlyMissedTopics[1];
        Assert.Equal(b2TopicId, strongestMissed.TopicId);
        Assert.Equal(CefrLevel.B2, strongestMissed.CefrLevel);
        Assert.Equal(100, strongestMissed.AccuracyPercent);
    }

    [Fact]
    public async Task Recommendations_can_be_marked_resolved_and_drop_off_the_open_list()
    {
        var certCode = Unique("RECO");
        var (certId, _, _) = await SeedCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        int rec1Id;
        int rec2Id;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var r1 = new Recommendation
            {
                UserId = userId,
                CertId = certId,
                Text = "Review past continuous and present perfect.",
                Severity = "major",
                IsResolved = false,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            var r2 = new Recommendation
            {
                UserId = userId,
                CertId = certId,
                Text = "Practice reading comprehension with inference questions.",
                Severity = "info",
                IsResolved = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Recommendations.AddRange(r1, r2);
            await db.SaveChangesAsync();
            rec1Id = r1.RecId;
            rec2Id = r2.RecId;
        }

        // Initially both recommendations are open and returned
        var viewBefore = await service.GetWeaknessAnalysisAsync(userId);
        Assert.Equal(2, viewBefore.Recommendations.Count);
        Assert.Contains(viewBefore.Recommendations, r => r.RecId == rec1Id && !r.IsResolved);
        Assert.Contains(viewBefore.Recommendations, r => r.RecId == rec2Id && !r.IsResolved);

        // Mark rec1 as resolved
        var resolved = await service.ResolveRecommendationAsync(userId, rec1Id);
        Assert.True(resolved);

        // In DB, rec1 is now resolved
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var r1Db = await db.Recommendations.FindAsync(rec1Id);
            Assert.NotNull(r1Db);
            Assert.True(r1Db!.IsResolved);
        }

        // Weakness analysis now drops rec1 off the open list!
        var viewAfter = await service.GetWeaknessAnalysisAsync(userId);
        Assert.Single(viewAfter.Recommendations);
        Assert.DoesNotContain(viewAfter.Recommendations, r => r.RecId == rec1Id);
        Assert.Contains(viewAfter.Recommendations, r => r.RecId == rec2Id);
    }

    [Fact]
    public async Task ResolveRecommendation_throws_when_recommendation_belongs_to_another_user()
    {
        var certCode = Unique("ISOL");
        var (certId, _, _) = await SeedCertificateAsync(certCode);
        var user1 = await SeedLearnerAsync(certCode);
        var user2 = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        int recId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var r = new Recommendation
            {
                UserId = user1,
                CertId = certId,
                Text = "User 1 recommendation",
                Severity = "minor",
                IsResolved = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Recommendations.Add(r);
            await db.SaveChangesAsync();
            recId = r.RecId;
        }

        // User 2 attempts to resolve User 1's recommendation
        await Assert.ThrowsAsync<RecommendationNotFoundException>(() =>
            service.ResolveRecommendationAsync(user2, recId));
    }

    [Fact]
    public async Task Empty_accuracy_learner_still_receives_open_recommendations()
    {
        var certCode = Unique("EMPTYREC");
        var (certId, _, _) = await SeedCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        // Seed an open recommendation without any practice/exam answers
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            db.Recommendations.Add(new Recommendation
            {
                UserId = userId,
                CertId = certId,
                Text = "Focus on time management for reading.",
                Severity = "major",
                IsResolved = false,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var view = await service.GetWeaknessAnalysisAsync(userId);
        Assert.False(view.HasData); // No accuracy data from SQL views
        Assert.Single(view.Recommendations); // But recommendations are present!
        Assert.Equal("Focus on time management for reading.", view.Recommendations[0].Text);
    }

    [Fact]
    public async Task Recommendations_are_strictly_isolated_by_active_certificate()
    {
        var certCodeA = Unique("RECA");
        var certCodeB = Unique("RECB");
        var (certIdA, _, _) = await SeedCertificateAsync(certCodeA);
        var (certIdB, _, _) = await SeedCertificateAsync(certCodeB);
        var userId = await SeedLearnerAsync(certCodeA);
        var (service, learner) = CreateServices();

        // Add reco in Cert A and Cert B
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            db.Recommendations.AddRange(
                new Recommendation { UserId = userId, CertId = certIdA, Text = "Reco in Cert A", Severity = "minor", IsResolved = false, CreatedAt = DateTimeOffset.UtcNow },
                new Recommendation { UserId = userId, CertId = certIdB, Text = "Reco in Cert B", Severity = "minor", IsResolved = false, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        // When in Cert A, only Cert A reco appears
        var viewA = await service.GetWeaknessAnalysisAsync(userId);
        Assert.Single(viewA.Recommendations);
        Assert.Equal("Reco in Cert A", viewA.Recommendations[0].Text);

        // Switch to Cert B
        await learner.SwitchCertificateAsync(userId, certCodeB);
        var viewB = await service.GetWeaknessAnalysisAsync(userId);
        Assert.Single(viewB.Recommendations);
        Assert.Equal("Reco in Cert B", viewB.Recommendations[0].Text);
    }
    /// <summary>
    /// #271: multi-select and inline-completion answers reach the weakness page on the TOPIC axis. This
    /// used to run through <c>QuestionCategories</c> and <c>vw_UserCategoryAccuracy</c>; the pool carries
    /// no category, so the cells are <c>Items.TopicId/CefrLevel</c> and the same two answers must land
    /// in one cell.
    /// <para>
    /// <b>The wrong input this catches:</b> a view that only counts labelled MCQ answers. An
    /// inline-completion answer records <c>AcceptedAnswersJson</c> and no key, and a multi-select answer
    /// records a key set; a reader that assumed a single <c>KeyLabel</c> would report fewer answered
    /// than were graded, so the figures here are asserted against the answers actually written.
    /// </para>
    /// <para>
    /// It is a graded attempt rather than a practice session because a pool PRACTICE answer has no
    /// section (<c>pa.SectionId IS NOT NULL</c> is the section view's filter), so it could not be
    /// compared against a section figure at all. The cell is the same either way.
    /// </para>
    /// </summary>
    [Fact]
    public async Task WeaknessAnalysis_includes_multi_select_and_inline_completion_answers()
    {
        var certCode = Unique("NC");
        int secId, examId, topicId;
        int multiItemId, multiVersionId, inlineItemId, inlineVersionId;

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var sec = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P6", Name = "Mixed Sec", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1 };
            db.Sections.Add(sec);
            await db.SaveChangesAsync();
            secId = sec.SectionId;

            var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 60, Purpose = "mock" };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();
            examId = exam.ExamId;

            var topic = new Topic { Modality = "reading", Code = "R-COH", Name = "Cohesion" };
            db.Topics.Add(topic);
            await db.SaveChangesAsync();
            topicId = topic.TopicId;

            // multi_select: four options, a key set.
            var multi = new Item
            {
                Modality = "reading", TopicId = topicId, CefrLevel = CefrLevel.B1,
                ItemType = "passage_comprehension", RenderFormat = "multi_select",
                ContentTier = "demo", LifecycleStatus = "active",
            };
            // inline_completion: no options and no key label; its answer set is AcceptedAnswersJson.
            var inline = new Item
            {
                Modality = "reading", TopicId = topicId, CefrLevel = CefrLevel.B1,
                ItemType = "sentence_cloze", RenderFormat = "inline_completion",
                ContentTier = "demo", LifecycleStatus = "active",
            };
            db.Items.AddRange(multi, inline);
            await db.SaveChangesAsync();
            multiItemId = multi.ItemId;
            inlineItemId = inline.ItemId;

            var multiVersion = new ItemVersion
            {
                ItemId = multi.ItemId, VersionNumber = 1, Stem = "Multi select q1",
                OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d",
                KeyLabel = "A,C", MaxSelections = 2,
            };
            var inlineVersion = new ItemVersion
            {
                ItemId = inline.ItemId, VersionNumber = 1, Stem = "Inline completion q2",
                AcceptedAnswersJson = "[\"telecommunications\"]", MinWords = 1, MaxWords = 2,
            };
            db.ItemVersions.AddRange(multiVersion, inlineVersion);
            await db.SaveChangesAsync();
            multiVersionId = multiVersion.ItemVersionId;
            inlineVersionId = inlineVersion.ItemVersionId;

            db.ExamItems.AddRange(
                new ExamItem
                {
                    ExamId = exam.ExamId, CertId = cert.CertId, SectionId = sec.SectionId,
                    ItemId = multi.ItemId, ItemVersionId = multiVersion.ItemVersionId,
                    Modality = "reading", DisplayOrder = 1,
                },
                new ExamItem
                {
                    ExamId = exam.ExamId, CertId = cert.CertId, SectionId = sec.SectionId,
                    ItemId = inline.ItemId, ItemVersionId = inlineVersion.ItemVersionId,
                    Modality = "reading", DisplayOrder = 2,
                });
            await db.SaveChangesAsync();
        }

        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        // A graded attempt: 2 answers, 1 correct (multi_select), 1 incorrect (inline_completion).
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var attempt = new ExamAttempt
            {
                UserId = userId,
                CertId = (await db.Sections.Where(s => s.SectionId == secId).Select(s => s.CertId).SingleAsync()),
                ExamId = examId,
                Status = "graded",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                TotalScore = 50,
            };
            db.ExamAttempts.Add(attempt);
            await db.SaveChangesAsync();

            var multiPosition = await db.ExamItems.SingleAsync(i => i.ExamId == examId && i.ItemId == multiItemId);
            var inlinePosition = await db.ExamItems.SingleAsync(i => i.ExamId == examId && i.ItemId == inlineItemId);
            var attemptId = attempt.AttemptId;

            db.AttemptAnswers.AddRange(
                new AttemptAnswer
                {
                    AttemptId = attemptId, ExamId = examId,
                    ExamItemId = multiPosition.ExamItemId, ItemId = multiItemId, ItemVersionId = multiVersionId,
                    SelectedOptionIdsJson = "[\"A\",\"C\"]", IsCorrect = true,
                },
                new AttemptAnswer
                {
                    AttemptId = attemptId, ExamId = examId,
                    ExamItemId = inlinePosition.ExamItemId, ItemId = inlineItemId, ItemVersionId = inlineVersionId,
                    ResponseText = "telecommunictions", IsCorrect = false,
                });
            await db.SaveChangesAsync();
        }

        var view = await service.GetWeaknessAnalysisAsync(userId);
        Assert.True(view.HasData);
        Assert.Equal(2, view.TotalAnswered);
        Assert.Equal(1, view.TotalCorrect);
        Assert.Equal(1, view.GradedExamCount);

        var sectionStats = Assert.Single(view.Sections, s => s.SectionId == secId);
        Assert.Equal(2, sectionStats.Answered);
        Assert.Equal(1, sectionStats.Correct);
        Assert.Equal(50, sectionStats.AccuracyPercentage);

        // Both render formats sat in ONE cell, so the topic axis reports one row holding both answers -
        // the same two figures the section reported.
        var missedCell = Assert.Single(view.FrequentlyMissedTopics);
        Assert.Equal(topicId, missedCell.TopicId);
        Assert.Equal(CefrLevel.B1, missedCell.CefrLevel);
        Assert.Equal(2, missedCell.Answered);
        Assert.Equal(1, missedCell.Correct);
        Assert.Equal(50, missedCell.AccuracyPercent);
    }
}
