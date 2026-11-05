
namespace Contracts;

/// <summary>#192: one band of a criterion's seven-band rubric, as the wizard carries it.</summary>
/// <param name="Band">The CEFR band itself: 0 = Pre-A1 ... 6 = C2.</param>
/// <param name="Descriptor">What the band looks like, in observable terms - not adjectives.</param>
/// <param name="BinaryGates">What must be true before the band is reached at all.</param>
public sealed record SaveGradingCriterionBandDto(byte Band, string Descriptor, string BinaryGates);

/// <summary>#193: one calibrated anchor response, at band 2, 3 or 4.</summary>
public sealed record SaveGradingCriterionAnchorDto(byte Band, string ResponseText);

/// <summary>E1.2 (#147): one grading criterion authored in the certificate wizard, identified by
/// the skill's code because the skill may not have an id yet on first save.</summary>
/// <param name="Bands">
/// #192: the seven-band grid. Null or empty means "not authored here", which keeps every existing
/// caller compiling and keeps the prose path working; a partial grid is refused by the validator,
/// because the bands it omits would have to be invented by the grader.
/// </param>
/// <param name="Anchors">
/// #193: the calibrated benchmark responses at bands 2, 3 and 4. Null or empty means "not authored
/// here", which leaves any anchors already stored alone - the same rule as <paramref name="Bands"/>,
/// and for the same reason. A partial set is refused by the validator.
/// </param>
public sealed record SaveGradingCriterionWizardDto(
    int? CriterionId,
    string SkillCode,
    string Code,
    string Name,
    decimal MaxScore,
    decimal Weight,
    int DisplayOrder,
    string? RubricDescription,
    IReadOnlyList<SaveGradingCriterionBandDto>? Bands = null,
    IReadOnlyList<SaveGradingCriterionAnchorDto>? Anchors = null);
