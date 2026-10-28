using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Http;

namespace WebMVC.Models;

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
