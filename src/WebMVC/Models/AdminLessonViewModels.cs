using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed record AdminLessonListViewModel(
    IReadOnlyList<AdminLessonSummaryDto> Lessons,
    IReadOnlyList<AdminCertificateOptionDto> Certificates,
    int? SelectedCertId,
    int? SelectedSkillId,
    string? ErrorMessage = null,
    string? SuccessMessage = null);

public sealed class AdminLessonEditViewModel
{
    public int LessonId { get; set; }

    [Required(ErrorMessage = "Certificate is required.")]
    public int CertId { get; set; }

    public string? CertCode { get; set; }

    [Required(ErrorMessage = "Skill is required.")]
    public int SkillId { get; set; }

    public string? SkillCode { get; set; }

    public int? CategoryId { get; set; }

    public string? CategoryCode { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(160, ErrorMessage = "Title cannot exceed 160 characters.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Summary cannot exceed 500 characters.")]
    public string? Summary { get; set; }

    [Range(1, 180, ErrorMessage = "Estimated minutes must be between 1 and 180.")]
    public int EstimatedMinutes { get; set; } = 5;

    public int DisplayOrder { get; set; } = 0;

    public List<AdminLessonCardEditViewModel> Cards { get; set; } = new();

    public IReadOnlyList<AdminCertificateOptionDto> Certificates { get; set; } = Array.Empty<AdminCertificateOptionDto>();

    public string? ErrorMessage { get; set; }
}

public sealed class AdminLessonCardEditViewModel
{
    public int? CardId { get; set; }

    [Required(ErrorMessage = "Card type is required.")]
    public string CardType { get; set; } = "rule"; // rule, pitfall, example, micro_check

    [Required(ErrorMessage = "Card title is required.")]
    [StringLength(160, ErrorMessage = "Card title cannot exceed 160 characters.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Content in markdown is required.")]
    public string ContentMarkdown { get; set; } = string.Empty;

    public string? QuestionPrompt { get; set; }

    [StringLength(250, ErrorMessage = "Option A cannot exceed 250 characters.")]
    public string? OptionA { get; set; }

    [StringLength(250, ErrorMessage = "Option B cannot exceed 250 characters.")]
    public string? OptionB { get; set; }

    [StringLength(250, ErrorMessage = "Option C cannot exceed 250 characters.")]
    public string? OptionC { get; set; }

    [StringLength(250, ErrorMessage = "Option D cannot exceed 250 characters.")]
    public string? OptionD { get; set; }

    [RegularExpression("^[ABCDabcd]$", ErrorMessage = "Correct option must be A, B, C, or D.")]
    public string? CorrectOption { get; set; }

    public string? Explanation { get; set; }

    public string? Rationale { get; set; }

    public int DisplayOrder { get; set; }
}
