using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class MentorServiceTests(DatabaseFixture database)
{
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    private MentorService CreateService() => new(
        DbContextFactory.Create(database.ConnectionString),
        _clock);

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

    private async Task<int> SeedUserAsync(string emailPrefix = "mentor-user", string timeZoneId = "Asia/Ho_Chi_Minh")
    {
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@demo.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, TimeZoneId, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Dr. Minh Khoa', '{timeZoneId}', SYSDATETIMEOFFSET());
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    [Fact]
    public async Task CreateMentorAsync_CreatesHumanAndAiMentors_WithCorrectConstraints()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var userId = await SeedUserAsync("human-mentor");

        // 1. Create human mentor
        var humanRequest = new CreateMentorProfileRequest(
            userId,
            $"Dr. Khoa {Guid.NewGuid():N}",
            "Experienced TOEIC mentor.",
            certId,
            skillId,
            "human",
            250000m,
            1,
            "10 years teaching TOEIC",
            "Listening strategies",
            60,
            true);

        var human = await service.CreateMentorAsync(humanRequest);
        Assert.NotNull(human);
        Assert.Equal("human", human.Type);
        Assert.Equal(userId, human.UserId);
        Assert.Equal(certCode, human.CertCode);
        Assert.Equal(250000m, human.PricePerSlot);
        Assert.Equal(1, human.PriceCredits);

        // 2. Create AI mentor (UserId must be null)
        var aiRequest = new CreateMentorProfileRequest(
            null,
            $"AI Coach {Guid.NewGuid():N}",
            "AI speaking practice 24/7.",
            certId,
            null,
            "ai",
            0m,
            1,
            "24/7 AI Speaking Coach",
            "Instant feedback",
            45,
            true);

        var ai = await service.CreateMentorAsync(aiRequest);
        Assert.NotNull(ai);
        Assert.Equal("ai", ai.Type);
        Assert.Null(ai.UserId);
        Assert.Equal(45, ai.SessionMinutes);
    }

    [Fact]
    public async Task CreateMentorAsync_ValidatesInputAndThrowsValidationException()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var userId = await SeedUserAsync("invalid-val");

        // Missing display name
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            userId, "", "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, true)));

        // Invalid type
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            userId, "Name", "Bio", certId, skillId, "robot", 200000m, 1, null, null, 60, true)));

        // Human mentor without userId
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            null, "Name", "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, true)));

        // AI mentor with userId
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            userId, "Name", "Bio", certId, skillId, "ai", 0m, 1, null, null, 60, true)));

        // Non-existent certificate
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            userId, "Name", "Bio", 999999, null, "human", 200000m, 1, null, null, 60, true)));

        // Negative price
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            userId, "Name", "Bio", certId, skillId, "human", -10m, 1, null, null, 60, true)));

        // Non-positive credits
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateMentorAsync(new CreateMentorProfileRequest(
            userId, "Name", "Bio", certId, skillId, "human", 200000m, 0, null, null, 60, true)));
    }

    [Fact]
    public async Task GetMentorsAsync_FiltersByActiveStatus_Cert_And_Skill()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var userId1 = await SeedUserAsync("mentor-active");
        var userId2 = await SeedUserAsync("mentor-inactive");

        var activeName = $"Active Mentor {Guid.NewGuid():N}";
        await service.CreateMentorAsync(new CreateMentorProfileRequest(
            userId1, activeName, "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, true));

        var inactiveName = $"Inactive Mentor {Guid.NewGuid():N}";
        await service.CreateMentorAsync(new CreateMentorProfileRequest(
            userId2, inactiveName, "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, false));

        // 1. Browsing only active mentors
        var mentors = await service.GetMentorsAsync(certCode);
        Assert.Contains(mentors, m => m.DisplayName == activeName);
        Assert.DoesNotContain(mentors, m => m.DisplayName == inactiveName);

        // 2. Filter by skill
        var listeningMentors = await service.GetMentorsAsync(certCode, "listening");
        Assert.Contains(listeningMentors, m => m.DisplayName == activeName);

        var writingMentors = await service.GetMentorsAsync(certCode, "writing");
        Assert.DoesNotContain(writingMentors, m => m.DisplayName == activeName);
    }

    [Fact]
    public async Task CreateMentorSlotAsync_ConvertsFromAdminTimeZoneToUtc()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("mentor-slot");
        var adminUserId = await SeedUserAsync("admin-tz", timeZoneId: "Asia/Ho_Chi_Minh");

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId, $"Slot Test Mentor {Guid.NewGuid():N}", "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, true));

        // Admin enters 2026-10-25 09:00:00 in Asia/Ho_Chi_Minh (+07:00)
        var localStart = new DateTime(2026, 10, 25, 9, 0, 0);
        var slot = await service.CreateMentorSlotAsync(mentor.MentorId, adminUserId, new CreateMentorSlotRequest(localStart, null));

        Assert.NotNull(slot);
        Assert.Equal("open", slot.Status);

        // In UTC (+00:00), 09:00 in +07:00 is 02:00 UTC!
        var expectedStartUtc = new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero);
        var expectedEndUtc = expectedStartUtc.AddMinutes(60);

        Assert.Equal(expectedStartUtc, slot.StartAt);
        Assert.Equal(expectedEndUtc, slot.EndAt);
    }

    [Fact]
    public async Task GetMentorDetailAsync_ReturnsOnlyOpenFutureSlots()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("mentor-detail");
        var adminUserId = await SeedUserAsync("admin-detail", timeZoneId: "UTC");

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId, $"Detail Test Mentor {Guid.NewGuid():N}", "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, true));

        // Create 1 past slot, 1 future open slot, and 1 closed future slot
        // Current clock: 2026-10-15 12:00:00 UTC
        var pastStart = new DateTime(2026, 10, 14, 10, 0, 0);
        var futureOpenStart = new DateTime(2026, 10, 16, 10, 0, 0);
        var futureClosedStart = new DateTime(2026, 10, 17, 10, 0, 0);

        var pastSlot = await service.CreateMentorSlotAsync(mentor.MentorId, adminUserId, new CreateMentorSlotRequest(pastStart, null, "UTC"));
        var openSlot = await service.CreateMentorSlotAsync(mentor.MentorId, adminUserId, new CreateMentorSlotRequest(futureOpenStart, null, "UTC"));
        var closedSlot = await service.CreateMentorSlotAsync(mentor.MentorId, adminUserId, new CreateMentorSlotRequest(futureClosedStart, null, "UTC"));

        // Close the third slot
        await service.CloseMentorSlotAsync(mentor.MentorId, closedSlot.SlotId);

        // Fetch detail
        var detail = await service.GetMentorDetailAsync(mentor.MentorId);
        Assert.NotNull(detail);
        Assert.Equal(mentor.DisplayName, detail.Mentor.DisplayName);

        // Only open future slot should be returned to learner!
        Assert.Single(detail.AvailableSlots);
        Assert.Equal(openSlot.SlotId, detail.AvailableSlots[0].SlotId);
        Assert.DoesNotContain(detail.AvailableSlots, s => s.SlotId == pastSlot.SlotId);
        Assert.DoesNotContain(detail.AvailableSlots, s => s.SlotId == closedSlot.SlotId);
    }

    [Fact]
    public async Task UpdateMentorAsync_UpdatesMentorProperties()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("mentor-update");

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId, $"Original Name {Guid.NewGuid():N}", "Old Bio", certId, skillId, "human", 150000m, 1, "Old Head", "Old Exp", 60, true));

        var updated = await service.UpdateMentorAsync(mentor.MentorId, new UpdateMentorProfileRequest(
            mentorUserId, "Updated Name", "New Bio", certId, skillId, 300000m, 2, "New Head", "New Exp", 90, false));

        Assert.Equal("Updated Name", updated.DisplayName);
        Assert.Equal("New Bio", updated.Bio);
        Assert.Equal(300000m, updated.PricePerSlot);
        Assert.Equal(2, updated.PriceCredits);
        Assert.Equal(90, updated.SessionMinutes);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task GetMentorsAsync_IncludesRatingFromView()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("mentor-rating");
        var learnerUserId = await SeedUserAsync("learner-rating");

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId, $"Rated Mentor {Guid.NewGuid():N}", "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, true));

        // Add slot, booking and review directly
        await database.SqlAsync($"""
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES ({mentor.MentorId}, DATEADD(day, -2, SYSDATETIMEOFFSET()), DATEADD(day, -2, DATEADD(hour, 1, SYSDATETIMEOFFSET())), 'open');
            DECLARE @slotId INT = SCOPE_IDENTITY();

            INSERT INTO Bookings (UserId, MentorId, SlotId, MentorType, Status, CreatedAt)
            VALUES ({learnerUserId}, {mentor.MentorId}, @slotId, 'human', 'done', SYSDATETIMEOFFSET());
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
    public async Task CreateMentorSlotAsync_ThrowsWhenMentorIsInactive()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("mentor-inactive-slot");
        var adminUserId = await SeedUserAsync("admin-inactive-slot");

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId, $"Inactive Slot Mentor {Guid.NewGuid():N}", "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, false));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateMentorSlotAsync(mentor.MentorId, adminUserId, new CreateMentorSlotRequest(new DateTime(2026, 11, 1, 9, 0, 0), null)));
        Assert.Contains("inactive mentor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateMentorSlotAsync_ThrowsWhenSlotOverlaps()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("mentor-overlap");
        var adminUserId = await SeedUserAsync("admin-overlap");

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId, $"Overlap Mentor {Guid.NewGuid():N}", "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, true));

        // Create first slot: 10:00 - 11:00 UTC
        await service.CreateMentorSlotAsync(mentor.MentorId, adminUserId, new CreateMentorSlotRequest(
            new DateTime(2026, 11, 2, 10, 0, 0),
            new DateTime(2026, 11, 2, 11, 0, 0),
            "UTC"));

        // Overlapping slot: 10:30 - 11:30 UTC
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateMentorSlotAsync(mentor.MentorId, adminUserId, new CreateMentorSlotRequest(
                new DateTime(2026, 11, 2, 10, 30, 0),
                new DateTime(2026, 11, 2, 11, 30, 0),
                "UTC")));
        Assert.Contains("overlaps", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Non-overlapping slot: 11:00 - 12:00 UTC (exact adjacent boundary is allowed)
        var adjacentSlot = await service.CreateMentorSlotAsync(mentor.MentorId, adminUserId, new CreateMentorSlotRequest(
            new DateTime(2026, 11, 2, 11, 0, 0),
            new DateTime(2026, 11, 2, 12, 0, 0),
            "UTC"));
        Assert.NotNull(adjacentSlot);
    }

    [Fact]
    public async Task CreateMentorSlotAsync_ThrowsWhenEndTimeIsBeforeOrEqualStartTime()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("mentor-time-range");
        var adminUserId = await SeedUserAsync("admin-time-range");

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId, $"Range Mentor {Guid.NewGuid():N}", "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, true));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateMentorSlotAsync(mentor.MentorId, adminUserId, new CreateMentorSlotRequest(
                new DateTime(2026, 11, 3, 11, 0, 0),
                new DateTime(2026, 11, 3, 10, 0, 0),
                "UTC")));
        Assert.Contains("End time must be after start time", ex.Message);
    }

    [Fact]
    public async Task CreateMentorSlotAsync_SupportsWindowsTimeZoneIdWithConversion()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("mentor-wintz");
        var adminUserId = await SeedUserAsync("admin-wintz");

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId, $"WinTz Mentor {Guid.NewGuid():N}", "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, true));

        // Use Windows time zone "SE Asia Standard Time" (+07:00)
        var slot = await service.CreateMentorSlotAsync(mentor.MentorId, adminUserId, new CreateMentorSlotRequest(
            new DateTime(2026, 11, 4, 14, 0, 0),
            null,
            "SE Asia Standard Time"));

        Assert.NotNull(slot);
        // 14:00 +07:00 is 07:00 UTC
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 7, 0, 0, TimeSpan.Zero), slot.StartAt);
    }

    [Fact]
    public async Task CloseMentorSlotAsync_ThrowsWhenSlotIsBooked()
    {
        var service = CreateService();
        var (certId, skillId, _) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("mentor-close-booked");
        var learnerUserId = await SeedUserAsync("learner-close-booked");

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId, $"Booked Slot Mentor {Guid.NewGuid():N}", "Bio", certId, skillId, "human", 200000m, 1, null, null, 60, true));

        await database.SqlAsync($"""
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES ({mentor.MentorId}, DATEADD(day, 2, SYSDATETIMEOFFSET()), DATEADD(day, 2, DATEADD(hour, 1, SYSDATETIMEOFFSET())), 'booked');
            DECLARE @slotId INT = SCOPE_IDENTITY();

            INSERT INTO Bookings (UserId, MentorId, SlotId, MentorType, Status, CreatedAt)
            VALUES ({learnerUserId}, {mentor.MentorId}, @slotId, 'human', 'confirmed', SYSDATETIMEOFFSET());
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var bookedSlot = await db.MentorSlots.FirstAsync(s => s.MentorId == mentor.MentorId && s.Status == "booked");

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CloseMentorSlotAsync(mentor.MentorId, bookedSlot.SlotId));
        Assert.Contains("booked", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    private async Task<int> SeedLearnerWithCreditsAsync(int startingCredits = 0, string? activeCertCode = null)
    {
        var email = $"learner-{Guid.NewGuid():N}@demo.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, TimeZoneId, EmailVerifiedAt, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Learner Test', 'Asia/Ho_Chi_Minh', SYSDATETIMEOFFSET(),
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
    public async Task BookMentorAsync_HumanMentor_Succeeds_AndSyncsSlotAndDebitsLedger()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-book");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Khoa Book {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            2, // costs 2 credits
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 16, 9, 0, 0), new DateTime(2026, 10, 16, 10, 0, 0)));

        var booking = await service.BookMentorAsync(
            mentor.MentorId,
            learnerUserId,
            new BookMentorRequest(slot.SlotId, "Listening Part 3 strategies"));

        Assert.Equal("confirmed", booking.Status);
        Assert.Equal("human", booking.MentorType);
        Assert.NotNull(booking.MeetLink);
        Assert.StartsWith("https://meet.google.com/", booking.MeetLink);
        Assert.Equal("Listening Part 3 strategies", booking.FocusArea);
        Assert.Equal(2, booking.PriceCredits);

        await using var db = DbContextFactory.Create(database.ConnectionString);

        // Verify slot status was synced to 'booked' by trigger
        var dbSlot = await db.MentorSlots.FirstAsync(s => s.SlotId == slot.SlotId);
        Assert.Equal("booked", dbSlot.Status);

        // Verify credit transaction
        var txn = await db.CreditTransactions.FirstOrDefaultAsync(t => t.BookingId == booking.BookingId && t.Reason == "mentor_booking");
        Assert.NotNull(txn);
        Assert.Equal(-2, txn.Delta);

        // Verify balance was reduced: 5 - 2 = 3
        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == learnerUserId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(3, balance);
    }

    [Fact]
    public async Task BookMentorAsync_InsufficientCredits_ThrowsAndRefusesWithoutDeduction()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-insuf");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 1, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Khoa Insuf {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            3, // costs 3 credits, learner only has 1
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 16, 14, 0, 0), new DateTime(2026, 10, 16, 15, 0, 0)));

        var ex = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId)));

        Assert.Equal("insufficient_credits", ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        // Verify 0 credits deducted
        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == learnerUserId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(1, balance);

        // Verify slot remains open
        var dbSlot = await db.MentorSlots.FirstAsync(s => s.SlotId == slot.SlotId);
        Assert.Equal("open", dbSlot.Status);

        // Verify no booking created
        var hasBooking = await db.Bookings.AnyAsync(b => b.SlotId == slot.SlotId);
        Assert.False(hasBooking);
    }

    [Fact]
    public async Task BookMentorAsync_SlotAlreadyBooked_ThrowsSlotNotAvailableWithoutDeduction()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-double");
        var learner1 = await SeedLearnerWithCreditsAsync(startingCredits: 10, activeCertCode: certCode);
        var learner2 = await SeedLearnerWithCreditsAsync(startingCredits: 10, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Khoa Double {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 16, 16, 0, 0), new DateTime(2026, 10, 16, 17, 0, 0)));

        // Learner 1 books slot
        var booking1 = await service.BookMentorAsync(mentor.MentorId, learner1, new BookMentorRequest(slot.SlotId));
        Assert.NotNull(booking1);

        // Learner 2 attempts to book same slot
        var ex = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.BookMentorAsync(mentor.MentorId, learner2, new BookMentorRequest(slot.SlotId)));

        Assert.Equal("slot_not_available", ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        // Verify learner 2 was not charged
        var balance2 = await db.vw_UserCreditBalances.Where(b => b.UserId == learner2).Select(b => b.Balance).SingleAsync();
        Assert.Equal(10, balance2);
    }

    [Fact]
    public async Task CancelBookingAsync_CancelsBooking_FreesSlot_AndRefundsLedger()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-cancel");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Khoa Cancel {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            2,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 17, 9, 0, 0), new DateTime(2026, 10, 17, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));
        Assert.Equal("confirmed", booking.Status);

        // Cancel booking
        var cancelResult = await service.CancelBookingAsync(booking.BookingId, learnerUserId);
        Assert.Equal("cancelled", cancelResult.Status);
        Assert.Equal(2, cancelResult.RefundedCredits);

        await using var db = DbContextFactory.Create(database.ConnectionString);

        // Slot status reverted to 'open' by trigger
        var dbSlot = await db.MentorSlots.FirstAsync(s => s.SlotId == slot.SlotId);
        Assert.Equal("open", dbSlot.Status);

        // Refund ledger row
        var refundTxn = await db.CreditTransactions.FirstOrDefaultAsync(t => t.BookingId == booking.BookingId && t.Reason == "booking_cancel");
        Assert.NotNull(refundTxn);
        Assert.Equal(2, refundTxn.Delta);

        // Learner balance restored: 5 - 2 + 2 = 5
        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == learnerUserId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(5, balance);
    }

    [Fact]
    public async Task CancelBookingAsync_IdempotencyGuard_ThrowsIfAlreadyCancelledOrRefunded()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-idem");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Khoa Idem {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            2,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 17, 11, 0, 0), new DateTime(2026, 10, 17, 12, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));
        await service.CancelBookingAsync(booking.BookingId, learnerUserId);

        // Second cancellation attempt
        var ex = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.CancelBookingAsync(booking.BookingId, learnerUserId));

        Assert.Equal("booking_already_refunded", ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var refundCount = await db.CreditTransactions.CountAsync(t => t.BookingId == booking.BookingId && t.Reason == "booking_cancel");
        Assert.Equal(1, refundCount);
    }

    [Fact]
    public async Task BookMentorAsync_AiMentor_StartsSpeakingAttempt_AndDebitsLedger()
    {
        var certCode = $"SPK{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert, 'SPEA', N'Speaking', 'speaking', 1, 20);
            DECLARE @sS INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sS, @cert, 'speaking', 'S1', N'Read aloud', 1, 1);
            DECLARE @sec1 INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES
              (@cert, N'AI Speaking Practice {certCode}', 'published', 20, 'speaking_practice', 1);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec1, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp1, @exam, @sec1, @sS, N'Read aloud text.', 1, 'free_response', 1);
            """);

        await using var dbSetup = DbContextFactory.Create(database.ConnectionString);
        var cert = await dbSetup.Certificates.FirstAsync(c => c.Code == certCode);
        var learners = new LearnerService(dbSetup);
        var tempDir = Path.Combine(Path.GetTempPath(), "extra_eff_audio_" + Guid.NewGuid().ToString("N"));
        var fileStore = new LocalFileStore(tempDir);
        var grader = new FakeAiGrader();
        var writingService = new WritingPracticeService(dbSetup, learners, grader);
        var speakingService = new SpeakingPracticeService(dbSetup, learners, fileStore, writingService);

        var service = new MentorService(dbSetup, _clock, learners, speakingService);

        var aiMentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            null,
            $"AI Coach {Guid.NewGuid():N}",
            "AI speaking practice 24/7",
            cert.CertId,
            null,
            "ai",
            0m,
            1, // costs 1 credit
            "Headline",
            "Speaking feedback",
            60,
            true));

        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 4, activeCertCode: certCode);

        // Book AI mentor with SlotId = null
        var booking = await service.BookMentorAsync(aiMentor.MentorId, learnerUserId, new BookMentorRequest(SlotId: null));

        Assert.Equal("confirmed", booking.Status);
        Assert.Equal("ai", booking.MentorType);
        Assert.Null(booking.SlotId);
        Assert.NotNull(booking.AttemptId);
        Assert.NotNull(booking.RedirectUrl);
        Assert.Equal($"/speaking/take/{booking.AttemptId}", booking.RedirectUrl);

        // Verify credit transaction was created
        var txn = await dbSetup.CreditTransactions.FirstOrDefaultAsync(t => t.BookingId == booking.BookingId && t.Reason == "mentor_booking");
        Assert.NotNull(txn);
        Assert.Equal(-1, txn.Delta);

        // Verify balance was reduced: 4 - 1 = 3
        var balance = await dbSetup.vw_UserCreditBalances.Where(b => b.UserId == learnerUserId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(3, balance);
    }

    [Fact]
    public async Task GetBookingDetailAsync_And_GetLearnerBookingsAsync_ReturnCorrectData()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-view");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 10, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Khoa View {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 18, 9, 0, 0), new DateTime(2026, 10, 18, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId, "Focus 1"));

        var detail = await service.GetBookingDetailAsync(booking.BookingId, learnerUserId);
        Assert.Equal(booking.BookingId, detail.BookingId);
        Assert.Equal(mentor.DisplayName, detail.MentorName);
        Assert.NotNull(detail.MeetLink);
        Assert.Equal("Focus 1", detail.FocusArea);

        var bookingsList = await service.GetLearnerBookingsAsync(learnerUserId);
        Assert.Single(bookingsList);
        Assert.Equal(booking.BookingId, bookingsList[0].BookingId);
    }

    [Fact]
    public async Task BookMentorAsync_AiMentor_InvalidOrCrossCertExamId_ThrowsExamNotFound()
    {
        var certCode1 = $"SPK1{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var certCode2 = $"SPK2{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode1}', N'Cert {certCode1}', 1);
            DECLARE @cert1 INT = SCOPE_IDENTITY();
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode2}', N'Cert {certCode2}', 1);
            DECLARE @cert2 INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert1, 'SP1', N'Speaking 1', 'speaking', 1, 20),
              (@cert2, 'SP2', N'Speaking 2', 'speaking', 1, 20);
            DECLARE @s1 INT = (SELECT SkillId FROM Skills WHERE CertId = @cert1);
            DECLARE @s2 INT = (SELECT SkillId FROM Skills WHERE CertId = @cert2);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@s1, @cert1, 'speaking', 'S1', N'Read aloud', 1, 1),
              (@s2, @cert2, 'speaking', 'S2', N'Read aloud', 1, 1);
            DECLARE @sec1 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert1);
            DECLARE @sec2 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES
              (@cert1, N'Exam Cert 1', 'published', 20, 'speaking_practice', 1),
              (@cert2, N'Exam Cert 2', 'published', 20, 'speaking_practice', 1);
            """);

        await using var dbSetup = DbContextFactory.Create(database.ConnectionString);
        var cert1 = await dbSetup.Certificates.FirstAsync(c => c.Code == certCode1);
        var examCert2 = await dbSetup.Exams.FirstAsync(e => e.Cert.Code == certCode2);
        var learners = new LearnerService(dbSetup);
        var tempDir = Path.Combine(Path.GetTempPath(), "extra_eff_audio_" + Guid.NewGuid().ToString("N"));
        var fileStore = new LocalFileStore(tempDir);
        var grader = new FakeAiGrader();
        var writingService = new WritingPracticeService(dbSetup, learners, grader);
        var speakingService = new SpeakingPracticeService(dbSetup, learners, fileStore, writingService);

        var service = new MentorService(dbSetup, _clock, learners, speakingService);

        var aiMentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            null,
            $"AI Coach {Guid.NewGuid():N}",
            "AI speaking practice 24/7",
            cert1.CertId,
            null,
            "ai",
            0m,
            1,
            "Headline",
            "Speaking feedback",
            60,
            true));

        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode1);

        // Attempting to book aiMentor (Cert 1) with exam from Cert 2 must throw ExamNotFound
        var ex = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.BookMentorAsync(aiMentor.MentorId, learnerUserId, new BookMentorRequest(ExamId: examCert2.ExamId)));

        Assert.Equal(MentorBookingException.ExamNotFound, ex.Code);
    }

    [Fact]
    public async Task CancelBookingAsync_RefundsExactOriginalCharge_EvenIfMentorPriceCreditsChanged()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-price-change");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 10, activeCertCode: certCode);

        // Mentor initially priced at 2 credits
        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Khoa PriceChange {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            2,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 19, 10, 0, 0), new DateTime(2026, 10, 19, 11, 0, 0)));

        // Booked when PriceCredits was 2
        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));
        Assert.Equal("confirmed", booking.Status);

        // Admin subsequently increases mentor's price to 5 credits
        await service.UpdateMentorAsync(mentor.MentorId, new UpdateMentorProfileRequest(
            mentorUserId,
            mentor.DisplayName,
            "Bio",
            certId,
            skillId,
            500000m,
            5,
            "Headline",
            "Expertise",
            60,
            true));

        // Cancellation should refund exactly the original 2 credits, NOT 5, and DB trigger should succeed
        var cancelResult = await service.CancelBookingAsync(booking.BookingId, learnerUserId);
        Assert.Equal("cancelled", cancelResult.Status);
        Assert.Equal(2, cancelResult.RefundedCredits);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var refundTxn = await db.CreditTransactions.FirstOrDefaultAsync(t => t.BookingId == booking.BookingId && t.Reason == "booking_cancel");
        Assert.NotNull(refundTxn);
        Assert.Equal(2, refundTxn.Delta);

        // Balance was 10 - 2 + 2 = 10
        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == learnerUserId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(10, balance);
    }

    [Fact]
    public async Task CancelBookingAsync_SlotAlreadyStartedOrPassed_ThrowsBookingCannotCancel()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-passed");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Khoa Past {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            2,
            "Headline",
            "Expertise",
            60,
            true));

        // Slot in future when created
        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 16, 9, 0, 0), new DateTime(2026, 10, 16, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));

        // Advance time to AFTER the slot start
        var advancedClock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.Zero));
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var servicePast = new MentorService(db, advancedClock);

        var ex = await Assert.ThrowsAsync<MentorBookingException>(() =>
            servicePast.CancelBookingAsync(booking.BookingId, learnerUserId));

        Assert.Equal("booking_cannot_cancel", ex.Code);
        Assert.Contains("started or passed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelBookingAsync_DifferentUser_ThrowsKeyNotFoundException()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-other");
        var learner1 = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);
        var learner2 = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Khoa Other {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 20, 9, 0, 0), new DateTime(2026, 10, 20, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learner1, new BookMentorRequest(slot.SlotId));

        // Learner 2 tries to cancel learner 1's booking
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CancelBookingAsync(booking.BookingId, learner2));
    }

    [Fact]
    public async Task RateBookingAsync_FinishedBooking_CreatesReviewAndReturnsDto()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-rate");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Rate {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 18, 9, 0, 0), new DateTime(2026, 10, 18, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));

        // Mark booking as done
        await database.SqlAsync($"UPDATE Bookings SET Status = 'done' WHERE BookingId = {booking.BookingId};");

        var review = await service.RateBookingAsync(booking.BookingId, learnerUserId, new RateMentorRequest(5, "Excellent session, very clear feedback!"));

        Assert.NotNull(review);
        Assert.True(review.ReviewId > 0);
        Assert.Equal(5, review.Rating);
        Assert.Equal("Excellent session, very clear feedback!", review.Text);
        Assert.Equal("Learner Test", review.UserDisplayName);

        // Verify review exists in DB
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbReview = await db.Reviews.FirstOrDefaultAsync(r => r.BookingId == booking.BookingId);
        Assert.NotNull(dbReview);
        Assert.Equal(5, dbReview.Rating);
        Assert.Equal(learnerUserId, dbReview.UserId);

        // Verify vw_MentorAvgRating reflects the review
        var ratingRow = await db.vw_MentorAvgRatings.FirstOrDefaultAsync(r => r.MentorId == mentor.MentorId);
        Assert.NotNull(ratingRow);
        Assert.Equal(5.0m, ratingRow.AvgRating);
        Assert.Equal(1, ratingRow.ReviewCount);

        // Verify booking detail includes the review
        var bookingDetail = await service.GetBookingDetailAsync(booking.BookingId, learnerUserId);
        Assert.NotNull(bookingDetail.Review);
        Assert.Equal(5, bookingDetail.Review.Rating);
        Assert.Equal("Excellent session, very clear feedback!", bookingDetail.Review.Text);
    }

    [Fact]
    public async Task RateBookingAsync_UnfinishedBooking_ThrowsBookingNotFinished()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-unfin");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Unfinished {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 19, 9, 0, 0), new DateTime(2026, 10, 19, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));

        // Booking is 'confirmed', not 'done'
        var ex = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.RateBookingAsync(booking.BookingId, learnerUserId, new RateMentorRequest(5, "Premature review")));

        Assert.Equal(MentorBookingException.BookingNotFinished, ex.Code);
        Assert.Equal(400, ex.Status);

        // Test with cancelled booking
        await database.SqlAsync($"UPDATE Bookings SET Status = 'cancelled' WHERE BookingId = {booking.BookingId};");
        var exCancelled = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.RateBookingAsync(booking.BookingId, learnerUserId, new RateMentorRequest(4, "Cancelled review")));

        Assert.Equal(MentorBookingException.BookingNotFinished, exCancelled.Code);
    }

    [Fact]
    public async Task RateBookingAsync_DifferentUser_ThrowsKeyNotFoundException()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-userdiff");
        var learner1 = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);
        var learner2 = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. UserDiff {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 21, 9, 0, 0), new DateTime(2026, 10, 21, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learner1, new BookMentorRequest(slot.SlotId));
        await database.SqlAsync($"UPDATE Bookings SET Status = 'done' WHERE BookingId = {booking.BookingId};");

        // Learner 2 tries to rate learner 1's booking
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RateBookingAsync(booking.BookingId, learner2, new RateMentorRequest(5, "Other learner review")));
    }

    [Fact]
    public async Task RateBookingAsync_AlreadyRated_ThrowsBookingAlreadyRated()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-dup");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Dup {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));
        await database.SqlAsync($"UPDATE Bookings SET Status = 'done' WHERE BookingId = {booking.BookingId};");

        // First review succeeds
        await service.RateBookingAsync(booking.BookingId, learnerUserId, new RateMentorRequest(5, "First review"));

        // Second review attempt throws BookingAlreadyRated
        var ex = await Assert.ThrowsAsync<MentorBookingException>(() =>
            service.RateBookingAsync(booking.BookingId, learnerUserId, new RateMentorRequest(4, "Second review attempt")));

        Assert.Equal(MentorBookingException.BookingAlreadyRated, ex.Code);
        Assert.Equal(400, ex.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(10)]
    public async Task RateBookingAsync_InvalidRating_ThrowsValidationException(int invalidRating)
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-inv");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. Inv {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 23, 9, 0, 0), new DateTime(2026, 10, 23, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));
        await database.SqlAsync($"UPDATE Bookings SET Status = 'done' WHERE BookingId = {booking.BookingId};");

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RateBookingAsync(booking.BookingId, learnerUserId, new RateMentorRequest(invalidRating, "Invalid rating")));

        Assert.Contains("Rating must be between 1 and 5", ex.Message);
    }

    [Fact]
    public async Task RateBookingAsync_MultipleReviews_UpdatesMentorAvgRating()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-multirate");
        var learner1 = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);
        var learner2 = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. MultiRate {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot1 = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 24, 9, 0, 0), new DateTime(2026, 10, 24, 10, 0, 0)));

        var slot2 = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 24, 11, 0, 0), new DateTime(2026, 10, 24, 12, 0, 0)));

        var booking1 = await service.BookMentorAsync(mentor.MentorId, learner1, new BookMentorRequest(slot1.SlotId));
        var booking2 = await service.BookMentorAsync(mentor.MentorId, learner2, new BookMentorRequest(slot2.SlotId));

        await database.SqlAsync($"""
            UPDATE Bookings SET Status = 'done' WHERE BookingId IN ({booking1.BookingId}, {booking2.BookingId});
            """);

        // Learner 1 rates 4
        await service.RateBookingAsync(booking1.BookingId, learner1, new RateMentorRequest(4, "Good session"));
        // Learner 2 rates 5
        await service.RateBookingAsync(booking2.BookingId, learner2, new RateMentorRequest(5, "Superb help"));

        // Verify view: (4 + 5) / 2 = 4.5
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ratingRow = await db.vw_MentorAvgRatings.FirstOrDefaultAsync(r => r.MentorId == mentor.MentorId);
        Assert.NotNull(ratingRow);
        Assert.Equal(4.5m, ratingRow.AvgRating);
        Assert.Equal(2, ratingRow.ReviewCount);

        // Verify GetMentorDetailAsync reflects updated rating and returns reviews
        var detail = await service.GetMentorDetailAsync(mentor.MentorId);
        Assert.Equal(4.5m, detail.Mentor.AvgRating);
        Assert.Equal(2, detail.Mentor.ReviewCount);
        Assert.Equal(2, detail.Reviews.Count);
        Assert.Contains(detail.Reviews, r => r.Rating == 4 && r.Text == "Good session");
        Assert.Contains(detail.Reviews, r => r.Rating == 5 && r.Text == "Superb help");
    }

    [Fact]
    public async Task RateBookingAsync_ReviewTextExceeds2000Chars_ThrowsValidationException()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-toolong");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. TooLong {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 25, 9, 0, 0), new DateTime(2026, 10, 25, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));
        await database.SqlAsync($"UPDATE Bookings SET Status = 'done' WHERE BookingId = {booking.BookingId};");

        var longText = new string('A', 2001);
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RateBookingAsync(booking.BookingId, learnerUserId, new RateMentorRequest(5, longText)));

        Assert.Contains("Review text cannot exceed 2000 characters", ex.Message);
    }

    [Fact]
    public async Task RateBookingAsync_WhitespaceReviewText_IsSavedAsNull()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-white");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. White {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 26, 9, 0, 0), new DateTime(2026, 10, 26, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));
        await database.SqlAsync($"UPDATE Bookings SET Status = 'done' WHERE BookingId = {booking.BookingId};");

        var review = await service.RateBookingAsync(booking.BookingId, learnerUserId, new RateMentorRequest(5, "   \n\t  "));
        Assert.Null(review.Text);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbReview = await db.Reviews.FirstOrDefaultAsync(r => r.BookingId == booking.BookingId);
        Assert.NotNull(dbReview);
        Assert.Null(dbReview.Text);
    }

    [Fact]
    public async Task GetLearnerBookingsAsync_IncludesReviewDto_WhenBookingIsRated()
    {
        var service = CreateService();
        var (certId, skillId, certCode) = await SeedCertAndSkillAsync();
        var mentorUserId = await SeedUserAsync("human-mentor-listrev");
        var learnerUserId = await SeedLearnerWithCreditsAsync(startingCredits: 5, activeCertCode: certCode);

        var mentor = await service.CreateMentorAsync(new CreateMentorProfileRequest(
            mentorUserId,
            $"Dr. ListRev {Guid.NewGuid():N}",
            "Bio",
            certId,
            skillId,
            "human",
            200000m,
            1,
            "Headline",
            "Expertise",
            60,
            true));

        var slot = await service.CreateMentorSlotAsync(
            mentor.MentorId,
            mentorUserId,
            new CreateMentorSlotRequest(new DateTime(2026, 10, 27, 9, 0, 0), new DateTime(2026, 10, 27, 10, 0, 0)));

        var booking = await service.BookMentorAsync(mentor.MentorId, learnerUserId, new BookMentorRequest(slot.SlotId));
        await database.SqlAsync($"UPDATE Bookings SET Status = 'done' WHERE BookingId = {booking.BookingId};");

        // Before rating: review is null in learner bookings list
        var bookingsBefore = await service.GetLearnerBookingsAsync(learnerUserId);
        var bBefore = Assert.Single(bookingsBefore, b => b.BookingId == booking.BookingId);
        Assert.Null(bBefore.Review);

        // Submit rating
        await service.RateBookingAsync(booking.BookingId, learnerUserId, new RateMentorRequest(5, "Great lesson!"));

        // After rating: review is populated in learner bookings list
        var bookingsAfter = await service.GetLearnerBookingsAsync(learnerUserId);
        var bAfter = Assert.Single(bookingsAfter, b => b.BookingId == booking.BookingId);
        Assert.NotNull(bAfter.Review);
        Assert.Equal(5, bAfter.Review.Rating);
        Assert.Equal("Great lesson!", bAfter.Review.Text);
    }
}
