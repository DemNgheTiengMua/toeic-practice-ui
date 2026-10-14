using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class PracticeException(string code, string message) : Exception(message)
{
    public const string SectionUnavailable = "section_unavailable";
    public const string ModalityNotAllowed = "modality_not_allowed";
    public const string NoQuestionsAvailable = "no_questions_available";
    public const string SessionNotFound = "session_not_found";
    public const string QuestionNotFound = "question_not_found";
    public const string InvalidOption = "invalid_option";
    public const string SessionAlreadyFinished = "session_already_finished";

    public string Code { get; } = code;
}

public interface IPracticeService
{
    Task<PracticeSectionsView> GetPracticeSectionsAsync(int userId, CancellationToken cancellationToken = default);

    Task<PracticeSessionInfo> StartSessionAsync(int userId, StartPracticeSessionInput input, CancellationToken cancellationToken = default);

    Task<PracticeSessionInfo> GetSessionInfoAsync(int userId, int sessionId, CancellationToken cancellationToken = default);

    Task<PracticeQuestionView> GetQuestionAsync(int userId, int sessionId, int displayOrder, CancellationToken cancellationToken = default);

    Task<string?> RevealHintAsync(int userId, int sessionId, int questionId, CancellationToken cancellationToken = default);

    Task<GradedPracticeAnswerResult> SubmitAnswerAsync(int userId, int sessionId, int questionId, SubmitPracticeAnswerInput input, CancellationToken cancellationToken = default);

    Task<PracticeSummary> GetSummaryAsync(int userId, int sessionId, CancellationToken cancellationToken = default);
}

public sealed class PracticeService(CompanionDbContext db, ILearnerService learners) : IPracticeService
{
    public async Task<PracticeSectionsView> GetPracticeSectionsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .Select(c => new CertificateOption(c.Code, c.Name))
            .SingleAsync(cancellationToken);

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == activeCertId)
            .Include(s => s.Skill)
            .OrderBy(s => s.Skill.DisplayOrder)
            .ThenBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questionCounts = await db.Questions
            .AsNoTracking()
            .Where(q => q.Section.CertId == activeCertId && q.QuestionType == "mcq")
            .GroupBy(q => q.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SectionId, g => g.Count, cancellationToken);

        var accuracies = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .ToDictionaryAsync(v => v.SectionId, cancellationToken);

        var items = sections.Select(s =>
        {
            int answered = 0;
            int? accuracy = null;
            if (accuracies.TryGetValue(s.SectionId, out var acc) && acc.Answered is { } total && total > 0)
            {
                answered = total;
                accuracy = (int)Math.Round((double)(acc.Correct ?? 0) / total * 100);
            }

            questionCounts.TryGetValue(s.SectionId, out var available);
            return new PracticeSectionItem(
                s.SectionId,
                s.Skill.Code,
                s.Skill.Name,
                s.Modality,
                s.Code,
                s.Name,
                s.OptionCount,
                s.ExpectedQuestionCount ?? 0,
                available,
                accuracy,
                answered,
                s.DisplayOrder);
        }).ToList();

        var listening = items.Where(i => i.Modality == "listening").ToList();
        var reading = items.Where(i => i.Modality == "reading").ToList();
        var other = items.Where(i => i.Modality is not ("listening" or "reading")).ToList();

        // Weakest part: listening/reading section with lowest accuracy among those with at least 1 answer
        var weakest = items
            .Where(i => i.Modality is "listening" or "reading" && i.AccuracyPercentage.HasValue && i.AnsweredCount > 0)
            .OrderBy(i => i.AccuracyPercentage!.Value)
            .ThenByDescending(i => i.AnsweredCount)
            .FirstOrDefault();

        return new PracticeSectionsView(cert, listening, reading, other, weakest);
    }

    public async Task<PracticeSessionInfo> StartSessionAsync(int userId, StartPracticeSessionInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var section = await db.Sections
            .AsNoTracking()
            .Where(s => s.SectionId == input.SectionId && s.CertId == activeCertId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SectionUnavailable, $"Section {input.SectionId} is not available for active certificate.");

        if (section.Modality is not ("listening" or "reading"))
        {
            throw new PracticeException(PracticeException.ModalityNotAllowed, "Free practice is only available for listening and reading sections.");
        }

        var query = db.Questions
            .AsNoTracking()
            .Where(q => q.SectionId == section.SectionId && q.QuestionType == "mcq" && db.QuestionOptions.Any(o => o.QuestionId == q.QuestionId));

        if (input.QuestionSource == "wrong_before")
        {
            var wrongFromPractice = db.PracticeAnswers
                .Where(pa => pa.Session.UserId == userId && pa.SectionId == section.SectionId && pa.IsCorrect == false)
                .Select(pa => pa.QuestionId);
            var wrongFromAttempts = db.AttemptAnswers
                .Where(aa => aa.Attempt.UserId == userId && aa.Question.SectionId == section.SectionId && aa.IsCorrect == false)
                .Select(aa => aa.QuestionId);
            var wrongIds = wrongFromPractice.Union(wrongFromAttempts);
            query = query.Where(q => wrongIds.Contains(q.QuestionId));
        }
        else if (input.QuestionSource == "unseen")
        {
            var seenFromPractice = db.PracticeAnswers
                .Where(pa => pa.Session.UserId == userId && pa.SectionId == section.SectionId)
                .Select(pa => pa.QuestionId);
            var seenFromAttempts = db.AttemptAnswers
                .Where(aa => aa.Attempt.UserId == userId && aa.Question.SectionId == section.SectionId)
                .Select(aa => aa.QuestionId);
            var seenIds = seenFromPractice.Union(seenFromAttempts);
            query = query.Where(q => !seenIds.Contains(q.QuestionId));
        }

        var candidateQuestionIds = await query
            .OrderBy(q => q.DisplayOrder)
            .ThenBy(q => q.QuestionId)
            .Select(q => q.QuestionId)
            .ToListAsync(cancellationToken);

        if (candidateQuestionIds.Count == 0)
        {
            throw new PracticeException(PracticeException.NoQuestionsAvailable, "No questions match the selected criteria for this section.");
        }

        var takeCount = Math.Min(candidateQuestionIds.Count, input.QuestionCount);
        var selectedIds = candidateQuestionIds.Take(takeCount).ToList();

        var session = new PracticeSession
        {
            UserId = userId,
            SectionId = section.SectionId,
            Modality = section.Modality,
            QuestionCount = selectedIds.Count,
            Status = "in_progress",
            CorrectCount = 0,
            QuestionSource = input.QuestionSource,
            FeedbackMode = input.FeedbackMode,
            StartedAt = DateTimeOffset.UtcNow,
        };

        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < selectedIds.Count; i++)
        {
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                QuestionId = selectedIds[i],
                SectionId = section.SectionId,
                SelectedOptionId = null,
                IsCorrect = null,
                HintUsed = false,
                DisplayOrder = i + 1,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        return new PracticeSessionInfo(
            session.SessionId,
            session.SectionId,
            section.Code,
            section.Name,
            session.Modality,
            session.QuestionCount,
            session.Status,
            session.CorrectCount,
            session.QuestionSource,
            session.FeedbackMode,
            session.StartedAt,
            session.FinishedAt);
    }

    public async Task<PracticeSessionInfo> GetSessionInfoAsync(int userId, int sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.PracticeSessions
            .AsNoTracking()
            .Include(s => s.Section)
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SessionNotFound, $"Practice session {sessionId} was not found.");

        return new PracticeSessionInfo(
            session.SessionId,
            session.SectionId,
            session.Section.Code,
            session.Section.Name,
            session.Modality,
            session.QuestionCount,
            session.Status,
            session.CorrectCount,
            session.QuestionSource,
            session.FeedbackMode,
            session.StartedAt,
            session.FinishedAt);
    }

    public async Task<PracticeQuestionView> GetQuestionAsync(int userId, int sessionId, int displayOrder, CancellationToken cancellationToken = default)
    {
        var query = db.PracticeAnswers
            .AsNoTracking()
            .Include(pa => pa.Session)
                .ThenInclude(s => s.Section)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Group)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Category)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Where(pa => pa.SessionId == sessionId && pa.Session.UserId == userId);

        PracticeAnswer? answer;
        if (displayOrder <= 0)
        {
            answer = await query
                .Where(pa => pa.SelectedOptionId == null && pa.IsCorrect == null)
                .OrderBy(pa => pa.DisplayOrder)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await query.OrderBy(pa => pa.DisplayOrder).FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            answer = await query
                .Where(pa => pa.DisplayOrder == displayOrder)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (answer is null)
        {
            throw new PracticeException(PracticeException.QuestionNotFound, $"Question at index {displayOrder} was not found in session {sessionId}.");
        }

        var q = answer.Question;
        var group = q.Group;
        var isAnswered = answer.SelectedOptionId.HasValue || answer.IsCorrect.HasValue;

        var allOptions = await db.QuestionOptions
            .AsNoTracking()
            .Where(o => o.QuestionId == q.QuestionId)
            .OrderBy(o => o.Label)
            .ToListAsync(cancellationToken);

        var options = allOptions
            .Select(o => new PracticeQuestionOption(o.OptionId, o.Label, o.Text))
            .ToList();

        int? correctOptionId = null;
        string? explanation = null;
        string? transcript = null;

        if (isAnswered)
        {
            correctOptionId = allOptions.FirstOrDefault(o => o.IsCorrect)?.OptionId;
            explanation = q.Explanation;
            transcript = group.Transcript;
        }

        var hintText = answer.HintUsed ? q.Hint : null;

        return new PracticeQuestionView(
            sessionId,
            answer.Session.Section.Name,
            q.QuestionId,
            answer.DisplayOrder,
            answer.Session.QuestionCount,
            q.Stem,
            q.Category?.Name,
            hintText,
            answer.HintUsed,
            options,
            answer.SelectedOptionId,
            answer.IsCorrect,
            correctOptionId,
            explanation,
            transcript,
            group.Passage,
            group.AudioPath,
            group.ImagePath,
            group.ImageAlt);
    }

    public async Task<string?> RevealHintAsync(int userId, int sessionId, int questionId, CancellationToken cancellationToken = default)
    {
        var answer = await db.PracticeAnswers
            .Include(pa => pa.Session)
            .Include(pa => pa.Question)
            .Where(pa => pa.SessionId == sessionId && pa.Session.UserId == userId && pa.QuestionId == questionId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.QuestionNotFound, $"Question {questionId} was not found in session {sessionId}.");

        if (answer.Session.Status != "in_progress")
        {
            throw new PracticeException(PracticeException.SessionAlreadyFinished, "Hints cannot be revealed in a finished session.");
        }

        if (!answer.HintUsed)
        {
            answer.HintUsed = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        return answer.Question.Hint;
    }

    public async Task<GradedPracticeAnswerResult> SubmitAnswerAsync(int userId, int sessionId, int questionId, SubmitPracticeAnswerInput input, CancellationToken cancellationToken = default)
    {
        var answer = await db.PracticeAnswers
            .Include(pa => pa.Session)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Group)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Where(pa => pa.SessionId == sessionId && pa.Session.UserId == userId && pa.QuestionId == questionId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.QuestionNotFound, $"Question {questionId} was not found in session {sessionId}.");

        if (answer.Session.Status != "in_progress")
        {
            throw new PracticeException(PracticeException.SessionAlreadyFinished, "Answers cannot be submitted to a finished session.");
        }

        var q = answer.Question;
        var allOptions = await db.QuestionOptions
            .AsNoTracking()
            .Where(o => o.QuestionId == questionId)
            .ToListAsync(cancellationToken);

        int? correctOptionId = allOptions.FirstOrDefault(o => o.IsCorrect)?.OptionId;

        if (input.SelectedOptionId is { } optionId)
        {
            var option = allOptions.FirstOrDefault(o => o.OptionId == optionId)
                ?? throw new PracticeException(PracticeException.InvalidOption, $"Option {optionId} does not belong to question {questionId}.");

            answer.SelectedOptionId = optionId;
            answer.IsCorrect = option.IsCorrect;
        }
        else
        {
            answer.SelectedOptionId = null;
            answer.IsCorrect = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Update correct count on session
        var correctCount = await db.PracticeAnswers
            .CountAsync(pa => pa.SessionId == sessionId && pa.IsCorrect == true, cancellationToken);
        answer.Session.CorrectCount = correctCount;

        // Check if this was the last question in the session
        var isLast = answer.DisplayOrder >= answer.Session.QuestionCount;
        if (isLast)
        {
            answer.Session.Status = "finished";
            answer.Session.FinishedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        int? nextDisplayOrder = isLast ? null : answer.DisplayOrder + 1;

        return new GradedPracticeAnswerResult(
            answer.IsCorrect,
            answer.SelectedOptionId,
            correctOptionId,
            q.Explanation,
            q.Group.Transcript,
            isLast,
            nextDisplayOrder);
    }

    public async Task<PracticeSummary> GetSummaryAsync(int userId, int sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.PracticeSessions
            .Include(s => s.Section)
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SessionNotFound, $"Practice session {sessionId} was not found.");

        if (session.Status == "in_progress")
        {
            session.Status = "finished";
            session.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Category)
            .Where(pa => pa.SessionId == sessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questionIds = answers.Select(a => a.QuestionId).ToList();
        var allOptions = await db.QuestionOptions
            .AsNoTracking()
            .Where(o => questionIds.Contains(o.QuestionId))
            .ToListAsync(cancellationToken);

        var outcomes = answers.Select(a =>
        {
            var q = a.Question;
            var qOptions = allOptions.Where(o => o.QuestionId == q.QuestionId).ToList();
            var selectedOpt = a.SelectedOptionId.HasValue
                ? qOptions.FirstOrDefault(o => o.OptionId == a.SelectedOptionId.Value)
                : null;
            var correctOpt = qOptions.FirstOrDefault(o => o.IsCorrect);

            return new QuestionOutcome(
                a.DisplayOrder,
                q.QuestionId,
                q.Category?.Name,
                q.Stem,
                selectedOpt?.Label,
                selectedOpt?.Text,
                correctOpt?.Label,
                correctOpt?.Text,
                a.IsCorrect,
                a.HintUsed,
                q.Explanation);
        }).ToList();

        var total = answers.Count;
        var answered = answers.Count(a => a.SelectedOptionId.HasValue);
        var correct = answers.Count(a => a.IsCorrect == true);
        var incorrect = answered - correct;
        var skipped = total - answered;
        var accuracy = answered > 0 ? (int)Math.Round((double)correct / answered * 100) : 0;
        var hintUsedTotal = answers.Count(a => a.HintUsed);
        var correctWithHint = answers.Count(a => a.HintUsed && a.IsCorrect == true);
        var incorrectWithHint = answers.Count(a => a.HintUsed && a.IsCorrect == false);

        return new PracticeSummary(
            session.SessionId,
            session.Section.Code,
            session.Section.Name,
            session.Modality,
            session.StartedAt,
            session.FinishedAt,
            total,
            answered,
            correct,
            incorrect,
            skipped,
            accuracy,
            hintUsedTotal,
            correctWithHint,
            incorrectWithHint,
            outcomes);
    }
}
