
namespace Contracts;

public interface ISupportClient
{
    Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetLearnerComplaintsAsync(cancellationToken);
    Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(CancellationToken cancellationToken = default);
    Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetEligibleSubjectsAsync(cancellationToken);
    Task<LearnerComplaintDto> FileComplaintAsync(FileComplaintRequest request, CancellationToken cancellationToken = default);
    Task<LearnerComplaintDto> FileComplaintAsync(FileComplaintRequest request, string bearerToken, CancellationToken cancellationToken = default) => FileComplaintAsync(request, cancellationToken);
}
