using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed class CreateSlotViewModel
{
    [Required(ErrorMessage = "Start date and time is required.")]
    public DateTime LocalStartAt { get; set; } = DateTime.Today.AddDays(1).AddHours(9);

    public DateTime? LocalEndAt { get; set; }
}
