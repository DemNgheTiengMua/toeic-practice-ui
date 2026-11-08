namespace Contracts;

/// <summary>
/// #238 / SCR-PRAC-01: one <c>(topic, level)</c> cell of the objective practice matrix.
/// <para>
/// <b>Mastery</b> is the four-state estimate the plan also sizes its steps from, so the matrix and the
/// path cannot disagree about a cell. <b>IsPrioritised</b> is the plan's own top-three, passed in by the
/// service rather than recomputed here: a view that ranked cells itself would be the second opinion
/// PLC-61 forbids.
/// </para>
/// <para>
/// <b>ReopensAt</b> is a UTC instant, or null when the cell is open. It is converted to the learner's
/// own zone at the MVC edge, never here — a stored offset cannot know the learner's DST state later.
/// </para>
/// </summary>
public sealed record PracticeCellDto(
    int TopicId,
    string TopicCode,
    string TopicName,
    ModalityDto Modality,
    string LevelCode,
    PracticeCellMasteryDto Mastery,
    bool IsPrioritised,
    DateTimeOffset? ReopensAt,
    /// <summary>
    /// Whether the cell rests, decided by the server's clock — the same clock the selector excludes
    /// cooling items with. A contract that re-derived this from <c>UtcNow</c> would let a test's fixed
    /// instant and the machine's disagree about the same cell.
    /// </summary>
    bool IsResting = false)
{
    /// <summary>True when the cell has a return time and is set aside now (U19).</summary>
    public bool IsRestingNow => IsResting && ReopensAt is not null;
}

/// <summary>
/// #238: the four states a cell's mastery can be in, mirroring <c>BusinessObjects.MasteryState</c>.
/// A bool cannot carry four states, so the wire carries the state and the view renders it verbatim.
/// </summary>
public enum PracticeCellMasteryDto
{
    Untested,
    Learning,
    Reviewing,
    Mastered,
}
