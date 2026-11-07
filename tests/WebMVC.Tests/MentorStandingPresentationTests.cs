using System.IO;
using Contracts;
using WebMVC.Models;

namespace WebMVC.Tests;

/// <summary>
/// #223 / D21, the presentation half. The service test proves what the payload contains; these prove
/// that the page says three different things about three different absences, and that a band code which
/// happens to read "B2" cannot reach the Level column.
/// <para>
/// <b>The trap this file exists for.</b> A row modelled as "level or null" renders every absence as the
/// same dash. "We have not tested you in reading yet" and "this certificate does not test speaking"
/// then look identical to a mentor, which is worse than saying nothing: one is a task for the learner
/// and the other is a fact about the paper.
/// </para>
/// </summary>
public sealed class MentorStandingPresentationTests
{
    private static MentorLearnerStandingDto Standing(
        bool hasNoMeasurement, string? overall, params MentorSkillStandingDto[] rows) =>
        new(1, "TOEIC", hasNoMeasurement, overall, null, null, rows);

    private static MentorSkillStandingDto Measured(string modality, string skill, string level, int position) =>
        new(1, modality, skill, skill, MentorSkillStandingStatus.Measured, level, position);

    private static MentorSkillStandingDto NotMeasured(string modality, string skill) =>
        new(1, modality, skill, skill, MentorSkillStandingStatus.NotMeasured);

    private static MentorSkillStandingDto NotExamined(string modality) =>
        new(null, modality, null, null, MentorSkillStandingStatus.NotExamined);

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> a single "level or null" column. Every
    /// absence renders as the same empty cell, the table still has the right number of rows, and no
    /// assertion on counts or levels would notice. This asserts the three words are three words.
    /// </summary>
    [Fact]
    public void Not_measured_and_not_examined_are_two_different_sentences()
    {
        var model = new MentorStandingViewModel(Standing(false, "B1",
            Measured("listening", "LIST", "B1", 40),
            NotMeasured("reading", "READ"),
            NotExamined("speaking")));

        var states = model.Rows.Select(r => r.State).ToList();

        Assert.Equal(3, states.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("—", states);
        Assert.DoesNotContain("-", states);
        Assert.All(states, state => Assert.False(string.IsNullOrWhiteSpace(state)));

        // The paper's fact and the learner's task are named as what they are.
        Assert.Contains("Not measured", states[1], StringComparison.Ordinal);
        Assert.Contains("Not examined", states[2], StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> a not-examined row that still carries a
    /// level - exactly what a payload built by looking up "the band for this certificate" produces,
    /// because the lookup finds one for the certificate and hands it to every row. The row must print
    /// no level and no position, and must still name the modality.
    /// </summary>
    [Fact]
    public void A_row_the_certificate_does_not_examine_shows_no_level_at_all()
    {
        var row = new MentorStandingRowViewModel(new MentorSkillStandingDto(
            null, "speaking", null, null, MentorSkillStandingStatus.NotExamined,
            LevelCode: "B2", PositionInBand: 55));

        Assert.Null(row.Level);
        Assert.Null(row.Position);
        Assert.Equal("Speaking", row.Modality);
        Assert.Equal("Speaking", row.SkillName);
        Assert.Contains("Not examined", row.State, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> the payload gaining a fourth absence - a
    /// "measured" status whose level is the certificate's band. On this fixture the four skills are all
    /// measured and the certificate's band code is "B2", so printing the band would look right for B2
    /// and be wrong for C2. The C2 row has no C2 band to come from, which is the assertion that
    /// separates them: a level that could only have come from the measured state.
    /// </summary>
    [Fact]
    public void A_measured_level_that_the_certificate_has_no_band_for_is_still_printed()
    {
        var model = new MentorStandingViewModel(Standing(false, "B2",
            Measured("listening", "LIST", "B2", 12),
            Measured("reading", "READ", "C2", 84)));

        Assert.Equal(["B2", "C2"], model.Rows.Select(r => r.Level).ToList());
        Assert.Equal(["12/100 in level", "84/100 in level"], model.Rows.Select(r => r.Position).ToList());
        Assert.Equal("B2", model.OverallText);
    }

    /// <summary>
    /// Nothing measured at all is the third state: one notice and no table, not four rows of absences.
    /// <b>The input that passes a wrong implementation:</b> a table of four "not measured" rows, which
    /// is true for a four-skill certificate and tells the learner's mentor nothing about what to do.
    /// </summary>
    [Fact]
    public void Nothing_measured_at_all_is_one_notice_rather_than_a_table_of_dashes()
    {
        var model = new MentorStandingViewModel(Standing(true, null,
            NotMeasured("listening", "LIST"), NotMeasured("reading", "READ")));

        Assert.True(model.OwesPlacement);
        // The overall is not invented, and no limiting skill is named from nothing.
        Assert.Equal("No overall yet", model.OverallText);
        Assert.Null(model.LimitingSkillText);
    }

    /// <summary>
    /// The overall is always printed beside the skill that produced it, because an overall with no
    /// name lets a mentor read a low figure and guess the modality.
    /// </summary>
    [Fact]
    public void The_limiting_skill_is_named_beside_the_overall()
    {
        var model = new MentorStandingViewModel(new MentorLearnerStandingDto(
            1, "TOEIC", false, "B1", 7, "READ",
            [Measured("listening", "LIST", "B2", 60), Measured("reading", "READ", "B1", 20)]));

        Assert.Equal("B1", model.OverallText);
        Assert.Contains("READ", model.LimitingSkillText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page renders the standing for a booking made by the current code, and the view never reads a
    /// band. <b>The input that passes a wrong implementation:</b> a page that prints
    /// <c>BandCode</c> where the level belongs - the string on the left of the assertion is the exact
    /// property that would have to be named, and it is absent.
    /// </summary>
    [Fact]
    public void The_booking_detail_renders_the_standing_and_never_reads_a_band()
    {
        var root = TestPathHelper.FindRepoRoot();
        var detail = File.ReadAllText(Path.Combine(root, "src", "WebMVC", "Views", "Mentors", "BookingDetail.cshtml"));
        var partial = File.ReadAllText(Path.Combine(root, "src", "WebMVC", "Views", "Mentors", "_MentorStanding.cshtml"));

        Assert.Contains("_MentorStanding", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("BandCode", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("BandCode", partial, StringComparison.Ordinal);
        Assert.DoesNotContain("ScoreBands", partial, StringComparison.Ordinal);
    }

    /// <summary>
    /// An existing booking renders: the standing is derived at read time, so a row written before this
    /// payload existed simply has no stored payload to read. The model's own null case - a certificate
    /// that cannot be described - must degrade to no block rather than a half-built table.
    /// </summary>
    [Fact]
    public void A_booking_with_no_derivable_standing_still_renders_without_the_block()
    {
        var booking = new MentorBookingDto(
            1, 7, 1, "Dr. Minh Khoa", "Headline", "human", 1,
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            "confirmed", "https://meet.google.com/abc-defg-hij", "Listening", 1, null, DateTimeOffset.UtcNow);

        var withoutStanding = new MentorBookingDetailViewModel(booking);
        Assert.Null(withoutStanding.Standing);

        var withStanding = new MentorBookingDetailViewModel(
            booking with { Standing = Standing(false, "B1", Measured("listening", "LIST", "B1", 30)) });
        Assert.NotNull(withStanding.Standing);
        Assert.Equal("B1", withStanding.Standing.OverallText);
    }
}
