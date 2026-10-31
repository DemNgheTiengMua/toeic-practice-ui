namespace BusinessObjects;

/// <summary>CK_TargetBands_Feasibility: how a goal's required hours compare with the hours the learner
/// has before the deadline. <see cref="Unknown"/> is not stored — it is what a goal with no deadline,
/// or a certificate whose bands carry no hours, answers instead of inventing a verdict.</summary>
public enum FeasibilityStatus
{
    Unknown,
    Feasible,
    Stretch,
    Unrealistic,
}
