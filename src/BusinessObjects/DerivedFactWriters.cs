using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BusinessObjects;

/// <summary>
/// #191 / D14 / TRN-06: the eleven derived facts and the one service allowed to write each.
///
/// These names are what a write declares in <c>WrittenByService</c>, and a trigger compares the
/// declaration against <c>DerivedFactOwners</c>. A string that is not exactly one of these is refused
/// at write time, so a typo is a failure rather than a silently unguarded second writer.
///
/// PRN-01: the identity travels as a column on the write, never through
/// <c>sp_set_session_context</c> — session context survives commit and rollback on a pooled
/// connection and would vouch for whatever ran next.
///
/// Coverage today is ten of the eleven (#237 added per-cell mastery, #274 the attempt paper). The rest
/// are named here so the registry has one source in the application as well as in the schema, and so a
/// later ticket has a name to declare rather than inventing one.
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

    /// <summary>
    /// #227: learner per-skill state, <c>LearnerSkillStates</c>.
    /// </summary>
    public const string LearnerSkillState = "SkillStateWriter";

    /// <summary>
    /// #227: the frozen edition snapshot, <c>ExamEditions</c>. Written by the publish trigger's own
    /// declared name - the freeze is the only thing that can know what was in force at that instant,
    /// and a reader that re-derived it would print a number nobody was measured against.
    /// </summary>
    public const string ExamEditionSnapshot = "ExamEditionFreeze";

    /// <summary>
    /// #212: one row per skill per placement, <c>PlacementSkillResults</c>. Registered as its own fact
    /// rather than folded into <see cref="PlacementResult"/>: <c>CoveredTable</c> names exactly one
    /// table, so folding it in would leave this table guarded by nothing.
    /// </summary>
    public const string PlacementSkillResult = "AttemptEngine";

    /// <summary>
    /// #206: the audit's outcome for one stratum of one batch, <c>ReviewStrata</c>. The status is
    /// derived from the sample verdicts - one key error or a non-key defect rate above 5% quarantines -
    /// so a second service setting it would be a second answer to "was this batch promoted". Drawing
    /// the sample derives nothing and declares nothing.
    /// </summary>
    public const string StratumDecision = "StratifiedAuditService";

    /// <summary>
    /// #237: per-cell mastery, <c>UserCellMasteries</c>. The estimate and the four states are derived
    /// from the learner's graded answers, so <c>MasteryService</c> is the only service that writes the
    /// row. The estimate is a probability with a prior, which SQL Server cannot express as a computed
    /// column, so the writer is a service rather than the schema - but the state machine it calls is a
    /// pure function in <see cref="MasteryCellMachine"/>, so the arithmetic has exactly one home.
    /// </summary>
    public const string TopicCapability = "CapabilityWriter";

    /// <summary>
    /// #274: the paper a sitting was served, <c>AttemptStageBundles</c>. The stage plan and the routed
    /// band pair per skill are derived from the draw, and a second draw restating them would show a
    /// learner a paper they are not sitting - so <c>AttemptEngine</c> is the only writer.
    /// </summary>
    public const string AttemptPaper = "AttemptEngine";

    // ---- awaiting a table ----

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
