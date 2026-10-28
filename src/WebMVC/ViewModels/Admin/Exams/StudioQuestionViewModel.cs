using System.Text.Json;
using Contracts;
namespace WebMVC.Models;

public sealed class StudioQuestionViewModel
{
    public ExamStudioQuestionDto Question { get; }
    public ExamStudioSectionDto Section { get; }
    public IReadOnlyList<AdminQuestionCategoryOptionDto> AvailableCategories { get; }
    public HashSet<string> AllowedTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string TypeCode { get; }
    public bool IsRadioType { get; }
    public IReadOnlyList<StudioOptionViewModel> RadioOptions { get; }
    public IReadOnlyList<StudioOptionViewModel> Options { get; }
    public IReadOnlyList<StudioCriterionViewModel> Criteria { get; }

    public StudioQuestionViewModel(ExamStudioQuestionDto question, ExamStudioSectionDto section,
        IReadOnlyList<AdminQuestionCategoryOptionDto> categories, IReadOnlyList<ExamStudioCriterionDto> criteria)
    {
        Question = question;
        Section = section;
        AvailableCategories = categories;
        if (!string.IsNullOrWhiteSpace(section.AllowedComponentTypesJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(section.AllowedComponentTypesJson);
                if (parsed is not null) foreach (var value in parsed) AllowedTypes.Add(value.Trim());
            }
            catch { }
        }
        if (AllowedTypes.Count == 0) { AllowedTypes.Add("mcq"); AllowedTypes.Add("free_response"); }
        TypeCode = question.QuestionType switch {
            QuestionTypeDto.Mcq_3 => "mcq_3", QuestionTypeDto.MultiSelect => "multi_select",
            QuestionTypeDto.InlineCompletion => "inline_completion", QuestionTypeDto.Dropdown => "dropdown",
            QuestionTypeDto.DragDrop => "drag_drop", QuestionTypeDto.FreeResponse => "free_response", _ => "mcq"
        };
        AllowedTypes.Add(TypeCode);
        IsRadioType = TypeCode is "mcq" or "mcq_3" or "dropdown";
        Options = BuildOptions(section.OptionCount);
        RadioOptions = TypeCode == "mcq_3" ? BuildOptions(3) : Options;
        Criteria = criteria.Where(c => c.SkillId == question.SkillId || c.SkillId == section.SkillId)
            .Select(c => new StudioCriterionViewModel(c, question.Criteria.Any(a => a.CriterionId == c.CriterionId))).ToArray();
    }

    private StudioOptionViewModel[] BuildOptions(int count) => Enumerable.Range(0, Math.Max(0, count)).Select(index => {
        var label = ((char)('A' + index)).ToString();
        var option = Question.Options.FirstOrDefault(o => o.Label == label);
        return new StudioOptionViewModel(label, option?.Text ?? "", option?.Rationale ?? "",
            option?.IsCorrect ?? index == 0, option?.IsCorrect ?? false);
    }).ToArray();
}
