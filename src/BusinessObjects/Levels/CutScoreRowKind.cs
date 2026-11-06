namespace BusinessObjects;

/// <summary>
/// #210 / D6: what one row of the per-skill cut-score table records. §4.6's table has three kinds,
/// and a single <c>(MinScaled, MaxScaled)</c> pair cannot carry all three, so the kind is a column and
/// the range's presence is derived from it.
/// <para>
/// The kind exists because "a level" and "the highest number this scale can print" are different
/// claims. Collapsing them is how TOEIC came to display "C2", which LVL-14 refuses.
/// </para>
/// </summary>
public enum CutScoreRowKind
{
    /// <summary>The range this level occupies on the certificate's scale. The only kind a level is read from.</summary>
    Cut,

    /// <summary>A range the certificate scores but does not certify (§4.6's IELTS/VSTEP Pre-A1 rows):
    /// the value is real, the level is never awarded.</summary>
    Uncertified,

    /// <summary>No range at all. <c>MaxScaled</c> is the highest value the scale can display when the
    /// learner's coordinate sits above the highest level the certificate expresses. Never a level.</summary>
    DisplayCeiling,
}
