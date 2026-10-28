
namespace Contracts;

public interface IAdminComplaintsClient
{
    Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminComplaintsAsync(q, status, page, pageSize, cancellationToken);
    Task<AdminComplaintDetailDto> GetAdminComplaintDetailAsync(int complaintId, CancellationToken cancellationToken = default);
    Task<AdminComplaintDetailDto> GetAdminComplaintDetailAsync(int complaintId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminComplaintDetailAsync(complaintId, cancellationToken);
    Task<AdminComplaintDetailDto> SetAdminComplaintStatusAsync(int complaintId, UpdateComplaintStatusRequest request, CancellationToken cancellationToken = default);
    Task<AdminComplaintDetailDto> SetAdminComplaintStatusAsync(int complaintId, UpdateComplaintStatusRequest request, string bearerToken, CancellationToken cancellationToken = default) => SetAdminComplaintStatusAsync(complaintId, request, cancellationToken);
    Task<AdminComplaintDetailDto> ResolveAdminComplaintAsync(int complaintId, ResolveComplaintRequest request, CancellationToken cancellationToken = default);
    Task<AdminComplaintDetailDto> ResolveAdminComplaintAsync(int complaintId, ResolveComplaintRequest request, string bearerToken, CancellationToken cancellationToken = default) => ResolveAdminComplaintAsync(complaintId, request, cancellationToken);
}
