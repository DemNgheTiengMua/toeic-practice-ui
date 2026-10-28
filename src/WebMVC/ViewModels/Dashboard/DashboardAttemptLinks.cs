using Contracts;
namespace WebMVC.Models;

public sealed record DashboardAttemptLinks
{
    public string Resume { get; }
    public string Result { get; }
    public DashboardAttemptLinks(RecentAttemptDto attempt)
    {
        var prefix = attempt.Purpose switch {
            ExamPurposeDto.Placement => "/placement", ExamPurposeDto.SpeakingPractice => "/speaking",
            ExamPurposeDto.WritingPractice => "/writing", _ => "/exams"
        };
        Resume = $"{prefix}/take/{attempt.AttemptId}";
        var action = attempt.Purpose is ExamPurposeDto.SpeakingPractice or ExamPurposeDto.WritingPractice ? "review" : "result";
        Result = $"{prefix}/{action}/{attempt.AttemptId}";
    }
}
