using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

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
