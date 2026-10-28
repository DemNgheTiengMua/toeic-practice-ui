using System.Text.Json;
using Contracts;

namespace WebMVC.Models;

public sealed class ExamStudioViewModel
{
    public ExamStudioDetailDto Detail { get; }
    public IReadOnlyList<StudioSkillViewModel> Skills { get; }
    public IReadOnlyDictionary<int, StudioSectionViewModel> SectionStates { get; }
    public IReadOnlyDictionary<int, StudioQuestionViewModel> Questions { get; }
    public ExamStudioSectionDto? FirstSection { get; }
    public string FirstSkillKey { get; }
    public int RequiredQuestions { get; }
    public string ConfigurationJson { get; }
    public string CertificateCode => Detail.CertCode.ToUpperInvariant();
    public IReadOnlyDictionary<int, string?> GroupImageUrls { get; }
    public IReadOnlyList<StudioAuditIssueViewModel> AuditIssues { get; }
    public string AuditStatusText => Detail.Audit.CanPublish ? "PASSED" : $"{Detail.Audit.BlockingErrorCount} BLOCKS";
    public string AuditBadgeStyle => Detail.Audit.CanPublish ? "background: #e6f1ea; color: #0a6b3c;" : "background: #fbe9e9; color: #b31217;";

    public ExamStudioViewModel(ExamStudioDetailDto detail)
    {
        Detail = detail;
        FirstSection = detail.Sections.FirstOrDefault();
        Skills = detail.Sections.GroupBy(s => s.SkillId > 0 ? s.SkillId.ToString() : s.Modality.ToString())
            .Select(g => new StudioSkillViewModel(g.Key, g.First().SkillName ?? g.First().Modality.ToString(),
                g.ToArray(), g.Sum(s => s.CurrentQuestionCount), g.Sum(s => s.ExpectedQuestionCount ?? 0))).ToArray();
        FirstSkillKey = Skills.FirstOrDefault()?.Key ?? "";
        RequiredQuestions = detail.QuestionCount ?? detail.Sections.Sum(s => s.ExpectedQuestionCount ?? 0);
        SectionStates = detail.Sections.ToDictionary(s => s.SectionId, s => new StudioSectionViewModel(s));
        Questions = detail.Sections.SelectMany(s => s.Groups.SelectMany(g => g.Questions.Select(q =>
            new StudioQuestionViewModel(q, s, detail.AvailableCategories, detail.AvailableCriteria))))
            .ToDictionary(q => q.Question.QuestionId);
        GroupImageUrls = detail.Sections.SelectMany(s => s.Groups).ToDictionary(g => g.GroupId, g => MediaUrlHelper.NormalizeMediaUrl(g.ImagePath));
        AuditIssues = detail.Audit.Issues.Select(i => new StudioAuditIssueViewModel(i)).ToArray();
        ConfigurationJson = JsonSerializer.Serialize(new {
            detail.ExamId, CurrentSectionId = FirstSection?.SectionId ?? 0,
            Sections = detail.Sections.Select(s => new { s.SectionId, s.Name, Modality = s.Modality.ToString(), s.ClusterSize }),
            IngestSections = detail.Sections.Select(s => new { s.SectionId, s.Code, s.Name, s.OptionCount, s.ClusterSize,
                s.RequirePassage, s.RequireAudio, s.RequireImage, s.RequiredKeywordCount, s.AllowedComponentTypesJson,
                s.ExpectedQuestionCount, s.CurrentQuestionCount, s.HideOptionText })
        });
    }
}
