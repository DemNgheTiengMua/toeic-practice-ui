using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

public sealed partial class LearnerServiceTests
{
    private async Task<int> SeedHumanMentorAsync(string certCode, string displayName, string? headline = null)
    {
        var mentorUserId = await SeedLearnerAsync(certCode);
        var hlSql = headline is null ? "NULL" : $"N'{headline}'";
        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            INSERT INTO MentorProfiles (UserId, DisplayName, CertId, Type, PricePerSlot, PriceCredits, Headline, SessionMinutes, IsActive)
            VALUES ({mentorUserId}, N'{displayName}', @cert, 'human', 200000, 2, {hlSql}, 60, 1);
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.MentorProfiles
            .Where(m => m.UserId == mentorUserId && m.DisplayName == displayName)
            .Select(m => m.MentorId)
            .SingleAsync();
    }

    private async Task<int> SeedAiMentorAsync(string certCode, string displayName)
    {
        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            INSERT INTO MentorProfiles (UserId, DisplayName, CertId, Type, PricePerSlot, PriceCredits, SessionMinutes, IsActive)
            VALUES (NULL, N'{displayName}', @cert, 'ai', 0, 1, 30, 1);
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.MentorProfiles
            .Where(m => m.UserId == null && m.DisplayName == displayName)
            .OrderByDescending(m => m.MentorId)
            .Select(m => m.MentorId)
            .FirstAsync();
    }

    private async Task<int> SeedHumanBookingAsync(
        int learnerId,
        int mentorId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string status = "confirmed",
        string? focusArea = null,
        string? meetLink = null)
    {
        var focusSql = focusArea is null ? "NULL" : $"N'{focusArea}'";
        var meetSql = meetLink is null ? "NULL" : $"N'{meetLink}'";
        var cancelledAtSql = status == "cancelled" ? "SYSDATETIMEOFFSET()" : "NULL";

        await database.SqlAsync($"""
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES ({mentorId}, '{startAt:O}', '{endAt:O}', 'open');
            DECLARE @slot INT = SCOPE_IDENTITY();

            INSERT INTO Bookings (UserId, MentorId, SlotId, MentorType, Status, FocusArea, MeetLink, CancelledAt)
            VALUES ({learnerId}, {mentorId}, @slot, 'human', '{status}', {focusSql}, {meetSql}, {cancelledAtSql});
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Bookings
            .Where(b => b.UserId == learnerId && b.MentorId == mentorId && b.Slot!.StartAt == startAt)
            .OrderByDescending(b => b.BookingId)
            .Select(b => b.BookingId)
            .FirstAsync();
    }

    private Task SeedAiBookingAsync(int learnerId, int aiMentorId, string status = "confirmed") =>
        database.SqlAsync($"""
            INSERT INTO Bookings (UserId, MentorId, SlotId, MentorType, Status, FocusArea)
            VALUES ({learnerId}, {aiMentorId}, NULL, 'ai', '{status}', N'AI Speaking Drill');
            """);

    [Fact]
    public async Task UpcomingBooking_is_null_when_learner_has_no_confirmed_future_human_booking()
    {
        var code = Unique("BN");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Null(dashboard.UpcomingBooking);
    }

    [Fact]
    public async Task UpcomingBooking_selects_earliest_confirmed_future_human_booking_for_active_certificate()
    {
        var code = Unique("BE");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);
        var mentorA = await SeedHumanMentorAsync(code, "Coach Lan", "12 years TOEIC Listening & Speaking");
        var mentorB = await SeedHumanMentorAsync(code, "Coach Minh", "IELTS & TOEIC Reading Specialist");

        var now = DateTimeOffset.UtcNow;
        var laterStart = now.AddDays(5);
        var laterEnd = laterStart.AddHours(1);
        var soonerStart = now.AddDays(2);
        var soonerEnd = soonerStart.AddHours(1);

        await SeedHumanBookingAsync(
            userId, mentorB, laterStart, laterEnd,
            status: "confirmed",
            focusArea: "Part 7 double passages",
            meetLink: "https://meet.example.com/later");

        var expectedBookingId = await SeedHumanBookingAsync(
            userId, mentorA, soonerStart, soonerEnd,
            status: "confirmed",
            focusArea: "Part 3 implied meaning",
            meetLink: "https://meet.example.com/sooner");

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.NotNull(dashboard.UpcomingBooking);
        Assert.Equal(expectedBookingId, dashboard.UpcomingBooking.BookingId);
        Assert.Equal("Coach Lan", dashboard.UpcomingBooking.MentorName);
        Assert.Equal("12 years TOEIC Listening & Speaking", dashboard.UpcomingBooking.MentorHeadline);
        Assert.Equal("Part 3 implied meaning", dashboard.UpcomingBooking.FocusArea);
        Assert.Equal(soonerStart, dashboard.UpcomingBooking.StartAt);
        Assert.Equal(soonerEnd, dashboard.UpcomingBooking.EndAt);
        Assert.Equal("https://meet.example.com/sooner", dashboard.UpcomingBooking.MeetLink);
    }

    [Fact]
    public async Task UpcomingBooking_ignores_cancelled_done_pending_past_ai_other_cert_and_other_learner_bookings()
    {
        var toeic = Unique("BT");
        var ielts = Unique("BI");
        await SeedCertificateAsync(toeic);
        await SeedCertificateAsync(ielts);

        var mine = await SeedLearnerAsync(toeic);
        var theirs = await SeedLearnerAsync(toeic);

        var toeicMentor = await SeedHumanMentorAsync(toeic, "Coach Thao", "TOEIC Strategist");
        var toeicAiMentor = await SeedAiMentorAsync(toeic, "AI Coach Bot");
        var ieltsMentor = await SeedHumanMentorAsync(ielts, "Coach David", "Former IELTS Examiner");

        var now = DateTimeOffset.UtcNow;

        // 1. Past confirmed booking on active cert -> ignored
        await SeedHumanBookingAsync(
            mine, toeicMentor, now.AddHours(-3), now.AddHours(-2),
            status: "confirmed", focusArea: "Past session");

        // 2. Cancelled future booking on active cert -> ignored
        await SeedHumanBookingAsync(
            mine, toeicMentor, now.AddHours(4), now.AddHours(5),
            status: "cancelled", focusArea: "Cancelled session");

        // 3. Done future booking on active cert -> ignored
        await SeedHumanBookingAsync(
            mine, toeicMentor, now.AddHours(6), now.AddHours(7),
            status: "done", focusArea: "Done session");

        // 4. Pending future booking on active cert -> ignored
        await SeedHumanBookingAsync(
            mine, toeicMentor, now.AddHours(8), now.AddHours(9),
            status: "pending", focusArea: "Pending session");

        // 5. AI confirmed booking on active cert -> ignored
        await SeedAiBookingAsync(mine, toeicAiMentor, status: "confirmed");

        // 6. Another learner's confirmed future booking on active cert -> ignored
        await SeedHumanBookingAsync(
            theirs, toeicMentor, now.AddHours(2), now.AddHours(3),
            status: "confirmed", focusArea: "Other learner session");

        // 7. Current learner's confirmed future booking on OTHER cert (IELTS) -> ignored while TOEIC is active
        var ieltsStart = now.AddDays(1);
        var ieltsEnd = ieltsStart.AddHours(1);
        var ieltsBookingId = await SeedHumanBookingAsync(
            mine, ieltsMentor, ieltsStart, ieltsEnd,
            status: "confirmed",
            focusArea: "Speaking Part 2 cue cards",
            meetLink: "https://meet.example.com/ielts-david");

        var toeicDashboard = await CreateService().GetDashboardAsync(mine);
        Assert.Null(toeicDashboard.UpcomingBooking);

        // Switching active certificate to IELTS surfaces the confirmed future IELTS booking
        await CreateService().SwitchCertificateAsync(mine, ielts);
        var ieltsDashboard = await CreateService().GetDashboardAsync(mine);

        Assert.NotNull(ieltsDashboard.UpcomingBooking);
        Assert.Equal(ieltsBookingId, ieltsDashboard.UpcomingBooking.BookingId);
        Assert.Equal("Coach David", ieltsDashboard.UpcomingBooking.MentorName);
        Assert.Equal("Former IELTS Examiner", ieltsDashboard.UpcomingBooking.MentorHeadline);
        Assert.Equal("Speaking Part 2 cue cards", ieltsDashboard.UpcomingBooking.FocusArea);
        Assert.Equal("https://meet.example.com/ielts-david", ieltsDashboard.UpcomingBooking.MeetLink);
    }
}
