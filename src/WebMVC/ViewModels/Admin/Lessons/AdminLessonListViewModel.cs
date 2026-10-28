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
    string? SuccessMessage = null,
    IReadOnlyList<AdminLessonSkillOptionDto>? Skills = null)
{
    public IReadOnlyList<AdminLessonSkillOptionDto> FilteredSkills { get; } = (Skills ?? [])
        .Where(skill => !SelectedCertId.HasValue || skill.CertId == SelectedCertId.Value).ToArray();
}
