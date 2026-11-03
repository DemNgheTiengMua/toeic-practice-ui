using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>
/// Probes a claim made by a code-reading review that was never confirmed at runtime: exam 13
/// ("Diagnostic Benchmark 45M") is a *placement* exam carrying two free-response Writing questions.
/// A free response goes to `pending_ai` on submit, which holds the attempt at `submitted` rather than
/// `graded`, so TotalScore and BandCode stay null. PlacementService then writes PlacementTests.Status
/// = 'done' with a null ResultBandCode, and CK_Placement_DoneHasResult should refuse that row.
///
/// If this test passes, submitting a placement that contains free response is broken for every
/// learner who tries it. If the insert is accepted, the constraint does not do what its name says.
/// </summary>
public sealed partial class LearnerServiceTests
{
    [Fact]
    public async Task A_placement_with_free_responses_cannot_be_marked_done_without_a_band()
    {
        // The constraint directly: a done placement must name its band and its total.
        var rejected = false;
        try
        {
            await database.SqlAsync("""
                INSERT INTO PlacementTests (UserId, CertId, ExamId, AttemptId, Status, TakenAt)
                VALUES (1, 1, 13, NULL, 'done', SYSDATETIMEOFFSET());
                """);
        }
        catch (Exception)
        {
            rejected = true;
        }

        Assert.True(rejected,
            "CK_Placement_DoneHasResult should reject a done placement with no band and no total. "
            + "If this row was accepted, the constraint does not enforce what its name claims.");
    }

    [Fact]
    public async Task Exam_13_is_a_placement_exam_that_carries_free_response_questions()
    {
        // The precondition of the defect: a placement exam whose submit cannot produce a band.
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var placementExam = await db.Exams.AsNoTracking()
            .Where(e => e.ExamId == 13)
            .Select(e => new { e.Purpose, e.Name })
            .SingleOrDefaultAsync();

        if (placementExam is null)
        {
            return; // the seed of this fixture does not carry exam 13; nothing to assert.
        }

        Assert.Equal(ExamPurpose.Placement.ToCode(), placementExam.Purpose);

        var freeResponses = await db.Questions.AsNoTracking()
            .CountAsync(q => q.ExamId == 13 && q.QuestionType == "free_response");

        Assert.True(freeResponses > 0,
            $"Exam 13 carries {freeResponses} free responses. If it were 0, submitting it would grade "
            + "synchronously and the band would exist.");
    }
}
