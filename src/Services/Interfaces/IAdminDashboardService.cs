using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IAdminDashboardService
{
    /// <summary>
    /// #351: <paramref name="month"/> names the calendar month the period-scoped figures describe; its
    /// day is ignored. Absent, it means the month the clock is in, which is the behaviour every caller had
    /// before the parameter existed. The two <c>…Today</c> figures never follow it.
    /// </summary>
    Task<AdminDashboardDto> GetDashboardMetricsAsync(DateOnly? month = null, CancellationToken cancellationToken = default);
}
