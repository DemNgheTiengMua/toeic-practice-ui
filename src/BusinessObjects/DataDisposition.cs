namespace BusinessObjects;

/// <summary>
/// #246.3, #246.4, #246.5: what survives a cutover, decided once.
/// <para>
/// D23's rule is "preserve what is an independent fact; drop what is a derived opinion", and the ticket
/// turns it into a list. A list in a document is not a decision: it cannot fail, so a new table arrives
/// unclassified and nobody notices until the cutover drops something it should have kept. This is the
/// same list as data, and <c>DataDispositionTests</c> asserts every table in the schema is named
/// exactly once - so adding a table without deciding its fate fails the suite.
/// </para>
/// </summary>
public enum Disposition
{
    /// <summary>An independent fact. Crosses the cutover unchanged, and is augmented rather than replaced.</summary>
    Preserve,

    /// <summary>A derived opinion of the old model. Deliberately NOT migrated: no mapping is invented.</summary>
    Drop,

    /// <summary>Infrastructure the new build owns; it is neither preserved data nor legacy debris.</summary>
    Rebuild,
}

/// <summary>One table's fate, and why. The reason is required: "drop" without a reason is indistinguishable from an oversight.</summary>
public sealed record DataDispositionEntry(string Table, Disposition Disposition, string Reason);

/// <summary>
/// #246: the disposition table, in the one place a test can read it.
/// </summary>
public static class DataDisposition
{
    public static readonly IReadOnlyList<DataDispositionEntry> Entries =
    [
        // ---- preserved: independent facts ------------------------------------------------------
        new("Users", Disposition.Preserve,
            "A learner is not derived from anything. Preserved and augmented (the rebuild adds skill state beside it)."),
        new("Roles", Disposition.Preserve, "Identity vocabulary; a role is not an opinion about a level."),
        new("Permissions", Disposition.Preserve, "Identity vocabulary; the staff gates read it at runtime."),
        new("RolePermissions", Disposition.Preserve, "The join between two preserved facts."),
        new("CreditTransactions", Disposition.Preserve,
            "The ledger is append-only and its balance IS its sum. #246's export/import carries every row and every reason string verbatim."),
        new("Orders", Disposition.Preserve, "A purchase happened; it is not derived from a level model."),
        new("PaymentEvents", Disposition.Preserve, "The gateway's own record of what it told us."),
        new("Certificates", Disposition.Preserve,
            "The catalogue survives and is augmented: it gains examined-skill vectors and per-skill cut scores."),
        new("Skills", Disposition.Preserve, "A certificate's examined modalities; the anchor the new level model reads."),
        new("Sections", Disposition.Preserve, "Paper structure; the sitting still renders it."),
        new("ScoreBands", Disposition.Preserve,
            "A certificate's own score bands. #259 forbids parsing them as CEFR levels, which is why they are preserved as bands and nothing more."),
        new("ScoreScales", Disposition.Preserve, "The raw-to-scaled table the certificate's own scale uses."),
        new("MentorProfiles", Disposition.Preserve, "A mentor's standing; augmented, not replaced."),
        new("MentorWeeklySlots", Disposition.Preserve, "A mentor's recurring weekly availability schedule."),
        new("MentorSlots", Disposition.Preserve, "Times a human committed to."),
        new("Bookings", Disposition.Preserve, "A commitment a learner paid for; the payload is refreshed, the booking survives."),
        new("Complaints", Disposition.Preserve, "A support case is independent of any level model."),
        new("Refunds", Disposition.Rebuild,
            "#280: refunds are new to this build. A refund records money or credits the product returned; there is no prior refund record to migrate, because until now the product had no refund at all."),
        new("ExamAttempts", Disposition.Preserve,
            "Completed mock sittings are preserved and tagged legacy_v1 (#246.2). In-progress and placement attempts are dropped at the row level, not by table."),
        new("AttemptAnswers", Disposition.Preserve, "What a preserved attempt chose; dropping it would leave the attempt unreadable."),
        new("AttemptSkillScores", Disposition.Preserve, "The per-skill snapshot a preserved attempt was scored with."),
        new("FreeResponses", Disposition.Preserve, "A preserved attempt's written or spoken answer."),
        new("AiGradings", Disposition.Preserve, "The grading a preserved free response received."),
        new("AiGradingScores", Disposition.Preserve, "The per-criterion scores behind that grading."),
        new("Exams", Disposition.Preserve, "A paper. Its CONTENT model moves (#250); the paper row does not."),

        // ---- dropped: derived opinions of the old model ----------------------------------------
        new("PlacementTests", Disposition.Drop,
            "D23: placement results are dropped and started clean. A placement taken under the old level model cannot be converted without inventing a crosswalk (PRN-03)."),
        new("UserCategoryMasteries", Disposition.Drop, "Same: a derived opinion about a learner, not an independent fact."),
        new("LearnerPaths", Disposition.Drop, "D23: learning paths are dropped and regenerated, because a plan built on the old level model is the wrong plan."),
        new("LearnerPathSteps", Disposition.Drop, "Belongs to a dropped path."),
        new("TargetBands", Disposition.Drop, "A goal stated in a certificate band; the rebuild states goals on the CEFR axis."),
        new("AiInsights", Disposition.Drop,
            "D23 drops insights except those attached to preserved mock attempts. The narrative is regenerated from stored facts."),
        new("Recommendations", Disposition.Drop, "Derived from a weakness analysis that is itself dropped."),
        new("PracticeSessions", Disposition.Drop, "Diagnostic practice history; the free loop starts clean."),
        new("PracticeAnswers", Disposition.Drop, "Belongs to a dropped session, and is keyed by the legacy question id."),
        new("SavedQuestions", Disposition.Drop,
            "#246.5: legacy saved links are dropped rather than re-pointed. The rebuild starts clean against the warehouse."),
        new("Lessons", Disposition.Drop, "D23: lessons are dropped and replaced."),
        new("LessonCards", Disposition.Drop, "Belongs to a dropped lesson."),
        new("LearnerLessonProgress", Disposition.Drop, "Progress through a dropped lesson."),
        new("LearnerLessonMicroChecks", Disposition.Drop, "Belongs to a dropped lesson."),
        new("QuestionCategories", Disposition.Drop, "The legacy topic axis; replaced by the canonical twenty."),
        new("SectionCriteria", Disposition.Drop, "The legacy rubric shape; the rebuild authors descriptors instead."),
        new("GradingCriteria", Disposition.Drop, "The legacy rubric."),
        new("Topics", Disposition.Drop, "D23: topics are dropped and replaced by the canonical twenty."),
        new("Stimuli", Disposition.Drop, "Legacy stimulus content; the pool's Stimuli row is written by the new build."),
        new("Items", Disposition.Drop, "D23: legacy items are dropped and replaced by the pool."),
        new("ItemVersions", Disposition.Drop, "Belongs to a dropped item."),
        new("ImportBatches", Disposition.Drop, "Ingestion provenance for content that is being dropped."),
        new("ImportLog", Disposition.Drop, "Belongs to a dropped batch."),
        new("Reviews", Disposition.Drop, "The legacy review flow."),
        new("ExamSectionAudio", Disposition.Drop, "Legacy paper audio; the pool's Stimuli carries audio after #250."),
        new("EmailVerificationTokens", Disposition.Drop, "A short-lived token; re-issuing one is cheaper than migrating it."),
        new("PasswordResetTokens", Disposition.Drop, "Same: a short-lived token."),

        // ---- rebuilt: the new build's own machinery --------------------------------------------
        new("ExamItems", Disposition.Rebuild, "#250's roster: the paper's positions, read from the pool."),
        new("CatalogMode", Disposition.Rebuild, "A test-environment switch, not data."),
    ];

    /// <summary>The entry for a table, or null when nobody has decided its fate.</summary>
    public static DataDispositionEntry? For(string table) =>
        Entries.FirstOrDefault(e => string.Equals(e.Table, table, StringComparison.OrdinalIgnoreCase));
}
