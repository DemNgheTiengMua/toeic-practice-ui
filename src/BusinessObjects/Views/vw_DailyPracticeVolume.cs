using System;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Views;

/// <summary>
/// #235 / D12 / PLC-64: the daily ceiling, derived from the answers themselves — one row per learner per
/// UTC day they answered anything. <b>No stored counter</b>, so a recount cannot disagree with it
/// (NFI-05), and a session row written before anything was answered charges the learner nothing.
/// </summary>
[Keyless]
public partial class vw_DailyPracticeVolume
{
    public int UserId { get; set; }

    /// <summary>The UTC day (<c>PracticeAnswers.AnsweredAt</c> holds UTC).</summary>
    public DateTime PractisedOn { get; set; }

    /// <summary>Answered items today — the ceiling's volume axis.</summary>
    public int AnsweredCount { get; set; }

    /// <summary>Answered productive tasks today, whose ceiling is 4 rather than 50.</summary>
    public int ProductiveTaskCount { get; set; }

    /// <summary>Pool cells answered in today: "one session per cell per day" (PLC-64).</summary>
    public int DistinctCells { get; set; }
}
