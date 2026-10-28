using Contracts;
namespace WebMVC.Models;

public sealed record StudioSkillViewModel(string Key, string Name, IReadOnlyList<ExamStudioSectionDto> Sections,
    int TotalQuestions, int ExpectedQuestions);
