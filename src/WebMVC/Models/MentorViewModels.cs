using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed record MentorListViewModel(
    IReadOnlyList<MentorSummaryDto> Mentors,
    string? ActiveSkill,
    string ActiveCertCode,
    string? ErrorMessage = null);

public sealed record MentorDetailViewModel(
    MentorDetailDto Detail,
    string? ErrorMessage = null);

public sealed class MentorFormViewModel
{
    public int? MentorId { get; set; }

    [Required]
    public string Type { get; set; } = "human";

    public int? UserId { get; set; }

    [Required(ErrorMessage = "Display name is required.")]
    [StringLength(120, ErrorMessage = "Display name cannot exceed 120 characters.")]
    public string DisplayName { get; set; } = string.Empty;

    public string? Bio { get; set; }

    [Required]
    public int CertId { get; set; }

    public int? SkillId { get; set; }

    [Range(0, 100000000, ErrorMessage = "Price per slot cannot be negative.")]
    public decimal PricePerSlot { get; set; } = 200000;

    [Range(1, 1000, ErrorMessage = "Price in credits must be at least 1.")]
    public int PriceCredits { get; set; } = 1;

    [StringLength(200)]
    public string? Headline { get; set; }

    [StringLength(400)]
    public string? Expertise { get; set; }

    [Range(15, 300, ErrorMessage = "Session duration must be between 15 and 300 minutes.")]
    public int SessionMinutes { get; set; } = 60;

    public bool IsActive { get; set; } = true;

    public IReadOnlyList<MentorCertificateOptionDto> Certificates { get; set; } = [];

    public IReadOnlyList<AdminUserDto> Users { get; set; } = [];
}

public sealed class MentorSlotsViewModel
{
    public MentorSummaryDto Mentor { get; set; } = null!;

    public IReadOnlyList<AdminMentorSlotDto> Slots { get; set; } = [];

    public CreateSlotViewModel NewSlot { get; set; } = new();
}

public sealed class CreateSlotViewModel
{
    [Required(ErrorMessage = "Start date and time is required.")]
    public DateTime LocalStartAt { get; set; } = DateTime.Today.AddDays(1).AddHours(9);

    public DateTime? LocalEndAt { get; set; }
}
