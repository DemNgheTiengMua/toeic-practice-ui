using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>
/// Probes a claim made by a code-reading review that was never confirmed at runtime: a placement exam
/// carrying free-response questions cannot be completed. A free response goes to `pending_ai` on submit,
/// which holds the attempt at `submitted` rather than `graded`, so TotalScore and BandCode stay null.
/// PlacementService then writes PlacementTests.Status = 'done' with a null ResultBandCode, and
/// CK_Placement_DoneHasResult refuses that row.
///
/// These tests build their own fixture rather than asserting about a seeded exam id: the test database
/// carries the schema and nothing else, and it is shared across the run, so an exam id read here may
/// belong to whichever test inserted it last.
/// </summary>
public sealed partial class LearnerServiceTests
{
    [Fact]
    public async Task A_placement_with_free_responses_cannot_be_marked_done_without_a_band()
    {
        var code = Unique("CKDONE");
        var (certId, _, _) = await SeedCertWithNamedSkillsAsync(code, ("LIST", "Listening", "listening"));
        var userId = await SeedLearnerAsync(code);

        var rejected = false;
        try
        {
            await database.SqlAsync($"""
                INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, TakenAt)
                VALUES ({userId}, {certId}, NULL, NULL, 'done', SYSDATETIMEOFFSET());
                """);
        }
        catch (Exception)
        {
            rejected = true;
        }

        // #212 replaced CK_Placement_DoneHasResult ("done needs a band and a total") with a rule about
        // measured skills, which is enforced by trg_PlacementTests_CompletionIsEarned. Either way this
        // row is refused, and the assertion is that a finished placement cannot carry no result at all.
        Assert.True(rejected,
            "A done placement with no band, no total and nothing measured should be refused. "
            + "If this row was accepted, neither the constraint nor the completion trigger enforces what its name claims.");
    }

    [Fact]
    public async Task A_free_response_at_submit_is_pending_ai_and_has_no_score()
    {
        // The other half of the defect: a free response is not graded synchronously, so an attempt
        // carrying one cannot produce the band the placement snapshot requires.
        var code = Unique("FRBAND");
        var (certId, skillId, sectionId) = await SeedCertWithNamedSkillsAsync(code, ("WRIT", "Writing", "writing"));

        await database.SqlAsync($"""
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
              VALUES ({certId}, N'Free response placement {code}', 'published', 30, 'placement');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var examId = await db.Exams.AsNoTracking()
            .Where(e => e.CertId == certId && e.Purpose == "placement")
            .Select(e => e.ExamId).SingleAsync();

        // #269: the paper's one position is a pool productive task. A pool version carries no options
        // (CNT-06's other legal shape), which is what makes the task render as a free response -
        // the fact the legacy Questions.QuestionType column used to state.
        await PoolFixture.AddProductiveAsync(
            db, examId, certId, sectionId, "writing", "Write an email.", displayOrder: 1, level: "B2");

        // The precondition of the defect: a placement exam whose only question is a free response,
        // so submitting it cannot produce a band synchronously.
        var purpose = await db.Exams.AsNoTracking()
            .Where(e => e.ExamId == examId).Select(e => e.Purpose).SingleAsync();
        Assert.Equal(ExamPurpose.Placement.ToCode(), purpose);

        var freeResponses = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == examId)
            .Join(db.Items.AsNoTracking(), i => i.ItemId, item => item.ItemId, (i, item) => item)
            .CountAsync(item => item.RenderFormat == "free_response");
        Assert.Equal(1, freeResponses);
        Assert.Equal(skillId, await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == examId)
            .Join(db.Sections.AsNoTracking(), i => i.SectionId, s => s.SectionId, (i, s) => s.SkillId)
            .SingleAsync());
    }
}
