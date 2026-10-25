using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Http;

namespace WebMVC.Models;

public sealed record AdminExamListViewModel(
    AdminExamListSummaryDto Summary,
    string? Query,
    string? Status,
    int? CertId,
    int Page,
    string? Purpose = null,
    string? ErrorMessage = null,
    string? SuccessMessage = null);

public sealed class ExamFormViewModel
{
    public int? ExamId { get; set; }

    [Required(ErrorMessage = "Please select a certificate.")]
    public int CertId { get; set; }

    [Required(ErrorMessage = "Exam name is required.")]
    [StringLength(160, ErrorMessage = "Exam name cannot exceed 160 characters.")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 1440, ErrorMessage = "Duration must be at least 1 minute.")]
    public int DurationMinutes { get; set; } = 120;

    public ExamPurposeDto Purpose { get; set; } = ExamPurposeDto.Mock;

    public ExamStatusDto Status { get; set; } = ExamStatusDto.Draft;

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    [StringLength(120, ErrorMessage = "Source name cannot exceed 120 characters.")]
    public string? SourceName { get; set; }

    [StringLength(1000, ErrorMessage = "Internal note cannot exceed 1000 characters.")]
    public string? InternalNote { get; set; }

    [Range(1, 1000, ErrorMessage = "Question count must be at least 1.")]
    public int? QuestionCount { get; set; }

    public int Step { get; set; } = 1;

    public AdminExamDetailDto? Detail { get; set; }

    public IReadOnlyList<AdminCertificateOptionDto> Certificates { get; set; } = [];

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}

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

public sealed class QuestionOptionInputModel
{
    public int? OptionId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public string? Rationale { get; set; }
}

public sealed class QuestionGroupSaveModel
{
    public int? GroupId { get; set; }
    public int SectionId { get; set; }
    public string? Passage { get; set; }
    public string? AudioPath { get; set; }
    public int? AudioStartMs { get; set; }
    public int? AudioEndMs { get; set; }
    public string? ImagePath { get; set; }
    public string? ImageAlt { get; set; }
    public string? Transcript { get; set; }
    public int? PrepSeconds { get; set; }
    public int? ResponseSeconds { get; set; }
    public int DisplayOrder { get; set; }
    public int UnitNumber { get; set; } = 1;
}

public sealed class UploadAudioModel
{
    [Required]
    public int SectionId { get; set; }

    public IFormFile? AudioFile { get; set; }

    public string? AudioPath { get; set; }

    [Range(1, 86400, ErrorMessage = "Duration must be greater than 0.")]
    public int DurationSec { get; set; } = 60;

    [Range(1, 62914560, ErrorMessage = "File size must be between 1 and 60 MB.")]
    public long SizeBytes { get; set; } = 1048576;
}

public sealed record QuestionEditorViewModel(
    AdminExamQuestionsDto QuestionsData,
    int? SelectedSectionId = null,
    int? SelectedQuestionId = null,
    string? ErrorMessage = null,
    string? SuccessMessage = null);
