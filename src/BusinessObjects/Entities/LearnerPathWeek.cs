using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #233 / D10: a week that has closed, frozen.
/// <para>
/// The archived week is a <b>row</b>, not a flag on the plan: a flag nobody checks is the failure the
/// ticket names. The numbers are copied in at the instant the week closes and the trigger on this
/// table refuses every UPDATE and DELETE, so a late answer arriving for one of the week's steps has
/// nowhere to write its change.
/// </para>
/// </summary>
[Index("LearnerPathId", "WeekNumber", Name = "UQ_PathWeeks_Path_Week", IsUnique = true)]
public partial class LearnerPathWeek
{
    [Key]
    public int LearnerPathWeekId { get; set; }

    public int LearnerPathId { get; set; }

    /// <summary>Carried down, so an archived week is pinned to the certificate its path belongs to.</summary>
    public int CertId { get; set; }

    /// <summary>The path's own week number. A number that slides out of the live window is what makes
    /// "which week was that" answerable a year later — <c>WeekNumber</c> alone on a step is not.</summary>
    public int WeekNumber { get; set; }

    /// <summary>The calendar span the week was open for.</summary>
    public DateOnly StartedOn { get; set; }

    public DateOnly ClosedOn { get; set; }

    /// <summary>Which close condition fired: <c>all_done</c> or <c>calendar</c>. Both close the week;
    /// only one of them says the learner finished the work.</summary>
    [StringLength(16)]
    [Unicode(false)]
    public string CloseReason { get; set; } = null!;

    public int PlannedSteps { get; set; }

    public int CompletedSteps { get; set; }

    public int PlannedMinutes { get; set; }

    /// <summary>Measured, never guessed. NULL when no step of the week produced a score.</summary>
    [Column(TypeName = "decimal(5, 2)")]
    public decimal? AverageAccuracyPct { get; set; }

    /// <summary>The topics the week left unmastered, as JSON, so "re-queued" is checkable against
    /// what was archived rather than against what the new week happens to contain.</summary>
    public string? RequeuedTopicIdsJson { get; set; }

    public DateTimeOffset ArchivedAt { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string? WrittenByService { get; set; }

    public int ProjectionVersion { get; set; }

    [ForeignKey("LearnerPathId, CertId")]
    [InverseProperty("LearnerPathWeeks")]
    public virtual LearnerPath LearnerPath { get; set; } = null!;
}

/// <summary>The serialised shape of <see cref="LearnerPathWeek.RequeuedTopicIdsJson"/>. One definition,
/// so the writer and the reader cannot disagree about it.</summary>
public static class RequeuedTopics
{
    public static string Serialize(IReadOnlyList<int> topicIds) =>
        System.Text.Json.JsonSerializer.Serialize(topicIds);

    public static IReadOnlyList<int> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : System.Text.Json.JsonSerializer.Deserialize<List<int>>(json) ?? [];
}
