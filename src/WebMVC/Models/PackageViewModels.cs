using System.ComponentModel.DataAnnotations;

namespace WebMVC.Models;

public sealed class PackageFormViewModel
{
    public int? PackageId { get; set; }

    [Required(ErrorMessage = "Package name is required.")]
    [StringLength(120, ErrorMessage = "Package name cannot exceed 120 characters.")]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Credits must be at least 1.")]
    public int Credits { get; set; } = 1;

    [Range(0, double.MaxValue, ErrorMessage = "Price must be non-negative.")]
    public decimal Price { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public bool IsFeatured { get; set; } = false;

    public string? Features { get; set; }

    public int DisplayOrder { get; set; } = 0;
}
