using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public async Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(certCode)) query.Add($"certCode={Uri.EscapeDataString(certCode)}");
        if (!string.IsNullOrWhiteSpace(skill)) query.Add($"skill={Uri.EscapeDataString(skill)}");
        var uri = "api/mentors" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        using var response = await http.GetAsync(uri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<MentorSummaryDto>>(Json, cancellationToken))!;
    }

    public async Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/mentors/{mentorId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MentorDetailDto>(Json, cancellationToken))!;
    }

    public Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<MentorCertificateOptionDto>>("api/admin/mentors/options", bearerToken, cancellationToken);

    public Task<MentorBookingDto> BookMentorAsync(int mentorId, BookMentorRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<BookMentorRequest, MentorBookingDto>($"api/mentors/{mentorId}/book", request, bearerToken, cancellationToken);

    public Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<object, CancelBookingResultDto>($"api/mentors/bookings/{bookingId}/cancel", new { }, bearerToken, cancellationToken);

    public Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<MentorBookingDto>($"api/mentors/bookings/{bookingId}", bearerToken, cancellationToken);

    public Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<MentorBookingDto>>("api/mentors/bookings", bearerToken, cancellationToken);

    public Task<MentorReviewDto> RateBookingAsync(int bookingId, RateMentorRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<RateMentorRequest, MentorReviewDto>($"api/mentors/bookings/{bookingId}/rate", request, bearerToken, cancellationToken);

    public Task<MentorReviewDto> RateBookingAsync(int bookingId, RateMentorRequest request, CancellationToken cancellationToken = default) =>
        RateBookingAsync(bookingId, request, string.Empty, cancellationToken);

    public Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(CancellationToken cancellationToken = default) =>
        GetMentorCertificateOptionsAsync(string.Empty, cancellationToken);

    public Task<MentorBookingDto> BookMentorAsync(int mentorId, BookMentorRequest request, CancellationToken cancellationToken = default) =>
        BookMentorAsync(mentorId, request, string.Empty, cancellationToken);

    public Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
        CancelBookingAsync(bookingId, string.Empty, cancellationToken);

    public Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, CancellationToken cancellationToken = default) =>
        GetBookingDetailAsync(bookingId, string.Empty, cancellationToken);

    public Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(CancellationToken cancellationToken = default) =>
        GetLearnerBookingsAsync(string.Empty, cancellationToken);
}
