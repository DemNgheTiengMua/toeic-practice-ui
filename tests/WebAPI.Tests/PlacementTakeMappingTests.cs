using System.Reflection;
using BusinessObjects;
using Contracts;
using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #134: the placement sitting decides where a locked part ends (and so where Next must confirm)
/// from each question's navigation mode and question group. The placement mapper used to copy only
/// the current question's mode and drop the group from every question, so the sitting never saw
/// the boundary between two locked writing tasks.
/// </summary>
public sealed class PlacementTakeMappingTests
{
    [Fact]
    public void Every_question_keeps_its_navigation_mode_group_and_unit()
    {
        var first = Writing(31, 1, groupId: 54);
        var second = Writing(32, 2, groupId: 55);
        var take = new PlacementTakeView(
            7, 100, 14, "Diagnostic", "TOEIC", PlacementStatus.InProgress,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), 600, 2, 0, 0,
            first, [], false, [first, second]);

        var dto = Map(take);

        Assert.Equal(54, dto.CurrentQuestion.GroupId);
        Assert.Equal("HYBRID_GROUP_LOCKED", dto.CurrentQuestion.NavigationMode);
        Assert.Equal(new int?[] { 54, 55 }, dto.Questions!.Select(q => q.GroupId));
        Assert.All(dto.Questions!, q => Assert.Equal("HYBRID_GROUP_LOCKED", q.NavigationMode));
        Assert.All(dto.Questions!, q => Assert.Equal(2, q.UnitNumber));
        Assert.All(dto.Questions!, q => Assert.Equal("{\"wordCount\":true}", q.ToolsConfigJson));
    }

    private static AttemptQuestionInfo Writing(int id, int order, int groupId) => new(
        id, order, 2, 14, "W2", "Writing", "WRIT", "Writing", Modality.Writing, "Write.",
        null, null, null, null, null, null, null, [], null, false, "free_response",
        NavigationMode: "HYBRID_GROUP_LOCKED", ToolsConfigJson: "{\"wordCount\":true}",
        UnitNumber: 2, GroupId: groupId);

    private static PlacementTakeViewDto Map(PlacementTakeView take) =>
        (PlacementTakeViewDto)typeof(PlacementEndpoints)
            .GetMethod("ToPlacementTakeViewDto", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [take])!;
}
