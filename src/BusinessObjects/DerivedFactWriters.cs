using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BusinessObjects;

/// <summary>
/// #191 / D14 / TRN-06: the seven derived facts and the one service allowed to write each.
///
/// These names are what a write declares in <c>WrittenByService</c>, and a trigger compares the
/// declaration against <c>DerivedFactOwners</c>. A string that is not exactly one of these is refused
/// at write time, so a typo is a failure rather than a silently unguarded second writer.
///
/// PRN-01: the identity travels as a column on the write, never through
/// <c>sp_set_session_context</c> — session context survives commit and rollback on a pooled
/// connection and would vouch for whatever ran next.
///
/// Coverage today is four of the seven. The rest are named here so the registry has one source in
/// the application as well as in the schema, and so a later ticket has a name to declare rather than
/// inventing one.
/// </summary>
public static class DerivedFactWriters
{
    // ---- covered: the table exists and its trigger fires ----

    /// <summary>Placement snapshot: <c>PlacementTests</c>.</summary>
    public const string PlacementResult = "AttemptEngine";

    /// <summary>Attempt score and band: <c>ExamAttempts.TotalScore</c> and <c>BandCode</c>.</summary>
    public const string AttemptScoreAndBand = "AttemptEngine";

    /// <summary>Step closure: <c>LearnerPathSteps</c> moving to done.</summary>
    public const string StepClosure = "PathStepCompletion";

    /// <summary>#211: learner per-skill state, <c>LearnerSkillStates</c>.</summary>
    public const string LearnerSkillState = "SkillStateWriter";

    // ---- awaiting a table ----

    /// <summary>Topic capability: awaiting the per-cell mastery table (#232).</summary>
    public const string TopicCapability = "CapabilityWriter";

    /// <summary>Window synthesis: awaiting the window rows (#212).</summary>
    public const string WindowSynthesis = "PathWindowSynthesizer";

    // ---- exempt ----

    /// <summary>
    /// Credit movement: <c>CreditTransactions</c> is already append-only and immutable
    /// (<c>trg_CreditTransactions_AppendOnly</c>, <c>trg_CreditTransactions_NoRewrite</c>). SRS
    /// decision D-09 refuses a second mutation guard on the one table declared frozen, so this fact
    /// is registered but deliberately carries no writer column and no trigger.
    /// </summary>
    public const string CreditMovement = "CreditLedger";
}
