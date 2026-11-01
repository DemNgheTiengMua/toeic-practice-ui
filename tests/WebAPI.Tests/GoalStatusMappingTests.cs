using System.Reflection;
using BusinessObjects;
using Contracts;
using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #156: <c>/placement/target-band</c> answered 500 for any learner whose goal had no deadline.
/// <see cref="GoalStatus.Status"/> is null in exactly that case, and the mapper passed it to
/// <c>MapTo&lt;FeasibilityStatusDto&gt;()</c>, which throws on a null enum. The page it broke is the
/// one where a learner sets a goal, so the failure hit the flow it was supposed to support.
/// </summary>
public sealed class GoalStatusMappingTests
{
    [Fact]
    public void A_goal_with_no_deadline_maps_to_a_null_status_instead_of_throwing()
    {
        var goal = new GoalStatus(
            CurrentBandCode: "B1",
            TargetBandCode: "B2",
            TargetDate: null,
            WeeklyStudyHours: null,
            RequiredHours: null,
            AvailableHours: null,
            Status: null,
            SuggestedWeeklyHours: null,
            EarliestFeasibleDate: null);

        var dto = Map(goal);

        Assert.NotNull(dto);
        Assert.Null(dto!.Status);
        Assert.Equal("B2", dto.TargetBandCode);
    }

    [Fact]
    public void A_goal_with_a_verdict_still_carries_it()
    {
        var goal = new GoalStatus("B1", "B2", new DateOnly(2027, 5, 31), 10m, 300, 310m,
            FeasibilityStatus.Feasible, 6, null);

        var dto = Map(goal);

        Assert.Equal(FeasibilityStatusDto.Feasible, dto!.Status);
        Assert.Equal(300, dto.RequiredHours);
    }

    [Fact]
    public void No_goal_stays_no_goal()
    {
        Assert.Null(Map(null));
    }

    private static GoalStatusDto? Map(GoalStatus? goal) =>
        (GoalStatusDto?)typeof(PlacementEndpoints)
            .GetMethod("ToGoalStatusDto", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [goal]);
}
