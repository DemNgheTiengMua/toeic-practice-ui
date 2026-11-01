namespace BusinessObjects;
/// <summary>CK_TargetBands_Feasibility: how a goal's required hours compare with the hours the learner
/// has before the deadline.
/// <para>
/// There is deliberately no <c>Unknown</c> member. An undecidable goal — no deadline, or a certificate
/// whose bands carry no hours — has no verdict to store, so
/// <see cref="GoalFeasibilityCalculator.Evaluate"/> returns null and the column stays NULL. A member
/// here would be a fourth code that the CHECK constraint does not accept, which is how a "zero" value
/// turns into a constraint violation at save time (ADR-0001).
/// </para></summary>
public enum FeasibilityStatus
{
    Feasible,
    Stretch,
    Unrealistic,
}
