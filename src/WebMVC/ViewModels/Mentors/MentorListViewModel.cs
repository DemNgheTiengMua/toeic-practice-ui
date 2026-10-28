using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed record MentorListViewModel(
    IReadOnlyList<MentorSummaryDto> Mentors,
    string? ActiveSkill,
    string ActiveCertCode,
    string? ErrorMessage = null);
