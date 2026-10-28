using System.Collections.Generic;
using Contracts;

namespace WebMVC.Models;

public sealed class ScoreBandFormEntry
{
    public int? BandId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MinTotal { get; set; }
    public int MaxTotal { get; set; }
    public int DisplayOrder { get; set; }
}
