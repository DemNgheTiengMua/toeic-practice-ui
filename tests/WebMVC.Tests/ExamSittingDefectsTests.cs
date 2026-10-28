using System.Text.Json;
using Contracts;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Regression tests for the sitting-room phase state the shell consumes
/// (docs/specs/sitting-room-navigation-and-practice-qol-defects.md, ticket d01).
/// </summary>
/// <remarks>
/// Deliberately narrow. The presentation layer's only job here is to hand the browser an
/// authoritative phase snapshot. The shell behaviours these defects were about — advancing at the
/// last question of a skill, not rewinding on a timer tick, tokenizing stems for the highlighter,
/// locked-palette announcements — are covered by
/// WebMVC.E2ETests/Journeys/SittingAndPracticeQolTests.cs, which drives a real browser. The
/// source-grep tests that used to live here asserted on substrings of concatenated .js/.cshtml
/// files: they passed while every one of those defects was live, so they were removed rather than
/// kept as false assurance.
/// </remarks>
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

    /// <summary>
    /// The navigator drives which questions the shell lets a candidate enter, so a question from a
    /// skill the server is not currently serving must be reported locked, not merely unanswered
    /// (ticket d05).
    /// </summary>
    [Fact]
    public void Navigator_Marks_Non_Active_Skill_Questions_As_Locked()
    {
        var display = new ExamTakeViewModel(TwoSkillTake()).Display;

        var activeSkillItem = display.Navigator.Single(item => item.DisplayOrder == 1);
        var otherSkillItem = display.Navigator.Single(item => item.DisplayOrder == 2);

        Assert.False(activeSkillItem.IsLocked);
        Assert.True(otherSkillItem.IsLocked,
            "A question belonging to a skill the server is not serving must be locked.");
    }

    /// <summary>
    /// A two-skill sitting must describe both phases, in order, with contiguous offsets — the
    /// advance control (ticket d02) resolves the next skill from this list.
    /// </summary>
    [Fact]
    public void Phases_Describe_Each_Skill_In_Order_With_Contiguous_Offsets()
    {
        var display = new ExamTakeViewModel(TwoSkillTake()).Display;

        using var payload = JsonDocument.Parse(display.PhasesJson);
        var phases = payload.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, phases.Count);
        Assert.Equal("LIST", phases[0].GetProperty("skillCode").GetString());
        Assert.Equal("READ", phases[1].GetProperty("skillCode").GetString());
        Assert.Equal(
            phases[0].GetProperty("startOffsetSeconds").GetInt32() + phases[0].GetProperty("durationSeconds").GetInt32(),
            phases[1].GetProperty("startOffsetSeconds").GetInt32());

        // Exactly one phase is marked current, and it is the one the server reports as active.
        Assert.Equal("step", display.Phases[0].CurrentStep);
        Assert.Equal("false", display.Phases[1].CurrentStep);
    }
}
