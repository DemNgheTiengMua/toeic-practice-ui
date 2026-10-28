using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public interface IMentorService
{
    // Learner browsing
    Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default);
    Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default);

    // Learner booking & cancellation
    Task<MentorBookingDto> BookMentorAsync(int mentorId, int userId, BookMentorRequest request, CancellationToken cancellationToken = default);
    Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, int userId, CancellationToken cancellationToken = default);
    Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(int userId, CancellationToken cancellationToken = default);
    Task<MentorReviewDto> RateBookingAsync(int bookingId, int userId, RateMentorRequest request, CancellationToken cancellationToken = default);

    // Admin management
    Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, int adminUserId, CreateMentorSlotRequest request, CancellationToken cancellationToken = default);
    Task CloseMentorSlotAsync(int mentorId, int slotId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetCertificateOptionsAsync(CancellationToken cancellationToken = default);
}
