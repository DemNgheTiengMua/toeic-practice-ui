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
}
