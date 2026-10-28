using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IAdminDashboardService
{
    Task<AdminDashboardDto> GetDashboardMetricsAsync(CancellationToken cancellationToken = default);
}
