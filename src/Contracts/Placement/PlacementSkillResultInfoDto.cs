using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #213 / story 55: one skill of a finished placement, per skill rather than per page, so a learner can
/// tell which of several numbers is a carried-over one and how old it is.
/// </summary>
/// <param name="SkillId">The certificate's skill.</param>
/// <param name="SkillCode">Its code.</param>
/// <param name="SkillName">Its display name.</param>
/// <param name="Status">measured, estimated_from_one_task, grading, not_measured or exempt_accommodated.</param>
/// <param name="LevelCode">A level CODE — never a band code (PRN-03). Null unless measured.</param>
/// <param name="PositionInBand">0-100 inside the level; a position, never a percentage.</param>
/// <param name="MeasuredAt">When this number was measured — the original sitting for a carried result.</param>
/// <param name="ReusedFromPrior">True when this result was carried over from an earlier sitting.</param>
/// <param name="FailureReason">Why this skill was not measured, when that is its status.</param>
public sealed record PlacementSkillResultInfoDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Status,
    string? LevelCode,
    int? PositionInBand,
    DateTimeOffset? MeasuredAt,
    bool ReusedFromPrior,
    string? FailureReason);
