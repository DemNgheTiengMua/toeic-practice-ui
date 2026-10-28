using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Http;

namespace WebMVC.Models;

public sealed class QuestionSaveModel
{
    public int? QuestionId { get; set; }

    [Required]
    public int GroupId { get; set; }

    [Required]
    public int SectionId { get; set; }

    [Required(ErrorMessage = "Question stem is required.")]
    public string Stem { get; set; } = string.Empty;

    [Range(1, 5, ErrorMessage = "Difficulty level must be between 1 and 5.")]
    public int DifficultyLevel { get; set; } = 3;

    public QuestionTypeDto QuestionType { get; set; } = QuestionTypeDto.Mcq;

    public string? Explanation { get; set; }

    public int? CategoryId { get; set; }

    public int? PrepSeconds { get; set; }

    public int? ResponseSeconds { get; set; }

    public int? MinWords { get; set; }

    public int? MaxWords { get; set; }

    [StringLength(1000, ErrorMessage = "Hint cannot exceed 1000 characters.")]
    public string? Hint { get; set; }

    public int DisplayOrder { get; set; }

    public int? MaxSelections { get; set; }

    public bool AllowNumber { get; set; } = true;

    public string? AcceptedAnswersJson { get; set; }

    public string? RequiredKeywords { get; set; }

    // MCQ Options
    public List<QuestionOptionInputModel> Options { get; set; } = [];

    public string CorrectOptionLabel { get; set; } = "A";
}
