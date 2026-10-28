using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Http;

namespace WebMVC.Models;

public sealed class QuestionGroupSaveModel
{
    public int? GroupId { get; set; }
    public int SectionId { get; set; }
    public string? Passage { get; set; }
    public string? AudioPath { get; set; }
    public int? AudioStartMs { get; set; }
    public int? AudioEndMs { get; set; }
    public string? ImagePath { get; set; }
    public string? ImageAlt { get; set; }
    public string? Transcript { get; set; }
    public int? PrepSeconds { get; set; }
    public int? ResponseSeconds { get; set; }
    public int DisplayOrder { get; set; }
    public int UnitNumber { get; set; } = 1;
}
