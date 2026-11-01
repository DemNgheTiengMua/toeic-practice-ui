using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IPracticeService
{
    Task<PracticeSectionsView> GetPracticeSectionsAsync(int userId, CancellationToken cancellationToken = default);

    Task<PracticeSessionInfo> StartSessionAsync(int userId, StartPracticeSessionInput input, CancellationToken cancellationToken = default);

    /// <summary>#155: a lesson's micro-check is graded on the server and writes the same mastery row a
    /// drill answer does, so a lesson is no longer a place where answers go unrecorded.</summary>
    Task<LessonMicroCheckAnswer> SubmitLessonMicroCheckAsync(int userId, int lessonId, int cardId, string selectedOption, CancellationToken cancellationToken = default);

    Task<PracticeSessionInfo> GetSessionInfoAsync(int userId, int sessionId, CancellationToken cancellationToken = default);

    Task<PracticeQuestionView> GetQuestionAsync(int userId, int sessionId, int displayOrder, CancellationToken cancellationToken = default);

    Task<string?> RevealHintAsync(int userId, int sessionId, int questionId, CancellationToken cancellationToken = default);

    Task<GradedPracticeAnswerResult> SubmitAnswerAsync(int userId, int sessionId, int questionId, SubmitPracticeAnswerInput input, CancellationToken cancellationToken = default);

    Task<PracticeSummary> GetSummaryAsync(int userId, int sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PracticeQuestionView>> GetSessionQuestionsAsync(int userId, int sessionId, CancellationToken cancellationToken = default);
    Task<PracticeSessionTake> GetSessionTakeAsync(int userId, int sessionId, int? displayOrder = null, CancellationToken cancellationToken = default);
}
