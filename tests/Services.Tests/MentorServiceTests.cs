using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class MentorServiceTests(DatabaseFixture database)
{
    // 2026-10-15 is a Thursday (ISO DayOfWeek = 4).
    // 12:00:00 UTC is 19:00:00 +07:00 (Asia/Ho_Chi_Minh).
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    private MentorService CreateService(TimeProvider? clock = null) => new(
        DbContextFactory.Create(database.ConnectionString),
        clock ?? _clock);

    private async Task<(int CertId, int SkillId, string CertCode)> SeedCertAndSkillAsync()
    {
        var certCode = $"M{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cId INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cId, 'LIST', N'Listening', 'listening', 1),
              (@cId, 'READ', N'Reading', 'reading', 2);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.FirstAsync(c => c.Code == certCode);
        var skill = await db.Skills.FirstAsync(s => s.CertId == cert.CertId && s.Code == "LIST");
        return (cert.CertId, skill.SkillId, certCode);
    }

    private async Task<int> SeedLearnerWithCreditsAsync(int startingCredits = 0, string? activeCertCode = null, string fullName = "Learner Test")
    {
        var email = $"learner-{Guid.NewGuid():N}@demo.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, TimeZoneId, EmailVerifiedAt, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'{fullName}', 'Asia/Ho_Chi_Minh', SYSDATETIMEOFFSET(),
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        if (startingCredits > 0)
        {
            await database.SqlAsync($"""
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
                  INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@demo.local', 0x01, N'Admin', SYSDATETIMEOFFSET());
                DECLARE @adminId INT = SCOPE_IDENTITY();
                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
                VALUES ({userId}, 'admin_grant', {startingCredits}, @adminId, N'Test setup credits');
                """);
        }

        return userId;
    }

    [Fact]
    public async Task CreateMentorAsync_CreatesHumanMentor_WithMeetLink_PriceCredits_AndWeeklySlots()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();

        var weeklyInputs = new[]
        {
            new MentorWeeklySlotInputDto(2, "09:00", "10:00"),
            new MentorWeeklySlotInputDto(2, "14:00", "15:00"),
            new MentorWeeklySlotInputDto(5, "19:00", "20:00"),
        };

        var created = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Dr. Khoa {Guid.NewGuid():N}",
            "Experienced TOEIC mentor.",
            certId,
            skillId,
            "https://meet.google.com/kho-a111-222",
            2,
            "10 years teaching TOEIC",
            "Listening strategies",
            60,
            true,
            weeklyInputs));

        Assert.NotNull(created);
        Assert.Equal(certCode, created.CertCode);
        Assert.Equal("https://meet.google.com/kho-a111-222", created.MeetLink);
        Assert.Equal(2, created.PriceCredits);
        Assert.NotNull(created.WeeklySlots);
        Assert.Equal(3, created.WeeklySlots.Count);
        Assert.Equal(2, created.WeeklySlots[0].DayOfWeek);
        Assert.Equal("09:00", created.WeeklySlots[0].StartTime);
        Assert.Equal("10:00", created.WeeklySlots[0].EndTime);

        var adminList = await service.GetAdminMentorsAsync();
        var row = Assert.Single(adminList, m => m.MentorId == created.MentorId);
        Assert.Equal("https://meet.google.com/kho-a111-222", row.MeetLink);
        Assert.Equal(2, row.PriceCredits);
        Assert.Equal(40_000m, row.PriceVnd);
        Assert.Equal(3, row.WeeklySlots.Count);
    }

    [Fact]
    public async Task CreateMentorAsync_ValidatesInputAndThrowsValidationException()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();

        // Missing display name
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            "", "Bio", certId, skillId, "https://meet.google.com/abc-defg-hij", 1, null, null, 60, true)));

        // Missing or invalid MeetLink
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            "Name", "Bio", certId, skillId, "", 1, null, null, 60, true)));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            "Name", "Bio", certId, skillId, "not-a-valid-url", 1, null, null, 60, true)));

        // Non-existent certificate
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            "Name", "Bio", 999999, null, "https://meet.google.com/abc-defg-hij", 1, null, null, 60, true)));

        // Non-positive credits
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            "Name", "Bio", certId, skillId, "https://meet.google.com/abc-defg-hij", 0, null, null, 60, true)));

        // Non-positive session minutes
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            "Name", "Bio", certId, skillId, "https://meet.google.com/abc-defg-hij", 1, null, null, 0, true)));
    }

    [Fact]
    public async Task ReplaceMentorWeeklySlotsAsync_ValidatesDayOfWeek_Range_AndOverlaps()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Schedule Mentor {Guid.NewGuid():N}", "Bio", certId, skillId, "https://meet.google.com/sch-edul-001", 1, null, null, 60, true));

        // Invalid DayOfWeek (0 and 8)
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReplaceMentorWeeklySlotsAsync(mentor.MentorId, [new MentorWeeklySlotInputDto(0, "09:00", "10:00")]));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReplaceMentorWeeklySlotsAsync(mentor.MentorId, [new MentorWeeklySlotInputDto(8, "09:00", "10:00")]));

        // EndTime <= StartTime
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReplaceMentorWeeklySlotsAsync(mentor.MentorId, [new MentorWeeklySlotInputDto(1, "10:00", "10:00")]));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReplaceMentorWeeklySlotsAsync(mentor.MentorId, [new MentorWeeklySlotInputDto(1, "11:00", "10:00")]));

        // Overlapping windows on the same day
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReplaceMentorWeeklySlotsAsync(
                mentor.MentorId,
                [
                    new MentorWeeklySlotInputDto(3, "09:00", "10:30"),
                    new MentorWeeklySlotInputDto(3, "10:00", "11:00"),
                ]));
        Assert.Contains("overlap", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Adjacent windows on the same day (09:00-10:00 and 10:00-11:00) + another day -> valid!
        var saved = await service.ReplaceMentorWeeklySlotsAsync(
            mentor.MentorId,
            [
                new MentorWeeklySlotInputDto(3, "10:00", "11:00"),
                new MentorWeeklySlotInputDto(3, "09:00", "10:00"),
                new MentorWeeklySlotInputDto(6, "14:00", "15:00"),
            ]);

        Assert.Equal(3, saved.Count);
        Assert.Equal(3, saved[0].DayOfWeek);
        Assert.Equal("09:00", saved[0].StartTime);
        Assert.Equal("10:00", saved[0].EndTime);
        Assert.Equal("10:00", saved[1].StartTime);
        Assert.Equal("11:00", saved[1].EndTime);
    }

    [Fact]
    public async Task GetMentorDetailAsync_MaterializesNextWeeklyOccurrenceInUtc7_ShowsOpenAndBooked_AndClosesStaleOpenSlotsOnScheduleEdit()
    {
        // Current clock: 2026-10-15 12:00:00 UTC = Thursday 2026-10-15 19:00:00 +07:00 (DayOfWeek = 4)
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 10, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"AutoSlot Mentor {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "https://meet.google.com/aut-oslt-001",
            2,
            null,
            null,
            60,
            true,
            [
                // Window 1: Thursday (Day 4) 20:00-21:00 +07:00 -> later TODAY (2026-10-15 13:00-14:00 UTC)
                new MentorWeeklySlotInputDto(4, "20:00", "21:00"),
                // Window 2: Thursday (Day 4) 09:00-10:00 +07:00 -> already passed today, so NEXT Thursday (2026-10-22 02:00-03:00 UTC)
                new MentorWeeklySlotInputDto(4, "09:00", "10:00"),
                // Window 3: Friday (Day 5) 10:00-11:00 +07:00 -> tomorrow (2026-10-16 03:00-04:00 UTC)
                new MentorWeeklySlotInputDto(5, "10:00", "11:00"),
            ]));

        var detail1 = await service.GetMentorDetailAsync(mentor.MentorId);
        Assert.Equal(3, detail1.WeeklySlots.Count);
        Assert.Equal(3, detail1.AvailableSlots.Count);
        Assert.All(detail1.AvailableSlots, s => Assert.Equal("open", s.Status));

        // Ordered by StartAt ascending:
        // 1) 2026-10-15 13:00 UTC (Thu 20:00 +07:00)
        // 2) 2026-10-16 03:00 UTC (Fri 10:00 +07:00)
        // 3) 2026-10-22 02:00 UTC (Next Thu 09:00 +07:00)
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero), detail1.AvailableSlots[0].StartAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 3, 0, 0, TimeSpan.Zero), detail1.AvailableSlots[1].StartAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 2, 0, 0, TimeSpan.Zero), detail1.AvailableSlots[2].StartAt);

        // Learner books the Thursday 20:00 +07:00 slot
        var thuEveningSlotId = detail1.AvailableSlots[0].SlotId;
        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(thuEveningSlotId, "Focus"));
        Assert.Equal("confirmed", booking.Status);

        // While Thursday 20:00 +07:00 is still in the future, GetMentorDetailAsync returns it with Status == "booked"
        // so the UI can grey it out as taken!
        var detailAfterBook = await service.GetMentorDetailAsync(mentor.MentorId);
        Assert.Equal(3, detailAfterBook.AvailableSlots.Count);
        var bookedSlotDto = Assert.Single(detailAfterBook.AvailableSlots, s => s.SlotId == thuEveningSlotId);
        Assert.Equal("booked", bookedSlotDto.Status);

        // Now Admin edits the weekly schedule: keeps Thursday 20:00-21:00, removes Friday 10:00-11:00 and Thursday 09:00-10:00,
        // and adds Saturday (Day 6) 15:00-16:00 +07:00.
        // Unbooked future slots that no longer match any weekly window must be closed, while the booked slot is preserved!
        await service.ReplaceMentorWeeklySlotsAsync(
            mentor.MentorId,
            [
                new MentorWeeklySlotInputDto(4, "20:00", "21:00"),
                new MentorWeeklySlotInputDto(6, "15:00", "16:00"),
            ]);

        var detailAfterScheduleEdit = await service.GetMentorDetailAsync(mentor.MentorId);
        Assert.Equal(2, detailAfterScheduleEdit.AvailableSlots.Count);
        Assert.Contains(detailAfterScheduleEdit.AvailableSlots, s => s.SlotId == thuEveningSlotId && s.Status == "booked");
        Assert.Contains(detailAfterScheduleEdit.AvailableSlots, s => s.StartAt == new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero) && s.Status == "open");

        // Advance clock past Thursday 20:00 +07:00 (e.g., to Thursday 20:30 +07:00 = 13:30 UTC):
        // Now the next occurrence of Thursday 20:00-21:00 +07:00 is NEXT Thursday (2026-10-22 13:00 UTC) and is materialized as "open"!
        var laterClock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 15, 13, 30, 0, TimeSpan.Zero));
        var laterService = CreateService(laterClock);
        var detailNextWeek = await laterService.GetMentorDetailAsync(mentor.MentorId);
        Assert.Equal(2, detailNextWeek.AvailableSlots.Count);
        Assert.Contains(detailNextWeek.AvailableSlots, s => s.StartAt == new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero) && s.Status == "open");
        Assert.Contains(detailNextWeek.AvailableSlots, s => s.StartAt == new DateTimeOffset(2026, 10, 22, 13, 0, 0, TimeSpan.Zero) && s.Status == "open");
    }

    [Fact]
    public async Task GetMentorsAsync_FiltersByActiveStatus_Cert_And_Skill()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();

        var activeName = $"Active Mentor {Guid.NewGuid():N}";
        await service.CreateMentorAsync(new CreateMentorProfileRequest(
            activeName, "Bio", certId, skillId, "https://meet.google.com/act-ive1-001", 1, null, null, 60, true));

        var inactiveName = $"Inactive Mentor {Guid.NewGuid():N}";
        await service.CreateMentorAsync(new CreateMentorProfileRequest(
            inactiveName, "Bio", certId, skillId, "https://meet.google.com/ina-ctv1-002", 1, null, null, 60, false));

        var mentors = await service.GetMentorsAsync(certCode);
        Assert.Contains(mentors, m => m.DisplayName == activeName);
        Assert.DoesNotContain(mentors, m => m.DisplayName == inactiveName);

        var listeningMentors = await service.GetMentorsAsync(certCode, "listening");
        Assert.Contains(listeningMentors, m => m.DisplayName == activeName);

        var writingMentors = await service.GetMentorsAsync(certCode, "writing");
        Assert.DoesNotContain(writingMentors, m => m.DisplayName == activeName);
    }

    [Fact]
    public async Task UpdateMentorAsync_UpdatesMentorProperties_AndWeeklySlots()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Original Name {Guid.NewGuid():N}", "Old Bio", certId, skillId, "https://meet.google.com/old-link-001", 1, "Old Head", "Old Exp", 60, true));

        var updated = await service.UpdateMentorAsync(mentor.MentorId, new UpdateMentorProfileRequest(
            "Updated Name",
            "New Bio",
            certId,
            skillId,
            "https://meet.google.com/new-link-002",
            3,
            "New Head",
            "New Exp",
            90,
            false,
            [new MentorWeeklySlotInputDto(1, "08:00", "09:30")]));

        Assert.Equal("Updated Name", updated.DisplayName);
        Assert.Equal("New Bio", updated.Bio);
        Assert.Equal("https://meet.google.com/new-link-002", updated.MeetLink);
        Assert.Equal(3, updated.PriceCredits);
        Assert.Equal(90, updated.SessionMinutes);
        Assert.False(updated.IsActive);
        Assert.NotNull(updated.WeeklySlots);
        Assert.Single(updated.WeeklySlots);
    }

    [Fact]
    public async Task GetMentorsAsync_IncludesRatingFromView()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Rated Mentor {Guid.NewGuid():N}", "Bio", certId, skillId, "https://meet.google.com/rat-ed01-001", 1, null, null, 60, true));

        await database.SqlAsync($"""
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES ({mentor.MentorId}, DATEADD(day, -2, SYSDATETIMEOFFSET()), DATEADD(day, -2, DATEADD(hour, 1, SYSDATETIMEOFFSET())), 'open');
            DECLARE @slotId INT = SCOPE_IDENTITY();

            INSERT INTO Bookings (UserId, MentorId, SlotId, Status, MeetLink, PriceCredits, CreatedAt)
            VALUES ({learnerUserId}, {mentor.MentorId}, @slotId, 'done', N'https://meet.google.com/rat-ed01-001', 1, SYSDATETIMEOFFSET());
            DECLARE @bookingId INT = SCOPE_IDENTITY();

            INSERT INTO Reviews (BookingId, UserId, Rating, Text, CreatedAt)
            VALUES (@bookingId, {learnerUserId}, 5, N'Outstanding mentor!', SYSDATETIMEOFFSET());
            """);

        var mentors = await service.GetMentorsAsync(certCode);
        var matched = mentors.FirstOrDefault(m => m.MentorId == mentor.MentorId);
        Assert.NotNull(matched);
        Assert.Equal(5.0m, matched.AvgRating);
        Assert.Equal(1, matched.ReviewCount);
    }

    [Fact]
    public async Task GetCertificateOptionsAsync_ReturnsActiveCertificatesWithSkills()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();

        var options = await service.GetCertificateOptionsAsync();
        Assert.NotEmpty(options);

        var matched = options.FirstOrDefault(c => c.CertId == certId);
        Assert.NotNull(matched);
        Assert.Equal(certCode, matched.Code);
        Assert.Contains(matched.Skills, s => s.SkillId == skillId);
    }

    [Fact]
    public async Task BookMentorAsync_SnapshotsMeetLinkAndPriceCredits_SyncsSlot_AndDebitsLedger()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Dr. Khoa Book {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "https://meet.google.com/kho-abk1-999",
            2,
            "Headline",
            "Expertise",
            60,
            true,
            [new MentorWeeklySlotInputDto(5, "16:00", "17:00")]));

        var detail = await service.GetMentorDetailAsync(mentor.MentorId);
        var slot = Assert.Single(detail.AvailableSlots);

        var booking = await service.BookMentorAsync(
            mentor.MentorId,
            learnerUserId,
            new BookMentorRequest(slot.SlotId, "Listening Part 3 strategies"));

        Assert.Equal("confirmed", booking.Status);
        Assert.Equal("https://meet.google.com/kho-abk1-999", booking.MeetLink);
        Assert.Equal("Listening Part 3 strategies", booking.FocusArea);
        Assert.Equal(2, booking.PriceCredits);

        await using var db = DbContextFactory.Create(database.ConnectionString);

        var dbSlot = await db.MentorSlots.FirstAsync(s => s.SlotId == slot.SlotId);
        Assert.Equal("booked", dbSlot.Status);

        var dbBooking = await db.Bookings.FirstAsync(b => b.BookingId == booking.BookingId);
        Assert.Equal(2, dbBooking.PriceCredits);
        Assert.Equal("https://meet.google.com/kho-abk1-999", dbBooking.MeetLink);

        var txn = await db.CreditTransactions.FirstOrDefaultAsync(t => t.BookingId == booking.BookingId && t.Reason == "mentor_booking");
        Assert.NotNull(txn);
        Assert.Equal(-2, txn.Delta);

        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == learnerUserId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(3, balance);
    }

    [Fact]
    public async Task BookMentorAsync_InsufficientCredits_ThrowsAndRefusesWithoutDeduction()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 1, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Dr. Khoa Insuf {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "https://meet.google.com/ins-uf01-001",
            3,
            "Headline",
            "Expertise",
            60,
            true,
            [new MentorWeeklySlotInputDto(5, "16:00", "17:00")]));

        var detail = await service.GetMentorDetailAsync(mentor.MentorId);
        var slot = Assert.Single(detail.AvailableSlots);

        var ex = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId)));

        Assert.Equal("insufficient_credits", ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == learnerUserId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(1, balance);

        var dbSlot = await db.MentorSlots.FirstAsync(s => s.SlotId == slot.SlotId);
        Assert.Equal("open", dbSlot.Status);

        var hasBooking = await db.Bookings.AnyAsync(b => b.SlotId == slot.SlotId);
        Assert.False(hasBooking);
    }

    [Fact]
    public async Task BookMentorAsync_SlotAlreadyBooked_ThrowsSlotNotAvailableWithoutDeduction()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var learner1 = await SeedLearnerWithCreditsAsync(startingCredits: 10, activeCertCode: certCode);
        var learner2 = await SeedLearnerWithCreditsAsync(startingCredits: 10, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Dr. Khoa Double {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "https://meet.google.com/dbl-book-001",
            1,
            "Headline",
            "Expertise",
            60,
            true,
            [new MentorWeeklySlotInputDto(5, "16:00", "17:00")]));

        var detail = await service.GetMentorDetailAsync(mentor.MentorId);
        var slot = Assert.Single(detail.AvailableSlots);

        var booking1 = await service.BookMentorAsync(mentor.MentorId, learner1, new BookMentorRequest(slot.SlotId));
        Assert.NotNull(booking1);

        var ex = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.BookMentorAsync(mentor.MentorId, learner2, new BookMentorRequest(slot.SlotId)));

        Assert.Equal("slot_not_available", ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var balance2 = await db.vw_UserCreditBalances.Where(b => b.UserId == learner2).Select(b => b.Balance).SingleAsync();
        Assert.Equal(10, balance2);
    }

    [Fact]
    public async Task CancelBookingAsync_CancelsBooking_FreesSlot_AndRefundsSnapshottedCredits_EvenIfMentorPriceChanged()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 10, activeCertCode: certCode);

        // Clock is Thu 2026-10-15 12:00 UTC.
        // Schedule a Saturday (Day 6) 16:00-17:00 +07:00 slot -> 2026-10-17 09:00 UTC (> 24 hours away).
        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Dr. Khoa PriceChange {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "https://meet.google.com/prc-chng-001",
            2,
            "Headline",
            "Expertise",
            60,
            true,
            [new MentorWeeklySlotInputDto(6, "16:00", "17:00")]));

        var detail = await service.GetMentorDetailAsync(mentor.MentorId);
        var slot = Assert.Single(detail.AvailableSlots);

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));
        Assert.Equal("confirmed", booking.Status);
        Assert.Equal(2, booking.PriceCredits);

        // Admin subsequently changes mentor's PriceCredits to 5
        await service.UpdateMentorAsync(mentor.MentorId, new UpdateMentorProfileRequest(
            mentor.DisplayName,
            "Bio",
            certId,
            skillId,
            "https://meet.google.com/prc-chng-001",
            5,
            "Headline",
            "Expertise",
            60,
            true));

        // Cancel >= 24h before StartAt: refunds the booking's snapshotted 2 credits
        var cancelResult = await service.CancelBookingAsync(booking.BookingId, learnerUserId);
        Assert.Equal("cancelled", cancelResult.Status);
        Assert.Equal(2, cancelResult.RefundedCredits);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbSlot = await db.MentorSlots.FirstAsync(s => s.SlotId == slot.SlotId);
        Assert.Equal("open", dbSlot.Status);

        var refundTxn = await db.CreditTransactions.FirstOrDefaultAsync(t => t.BookingId == booking.BookingId && t.Reason == "booking_cancel");
        Assert.NotNull(refundTxn);
        Assert.Equal(2, refundTxn.Delta);

        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == learnerUserId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(10, balance);

        // Second cancellation attempt throws booking_already_refunded
        var ex = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.CancelBookingAsync(booking.BookingId, learnerUserId));
        Assert.Equal("booking_already_refunded", ex.Code);
    }

    [Fact]
    public async Task CancelBookingAsync_WhenWeeklyScheduleWasCleared_ClosesFreedSlotInsteadOfLeavingItOpen()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        // Clock is Thu 2026-10-15 12:00 UTC. Sat (Day 6) 16:00-17:00 +07:00 is > 24h away.
        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Dr. ClearedSchedule {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "https://meet.google.com/clr-schd-001",
            2,
            "Headline",
            "Expertise",
            60,
            true,
            [new MentorWeeklySlotInputDto(6, "16:00", "17:00")]));

        var detailBefore = await service.GetMentorDetailAsync(mentor.MentorId);
        var slot = Assert.Single(detailBefore.AvailableSlots);

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));

        // Admin clears mentor's weekly schedule to 0 windows; existing booked slot is preserved
        await service.ReplaceMentorWeeklySlotsAsync(mentor.MentorId, []);

        await using (var dbCheck = DbContextFactory.Create(database.ConnectionString))
        {
            var preserved = await dbCheck.MentorSlots.AsNoTracking().FirstAsync(s => s.SlotId == slot.SlotId);
            Assert.Equal("booked", preserved.Status);
        }

        // Learner cancels the booking; freed slot has no matching weekly window so it must be closed, not left open
        var cancelResult = await service.CancelBookingAsync(booking.BookingId, learnerUserId);
        Assert.Equal("cancelled", cancelResult.Status);

        await using var dbAfter = DbContextFactory.Create(database.ConnectionString);
        var dbSlotAfter = await dbAfter.MentorSlots.AsNoTracking().FirstAsync(s => s.SlotId == slot.SlotId);
        Assert.Equal("closed", dbSlotAfter.Status);

        var detailAfter = await service.GetMentorDetailAsync(mentor.MentorId);
        Assert.Empty(detailAfter.AvailableSlots);
    }

    [Fact]
    public async Task CancelBookingAsync_Within24HoursOrPassed_ThrowsBookingCannotCancel()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        // Clock is Thu 2026-10-15 12:00 UTC (19:00 +07:00).
        // Friday (Day 5) 10:00 +07:00 is 2026-10-16 03:00 UTC -> only 15 hours away (< 24h)!
        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Dr. Khoa 24h {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "https://meet.google.com/lim-24hr-001",
            2,
            "Headline",
            "Expertise",
            60,
            true,
            [new MentorWeeklySlotInputDto(5, "10:00", "11:00")]));

        var detail = await service.GetMentorDetailAsync(mentor.MentorId);
        var slot = Assert.Single(detail.AvailableSlots);

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));

        var exWithin24h = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.CancelBookingAsync(booking.BookingId, learnerUserId));
        Assert.Equal("booking_cannot_cancel", exWithin24h.Code);

        // Advance time to AFTER the slot start
        var advancedClock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 16, 3, 30, 0, TimeSpan.Zero));
        var servicePast = CreateService(advancedClock);

        var exPast = await Assert.ThrowsAsync<MentorBookingException>(() =>
            servicePast.CancelBookingAsync(booking.BookingId, learnerUserId));
        Assert.Equal("booking_cannot_cancel", exPast.Code);
    }

    [Fact]
    public async Task GetAdminMentorPayoutsAsync_And_GetMentorPayoutExportRowsAsync_ComputeVndAt20000PerCredit_AndExcludeResolvedNoShowComplaints()
    {
        // Current clock: 2026-11-14 12:00:00 UTC (19:00 +07:00)
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(clock);
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 20, activeCertCode: certCode, fullName: "Nguyễn Thị Lan");

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Dr. Trần Minh Khoa {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "https://meet.google.com/pay-out1-001",
            2,
            "Headline",
            "Expertise",
            60,
            true));

        // Seed 4 bookings:
        // 1) Completed session in window (2026-11-10 09:00-10:00 +07:00 = 02:00-03:00 UTC), PriceCredits = 2 -> INCLUDED (40,000 VND)
        // 2) Completed session in window (2026-11-11 14:00-15:00 +07:00 = 07:00-08:00 UTC), PriceCredits = 3, with a RESOLVED mentor_no_show complaint (ResolvedByGrantTxnId IS NOT NULL) -> EXCLUDED
        // 3) Cancelled session in window -> EXCLUDED
        // 4) Future session (2026-11-15) -> EXCLUDED
        await database.SqlAsync($"""
            DECLARE @adminId INT = (SELECT TOP 1 UserId FROM Users WHERE RoleId = (SELECT RoleId FROM Roles WHERE Code = 'admin'));

            -- Slot & Booking 1: Valid completed session (2 credits = 40,000 VND)
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES ({mentor.MentorId}, '2026-11-10T02:00:00Z', '2026-11-10T03:00:00Z', 'booked');
            DECLARE @s1 INT = SCOPE_IDENTITY();
            INSERT INTO Bookings (UserId, MentorId, SlotId, Status, MeetLink, PriceCredits, CreatedAt)
            VALUES ({learnerUserId}, {mentor.MentorId}, @s1, 'confirmed', N'https://meet.google.com/pay-out1-001', 2, '2026-11-09T02:00:00Z');

            -- Slot & Booking 2: Completed session with resolved mentor_no_show complaint
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES ({mentor.MentorId}, '2026-11-11T07:00:00Z', '2026-11-11T08:00:00Z', 'booked');
            DECLARE @s2 INT = SCOPE_IDENTITY();
            INSERT INTO Bookings (UserId, MentorId, SlotId, Status, MeetLink, PriceCredits, CreatedAt)
            VALUES ({learnerUserId}, {mentor.MentorId}, @s2, 'confirmed', N'https://meet.google.com/pay-out1-001', 3, '2026-11-10T02:00:00Z');
            DECLARE @b2 INT = SCOPE_IDENTITY();

            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES ({learnerUserId}, 'admin_grant', 3, @adminId, N'No-show compensation');
            DECLARE @grantTxnId INT = SCOPE_IDENTITY();

            INSERT INTO Complaints (UserId, Type, Status, Reason, BookingId, ResolvedByUserId, Resolution, ResolvedByGrantTxnId, CreatedAt, ResolvedAt)
            VALUES ({learnerUserId}, 'mentor_no_show', 'resolved', N'Mentor did not join', @b2, @adminId, N'Refunded credits', @grantTxnId, '2026-11-11T08:10:00Z', '2026-11-11T09:00:00Z');

            -- Slot & Booking 3: Cancelled session in window
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES ({mentor.MentorId}, '2026-11-12T02:00:00Z', '2026-11-12T03:00:00Z', 'open');
            DECLARE @s3 INT = SCOPE_IDENTITY();
            INSERT INTO Bookings (UserId, MentorId, SlotId, Status, MeetLink, PriceCredits, CancelledAt, CreatedAt)
            VALUES ({learnerUserId}, {mentor.MentorId}, @s3, 'cancelled', N'https://meet.google.com/pay-out1-001', 2, '2026-11-11T01:00:00Z', '2026-11-10T01:00:00Z');

            -- Slot & Booking 4: Future session
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES ({mentor.MentorId}, '2026-11-15T02:00:00Z', '2026-11-15T03:00:00Z', 'booked');
            DECLARE @s4 INT = SCOPE_IDENTITY();
            INSERT INTO Bookings (UserId, MentorId, SlotId, Status, MeetLink, PriceCredits, CreatedAt)
            VALUES ({learnerUserId}, {mentor.MentorId}, @s4, 'confirmed', N'https://meet.google.com/pay-out1-001', 2, '2026-11-13T01:00:00Z');
            """);

        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 14);

        var report = await service.GetAdminMentorPayoutsAsync(from, to);
        Assert.Equal(20_000m, report.VndPerCredit);

        var mentorSummary = Assert.Single(report.Mentors, m => m.MentorId == mentor.MentorId);
        Assert.Equal(1, mentorSummary.CompletedSessionsCount);
        Assert.Equal(2, mentorSummary.TotalCredits);
        Assert.Equal(40_000m, mentorSummary.TotalAmountVnd);

        var sessionItem = Assert.Single(report.Sessions, s => s.MentorId == mentor.MentorId);
        Assert.Equal(new DateOnly(2026, 11, 10), sessionItem.SessionDate);
        Assert.Equal(2, sessionItem.PriceCredits);
        Assert.Equal(40_000m, sessionItem.AmountVnd);
        Assert.Equal("Nguyễn Thị Lan", sessionItem.LearnerName);

        var exportRows = await service.GetMentorPayoutExportRowsAsync(from, to);
        var exportRow = Assert.Single(exportRows, r => r.MentorId == mentor.MentorId);
        Assert.Equal(new DateOnly(2026, 11, 10), exportRow.SessionDate);
        Assert.Equal(new TimeOnly(9, 0), exportRow.StartTime);
        Assert.Equal(new TimeOnly(10, 0), exportRow.EndTime);
        Assert.Equal(2, exportRow.PriceCredits);
        Assert.Equal(40_000m, exportRow.AmountVnd);
    }

    [Fact]
    public async Task RateBookingAsync_FinishedBooking_CreatesReview_AndValidatesRules()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var learner1 = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);
        var learner2 = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            $"Dr. Rate {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "https://meet.google.com/rat-ebk1-001",
            1,
            "Headline",
            "Expertise",
            60,
            true,
            [new MentorWeeklySlotInputDto(6, "09:00", "10:00")]));

        var detail = await service.GetMentorDetailAsync(mentor.MentorId);
        var slot = Assert.Single(detail.AvailableSlots);

        var booking = await service.BookMentorAsync(mentor.MentorId, learner1, new BookMentorRequest(slot.SlotId));

        // Unfinished booking cannot be rated
        var exUnfinished = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.RateBookingAsync(booking.BookingId, learner1, new RateMentorRequest(5, "Premature")));
        Assert.Equal(MentorBookingException.BookingNotFinished, exUnfinished.Code);

        // Mark booking as done
        await database.SqlAsync($"UPDATE Bookings SET Status = 'done' WHERE BookingId = {booking.BookingId};");

        // Invalid rating throws ValidationException
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RateBookingAsync(booking.BookingId, learner1, new RateMentorRequest(0, "Bad")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RateBookingAsync(booking.BookingId, learner1, new RateMentorRequest(6, "Bad")));

        // Other user throws KeyNotFoundException
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RateBookingAsync(booking.BookingId, learner2, new RateMentorRequest(5, "Other")));

        // Valid rating succeeds
        var review = await service.RateBookingAsync(booking.BookingId, learner1, new RateMentorRequest(5, "Excellent session, very clear feedback!"));
        Assert.Equal(5, review.Rating);
        Assert.Equal("Excellent session, very clear feedback!", review.Text);

        // Duplicate rating throws BookingAlreadyRated
        var exDup = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.RateBookingAsync(booking.BookingId, learner1, new RateMentorRequest(4, "Second")));
        Assert.Equal(MentorBookingException.BookingAlreadyRated, exDup.Code);

        // Booking detail and learner bookings list include the review
        var bookingDetail = await service.GetBookingDetailAsync(booking.BookingId, learner1);
        Assert.NotNull(bookingDetail.Review);
        Assert.Equal(5, bookingDetail.Review.Rating);

        var bookingsList = await service.GetLearnerBookingsAsync(learner1);
        var listed = Assert.Single(bookingsList, b => b.BookingId == booking.BookingId);
        Assert.NotNull(listed.Review);
        Assert.Equal(5, listed.Review.Rating);
    }
}
