using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;

namespace Services.Tests;

/// <summary>
/// #223 / D21: what a mentor receives about the learner, and the three absences it must keep apart.
/// <para>
/// <b>Why these tests use a database of their own.</b> Each seeds a certificate, skills, bands, a
/// learner, a mentor and a booking. Seeding the run-wide catalog has leaked into other classes three
/// times in this rebuild, so every case here creates a fresh database and drops it afterwards.
/// </para>
/// <para>
/// <b>The band-vs-level test cannot be a string comparison.</b> A TOEIC band code is "B2" and a CEFR
/// level is also "B2", so "the level reads B2" passes an implementation that copied the band straight
/// through. The fixture therefore puts TWO skills at B2 with different positions (12 and 84) on a
/// certificate whose band table contains "B2": a band cannot produce two positions, and it cannot
/// report C2 on a certificate that has no C2 band at all.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MentorStandingPayloadTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EE223_{Guid.NewGuid():N}"[..24];
    private string _connectionString = null!;
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

    public MentorStandingPayloadTests(DatabaseFixture database) => _database = database;

    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    private MentorService CreateService() => new(Db(), _clock);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private Task SqlAsync(string batch)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ee223-{Guid.NewGuid():N}.sql");
        File.WriteAllText(path, batch);
        return _database.SqlCmdFileToDatabaseAsync(_dbName, path);
    }

    /// <summary>
    /// A certificate with the named skills, a band table whose codes are the certificate's own, and a
    /// learner. Bands are seeded because a real certificate has them and because the trap needs one
    /// whose code reads like a level.
    /// </summary>
    private async Task<Fixture> SeedAsync(
        string certCode, (string Code, string Modality)[] skills, string[] bandCodes)
    {
        var skillRows = string.Join(",", skills.Select((s, i) =>
            $"(@c, '{s.Code}', N'{s.Code}', '{s.Modality}', {i + 1})"));
        var bandRows = string.Join(",", bandCodes.Select((code, i) =>
            $"(@c, '{code}', N'{code}', {(i * 100) + 1}, {(i + 1) * 100}, {i + 1})"));
        var skillInsert = skills.Length == 0
            ? ""
            : $"INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES {skillRows};";

        await SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @c INT = SCOPE_IDENTITY();
            {skillInsert}
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES {bandRows};
            INSERT INTO Users (RoleId, Email, PasswordHash, ActiveCertId)
              VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), CONCAT(NEWID(), '@test.local'), 0x01, @c);
            DECLARE @learner INT = SCOPE_IDENTITY();
            -- A booking costs credits; the learner needs a balance before the booking can be made.
            INSERT INTO Users (RoleId, Email, PasswordHash)
              VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), CONCAT(NEWID(), '@test.local'), 0x01);
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
              VALUES (@learner, 'admin_grant', 10, SCOPE_IDENTITY(), N'Mentor standing fixture');
            SELECT @c AS CertId, @learner AS UserId;
            """);

        await using var db = Db();
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var users = await db.Users.Where(u => u.ActiveCertId == cert.CertId).Select(u => u.UserId).ToListAsync();
        return new Fixture(
            cert.CertId,
            certCode,
            users.Max(),
            await db.Skills.Where(s => s.CertId == cert.CertId)
                .OrderBy(s => s.DisplayOrder).ThenBy(s => s.SkillId).ToListAsync());
    }

    private sealed record Fixture(int CertId, string CertCode, int LearnerId, IReadOnlyList<BusinessObjects.Entities.Skill> Skills);

    private SkillStateWriter Writer() => new(Db(), _clock);

    private async Task RecordAsync(int userId, int certId, int skillId, CefrLevel level, int position)
    {
        await Writer().RecordAsync(userId, certId, skillId, level, position,
            // The coordinate must agree with the level inside 1.00-6.99; the exact value is not what
            // this ticket asserts, only that the payload reports the stored level and position.
            level.Rank + 0.5m, LearnerSkillStateSource.Placement);
    }

    /// <summary>
    /// A human mentor on the certificate plus an open slot, and a learner with credits. The booking
    /// itself is made by whatever the test is about: the service for a new one, SQL for an old one.
    /// </summary>
    private async Task<(int MentorId, int SlotId)> SeedMentorAndSlotAsync(int certId, int learnerId)
    {
        await SqlAsync($"""
            INSERT INTO MentorProfiles (CertId, DisplayName, MeetLink, PriceCredits, SessionMinutes, IsActive)
              VALUES ({certId}, N'Dr. Minh Khoa', N'https://meet.google.com/abc-defg-hij', 1, 60, 1);
            DECLARE @m INT = SCOPE_IDENTITY();
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
              VALUES (@m, DATEADD(day, 2, SYSDATETIMEOFFSET()), DATEADD(day, 2, DATEADD(hour, 1, SYSDATETIMEOFFSET())), 'open');
            SELECT @m AS MentorId, SCOPE_IDENTITY() AS SlotId;
            """);

        await using var db = Db();
        var mentor = await db.MentorProfiles.Where(m => m.CertId == certId)
            .OrderByDescending(m => m.MentorId).FirstAsync();
        var slot = await db.MentorSlots.Where(s => s.MentorId == mentor.MentorId)
            .OrderByDescending(s => s.SlotId).FirstAsync();
        return (mentor.MentorId, slot.SlotId);
    }

    // =============================================================================================
    // AC1 + AC4: per-skill level and position, overall and limiting skill, and no band as a level.
    // =============================================================================================

    /// <summary>
    /// Two skills at the SAME level with DIFFERENT positions, on a certificate whose band table has a
    /// band called "B2".
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> reporting the certificate's band as the
    /// level, which on this fixture reads exactly "B2" and agrees with the stored level by coincidence.
    /// The two positions are what separate them: a band has no position inside it, so a payload that
    /// copied the band can carry one value for both skills and at most one position.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_booking_carries_two_positions_at_one_level_and_the_overall_with_its_limiting_skill()
    {
        var code = Unique("MT223");
        var fixture = await SeedAsync(code,
            [("LIST", "listening"), ("READ", "reading")],
            ["A1", "A2", "B1", "B2", "C1"]);
        var (mentorId, slotId) = await SeedMentorAndSlotAsync(fixture.CertId, fixture.LearnerId);

        // Two skills at B2, four points apart in the band, and both on the certificate's "B2" band.
        await RecordAsync(fixture.LearnerId, fixture.CertId, fixture.Skills[0].SkillId, CefrLevel.B2, 12);
        await RecordAsync(fixture.LearnerId, fixture.CertId, fixture.Skills[1].SkillId, CefrLevel.B2, 84);

        var booking = await CreateService().BookMentorAsync(
            mentorId, fixture.LearnerId, new BookMentorRequest(slotId));

        var standing = Assert.IsType<MentorLearnerStandingDto>(booking.Standing);
        Assert.False(standing.HasNoMeasurement);
        Assert.Equal("B2", standing.OverallLevelCode);

        var rows = standing.Skills.Where(r => r.Status == MentorSkillStandingStatus.Measured).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("B2", r.LevelCode));
        Assert.Equal([12, 84], rows.Select(r => r.PositionInBand).ToList());

        // Both skills sit at B2, so the tie is broken by position: 12/100 is one answer from dropping
        // to B1, 84/100 is not. Naming the 84 skill would point the mentor at the wrong modality.
        Assert.Equal(fixture.Skills[0].SkillId, standing.LimitingSkillId);
        // LimitingSkillCode is the SKILL's code, never the level: the two are different quantities.
        Assert.Equal("LIST", standing.LimitingSkillCode);
    }

    /// <summary>
    /// A C2 skill on a certificate whose band table stops at C1.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> deriving the level from "the band whose
    /// range contains this result". TOEIC cannot award C2, so such an implementation reports C1 - or
    /// nothing - for a learner who really is C2. Asserting "not C1" is what catches the cap; asserting
    /// "the level is a level" would not.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_skill_above_the_certificates_highest_band_still_reports_its_measured_level()
    {
        var code = Unique("MT223");
        var fixture = await SeedAsync(code,
            [("LIST", "listening")],
            ["A1", "A2", "B1", "B2", "C1"]);
        var (mentorId, slotId) = await SeedMentorAndSlotAsync(fixture.CertId, fixture.LearnerId);
        await RecordAsync(fixture.LearnerId, fixture.CertId, fixture.Skills[0].SkillId, CefrLevel.C2, 40);

        var booking = await CreateService().BookMentorAsync(
            mentorId, fixture.LearnerId, new BookMentorRequest(slotId));

        var row = Assert.Single(booking.Standing!.Skills, r => r.Status == MentorSkillStandingStatus.Measured);
        Assert.Equal("C2", row.LevelCode);
        Assert.NotEqual("C1", row.LevelCode);
        Assert.Equal(40, row.PositionInBand);
        Assert.Equal("C2", booking.Standing.OverallLevelCode);
    }

    // =============================================================================================
    // AC2: not examined is stated, and it is not the same statement as not measured.
    // =============================================================================================

    /// <summary>
    /// A two-skill certificate and a learner with nothing measured: speaking and writing are NOT
    /// EXAMINED (no skill row exists for them), while the certificate's own two skills are NOT
    /// MEASURED (they exist and nothing filled them yet).
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> omitting the modalities the certificate
    /// does not examine. The list would still be non-empty, the levels would still be absent, and no
    /// screen would crash - so the assertion is on the exact four rows and on the two enums, not on
    /// "there is at least one not-measured row".
    /// </para>
    /// <para>
    /// <b>The second input that passes a wrong implementation:</b> representing both absences as a null
    /// level, so the page renders the same glyph for "we have not tested you yet" and "this paper never
    /// tests that". The enum is the assertion, and <c>MentorStandingViewModel</c>'s own test holds the
    /// two labels apart.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_modality_the_certificate_does_not_examine_is_stated_rather_than_omitted_or_low()
    {
        var code = Unique("MT223");
        var fixture = await SeedAsync(code,
            [("LIST", "listening"), ("READ", "reading")],
            ["A1", "B1", "B2"]);
        var (mentorId, slotId) = await SeedMentorAndSlotAsync(fixture.CertId, fixture.LearnerId);

        var booking = await CreateService().BookMentorAsync(
            mentorId, fixture.LearnerId, new BookMentorRequest(slotId));

        var standing = Assert.IsType<MentorLearnerStandingDto>(booking.Standing);
        Assert.True(standing.HasNoMeasurement);
        Assert.Null(standing.OverallLevelCode);
        Assert.Null(standing.LimitingSkillId);

        Assert.Equal(4, standing.Skills.Count);

        var notMeasured = standing.Skills.Where(r => r.Status == MentorSkillStandingStatus.NotMeasured).ToList();
        Assert.Equal(["listening", "reading"], notMeasured.Select(r => r.Modality).Order().ToList());
        Assert.All(notMeasured, r =>
        {
            Assert.NotNull(r.SkillId);
            Assert.NotNull(r.SkillCode);
            Assert.Null(r.LevelCode);
        });

        var notExamined = standing.Skills.Where(r => r.Status == MentorSkillStandingStatus.NotExamined).ToList();
        Assert.Equal(["speaking", "writing"], notExamined.Select(r => r.Modality).Order().ToList());
        Assert.All(notExamined, r =>
        {
            // No invented skill, and never a low level: both would make the table look complete.
            Assert.Null(r.SkillId);
            Assert.Null(r.SkillCode);
            Assert.Null(r.SkillName);
            Assert.Null(r.LevelCode);
            Assert.Null(r.PositionInBand);
        });
    }

    /// <summary>
    /// One skill measured on a three-skill certificate: the overall exists, the limiting skill is the
    /// one measured skill, and the other two examined skills are still absent from the vector.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> counting an unmeasured skill as A1. On this
    /// fixture the overall would be A1 and the limiting skill would be the invented one, and both
    /// figures look plausible on screen. The assertion is the measured level and the measured id.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_unmeasured_skill_is_not_counted_as_a_low_level_and_the_overall_names_the_measured_one()
    {
        var code = Unique("MT223");
        var fixture = await SeedAsync(code,
            [("LIST", "listening"), ("READ", "reading"), ("SPEA", "speaking")],
            ["A1", "B1", "B2"]);
        var (mentorId, slotId) = await SeedMentorAndSlotAsync(fixture.CertId, fixture.LearnerId);
        await RecordAsync(fixture.LearnerId, fixture.CertId, fixture.Skills[0].SkillId, CefrLevel.B1, 55);

        var booking = await CreateService().BookMentorAsync(
            mentorId, fixture.LearnerId, new BookMentorRequest(slotId));

        var standing = Assert.IsType<MentorLearnerStandingDto>(booking.Standing);
        Assert.False(standing.HasNoMeasurement);
        Assert.Equal("B1", standing.OverallLevelCode);
        Assert.Equal(fixture.Skills[0].SkillId, standing.LimitingSkillId);
        Assert.Equal("LIST", standing.LimitingSkillCode);

        Assert.Equal(2, standing.Skills.Count(r => r.Status == MentorSkillStandingStatus.NotMeasured));
        Assert.Single(standing.Skills, r => r.Status == MentorSkillStandingStatus.NotExamined
            && r.Modality == "writing");
    }

    /// <summary>
    /// A certificate with no skills at all: no standing, not four "not examined" rows. Claiming this
    /// certificate examines nothing is a statement about a content defect that the product cannot make
    /// on the learner's behalf.
    /// </summary>
    [Fact]
    public async Task A_certificate_with_no_skills_gets_no_standing_rather_than_four_invented_rows()
    {
        var code = Unique("MT223");
        var fixture = await SeedAsync(code, [], ["A1"]);
        var (mentorId, slotId) = await SeedMentorAndSlotAsync(fixture.CertId, fixture.LearnerId);

        var booking = await CreateService().BookMentorAsync(
            mentorId, fixture.LearnerId, new BookMentorRequest(slotId));

        Assert.Null(booking.Standing);
    }

    // =============================================================================================
    // AC3: a booking written before this payload existed still renders with it.
    // =============================================================================================

    /// <summary>
    /// The booking row is inserted by SQL, exactly as the rows that already exist were written, and
    /// then read back through both read paths.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> storing the standing on the booking. A new
    /// booking made by this code would carry one and every test above would pass; an old row would carry
    /// nothing and the page would go blank. This is why the row here is not made by
    /// <c>BookMentorAsync</c>: the payload is derived at read time, so there is nothing to migrate.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_booking_row_written_before_the_payload_still_reads_with_a_standing()
    {
        var code = Unique("MT223");
        var fixture = await SeedAsync(code,
            [("LIST", "listening"), ("READ", "reading")],
            ["A1", "B1", "B2"]);
        var (mentorId, slotId) = await SeedMentorAndSlotAsync(fixture.CertId, fixture.LearnerId);
        await RecordAsync(fixture.LearnerId, fixture.CertId, fixture.Skills[0].SkillId, CefrLevel.B1, 30);

        await SqlAsync($"""
            INSERT INTO Bookings (UserId, MentorId, SlotId, PriceCredits, Status, MeetLink, CreatedAt)
              VALUES ({fixture.LearnerId}, {mentorId}, {slotId}, 1, 'confirmed', N'https://meet.google.com/aaa-bbbb-ccc', SYSDATETIMEOFFSET());
            """);

        await using var db = Db();
        var bookingId = await db.Bookings.Where(b => b.UserId == fixture.LearnerId)
            .OrderByDescending(b => b.BookingId).Select(b => b.BookingId).FirstAsync();

        var service = CreateService();
        var detail = await service.GetBookingDetailAsync(bookingId, fixture.LearnerId);
        Assert.Equal(bookingId, detail.BookingId);
        Assert.Equal(fixture.CertId, detail.Standing!.CertId);
        Assert.Equal("B1", detail.Standing.OverallLevelCode);
        Assert.Single(detail.Standing.Skills, r => r.Status == MentorSkillStandingStatus.Measured);

        var list = await service.GetLearnerBookingsAsync(fixture.LearnerId);
        var listed = Assert.Single(list);
        Assert.NotNull(listed.Standing);
        Assert.Equal("B1", listed.Standing.OverallLevelCode);
    }
}
