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

    /// <summary>
    /// #220: the word in the badge. The raw enum used to be the cell's text node, which rendered
    /// "Published" only because the member happens to be spelled like prose - "Standby" would have
    /// shipped as "Standby" and a two-word member as "PendingReview".
    /// </summary>
    public string StatusLabel { get; } = DisplayLabels.LabelFor(exam.Status, NullLog);

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

    private static readonly Microsoft.Extensions.Logging.ILogger NullLog =
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
