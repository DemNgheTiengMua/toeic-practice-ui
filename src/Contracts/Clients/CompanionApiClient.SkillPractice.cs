namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<SkillPracticeExamListDto> GetWritingExamsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<SkillPracticeExamListDto>("api/learner/skill-practice/writing", bearerToken, cancellationToken);

    public Task<SkillPracticeExamListDto> GetSpeakingExamsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<SkillPracticeExamListDto>("api/learner/skill-practice/speaking", bearerToken, cancellationToken);

    Task<SkillPracticeExamListDto> ISkillPracticeClient.GetWritingExamsAsync(CancellationToken cancellationToken) =>
        GetWritingExamsAsync(null!, cancellationToken);

    Task<SkillPracticeExamListDto> ISkillPracticeClient.GetSpeakingExamsAsync(CancellationToken cancellationToken) =>
        GetSpeakingExamsAsync(null!, cancellationToken);
}
