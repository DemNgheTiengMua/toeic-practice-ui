using System.Collections.Generic;
using Contracts;

namespace WebMVC.Models;

public sealed record AdminScoreConversionViewModel(
    AdminScoreConversionDto Conversion,
    string? ErrorMessage = null,
    string? SuccessMessage = null);

public sealed class ScoreScaleFormEntry
{
    public int SkillId { get; set; }
    public int RawScore { get; set; }
    public int ScaledScore { get; set; }
}

public sealed class ScoreBandFormEntry
{
    public int? BandId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MinTotal { get; set; }
    public int MaxTotal { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class ScoreConversionFormModel
{
    public int? ExamId { get; set; }
    public List<ScoreScaleFormEntry> Scales { get; set; } = [];
    public List<ScoreBandFormEntry> Bands { get; set; } = [];
}
