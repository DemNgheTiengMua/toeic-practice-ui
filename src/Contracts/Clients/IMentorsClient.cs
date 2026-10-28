
namespace Contracts;

public interface IMentorsClient
{
    Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default);
    Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetMentorCertificateOptionsAsync(cancellationToken);
    Task<MentorBookingDto> BookMentorAsync(int mentorId, BookMentorRequest request, CancellationToken cancellationToken = default);
    Task<MentorBookingDto> BookMentorAsync(int mentorId, BookMentorRequest request, string bearerToken, CancellationToken cancellationToken = default) => BookMentorAsync(mentorId, request, cancellationToken);
    Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, CancellationToken cancellationToken = default);
    Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, string bearerToken, CancellationToken cancellationToken = default) => CancelBookingAsync(bookingId, cancellationToken);
    Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, CancellationToken cancellationToken = default);
    Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, string bearerToken, CancellationToken cancellationToken = default) => GetBookingDetailAsync(bookingId, cancellationToken);
    Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetLearnerBookingsAsync(cancellationToken);
    Task<MentorReviewDto> RateBookingAsync(int bookingId, RateMentorRequest request, CancellationToken cancellationToken = default);
    Task<MentorReviewDto> RateBookingAsync(int bookingId, RateMentorRequest request, string bearerToken, CancellationToken cancellationToken = default) => RateBookingAsync(bookingId, request, cancellationToken);
}
