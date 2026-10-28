using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<AdminMentorSlotDto>>($"api/admin/mentors/{mentorId}/slots", bearerToken, cancellationToken);

    public Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, CreateMentorSlotRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<CreateMentorSlotRequest, AdminMentorSlotDto>($"api/admin/mentors/{mentorId}/slots", request, bearerToken, cancellationToken);

    public async Task CloseMentorSlotAsync(int mentorId, int slotId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/mentors/{mentorId}/slots/{slotId}/close");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(CancellationToken cancellationToken = default) =>
        GetAdminMentorsAsync(string.Empty, cancellationToken);

    public Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, CancellationToken cancellationToken = default) =>
        GetAdminMentorByIdAsync(mentorId, string.Empty, cancellationToken);

    public Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, CancellationToken cancellationToken = default) =>
        CreateMentorAsync(request, string.Empty, cancellationToken);

    public Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, CancellationToken cancellationToken = default) =>
        UpdateMentorAsync(mentorId, request, string.Empty, cancellationToken);

    public Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, CancellationToken cancellationToken = default) =>
        GetAdminMentorSlotsAsync(mentorId, string.Empty, cancellationToken);

    public Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, CreateMentorSlotRequest request, CancellationToken cancellationToken = default) =>
        CreateMentorSlotAsync(mentorId, request, string.Empty, cancellationToken);

    public Task CloseMentorSlotAsync(int mentorId, int slotId, CancellationToken cancellationToken = default) =>
        CloseMentorSlotAsync(mentorId, slotId, string.Empty, cancellationToken);
}
