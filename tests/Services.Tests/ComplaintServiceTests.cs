using BusinessObjects;
using Contracts;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ComplaintServiceTests(DatabaseFixture database)
{
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private ComplaintService CreateService() => new(
        DbContextFactory.Create(database.ConnectionString),
        _clock);

    private async Task<int> SeedAdminAsync()
    {
        var email = $"admin-{Guid.NewGuid():N}@toeic.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{email}', 0x01, N'System Admin', SYSDATETIMEOFFSET());
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<int> SeedLearnerAsync(string? name = null)
    {
        var email = $"learner-{Guid.NewGuid():N}@test.local";
        var fullName = name ?? "Test Learner";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'{fullName}', SYSDATETIMEOFFSET());
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<int> EnsureCertAsync()
    {
        var code = $"C{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, Description, IsActive)
            VALUES ('{code}', N'Cert {code}', N'Standard test', 1);
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
    }

    private async Task<(int BookingId, int MentorId)> SeedBookingAsync(int userId)
    {
        var certId = await EnsureCertAsync();
        var mentorUser = await SeedLearnerAsync("Mentor User");

        await database.SqlAsync($"""
            INSERT INTO MentorProfiles (UserId, CertId, DisplayName, Type, PricePerSlot, PriceCredits, SessionMinutes, IsActive)
            VALUES ({mentorUser}, {certId}, N'Dr. Tran Minh Khoa', 'human', 200000, 1, 60, 1);
            DECLARE @mentorId INT = SCOPE_IDENTITY();

            INSERT INTO MentorSlots (MentorId, StartAt, EndAt, Status)
            VALUES (@mentorId, DATEADD(day, -1, SYSDATETIMEOFFSET()), DATEADD(day, -1, DATEADD(hour, 1, SYSDATETIMEOFFSET())), 'booked');
            DECLARE @slotId INT = SCOPE_IDENTITY();

            INSERT INTO Bookings (UserId, MentorId, SlotId, MentorType, Status, CreatedAt)
            VALUES ({userId}, @mentorId, @slotId, 'human', 'done', SYSDATETIMEOFFSET());
            SELECT @slotId AS SlotId, @mentorId AS MentorId, SCOPE_IDENTITY() AS BookingId;
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var booking = await db.Bookings.Where(b => b.UserId == userId).OrderByDescending(b => b.BookingId).FirstAsync();
        return (booking.BookingId, booking.MentorId);
    }

    private async Task<int> SeedExamAttemptAsync(int userId)
    {
        var certId = await EnsureCertAsync();
        var examName = $"Exam-{Guid.NewGuid():N}"[..20];

        await database.SqlAsync($"""
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, CreatedAt)
            VALUES ({certId}, N'{examName}', 'published', 120, 'mock', SYSDATETIMEOFFSET());
            DECLARE @examId INT = SCOPE_IDENTITY();

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore)
            VALUES ({userId}, @examId, {certId}, 'graded', DATEADD(hour, -2, SYSDATETIMEOFFSET()), DATEADD(hour, 1, SYSDATETIMEOFFSET()), DATEADD(hour, -1, SYSDATETIMEOFFSET()), 850);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attempt = await db.ExamAttempts.Where(a => a.UserId == userId).OrderByDescending(a => a.AttemptId).FirstAsync();
        return attempt.AttemptId;
    }

    private async Task<int> SeedOrderAsync(int userId)
    {
        var orderCode = $"DH-{Guid.NewGuid():N}"[..18].ToUpper();
        var paymentNo = Random.Shared.NextInt64(1000000, 999999999);
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Packages WHERE Name = N'5-credit package')
              INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder)
              VALUES (N'5-credit package', 5, 199000, 1, 0, N'Features', 1);
            DECLARE @pkgId INT = (SELECT TOP 1 PackageId FROM Packages WHERE Name = N'5-credit package');

            INSERT INTO Orders (OrderCode, PaymentNo, UserId, PackageId, Amount, Status, Gateway, CreatedAt, PaidAt)
            VALUES ('{orderCode}', {paymentNo}, {userId}, @pkgId, 199000, 'paid', 'mock', DATEADD(day, -1, SYSDATETIMEOFFSET()), DATEADD(day, -1, SYSDATETIMEOFFSET()));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var order = await db.Orders.Where(o => o.UserId == userId).OrderByDescending(o => o.OrderId).FirstAsync();
        return order.OrderId;
    }

    [Fact]
    public async Task Filing_a_complaint_on_learners_own_booking_attempt_or_order_succeeds()
    {
        var learnerId = await SeedLearnerAsync();
        var (bookingId, _) = await SeedBookingAsync(learnerId);
        var attemptId = await SeedExamAttemptAsync(learnerId);
        var orderId = await SeedOrderAsync(learnerId);
        var service = CreateService();

        // 1. Booking complaint (mentor_no_show)
        var bookingComplaint = await service.FileComplaintAsync(learnerId, new FileComplaintRequest(
            ComplaintTypeDto.MentorNoShow,
            "Mentor did not show up for session.",
            BookingId: bookingId));

        Assert.Equal(ComplaintTypeDto.MentorNoShow, bookingComplaint.Type);
        Assert.Equal(ComplaintStatusDto.Open, bookingComplaint.Status);
        Assert.Equal(bookingId, bookingComplaint.BookingId);
        Assert.Null(bookingComplaint.AttemptId);
        Assert.Null(bookingComplaint.OrderId);

        // 2. Attempt complaint (grading_issue)
        var attemptComplaint = await service.FileComplaintAsync(learnerId, new FileComplaintRequest(
            ComplaintTypeDto.GradingIssue,
            "Speaking question 3 grading appears miscalculated.",
            AttemptId: attemptId));

        Assert.Equal(ComplaintTypeDto.GradingIssue, attemptComplaint.Type);
        Assert.Equal(ComplaintStatusDto.Open, attemptComplaint.Status);
        Assert.Equal(attemptId, attemptComplaint.AttemptId);

        // 3. Order complaint (payment_issue)
        var orderComplaint = await service.FileComplaintAsync(learnerId, new FileComplaintRequest(
            ComplaintTypeDto.PaymentIssue,
            "Duplicate bank deduction for package purchase.",
            OrderId: orderId));

        Assert.Equal(ComplaintTypeDto.PaymentIssue, orderComplaint.Type);
        Assert.Equal(ComplaintStatusDto.Open, orderComplaint.Status);
        Assert.Equal(orderId, orderComplaint.OrderId);

        // Check history retrieves all 3
        var history = await service.GetLearnerComplaintsAsync(learnerId);
        Assert.Equal(3, history.Count);
    }

    [Fact]
    public async Task Filing_a_complaint_on_another_users_subject_is_rejected_with_ownership_error()
    {
        var learner1 = await SeedLearnerAsync("Learner 1");
        var learner2 = await SeedLearnerAsync("Learner 2");
        var (otherBookingId, _) = await SeedBookingAsync(learner2);
        var otherAttemptId = await SeedExamAttemptAsync(learner2);
        var otherOrderId = await SeedOrderAsync(learner2);

        var service = CreateService();

        // Attempting to report other's booking
        var exBooking = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.FileComplaintAsync(learner1, new FileComplaintRequest(
                ComplaintTypeDto.MentorNoShow,
                "Not my booking",
                BookingId: otherBookingId)));
        Assert.Equal(ApiError.Codes.ComplaintInvalidSubject, exBooking.Code);

        // Attempting to report other's attempt
        var exAttempt = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.FileComplaintAsync(learner1, new FileComplaintRequest(
                ComplaintTypeDto.GradingIssue,
                "Not my attempt",
                AttemptId: otherAttemptId)));
        Assert.Equal(ApiError.Codes.ComplaintInvalidSubject, exAttempt.Code);

        // Attempting to report other's order
        var exOrder = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.FileComplaintAsync(learner1, new FileComplaintRequest(
                ComplaintTypeDto.PaymentIssue,
                "Not my order",
                OrderId: otherOrderId)));
        Assert.Equal(ApiError.Codes.ComplaintInvalidSubject, exOrder.Code);
    }

    [Fact]
    public async Task Mismatched_complaint_type_and_subject_is_rejected()
    {
        var learner = await SeedLearnerAsync();
        var (bookingId, _) = await SeedBookingAsync(learner);
        var attemptId = await SeedExamAttemptAsync(learner);
        var orderId = await SeedOrderAsync(learner);
        var service = CreateService();

        // payment_issue with a booking
        var ex1 = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.FileComplaintAsync(learner, new FileComplaintRequest(
                ComplaintTypeDto.PaymentIssue,
                "Payment issue with booking",
                BookingId: bookingId)));
        Assert.Equal(ApiError.Codes.ComplaintInvalidSubject, ex1.Code);

        // mentor_no_show with an attempt
        var ex2 = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.FileComplaintAsync(learner, new FileComplaintRequest(
                ComplaintTypeDto.MentorNoShow,
                "Mentor no show on attempt",
                AttemptId: attemptId)));
        Assert.Equal(ApiError.Codes.ComplaintInvalidSubject, ex2.Code);

        // grading_issue with an order
        var ex3 = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.FileComplaintAsync(learner, new FileComplaintRequest(
                ComplaintTypeDto.GradingIssue,
                "Grading issue on order",
                OrderId: orderId)));
        Assert.Equal(ApiError.Codes.ComplaintInvalidSubject, ex3.Code);
    }

    [Fact]
    public async Task Multiple_subjects_or_no_subjects_rejected()
    {
        var learner = await SeedLearnerAsync();
        var (bookingId, _) = await SeedBookingAsync(learner);
        var attemptId = await SeedExamAttemptAsync(learner);
        var service = CreateService();

        // No subject
        var exNone = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.FileComplaintAsync(learner, new FileComplaintRequest(
                ComplaintTypeDto.MentorNoShow,
                "No subject provided")));
        Assert.Equal(ApiError.Codes.ComplaintInvalidSubject, exNone.Code);

        // Multiple subjects
        var exMultiple = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.FileComplaintAsync(learner, new FileComplaintRequest(
                ComplaintTypeDto.MentorNoShow,
                "Two subjects provided",
                BookingId: bookingId,
                AttemptId: attemptId)));
        Assert.Equal(ApiError.Codes.ComplaintInvalidSubject, exMultiple.Code);
    }

    [Fact]
    public async Task Learner_complaint_history_hides_internal_notes()
    {
        var adminId = await SeedAdminAsync();
        var learner = await SeedLearnerAsync();
        var (bookingId, _) = await SeedBookingAsync(learner);
        var service = CreateService();

        var complaint = await service.FileComplaintAsync(learner, new FileComplaintRequest(
            ComplaintTypeDto.MentorNoShow,
            "Mentor was 20 minutes late.",
            BookingId: bookingId));

        // Admin resolves complaint with internal notes
        const string secretInternalNote = "INVESTIGATION: Mentor admitted oversleeping. Issue warning.";
        const string publicResolution = "We have refunded 1 credit for the session.";

        await service.ResolveComplaintAsync(adminId, complaint.ComplaintId, new ResolveComplaintRequest(
            Resolution: publicResolution,
            InternalNotes: secretInternalNote,
            GrantCredits: 1));

        // Learner queries their complaints
        var learnerComplaints = await service.GetLearnerComplaintsAsync(learner);
        var item = Assert.Single(learnerComplaints);

        Assert.Equal(complaint.ComplaintId, item.ComplaintId);
        Assert.Equal(publicResolution, item.Resolution);
        Assert.Equal(1, item.GrantedCredits);
        // The DTO has no property for internal notes at all!
        // Also check Admin detail HAS the internal notes
        var adminDetail = await service.GetAdminComplaintDetailAsync(complaint.ComplaintId);
        Assert.NotNull(adminDetail);
        Assert.Equal(secretInternalNote, adminDetail.InternalNotes);
    }

    [Fact]
    public async Task Admin_resolving_complaint_with_credit_grant_writes_admin_grant_ledger_row_and_updates_balance()
    {
        var adminId = await SeedAdminAsync();
        var learner = await SeedLearnerAsync();
        var (bookingId, _) = await SeedBookingAsync(learner);
        var service = CreateService();

        var complaint = await service.FileComplaintAsync(learner, new FileComplaintRequest(
            ComplaintTypeDto.MentorNoShow,
            "Mentor never joined.",
            BookingId: bookingId));

        var resolveResult = await service.ResolveComplaintAsync(adminId, complaint.ComplaintId, new ResolveComplaintRequest(
            Resolution: "Confirmed mentor absence. 1 credit granted as make-good.",
            InternalNotes: "Admin verified meet room logs.",
            GrantCredits: 1));

        Assert.Equal(ComplaintStatusDto.Resolved, resolveResult.Status);
        Assert.NotNull(resolveResult.ResolvedByGrantTxnId);
        Assert.Equal(1, resolveResult.GrantedCredits);

        // Verify CreditTransactions ledger row
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var txn = await db.CreditTransactions.SingleOrDefaultAsync(t => t.TxnId == resolveResult.ResolvedByGrantTxnId);
            Assert.NotNull(txn);
            Assert.Equal(learner, txn.UserId);
            Assert.Equal("admin_grant", txn.Reason);
            Assert.Equal(1, txn.Delta);
            Assert.Equal(adminId, txn.AdminUserId);
            Assert.NotNull(txn.Note);

            // Verify wallet balance is 1
            var balance = await db.vw_UserCreditBalances
                .Where(b => b.UserId == learner)
                .Select(b => b.Balance)
                .SingleOrDefaultAsync();
            Assert.Equal(1, balance);
        }
    }

    [Fact]
    public async Task Admin_resolving_without_credits_resolves_without_ledger_row()
    {
        var adminId = await SeedAdminAsync();
        var learner = await SeedLearnerAsync();
        var orderId = await SeedOrderAsync(learner);
        var service = CreateService();

        var complaint = await service.FileComplaintAsync(learner, new FileComplaintRequest(
            ComplaintTypeDto.PaymentIssue,
            "Clarification on payment receipt.",
            OrderId: orderId));

        var resolveResult = await service.ResolveComplaintAsync(adminId, complaint.ComplaintId, new ResolveComplaintRequest(
            Resolution: "Receipt resent to your email.",
            InternalNotes: "No credit needed, informational reply.",
            GrantCredits: 0));

        Assert.Equal(ComplaintStatusDto.Resolved, resolveResult.Status);
        Assert.Null(resolveResult.ResolvedByGrantTxnId);
        Assert.Null(resolveResult.GrantedCredits);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var txnCount = await db.CreditTransactions.CountAsync(t => t.UserId == learner);
            Assert.Equal(0, txnCount);
        }
    }

    [Fact]
    public async Task Status_transitions_work_correctly()
    {
        var adminId = await SeedAdminAsync();
        var learner = await SeedLearnerAsync();
        var (bookingId, _) = await SeedBookingAsync(learner);
        var service = CreateService();

        var complaint = await service.FileComplaintAsync(learner, new FileComplaintRequest(
            ComplaintTypeDto.MentorQuality,
            "Audio crackled continuously.",
            BookingId: bookingId));

        Assert.Equal(ComplaintStatusDto.Open, complaint.Status);

        // Open -> Reviewing
        var reviewing = await service.SetComplaintStatusAsync(adminId, complaint.ComplaintId, ComplaintStatus.Reviewing);
        Assert.Equal(ComplaintStatusDto.Reviewing, reviewing.Status);

        // Reviewing -> Open
        var reopened = await service.SetComplaintStatusAsync(adminId, complaint.ComplaintId, ComplaintStatus.Open);
        Assert.Equal(ComplaintStatusDto.Open, reopened.Status);

        // Cannot set status directly to Resolved via SetComplaintStatusAsync
        await Assert.ThrowsAsync<ComplaintException>(() =>
            service.SetComplaintStatusAsync(adminId, complaint.ComplaintId, ComplaintStatus.Resolved));

        // Resolve
        var resolved = await service.ResolveComplaintAsync(adminId, complaint.ComplaintId, new ResolveComplaintRequest(
            Resolution: "Reviewed session recording. Granted 1 credit.",
            InternalNotes: "Audio issue verified.",
            GrantCredits: 1));
        Assert.Equal(ComplaintStatusDto.Resolved, resolved.Status);

        // Once resolved, cannot change status back
        await Assert.ThrowsAsync<ComplaintException>(() =>
            service.SetComplaintStatusAsync(adminId, complaint.ComplaintId, ComplaintStatus.Reviewing));

        // Cannot resolve again
        await Assert.ThrowsAsync<ComplaintException>(() =>
            service.ResolveComplaintAsync(adminId, complaint.ComplaintId, new ResolveComplaintRequest(
                Resolution: "Again",
                InternalNotes: "Again",
                GrantCredits: 1)));
    }

    [Fact]
    public async Task Trigger_trg_Complaints_GrantMustBeAdminGrant_enforces_admin_grant_reason()
    {
        var learner = await SeedLearnerAsync();
        var (bookingId, _) = await SeedBookingAsync(learner);

        await using var db = DbContextFactory.Create(database.ConnectionString);

        // Create a non-admin_grant transaction for the user (e.g. purchase)
        var orderId = await SeedOrderAsync(learner);
        var txn = new CreditTransaction
        {
            UserId = learner,
            Reason = "purchase",
            Delta = 5,
            OrderId = orderId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.CreditTransactions.Add(txn);
        await db.SaveChangesAsync();

        // Attempt to insert/link this non-admin_grant txn directly into Complaints
        var complaint = new Complaint
        {
            UserId = learner,
            Type = "mentor_no_show",
            Reason = "Test trigger",
            Status = "resolved",
            BookingId = bookingId,
            ResolvedByGrantTxnId = txn.TxnId,
            ResolvedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Complaints.Add(complaint);

        // Should throw SQL exception from trigger 50023
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ResolvedByGrantTxnId must reference an admin_grant transaction", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    public async Task Eligible_subjects_returns_learner_bookings_attempts_and_orders()
    {
        var learner = await SeedLearnerAsync();
        var (bookingId, _) = await SeedBookingAsync(learner);
        var attemptId = await SeedExamAttemptAsync(learner);
        var orderId = await SeedOrderAsync(learner);

        var service = CreateService();
        var eligible = await service.GetEligibleSubjectsAsync(learner);

        Assert.Contains(eligible.Bookings, b => b.BookingId == bookingId);
        Assert.Contains(eligible.Attempts, a => a.AttemptId == attemptId);
        Assert.Contains(eligible.Orders, o => o.OrderId == orderId);
    }

    [Fact]
    public async Task Learners_only_see_their_own_complaints_in_history()
    {
        var learner1 = await SeedLearnerAsync("Learner 1");
        var learner2 = await SeedLearnerAsync("Learner 2");
        var (booking1, _) = await SeedBookingAsync(learner1);
        var (booking2, _) = await SeedBookingAsync(learner2);

        var service = CreateService();
        var c1 = await service.FileComplaintAsync(learner1, new FileComplaintRequest(
            ComplaintTypeDto.MentorNoShow, "Issue 1", BookingId: booking1));
        var c2 = await service.FileComplaintAsync(learner2, new FileComplaintRequest(
            ComplaintTypeDto.MentorNoShow, "Issue 2", BookingId: booking2));

        var history1 = await service.GetLearnerComplaintsAsync(learner1);
        var history2 = await service.GetLearnerComplaintsAsync(learner2);

        Assert.Contains(history1, c => c.ComplaintId == c1.ComplaintId);
        Assert.DoesNotContain(history1, c => c.ComplaintId == c2.ComplaintId);

        Assert.Contains(history2, c => c.ComplaintId == c2.ComplaintId);
        Assert.DoesNotContain(history2, c => c.ComplaintId == c1.ComplaintId);
    }

    [Fact]
    public async Task Validation_bounds_on_reason_resolution_notes_and_credits_are_enforced()
    {
        var adminId = await SeedAdminAsync();
        var learner = await SeedLearnerAsync();
        var (bookingId, _) = await SeedBookingAsync(learner);
        var service = CreateService();

        // 1. Reason too long (> 4000)
        var longReason = new string('a', 4001);
        var exReason = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.FileComplaintAsync(learner, new FileComplaintRequest(
                ComplaintTypeDto.MentorNoShow, longReason, BookingId: bookingId)));
        Assert.Equal(ApiError.Codes.Validation, exReason.Code);

        // File valid complaint for resolution tests
        var complaint = await service.FileComplaintAsync(learner, new FileComplaintRequest(
            ComplaintTypeDto.MentorNoShow, "Valid reason", BookingId: bookingId));

        // 2. Resolution too long (> 4000)
        var exRes = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.ResolveComplaintAsync(adminId, complaint.ComplaintId, new ResolveComplaintRequest(
                Resolution: new string('b', 4001), InternalNotes: "Valid note", GrantCredits: 1)));
        Assert.Equal(ApiError.Codes.Validation, exRes.Code);

        // 3. Internal notes too long (> 4000)
        var exNotes = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.ResolveComplaintAsync(adminId, complaint.ComplaintId, new ResolveComplaintRequest(
                Resolution: "Valid resolution", InternalNotes: new string('c', 4001), GrantCredits: 1)));
        Assert.Equal(ApiError.Codes.Validation, exNotes.Code);

        // 4. Grant credits > 20
        var exCredits = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.ResolveComplaintAsync(adminId, complaint.ComplaintId, new ResolveComplaintRequest(
                Resolution: "Valid resolution", InternalNotes: "Valid note", GrantCredits: 21)));
        Assert.Equal(ApiError.Codes.Validation, exCredits.Code);

        // 5. Admin ID <= 0
        var exAdmin = await Assert.ThrowsAsync<ComplaintException>(() =>
            service.ResolveComplaintAsync(0, complaint.ComplaintId, new ResolveComplaintRequest(
                Resolution: "Valid resolution", InternalNotes: "Valid note", GrantCredits: 1)));
        Assert.Equal(ApiError.Codes.Validation, exAdmin.Code);

        // 6. Non-existent complaint ID throws ComplaintNotFoundException
        await Assert.ThrowsAsync<ComplaintNotFoundException>(() =>
            service.ResolveComplaintAsync(adminId, 999999, new ResolveComplaintRequest(
                Resolution: "Valid resolution", InternalNotes: "Valid note", GrantCredits: 1)));

        await Assert.ThrowsAsync<ComplaintNotFoundException>(() =>
            service.SetComplaintStatusAsync(adminId, 999999, ComplaintStatus.Reviewing));
    }

    [Fact]
    public async Task Search_and_status_filtering_works_in_admin_complaints()
    {
        var adminId = await SeedAdminAsync();
        var learner = await SeedLearnerAsync("Special Learner");
        var (bookingId, _) = await SeedBookingAsync(learner);
        var service = CreateService();

        var complaint = await service.FileComplaintAsync(learner, new FileComplaintRequest(
            ComplaintTypeDto.MentorNoShow, "Specific reason", BookingId: bookingId));

        // Search by #C-id, C-id, #id, id
        var res1 = await service.GetAdminComplaintsAsync(q: $"#C-{complaint.ComplaintId}");
        Assert.Contains(res1.Complaints, c => c.ComplaintId == complaint.ComplaintId);

        var res2 = await service.GetAdminComplaintsAsync(q: $"c-{complaint.ComplaintId}");
        Assert.Contains(res2.Complaints, c => c.ComplaintId == complaint.ComplaintId);

        var res3 = await service.GetAdminComplaintsAsync(q: $"#{complaint.ComplaintId}");
        Assert.Contains(res3.Complaints, c => c.ComplaintId == complaint.ComplaintId);

        var res4 = await service.GetAdminComplaintsAsync(q: $"{complaint.ComplaintId}");
        Assert.Contains(res4.Complaints, c => c.ComplaintId == complaint.ComplaintId);

        // Search by learner name substring
        var resName = await service.GetAdminComplaintsAsync(q: "special learner");
        Assert.Contains(resName.Complaints, c => c.ComplaintId == complaint.ComplaintId);

        // Filter by status=pending
        var resPending = await service.GetAdminComplaintsAsync(status: "pending");
        Assert.Contains(resPending.Complaints, c => c.ComplaintId == complaint.ComplaintId);

        // Resolve complaint
        await service.ResolveComplaintAsync(adminId, complaint.ComplaintId, new ResolveComplaintRequest(
            Resolution: "Done", InternalNotes: "Done", GrantCredits: 0));

        // Now filter by status=pending should not contain it
        var resPendingAfter = await service.GetAdminComplaintsAsync(status: "pending");
        Assert.DoesNotContain(resPendingAfter.Complaints, c => c.ComplaintId == complaint.ComplaintId);

        // Filter by status=resolved should contain it
        var resResolved = await service.GetAdminComplaintsAsync(status: "resolved");
        Assert.Contains(resResolved.Complaints, c => c.ComplaintId == complaint.ComplaintId);
    }
}
