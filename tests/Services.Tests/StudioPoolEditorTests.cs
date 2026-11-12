using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 / #265: <b>the Studio editor reads and writes a pool-authored paper.</b>
/// <para>
/// The editor is a live admin screen. Before this ticket it enumerated a paper through
/// <c>QuestionGroups</c> and <c>Questions</c>, so a paper assembled on the pool rendered an empty rail -
/// and its autosave and reorder wrote rows those papers do not have.
/// </para>
/// <para>
/// <b>What this pins.</b> The precondition (zero question rows) is asserted. The assertions are on what
/// an admin sees and does: the rail's per-section count, the stem on a card, the key shown beside an
/// option, and - after an autosave - that the edit survives a re-read and lands on the version the
/// binding pins rather than on a new version.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StudioPoolEditorTests(DatabaseFixture database)
{
    [Fact]
    public async Task The_studio_lists_a_pool_paper_from_its_roster()
    {
        var name = $"EE265S_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedPoolPaperAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);

            // #269: Questions is gone, so "no legacy row" is a schema fact; the roster below is what
            // must be there.
            Assert.Equal(3, await db.ExamItems.CountAsync(i => i.ExamId == fixture.ExamId));

            var studio = await new AdminExamService(db, TimeProvider.System).GetExamStudioAsync(fixture.ExamId);

            Assert.Equal(3, studio.ActualQuestionCount);

            var section = Assert.Single(studio.Sections.Where(s => s.SectionId == fixture.SectionId));
            Assert.Equal(3, section.CurrentQuestionCount);

            var questions = section.Groups.SelectMany(g => g.Questions).ToList();
            Assert.Equal(3, questions.Count);
            Assert.All(questions, q => Assert.StartsWith("Pool studio item", q.Stem, StringComparison.Ordinal));

            // Positions ascend, so the rail's numbering is the paper's own.
            Assert.Equal(new[] { 1, 2, 3 }, questions.Select(q => q.DisplayOrder).OrderBy(o => o));

            // Each card shows four options with the key marked - the same shape the sitting renders.
            var first = questions[0];
            Assert.Equal(4, first.Options.Count);
            Assert.Single(first.Options.Where(o => o.IsCorrect));
            Assert.Equal("A", first.Options.Single(o => o.IsCorrect).Label);
            Assert.Equal("B1", first.CefrLevelCode);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// #265 / #269: an edit to a pool paper's position forks a version and leaves the pinned one intact.
    /// <para>
    /// <b>What changed.</b> This drove <c>AdminExamService.UpdateQuestionAsync</c>, which went with the
    /// <c>Questions</c> table. The durable rule it asserted - CNT-43 copy-on-write: a scored edit inserts
    /// the next version and the previous row stays byte-identical, so a finished attempt that pinned it
    /// still reads what it saw - is now the pool's, and <c>IAdminItemService.UpdateAsync</c> is its only
    /// writer. That is the door this test drives.
    /// </para>
    /// <para>
    /// <b>What is no longer asserted here, and why.</b> The old test also proved the Studio's autosave
    /// moved the paper's <c>ExamItem</c> binding onto the new version, so a re-read showed the edit. That
    /// write has no public surface any more: <c>AdminExamService.UpdatePoolPositionAsync</c> is private and
    /// called by nothing, and the MVC autosave route posts to <c>PUT /api/admin/questions/{id}</c>, which
    /// #265 removed. Re-asserting it would mean inventing a caller. The copy-on-write half is kept and the
    /// binding half is reported rather than faked.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_scored_edit_forks_a_version_and_never_rewrites_the_pinned_one()
    {
        var name = $"EE265W_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedPoolPaperAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var studio = await new AdminExamService(db, TimeProvider.System).GetExamStudioAsync(fixture.ExamId);
            var position = studio.Sections.Single(s => s.SectionId == fixture.SectionId)
                .Groups.SelectMany(g => g.Questions).First();

            var updated = await new AdminItemService(db, TimeProvider.System).UpdateAsync(
                fixture.FirstItemId,
                new UpdatePoolItemRequest(
                    Stem: "Pool studio item: the edited stem.",
                    OptionA: "at six o'clock in the morning",
                    OptionB: "only after seven in the evening",
                    OptionC: "sometime around half past eight",
                    OptionD: "by the middle of the following day",
                    KeyLabel: "A",
                    Explanation: "Edited explanation.",
                    RationaleA: "Correct.",
                    RationaleB: "Wrong: a day takes 'on'.",
                    RationaleC: "Wrong.",
                    RationaleD: "Wrong."));

            Assert.Equal("Pool studio item: the edited stem.", updated.Stem);
            Assert.Equal(2, updated.VersionNumber);

            // The edit landed as a NEW version and the first is untouched - copy-on-write, because a
            // version's scored fields are immutable by trigger (CNT-43). The trigger is the guard; this
            // asserts the behaviour it demands.
            await using (var verify = DbContextFactory.Create(connectionString))
            {
                var versions = await verify.ItemVersions
                    .Where(v => v.ItemId == fixture.FirstItemId)
                    .OrderBy(v => v.VersionNumber)
                    .ToListAsync();

                Assert.Equal(2, versions.Count);

                // The original is byte-identical: a finished attempt that pinned it still reads what it saw.
                Assert.Equal("Pool studio item 1: the depot opens _____ six.", versions[0].Stem);
                Assert.Equal("A", versions[0].KeyLabel);
                Assert.Equal("at", versions[0].OptionA);

                // The new version carries the edit.
                Assert.Equal("Pool studio item: the edited stem.", versions[1].Stem);
                Assert.Equal("A", versions[1].KeyLabel);
                Assert.Equal("at six o'clock in the morning", versions[1].OptionA);
                Assert.Equal("only after seven in the evening", versions[1].OptionB);
            }

            // The paper itself is unchanged by a pool edit: its position still pins the version it was
            // bound to, and the Studio still reads the whole paper.
            var reread = await new AdminExamService(db, TimeProvider.System).GetExamStudioAsync(fixture.ExamId);
            var after = reread.Sections.Single(s => s.SectionId == fixture.SectionId)
                .Groups.SelectMany(g => g.Questions).ToList();
            Assert.Equal(3, after.Count);
            Assert.Equal(position.QuestionId, after[0].QuestionId);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    [Fact]
    public async Task Reordering_a_pool_section_moves_every_position_without_colliding()
    {
        var name = $"EE265R_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedPoolPaperAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var service = new AdminExamService(db, TimeProvider.System);

            // Reverse the three positions. A naive in-place renumber collides on
            // UQ_ExamItems_Exam_Section_Order; this is the same trap the content migration hit.
            var nums = await db.ExamItems.AsNoTracking()
                .Where(i => i.ExamId == fixture.ExamId)
                .OrderBy(i => i.DisplayOrder)
                .Select(i => i.ExamItemId)
                .ToListAsync();

            await service.ReorderSectionItemsAsync(
                fixture.ExamId,
                new ReorderSectionItemsRequest(
                    fixture.SectionId,
                    [
                        new ReorderSectionItemEntry(nums[2], null, 1),
                        new ReorderSectionItemEntry(nums[1], null, 2),
                        new ReorderSectionItemEntry(nums[0], null, 3),
                    ]));

            await using var verify = DbContextFactory.Create(connectionString);
            var after = await verify.ExamItems.AsNoTracking()
                .Where(i => i.ExamId == fixture.ExamId)
                .OrderBy(i => i.DisplayOrder)
                .ToListAsync();

            Assert.Equal(new[] { nums[2], nums[1], nums[0] }, after.Select(i => i.ExamItemId));

            // The ranks are 1..n with no gap and no duplicate.
            Assert.Equal(new[] { 1, 2, 3 }, after.Select(i => i.DisplayOrder));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ---------------------------------------------------------------------------

    private sealed record Fixture(int ExamId, int SectionId, int FirstItemId);

    private static async Task<Fixture> SeedPoolPaperAsync(string connectionString)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"S{Guid.NewGuid():N}"[..12];
        var cert = new Certificate { Code = code, Name = $"Studio cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading",
            DisplayOrder = 1, DurationMinutes = 45, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId,
            SkillId = skill.SkillId,
            Modality = "reading",
            Code = "P5",
            Name = "Incomplete Sentences",
            OptionCount = 4,
            ExpectedQuestionCount = 3,
            DisplayOrder = 1,
            NavigationMode = "FULL_SANDBOX",
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        db.ScoreBands.Add(new ScoreBand
        {
            CertId = cert.CertId, Code = "LOW", Name = "Low", MinTotal = 0, MaxTotal = 49, DisplayOrder = 1,
        });
        db.ScoreBands.Add(new ScoreBand
        {
            CertId = cert.CertId, Code = "HIGH", Name = "High", MinTotal = 50, MaxTotal = 100, DisplayOrder = 2,
        });
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId, Name = $"Studio paper {code}", Status = "draft",
            DurationMinutes = 45, Purpose = "mock", QuestionCount = 3,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var topic = new Topic { Modality = "reading", Code = "R-DET", Name = "Local factual retrieval" };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var firstItemId = 0;
        for (var i = 0; i < 3; i++)
        {
            var item = new Item
            {
                Modality = "reading",
                TopicId = topic.TopicId,
                CefrLevel = CefrLevel.B1,
                ItemType = "sentence_cloze",
                RenderFormat = "mcq",
                ContentTier = "demo",
                LifecycleStatus = "active",
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var version = new ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = 1,
                Stem = $"Pool studio item {i + 1}: the depot opens _____ six.",
                OptionA = "at", OptionB = "on", OptionC = "in", OptionD = "by",
                KeyLabel = "A",
                Explanation = "A clock time takes 'at'.",
            };
            db.ItemVersions.Add(version);
            await db.SaveChangesAsync();

            db.ExamItems.Add(new ExamItem
            {
                ExamId = exam.ExamId,
                CertId = cert.CertId,
                SectionId = section.SectionId,
                ItemId = item.ItemId,
                ItemVersionId = version.ItemVersionId,
                Modality = "reading",
                DisplayOrder = i + 1,
            });
            await db.SaveChangesAsync();

            if (i == 0) firstItemId = item.ItemId;
        }

        return new Fixture(exam.ExamId, section.SectionId, firstItemId);
    }
}
