using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Http;

namespace WebMVC.Models;

/// <summary>
/// #225: one binding, as the assembly workbench posts it. The position and the unit are the paper's,
/// not the item's — a version has no opinion about where on a paper it sits — so they are on the form
/// rather than looked up.
/// <para>
/// <see cref="SectionCode"/> is carried for the confirmation sentence only: the API answers with the
/// section id, and a message saying "section 41" names nothing a person can check.
/// </para>
/// </summary>
public sealed class BindItemFormModel
{
    public int SectionId { get; set; }

    public int ItemVersionId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "A position is 1-based.")]
    public int DisplayOrder { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "A unit is 1-based.")]
    public int UnitNumber { get; set; } = 1;

    public string? SectionCode { get; set; }
}
