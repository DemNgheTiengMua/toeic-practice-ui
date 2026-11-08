using System.Reflection;
using BusinessObjects;
using Contracts;
using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #217: the per-skill status crosses the wire as the snake_case code the schema stores, never as the
/// CLR member name.
/// <para>
/// <b>Why this is a separate test.</b> <c>PlacementResultViewModel</c> compares against the codes
/// (<c>"grading"</c>, <c>"not_measured"</c>, <c>"measured"</c>, <c>"estimated_from_one_task"</c>). A
/// mapper that emits <c>status.ToString()</c> sends <c>"Grading"</c> / <c>"NotMeasured"</c>, so:
/// </para>
/// <list type="bullet">
/// <item>a skill still being graded falls through <c>LevelCell</c> to "Not measured", which is the
/// exact lie PLC-19 exists to prevent;</item>
/// <item><c>CanRetryGrading</c> never fires, so the free retry is unreachable;</item>
/// <item>the partial-result banner never appears.</item>
/// </list>
/// <para>
/// The existing view tests all construct the DTO by hand with the correct codes, which is why a
/// fixture that agrees with the test and not with the endpoint passed the whole wave.
/// </para>
/// </summary>
public sealed class PlacementSkillStatusWireCodeTests
{
    [Fact]
    public void A_grading_skill_crosses_the_wire_as_the_code_the_view_compares_against()
    {
        var dto = Map(PlacementResultStatus.Grading);

        var status = Assert.Single(dto.SkillResults!).Status;

        // The input that would pass a weaker test: asserting the string is non-empty, or that it
        // contains "rading" case-insensitively. Both pass "Grading" and "grading".
        Assert.Equal("grading", status);
        Assert.NotEqual(PlacementResultStatus.Grading.ToString(), status);
    }

    [Fact]
    public void Every_placement_result_status_crosses_the_wire_as_its_snake_case_code()
    {
        // Enumerated, not spot-checked: a mapper that special-cases one value still fails the next.
        foreach (var value in Enum.GetValues<PlacementResultStatus>())
        {
            var dto = Map(value);
            var status = Assert.Single(dto.SkillResults!).Status;
            Assert.Equal(value.ToCode(), status);
        }
    }

    private static PlacementResultViewDto Map(PlacementResultStatus status)
    {
        var result = new PlacementResultView(
            1, 2, 3, "TOEIC", "Diagnostic", DateTimeOffset.UnixEpoch, 120, 40, 500,
            "B1", "Band B1", [], [],
            WeakerSkillName: null,
            BandNote: null,
            SkillResults:
            [
                new PlacementSkillResultInfo(
                    SkillId: 10,
                    SkillCode: "reading",
                    SkillName: "Reading",
                    Status: status,
                    Level: null,
                    PositionInBand: null,
                    MeasuredAt: null,
                    ReusedFromPrior: false,
                    FailureReason: null),
            ]);

        return (PlacementResultViewDto)typeof(PlacementEndpoints)
            .GetMethod("ToPlacementResultDto", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [result])!;
    }
}
