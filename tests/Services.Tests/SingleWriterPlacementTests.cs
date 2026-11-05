using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>
/// #191 / D14: the regression test for the confirmed defect in
/// docs/research/placement-free-response-defect.md, driven through the real services.
///
/// The defect was that `AttemptEngine` and `PlacementService` both wrote the placement snapshot, so a
/// placement carrying a free response was marked `done` with a null band and the learner could not
/// finish it. The schema test proves the database refuses a second writer; this proves the services no
/// longer attempt one, which is the half a trigger cannot show.
/// </summary>
public sealed partial class LearnerServiceTests
{
    /// <summary>
    /// A placement whose only question is a free response: submitting it cannot produce a band
    /// synchronously. Under the old two-writer arrangement this threw, because PlacementService wrote
    /// `done` with a null ResultBandCode and CK_Placement_DoneHasResult refused it. Now the placement
    /// stays in progress until its grading settles, and the learner can still read the page.
    /// </summary>
    [Fact]
    public async Task A_placement_with_a_free_response_stays_in_progress_instead_of_crashing_on_submit()
    {
        var code = Unique("SW191");
        var (certId, _, _) = await SeedCertWithNamedSkillsAsync(code, ("WRIT", "Writing", "writing"));
        var userId = await SeedLearnerAsync(code);
        var q = await SeedOneQuestionPlacementAsync(certId, code, "free_response");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var placementService = new PlacementService(db, new LearnerService(db));

        var started = await placementService.StartPlacementTestAsync(userId, q.ExamId);
        await placementService.SaveDraftAsync(userId, started.PlacementId, q.QuestionId, "A complete written response.");

        // The act that used to crash: submit, and the attempt goes `submitted` because the free
        // response is pending AI. No exception, and the placement is NOT falsely marked done.
        await using var submitDb = DbContextFactory.Create(database.ConnectionString);
        var submitService = new PlacementService(submitDb, new LearnerService(submitDb));
        var result = await submitService.SubmitPlacementTestAsync(userId, started.PlacementId, isAutoSubmit: false);

        // the view says "not graded yet", and the stored row agrees
        Assert.False(result.IsGraded);
        Assert.Equal(1, result.FreeResponseCount);
        Assert.Equal(0, result.GradedFreeResponseCount);

        await using var verifyDb = DbContextFactory.Create(database.ConnectionString);
        var placement = await verifyDb.PlacementTests.AsNoTracking()
            .SingleAsync(p => p.PlacementId == started.PlacementId);
        Assert.Equal(PlacementStatus.InProgress.ToCode(), placement.Status);
        // Nobody claimed the snapshot, because there is no result to derive it from yet
        Assert.Null(placement.WrittenByService);
        Assert.Equal(0, placement.ProjectionVersion);
    }

    /// <summary>
    /// The other half: the placement is not written beside the attempt, and it is not left behind
    /// either. When the objective part scores, the attempt engine derives the snapshot and names
    /// itself as the writer. A placement result that nobody wrote is as wrong as two who did.
    /// </summary>
    [Fact]
    public async Task A_scored_placement_names_the_attempt_engine_as_the_one_writer()
    {
        var code = Unique("SW191");
        var (certId, _, _) = await SeedCertWithNamedSkillsAsync(code, ("LIST", "Listening", "listening"));
        var userId = await SeedLearnerAsync(code);
        var q = await SeedOneQuestionPlacementAsync(certId, code, "mcq");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new PlacementService(db, new LearnerService(db));
        var started = await service.StartPlacementTestAsync(userId, q.ExamId);

        await using var answerDb = DbContextFactory.Create(database.ConnectionString);
        var answerService = new PlacementService(answerDb, new LearnerService(answerDb));
        await answerService.SaveAnswerAsync(userId, started.PlacementId, q.QuestionId, q.CorrectOptionId, isFlagged: false);
        var result = await answerService.SubmitPlacementTestAsync(userId, started.PlacementId, isAutoSubmit: false);

        Assert.True(result.IsGraded);
        // scaled 300 lands in the HIGH band (250-500), which is what the fixture's own bands say
        Assert.Equal("HIGH", result.ResultBandCode);

        await using var verifyDb = DbContextFactory.Create(database.ConnectionString);
        var placement = await verifyDb.PlacementTests.AsNoTracking()
            .SingleAsync(p => p.PlacementId == started.PlacementId);
        Assert.Equal(DerivedFactWriters.PlacementResult, placement.WrittenByService);
        Assert.True(placement.ProjectionVersion > 0);
    }
}
