
namespace Contracts;

public interface IAdminMentorsClient
{
    Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminMentorsAsync(cancellationToken);
    Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminMentorByIdAsync(mentorId, cancellationToken);
    Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateMentorAsync(request, cancellationToken);
    Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateMentorAsync(mentorId, request, cancellationToken);
    Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminMentorSlotsAsync(mentorId, cancellationToken);
    Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, CreateMentorSlotRequest request, CancellationToken cancellationToken = default);
    Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, CreateMentorSlotRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateMentorSlotAsync(mentorId, request, cancellationToken);
    Task CloseMentorSlotAsync(int mentorId, int slotId, CancellationToken cancellationToken = default);
    Task CloseMentorSlotAsync(int mentorId, int slotId, string bearerToken, CancellationToken cancellationToken = default) => CloseMentorSlotAsync(mentorId, slotId, cancellationToken);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetMentorCertificateOptionsAsync(cancellationToken);
    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminUsersAsync(q, status, page, pageSize, cancellationToken);
}
