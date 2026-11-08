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
    public IReadOnlyDictionary<int, string> GroupSliceReadouts { get; }
    public IReadOnlyList<StudioAuditIssueViewModel> AuditIssues { get; }

    /// <summary>
    /// #226 / ADM-12: the six publish invariants as six rows. The rows are never collapsed into one -
    /// ADM-13 wants the unmet invariant named, and a rail showing a single "cannot publish" sends the
    /// author hunting.
    /// </summary>
    public IReadOnlyList<ExamInvariantDto> Invariants => Detail.Audit.Invariants ?? [];
    public string AuditStatusText => Detail.Audit.CanPublish ? "PASSED" : $"{Detail.Audit.BlockingErrorCount} BLOCKS";
    public string AuditBadgeStyle => Detail.Audit.CanPublish ? "background: #e6f1ea; color: #0a6b3c;" : "background: #fbe9e9; color: #b31217;";

    public ExamStudioViewModel(ExamStudioDetailDto detail)
    {
        Detail = detail;
        FirstSection = detail.Sections.FirstOrDefault();
        Skills = detail.Sections.GroupBy(s => s.SkillId > 0 ? s.SkillId.ToString() : s.Modality.ToString())
            .Select(g => new StudioSkillViewModel(g.Key, g.First().SkillName ?? g.First().Modality.ToString(),
                g.ToArray(), g.Sum(s => s.CurrentQuestionCount), g.Sum(s => s.ExpectedQuestionCount))).ToArray();
        FirstSkillKey = Skills.FirstOrDefault()?.Key ?? "";
        RequiredQuestions = detail.QuestionCount ?? detail.Sections.Sum(s => s.ExpectedQuestionCount);
        SectionStates = detail.Sections.ToDictionary(s => s.SectionId, s => new StudioSectionViewModel(s));
        Questions = detail.Sections.SelectMany(s => s.Groups.SelectMany(g => g.Questions.Select(q =>
            new StudioQuestionViewModel(q, s, detail.AvailableCategories, detail.AvailableCriteria))))
            .ToDictionary(q => q.Question.QuestionId);
        var groups = detail.Sections.SelectMany(s => s.Groups).ToArray();
        GroupImageUrls = groups.ToDictionary(g => g.GroupId, g => MediaUrlHelper.NormalizeMediaUrl(g.ImagePath));
        GroupSliceReadouts = groups.ToDictionary(g => g.GroupId, g => SliceReadout(g.AudioStartMs, g.AudioEndMs));
        AuditIssues = detail.Audit.Issues.Select(i => new StudioAuditIssueViewModel(i)).ToArray();
        ConfigurationJson = JsonSerializer.Serialize(new {
            detail.ExamId, CurrentSectionId = FirstSection?.SectionId ?? 0,
            Sections = detail.Sections.Select(s => new { s.SectionId, s.Name, Modality = s.Modality.ToString(), s.ClusterSize }),
            IngestSections = detail.Sections.Select(s => new { s.SectionId, s.Code, s.Name, s.OptionCount, s.ClusterSize,
                s.RequirePassage, s.RequireAudio, s.RequireImage, s.RequiredKeywordCount, s.AllowedComponentTypesJson,
                s.ExpectedQuestionCount, s.CurrentQuestionCount, s.HideOptionText })
        });
    }

    private static string SliceReadout(int? startMs, int? endMs) =>
        startMs is >= 0 && endMs > startMs
            ? $"{TimeSpan.FromMilliseconds(startMs.Value):mm\\:ss\\.fff} → {TimeSpan.FromMilliseconds(endMs!.Value):mm\\:ss\\.fff}"
            : "No slice — the whole track plays";
}
