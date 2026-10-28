using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IReadOnlyList<LearnerComplaintDto>>("api/learner/complaints", bearerToken, cancellationToken);

    public Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<EligibleSubjectsDto>("api/learner/complaints/eligible-subjects", bearerToken, cancellationToken);

    public Task<LearnerComplaintDto> FileComplaintAsync(FileComplaintRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<FileComplaintRequest, LearnerComplaintDto>("api/learner/complaints", request, bearerToken, cancellationToken);

    public Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(CancellationToken cancellationToken = default) =>
        GetLearnerComplaintsAsync(string.Empty, cancellationToken);

    public Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(CancellationToken cancellationToken = default) =>
        GetEligibleSubjectsAsync(string.Empty, cancellationToken);

    public Task<LearnerComplaintDto> FileComplaintAsync(FileComplaintRequest request, CancellationToken cancellationToken = default) =>
        FileComplaintAsync(request, string.Empty, cancellationToken);
}
