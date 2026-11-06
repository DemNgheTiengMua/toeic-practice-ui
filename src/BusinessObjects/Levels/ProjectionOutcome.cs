namespace BusinessObjects;

/// <summary>
/// #210: what a projection was able to say, per skill and for the certificate as a whole. A status,
/// not a boolean, because the four answers mean different things on screen: a projected skill has a
/// level, an uncertified one has a number but no level, a ceiling has the top of the scale and no
/// level, and a cannot-judge has neither.
/// </summary>
public enum ProjectionOutcome
{
    /// <summary>A cut row matched the coordinate: score and level are both real.</summary>
    Projected,

    /// <summary>An uncertified range matched: a score, deliberately no level.</summary>
    Uncertified,

    /// <summary>The coordinate is above what the certificate can express: the scale's top, no level.</summary>
    DisplayCeiling,

    /// <summary>The table does not answer this question. Never a nearest row, never a prorated number.</summary>
    CannotJudge,
}
