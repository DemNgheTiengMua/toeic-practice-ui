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

    /// <summary>#240: the skill's modality, carried with the cell so the topic list can be narrowed to
    /// the topics a lesson of this skill could sit on.</summary>
    public string Modality { get; set; } = "reading";

    [Range(1, int.MaxValue, ErrorMessage = "Topic is required.")]
    public int TopicId { get; set; }

    /// <summary>#240: the lesson's own level. A certificate band is not a level.</summary>
    [Required(ErrorMessage = "Level is required.")]
    [RegularExpression("^(A1|A2|B1|B2|C1|C2)$", ErrorMessage = "Level must be one of A1, A2, B1, B2, C1 or C2.")]
    public string CefrLevel { get; set; } = "B1";

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

    /// <summary>#240: the canonical twenty, filtered by modality in the editor.</summary>
    public IReadOnlyList<AdminLessonTopicOptionDto> Topics { get; set; } = Array.Empty<AdminLessonTopicOptionDto>();

    public string? ErrorMessage { get; set; }
}
