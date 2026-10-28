using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        var path = $"api/admin/complaints?{string.Join("&", query)}";
        return GetAuthorizedAsync<AdminComplaintsSummaryDto>(path, bearerToken, cancellationToken);
    }

    public Task<AdminComplaintDetailDto> GetAdminComplaintDetailAsync(int complaintId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminComplaintDetailDto>($"api/admin/complaints/{complaintId}", bearerToken, cancellationToken);

    public Task<AdminComplaintDetailDto> SetAdminComplaintStatusAsync(int complaintId, UpdateComplaintStatusRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<UpdateComplaintStatusRequest, AdminComplaintDetailDto>($"api/admin/complaints/{complaintId}/status", request, bearerToken, cancellationToken);

    public Task<AdminComplaintDetailDto> ResolveAdminComplaintAsync(int complaintId, ResolveComplaintRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<ResolveComplaintRequest, AdminComplaintDetailDto>($"api/admin/complaints/{complaintId}/resolve", request, bearerToken, cancellationToken);

    public Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminComplaintsAsync(q, status, page, pageSize, string.Empty, cancellationToken);

    public Task<AdminComplaintDetailDto> GetAdminComplaintDetailAsync(int complaintId, CancellationToken cancellationToken = default) =>
        GetAdminComplaintDetailAsync(complaintId, string.Empty, cancellationToken);

    public Task<AdminComplaintDetailDto> SetAdminComplaintStatusAsync(int complaintId, UpdateComplaintStatusRequest request, CancellationToken cancellationToken = default) =>
        SetAdminComplaintStatusAsync(complaintId, request, string.Empty, cancellationToken);

    public Task<AdminComplaintDetailDto> ResolveAdminComplaintAsync(int complaintId, ResolveComplaintRequest request, CancellationToken cancellationToken = default) =>
        ResolveAdminComplaintAsync(complaintId, request, string.Empty, cancellationToken);
}
