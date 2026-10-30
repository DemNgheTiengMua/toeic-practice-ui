using System.IO;
using System.Text.Json;
using Contracts;
using WebMVC.Models;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

public sealed class ExamsViewTests
{
    [Fact]
    public void OptionMarkerAndText_MustHavePointerEventsNone_ToAvoidInterceptingClicks()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var cssPath = Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "css", "components.css");

        Assert.True(File.Exists(cssPath), "components.css must exist");
        var cssContent = File.ReadAllText(cssPath);

        // Match .option__marker block
        var markerMatch = Regex.Match(cssContent, @"\.option__marker\s*\{(?<rules>[^}]+)\}");
        Assert.True(markerMatch.Success, ".option__marker rule block must be found in components.css");
        Assert.Matches(@"pointer-events\s*:\s*none", markerMatch.Groups["rules"].Value);

        // Match .option__text block
        var textMatch = Regex.Match(cssContent, @"\.option__text\s*\{(?<rules>[^}]+)\}");
        Assert.True(textMatch.Success, ".option__text rule block must be found in components.css");
        Assert.Matches(@"pointer-events\s*:\s*none", textMatch.Groups["rules"].Value);
    }

    [Fact]
    public void Take_View_Contains_ExamShell_Integrity_And_ZeroReload_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. Embedded JSON payload for 0ms in-memory swapping
        Assert.Contains("id=\"exam-data\"", html);

        // 2. Offline Network Banner
        Assert.Contains("id=\"network-warning\"", html);



        // 4. Modal In-flight Sync Warning
        Assert.Contains("id=\"modal-sync-pending\"", html);

        // 5. Zero-Reload: Navigator cells must be buttons, NOT anchor tags with href navigation
        Assert.Contains("<button type=\"button\"", html);
        Assert.DoesNotMatch(@"<a\s+[^>]*href=[""']/exams/take/[^""']*\?q=", html);

        // 6. Non-happy path guards
        Assert.Contains("window.history.pushState", html);
        Assert.Contains("popstate", html);
        Assert.Contains("beforeunload", html);
        Assert.Contains("contextmenu", html);
        Assert.DoesNotContain("e.key === 'F12'", html);
    }

    [Fact]
    public void Exit_resume_later_leaves_without_the_accidental_close_prompt()
    {
        // The exit modal's "Exit (resume later)" is a deliberate leave; it used to be a bare link,
        // so the beforeunload guard stacked a browser "Leave site?" prompt on top of it.
        var repoRoot = TestPathHelper.FindRepoRoot();
        var html = ReadTakeSource(repoRoot);
        Assert.Matches(@"<a\s[^>]*href=""/exams""[^>]*id=""btn-exit-resume-later""[^>]*>Exit \(resume later\)</a>", html);

        var script = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "js", "exams", "submission.js"));
        var handler = Regex.Match(script,
            @"getElementById\('btn-exit-resume-later'\)[\s\S]*?addEventListener\('click'(?<body>[\s\S]*?)\}\)\(\);");
        Assert.True(handler.Success, "Exit (resume later) must have its own click handler.");
        var body = handler.Groups["body"].Value;
        Assert.Contains("e.preventDefault()", body);
        Assert.Contains("sitting.triggerFlush()", body);
        // isSubmitting is what the beforeunload guard checks; it must be set before navigating.
        Assert.True(body.IndexOf("sitting.isSubmitting = true", StringComparison.Ordinal)
            < body.IndexOf("window.location.href = href", StringComparison.Ordinal));
        Assert.Contains("sitting.isSubmitting = true", body);
    }

    [Fact]
    public void Take_View_Contains_SplitScreen_TopControlBar_Palette_Highlighter_And_ComponentWidgets_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. Split-Screen Viewport dual-pane layout & independent scrollbars
        Assert.Contains(".exam-layout--split", html);
        Assert.Contains("id=\"split-stimulus-pane\"", html);
        Assert.Contains("id=\"split-passage-scroll\"", html);
        Assert.Contains("id=\"split-passage-text\"", html);

        // 2. Top Control Bar: Red countdown flash placard, volume slider, accessibility modal
        Assert.Contains("keyframes timer-pulse-red", html);
        Assert.Contains(".timer--warn-flash", html);
        Assert.Contains(".timer-alert-placard", html);
        Assert.Contains("id=\"exam-volume-slider\"", html);
        Assert.Contains("id=\"exam-volume-pct\"", html);
        Assert.Contains("id=\"accessibility-modal\"", html);
        Assert.Contains("data-size=\"standard\"", html);
        Assert.Contains("data-size=\"large\"", html);
        Assert.Contains("data-size=\"xlarge\"", html);
        Assert.Contains("data-theme=\"standard\"", html);
        Assert.Contains("data-theme=\"inverted\"", html);
        Assert.Contains("data-theme=\"high-contrast\"", html);

        // 3. Question Component Widgets Container and Renderers
        Assert.Contains("id=\"component-widget-container\"", html);
        Assert.Contains("renderComponentWidget", html);
        Assert.Contains("multi-select-widget", html);
        Assert.Contains("inline-completion-widget", html);
        Assert.Contains("dropdown-widget", html);
        Assert.Contains("drag-drop-widget", html);

        // 4. Bottom Question Palette with 4 status states
        Assert.Contains("id=\"bottom-question-palette\"", html);
        Assert.Contains("id=\"bottom-palette-chips\"", html);
        Assert.Contains("palette-chip--unanswered", html);
        Assert.Contains("palette-chip--answered", html);
        Assert.Contains("palette-chip--current", html);
        Assert.Contains("palette-chip--flagged", html);

        // 5. Tokenized Text Highlighter and Notes Engine
        Assert.Contains("id=\"highlighter-popover\"", html);
        Assert.Contains("data-action=\"highlight\"", html);
        Assert.Contains("data-action=\"note\"", html);
        Assert.Contains("data-action=\"clear\"", html);
        Assert.Contains("id=\"sticky-note-modal\"", html);
        Assert.Contains("renderTokenizedText", html);
        Assert.Contains("ee-token", html);
        Assert.Contains("candidateNotes", html);
    }

    [Fact]
    public void Take_View_Contains_Masked_Stimulus_And_Option_Count_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. Masked styling classes
        Assert.Contains(".option-list--masked", html);
        Assert.Contains(".option--masked", html);

        // 2. HideOptionText & OptionCount bindings
        Assert.Contains("option-list--masked", html);
        Assert.Contains("q.HideOptionText", html);
        Assert.Contains("@foreach (var opt in q.Options)", html);
        Assert.Contains("hideOptText", html);
        Assert.Contains("optCount", html);

        // 3. Option marker placard is preserved while option text is conditionally hidden
        Assert.Contains("option__marker", html);
        Assert.Contains("@if (!q.HideOptionText)", html);
        Assert.Contains("if (!hideOptText)", html);

        // 4. RequireImage styling and display
        Assert.Contains("q.RequireImage", html);
        Assert.Contains("id=\"image-container\"", html);
    }

    [Fact]
    public void Take_View_Contains_Clustered_Audio_Cards_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. Clustered audio container and card items
        Assert.Contains("id=\"cluster-card-container\"", html);
        Assert.Contains(".cluster-card-container", html);
        Assert.Contains(".cluster-item", html);
        Assert.Contains("q.IsCluster", html);
        Assert.Contains("isCluster", html);

        // 2. Independent radio and flag inputs per question in cluster
        Assert.Contains("js-cluster-radio", html);
        Assert.Contains("js-cluster-flag-btn", html);
        Assert.Contains("cluster_opt_", html);
        Assert.Contains("data-question-id", html);
    }

    [Fact]
    public void Take_View_Contains_Audio_Driven_Restrictions_And_One_Way_Pacing_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. AUDIO_DRIVEN mode checks
        Assert.Contains("AUDIO_DRIVEN", html);
        Assert.Contains("isAudioDriven", html);

        // 2. Previous button disabled/hidden in audio-driven mode
        Assert.Contains("sitting.btnPrev.style.display = 'none'", html);
        Assert.Contains("sitting.btnPrev.disabled = !phase", html);

        // 3. Jumping backward blocked in goToQuestion
        Assert.Contains("if (isOneWay && targetOrder < sitting.currentDisplayOrder) return;", html);

        // 4. Backward cells disabled in bottom palette and navigator
        Assert.Contains("isBackInAudio", html);
        Assert.Contains("palette-chip--disabled", html);
    }

    [Fact]
    public void Take_View_Contains_Reading_Review_Screen_Modal_And_Filters()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. Review Screen Modal markup and button
        Assert.Contains("id=\"review-screen-modal\"", html);
        Assert.Contains("id=\"btn-open-review-screen\"", html);

        // 2. Statistics displays
        Assert.Contains("id=\"review-stat-answered\"", html);
        Assert.Contains("id=\"review-stat-flagged\"", html);
        Assert.Contains("id=\"review-stat-unanswered\"", html);

        // 3. 3 Quick-Action Buttons
        Assert.Contains("id=\"btn-review-first-unanswered\"", html);
        Assert.Contains("id=\"btn-review-only-unanswered\"", html);
        Assert.Contains("id=\"btn-review-only-flagged\"", html);
        Assert.Contains("id=\"btn-review-show-all\"", html);

        // 4. Questions Grid List and navigation
        Assert.Contains("id=\"review-questions-list\"", html);
        Assert.Contains("review-item-card", html);
        Assert.Contains("renderReviewQuestions", html);
    }

    [Fact]
    public void Take_View_Contains_Adaptive_Stage_Unit_Locking_Modal_And_Transition()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. UNIT_LOCKED_SANDBOX checks
        Assert.Contains("UNIT_LOCKED_SANDBOX", html);
        Assert.Contains("activeUnitNumber", html);
        Assert.Contains("isUnitLocked", html);

        // 2. Unit Lock Confirmation Modal and warning text
        Assert.Contains("id=\"unit-lock-modal\"", html);
        Assert.Contains("Once confirmed, Stage 1 will be permanently locked and you cannot return to it.", html);
        Assert.Contains("id=\"btn-confirm-unit-lock\"", html);

        // 3. Transition call to endpoint
        Assert.Contains("/exams/attempts/' + sitting.attemptId + '/transition-unit", html);
        Assert.Contains("targetUnitNumber: 2", html);

        // 4. Finish Unit 1 button on final question
        Assert.Contains("Finish Stage 1 & Proceed to Stage 2", html);
        Assert.Contains("js-open-unit-lock-modal", html);
    }

    [Fact]
    public void Take_View_Contains_Writing_Editor_Live_Word_Counter_And_Spellcheck_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. Plain text editor textarea with spellcheck, autocorrect, autocomplete, autocapitalize disabled
        Assert.Contains("id=\"writing-editor\"", html);
        Assert.Contains("spellcheck=\"false\"", html);
        Assert.Contains("autocorrect=\"off\"", html);
        Assert.Contains("autocomplete=\"off\"", html);
        Assert.Contains("autocapitalize=\"off\"", html);

        // 2. Live word counter updating dynamically
        Assert.Contains("id=\"live-word-count\"", html);
        Assert.Contains("id=\"writing-counts\"", html);
        Assert.Contains("countWords", html);
        Assert.Contains("updateWritingCounters", html);

        // 3. Minimum words threshold badge and under-length warning indicator
        Assert.Contains("id=\"min-words-badge\"", html);
        Assert.Contains("Minimum: 0 words", html);
        Assert.Contains("id=\"under-length-warning\"", html);
        Assert.Contains("(Below minimum word limit)", html);
        Assert.Contains("minWords", html);
    }

    [Fact]
    public void Take_View_Contains_Restricted_Clipboard_And_OnScreen_CutPaste_Toolbar_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. On-screen editing toolbar markup & buttons
        Assert.Contains("id=\"writing-onscreen-toolbar\"", html);
        Assert.Contains("id=\"btn-writing-cut\"", html);
        Assert.Contains("id=\"btn-writing-paste\"", html);
        Assert.Contains("id=\"btn-writing-undo\"", html);
        Assert.Contains("id=\"btn-writing-redo\"", html);
        Assert.Contains("Cut", html);
        Assert.Contains("Paste", html);
        Assert.Contains("Undo", html);
        Assert.Contains("Redo", html);

        // 2. Clipboard and undo/redo state management
        Assert.Contains("internalClipboard", html);
        Assert.Contains("undoStack", html);
        Assert.Contains("redoStack", html);
        Assert.Contains("pushUndoState", html);

        // 3. Shortcut interception & keyboard suppression
        Assert.Contains("isCutPasteRestricted", html);
        Assert.Contains("allowKeyboardCutPaste", html);
        Assert.Contains("showOnScreenCutPaste", html);
        Assert.Contains("sitting.writingEditor.addEventListener('keydown'", html);
        Assert.Contains("sitting.writingEditor.addEventListener('cut'", html);
        Assert.Contains("sitting.writingEditor.addEventListener('copy'", html);
        Assert.Contains("sitting.writingEditor.addEventListener('paste'", html);
    }

    [Fact]
    public void Take_View_Contains_Required_Keywords_Badges_And_Live_Lemma_Detection_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. Required keywords area and badge container
        Assert.Contains("id=\"required-keywords-area\"", html);
        Assert.Contains("id=\"required-keywords-list\"", html);
        Assert.Contains("Mandatory Keywords:", html);

        // 2. Keyword badge CSS classes
        Assert.Contains(".keyword-badge", html);
        Assert.Contains("badge--used", html);
        Assert.Contains("badge--pending", html);

        // 3. Lemma checker and live detection
        Assert.Contains("containsKeywordStem", html);
        Assert.Contains("stripSuffixes", html);
        Assert.Contains("renderRequiredKeywords", html);
        Assert.Contains("updateRequiredKeywordsMatch", html);
    }

    [Fact]
    public void Take_View_Contains_Hybrid_Group_Locked_Confirmation_Modal_And_Transition_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. NavigationMode HYBRID_GROUP_LOCKED check
        Assert.Contains("HYBRID_GROUP_LOCKED", html);
        Assert.Contains("activeGroupId", html);

        // 2. Group Lock Confirmation Modal & required exact warning text
        Assert.Contains("id=\"group-lock-modal\"", html);
        Assert.Contains("Confirm transition to next question: Current section will be locked permanently and remaining time will not carry over.", html);
        Assert.Contains("id=\"btn-confirm-group-lock\"", html);
        Assert.Contains("js-close-group-lock-modal", html);

        // 3. Transition call to endpoint
        Assert.Contains("/exams/attempts/' + sitting.attemptId + '/transition-group", html);
        Assert.Contains("targetGroupId: sitting.pendingTargetGroupId", html);
    }

    [Fact]
    public void Take_View_Contains_Speaking_Pacer_State_Machine_And_Verification_Screen_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. NavigationMode SYSTEM_PACED_AUTO check & navigation suppression
        Assert.Contains("SYSTEM_PACED_AUTO", html);
        Assert.Contains("q.IsSystemPacedAuto", html);
        Assert.Contains("isSystemPacedAuto", html);

        // 2. Prep Countdown placard, timer and visual progress bar
        Assert.Contains("id=\"speaking-pacer-container\"", html);
        Assert.Contains("id=\"speaking-prep-placard\"", html);
        Assert.Contains("id=\"speaking-prep-timer\"", html);
        Assert.Contains("id=\"speaking-prep-bar\"", html);
        Assert.Contains("PREPARATION TIME:", html);

        // 3. Audible beep cue using Web Audio API (1000Hz for 300ms)
        Assert.Contains("playBeepCue", html);
        Assert.Contains("AudioContext", html);
        Assert.Contains("1000", html);
        Assert.Contains("0.3", html);

        // 4. Recording Countdown placard, flashing indicator & audio level meter
        Assert.Contains("id=\"speaking-record-placard\"", html);
        Assert.Contains("id=\"speaking-record-timer\"", html);
        Assert.Contains("id=\"speaking-record-bar\"", html);
        Assert.Contains("RECORDING IN PROGRESS:", html);
        Assert.Contains("id=\"audio-level-container\"", html);
        Assert.Contains("id=\"audio-level-meter\"", html);
        Assert.Contains("id=\"audio-level-bar\"", html);
        Assert.Contains("createAnalyser", html);

        // 5. Background upload endpoint call
        Assert.Contains("/exams/take/' + sitting.attemptId + '/upload-audio", html);

        // 6. Post-Section Recorded Audio Verification Screen & Playback
        Assert.Contains("id=\"speaking-verification-screen\"", html);
        Assert.Contains("id=\"speaking-verification-items\"", html);
        Assert.Contains("renderSpeakingVerificationScreen", html);
        Assert.Contains("audio controls", html);
        Assert.Contains("Recorded [DONE]", html);
        Assert.Contains("No recording [!]", html);

        // 7. Final Submission CTA
        Assert.Contains("id=\"btn-submit-speaking-verification\"", html);
        Assert.Contains("Submit Speaking Phase for AI Grading", html);
    }

    [Fact]
    public void Take_View_DoesNotContain_Mojibake_And_Enforces_Scenario7_And_SplitScreen_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = ReadTakeSource(repoRoot);

        // 1. Zero Mojibake / double-encoded UTF-8 sequences
        string[] mojibakePatterns = ["Ã‚", "Ã¡", "Ã\xa0", "Ã£", "Ã©", "Ã¨", "Ã³", "Ã²", "Ãº", "Ã¹", "áº", "á»", "Æ°", "Äƒ", "Ä‘"];
        foreach (var pat in mojibakePatterns)
        {
            Assert.DoesNotContain(pat, html);
        }

        // Clean Vietnamese placarding
        Assert.Contains("Font & Contrast", html);
        Assert.Contains("READING PASSAGE & STIMULUS", html);
        Assert.Contains("QUESTION PALETTE", html);
        Assert.Contains("Unanswered", html);
        Assert.Contains("Answered", html);
        Assert.Contains("Current", html);
        Assert.Contains("Flagged", html);
        Assert.Contains("MAX", html);
        Assert.Contains("LETTERS & NUMBERS", html);
        Assert.Contains("Type your answer here...", html);

        // 2. Scenario 7: Clustered questions clean option list suppression
        Assert.Contains("if (q.clusterSize === 3)", html);
        Assert.Contains("sitting.optionList.style.display = 'none'", html);
        Assert.Contains("sitting.optionList.innerHTML = ''", html);

        // 3. Stage 2 Question 7: Split screen only grids exam-main-layout, keeping sitting-container fluid
        Assert.Contains("#exam-main-layout.exam-layout--split", html);
        Assert.DoesNotContain("sittingContainer.classList.add('exam-layout--split')", html);
        Assert.Contains("sittingContainer.style.maxWidth = '100%'", html);
        Assert.Contains("btn--danger.js-open-submit-modal", html);
        Assert.Contains("white-space: nowrap", html);

        // 4. Adaptive Stage Unit Locking: Future unit hidden, past units permanently locked
        Assert.Contains("isFutureUnit", html);
        Assert.Contains("palette-chip--locked", html);
        Assert.Contains("palette-chip--disabled", html);
    }

    [Fact]
    public void Take_Shell_Keeps_Executable_Assets_External_And_Data_In_Json_Islands()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewsDirectory = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams");
        var view = File.ReadAllText(Path.Combine(viewsDirectory, "Take.cshtml"));
        Assert.Contains("<partial name=\"_Take", view);
        foreach (var file in Directory.GetFiles(viewsDirectory, "*Take*.cshtml"))
        {
            var markup = File.ReadAllText(file);
            Assert.DoesNotMatch(@"<style\b", markup);
            Assert.DoesNotMatch(@"<script\b(?![^>]*(?:\bsrc=|type=""application/json""))", markup);
            Assert.DoesNotContain("JsonSerializer", markup);
            Assert.DoesNotMatch(@"\.(?:GroupBy|Select|Where|OrderBy|Take|Sum|Max|FirstOrDefault)\s*\(", markup);
            Assert.DoesNotMatch(@"new\s+\w+(?:Dto|Model)\s*\(", markup);
        }
        Assert.Contains("id=\"sitting-data\" type=\"application/json\"", view);
        var assets = Regex.Matches(view, @"(?:src|href)=""~/(?<asset>(?:js|css)/exams/[^""]+)""")
            .Select(match => match.Groups["asset"].Value).ToArray();
        Assert.Contains("css/exams/take.css", assets);
        var scripts = assets.Where(asset => asset.EndsWith(".js")).ToArray();
        Assert.True(scripts.Length > 1, "Sitting behavior must load its cohesive modules.");
        Assert.Equal("js/exams/context.js", scripts[0]);
        Assert.Equal("js/exams/shell.js", scripts[^1]);
        Assert.Equal(scripts.Length, scripts.Distinct().Count());
        foreach (var asset in assets)
        {
            var source = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", asset));
            Assert.DoesNotContain("@Html", source);
            Assert.DoesNotContain("@take", source);
            Assert.DoesNotContain("@q.", source);
        }
    }

    [Fact]
    public void Take_Model_Prepares_Fallbacks_Masked_Options_And_Progress()
    {
        var take = TakeFixture();
        var model = new ExamTakeViewModel(take, "");
        var display = model.Display;

        Assert.False(model.HasErrorMessage);
        Assert.Equal(33, display.ProgressPercent);
        Assert.Equal(2, display.UnansweredCount);
        Assert.Equal(3, display.Question.Options.Count);
        Assert.True(display.Question.Options[1].IsSelected);
        Assert.True(display.Question.HideStem);
        Assert.Equal(15, display.Question.PrepSeconds);
        Assert.Equal(30, display.Question.ResponseSeconds);
        Assert.Empty(display.Phases);
        Assert.True(display.AudioEquipment?.ShowOutput);
        Assert.False(display.AudioEquipment?.ShowInput);
        Assert.Equal("Question 2 · Answered · Flagged · Locked", display.Navigator[0].NavigatorTitle);
        using var questions = JsonDocument.Parse(display.QuestionsJson);
        Assert.Equal(1, questions.RootElement.GetArrayLength());
        Assert.Equal(12, questions.RootElement[0].GetProperty("questionId").GetInt32());
    }

    [Fact]
    public void Take_Model_Preserves_Cluster_Membership_And_Unmasked_Cluster_Option_Count()
    {
        var take = TakeFixture();
        var current = take.CurrentQuestion with { ClusterSize = 3 };
        var display = new ExamTakeViewModel(take with
        {
            CurrentQuestion = current,
            Questions = [current with { QuestionId = 11, DisplayOrder = 1 }, current,
                current with { QuestionId = 13, DisplayOrder = 3 }, current with { QuestionId = 14, DisplayOrder = 4 }]
        }).Display;

        Assert.Equal(new[] { 11, 12, 13 }, display.ClusterQuestions.Select(q => q.QuestionId));
        Assert.All(display.ClusterQuestions, q => Assert.Equal(4, q.Options.Count));
        var fallback = new ExamTakeViewModel(take with { CurrentQuestion = current, Questions = [] }).Display;
        Assert.Equal(12, Assert.Single(fallback.ClusterQuestions).QuestionId);
    }

    [Fact]
    public void Take_Model_Json_Islands_Escape_Script_Endings_And_RoundTrip_Content()
    {
        const string content = "</script><script>alert('exam')</script> & tiếng Việt";
        var take = TakeFixture();
        var current = take.CurrentQuestion with { Stem = content, AudioPath = content };
        var display = new ExamTakeViewModel(take with { ExamName = content, CandidateNotesJson = content, CurrentQuestion = current }).Display;

        Assert.DoesNotContain("</script", display.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</script", display.SittingJson, StringComparison.OrdinalIgnoreCase);
        using var payload = JsonDocument.Parse(display.SittingJson);
        Assert.Equal(content, payload.RootElement.GetProperty("examName").GetString());
        Assert.Equal(content, payload.RootElement.GetProperty("candidateNotesJson").GetString());
        Assert.Equal(content, payload.RootElement.GetProperty("currentQuestion").GetProperty("audioPath").GetString());
        Assert.Equal(take.ExpiresAt.ToUnixTimeMilliseconds(), payload.RootElement.GetProperty("expiresAt").GetInt64());
    }

    private static AttemptTakeDto TakeFixture()
    {
        var question = new AttemptQuestionDto(12, 2, 3, 7, "P2", "Section 2", "LIST", "Listening", ModalityDto.Listening,
            "Choose a response.", null, null, null, null, null, null, null,
            [new(1, "A", "First"), new(2, "B", "Second"), new(3, "C", "Third"), new(4, "D", "Fourth")],
            2, true, HideOptionText: true, OptionCount: 3);
        var start = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
        return new AttemptTakeDto(42, 5, "Exam", "TOEIC", AttemptStatusDto.InProgress, start, start.AddHours(1),
            3600, 3, 1, 1, question, [new(12, 2, 7, "P2", ModalityDto.Listening, true, true, true, IsLocked: true)], false);
    }

    private static string ReadTakeSource(string repoRoot)
    {
        var viewsDirectory = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams");
        string ReadMarkup(string name)
        {
            var markup = File.ReadAllText(Path.Combine(viewsDirectory, name + ".cshtml"));
            return Regex.Replace(markup, @"<partial\s+name=""(?<name>_Take[^""]+)""[^>]*/>",
                match => ReadMarkup(match.Groups["name"].Value));
        }

        var view = ReadMarkup("Take");
        var sources = Regex.Matches(view, @"(?:src|href)=""~/(?<asset>(?:js|css)/exams/[^""]+)""")
            .Select(match => File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", match.Groups["asset"].Value)));
        return view + Environment.NewLine + string.Join(Environment.NewLine, sources);
    }
}
