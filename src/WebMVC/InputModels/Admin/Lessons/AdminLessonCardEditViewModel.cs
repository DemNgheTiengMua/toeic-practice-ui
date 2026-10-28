using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed class AdminLessonCardEditViewModel
{
    public bool IsMicroCheck => string.Equals(CardType, "micro_check", StringComparison.OrdinalIgnoreCase);
    public string TypeLabel => CardType.ToUpper();

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
