using Contracts;

namespace WebMVC.Tests;

/// <summary>#159: a stand-in for the Writing/Speaking door list. Controller tests that only exercise
/// one action pass this in rather than describing a whole catalog.</summary>
internal sealed class SkillPracticeStub : ISkillPracticeClient
{
    public SkillPracticeExamListDto List { get; set; } =
        new(new CertificateOptionDto("TOEIC", "TOEIC"), 3, []);

    public Task<SkillPracticeExamListDto> GetWritingExamsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(List);

    public Task<SkillPracticeExamListDto> GetSpeakingExamsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(List);
}
