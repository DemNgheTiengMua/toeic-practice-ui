using Contracts;

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

    // Admin management & payouts
    Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorWeeklySlotDto>> GetAdminMentorWeeklySlotsAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorWeeklySlotDto>> ReplaceMentorWeeklySlotsAsync(int mentorId, IReadOnlyList<MentorWeeklySlotInputDto> weeklySlots, CancellationToken cancellationToken = default);
    Task<AdminMentorPayoutsReportDto> GetAdminMentorPayoutsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorPayoutExportRow>> GetMentorPayoutExportRowsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetCertificateOptionsAsync(CancellationToken cancellationToken = default);
}
