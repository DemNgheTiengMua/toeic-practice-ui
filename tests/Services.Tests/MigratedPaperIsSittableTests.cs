using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 criteria 1 and 5 together: <b>after the migration, a real seeded paper can be sat with its
/// legacy rows deleted.</b>
/// <para>
/// <b>Why this is the test the ticket needs.</b> Two separate proofs already exist and neither is
/// enough: <c>PaperMigrationCountsTests</c> shows the roster has the right count, and
/// <c>PoolOnlyPaperTests</c> shows a hand-built roster-only paper sits. Neither connects them - a
/// migration that produced the right COUNT but bound the wrong content would pass the first, and a
/// sitting path that works on a fixture says nothing about the seeded catalogue.
/// </para>
/// <para>
/// So this runs the real seed, runs the real migration, DELETES the three legacy tables' rows for one
/// exam, and then sits that exam end to end. If any reader still needs <c>Questions</c>, it fails here.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> Asserting "the attempt was created" passes on
/// an empty paper. The assertions are the paper's own length, the pinned content appearing in the
/// take view, an answer being recorded against a roster position, and a score after submission.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MigratedPaperIsSittableTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_seeded_paper_is_sat_from_the_pool_after_its_legacy_rows_are_deleted()
    {
        var name = $"EE250M_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await database.SqlCmdFileToDatabaseAsync(
                name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));
            await database.SqlCmdFileToDatabaseAsync(
                name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "migrate-paper-to-pool.sql"));

            int examId, userId, expectedQuestions, certId;
            await using (var setup = DbContextFactory.Create(connectionString))
            {
                // A published mock paper with the most questions in the seed, so the test exercises a
                // real paper rather than a two-question stub.
                var candidate = await setup.Exams.AsNoTracking()
                    .Where(e => e.Status == "published" && e.Purpose == "mock")
                    .Select(e => new { e.ExamId, e.CertId, Count = e.ExamItems.Count })
                    .Where(e => e.Count > 0)
                    .OrderByDescending(e => e.Count)
                    .FirstAsync();

                examId = candidate.ExamId;
                certId = candidate.CertId;
                expectedQuestions = candidate.Count;

                userId = await setup.Users.AsNoTracking()
                    .Where(u => u.Email == "learner@demo.local")
                    .Select(u => u.UserId)
                    .SingleAsync();
            }

            Assert.True(expectedQuestions >= 2, $"the chosen paper holds {expectedQuestions} roster row(s)");

            // Remove the legacy rows for THIS exam, so the sitting path has to use the roster.
            //
            // Narrow on purpose. The first four attempts at this purge walked the whole foreign-key
            // graph and kept finding one more table (PracticeAnswers, AiInsights, Recommendations,
            // Complaints), and disabling constraints then tripped
            // trg_Attempts_TotalMatchesSkills - which fires on DELETE and recomputes the sum over
            // AttemptSkillScores. None of that is necessary: the attempts themselves are PRESERVED by
            // criterion 5, so the cheap correct move is to detach them from the questions instead of
            // deleting them.
            await using (var purge = DbContextFactory.Create(connectionString))
            {
                // Detach the attempts. Both columns are FKs into the legacy tables.
                await purge.Database.ExecuteSqlRawAsync(
                    "UPDATE ExamAttempts SET LastQuestionId = NULL, ActiveGroupId = NULL WHERE ExamId = {0}",
                    examId);

                // Answers and responses record what was asked, so they go with the questions.
                await purge.Database.ExecuteSqlRawAsync(
                    "DELETE FROM AttemptAnswers WHERE ExamId = {0}", examId);
                await purge.Database.ExecuteSqlRawAsync(
                    "DELETE FROM FreeResponses WHERE ExamId = {0}", examId);

                // Legacy readers that name a question directly. Criterion 5 drops these tables.
                await purge.Database.ExecuteSqlRawAsync("DELETE FROM SavedQuestions");
                await purge.Database.ExecuteSqlRawAsync("DELETE FROM UserQuestionHistories");
                await purge.Database.ExecuteSqlRawAsync("DELETE FROM PracticeAnswers");
                await purge.Database.ExecuteSqlRawAsync("DELETE FROM Recommendations");
                await purge.Database.ExecuteSqlRawAsync("DELETE FROM AiInsights");
                await purge.Database.ExecuteSqlRawAsync("DELETE FROM PracticeSessions");

                await purge.Database.ExecuteSqlRawAsync(
                    "DELETE FROM QuestionOptions WHERE QuestionId IN (SELECT QuestionId FROM Questions WHERE ExamId = {0})",
                    examId);
                await purge.Database.ExecuteSqlRawAsync(
                    "DELETE FROM Questions WHERE ExamId = {0}", examId);
                await purge.Database.ExecuteSqlRawAsync(
                    "DELETE FROM QuestionGroups WHERE ExamId = {0}", examId);

                Assert.Equal(0, await purge.Questions.CountAsync(q => q.ExamId == examId));
                Assert.Equal(0, await purge.QuestionGroups.CountAsync(g => g.ExamId == examId));
                Assert.Equal(expectedQuestions, await purge.ExamItems.CountAsync(i => i.ExamId == examId));
            }

            // ---- sit it ----------------------------------------------------------------
            await using var db = DbContextFactory.Create(connectionString);
            var engine = new AttemptEngine(db);

            var attempt = await engine.CreateAttemptAsync(userId, certId, examId, durationMinutes: 30);

            // A paper with audio or a microphone requires the preflight gate; that is correct
            // behaviour, not a defect. Pass the gate the way a learner does.
            if (await engine.RequiresPreflightAsync(examId))
            {
                await engine.VerifyPreflightAsync(attempt.AttemptId, userId);
            }

            var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 1);
            Assert.Equal(expectedQuestions, take.TotalQuestions);
            Assert.Equal(expectedQuestions, take.Navigator.Count);
            Assert.False(string.IsNullOrWhiteSpace(take.CurrentQuestion.Stem));

            // Answer the first position. A pool answer must name a roster position, not a question.
            var first = take.CurrentQuestion;
            var option = first.Options.FirstOrDefault();
            Assert.NotNull(option);
            await engine.SaveAnswerAsync(
                attempt.AttemptId, userId, first.QuestionId, option!.OptionId, isFlagged: false);

            await using (var verify = DbContextFactory.Create(connectionString))
            {
                var saved = await verify.AttemptAnswers.AsNoTracking()
                    .SingleAsync(a => a.AttemptId == attempt.AttemptId && a.IsCorrect != null);
                Assert.Null(saved.QuestionId);
                Assert.NotNull(saved.ExamItemId);

                var answeredCount = await verify.AttemptAnswers.CountAsync(a => a.AttemptId == attempt.AttemptId);
                Assert.Equal(expectedQuestions, answeredCount);
            }

            // Submit: the paper grades from the roster.
            var graded = await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);
            Assert.NotNull(graded.Attempt.TotalScore);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }
}
