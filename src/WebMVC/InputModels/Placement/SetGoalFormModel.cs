using System.ComponentModel.DataAnnotations;

namespace WebMVC.Models;

/// <summary>
/// #153: the one-step goal form — certificate (the active one, shown not chosen), current band,
/// target band, exam date and weekly pace.
/// </summary>
public sealed class SetGoalFormModel
{
    public string? CurrentBandCode { get; set; }

    public string TargetBandCode { get; set; } = string.Empty;

    public DateOnly? TargetDate { get; set; }

    public decimal? WeeklyStudyHours { get; set; }
}
