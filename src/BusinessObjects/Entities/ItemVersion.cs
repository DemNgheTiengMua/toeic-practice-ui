using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #184: everything that can be scored - stem, four options, key, explanation, evidence span and the
/// reference a spoken answer is judged against. Copy-on-write (CNT-43): a scored change inserts a new
/// row, so an attempt that pinned version 3 renders version 3 forever even after the item is corrected
/// or retired.
/// <para>
/// Only <see cref="Explanation"/> may be edited in place, and only for a typo. Every other column is
/// immovable after insert - enforced by <c>trg_ItemVersions_ScoredFieldsImmutable</c>, because a
/// convention in a service does not survive its second caller. That trigger is declared in
/// <c>CompanionDbContext.Authoring.cs</c> via <c>HasTrigger</c>; a table with a trigger and no
/// declaration fails every save on the OUTPUT clause.
/// </para>
/// </summary>
public partial class ItemVersion
{
    [Key]
    public int ItemVersionId { get; set; }

    public int ItemId { get; set; }

    public int VersionNumber { get; set; }

    public string Stem { get; set; } = null!;

    public string? OptionA { get; set; }

    public string? OptionB { get; set; }

    public string? OptionC { get; set; }

    public string? OptionD { get; set; }

    /// <summary>A-D, or NULL for a productive task that has no options at all.</summary>
    [StringLength(1)]
    [Unicode(false)]
    public string? KeyLabel { get; set; }

    /// <summary>The one field a typo fix may change in place.</summary>
    public string? Explanation { get; set; }

    /// <summary>
    /// #194: why each option is what it is - the copy gate one requires before an item may be
    /// validated. Beside the option rather than in a separate table because CNT-06 fixes the count at
    /// four: a child table would buy nothing a fifth column does not, and would make "four options,
    /// four rationales" a join instead of a CHECK.
    /// <para>
    /// Scored, like the option text: the review screen shows a learner the rationale for the option
    /// they chose, so rewriting one rewrites what a past attempt renders.
    /// <c>trg_ItemVersions_ScoredFieldsImmutable</c> therefore compares them.
    /// </para>
    /// </summary>
    public string? RationaleA { get; set; }

    public string? RationaleB { get; set; }

    public string? RationaleC { get; set; }

    public string? RationaleD { get; set; }

    public int? EvidenceStart { get; set; }

    public int? EvidenceEnd { get; set; }

    /// <summary><c>char</c> for text, <c>sec</c> for audio.</summary>
    [StringLength(8)]
    [Unicode(false)]
    public string? EvidenceUnit { get; set; }

    /// <summary>The verbatim words that answer the item, so review can jump to them.</summary>
    public string? EvidenceQuote { get; set; }

    [StringLength(1000)]
    public string? ReferenceText { get; set; }

    /// <summary><c>script</c> (read aloud) or <c>image_desc</c> (describes the picture).</summary>
    [StringLength(12)]
    [Unicode(false)]
    public string? ReferenceKind { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// #201 / ADM-02: who wrote this version - the author on INSERT, the editor on the one in-place
    /// edit (a cosmetic <see cref="Explanation"/> fix, CNT-43). NULL means "written by a seed, a
    /// fixture or a migration", which names no account and is not a claim.
    /// <para>
    /// It is also the triage guard's declaration (ADM-03) and the "or last modified" half of the
    /// no-self-approval rule: the reviewer is barred from the newest version's value.
    /// </para>
    /// </summary>
    public int? LastWrittenBy { get; set; }

    [ForeignKey("ItemId")]
    [InverseProperty("ItemVersions")]
    public virtual Item Item { get; set; } = null!;

    /// <summary>#185: the papers that pin this exact version.</summary>
    [InverseProperty("ItemVersion")]
    public virtual ICollection<ExamItem> ExamItems { get; set; } = new List<ExamItem>();

    /// <summary>#186: the answers that pinned this exact version.</summary>
    [InverseProperty("ItemVersion")]
    public virtual ICollection<AttemptAnswer> AttemptAnswers { get; set; } = new List<AttemptAnswer>();

    /// <summary>#187: the practice answers that pinned this exact version.</summary>
    [InverseProperty("ItemVersion")]
    public virtual ICollection<PracticeAnswer> PracticeAnswers { get; set; } = new List<PracticeAnswer>();

    /// <summary>#224: the saved links that pinned this exact version.</summary>
    [InverseProperty("ItemVersion")]
    public virtual ICollection<SavedQuestion> SavedQuestions { get; set; } = new List<SavedQuestion>();
}
