using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Views;

/// <summary>
/// #243 / D3: one learner's accuracy on one <c>(topic, level)</c> cell, from answers already stored.
/// <b>The view computes the accuracy; the service ranks it.</b> A service that divided
/// <see cref="Correct"/> by <see cref="Answered"/> would be the second definition D3 forbids, exactly
/// as the mastery table would be for <c>p_m</c>.
/// <para>
/// A cell appears here only when it holds at least one graded answer, so a topic with no items
/// produces no row rather than an unsupported 0%. A row is not evidence, however: an answer count
/// below the observation floor is excluded by the reader, not by this view - the floor is a product
/// constant (PLC-68), and burying it in SQL would put a tunable figure in a place nobody thinks to
/// retune.
/// </para>
/// </summary>
[Keyless]
public partial class vw_UserTopicAccuracy
{
    public int UserId { get; set; }

    public int TopicId { get; set; }

    /// <summary>
    /// The item's authored level, as stored. Read as text rather than as <see cref="CefrLevel"/> so a
    /// row this build does not recognise degrades to "no cell" instead of throwing on a read path.
    /// </summary>
    public string CefrLevel { get; set; } = null!;

    /// <summary>Graded answers in this cell. An unanswered row is not an observation and is not counted.</summary>
    public int Answered { get; set; }

    public int Correct { get; set; }

    /// <summary>
    /// D3 / SRS "Accuracy is computed ... by a view, not by application code": the percentage, computed
    /// by SQL. <c>ROUND(x, 0)</c> then <c>CAST</c>, so 2 of 3 reads 67 rather than truncating to 66.
    /// </summary>
    public int AccuracyPercent { get; set; }
}
