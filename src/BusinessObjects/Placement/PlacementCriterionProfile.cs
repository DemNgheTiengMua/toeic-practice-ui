using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BusinessObjects;

/// <summary>
/// #219 / D5: one rubric criterion as the productive half scored it.
/// </summary>
/// <param name="CriterionId">The criterion the grader scored.</param>
/// <param name="Code">The criterion's own code ("GRAM", "TR"), so a reader names it without a join.</param>
/// <param name="Score">The score awarded.</param>
/// <param name="MaxScore">The ceiling it was scored against, so "3 of 6" is readable.</param>
public sealed record PlacementCriterionScore(
    int CriterionId,
    string Code,
    decimal Score,
    decimal MaxScore);

/// <summary>
/// #219 / D5: the productive half's rubric profile, stored on the per-skill result so the path starts
/// from a named weakest criterion instead of recomputing it from the grading rows.
/// <para>
/// <b>The weakest criterion is one of the scores, never an invented code.</b> It is the lowest
/// <c>Score / MaxScore</c> ratio — a ratio rather than a raw score, because a section may cap a
/// criterion below its table ceiling (TOEIC Speaking scores 0–3 on criteria IELTS scores 0–6), and
/// comparing 2/3 with 3/6 as if they were one axis is the mistake the certificate-level band already
/// made.
/// </para>
/// <para>
/// <c>PriorityFix</c> is the grader's own one-line advice, copied rather than generated: it is the
/// only place in this system that knows what to do about the weakest criterion.
/// </para>
/// </summary>
public sealed record PlacementCriterionProfile(
    IReadOnlyList<PlacementCriterionScore> Scores,
    string? WeakestCriterion,
    string? PriorityFix)
{
    private static readonly JsonSerializerOptions Storage = new(JsonSerializerDefaults.Web);

    public static string? Serialize(PlacementCriterionProfile? profile) =>
        profile is null || profile.Scores.Count == 0 ? null : JsonSerializer.Serialize(profile, Storage);

    public static PlacementCriterionProfile? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PlacementCriterionProfile>(json, Storage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The profile of one skill's graded tasks: the criterion scores, and the one with the lowest
    /// proportion of its ceiling. Null when nothing was scored — a skill with no rubric scoring has no
    /// weakest criterion, and naming one would invent a fact.
    /// <para>
    /// Averaging happens per criterion, because one task's 4 of 6 and another's 2 of 6 describe the
    /// same criterion. Ties break on the lowest criterion id, so two runs over the same gradings name
    /// the same criterion.
    /// </para>
    /// </summary>
    public static PlacementCriterionProfile? FromScores(
        IEnumerable<PlacementCriterionScore> scores,
        string? priorityFix)
    {
        var averaged = scores
            .GroupBy(s => (s.CriterionId, s.Code, s.MaxScore))
            .Select(g => new PlacementCriterionScore(
                g.Key.CriterionId, g.Key.Code, g.Average(s => s.Score), g.Key.MaxScore))
            .OrderBy(s => s.CriterionId)
            .ToList();

        if (averaged.Count == 0)
        {
            return null;
        }

        var weakest = averaged
            .OrderBy(s => s.MaxScore <= 0 ? decimal.MaxValue : s.Score / s.MaxScore)
            .ThenBy(s => s.CriterionId)
            .First();

        return new PlacementCriterionProfile(averaged, weakest.Code, priorityFix);
    }
}
