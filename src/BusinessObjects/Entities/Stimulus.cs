using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #184: a shared passage or audio track, pool-owned. It carries its own level (L_s), its domain
/// context and its passage, transcript or image - and no ExamId, no SectionId, no CertId. That is the
/// point of the table: PRN-05 says content is certificate-agnostic, and a foreign key is what makes
/// that true rather than aspirational.
/// <para>
/// The level is a <see cref="BusinessObjects.CefrLevel"/>, not an integer and not a certificate band.
/// It is authored, so <c>pre_a1</c> is refused: a stimulus cannot be written at the reportable floor.
/// </para>
/// </summary>
public partial class Stimulus
{
    [Key]
    public int StimulusId { get; set; }

    public CefrLevel CefrLevel { get; set; }

    /// <summary>A reading stimulus.</summary>
    public string? Passage { get; set; }

    /// <summary>A listening stimulus.</summary>
    [StringLength(400)]
    public string? AudioPath { get; set; }

    /// <summary>
    /// #250: the group's slice of the section's master track, in milliseconds. Both NULL means "the
    /// whole track". Migrated from <c>QuestionGroups.AudioStartMs/AudioEndMs</c>.
    /// </summary>
    public int? AudioStartMs { get; set; }

    public int? AudioEndMs { get; set; }

    /// <summary>The audio script, shown after the sitting.</summary>
    public string? Transcript { get; set; }

    /// <summary>Workplace, academic or general. Register only - it never changes what an item measures.</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string DomainContext { get; set; } = "general";

    /// <summary>A productive task's instruction copy.</summary>
    public string? PromptCopy { get; set; }

    [StringLength(400)]
    public string? ImagePath { get; set; }

    /// <summary>Required wherever an image is; an undescribed image is inaccessible.</summary>
    [StringLength(400)]
    public string? ImageAlt { get; set; }

    public int? PrepSeconds { get; set; }

    public int? ResponseSeconds { get; set; }

    public int DisplayOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [InverseProperty("Stimulus")]
    public virtual ICollection<Item> Items { get; set; } = new List<Item>();
}
