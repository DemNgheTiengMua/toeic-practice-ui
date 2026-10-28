using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Http;

namespace WebMVC.Models;

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
