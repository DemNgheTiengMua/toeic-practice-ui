using BusinessObjects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class LearnerDashboardTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task GetDashboardAsync_PopulatesUpcomingBookings_OrderedByStartAt_AndExcludesPastOrCancelled()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        var cert1 = Unique("T");
        var cert2 = Unique("I");
        var email1 = $"dash-learner1-{Guid.NewGuid():N}@test.local";
        var email2 = $"dash-learner2-{Guid.NewGuid():N}@test.local";

        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{cert1}', N'Cert {cert1}', 1);
            DECLARE @c1 INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality) VALUES (@c1, 'LIST', N'Listening', 'listening');
            DECLARE @s1 INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, ExpectedQuestionCount, Name)
              VALUES (@s1, @c1, 'listening', 'P1', 1, N'Part 1');
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
              VALUES (@c1, 'B1', N'Band B1', 0, 990, 1);

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{cert2}', N'Cert {cert2}', 1);
            DECLARE @c2 INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality) VALUES (@c2, 'SPEA', N'Speaking', 'speaking');
            DECLARE @s2 INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, ExpectedQuestionCount, Name)
              VALUES (@s2, @c2, 'speaking', 'S1', 1, N'Part 1');
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
              VALUES (@c2, 'B2', N'Band B2', 0, 9, 1);

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email1}', 0x01, N'Learner One', @c1);
            DECLARE @u1 INT = SCOPE_IDENTITY();

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email2}', 0x01, N'Learner Two', @c1);
            DECLARE @u2 INT = SCOPE_IDENTITY();

            INSERT INTO MentorProfiles (DisplayName, CertId, SkillId, MeetLink, PriceCredits, SessionMinutes, IsActive)
            VALUES (N'Dr. Trần Minh Khoa', @c1, @s1, N'https://meet.google.com/kho-a111-222', 2, 60, 1);
            DECLARE @m1 INT = SCOPE_IDENTITY();

            INSERT INTO MentorProfiles (DisplayName, CertId, SkillId, MeetLink, PriceCredits, SessionMinutes, IsActive)
            VALUES (N'Ms. Lê Hoàng Yến', @c2, @s2, N'https://meet.google.com/yen-b333-444', 1, 60, 1);
            DECLARE @m2 INT = SCOPE_IDENTITY();

            -- 1. Past slot (EndAt <= nowUtc): should be excluded
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES (@m1, '2026-11-14T08:00:00Z', '2026-11-14T09:00:00Z', 'booked');
            DECLARE @slotPast INT = SCOPE_IDENTITY();
            INSERT INTO Bookings (UserId, MentorId, SlotId, Status, MeetLink, PriceCredits, FocusArea, CreatedAt)
            VALUES (@u1, @m1, @slotPast, 'confirmed', N'https://meet.google.com/kho-a111-222', 2, N'Past session', '2026-11-13T08:00:00Z');

            -- 2. Future slot on cert2 (earlier: 2026-11-15 09:00Z): should be included even though active cert is cert1!
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES (@m2, '2026-11-15T09:00:00Z', '2026-11-15T10:00:00Z', 'booked');
            DECLARE @slotFuture1 INT = SCOPE_IDENTITY();
            INSERT INTO Bookings (UserId, MentorId, SlotId, Status, MeetLink, PriceCredits, FocusArea, CreatedAt)
            VALUES (@u1, @m2, @slotFuture1, 'confirmed', N'https://meet.google.com/yen-b333-444', 1, N'Speaking Part 2 fluency', '2026-11-14T08:00:00Z');

            -- 3. Future slot on cert1 (later: 2026-11-16 14:00Z): should be included second
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES (@m1, '2026-11-16T14:00:00Z', '2026-11-16T15:00:00Z', 'booked');
            DECLARE @slotFuture2 INT = SCOPE_IDENTITY();
            INSERT INTO Bookings (UserId, MentorId, SlotId, Status, MeetLink, PriceCredits, FocusArea, CreatedAt)
            VALUES (@u1, @m1, @slotFuture2, 'confirmed', N'https://meet.google.com/kho-a111-222', 2, N'Listening Part 3 traps', '2026-11-14T08:30:00Z');

            -- 4. Cancelled future slot: should be excluded
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES (@m1, '2026-11-17T09:00:00Z', '2026-11-17T10:00:00Z', 'open');
            DECLARE @slotCancelled INT = SCOPE_IDENTITY();
            INSERT INTO Bookings (UserId, MentorId, SlotId, Status, MeetLink, PriceCredits, CancelledAt, CreatedAt)
            VALUES (@u1, @m1, @slotCancelled, 'cancelled', N'https://meet.google.com/kho-a111-222', 2, '2026-11-14T09:00:00Z', '2026-11-14T08:00:00Z');

            -- 5. Another learner's future booking: should be excluded
            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES (@m1, '2026-11-18T09:00:00Z', '2026-11-18T10:00:00Z', 'booked');
            DECLARE @slotOther INT = SCOPE_IDENTITY();
            INSERT INTO Bookings (UserId, MentorId, SlotId, Status, MeetLink, PriceCredits, CreatedAt)
            VALUES (@u2, @m1, @slotOther, 'confirmed', N'https://meet.google.com/kho-a111-222', 2, '2026-11-14T08:00:00Z');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var userId = await db.Users.Where(u => u.Email == email1).Select(u => u.UserId).SingleAsync();
        var service = new LearnerService(db, clock: clock);

        var dashboard = await service.GetDashboardAsync(userId);

        Assert.NotNull(dashboard.UpcomingBookings);
        Assert.Equal(2, dashboard.UpcomingBookings.Count);

        // First upcoming booking is the earlier one (2026-11-15T09:00:00Z on cert2)
        var first = dashboard.UpcomingBookings[0];
        Assert.Equal("Ms. Lê Hoàng Yến", first.MentorName);
        Assert.Equal(cert2, first.CertCode);
        Assert.Equal("Speaking", first.SkillName);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero), first.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero), first.EndAt);
        Assert.Equal("https://meet.google.com/yen-b333-444", first.MeetLink);
        Assert.Equal("Speaking Part 2 fluency", first.FocusArea);

        // Second upcoming booking is 2026-11-16T14:00:00Z on cert1
        var second = dashboard.UpcomingBookings[1];
        Assert.Equal("Dr. Trần Minh Khoa", second.MentorName);
        Assert.Equal(cert1, second.CertCode);
        Assert.Equal("Listening", second.SkillName);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 14, 0, 0, TimeSpan.Zero), second.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeSpan.Zero), second.EndAt);
        Assert.Equal("https://meet.google.com/kho-a111-222", second.MeetLink);
        Assert.Equal("Listening Part 3 traps", second.FocusArea);
    }
}
