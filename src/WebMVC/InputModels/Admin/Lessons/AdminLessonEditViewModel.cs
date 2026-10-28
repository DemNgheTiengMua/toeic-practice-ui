using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

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

    public IReadOnlyList<AdminLessonSkillOptionDto> Skills { get; set; } = Array.Empty<AdminLessonSkillOptionDto>();

    public IReadOnlyList<AdminLessonCategoryOptionDto> Categories { get; set; } = Array.Empty<AdminLessonCategoryOptionDto>();

    public string? ErrorMessage { get; set; }
}
