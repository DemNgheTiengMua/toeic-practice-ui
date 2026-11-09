using System.Text.RegularExpressions;
using Contracts;
using WebMVC.Models;

namespace WebMVC.Tests;

/// <summary>
/// #239: the productive practice screens, asserted as source and as behaviour — the two halves a
/// Razor view and its JavaScript actually are.
/// <para>
/// <b>Why source assertions on the views are legitimate here.</b> These are structural claims a
/// rendered page cannot easily show (that the checklist sits beside the editor rather than above it, that
/// the word counter reads the server's expression). Each one is paired with a behavioural assertion
/// wherever behaviour exists to assert, so the guard is not a spelling check.
/// </para>
/// </summary>
public sealed class ProductivePracticePresentationTests
{
    private static readonly string Root = TestPathHelper.FindRepoRoot();

    private static string View(string relative) =>
        File.ReadAllText(Path.Combine(Root, "src", "WebMVC", "Views", relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Script(string relative) =>
        File.ReadAllText(Path.Combine(Root, "src", "WebMVC", "wwwroot", "js", relative.Replace('/', Path.DirectorySeparatorChar)));

    // ------------------------------------------------------------------ 1. checklist beside the editor

    /// <summary>
    /// <b>The input that would pass a wrong guard:</b> a checklist rendered anywhere on the page. The
    /// specification asks for a <em>split pane</em> — checklist left, editor right — so the guard is that
    /// both live in the same two-column grid and the checklist comes first in it. A checklist above the
    /// editor would satisfy "it is on the page" and fail this.
    /// </summary>
    [Fact]
    public void The_writing_checklist_and_the_editor_are_the_two_panes_of_one_grid()
    {
        var editor = View("Writing/_TakeEditor.cshtml");

        var grid = editor.IndexOf("class=\"writing-panes\"", StringComparison.Ordinal);
        var checklist = editor.IndexOf("class=\"writing-panes__prompt", StringComparison.Ordinal);
        var answer = editor.IndexOf("id=\"writing-editor\"", StringComparison.Ordinal);

        Assert.True(grid >= 0, "the editor partial must open the two-column grid");
        Assert.True(checklist > grid, "the checklist must be the grid's left pane");
        Assert.True(answer > checklist, "the editor must be the grid's right pane, after the checklist");
        Assert.DoesNotContain("style=", editor);
    }

    /// <summary>
    /// <b>The input that would pass a "the range is shown" check:</b> a hard-coded 30–300. The range
    /// must be read from the task's own row, which the view reaches through the model.
    /// </summary>
    [Fact]
    public void The_word_range_the_checklist_shows_comes_from_the_task_and_not_a_constant()
    {
        var editor = View("Writing/_TakeEditor.cshtml");

        Assert.Contains("q.MinWords", editor);
        Assert.Contains("q.MaxWords", editor);

        // The trap: a literal range. `30` and `300` are the demo essay's numbers; no view may carry them.
        Assert.DoesNotMatch(@"\b30\s*–\s*300\b", editor);
        Assert.DoesNotMatch(@"\bmin\s+\d+\b(?!\s*<)", editor.Replace("@q.MinWords", ""));
    }

    // ------------------------------------------------------------------ 2. the count agrees

    /// <summary>
    /// <b>The input that would pass a "live count exists" check:</b> any counter, including one that
    /// splits on <c>\s+</c> and therefore disagrees with the stored number for a pasted no-break space.
    /// The composer must compile the pattern the service sent, so the two are one rule.
    /// </summary>
    [Fact]
    public void The_composer_counts_with_the_servers_expression_and_never_a_second_copy()
    {
        var script = Script("writing/take.js");

        Assert.Contains("data-word-pattern", script);
        Assert.Contains("new RegExp(wordPattern)", script);

        // The trap: a whitespace split that agrees on "one  two" and disagrees on "one\u00A0two".
        Assert.DoesNotContain(@"split(/\s+/)", script);
        Assert.DoesNotContain(@"trim().split", script);
    }

    /// <summary>The value the page receives is <see cref="WordCount.Pattern"/> itself, not a literal.</summary>
    [Fact]
    public void The_pattern_the_controller_sends_is_the_servers_own()
    {
        Assert.Equal(@"[ \t\r\n]+", WordCount.Pattern);
        Assert.Equal(1, WordCount.Count("one\u00A0two"));
        Assert.Equal(2, WordCount.Count("one two"));
    }

    // ------------------------------------------------------------------ 3. the timers

    /// <summary>
    /// <b>The input that would pass a "there is a prep timer" check:</b> a prep timer that keeps
    /// counting into the recording window, so a learner is told "prepare" while the recorder is live.
    /// The two intervals are separate and the prep one is cleared before recording starts.
    /// </summary>
    [Fact]
    public void Preparation_and_the_recording_window_are_two_separate_clocks()
    {
        var script = Script("speaking/take.js");

        Assert.Contains("prepInterval", script);
        Assert.Contains("speakInterval", script);

        // Start recording must clear the prep clock before it starts the speaking clock: a prep timer
        // still running inside the window is the defect this asserts against.
        var startRecording = script.IndexOf("async function startRecording", StringComparison.Ordinal);
        Assert.True(startRecording > 0);
        var body = script[startRecording..script.IndexOf("function updateSpeakUI", startRecording, StringComparison.Ordinal)];
        Assert.Contains("clearInterval(prepInterval)", body);
        Assert.Contains("speakInterval = setInterval", body);
        Assert.DoesNotContain("prepInterval = setInterval", body);
    }

    /// <summary>
    /// <b>The input that would pass a client-only window:</b> a take longer than the task declares. The
    /// browser stops at the declared second, so only a server-side assertion can see the difference; that
    /// one is in <c>ProductivePracticeTests</c>, and this asserts the view hands the declared numbers in
    /// rather than constants.
    /// </summary>
    [Fact]
    public void The_take_page_hands_the_tasks_own_windows_to_the_recorder()
    {
        var take = View("Speaking/Take.cshtml");

        Assert.Contains("data-prep-seconds=\"@q.PrepSeconds\"", take);
        Assert.Contains("data-response-seconds=\"@q.ResponseSeconds\"", take);
        Assert.DoesNotContain("data-prep-seconds=\"45\"", take);
        Assert.DoesNotContain("data-response-seconds=\"30\"", take);
    }

    /// <summary>The timer is announced, not only coloured (the ticket's accessibility clause).</summary>
    [Fact]
    public void Each_timer_carries_a_live_region_and_a_text_label()
    {
        var recorder = View("Speaking/_TakeRecorder.cshtml");

        Assert.Contains("id=\"prep-timer\"", recorder);
        Assert.Contains("id=\"speak-timer\"", recorder);
        // A role alone is not announcement: the value must be in a polite live region and the block
        // must say which clock it is in words.
        Assert.Matches(@"role=""status""|aria-live=""polite""", recorder);
        Assert.Contains("Preparation time left", recorder);
        Assert.Contains("Speaking time left", recorder);
    }

    // ------------------------------------------------------------------ 4. one task is not a level

    /// <summary>
    /// <b>The input that would pass a "the estimate is labelled" check:</b> a page that always prints
    /// the label. Both branches are asserted on the view: the label is conditional on the graded count,
    /// so a two-task attempt cannot carry it.
    /// </summary>
    [Fact]
    public void The_one_task_label_is_conditional_on_the_graded_count_on_both_reviews()
    {
        foreach (var view in new[] { "Writing/Review.cshtml", "Speaking/Review.cshtml" })
        {
            var source = View(view);

            Assert.Contains("GradedQuestions == 1", source);
            Assert.Contains("GradedQuestions > 1", source);
            Assert.Contains("Estimated from one task", source);

            // The label must be inside the branch, not unconditionally in the page: a file that prints
            // it and separately compares the count would pass a "contains both" assertion.
            var branch = source.IndexOf("GradedQuestions == 1", StringComparison.Ordinal);
            var label = source.IndexOf("Estimated from one task", StringComparison.Ordinal);
            Assert.True(label > branch, $"{view} prints the one-task label outside its branch");
        }
    }

    // ------------------------------------------------------------------ 5. the criteria carry evidence

    /// <summary>
    /// <b>The input that would pass a "the criterion is named" check:</b> "Criterion: Task Achievement —
    /// 4/6", which names the criterion and its score and tells the learner nothing. The guard is that the
    /// evidence cell renders the grader's own note and, when there is none, says so as a fact rather than
    /// filling the gap with a phrase that reads like feedback.
    /// </summary>
    [Fact]
    public void A_criterion_shows_the_graders_note_and_never_a_placeholder_that_reads_like_feedback()
    {
        var writing = WithoutComments(View("Writing/Review.cshtml"));
        var speaking = WithoutComments(View("Speaking/Review.cshtml"));

        Assert.Contains("crit.Comment", writing);
        Assert.Contains("c.Comment", speaking);
        Assert.Contains("No note was recorded for this criterion", writing);
        Assert.Contains("No note was recorded for this criterion", speaking);

        // The two phrases that were the placeholder: they name nothing, and "Criteria satisfied." is
        // also a claim the grader never made. A Razor comment may quote them; a rendered node may not.
        Assert.DoesNotContain("Evaluated against rubric.", writing);
        Assert.DoesNotContain("Criteria satisfied.", speaking);
        Assert.DoesNotContain("Evaluated against rubric standards.", speaking);
    }

    /// <summary>A Razor comment holds prose and code alike; what is inside one is never rendered.</summary>
    private static string WithoutComments(string source) =>
        Regex.Replace(source, @"@\*.*?\*@", "", RegexOptions.Singleline);

    // ------------------------------------------------------------------ 6. prior submissions

    /// <summary>
    /// <b>The input that would pass a "history is shown" check:</b> a list that renders nothing where a
    /// learner needs to click. Each row must link through the route the service computed, and a status
    /// must never be printed as its code.
    /// </summary>
    [Fact]
    public void The_prior_attemptlist_links_through_the_services_route_and_labels_every_status()
    {
        var partial = View("Shared/_PriorAttempts.cshtml");

        Assert.Contains("attempt.ReopenPath", partial);
        Assert.Contains("attempt.CanReview", partial);

        // D19: a stored code must not reach a text node.
        Assert.Contains("DisplayLabels.LabelForCode<AttemptStatusDto>", partial);
        Assert.DoesNotContain("@attempt.Status<", partial);
    }

    /// <summary>
    /// <b>The input that would pass a "history is on the page" check:</b> it on one skill's page only.
    /// Both instructions pages render it, because both skills have prior attempts to re-open.
    /// </summary>
    [Fact]
    public void Both_instructions_pages_render_the_prior_attempts()
    {
        Assert.Contains("_PriorAttempts", View("Writing/Instructions.cshtml"));
        Assert.Contains("_PriorAttempts", View("Speaking/Instructions.cshtml"));
    }

    // ------------------------------------------------------------------ 7. a failure is a state, not a class

    /// <summary>
    /// <b>The input that would pass a "there is a failure state" check:</b> a red banner with no reason
    /// and no way out. The failure block must carry the stored reason and a real retry control.
    /// </summary>
    [Fact]
    public void The_failure_state_names_the_reason_and_offers_the_retry()
    {
        foreach (var (view, action) in new[] { ("Writing/Pending.cshtml", "/writing/retry/"), ("Speaking/Pending.cshtml", "/speaking/retry/") })
        {
            var source = View(view);

            Assert.Contains("p.FailureReason", source);
            Assert.Contains(action, source);
            Assert.Contains("method=\"post\"", source);
        }
    }
}
