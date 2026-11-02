using Contracts;

namespace WebMVC.Models;

/// <summary>#159: the Writing or Speaking door. One page, one skill, and a real link into each
/// published exam's instructions - the same route the flow already starts from.</summary>
public sealed record SkillPracticeIndexViewModel(
    string SkillLabel,
    string SkillPath,
    SkillPracticeExamListDto List,
    string? ErrorMessage = null)
{
    public bool HasCredits => List.CreditBalance > 0;
    public IReadOnlyList<SkillPracticeExamDto> Exams => List.Exams;
    public bool HasExams => List.Exams.Count > 0;
}
