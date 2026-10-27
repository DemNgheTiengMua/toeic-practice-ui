using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Contracts;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Regression tests for the sitting-room navigation and tooling defects recorded in
/// docs/specs/sitting-room-navigation-and-practice-qol-defects.md (tickets d01, d04, d05).
/// </summary>
public sealed class ExamSittingDefectsTests
{
    private static AttemptTakeDto TwoSkillTake()
    {
        var listening = new AttemptQuestionDto(11, 1, 3, 7, "P1", "Photographs", "LIST", "Listening", ModalityDto.Listening,
            "Look at the photograph.", null, null, null, null, null, null, null,
            [new(1, "A", "First"), new(2, "B", "Second"), new(3, "C", "Third"), new(4, "D", "Fourth")],
            1, false, HideOptionText: false, OptionCount: 4);
        var reading = new AttemptQuestionDto(12, 2, 9, 9, "P5", "Incomplete Sentences", "READ", "Reading", ModalityDto.Reading,
            "Choose the best word.", null, null, null, null, null, null, null,
            [new(5, "A", "before"), new(6, "B", "prior"), new(7, "C", "advance"), new(8, "D", "ahead")],
            3, false, HideOptionText: false, OptionCount: 4);
        var start = new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
        var phaseDeadline = start.AddMinutes(45);
        return new AttemptTakeDto(77, 1, "ETS", "TOEIC", AttemptStatusDto.InProgress, start, start.AddHours(2),
            7200, 2, 0, 0, listening,
            [
                new NavigatorItemDto(11, 1, 7, "P1", ModalityDto.Listening, false, false, true, IsLocked: false),
                new NavigatorItemDto(12, 2, 9, "P5", ModalityDto.Reading, false, false, false, IsLocked: true)
            ],
            false,
            Questions: [listening, reading],
            Phases:
            [
                new AttemptPhaseDto(1, "LIST", "Listening", 1, 0, 2700),
                new AttemptPhaseDto(2, "READ", "Reading", 2, 2700, 4500)
            ],
            ActiveSkillId: 9,
            ActiveUnitNumber: 1,
            ActiveGroupId: null,
            PhaseDeadlineAt: phaseDeadline);
    }

    [Fact]
    public void Sitting_Json_Carries_Server_Authoritative_Phase_State()
    {
        var display = new ExamTakeViewModel(TwoSkillTake()).Display;

        using var payload = JsonDocument.Parse(display.SittingJson);
        var root = payload.RootElement;

        Assert.Equal(9, root.GetProperty("activeSkillId").GetInt32());
        Assert.Equal(1, root.GetProperty("activeUnitNumber").GetInt32());
        Assert.True(root.TryGetProperty("phaseDeadlineAt", out var deadline), "sitting-data must expose the server phase deadline");
        Assert.Equal(DateTimeOffset.Parse("2026-10-27T08:45:00+00:00").ToUnixTimeMilliseconds(), deadline.GetInt64());
    }

    [Fact]
    public void Sitting_Shell_Never_Derives_Phase_From_Wall_Clock()
    {
        var source = ReadSittingSource();

        // The 1-second timer must not reassign the active phase or re-render the current question:
        // the server's ActiveSkillId/PhaseDeadlineAt is the only phase authority (ticket d01).
        var renderPhaseProgress = ExtractFunctionBody(source, "renderPhaseProgress");
        Assert.DoesNotContain("renderQuestion(", renderPhaseProgress, StringComparison.Ordinal);
        Assert.DoesNotContain("activePhaseIndex =", renderPhaseProgress, StringComparison.Ordinal);

        var updateTimer = ExtractFunctionBody(source, "updateTimer");
        Assert.DoesNotContain("activePhaseIndex =", updateTimer, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase_Advance_Control_Replaces_The_Dead_Next_Phase_Button()
    {
        var source = ReadSittingSource();

        // ticket d02: a candidate on the last question of a skill must get an enabled advance action,
        // never a permanently disabled "next phase starts when time expires" placeholder.
        Assert.DoesNotContain("NEXT PHASE STARTS WHEN TIME EXPIRES", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("js-open-skill-advance", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Tokenized_Text_Container_Is_Filled_Through_The_Tokenizer()
    {
        var source = ReadSittingSource();

        // ticket d04: the tokenized marker must only be produced by the code path that emits tokens.
        Assert.DoesNotContain("question__stem ee-tokenized-text", source, StringComparison.Ordinal);

        var renderQuestion = ExtractFunctionBody(source, "renderQuestion");
        Assert.Contains("renderTokenizedText", renderQuestion, StringComparison.Ordinal);
    }

    [Fact]
    public void Locked_Palette_Chips_Expose_An_Accessible_Reason()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var palette = File.ReadAllText(Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "_TakePalette.cshtml"));

        // ticket d05: hover-only title text is not an accessible explanation.
        Assert.Contains("aria-disabled", palette, StringComparison.Ordinal);
        Assert.Contains("aria-describedby", palette, StringComparison.Ordinal);
        Assert.Contains("palette-chip--locked", palette, StringComparison.Ordinal);
    }

    private static string ReadSittingSource()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var jsDirectory = Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "js", "exams");
        var scriptPaths = new[] { "phases.js", "navigation.js", "annotations.js", "shell.js", "context.js", "speaking.js" }
            .Select(name => Path.Combine(jsDirectory, name))
            .Where(File.Exists)
            .ToList();

        Assert.NotEmpty(scriptPaths);
        return string.Join(Environment.NewLine, scriptPaths.Select(File.ReadAllText));
    }

    /// <summary>
    /// Extracts the body of an assigned function (e.g. "sitting.renderPhaseProgress = function (...) { ... };").
    /// Bodies are brace-balanced so nested callbacks stay inside the returned slice.
    /// </summary>
    private static string ExtractFunctionBody(string source, string memberName)
    {
        var match = Regex.Match(source, @"\." + Regex.Escape(memberName) + @"\s*=\s*function[^{]*\{");
        Assert.True(match.Success, $"Could not locate the '{memberName}' function in the sitting scripts");

        var start = match.Index + match.Length;
        var depth = 1;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source[start..i];
            }
        }

        Assert.Fail($"Unbalanced braces while reading '{memberName}'");
        return string.Empty;
    }
}
