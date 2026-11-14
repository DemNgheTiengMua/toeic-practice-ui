using System.Net.Http.Json;

namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<AdminMentorListItemDto>>("api/admin/mentors", bearerToken, cancellationToken);

    public Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<MentorSummaryDto>($"api/admin/mentors/{mentorId}", bearerToken, cancellationToken);

    public Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<CreateMentorProfileRequest, MentorSummaryDto>("api/admin/mentors", request, bearerToken, cancellationToken);

    public async Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"api/admin/mentors/{mentorId}")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MentorSummaryDto>(Json, cancellationToken))!;
    }

    public Task<IReadOnlyList<MentorWeeklySlotDto>> GetAdminMentorWeeklySlotsAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<MentorWeeklySlotDto>>($"api/admin/mentors/{mentorId}/weekly-slots", bearerToken, cancellationToken);

    public async Task<IReadOnlyList<MentorWeeklySlotDto>> ReplaceMentorWeeklySlotsAsync(int mentorId, IReadOnlyList<MentorWeeklySlotInputDto> weeklySlots, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"api/admin/mentors/{mentorId}/weekly-slots")
        {
            Content = JsonContent.Create(new ReplaceMentorWeeklySlotsRequest(weeklySlots), options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<MentorWeeklySlotDto>>(Json, cancellationToken))!;
    }

    public Task<AdminMentorPayoutsReportDto> GetAdminMentorPayoutsAsync(DateOnly? from, DateOnly? to, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (from.HasValue) query.Add($"from={from.Value:yyyy-MM-dd}");
        if (to.HasValue) query.Add($"to={to.Value:yyyy-MM-dd}");
        var uri = "api/admin/mentors/payouts" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return GetAuthorizedAsync<AdminMentorPayoutsReportDto>(uri, bearerToken, cancellationToken);
    }

    public Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(CancellationToken cancellationToken = default) =>
        GetAdminMentorsAsync(string.Empty, cancellationToken);

    public Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, CancellationToken cancellationToken = default) =>
        GetAdminMentorByIdAsync(mentorId, string.Empty, cancellationToken);

    public Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, CancellationToken cancellationToken = default) =>
        CreateMentorAsync(request, string.Empty, cancellationToken);

    public Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, CancellationToken cancellationToken = default) =>
        UpdateMentorAsync(mentorId, request, string.Empty, cancellationToken);

    public Task<IReadOnlyList<MentorWeeklySlotDto>> GetAdminMentorWeeklySlotsAsync(int mentorId, CancellationToken cancellationToken = default) =>
        GetAdminMentorWeeklySlotsAsync(mentorId, string.Empty, cancellationToken);

    public Task<IReadOnlyList<MentorWeeklySlotDto>> ReplaceMentorWeeklySlotsAsync(int mentorId, IReadOnlyList<MentorWeeklySlotInputDto> weeklySlots, CancellationToken cancellationToken = default) =>
        ReplaceMentorWeeklySlotsAsync(mentorId, weeklySlots, string.Empty, cancellationToken);

    public Task<AdminMentorPayoutsReportDto> GetAdminMentorPayoutsAsync(DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default) =>
        GetAdminMentorPayoutsAsync(from, to, string.Empty, cancellationToken);
}
