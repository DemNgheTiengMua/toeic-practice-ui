using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #214 / D7, PLC-22: the fixed stage-one blueprint, as arithmetic.
/// <para>
/// The blueprint is the one place the distribution lives, so every assertion here is about the table
/// itself: the row that no selector can see (a band silently dropped), the sum, and the drawing rule
/// that lets a one-item band be served from a three-item bundle.
/// </para>
/// <para>
/// The trap this file is written to avoid: a blueprint tested only by total (8) passes while every
/// item sits at B1. Each assertion therefore names the band and its own count.
/// </para>
/// </summary>
public sealed class PlacementStageOneBlueprintTests
{
    [Fact]
    public void Stage_one_is_one_one_two_two_one_one_across_the_six_bands()
    {
        var expected = new (CefrLevel Level, int Count)[]
        {
            (CefrLevel.A1, 1),
            (CefrLevel.A2, 1),
            (CefrLevel.B1, 2),
            (CefrLevel.B2, 2),
            (CefrLevel.C1, 1),
            (CefrLevel.C2, 1),
        };

        Assert.Equal(expected, PlacementStageOneBlueprint.Distribution);
    }

    [Fact]
    public void Stage_one_is_eight_items_and_both_ends_are_present()
    {
        Assert.Equal(8, PlacementStageOneBlueprint.ItemsPerObjectiveSkill);
        Assert.Equal(1, PlacementStageOneBlueprint.CountAt(CefrLevel.A1));
        Assert.Equal(1, PlacementStageOneBlueprint.CountAt(CefrLevel.C2));
        // The floor is reportable, never authored, so a blueprint may not ask for it.
        Assert.Equal(0, PlacementStageOneBlueprint.CountAt(CefrLevel.PreA1));
    }

    [Fact]
    public void A_full_pool_yields_the_blueprint_and_no_deficits()
    {
        var candidates = new List<PlacementStageOneCandidate>();
        var id = 1;
        foreach (var (level, count) in PlacementStageOneBlueprint.Distribution)
        {
            for (var i = 0; i < count; i++)
            {
                candidates.Add(new PlacementStageOneCandidate(id++, level));
            }
        }

        var selection = PlacementStageOneBlueprint.Select(candidates);

        Assert.Equal(8, selection.ItemCount);
        Assert.True(selection.IsComplete);
        Assert.Empty(selection.Deficits);
        // Eight distinct items, not eight copies of the first one.
        Assert.Equal(8, selection.Items.Select(item => item.ItemId).Distinct().Count());
        Assert.Equal(1, selection.Items.Count(item => item.Level == CefrLevel.A1));
        Assert.Equal(2, selection.Items.Count(item => item.Level == CefrLevel.B1));
        Assert.Equal(1, selection.Items.Count(item => item.Level == CefrLevel.C2));
    }

    /// <summary>
    /// Criterion 2: a stage-one draw can serve a subset of a stimulus's children, so a one-item draw
    /// is possible from a three-item bundle. The input that passes this while being wrong is a pool
    /// whose three children share one stimulus: a selector that treats a stimulus as its unit would
    /// return zero items at that band, or all three.
    /// </summary>
    [Fact]
    public void One_item_is_drawn_from_a_three_child_bundle()
    {
        const int bundle = 900;
        var candidates = new List<PlacementStageOneCandidate>
        {
            new(1, CefrLevel.A1, bundle, 1),
            new(2, CefrLevel.A1, bundle, 2),
            new(3, CefrLevel.A1, bundle, 3),
        };

        var selection = PlacementStageOneBlueprint.Select(
            candidates,
            [(CefrLevel.A1, 1)]);

        Assert.Equal(1, selection.ItemCount);
        Assert.True(selection.IsComplete);
        Assert.Equal(bundle, selection.Items[0].StimulusId);
    }

    /// <summary>
    /// PLC-26: no substitution across bands. The input that passes this while being wrong is a
    /// neighbouring band that is over-full - an A2 pool of ten can silently fill a C2 slot and every
    /// count in the result would still sum to eight.
    /// </summary>
    [Fact]
    public void A_short_band_is_reported_and_never_filled_from_a_neighbour()
    {
        var candidates = PlacementStageOneBlueprint.Distribution
            .SelectMany((band, index) => Enumerable.Range(0, band.Count)
                .Select(offset => new PlacementStageOneCandidate(index * 10 + offset + 1, band.Level)))
            .Where(candidate => candidate.Level != CefrLevel.C2)
            .ToList();
        // Plenty of A2 to spare: the trap is using it to fill C2.
        candidates.AddRange(Enumerable.Range(100, 10)
            .Select(id => new PlacementStageOneCandidate(id, CefrLevel.A2)));

        var selection = PlacementStageOneBlueprint.Select(candidates);

        Assert.False(selection.IsComplete);
        var missing = Assert.Single(selection.Deficits);
        Assert.Equal(CefrLevel.C2, missing.Level);
        Assert.Equal(1, missing.Missing);
        Assert.Equal(7, selection.ItemCount);
        Assert.DoesNotContain(selection.Items, item => item.ItemId >= 100);
    }

    /// <summary>
    /// The same input, drawn twice, is the same paper. A draw that reshuffled would move a candidate's
    /// sitting under them on reload, and the resume would show a different item than the one saved.
    /// </summary>
    [Fact]
    public void The_same_candidates_draw_the_same_items_in_the_same_order()
    {
        var candidates = new List<PlacementStageOneCandidate>
        {
            new(50, CefrLevel.B1),
            new(10, CefrLevel.B1),
            new(30, CefrLevel.B1),
            new(70, CefrLevel.A1),
        };

        var first = PlacementStageOneBlueprint.Select(candidates, [(CefrLevel.A1, 1), (CefrLevel.B1, 2)]);
        var again = PlacementStageOneBlueprint.Select(candidates, [(CefrLevel.A1, 1), (CefrLevel.B1, 2)]);

        Assert.Equal(
            first.Items.Select(item => item.ItemId),
            again.Items.Select(item => item.ItemId));
    }

    [Fact]
    public void An_item_listed_twice_is_served_once()
    {
        var repeated = new PlacementStageOneCandidate(5, CefrLevel.B2);

        var selection = PlacementStageOneBlueprint.Select(
            [repeated, repeated],
            [(CefrLevel.B2, 2)]);

        Assert.Equal(1, selection.ItemCount);
        Assert.Equal(1, Assert.Single(selection.Deficits).Missing);
    }

    [Fact]
    public void Only_listening_and_reading_are_drawn_for_in_stage_one()
    {
        Assert.True(PlacementStageOneBlueprint.IsObjective(Modality.Listening));
        Assert.True(PlacementStageOneBlueprint.IsObjective(Modality.Reading));
        Assert.False(PlacementStageOneBlueprint.IsObjective(Modality.Speaking));
        Assert.False(PlacementStageOneBlueprint.IsObjective(Modality.Writing));
    }
}
