using System.Collections.Generic;
using Contracts;

namespace WebMVC.Models;

public sealed class ScoreScaleFormEntry
{
    public int SkillId { get; set; }
    public int RawScore { get; set; }
    public int ScaledScore { get; set; }
}
