namespace BusinessObjects;

/// <summary>
/// D10: the three execution paths a step can take. This is not a display label — each branch opens a
/// different surface, and a value that all three routes went through alike would not be three types.
/// <para>
/// The mapping is <see cref="LearningPathPlanner.ExecutionRouteOf"/>, asserted in
/// <c>PathStepCoordinateTests</c>, and <c>CK_Steps_ResourceMatchesType</c> refuses a row whose
/// resource does not match its type.
/// </para>
/// </summary>
public enum PathStepType
{
    /// <summary>Free diagnostic. Items come from the unified selector at (skill, topic, level).</summary>
    DiagnosticDrill,

    /// <summary>Blocked practice on a weak topic. Also selector-driven.</summary>
    FocusPractice,

    /// <summary>Spaced review of an earlier topic. Also selector-driven.</summary>
    ReviewSpaced,

    /// <summary>A leveled lesson, read in the lesson reader. Names its lesson.</summary>
    ConceptLesson,

    /// <summary>A lesson plus remedial drill for a weak spot. Also names its lesson.</summary>
    ExpandedRemedial,

    /// <summary>A timed paper section, sat through <c>IAttemptEngine</c>. Names its exam.</summary>
    MockSection,
}

/// <summary>
/// D10's single definition of how a step type is executed. The three routes are the ticket's three
/// bindings; a fourth value with no route is not allowed to exist.
/// </summary>
public enum StepRoute
{
    /// <summary>D11's unified item selector. The step carries a count, not a resource.</summary>
    ItemSelector,

    /// <summary>The lesson reader.</summary>
    LessonReader,

    /// <summary><c>IAttemptEngine</c>.</summary>
    AttemptEngine,
}
