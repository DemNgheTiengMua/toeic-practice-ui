using System.Text.RegularExpressions;

namespace WebMVC.E2ETests.Journeys;

/// <summary>
/// #134: a placement sitting keeps the mock exam's one-way locks and still carries the candidate
/// from one locked part into the next. The standard TOEIC placement (exam 2) is Q1–Q2 Listening
/// (AUDIO_DRIVEN) and Q3–Q4 Reading (FULL_SANDBOX); the Diagnostic Benchmark (exam 14) ends with
/// two writing tasks, Q31 (W2) and Q32 (W3), each its own HYBRID_GROUP_LOCKED group.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class PlacementSkillTransitionTests : PlaywrightTestBase
{
    [Test]
    public async Task Should_Keep_Placement_One_Way_And_Carry_The_Candidate_From_Listening_Into_Reading()
    {
        var placementId = await StartPlacementAsync(examId: 2);

        var sitting = Page.Locator("#placement-take-dynamic");
        var number = sitting.Locator(".question__number");
        var next = sitting.GetByRole(AriaRole.Link, new() { Name = "Next question" });
        var previous = sitting.GetByRole(AriaRole.Link, new() { Name = "Previous" });

        // Listening is AUDIO_DRIVEN: no Previous control, and a question locks once you move on.
        await Expect(number).ToHaveTextAsync(new Regex(@"Question 1\b", RegexOptions.IgnoreCase));
        await Expect(previous).ToHaveCountAsync(0);
        await next.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?q=2$"));
        await Expect(number).ToHaveTextAsync(new Regex(@"Question 2\b", RegexOptions.IgnoreCase));
        await Expect(previous).ToHaveCountAsync(0);
        var firstCell = sitting.Locator(".navigator__grid [id^='nav-cell-']").First;
        await Expect(firstCell).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.That(await firstCell.EvaluateAsync<string>("cell => cell.tagName"), Is.EqualTo("SPAN"));

        // A hand-edited rewind is refused, and the address bar shows the question on screen.
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q=1");
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?q=2$"));
        await Expect(number).ToHaveTextAsync(new Regex(@"Question 2\b", RegexOptions.IgnoreCase));

        // Answer the last Listening question and move straight on: the sitting confirms leaving
        // the skill, then opens Reading without a reload.
        var dialogs = new List<string>();
        Page.Dialog += (_, dialog) => dialogs.Add(dialog.Message);
        await sitting.Locator(".option").First.ClickAsync();
        await next.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?q=3$"));
        await Expect(number).ToHaveTextAsync(new Regex(@"Question 3\b", RegexOptions.IgnoreCase));
        Assert.That(dialogs, Has.Count.EqualTo(1));
        Assert.That(dialogs[0], Does.Contain("Listening").And.Contain("Reading"));

        // Listening is now closed: no Previous into it, and a hand-edited rewind is refused.
        await Expect(previous).ToHaveCountAsync(0);
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q=2");
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?q=3$"));

        // Reading is FULL_SANDBOX: free to move inside it, with no further confirmation.
        await next.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?q=4$"));
        await Expect(previous).ToHaveAttributeAsync("href", $"/placement/take/{placementId}?q=3");
        Assert.That(dialogs, Has.Count.EqualTo(1));

        // The answer given right before leaving Listening was saved, not refused by the closed skill.
        await using var check = await OpenAppDatabaseAsync();
        await using var answered = check.CreateCommand();
        answered.CommandText = """
            SELECT COUNT(*) FROM AttemptAnswers a
            JOIN PlacementTests p ON p.AttemptId = a.AttemptId
            WHERE p.PlacementId = @placementId AND a.SelectedOptionId IS NOT NULL;
            """;
        answered.Parameters.AddWithValue("@placementId", int.Parse(placementId));
        Assert.That(Convert.ToInt32(await answered.ExecuteScalarAsync()), Is.EqualTo(1));
    }

    [Test]
    public async Task Should_Carry_The_Candidate_From_The_First_Writing_Task_Into_The_Second()
    {
        var placementId = await StartPlacementAsync(examId: 14);

        var sitting = Page.Locator("#placement-take-dynamic");
        var number = sitting.Locator(".question__number");
        var next = sitting.GetByRole(AriaRole.Link, new() { Name = "Next question" });
        var previous = sitting.GetByRole(AriaRole.Link, new() { Name = "Previous" });

        // Asking for the first question of the next skill opens it, so two jumps reach Writing
        // without clicking through the 30 Listening and Reading questions.
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q=13");
        await Expect(number).ToHaveTextAsync(new Regex(@"Question 13\b", RegexOptions.IgnoreCase));
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q=31");
        await Expect(number).ToHaveTextAsync(new Regex(@"Question 31\b", RegexOptions.IgnoreCase));

        // Write the first task and move on: the sitting confirms leaving the question group, saves
        // the essay, then opens the second task without a reload.
        var dialogs = new List<string>();
        Page.Dialog += (_, dialog) => dialogs.Add(dialog.Message);
        await sitting.Locator("#writing-editor").FillAsync("An essay for the first writing task.");
        await next.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?q=32$"));
        await Expect(number).ToHaveTextAsync(new Regex(@"Question 32\b", RegexOptions.IgnoreCase));
        Assert.That(dialogs, Has.Count.EqualTo(1));
        Assert.That(dialogs[0], Does.Contain("question group"));

        // The first task is closed: no Previous into it, and a hand-edited rewind is refused.
        await Expect(previous).ToHaveCountAsync(0);
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q=31");
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?q=32$"));

        // The essay written before the group closed was saved.
        await using var check = await OpenAppDatabaseAsync();
        await using var essay = check.CreateCommand();
        essay.CommandText = """
            SELECT f.ResponseText FROM FreeResponses f
            JOIN PlacementTests p ON p.AttemptId = f.AttemptId
            JOIN Questions q ON q.QuestionId = f.QuestionId
            JOIN Sections s ON s.SectionId = q.SectionId
            WHERE p.PlacementId = @placementId AND s.Code = 'W2';
            """;
        essay.Parameters.AddWithValue("@placementId", int.Parse(placementId));
        Assert.That(await essay.ExecuteScalarAsync(), Is.EqualTo("An essay for the first writing task."));
    }

    /// <summary>Seeds a TOEIC learner with no history, signs in, starts the placement exam and
    /// passes preflight. Returns the placement id.</summary>
    private async Task<string> StartPlacementAsync(int examId)
    {
        var email = $"placement-parts-{Guid.NewGuid():N}@demo.local";
        await using (var connection = await OpenAppDatabaseAsync())
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
                SELECT RoleId, @email, PasswordHash, N'Placement part transition',
                    (SELECT CertId FROM Certificates WHERE Code = 'TOEIC')
                FROM Users WHERE Email = 'learner.fresh@demo.local';
                """;
            seed.Parameters.AddWithValue("@email", email);
            await seed.ExecuteNonQueryAsync();
        }

        await SignInAsync(email);
        await Page.GotoAsync($"{BaseUrl}/placement");
        await Page.Locator($"form[action='/placement/start']:has(input[name='examId'][value='{examId}'])")
            .GetByRole(AriaRole.Button).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"placementId=\d+"));
        var placementId = Regex.Match(Page.Url, @"placementId=(\d+)").Groups[1].Value;
        await Page.Locator("#start-test-btn").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($@"/placement/take/{placementId}"));
        return placementId;
    }
}
