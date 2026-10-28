using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<LearnerContextDto> GetLearnerContextAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LearnerContextDto>("api/learner/context", bearerToken, cancellationToken);

    public async Task SwitchCertificateAsync(string bearerToken, string code, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "api/learner/active-certificate")
        {
            Content = JsonContent.Create(new SwitchCertificateRequest(code), options: Json),
        };
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<LearnerDashboardDto> GetDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LearnerDashboardDto>("api/learner/dashboard", bearerToken, cancellationToken);

    public Task<LearnerContextDto> GetLearnerContextAsync(CancellationToken cancellationToken = default) =>
        GetLearnerContextAsync(string.Empty, cancellationToken);

    public Task SwitchCertificateAsync(string code, CancellationToken cancellationToken = default) =>
        SwitchCertificateAsync(string.Empty, code, cancellationToken);

    public Task<LearnerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) =>
        GetDashboardAsync(string.Empty, cancellationToken);
}
