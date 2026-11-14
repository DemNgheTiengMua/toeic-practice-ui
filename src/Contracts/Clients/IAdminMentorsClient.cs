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
    Task<IReadOnlyList<MentorWeeklySlotDto>> GetAdminMentorWeeklySlotsAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorWeeklySlotDto>> GetAdminMentorWeeklySlotsAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminMentorWeeklySlotsAsync(mentorId, cancellationToken);
    Task<IReadOnlyList<MentorWeeklySlotDto>> ReplaceMentorWeeklySlotsAsync(int mentorId, IReadOnlyList<MentorWeeklySlotInputDto> weeklySlots, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorWeeklySlotDto>> ReplaceMentorWeeklySlotsAsync(int mentorId, IReadOnlyList<MentorWeeklySlotInputDto> weeklySlots, string bearerToken, CancellationToken cancellationToken = default) => ReplaceMentorWeeklySlotsAsync(mentorId, weeklySlots, cancellationToken);
    Task<AdminMentorPayoutsReportDto> GetAdminMentorPayoutsAsync(DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default);
    Task<AdminMentorPayoutsReportDto> GetAdminMentorPayoutsAsync(DateOnly? from, DateOnly? to, string bearerToken, CancellationToken cancellationToken = default) => GetAdminMentorPayoutsAsync(from, to, cancellationToken);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetMentorCertificateOptionsAsync(cancellationToken);
    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminUsersAsync(q, status, page, pageSize, cancellationToken);
}
