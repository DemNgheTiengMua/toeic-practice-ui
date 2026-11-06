using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #210 / D6 / LVL-13: one row of the per-skill CEFR cut-score table. Read-only at the presentation
/// layer: projection is a lookup, and the table is versioned so a recorded projection can be
/// reproduced.
/// <para>
/// <b>The range belongs to the kind.</b> <see cref="MinScaled"/> is null exactly on a
/// <c>display_ceiling</c> row, which records only the highest value the scale can display and is never
/// emitted as a level. CK_CefrCutScores_KindsHoldTheirShape refuses the other combinations, so a
/// ceiling row carrying a range is unstorable rather than a branch a caller has to remember.
/// </para>
/// </summary>
[Index("CertId", "TableVersion", "SkillId", "CefrLevel", Name = "UQ_CefrCutScores_Version_Skill_Level", IsUnique = true)]
public partial class CefrCutScore
{
    [Key]
    public int CutScoreId { get; set; }

    public int CertId { get; set; }

    public int TableVersion { get; set; }

    public int SkillId { get; set; }

    /// <summary>Reportable levels only: the Pre-A1 floor is a result, and §4.6 has a row for it.</summary>
    [StringLength(10)]
    [Unicode(false)]
    public string CefrLevel { get; set; } = null!;

    /// <summary><c>cut</c>, <c>uncertified</c> or <c>display_ceiling</c>.</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string RowKind { get; set; } = null!;

    /// <summary>Null exactly on a display-ceiling row: a ceiling has no range, only a top.</summary>
    public int? MinScaled { get; set; }

    /// <summary>The top of the range for a cut row, or the scale's displayable top for a ceiling.</summary>
    public int MaxScaled { get; set; }

    [ForeignKey("CertId, TableVersion")]
    [InverseProperty("CutScores")]
    public virtual CefrCutScoreVersion Version { get; set; } = null!;

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("CutScores")]
    public virtual Skill SkillNavigation { get; set; } = null!;
}

/// <summary>
/// #210: the header a cut-score table version hangs from. It carries the provenance (LVL-13's
/// "taken from published standard-setting studies") and the one reporting property that belongs to
/// the version rather than to a row: whether the certificate reports its skills separately with no
/// combined total, which is TOEIC S&amp;W's rule and is not one of the three attempt aggregation
/// rules.
/// </summary>
public partial class CefrCutScoreVersion
{
    [Key]
    public int CertId { get; set; }

    [Key]
    public int TableVersion { get; set; }

    public bool ReportsSkillsSeparately { get; set; }

    [StringLength(200)]
    public string Source { get; set; } = null!;

    public DateOnly PublishedAt { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("CutScoreVersions")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("Version")]
    public virtual ICollection<CefrCutScore> CutScores { get; set; } = new List<CefrCutScore>();
}
