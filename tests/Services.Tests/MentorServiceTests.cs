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
}
