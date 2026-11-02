
namespace Contracts;

/// <summary>#159: the list behind the Writing and Speaking doors. The bearer is attached by the
/// handler pipeline (ADR-0002), so the tokenless overload matches the other clients' shape.</summary>
public interface ISkillPracticeClient
{
    Task<SkillPracticeExamListDto> GetWritingExamsAsync(CancellationToken cancellationToken = default);
    Task<SkillPracticeExamListDto> GetWritingExamsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetWritingExamsAsync(cancellationToken);
    Task<SkillPracticeExamListDto> GetSpeakingExamsAsync(CancellationToken cancellationToken = default);
    Task<SkillPracticeExamListDto> GetSpeakingExamsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetSpeakingExamsAsync(cancellationToken);
}
