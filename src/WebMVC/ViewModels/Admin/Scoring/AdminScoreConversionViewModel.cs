using System.Collections.Generic;
using Contracts;

namespace WebMVC.Models;

public sealed record AdminScoreConversionViewModel(
    AdminScoreConversionDto Conversion,
    string? ErrorMessage = null,
    string? SuccessMessage = null)
{
    public IReadOnlyDictionary<int, int> PreviewMaximums { get; } = Conversion.SkillScales.ToDictionary(s => s.SkillId, s => s.MaxQuestions > 0 ? s.MaxQuestions : 100);
    public IReadOnlyDictionary<int, int> PreviewSamples { get; } = Conversion.SkillScales.ToDictionary(s => s.SkillId, s => (s.MaxQuestions > 0 ? s.MaxQuestions : 100) * 3 / 4);
}
