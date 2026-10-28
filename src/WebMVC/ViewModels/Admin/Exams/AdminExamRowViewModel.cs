using Contracts;
namespace WebMVC.Models;

public sealed class AdminExamRowViewModel(AdminExamListItemDto exam)
{
    public AdminExamListItemDto Exam { get; } = exam;
    public string CertificateCode { get; } = exam.CertCode.ToUpperInvariant();
    public string QuestionDisplay { get; } = exam.QuestionCount.HasValue
        ? $"{exam.ActualQuestionCount}/{exam.QuestionCount.Value}"
        : $"{exam.ActualQuestionCount}";

    public string StatusBadgeClass { get; } = exam.Status switch
    {
        ExamStatusDto.Published => "badge badge--success",
        ExamStatusDto.Draft => "badge badge--warning",
        _ => "badge"
    };

    public string PurposeDisplay { get; } = exam.Purpose switch
    {
        ExamPurposeDto.Mock => "MOCK",
        ExamPurposeDto.Placement => "PLACEMENT",
        ExamPurposeDto.SpeakingPractice => "SPEAKING",
        ExamPurposeDto.WritingPractice => "WRITING",
        _ => exam.Purpose.ToString().ToUpperInvariant()
    };

    public bool IsAudioApplicable { get; } = exam.Purpose is not ExamPurposeDto.WritingPractice
        and not ExamPurposeDto.SpeakingPractice
        && !string.Equals(exam.AudioStatus, "N/A", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(exam.AudioStatus)
        && exam.AudioStatus != "—";

    public string DateDisplay { get; } = exam.UpdatedAt?.ToString("dd MMM yyyy") ?? exam.CreatedAt.ToString("dd MMM yyyy");

}
