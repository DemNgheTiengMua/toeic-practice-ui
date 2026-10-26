using Contracts;

namespace WebMVC.Models;

public sealed record ExamReviewQuestionViewModel(
    AttemptReviewQuestionDto Question,
    string Status,
    ExamAudioPlayerModel? AudioPlayer,
    IReadOnlyList<ExamReviewOptionViewModel> Options);
