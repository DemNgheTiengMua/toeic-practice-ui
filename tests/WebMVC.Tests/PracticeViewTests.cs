using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

public sealed class PracticeViewTests
{
    [Fact]
    public void Practice_index_offers_one_smart_button_and_no_drill_recipe()
    {
        // #155: the count and source dropdowns are gone. What must not come back is the pair of selects
        // the learner used to steer the queue with - the ten questions and their source are the
        // mastery planner's decision, and the page cannot offer what the service no longer accepts.
        var repoRoot = TestPathHelper.FindRepoRoot();
        var index = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "Index.cshtml"));

        Assert.DoesNotContain("name=\"QuestionCount\"", index);
        Assert.DoesNotContain("name=\"QuestionSource\"", index);
        Assert.DoesNotContain("pr-count", index);
        Assert.DoesNotContain("pr-source", index);
        Assert.Contains("Smart Practice", index);
        Assert.Contains("_PracticeSectionCard", index);
    }

    // #155: a section cannot say "all topics resting" without saying when they come back - the learner
    // would otherwise meet an empty session and have no idea why.
    [Fact]
    public void A_resting_section_says_when_its_next_review_is_due()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var card = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "_PracticeSectionCard.cshtml"));

        Assert.Contains("CategoriesInCooldown", card);
        Assert.Contains("NextReviewDueAt", card);
        Assert.Contains("resting", card);
    }

    [Fact]
    public void Take_View_Contains_PracticeExamShell_And_ZeroReload_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = File.ReadAllText(viewPath) + string.Concat(Directory.GetFiles(Path.GetDirectoryName(viewPath)!, "_Take*.cshtml").Select(File.ReadAllText));

        // 1. Two-column Exam Shell layout
        Assert.Contains("exam-layout", html);

        // 2. Embedded JSON manifest for 0ms in-memory swapping
        Assert.Contains("id=\"practice-data\"", html);

        // 3. Persistent Sidebar Navigator Grid with button cells
        Assert.Contains("class=\"navigator__grid\"", html);
        Assert.Contains("<button type=\"button\"", html);

        // 4. Zero-Reload: Navigator and next actions must NOT have href question links
        Assert.DoesNotMatch(@"<a\s+[^>]*href=[""']/practice/\d+\?q=", html);

        // 5. Practice Quality of Life (QoL) controls
        Assert.Contains("id=\"btn-controls-modal\"", html);
        Assert.Contains("id=\"controls-modal\"", html);
        Assert.Contains("data-filter=\"all\"", html);
        Assert.Contains("data-filter=\"incorrect\"", html);
        Assert.Contains("data-filter=\"saved\"", html);
        Assert.Contains("id=\"sidebar-accuracy-display\"", html);
    }

    [Fact]
    public void StudyAudioPlayer_Contains_Speed_Toggles_And_No_Robotic_Disclaimers()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var markupPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_ExamAudioPlayerMarkup.cshtml");

        Assert.True(File.Exists(markupPath), "_ExamAudioPlayerMarkup.cshtml must exist");
        var html = File.ReadAllText(markupPath);

        // Speed toggles for study mode ear-training
        Assert.Contains("data-speed=\"0.8\"", html);
        Assert.Contains("data-speed=\"1.0\"", html);
        Assert.Contains("data-speed=\"1.2\"", html);

        // Strict non-regression guard: No robotic disclaimer copy
        Assert.DoesNotContain("Seeking disabled", html);
        Assert.DoesNotContain("Plays once only", html);
        Assert.DoesNotContain("Replay permitted", html);
    }
    [Fact]
    public void Moving_between_questions_keeps_title_and_answer_group_name_on_the_question_shown()
    {
        // Both are rendered for the first question; the zero-reload swap left them on "Question 1"
        // while the drill showed question 4.
        var repoRoot = TestPathHelper.FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "js", "practice", "take.js"));
        var render = Regex.Match(script, @"function renderQuestion\(order\)\s*\{(?<body>[\s\S]*?)\n      function ").Groups["body"].Value;
        Assert.NotEmpty(render);
        Assert.Contains("document.title = document.title.replace(/Question \\d+/, \"Question \" + q.displayOrder)", render);
        Assert.Contains("optionList.setAttribute(\"aria-label\", \"Answer for question \" + q.displayOrder)", render);

        var view = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "Take.cshtml"));
        Assert.Contains("— Question @q.DisplayOrder —", view);
    }

    // #150 round 2: the drill widget must not silently stand in for a microphone. The free-response
    // branch is writing-only, and the practice page sends speaking to the recorded speaking practice.
    [Fact]
    public void Speaking_never_reaches_the_typed_free_response_widget()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "js", "practice", "take.js"));
        var render = Regex.Match(script, @"function renderComponentWidget\(q, s\)\s*\{(?<body>[\s\S]*?)\n      function ").Groups["body"].Value;
        Assert.NotEmpty(render);

        // Badge classes must exist in the design system; badge--neutral never did.
        Assert.DoesNotContain("badge--neutral", script);
        Assert.Contains("badge--muted", render);

        var view = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "Index.cshtml"));
        Assert.Contains("Model.SpeakingSections", view);
        Assert.Contains("/speaking/", view);
        // Speaking sections are not start buttons.
        var speakingBlock = Regex.Match(view, @"@if \(Model\.SpeakingSections\.Count > 0\)(?<body>[\s\S]*?)\n  \}").Groups["body"].Value;
        Assert.NotEmpty(speakingBlock);
        Assert.DoesNotContain("name=\"SectionId\"", speakingBlock);
    }

    [Fact]
    public void Take_View_Contains_Practice_QoL_Audio_Rewind_Transcript_And_Explanation_Accordion()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = File.ReadAllText(viewPath) + string.Concat(Directory.GetFiles(Path.GetDirectoryName(viewPath)!, "_Take*.cshtml").Select(File.ReadAllText));

        // 1. Practice QoL audio rewind 5s button
        Assert.Contains("id=\"btn-audio-rewind-5s\"", html);

        // 2. Transcript drawer toggle and drawer container
        Assert.Contains("id=\"btn-toggle-transcript\"", html);
        Assert.Contains("id=\"transcript-drawer\"", html);

        // 3. Dynamic 11-Component Widgets container
        Assert.Contains("id=\"component-container\"", html);

        // 4. Detailed explanation / rationale accordion
        Assert.Contains("id=\"explanation-container\"", html);
        Assert.Contains("accordion-explanation", html);
    }

    /// <summary>
    /// #235 / PLC-62 / PLC-64: the locked state is printed as the service wrote it. The input that would
    /// pass a weaker test while being wrong is a page that renders a lock badge and no reason — the
    /// brief names that as the most hostile state in the product — so the assertion is that the notice
    /// property is what reaches the page, and that the page offers the way on.
    /// </summary>
    [Fact]
    public void The_free_drill_lock_prints_its_sentence_and_offers_a_way_on()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var index = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "Index.cshtml"));

        Assert.Contains("FreeDrillLockNotice", index);
        Assert.Contains("/learning-path", index);
        Assert.Contains("/lessons", index);

        // #235 / PLC-61: the highlighted coordinates are rendered, and they are labelled as a hint.
        Assert.Contains("PriorityLabels", index);
        Assert.Contains("not required", index);

        // #235 / D12: the day's volume comes from the view and is printed as two numbers.
        Assert.Contains("AnsweredToday", index);
        Assert.Contains("DailyItemCeiling", index);
    }

    /// <summary>
    /// #238 / SCR-PRAC-01: the hub renders the matrix, and a resting cell names the local time it
    /// reopens. The input that would pass while being wrong is a view that prints the dim state but the
    /// UTC instant, or one that renders only the mastery label and never the time.
    /// </summary>
    [Fact]
    public void The_hub_renders_the_matrix_with_each_cells_state_and_reopen_time()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var index = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "Index.cshtml"));

        Assert.Contains("Model.Matrix", index);
        Assert.Contains("cell.CssClass", index);
        Assert.Contains("data-state=\"@cell.MasteryState\"", index);
        Assert.Contains("data-resting", index);
        Assert.Contains("cell.StatusLine", index);
        Assert.Contains("cell.MasteryLabel", index);
        Assert.Contains("cell.IsPrioritised", index);
        // The dim state is announced, not only coloured: a visually-hidden sentence carries it.
        Assert.Contains("visually-hidden", index);
        Assert.Contains("cell.AccessibleLabel", index);

        // The stylesheet is the page's own, and defines the resting plate and the ink inversion.
        var css = File.ReadAllText(Path.Combine(
            repoRoot, "src", "WebMVC", "wwwroot", "css", "practice", "matrix.css"));
        Assert.Contains(".matrix__cell.is-resting", css);
        Assert.Contains(".matrix__cell.is-prioritised", css);
        Assert.Contains("background: var(--ink)", css);
        Assert.DoesNotContain("border-radius", css);
        Assert.DoesNotContain("box-shadow", css);
    }

    /// <summary>
    /// #238 / U18: the take page reveals the level after the lock, and binds the explanation to the
    /// option it explains. The payload suppression is asserted on the DTO separately
    /// (<c>PracticeObjectiveSessionTests</c>); this is the markup that consumes it.
    /// </summary>
    [Fact]
    public void The_take_page_reveals_the_level_and_names_the_option_the_explanation_explains()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var explanation = File.ReadAllText(Path.Combine(
            repoRoot, "src", "WebMVC", "Views", "Practice", "_TakeExplanation.cshtml"));

        Assert.Contains("RevealedLevelCode", explanation);
        Assert.Contains("revealed-level", explanation);
        Assert.Contains("ExplanationSubjectLabel", explanation);
        Assert.Contains("ExplanationSubjectText", explanation);

        var question = File.ReadAllText(Path.Combine(
            repoRoot, "src", "WebMVC", "Views", "Practice", "_TakeQuestion.cshtml"));
        Assert.Contains("ShortNotice", question);
        Assert.Contains("question-level-badge", question);
    }
}
