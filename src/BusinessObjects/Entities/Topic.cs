using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #183: the topic axis of the warehouse. A topic is a communicative micro-skill within a skill and
/// belongs to the language, not to a certificate - so there is no CertId, and one topic serves every
/// certificate that examines its modality.
///
/// The vocabulary is a closed set of twenty. It replaces <see cref="QuestionCategory"/>, which was
/// per-certificate and unlevelled, which is why twelve of its fifteen buckets held no items.
/// </summary>
[Index("Modality", "Code", Name = "UQ_Topics_Modality_Code", IsUnique = true)]
[Index("TopicId", "Modality", Name = "UQ_Topics_Id_Modality", IsUnique = true)]
public partial class Topic
{
    [Key]
    public int TopicId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [StringLength(160)]
    public string Name { get; set; } = null!;

    /// <summary>#184: the items authored against this topic. A topic holds many cells, one per level.</summary>
    [InverseProperty("Topic")]
    public virtual ICollection<Item> Items { get; set; } = new List<Item>();

    /// <summary>#187: the practice sessions drawn from this topic's cells.</summary>
    [InverseProperty("Topic")]
    public virtual ICollection<PracticeSession> PracticeSessions { get; set; } = new List<PracticeSession>();

    /// <summary>#232: the path steps drilled at this topic.</summary>
    [InverseProperty("Topic")]
    public virtual ICollection<LearnerPathStep> LearnerPathSteps { get; set; } = new List<LearnerPathStep>();
}

