using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

using Services.Credits;

namespace Services;

public interface ILearnerService
{
    Task<LearnerContext> GetContextAsync(int userId, CancellationToken cancellationToken = default);

    Task SwitchCertificateAsync(int userId, string certificateCode, CancellationToken cancellationToken = default);

    Task<LearnerDashboard> GetDashboardAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>The certificate id every learner query filters on. Later slices (practice, attempts,
    /// history, weakness analysis) call this instead of taking a certificate from the request.</summary>
    Task<int> GetActiveCertIdAsync(int userId, CancellationToken cancellationToken = default);
}
