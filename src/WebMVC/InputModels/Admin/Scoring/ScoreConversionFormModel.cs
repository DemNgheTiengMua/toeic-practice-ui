using System.Collections.Generic;
using Contracts;

namespace WebMVC.Models;

public sealed class ScoreConversionFormModel
{
    public string? CertCode { get; set; }
    public int? ExamId { get; set; }
    public List<ScoreScaleFormEntry> Scales { get; set; } = [];
    public List<ScoreBandFormEntry> Bands { get; set; } = [];
}
