using System.Text.RegularExpressions;

namespace WebMVC.E2ETests.Journeys;

/// <summary>
/// #134/#169: a placement sitting keeps the mock exam's one-way locks and still carries the candidate
/// from one locked part into the next.
///
/// The seed no longer ships the old four-question "Standard" exam (#156). It ships a single
/// recommended diagnostic, <c>TOEIC Placement Test — Diagnostic</c> (24 questions: Listening Q1–Q10
/// in four AUDIO_DRIVEN sections, then Reading Q11–Q24 in three FULL_SANDBOX ones), and a second,
/// larger <c>Diagnostic Benchmark 45M</c> (32 questions: Listening Q1–Q12, Reading Q13–Q30, then two
/// HYBRID_GROUP_LOCKED writing tasks at Q31 and Q32). Both are looked up by name, so a reseeded id
/// cannot quietly point the journey at the wrong exam.
///
/// The boundaries the current sitting engine enforces are the same three the old journey drove, just
/// at different question numbers: the last Listening question of each exam (a skill advance, which
/// confirms leaving Listening), and the first writing task of the benchmark (a locked question group,
/// which confirms leaving the group). This fixture walks those rather than a listening→reading pair
/// that no longer sits at Q2→Q3.
/// </summary>
[NonParallelizable]
[TestFixture]
public class PlacementSkillTransitionTests : PlaywrightTestBase
{
    [Test]
    public async Task Should_Keep_Placement_One_Way_And_Carry_The_Candidate_From_Listening_Into_Reading()
    {
        var (examId, listeningLast, readingFirst) = await DiagnosticPlacementExamAsync();
        var placementId = await StartPlacementAsync(examId);

        var sitting = Page.Locator("#placement-take-dynamic");
        var number = sitting.Locator(".question__number");
        var next = sitting.GetByRole(AriaRole.Link, new() { Name = "Next question" });
        var previous = sitting.GetByRole(AriaRole.Link, new() { Name = "Previous" });

        // Listening is AUDIO_DRIVEN: no Previous control, and the server refuses a rewind past the
        // first question of the current audio cluster.
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q=1");
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

        // The last Listening question is the skill boundary: it carries the leave confirmation, and
        // moving on opens Reading rather than stranding the candidate.
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q={listeningLast}");
        await Expect(number).ToHaveTextAsync(new Regex($@"Question {listeningLast}\b", RegexOptions.IgnoreCase));
        await Expect(next).ToHaveAttributeAsync("data-confirm-leave", new Regex("Listening"));

        var dialogs = new List<string>();
        Page.Dialog += (_, dialog) => dialogs.Add(dialog.Message);
        await sitting.Locator(".option").First.ClickAsync();
        await next.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($@"\?q={readingFirst}$"));
        await Expect(number).ToHaveTextAsync(new Regex($@"Question {readingFirst}\b", RegexOptions.IgnoreCase));
        Assert.That(dialogs, Has.Count.EqualTo(1));
        Assert.That(dialogs[0], Does.Contain("Listening").And.Contain("Reading"));

        // Listening is now closed for good: no Previous into it, a hand-edited rewind is refused, and
        // the phase reads "Closed" rather than a clock - #214 / PLC-12 removed the countdown.
        await Expect(Page.Locator("[data-phase-skill='LIST'][aria-current='false']")).ToContainTextAsync("Closed");
        await Expect(previous).ToHaveCountAsync(0);
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q={listeningLast}");
        await Expect(Page).ToHaveURLAsync(new Regex($@"\?q={readingFirst}$"));

        // Reading's first question is the only one opened; the rest of Reading is free sandbox.
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q={readingFirst + 1}");
        await Expect(number).ToHaveTextAsync(new Regex($@"Question {readingFirst + 1}\b", RegexOptions.IgnoreCase));
        await Expect(previous).ToHaveAttributeAsync("href", $"/placement/take/{placementId}?q={readingFirst}");

        // The answer given right before leaving Listening was saved, not refused by the closed skill.
        //
        // #250: "saved" now has two legal shapes. A paper the pool has assembled records the pinned
        // version's LABEL (#186: an ItemVersion stores labels A-D, not option rows), while a paper with
        // no binding records the legacy SelectedOptionId. Asserting only SelectedOptionId passed before
        // the migration ran and fails after it - which is the migration working, not the save breaking.
        // The question is "was the choice recorded", so both columns are accepted.
        await using var check = await OpenAppDatabaseAsync();
        await using var answered = check.CreateCommand();
        answered.CommandText = """
            SELECT COUNT(*) FROM AttemptAnswers a
            JOIN PlacementTests p ON p.AttemptId = a.AttemptId
            WHERE p.PlacementId = @placementId
              AND (a.SelectedOptionId IS NOT NULL OR a.SelectedLabel IS NOT NULL);
            """;
        answered.Parameters.AddWithValue("@placementId", int.Parse(placementId));
        Assert.That(Convert.ToInt32(await answered.ExecuteScalarAsync()), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task Should_Carry_The_Candidate_From_The_First_Writing_Task_Into_The_Second()
    {
        // The group-lock boundary is the benchmark's, not the recommended diagnostic's: the
        // recommended exam is Listening+Reading and has no writing task to carry the candidate between.
        var (examId, firstWriting, secondWriting, readingFirst) = await WritingBenchmarkAsync();
        var placementId = await StartPlacementAsync(examId);

        var sitting = Page.Locator("#placement-take-dynamic");
        var number = sitting.Locator(".question__number");
        var next = sitting.GetByRole(AriaRole.Link, new() { Name = "Next question" });
        var previous = sitting.GetByRole(AriaRole.Link, new() { Name = "Previous" });

        // Asking for the first question of the next skill opens it, so a jump reaches Writing without
        // clicking through the Listening and Reading questions.
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q={readingFirst}");
        await Expect(number).ToHaveTextAsync(new Regex($@"Question {readingFirst}\b", RegexOptions.IgnoreCase));
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q={firstWriting}");
        await Expect(number).ToHaveTextAsync(new Regex($@"Question {firstWriting}\b", RegexOptions.IgnoreCase));
        await Expect(sitting.Locator("#writing-editor")).ToBeVisibleAsync();

        // Write the first task and move on: the sitting confirms leaving the question group, saves
        // the essay, then opens the second task without a reload.
        //
        // KNOWN BLOCKER (#272, not test debt): the group-loss dialog never fires on a pool paper, so the
        // two assertions below fail. `PlacementTakeViewModel.LeavingLockedPartConfirmation`
        // (src/WebMVC/ViewModels/Placement/PlacementTakeViewModel.cs:105) only confirms when
        // `next.GroupId != current.GroupId`, and a pool position's GroupId is its item's stimulus id
        // (`AttemptEngine.Sitting.Roster.cs:166`). docs/seed-pool-native.sql gives ALL 32 pooling items
        // stimulus 20 (one shared placeholder image), so exam 13's W2 and W3 positions - ExamItemId 85
        // and 86, one group per writing section in the legacy seed - both report GroupId 20 and read as
        // the SAME group. The pool has no per-writing-task stimulus to separate them. The legacy
        // benchmark seed gave each task its own QuestionGroup
        // (docs/seed-diagnostic-benchmark.sql:1105 and :1148), which is the shape a pool seed has to
        // reproduce before this boundary can exist again. The assertions are left intact: the group
        // lock is the subject of the test, and removing them would hide the defect.
        var dialogs = new List<string>();
        Page.Dialog += (_, dialog) => dialogs.Add(dialog.Message);
        await sitting.Locator("#writing-editor").FillAsync("An essay for the first writing task.");
        await next.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($@"\?q={secondWriting}$"));
        await Expect(number).ToHaveTextAsync(new Regex($@"Question {secondWriting}\b", RegexOptions.IgnoreCase));
        Assert.That(dialogs, Has.Count.EqualTo(1));
        Assert.That(dialogs[0], Does.Contain("question group"));

        // The first task is closed: no Previous into it, and a hand-edited rewind is refused.
        await Expect(previous).ToHaveCountAsync(0);
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}?q={firstWriting}");
        await Expect(Page).ToHaveURLAsync(new Regex($@"\?q={secondWriting}$"));

        // The essay written before the group closed was saved.
        await using var check = await OpenAppDatabaseAsync();
        await using var essay = check.CreateCommand();
        essay.CommandText = """
            SELECT f.ResponseText FROM FreeResponses f
            JOIN PlacementTests p ON p.AttemptId = f.AttemptId
            JOIN ExamItems i ON i.ExamItemId = f.ExamItemId
            JOIN Sections s ON s.SectionId = i.SectionId
            WHERE p.PlacementId = @placementId AND s.Code = 'W2';
            """;
        essay.Parameters.AddWithValue("@placementId", int.Parse(placementId));
        Assert.That(await essay.ExecuteScalarAsync(), Is.EqualTo("An essay for the first writing task."));
    }

    /// <summary>
    /// The placement exam the seed recommends, with the display orders of its last Listening question
    /// and its first Reading one. Looked up rather than hard-coded: #156 replaced the arrangement and a
    /// reseed may move the ids. Skills are read from the section's skill, not from a section code, so
    /// P1–P4 are Listening and P5–P7 are Reading by the seed's own definition.
    /// <para>
    /// #272: the paper is the pool roster (<c>ExamItems</c>), and the number these turn into is the
    /// sitting's own - <c>AttemptEngine.Sitting.cs:352</c> reads <c>?q=N</c> as index <c>N-1</c> of the
    /// list ordered by skill, section, then position <c>DisplayOrder</c>
    /// (<c>AttemptEngine.Sitting.Roster.cs:86</c>). <c>Questions.DisplayOrder</c> happened to be a global
    /// number; <c>ExamItems.DisplayOrder</c> restarts inside each section, so the ordering is turned into
    /// a <c>ROW_NUMBER</c> rather than read off the column.
    /// </para>
    /// </summary>
    private static async Task<(int ExamId, int ListeningLast, int ReadingFirst)> DiagnosticPlacementExamAsync()
    {
        await using var connection = await OpenAppDatabaseAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) e.ExamId
            FROM Exams e
            JOIN Certificates c ON c.CertId = e.CertId
            WHERE c.Code = 'TOEIC' AND e.Purpose = 'placement' AND e.Status = 'published'
              AND e.IsDeleted = 0
            ORDER BY CASE WHEN e.Name LIKE N'%Diagnostic' THEN 0 ELSE 1 END, e.ExamId;
            """;
        var examId = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.That(examId, Is.GreaterThan(0), "The demo seed must contain the TOEIC diagnostic placement exam.");

        await using var bounds = connection.CreateCommand();
        bounds.CommandText = """
            WITH sitting AS (
                SELECT
                    sk.Code AS SkillCode,
                    CAST(ROW_NUMBER() OVER (ORDER BY sk.DisplayOrder, s.DisplayOrder, p.DisplayOrder) AS INT) AS QuestionNumber
                FROM ExamItems p
                JOIN Sections s ON s.SectionId = p.SectionId
                JOIN Skills sk ON sk.SkillId = s.SkillId
                WHERE p.ExamId = @examId
            )
            SELECT
                MAX(CASE WHEN SkillCode = 'LIST' THEN QuestionNumber END),
                MIN(CASE WHEN SkillCode = 'READ' THEN QuestionNumber END)
            FROM sitting;
            """;
        bounds.Parameters.AddWithValue("@examId", examId);
        await using var reader = await bounds.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.IsDBNull(0) || reader.IsDBNull(1), Is.False,
            "The seeded diagnostic must carry both a Listening and a Reading skill for the boundary to exist.");
        return (examId, reader.GetInt32(0), reader.GetInt32(1));
    }

    /// <summary>
    /// The seeded placement exam that carries two writing tasks, with their display orders. The
    /// recommended diagnostic above is Listening+Reading only; the group-lock boundary lives on the
    /// larger benchmark. Same sitting numbering as above (#272).
    /// </summary>
    private static async Task<(int ExamId, int FirstWriting, int SecondWriting, int ReadingFirst)> WritingBenchmarkAsync()
    {
        await using var connection = await OpenAppDatabaseAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) p.ExamId
            FROM ExamItems p
            JOIN Sections s ON s.SectionId = p.SectionId
            JOIN Certificates c ON c.CertId = s.CertId
            JOIN Skills k ON k.SkillId = s.SkillId
            WHERE c.Code = 'TOEIC' AND k.Code = 'WRIT'
            GROUP BY p.ExamId
            HAVING COUNT(DISTINCT s.SectionId) >= 2
            ORDER BY p.ExamId;
            """;
        var examId = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.That(examId, Is.GreaterThan(0), "The demo seed must contain a TOEIC placement exam with writing tasks.");

        await using var orders = connection.CreateCommand();
        orders.CommandText = """
            WITH sitting AS (
                SELECT
                    s.Code AS SectionCode,
                    CAST(ROW_NUMBER() OVER (ORDER BY sk.DisplayOrder, s.DisplayOrder, p.DisplayOrder) AS INT) AS QuestionNumber
                FROM ExamItems p
                JOIN Sections s ON s.SectionId = p.SectionId
                JOIN Skills sk ON sk.SkillId = s.SkillId
                WHERE p.ExamId = @examId
            )
            SELECT SectionCode, MIN(QuestionNumber)
            FROM sitting
            WHERE SectionCode IN (SELECT Code FROM Sections WHERE SkillId = (SELECT SkillId FROM Skills WHERE CertId = (SELECT CertId FROM Exams WHERE ExamId = @examId) AND Code = 'WRIT'))
            GROUP BY SectionCode
            ORDER BY SectionCode;
            """;
        orders.Parameters.AddWithValue("@examId", examId);
        var writingOrder = new List<(string Code, int QuestionNumber)>();
        await using (var reader = await orders.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                writingOrder.Add((reader.GetString(0), reader.GetInt32(1)));
            }
        }

        await using var reading = connection.CreateCommand();
        reading.CommandText = """
            WITH sitting AS (
                SELECT
                    sk.Code AS SkillCode,
                    CAST(ROW_NUMBER() OVER (ORDER BY sk.DisplayOrder, s.DisplayOrder, p.DisplayOrder) AS INT) AS QuestionNumber
                FROM ExamItems p
                JOIN Sections s ON s.SectionId = p.SectionId
                JOIN Skills sk ON sk.SkillId = s.SkillId
                WHERE p.ExamId = @examId
            )
            SELECT MIN(QuestionNumber) FROM sitting WHERE SkillCode = 'READ';
            """;
        reading.Parameters.AddWithValue("@examId", examId);
        var readingFirst = Convert.ToInt32(await reading.ExecuteScalarAsync());

        // Two writing tasks are the minimum for a group boundary to exist at all: the sitting confirms
        // when the candidate leaves one group for another.
        Assert.That(writingOrder, Has.Count.GreaterThanOrEqualTo(2),
            "The seeded diagnostic must carry at least two writing sections for the group boundary to exist.");
        var ordered = writingOrder.OrderBy(w => w.QuestionNumber).ToList();
        return (examId, ordered[0].QuestionNumber, ordered[1].QuestionNumber, readingFirst);
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
