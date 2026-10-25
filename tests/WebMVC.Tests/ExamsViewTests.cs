using System.IO;
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
        var html = File.ReadAllText(viewPath);

        // 1. Embedded JSON payload for 0ms in-memory swapping
        Assert.Contains("id=\"exam-data\"", html);

        // 2. Exam Integrity Disciplinary Warning Banner
        Assert.Contains("id=\"integrity-warning\"", html);
        Assert.Contains("id=\"integrity-count\"", html);

        // 3. Offline Network Banner
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
        Assert.Contains("visibilitychange", html);
    }

    [Fact]
    public void Take_View_Contains_SplitScreen_TopControlBar_Palette_Highlighter_And_ComponentWidgets_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = File.ReadAllText(viewPath);

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
        var html = File.ReadAllText(viewPath);

        // 1. Masked styling classes
        Assert.Contains(".option-list--masked", html);
        Assert.Contains(".option--masked", html);

        // 2. HideOptionText & OptionCount bindings
        Assert.Contains("option-list--masked", html);
        Assert.Contains("q.HideOptionText", html);
        Assert.Contains("q.OptionCount", html);
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
        var html = File.ReadAllText(viewPath);

        // 1. Clustered audio container and card items
        Assert.Contains("id=\"cluster-card-container\"", html);
        Assert.Contains(".cluster-card-container", html);
        Assert.Contains(".cluster-item", html);
        Assert.Contains("q.ClusterSize == 3", html);
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
        var html = File.ReadAllText(viewPath);

        // 1. AUDIO_DRIVEN mode checks
        Assert.Contains("AUDIO_DRIVEN", html);
        Assert.Contains("isAudioDriven", html);

        // 2. Previous button disabled/hidden in audio-driven mode
        Assert.Contains("btnPrev.style.display = 'none'", html);
        Assert.Contains("btnPrev.disabled = !phase", html);

        // 3. Jumping backward blocked in goToQuestion
        Assert.Contains("if (isOneWay && targetOrder < currentDisplayOrder) return;", html);

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
        var html = File.ReadAllText(viewPath);

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
        var html = File.ReadAllText(viewPath);

        // 1. UNIT_LOCKED_SANDBOX checks
        Assert.Contains("UNIT_LOCKED_SANDBOX", html);
        Assert.Contains("activeUnitNumber", html);
        Assert.Contains("isUnitLocked", html);

        // 2. Unit Lock Confirmation Modal and warning text
        Assert.Contains("id=\"unit-lock-modal\"", html);
        Assert.Contains("Sau khi xác nhận, Giai đoạn 1 sẽ bị khóa vĩnh viễn và bạn không thể quay lại.", html);
        Assert.Contains("id=\"btn-confirm-unit-lock\"", html);

        // 3. Transition call to endpoint
        Assert.Contains("/exams/attempts/' + attemptId + '/transition-unit", html);
        Assert.Contains("targetUnitNumber: 2", html);

        // 4. Finish Unit 1 button on final question
        Assert.Contains("Hoàn thành Giai đoạn 1 (Finish Unit 1)", html);
        Assert.Contains("js-open-unit-lock-modal", html);
    }

    [Fact]
    public void Take_View_Contains_Writing_Editor_Live_Word_Counter_And_Spellcheck_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = File.ReadAllText(viewPath);

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
        Assert.Contains("Tối thiểu: 0 từ", html);
        Assert.Contains("id=\"under-length-warning\"", html);
        Assert.Contains("(Chưa đạt số từ tối thiểu)", html);
        Assert.Contains("minWords", html);
    }

    [Fact]
    public void Take_View_Contains_Restricted_Clipboard_And_OnScreen_CutPaste_Toolbar_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = File.ReadAllText(viewPath);

        // 1. On-screen editing toolbar markup & buttons
        Assert.Contains("id=\"writing-onscreen-toolbar\"", html);
        Assert.Contains("id=\"btn-writing-cut\"", html);
        Assert.Contains("id=\"btn-writing-paste\"", html);
        Assert.Contains("id=\"btn-writing-undo\"", html);
        Assert.Contains("id=\"btn-writing-redo\"", html);
        Assert.Contains("Cắt (Cut)", html);
        Assert.Contains("Dán (Paste)", html);
        Assert.Contains("Hoàn tác (Undo)", html);
        Assert.Contains("Làm lại (Redo)", html);

        // 2. Clipboard and undo/redo state management
        Assert.Contains("internalClipboard", html);
        Assert.Contains("undoStack", html);
        Assert.Contains("redoStack", html);
        Assert.Contains("pushUndoState", html);

        // 3. Shortcut interception & keyboard suppression
        Assert.Contains("isCutPasteRestricted", html);
        Assert.Contains("allowKeyboardCutPaste", html);
        Assert.Contains("showOnScreenCutPaste", html);
        Assert.Contains("writingEditor.addEventListener('keydown'", html);
        Assert.Contains("writingEditor.addEventListener('cut'", html);
        Assert.Contains("writingEditor.addEventListener('copy'", html);
        Assert.Contains("writingEditor.addEventListener('paste'", html);
    }

    [Fact]
    public void Take_View_Contains_Required_Keywords_Badges_And_Live_Lemma_Detection_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = File.ReadAllText(viewPath);

        // 1. Required keywords area and badge container
        Assert.Contains("id=\"required-keywords-area\"", html);
        Assert.Contains("id=\"required-keywords-list\"", html);
        Assert.Contains("Từ khóa bắt buộc:", html);

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
        var html = File.ReadAllText(viewPath);

        // 1. NavigationMode HYBRID_GROUP_LOCKED check
        Assert.Contains("HYBRID_GROUP_LOCKED", html);
        Assert.Contains("activeGroupId", html);

        // 2. Group Lock Confirmation Modal & required exact warning text
        Assert.Contains("id=\"group-lock-modal\"", html);
        Assert.Contains("Xác nhận chuyển sang câu tiếp theo: Phần hiện tại sẽ bị khóa vĩnh viễn và thời gian còn lại sẽ không được bảo lưu.", html);
        Assert.Contains("id=\"btn-confirm-group-lock\"", html);
        Assert.Contains("js-close-group-lock-modal", html);

        // 3. Transition call to endpoint
        Assert.Contains("/exams/attempts/' + attemptId + '/transition-group", html);
        Assert.Contains("targetGroupId: pendingTargetGroupId", html);
    }

    [Fact]
    public void Take_View_Contains_Speaking_Pacer_State_Machine_And_Verification_Screen_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = File.ReadAllText(viewPath);

        // 1. NavigationMode SYSTEM_PACED_AUTO check & navigation suppression
        Assert.Contains("SYSTEM_PACED_AUTO", html);
        Assert.Contains("q.NavigationMode == \"SYSTEM_PACED_AUTO\"", html);
        Assert.Contains("isSystemPacedAuto", html);

        // 2. Prep Countdown placard, timer and visual progress bar
        Assert.Contains("id=\"speaking-pacer-container\"", html);
        Assert.Contains("id=\"speaking-prep-placard\"", html);
        Assert.Contains("id=\"speaking-prep-timer\"", html);
        Assert.Contains("id=\"speaking-prep-bar\"", html);
        Assert.Contains("CHUẨN BỊ TRẢ LỜI:", html);

        // 3. Audible beep cue using Web Audio API (1000Hz for 300ms)
        Assert.Contains("playBeepCue", html);
        Assert.Contains("AudioContext", html);
        Assert.Contains("1000", html);
        Assert.Contains("0.3", html);

        // 4. Recording Countdown placard, flashing indicator & audio level meter
        Assert.Contains("id=\"speaking-record-placard\"", html);
        Assert.Contains("id=\"speaking-record-timer\"", html);
        Assert.Contains("id=\"speaking-record-bar\"", html);
        Assert.Contains("ĐANG THU ÂM:", html);
        Assert.Contains("id=\"audio-level-container\"", html);
        Assert.Contains("id=\"audio-level-meter\"", html);
        Assert.Contains("id=\"audio-level-bar\"", html);
        Assert.Contains("createAnalyser", html);

        // 5. Background upload endpoint call
        Assert.Contains("/exams/take/' + attemptId + '/upload-audio", html);

        // 6. Post-Section Recorded Audio Verification Screen & Playback
        Assert.Contains("id=\"speaking-verification-screen\"", html);
        Assert.Contains("id=\"speaking-verification-items\"", html);
        Assert.Contains("renderSpeakingVerificationScreen", html);
        Assert.Contains("audio controls", html);
        Assert.Contains("Đã ghi âm [✓]", html);
        Assert.Contains("Chưa có ghi âm [!]", html);

        // 7. Final Submission CTA
        Assert.Contains("id=\"btn-submit-speaking-verification\"", html);
        Assert.Contains("Nộp phần thi Nói để chấm AI", html);
    }
}
