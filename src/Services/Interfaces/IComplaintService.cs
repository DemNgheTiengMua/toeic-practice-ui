using System.Data;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using static Contracts.EnumMapping;
using Services.Credits;

namespace Services;

public interface IComplaintService
{
    Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<LearnerComplaintDto> FileComplaintAsync(
        int userId,
        FileComplaintRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(
        string? q = null,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<AdminComplaintDetailDto?> GetAdminComplaintDetailAsync(
        int complaintId,
        CancellationToken cancellationToken = default);

    Task<AdminComplaintDetailDto> SetComplaintStatusAsync(
        int adminUserId,
        int complaintId,
        ComplaintStatus status,
        CancellationToken cancellationToken = default);

    Task<AdminComplaintDetailDto> ResolveComplaintAsync(
        int adminUserId,
        int complaintId,
        ResolveComplaintRequest request,
        CancellationToken cancellationToken = default);
}
