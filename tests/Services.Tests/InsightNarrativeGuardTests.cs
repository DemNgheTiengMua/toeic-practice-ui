using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #244 / NFI-10: the guard that stands between a generated narrative and a learner.
/// <para>
/// Every test here is written as <b>the input that would pass a wrong implementation</b>. The four
/// shapes a plausible-but-wrong guard accepts are:
/// </para>
/// <list type="number">
///   <item>A regex over "B1"-shaped strings, which passes "your writing is at B1" whenever B1 exists
///   anywhere - for another skill, or for the target band.</item>
///   <item>A numeric check that only inspects numbers that appear, which passes "your weakest skill is
///   speaking" - an invented claim with no digit at all.</item>
///   <item>A check against the certificate instead of the payload, which passes a real skill that has
///   no measurement ("your writing is at A1").</item>
///   <item>An allowlist of words, which passes "B2 is your target, so keep going" when B2 is only the
///   goal and no skill measures it.</item>
/// </list>
/// </summary>
public sealed class InsightNarrativeGuardTests
{
    /// <summary>
    /// A four-skill certificate with two measured skills: listening at B2, reading at C1. Writing and
    /// speaking are examined but unmeasured - #211 keeps them out of the standing, so the payload
    /// carries them with a null level. The overall is reading (C1 is stronger than B2? no - the overall
    /// is the WEAKEST measured, so listening B2 at 40/100). Reading C1 at 70/100 is therefore the
    /// stronger skill and must never be narrated as the weakest.
    /// </summary>
    private static InsightFacts Facts() => new(
        "TOEIC",
        [
            new(1, "LIST", "Listening", "listening", CefrLevel.B2, 40, 4.35m),
            new(2, "READ", "Reading", "reading", CefrLevel.C1, 70, 5.60m),
            new(3, "SPEA", "Speaking", "speaking", null, null, null),
            new(4, "WRIT", "Writing", "writing", null, null, null),
        ],
        Overall: CefrLevel.B2,
        LimitingSkillCode: "LIST",
        TargetBandCode: "B2",
        TargetBandName: "Vantage",
        FeasibilityVerdict: "stretch");

    private static AiInsightGenerationRequest Request(InsightFacts? facts = null) => new(
        UserId: 1,
        CertId: 1,
        CertName: "TOEIC",
        Scope: AiInsightScope.Dashboard,
        PracticeSessionId: null,
        AttemptId: null,
        ActivityTitle: "TOEIC Dashboard",
        TotalQuestions: 40,
        AnsweredCount: 20,
        CorrectCount: 13,
        HintUsedCount: null,
        CorrectWithHintCount: null,
        TargetBandCode: "B2",
        TargetBandName: "Vantage",
        ScaledScore: 750,
        CurrentBandCode: "B2",
        Questions: Array.Empty<InsightQuestionDetail>(),
        SectionAccuracies: [new(5, "P5", "Incomplete Sentences", 20, 13, 65)],
        CategoryAccuracies: [new(9, "TENSE", "Verb tense", 10, 6, 60)],
        Facts: facts ?? Facts());

    // =============================================================================================
    // A citation the payload does not hold.
    // =============================================================================================

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "Your reading accuracy is 82%." 82 is a number, it
    /// is a plausible percentage, and a guard that only inspects the shape of a percentage accepts it.
    /// This one is refused because no field, and no arithmetic over a field, yields 82.
    /// </summary>
    [Fact]
    public void An_invented_percentage_is_rejected()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "Reading is your strongest skill at 82% accuracy. Keep drilling Part 5.");

        Assert.NotNull(reason);
        Assert.Contains("82", reason);
    }

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "You got 9 of 12 right." The payload says 13 of
    /// 20. A guard that allows "any count up to the largest count" accepts 9 and 12, because both sit
    /// inside the range the code could have produced. Refused because neither is a field's value nor a
    /// length of a subset of the payload's rows.
    /// </summary>
    [Fact]
    public void An_invented_count_inside_the_payloads_range_is_rejected()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "You answered 9 of 12 questions correctly in listening.");

        Assert.NotNull(reason);
        Assert.Contains("9", reason);
    }

    // =============================================================================================
    // A level claim checked against the payload, not against the token.
    // =============================================================================================

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "Reading is at B2." B2 is in the payload - it is
    /// listening's level and the target band - so a regex over level tokens anywhere accepts it. It is
    /// refused because the sentence names reading and gives it a level reading does not have.
    /// </summary>
    [Fact]
    public void A_level_attributed_to_the_wrong_skill_is_rejected()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "Reading is at B2, so the next step is more practice.");

        Assert.NotNull(reason);
        Assert.Contains("Reading", reason);
        Assert.Contains("B2", reason);
    }

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "Your overall level is C1." C1 is a real level and
    /// it is in the payload - reading's - so a token check passes. It is refused because the payload's
    /// overall is B2, and a sentence naming the overall may not move it.
    /// </summary>
    [Fact]
    public void An_overall_level_that_contradicts_the_payload_is_rejected()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "Your overall level is C1.");

        Assert.NotNull(reason);
        Assert.Contains("C1", reason);
    }

    /// <summary>The honest half of the same guard: the overall the payload computed may be quoted.</summary>
    [Fact]
    public void The_payloads_overall_and_limiting_skill_may_be_quoted()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(),
            "Your overall level is B2, with listening at 40/100 holding you back.");

        Assert.Null(reason);
    }

    // =============================================================================================
    // #211's distinction: an unmeasured skill is absent, not present at A1.
    // =============================================================================================

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "Your writing is at B2." B2 is a real level AND it
    /// is in the payload - listening's level and the target band - so both the token check and a check
    /// against the certificate's level range accept it. It is refused because the payload has no level
    /// for writing at all: #211 keeps an unmeasured skill absent, and giving it a level is the
    /// fabricated-bottom-band failure that invented.
    /// </summary>
    [Fact]
    public void A_level_given_to_an_unmeasured_skill_is_rejected()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "Your writing is at B2, so start with paragraph-level drills.");

        Assert.NotNull(reason);
        Assert.Contains("Writing", reason);
    }

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "Your writing is at A1." A1 is not in the payload
    /// at all, so the level check catches it first - but the interesting failure is the one that reaches
    /// the same sentence through a level the payload DOES hold, above. This test pins that the bottom
    /// band is not a safe default: nothing in the payload authorises A1 either.
    /// </summary>
    [Fact]
    public void The_bottom_band_is_not_a_default_for_an_unmeasured_skill()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "Your writing is at A1.");

        Assert.NotNull(reason);
        Assert.Contains("A1", reason);
    }

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "Speaking is holding you back." No digit, no level
    /// token - the shape-based checks see nothing at all. It is refused because it describes the
    /// standing of a skill the payload has no measurement for, and because it claims a weakest skill
    /// that the payload does not name.
    /// </summary>
    [Fact]
    public void A_standing_claim_about_an_unmeasured_skill_with_no_number_is_rejected()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "Speaking is holding you back.");

        Assert.NotNull(reason);
        Assert.Contains("Speaking", reason);
    }

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "Writing has not been measured yet." A naive rule
    /// that refuses any mention of an unmeasured skill rejects the one sentence that is true. The
    /// guard must let the honest statement through, or the model cannot say what the learner owes.
    /// </summary>
    [Fact]
    public void Naming_an_unmeasured_skill_as_unmeasured_is_allowed()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(),
            "Listening is at B2 at 40/100 and reading at C1. Writing is not measured yet, so a placement will fill it in.");

        Assert.Null(reason);
    }

    // =============================================================================================
    // Criterion 3: a skill the certificate does not examine.
    // =============================================================================================

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "Your speaking accuracy in Part 3 is 71%." On a
    /// listening-and-reading certificate, speaking is not a skill at all, so no amount of measurement
    /// produces it. A guard that only checks the four modalities it was handed accepts the sentence,
    /// because 71% looks like a percentage. Refused because the payload has no speaking row and 71 is
    /// nowhere in it.
    /// </summary>
    [Fact]
    public void A_skill_the_certificate_does_not_examine_is_rejected()
    {
        var facts = new InsightFacts(
            "TOEIC",
            [
                new(1, "LIST", "Listening", "listening", CefrLevel.B2, 40, 4.35m),
                new(2, "READ", "Reading", "reading", CefrLevel.C1, 70, 5.60m),
            ],
            Overall: CefrLevel.B2,
            LimitingSkillCode: "LIST",
            TargetBandCode: null,
            TargetBandName: null,
            FeasibilityVerdict: null);

        var reason = InsightNarrativeGuard.Rejection(
            Request(facts), "Your reading is strong at 70/100 in band.");

        Assert.Null(reason);

        var rejected = InsightNarrativeGuard.Rejection(
            Request(facts), "Speaking is your weak point; drill the pronunciation prompts.");

        Assert.NotNull(rejected);
        Assert.Contains("speaking", rejected, StringComparison.OrdinalIgnoreCase);
    }

    // =============================================================================================
    // The limiting skill is the payload's, not a second computation.
    // =============================================================================================

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "Reading is your weakest skill." Reading is a real
    /// measured skill with a real level and a real position, so every payload-existence check passes.
    /// It is refused because the payload computed listening as the weakest, and naming reading is a
    /// second answer to the same question - the defect class #211's tie-break exists to prevent.
    /// </summary>
    [Fact]
    public void Naming_a_different_skill_as_the_weakest_is_rejected()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "Reading is your weakest skill, so focus there.");

        Assert.NotNull(reason);
        Assert.Contains("LIST", reason);
    }

    /// <summary>
    /// <b>The wrong input that would still pass:</b> "Your weakest skill needs work." With nothing
    /// measured, an "area to improve" reads as a measured gap. Refused because the payload computed no
    /// overall, so there is no weakest skill to name.
    /// </summary>
    [Fact]
    public void Claiming_a_weakest_skill_when_nothing_is_measured_is_rejected()
    {
        var facts = new InsightFacts(
            "TOEIC",
            [new(1, "LIST", "Listening", "listening", null, null, null)],
            Overall: null,
            LimitingSkillCode: null,
            TargetBandCode: null,
            TargetBandName: null,
            FeasibilityVerdict: null);

        var reason = InsightNarrativeGuard.Rejection(
            Request(facts) with { Facts = facts }, "Listening is your weakest skill; start there.");

        Assert.NotNull(reason);
        Assert.Contains("no measurement", reason);
    }

    // =============================================================================================
    // The guard must not refuse honest prose, or every narrative fails.
    // =============================================================================================

    /// <summary>
    /// <b>The wrong input that would still pass:</b> a narrative checked only when a payload happens to
    /// be attached. A future request builder that forgets <see cref="AiInsightGenerationRequest.Facts"/>
    /// would silently lose the whole guard. The number check is therefore unconditional: it costs one
    /// set of stored figures to build, and it is the check that survives a missing payload.
    /// </summary>
    [Fact]
    public void The_number_check_does_not_depend_on_the_payload_being_attached()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request() with { Facts = null }, "You scored 97% on this set.");

        Assert.NotNull(reason);
        Assert.Contains("97", reason);
    }

    /// <summary>
    /// <b>The wrong input that would still pass:</b> a level token matched anywhere it appears. The
    /// certificate's own code can contain "a2" ("PRa2f1"), so an honest narrative that names its
    /// certificate was rejected whenever the random code happened to contain a level - one run in ten,
    /// which reads as flakiness and hides a false refusal. The token must be a whole word.
    /// </summary>
    [Theory]
    [InlineData("You practised on certificate PRa2f1 today.")]
    [InlineData("Your reading is at C1, the strongest measured skill.")]
    public void A_level_shaped_substring_inside_a_label_is_not_a_level_claim(string narrative)
    {
        var reason = InsightNarrativeGuard.Rejection(Request(), narrative);

        Assert.Null(reason);
    }

    /// <summary>
    /// <b>The wrong input that would still pass:</b> a guard strict enough to refuse everything is
    /// trivially "safe" and useless. This is the narrative shape the deterministic generator actually
    /// produces, and it must pass: a figure the payload derives, a skill it names honestly, and the
    /// target band it holds.
    /// </summary>
    [Fact]
    public void A_narrative_that_only_quotes_payload_facts_is_allowed()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(),
            "You answered 13 of 20 questions at 65% in Incomplete Sentences. " +
            "Listening is at B2 and is your weakest measured skill at 40/100 in band. " +
            "Writing and speaking are not measured yet. Your target band is B2.");

        Assert.Null(reason);
    }

    /// <summary>
    /// <b>The wrong input that would still pass:</b> a guard that treats the target band code as a
    /// level. "Reach for B2" is the goal, not a claim that any skill is at B2 - PRN-03's whole point is
    /// that the two are different quantities, and a guard that conflates them either refuses the goal
    /// or accepts a level claim about it.
    /// </summary>
    [Fact]
    public void The_target_band_is_not_a_level_claim()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "Your target band is B2; keeping listening at 40/100 closed is the priority.");

        Assert.Null(reason);
    }

    // =============================================================================================
    // Word boundaries in the claim words themselves.
    // =============================================================================================

    /// <summary>
    /// <b>The wrong input that would still pass:</b> a guard that matches claim words with
    /// <c>string.Contains</c>. "aim" is inside <em>claim</em>, so "you can claim your result" is read as
    /// naming the goal - which switches the level-attribution check into its permissive branch, and a
    /// level given to the wrong skill then passes.
    /// </summary>
    [Fact]
    public void A_word_that_merely_contains_a_goal_word_does_not_name_the_goal()
    {
        // "Reading is at B2" where reading is C1 and B2 is listening's. It is refused only when the
        // sentence is NOT read as naming the overall/goal. "claim" contains "aim".
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "You can claim Reading is at B2 once the drill is done.");

        Assert.NotNull(reason);
    }

    /// <summary>
    /// <b>The wrong input that would still pass:</b> the same <c>Contains</c> bug the other way.
    /// "reach" is inside <em>reached</em>, so a sentence reporting what the learner actually reached is
    /// read as a goal statement and licensed to quote the overall band.
    /// </summary>
    [Fact]
    public void A_past_tense_of_reach_does_not_make_a_level_the_goal()
    {
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "You reached Reading at B2, which this certificate does not measure there.");

        Assert.NotNull(reason);
    }

    /// <summary>
    /// The control: the same words used honestly, as whole words, are still accepted. A guard that
    /// refuses "your target band is B2" solves nothing.
    /// </summary>
    [Fact]
    public void The_whole_word_goal_phrases_still_license_the_target_band()
    {
        Assert.Null(InsightNarrativeGuard.Rejection(
            Request(), "Your goal is B2, and your target B2 is reachable this term."));
    }

    /// <summary>
    /// A certificate may be NAMED for the skills it examines, and the narrative echoes the activity
    /// title. When the title carried the certificate's name ("TOEIC Listening &amp; Reading dashboard"),
    /// `InsightNarrativeGuard` read that echo as a claim about two unmeasured skills and rejected a
    /// correct narrative - so the demo learner's dashboard insight failed on every retry with
    /// "Narrative describes Listening, Reading, which has no measurement in the payload".
    /// <para>
    /// The fix is in the BUILDER, which now titles the activity with the certificate CODE. This test
    /// pins the guard's half of the contract: an input that names only the certificate does not
    /// license claims about its unmeasured skills.
    /// </para>
    /// </summary>
    [Fact]
    public void A_claim_about_an_unmeasured_skill_is_still_refused()
    {
        // Writing and Speaking are examined but unmeasured in the payload, so a LEVEL attached to one
        // is the fabricated-bottom-band failure #211 forbids. The fix for the dashboard bug was to stop
        // the activity title naming skills - not to let a narrative name a level for one.
        var reason = InsightNarrativeGuard.Rejection(
            Request(), "Your writing is at B1, so focus there next.");

        // It is refused, and the reason is the level token: 'B1' is not a level the payload holds for
        // writing, so the narrative is citing a level that does not exist. (The "no measurement" arm is
        // a second, weaker guard behind this one.)
        Assert.NotNull(reason);
        Assert.Contains("level 'B1'", reason);
    }
}
