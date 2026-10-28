
namespace Contracts;

public interface ILearnerContextClient
{
    Task<LearnerContextDto> GetLearnerContextAsync(CancellationToken cancellationToken = default);
    Task<LearnerContextDto> GetLearnerContextAsync(string bearerToken, CancellationToken cancellationToken = default) => GetLearnerContextAsync(cancellationToken);
    Task SwitchCertificateAsync(string code, CancellationToken cancellationToken = default);
    Task SwitchCertificateAsync(string bearerToken, string code, CancellationToken cancellationToken = default) => SwitchCertificateAsync(code, cancellationToken);
    Task<LearnerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<LearnerDashboardDto> GetDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) => GetDashboardAsync(cancellationToken);
    Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) => GetDashboardInsightAsync(certId, cancellationToken);
    Task RetryDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default);
    Task RetryDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) => RetryDashboardInsightAsync(certId, cancellationToken);
}
